using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Utilities;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for initializing the player entity.
    /// Sprite sheet switching is handled by <see cref="SpriteSheetSystem"/>.
    /// </summary>
    public class PlayerSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly ICameraService _cameraService;
        private readonly ISpriteLoaderService _spriteLoader;
        private readonly IModManager? _modManager;
        private readonly ILogger _logger;
        private readonly IConstantsService _constants;
        private readonly QueryDescription _playerQuery;
        private Entity? _playerEntity;
        private bool _playerCreated;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.Player;

        /// <summary>
        /// Initializes a new instance of the PlayerSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="cameraService">The camera service for getting camera position.</param>
        /// <param name="spriteLoader">The sprite loader service for validating sprite sheets.</param>
        /// <param name="modManager">Optional mod manager for getting default tile sizes.</param>
        /// <param name="logger">The logger for logging operations.</param>
        /// <param name="constants">The constants service for accessing game constants. Required.</param>
        public PlayerSystem(
            World world,
            ICameraService cameraService,
            ISpriteLoaderService spriteLoader,
            IModManager? modManager = null,
            ILogger? logger = null,
            IConstantsService? constants = null
        )
            : base(world)
        {
            _cameraService =
                cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            _spriteLoader = spriteLoader ?? throw new ArgumentNullException(nameof(spriteLoader));
            _modManager = modManager;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _constants = constants ?? throw new ArgumentNullException(nameof(constants));

            _playerQuery = new QueryDescription().WithAll<PlayerComponent>();
        }

        /// <summary>
        /// Initializes the player entity. Should be called after camera is created.
        /// </summary>
        /// <param name="spriteSheetId">Optional sprite sheet ID. Defaults to <see cref="GameConstants.DefaultPlayerSpriteSheetId"/> if not provided.</param>
        /// <param name="initialAnimation">Optional initial animation name. Defaults to <see cref="GameConstants.DefaultPlayerInitialAnimation"/> if not provided.</param>
        /// <param name="cameraEntity">Optional camera entity. If provided, uses this camera's position. Otherwise queries for active camera.</param>
        /// <remarks>
        /// This method initializes the player entity at the camera's position.
        /// It should be called explicitly after the camera system has created an active camera.
        /// If sprite sheet ID or animation are not provided, defaults from <see cref="GameConstants"/> are used.
        /// If cameraEntity is provided, uses that camera directly. Otherwise queries for active camera via CameraService.
        /// </remarks>
        public void InitializePlayer(
            string? spriteSheetId = null,
            string? initialAnimation = null,
            Entity? cameraEntity = null
        )
        {
            if (_playerCreated)
            {
                return;
            }

            // Use provided values or defaults from constants service
            string spriteSheet = spriteSheetId ?? _constants.GetString("PlayerSpriteSheetId");
            string animation = initialAnimation ?? _constants.GetString("PlayerInitialAnimation");

            // Get camera component - prefer provided camera entity, fall back to querying for active camera
            CameraComponent? camera = null;
            if (cameraEntity.HasValue && World.IsAlive(cameraEntity.Value))
            {
                if (World.Has<CameraComponent>(cameraEntity.Value))
                {
                    camera = World.Get<CameraComponent>(cameraEntity.Value);
                }
                else
                {
                    _logger.Warning(
                        "PlayerSystem.InitializePlayer: Provided camera entity {CameraEntityId} does not have CameraComponent, querying for active camera",
                        cameraEntity.Value.Id
                    );
                }
            }

            // If no camera from provided entity, query for active camera
            if (!camera.HasValue)
            {
                camera = _cameraService.GetActiveCamera();
            }

            if (!camera.HasValue)
            {
                _logger.Warning(
                    "PlayerSystem.InitializePlayer: No active camera found, using default position (0, 0)"
                );
                CreatePlayerEntity(Vector2.Zero, spriteSheet, animation);
                return;
            }

            var cameraComponent = camera.Value;

            // Use default spawn position instead of camera position
            // Camera starts at (0,0) but player should spawn at a reasonable map location
            // Default spawn position is defined in constants service
            int tileWidth = cameraComponent.TileWidth;
            int tileHeight = cameraComponent.TileHeight;

            Vector2 pixelPosition = new Vector2(
                _constants.Get<int>("PlayerSpawnX") * tileWidth,
                _constants.Get<int>("PlayerSpawnY") * tileHeight
            );

            CreatePlayerEntity(pixelPosition, spriteSheet, animation);
        }

        /// <summary>
        /// Updates the player system.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        /// <remarks>
        /// Player entity initialization should be done via explicit <see cref="InitializePlayer()"/> call
        /// after camera is created (typically in Game.LoadContent()).
        /// </remarks>
        public override void Update(in float deltaTime)
        {
            // No update logic needed - player movement/input handled by other systems
        }

        /// <summary>
        /// Gets the player entity reference.
        /// </summary>
        /// <returns>The player entity, or null if the player has not been created yet.</returns>
        /// <remarks>
        /// Returns null if <see cref="InitializePlayer()"/> has not been called or if the player entity
        /// has not been created. Callers should check for null before using the entity.
        /// </remarks>
        public Entity? GetPlayerEntity()
        {
            return _playerEntity;
        }

        /// <summary>
        /// Creates a player entity with all required components.
        /// </summary>
        /// <param name="position">The world position in pixels.</param>
        /// <param name="initialSpriteSheetId">The initial sprite sheet ID.</param>
        /// <param name="initialAnimation">The initial animation name.</param>
        /// <returns>The created player entity.</returns>
        /// <exception cref="ArgumentNullException">Thrown if initialSpriteSheetId or initialAnimation is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown if sprite sheet or animation is invalid.</exception>
        public Entity CreatePlayerEntity(
            Vector2 position,
            string initialSpriteSheetId,
            string initialAnimation
        )
        {
            if (string.IsNullOrEmpty(initialSpriteSheetId))
            {
                throw new ArgumentNullException(
                    nameof(initialSpriteSheetId),
                    "Initial sprite sheet ID cannot be null or empty."
                );
            }

            if (string.IsNullOrEmpty(initialAnimation))
            {
                throw new ArgumentNullException(
                    nameof(initialAnimation),
                    "Initial animation name cannot be null or empty."
                );
            }

            // Validate sprite sheet and animation exist
            // Note: Player creation uses strict validation (throws on invalid) because player is critical
            // This differs from NPC creation which uses forgiving validation (logs warning, uses default)
            SpriteValidationHelper.ValidateSpriteAndAnimation(
                _spriteLoader,
                _logger,
                initialSpriteSheetId,
                initialAnimation,
                "Player",
                "player:main",
                throwOnInvalid: true
            );

            // Convert pixel position to grid coordinates for PositionComponent
            // Get tile dimensions from loaded maps or mod defaults (supports rectangular tiles)
            int tileWidth = TileSizeHelper.GetTileWidth(World, _modManager);
            int tileHeight = TileSizeHelper.GetTileHeight(World, _modManager);
            int gridX = (int)(position.X / tileWidth);
            int gridY = (int)(position.Y / tileHeight);
            float pixelX = gridX * tileWidth;
            float pixelY = gridY * tileHeight;

            // Create player entity with all required components
            var playerEntity = World.Create(
                new PlayerComponent { PlayerId = "player:main", Name = "May" },
                new SpriteSheetComponent { CurrentSpriteSheetId = initialSpriteSheetId },
                new SpriteAnimationComponent
                {
                    CurrentAnimationName = initialAnimation,
                    CurrentFrameIndex = 0,
                    ElapsedTime = 0.0f,
                    FlipHorizontal = false,
                    IsPlaying = true,
                    IsComplete = false,
                    PlayOnce = false,
                    TriggeredEventFrames = 0,
                },
                new PositionComponent
                {
                    X = gridX,
                    Y = gridY,
                    PixelX = pixelX,
                    PixelY = pixelY,
                },
                new GridMovement(_constants.Get<float>("PlayerMovementSpeed"))
                {
                    FacingDirection = Direction.South,
                    MovementDirection = Direction.South,
                    RunningState = RunningState.NotMoving,
                },
                new InputState
                {
                    InputEnabled = true,
                    PressedDirection = Direction.None,
                    ActionPressed = false,
                    InputBufferTime = 0f,
                    PressedActions = new HashSet<InputAction>(),
                    JustPressedActions = new HashSet<InputAction>(),
                    JustReleasedActions = new HashSet<InputAction>(),
                },
                new DirectionComponent { Value = Direction.South },
                new RenderableComponent
                {
                    IsVisible = true,
                    RenderOrder = 100, // Render above NPCs
                    Opacity = 1.0f,
                }
            );

            _playerEntity = playerEntity;
            _playerCreated = true;

            _logger.Information(
                "PlayerSystem.CreatePlayerEntity: Created player entity {EntityId} at grid position ({GridX}, {GridY}), pixel position ({PixelX}, {PixelY}) with sprite sheet {SpriteSheetId} and animation {AnimationName}",
                playerEntity.Id,
                gridX,
                gridY,
                pixelX,
                pixelY,
                initialSpriteSheetId,
                initialAnimation
            );

            return playerEntity;
        }
    }
}
