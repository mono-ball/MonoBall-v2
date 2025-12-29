using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Scripting.Utilities;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that handles player interactions with entities that have InteractionComponent (NPCs, signs, etc.).
///     Detects when player presses interact button near an interaction entity and triggers the interaction script.
///     Automatically pauses behavior scripts during interactions and resumes them when message boxes close.
/// </summary>
public class InteractionSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    // Track active interactions to prevent duplicate events
    private readonly HashSet<Entity> _activeInteractions = new();
    private readonly IConstantsService _constants;
    private readonly IInputBindingService _inputBindingService;
    private readonly QueryDescription _interactionQuery;
    private readonly ILogger _logger;
    private readonly QueryDescription _playerQuery;
    private readonly QueryDescription _playerStateQuery;
    private readonly DefinitionRegistry _registry;
    private bool _disposed;

    /// <summary>
    ///     Initializes a new instance of the InteractionSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="inputBindingService">The input binding service for checking interact button presses.</param>
    /// <param name="registry">The definition registry for looking up BehaviorDefinitions.</param>
    /// <param name="constants">The constants service for accessing player elevation.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public InteractionSystem(
        World world,
        IInputBindingService inputBindingService,
        DefinitionRegistry registry,
        IConstantsService constants,
        ILogger logger
    )
        : base(world)
    {
        _inputBindingService =
            inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Unified query: both map interactions and NPCs use InteractionComponent
        // Filter by ActiveMapEntity to only process interactions in active maps
        _interactionQuery = new QueryDescription().WithAll<
            InteractionComponent,
            PositionComponent,
            ActiveMapEntity
        >();

        _playerQuery = new QueryDescription().WithAll<
            PlayerComponent,
            PositionComponent,
            GridMovement
        >();

        // Query for player with interaction state
        _playerStateQuery = new QueryDescription().WithAll<
            PlayerComponent,
            InteractionStateComponent
        >();

        // Subscribe to events (using RefAction for ref parameter support)
        EventBus.Subscribe<InteractionEndedEvent>(OnInteractionEnded);
        EventBus.Subscribe<MessageBoxClosedEvent>(OnMessageBoxClosed);
    }

    /// <summary>
    ///     Disposes the system and unsubscribes from events.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.Interaction;

    /// <summary>
    ///     Updates the system, checking for player interactions with nearby entities.
    /// </summary>
    /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
    public override void Update(in float deltaTime)
    {
        // Check if interact button was just pressed (edge-triggered, not level-triggered)
        var interactJustPressed = _inputBindingService.IsActionJustPressed(InputAction.Interact);
        if (!interactJustPressed)
            return; // Player didn't press interact button this frame

        // Get player entity and tile position
        Entity? playerEntity = null;
        var playerTileX = 0;
        var playerTileY = 0;
        var playerFacing = Direction.South;
        var playerElevation = 0;

        World.Query(
            in _playerQuery,
            (
                Entity entity,
                ref PlayerComponent player,
                ref PositionComponent pos,
                ref GridMovement movement
            ) =>
            {
                playerEntity = entity;
                // Use tile coordinates directly from PositionComponent
                playerTileX = pos.X;
                playerTileY = pos.Y;
                playerFacing = movement.FacingDirection;
                // Get player elevation from constants (fail-fast if not found)
                playerElevation = _constants.Get<int>("PlayerElevation");
            }
        );

        if (playerEntity == null)
            return; // No player, nothing to do

        // Check if player is already in an interaction
        if (World.Has<InteractionStateComponent>(playerEntity.Value))
            return; // Player is already interacting with something

        // Check for interactions (unified: both map interactions and NPCs use InteractionComponent)
        World.Query(
            in _interactionQuery,
            (Entity entity, ref InteractionComponent interaction, ref PositionComponent pos) =>
            {
                if (!interaction.IsEnabled)
                    return; // Interaction disabled

                // Check if this interaction is already active
                var interactionEntity = entity;
                if (_activeInteractions.Contains(interactionEntity))
                    return; // Already interacting with this

                // Check elevation match (fail-fast validation)
                if (interaction.Elevation != playerElevation)
                {
                    _logger.Debug(
                        "Interaction blocked: elevation mismatch (player={PlayerElevation}, interaction={InteractionElevation})",
                        playerElevation,
                        interaction.Elevation
                    );
                    return; // Different elevation
                }

                // Check interaction distance using tile-based Manhattan distance
                // In Pokemon Emerald, interactions work when player is on an adjacent tile (1 tile away)
                // Player must be exactly 1 tile away (Manhattan distance == 1), not on the same tile
                var interactionTileX = pos.X;
                var interactionTileY = pos.Y;

                // Calculate Manhattan distance from player to interaction entity
                var tileDistanceX = Math.Abs(playerTileX - interactionTileX);
                var tileDistanceY = Math.Abs(playerTileY - interactionTileY);
                var tileDistance = tileDistanceX + tileDistanceY; // Manhattan distance

                // Interaction range: 1 tile (player must be adjacent in one of 4 cardinal directions)
                // Manhattan distance == 1 means: adjacent orthogonally (not diagonal)
                // Distance == 0 means player is on the same tile (not allowed)
                const int interactionRangeTiles = 1;

                if (tileDistance != interactionRangeTiles)
                    return; // Player must be exactly 1 tile away (adjacent), not on same tile or further

                // Calculate direction from player to interaction entity
                // Player must be facing toward the entity to interact
                var directionToEntity = DirectionHelper.GetDirectionTo(
                    playerTileX,
                    playerTileY,
                    interactionTileX,
                    interactionTileY
                );

                // Check if player is facing toward the entity
                if (playerFacing != directionToEntity)
                {
                    _logger.Debug(
                        "Interaction blocked: player not facing entity (player facing={PlayerFacing}, required={RequiredFacing})",
                        playerFacing,
                        directionToEntity
                    );
                    return; // Player must be facing toward the entity
                }

                // Check additional facing requirement if specified (for special cases)
                if (interaction.RequiredFacing.HasValue)
                {
                    var requiredDirection = interaction.RequiredFacing.Value;
                    if (playerFacing != requiredDirection)
                    {
                        _logger.Debug(
                            "Interaction blocked: required facing direction not met (player facing={PlayerFacing}, required={RequiredFacing})",
                            playerFacing,
                            requiredDirection
                        );
                        return; // Wrong facing direction
                    }
                }

                // Trigger interaction event (works for both map interactions and NPCs)
                TriggerInteraction(
                    entity,
                    playerEntity.Value,
                    interaction.InteractionId,
                    interaction.ScriptDefinitionId
                );
            }
        );
    }

    /// <summary>
    ///     Triggers an interaction event and handles behavior script pausing.
    /// </summary>
    /// <param name="interactionEntity">The entity being interacted with.</param>
    /// <param name="playerEntity">The player entity.</param>
    /// <param name="interactionId">The interaction ID.</param>
    /// <param name="scriptDefinitionId">The script definition ID for the interaction script.</param>
    private void TriggerInteraction(
        Entity interactionEntity,
        Entity playerEntity,
        string interactionId,
        string? scriptDefinitionId
    )
    {
        // Validate inputs (fail-fast)
        if (string.IsNullOrEmpty(interactionId))
            throw new ArgumentException(
                "InteractionId cannot be null or empty.",
                nameof(interactionId)
            );

        if (!World.IsAlive(interactionEntity))
            throw new InvalidOperationException("Interaction entity is not alive.");

        if (!World.IsAlive(playerEntity))
            throw new InvalidOperationException("Player entity is not alive.");

        // Mark as active to prevent duplicate events
        _activeInteractions.Add(interactionEntity);

        // Pause behavior script if this is an NPC interaction
        if (World.Has<NpcComponent>(interactionEntity))
        {
            var npcComponent = World.Get<NpcComponent>(interactionEntity);

            // Get behavior script ID from NPC's behavior definition
            if (!string.IsNullOrEmpty(npcComponent.BehaviorId))
            {
                var behaviorDef = _registry.GetById<BehaviorDefinition>(npcComponent.BehaviorId);
                if (behaviorDef != null && !string.IsNullOrEmpty(behaviorDef.ScriptId))
                {
                    PauseScript(interactionEntity, behaviorDef.ScriptId);
                    _logger.Debug(
                        "Paused behavior script {ScriptId} for NPC {NpcId} during interaction",
                        behaviorDef.ScriptId,
                        npcComponent.NpcId
                    );
                }
            }

            // Add InteractionStateComponent to track this interaction
            World.Add(
                playerEntity,
                new InteractionStateComponent
                {
                    BehaviorId = npcComponent.BehaviorId ?? string.Empty,
                    InteractionEntity = interactionEntity,
                    PlayerEntity = playerEntity,
                }
            );
        }

        // Fire InteractionTriggeredEvent
        var interactionEvent = new InteractionTriggeredEvent
        {
            InteractionEntity = interactionEntity,
            PlayerEntity = playerEntity,
            InteractionId = interactionId,
            ScriptDefinitionId = scriptDefinitionId,
        };

        _logger.Debug(
            "Firing InteractionTriggeredEvent: InteractionEntity={InteractionEntityId}, ScriptDefinitionId={ScriptDefinitionId}",
            interactionEntity.Id,
            scriptDefinitionId
        );

        EventBus.Send(ref interactionEvent);

        // Fire InteractionStartedEvent
        var startedEvent = new InteractionStartedEvent
        {
            InteractionEntity = interactionEntity,
            PlayerEntity = playerEntity,
            InteractionId = interactionId,
        };
        EventBus.Send(ref startedEvent);

        _logger.Debug("Interaction triggered: {InteractionId} by player", interactionId);
    }

    /// <summary>
    ///     Handles interaction end event.
    /// </summary>
    /// <param name="evt">The interaction ended event.</param>
    private void OnInteractionEnded(ref InteractionEndedEvent evt)
    {
        _activeInteractions.Remove(evt.InteractionEntity);
    }

    /// <summary>
    ///     Handles message box closed event - resumes behavior scripts.
    /// </summary>
    /// <param name="evt">The message box closed event.</param>
    private void OnMessageBoxClosed(ref MessageBoxClosedEvent evt)
    {
        // Find player entity with InteractionStateComponent
        World.Query(
            in _playerStateQuery,
            (Entity e, ref PlayerComponent player, ref InteractionStateComponent state) =>
            {
                // Resume behavior script
                if (!string.IsNullOrEmpty(state.BehaviorId))
                {
                    var behaviorDef = _registry.GetById<BehaviorDefinition>(state.BehaviorId);
                    if (behaviorDef != null && !string.IsNullOrEmpty(behaviorDef.ScriptId))
                    {
                        ResumeScript(state.InteractionEntity, behaviorDef.ScriptId);
                        _logger.Debug(
                            "Resumed behavior script {ScriptId} for NPC after interaction",
                            behaviorDef.ScriptId
                        );
                    }
                }

                // Fire InteractionEndedEvent
                var endedEvent = new InteractionEndedEvent
                {
                    InteractionEntity = state.InteractionEntity,
                    PlayerEntity = state.PlayerEntity,
                    InteractionId = string.Empty, // Can be retrieved from entity if needed
                };
                EventBus.Send(ref endedEvent);

                // Remove state component
                World.Remove<InteractionStateComponent>(e);

                // Remove from active interactions
                _activeInteractions.Remove(state.InteractionEntity);
            }
        );
    }

    /// <summary>
    ///     Pauses a script by setting its ScriptAttachmentComponent.IsActive to false.
    ///     Uses cached QueryDescription for performance.
    /// </summary>
    /// <param name="entity">The entity that has the script.</param>
    /// <param name="scriptDefinitionId">The script definition ID to pause.</param>
    /// <returns>True if the script was found and paused, false otherwise.</returns>
    private bool PauseScript(Entity entity, string scriptDefinitionId)
    {
        if (!World.Has<ScriptAttachmentComponent>(entity))
            return false;

        // Get the ScriptAttachmentComponent and update the specific script in the collection
        ref var component = ref World.Get<ScriptAttachmentComponent>(entity);
        if (component.Scripts == null || !component.Scripts.ContainsKey(scriptDefinitionId))
            return false;

        var attachment = component.Scripts[scriptDefinitionId];
        attachment.IsActive = false;
        component.Scripts[scriptDefinitionId] = attachment;
        World.Set(entity, component);
        return true;
    }

    /// <summary>
    ///     Resumes a script by setting its ScriptAttachmentComponent.IsActive to true.
    ///     Uses cached QueryDescription for performance.
    /// </summary>
    /// <param name="entity">The entity that has the script.</param>
    /// <param name="scriptDefinitionId">The script definition ID to resume.</param>
    /// <returns>True if the script was found and resumed, false otherwise.</returns>
    private bool ResumeScript(Entity entity, string scriptDefinitionId)
    {
        if (!World.Has<ScriptAttachmentComponent>(entity))
            return false;

        // Get the ScriptAttachmentComponent and update the specific script in the collection
        ref var component = ref World.Get<ScriptAttachmentComponent>(entity);
        if (component.Scripts == null || !component.Scripts.ContainsKey(scriptDefinitionId))
            return false;

        var attachment = component.Scripts[scriptDefinitionId];
        attachment.IsActive = true;
        component.Scripts[scriptDefinitionId] = attachment;
        World.Set(entity, component);
        return true;
    }

    /// <summary>
    ///     Protected dispose method following standard dispose pattern.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            EventBus.Unsubscribe<InteractionEndedEvent>(OnInteractionEnded);
            EventBus.Unsubscribe<MessageBoxClosedEvent>(OnMessageBoxClosed);
        }

        _disposed = true;
    }
}
