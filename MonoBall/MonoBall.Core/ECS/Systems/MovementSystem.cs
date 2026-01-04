using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that handles grid-based movement with smooth interpolation.
///     Implements Pokemon-style tile-by-tile movement and updates animations based on movement state.
///     Processes MovementRequest components, updates movement interpolation, and manages animation state.
/// </summary>
/// <remarks>
///     <para>
///         <b>SRP Violation - Intentional:</b>
///         This system handles both movement logic and animation state changes. While this violates
///         Single Responsibility Principle, it is intentional and necessary to prevent timing bugs.
///     </para>
///     <para>
///         <b>Why Animation Logic is Here:</b>
///         Animation state changes must happen atomically with movement state changes. For example:
///         - When movement completes, we must check for next movement BEFORE switching to idle animation
///         - Turn-in-place must check animation completion to transition states correctly
///         - Walk animation must start immediately when movement begins
///     </para>
///     <para>
///         If animation logic were separated into SpriteAnimationSystem (which runs after this system),
///         there would be a frame delay where movement state and animation state are out of sync,
///         causing visual bugs like animation flickering or incorrect idle animations.
///     </para>
///     <para>
///         Animation logic is organized in MovementAnimationHelper for code clarity, but must be called
///         from this system's Update method to maintain atomicity.
///     </para>
/// </remarks>
public class MovementSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly IActiveMapFilterService _activeMapFilterService;
    private readonly ICollisionService _collisionService;
    private readonly IConstantsService _constants;
    private readonly ILogger _logger;
    private readonly IModManager? _modManager;
    private readonly QueryDescription _movementQueryWithActiveMap;
    private readonly QueryDescription _movementRequestQuery;

    /// <summary>
    ///     Initializes a new instance of the MovementSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="collisionService">The collision service for movement validation.</param>
    /// <param name="activeMapFilterService">The active map filter service for filtering entities by active maps.</param>
    /// <param name="constants">The constants service for accessing game constants. Required.</param>
    /// <param name="modManager">Optional mod manager for getting default tile sizes.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public MovementSystem(
        World world,
        ICollisionService collisionService,
        IActiveMapFilterService activeMapFilterService,
        IConstantsService constants,
        IModManager? modManager = null,
        ILogger? logger = null
    )
        : base(world)
    {
        _collisionService =
            collisionService ?? throw new ArgumentNullException(nameof(collisionService));
        _activeMapFilterService =
            activeMapFilterService
            ?? throw new ArgumentNullException(nameof(activeMapFilterService));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _modManager = modManager;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Query for entities with movement requests to process (only in active maps)
        _movementRequestQuery = new QueryDescription().WithAll<
            PositionComponent,
            GridMovement,
            MovementRequest,
            ActiveMapEntity
        >();

        // Query for entities with movement (position + grid movement) - only in active maps
        // This query uses ActiveMapEntity tag to filter at query level, avoiding iteration over inactive entities
        _movementQueryWithActiveMap = new QueryDescription().WithAll<
            PositionComponent,
            GridMovement,
            ActiveMapEntity
        >();
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.Movement;

    /// <summary>
    ///     Updates the movement system, processing movement requests and updating movement interpolation.
    ///     Only processes entities in currently loaded maps (current map + connected maps) for performance.
    ///     Uses ActiveMapEntity tag component for query-level filtering to avoid iterating over inactive entities.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
    public override void Update(in float deltaTime)
    {
        // Process movement requests first (before updating existing movements)
        // Query includes ActiveMapEntity tag, so only entities in active maps are iterated
        ProcessMovementRequests(deltaTime);

        // Update existing movements (handles both with and without animation)
        // Query includes ActiveMapEntity tag, so only entities in active maps are iterated
        UpdateMovements(deltaTime);
    }

    /// <summary>
    ///     Processes MovementRequest components, validates movement, and starts movement if valid.
    ///     Query includes ActiveMapEntity tag, so only entities in active maps are processed.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    private void ProcessMovementRequests(float deltaTime)
    {
        World.Query(
            in _movementRequestQuery,
            (
                Entity entity,
                ref PositionComponent position,
                ref GridMovement movement,
                ref MovementRequest request
            ) =>
            {
                // CRITICAL: Check if entity is still alive before processing
                // Entity might be destroyed or modified during query iteration (race condition)
                if (!World.IsAlive(entity))
                    return; // Entity was destroyed, skip

                if (!request.Active || movement.IsMoving || movement.MovementLocked)
                    return;

                // Check if entity is turning in place - wait for turn to complete
                if (movement.RunningState == RunningState.TurnDirection)
                    return;

                // Mark request as processed
                request.Active = false;

                // Calculate target position
                var (deltaX, deltaY) = request.Direction.ToTileDelta();
                var targetX = position.X + deltaX;
                var targetY = position.Y + deltaY;

                // Store old position before updating (for events)
                var oldX = position.X;
                var oldY = position.Y;

                // Get map ID (from NpcComponent or MapComponent)
                var mapId = _activeMapFilterService.GetEntityMapId(entity);

                // Validate movement (collision checking)
                // Pass fromDirection for directional collision checking (e.g., one-way tiles)
                if (
                    !_collisionService.CanMoveTo(entity, targetX, targetY, mapId, request.Direction)
                )
                {
                    // Movement blocked - publish event
                    var blockedEvent = new MovementBlockedEvent
                    {
                        Entity = entity,
                        BlockReason = "Collision",
                        TargetPosition = (targetX, targetY),
                        Direction = request.Direction,
                        MapId = mapId,
                    };
                    EventBus.Send(ref blockedEvent);
                    _logger.Debug(
                        "Movement blocked for entity {EntityId} to ({TargetX}, {TargetY}) in direction {Direction}: {Reason}",
                        entity.Id,
                        targetX,
                        targetY,
                        request.Direction,
                        blockedEvent.BlockReason
                    );
                    return;
                }

                // Calculate movement positions
                // Get tile dimensions from loaded maps or constants service (supports rectangular tiles)
                var tileWidth = TileSizeHelper.GetTileWidth(World, _constants);
                var tileHeight = TileSizeHelper.GetTileHeight(World, _constants);
                var startPosition = new Vector2(position.PixelX, position.PixelY);
                var targetPosition = new Vector2(targetX * tileWidth, targetY * tileHeight);

                // Publish movement started event BEFORE starting movement (allows cancellation)
                // NOTE: Event handlers can set IsCancelled=true to prevent movement
                var startedEvent = new MovementStartedEvent
                {
                    Entity = entity,
                    StartPosition = startPosition,
                    TargetPosition = targetPosition,
                    Direction = request.Direction,
                    IsCancelled = false,
                };
                EventBus.Send(ref startedEvent);

                // Check if movement was cancelled by event handler
                if (startedEvent.IsCancelled)
                {
                    _logger.Debug(
                        "Movement cancelled for entity {EntityId}: {Reason}",
                        entity.Id,
                        startedEvent.CancellationReason ?? "Unknown reason"
                    );
                    return;
                }

                // Movement approved - start movement interpolation and update grid position
                movement.StartMovement(startPosition, targetPosition, request.Direction);

                // Update grid position immediately (for collision/lookup)
                position.X = targetX;
                position.Y = targetY;

                _logger.Debug(
                    "Movement started for entity {EntityId} from ({OldX}, {OldY}) to ({TargetX}, {TargetY}) in direction {Direction}",
                    entity.Id,
                    oldX,
                    oldY,
                    targetX,
                    targetY,
                    request.Direction
                );
            }
        );
    }

    /// <summary>
    ///     Updates movement interpolation for entities currently moving.
    ///     Handles animation state directly (matching oldmonoball architecture).
    ///     Query includes ActiveMapEntity tag, so only entities in active maps are processed.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update.</param>
    private void UpdateMovements(float deltaTime)
    {
        // Use query with ActiveMapEntity tag to filter at query level
        // This avoids iterating over entities in unloaded maps
        World.Query(
            in _movementQueryWithActiveMap,
            (Entity entity, ref PositionComponent position, ref GridMovement movement) =>
            {
                // CRITICAL: Check if entity is still alive before accessing components
                // Entity might be destroyed or modified during query iteration (race condition)
                if (!World.IsAlive(entity))
                    return; // Entity was destroyed, skip

                // Performance optimization: Skip stationary NPCs that aren't moving and have no movement request
                // Only process entities that are:
                // 1. Moving (IsMoving = true)
                // 2. Have a movement request (will start moving soon)
                // 3. Are the player (always process player for input responsiveness)
                // 4. Are turning in place (RunningState = TurnDirection)

                // Defensive checks: Verify entity still has required components before accessing
                // This prevents NullReferenceException if entity is being modified concurrently
                if (!World.Has<PositionComponent>(entity) || !World.Has<GridMovement>(entity))
                    return; // Entity lost required components, skip

                var isPlayer = World.TryGet<PlayerComponent>(entity, out _);
                var hasMovementRequest = World.TryGet<MovementRequest>(entity, out _);
                var isMoving = movement.IsMoving;
                var isTurning = movement.RunningState == RunningState.TurnDirection;

                // Skip if none of the above conditions are true (stationary NPC doing nothing)
                if (!isPlayer && !isMoving && !hasMovementRequest && !isTurning)
                    return;

                // Check for optional SpriteAnimationComponent
                // Defensive check: Verify entity is still alive before accessing optional component
                if (
                    World.IsAlive(entity)
                    && World.TryGet<SpriteAnimationComponent>(entity, out var animation)
                )
                {
                    ProcessMovementWithAnimation(
                        entity,
                        ref position,
                        ref movement,
                        ref animation,
                        deltaTime
                    );

                    // CRITICAL: Write modified animation back to entity
                    // TryGet returns a COPY of the struct, so changes must be written back
                    // Final check: Ensure entity is still alive before writing back
                    if (World.IsAlive(entity) && World.Has<SpriteAnimationComponent>(entity))
                        World.Set(entity, animation);
                }
                else if (World.IsAlive(entity))
                {
                    ProcessMovementNoAnimation(entity, ref position, ref movement, deltaTime);
                }
            }
        );
    }

    /// <summary>
    ///     Processes movement for entities with animation components.
    ///     Handles all animation state transitions atomically with movement state.
    /// </summary>
    /// <remarks>
    ///     Animation state changes are handled atomically with movement state changes to prevent
    ///     timing bugs. See MovementAnimationHelper for details on why this coupling is necessary.
    /// </remarks>
    private void ProcessMovementWithAnimation(
        Entity entity,
        ref PositionComponent position,
        ref GridMovement movement,
        ref SpriteAnimationComponent animation,
        float deltaTime
    )
    {
        if (movement.IsMoving)
        {
            UpdateMovementProgress(ref movement, deltaTime);

            if (movement.MovementProgress >= 1.0f)
            {
                // Movement complete - handle animation-specific logic before completing
                // CRITICAL: Check for next movement BEFORE completing movement to prevent
                // animation reset between consecutive tile movements
                var hasNextMovement = World.Has<MovementRequest>(entity);
                MovementAnimationHelper.OnMovementComplete(
                    ref animation,
                    ref movement,
                    hasNextMovement
                );

                CompleteMovement(entity, ref position, ref movement);
            }
            else
            {
                InterpolatePosition(ref position, ref movement);
                MovementAnimationHelper.OnMovementInProgress(ref animation, ref movement);
            }
        }
        else
        {
            SyncPositionToGrid(ref position);

            // Handle turn-in-place state (Pokemon Emerald behavior)
            if (movement.RunningState == RunningState.TurnDirection)
            {
                var turnComplete = MovementAnimationHelper.OnTurnInPlace(
                    ref animation,
                    ref movement
                );

                if (turnComplete)
                    // Turn complete - allow movement on next input
                    movement.RunningState = RunningState.NotMoving;
            }
            else
            {
                MovementAnimationHelper.OnIdle(ref animation, ref movement);
            }
        }
    }

    /// <summary>
    ///     Processes movement for entities without animation components.
    /// </summary>
    private void ProcessMovementNoAnimation(
        Entity entity,
        ref PositionComponent position,
        ref GridMovement movement,
        float deltaTime
    )
    {
        if (movement.IsMoving)
        {
            UpdateMovementProgress(ref movement, deltaTime);

            if (movement.MovementProgress >= 1.0f)
                CompleteMovement(entity, ref position, ref movement);
            else
                InterpolatePosition(ref position, ref movement);
        }
        else
        {
            SyncPositionToGrid(ref position);

            // For entities without animation, turn-in-place completes immediately
            if (movement.RunningState == RunningState.TurnDirection)
                movement.RunningState = RunningState.NotMoving;
        }
    }

    /// <summary>
    ///     Updates movement progress based on movement speed and delta time.
    /// </summary>
    /// <param name="movement">The movement component to update.</param>
    /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
    private void UpdateMovementProgress(ref GridMovement movement, float deltaTime)
    {
        movement.MovementProgress += movement.MovementSpeed * deltaTime;
    }

    /// <summary>
    ///     Completes movement by snapping to target position, calculating old position,
    ///     completing movement state, and publishing movement completed event.
    /// </summary>
    /// <param name="entity">The entity that completed movement.</param>
    /// <param name="position">The position component to update.</param>
    /// <param name="movement">The movement component to complete.</param>
    private void CompleteMovement(
        Entity entity,
        ref PositionComponent position,
        ref GridMovement movement
    )
    {
        // Snap to target position
        position.PixelX = movement.TargetPosition.X;
        position.PixelY = movement.TargetPosition.Y;
        SyncPositionToGrid(ref position);

        // Calculate old position (before movement started)
        var (deltaX, deltaY) = movement.MovementDirection.ToTileDelta();
        var oldX = position.X - deltaX;
        var oldY = position.Y - deltaY;

        // Complete movement state
        movement.CompleteMovement();

        // Get map ID (from NpcComponent or MapComponent)
        var mapId = _activeMapFilterService.GetEntityMapId(entity);

        // Publish movement completed event
        var completedEvent = new MovementCompletedEvent
        {
            Entity = entity,
            OldPosition = (oldX, oldY),
            NewPosition = (position.X, position.Y),
            Direction = movement.MovementDirection,
            MapId = mapId,
            MovementTime = 1.0f / movement.MovementSpeed,
        };
        EventBus.Send(ref completedEvent);

        _logger.Debug(
            "Movement completed for entity {EntityId} from ({OldX}, {OldY}) to ({NewX}, {NewY}) in direction {Direction}",
            entity.Id,
            oldX,
            oldY,
            position.X,
            position.Y,
            movement.MovementDirection
        );
    }

    /// <summary>
    ///     Interpolates pixel position between start and target based on movement progress.
    /// </summary>
    /// <param name="position">The position component to update.</param>
    /// <param name="movement">The movement component containing progress and positions.</param>
    private void InterpolatePosition(ref PositionComponent position, ref GridMovement movement)
    {
        var progress = MathHelper.Clamp(movement.MovementProgress, 0f, 1f);
        position.PixelX = MathHelper.Lerp(
            movement.StartPosition.X,
            movement.TargetPosition.X,
            progress
        );
        position.PixelY = MathHelper.Lerp(
            movement.StartPosition.Y,
            movement.TargetPosition.Y,
            progress
        );
    }

    /// <summary>
    ///     Syncs pixel position to grid coordinates.
    /// </summary>
    /// <param name="position">The position component to sync.</param>
    private void SyncPositionToGrid(ref PositionComponent position)
    {
        // Get tile dimensions from loaded maps or mod defaults (supports rectangular tiles)
        // World should be set by BaseSystem constructor, but add defensive check
        if (World == null)
            throw new InvalidOperationException(
                "World is null in MovementSystem. Ensure the system is properly initialized."
            );

        var tileWidth = TileSizeHelper.GetTileWidth(World, _constants);
        var tileHeight = TileSizeHelper.GetTileHeight(World, _constants);
        position.SyncPixelsToGrid(tileWidth, tileHeight);
    }
}
