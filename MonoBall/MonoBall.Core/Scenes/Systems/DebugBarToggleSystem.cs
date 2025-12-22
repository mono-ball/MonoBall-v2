using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.Scenes;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that handles toggling the debug bar scene visibility via F3 key press.
    /// </summary>
    public class DebugBarToggleSystem : BaseSystem<World, float>
    {
        private const string DebugBarSceneId = "debug:bar";

        private readonly SceneManagerSystem _sceneManagerSystem;
        private readonly IInputBindingService _inputBindingService;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the DebugBarToggleSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="sceneManagerSystem">The scene manager system for creating/toggling scenes.</param>
        /// <param name="inputBindingService">The input binding service for checking input.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public DebugBarToggleSystem(
            World world,
            SceneManagerSystem sceneManagerSystem,
            IInputBindingService inputBindingService,
            ILogger logger
        )
            : base(world)
        {
            _sceneManagerSystem =
                sceneManagerSystem ?? throw new ArgumentNullException(nameof(sceneManagerSystem));
            _inputBindingService =
                inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Updates the system, checking for F3 key press to toggle debug bar.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        /// <remarks>
        /// Note: InputBindingService.Update() is called by InputSystem, so we don't call it here.
        /// This system should run after InputSystem in the update order.
        /// </remarks>
        public override void Update(in float deltaTime)
        {
            // Check if ToggleDebugBar action was just pressed
            // Note: InputBindingService.Update() is already called by InputSystem
            if (_inputBindingService.IsActionJustPressed(InputAction.ToggleDebugBar))
            {
                _logger.Debug("F3 key pressed - toggling debug bar");

                // Check if scene exists
                var sceneEntity = _sceneManagerSystem.GetSceneEntity(DebugBarSceneId);

                if (sceneEntity == null)
                {
                    // Create the debug bar scene
                    var sceneComponent = CreateDebugBarSceneComponent();
                    var debugBarComponent = new DebugBarSceneComponent();

                    _sceneManagerSystem.CreateScene(sceneComponent, debugBarComponent);

                    _logger.Information("Debug bar scene created and activated");
                }
                else
                {
                    // Toggle active state
                    // Check if entity is alive before accessing
                    if (!World.IsAlive(sceneEntity.Value))
                    {
                        _logger.Warning("Debug bar scene entity is not alive, recreating scene");
                        // Scene entity was destroyed, recreate it
                        var sceneComponent = CreateDebugBarSceneComponent();
                        var debugBarComponent = new DebugBarSceneComponent();
                        _sceneManagerSystem.CreateScene(sceneComponent, debugBarComponent);
                        _logger.Information("Debug bar scene recreated");
                    }
                    else
                    {
                        // Get current active state, then toggle
                        ref var sceneComponent = ref World.Get<SceneComponent>(sceneEntity.Value);
                        bool currentActive = sceneComponent.IsActive;
                        bool newActiveState = !currentActive;

                        // Use SetSceneActive which safely handles the modification
                        _sceneManagerSystem.SetSceneActive(DebugBarSceneId, newActiveState);

                        _logger.Debug(
                            "Debug bar scene toggled from {OldState} to {NewState}",
                            currentActive ? "active" : "inactive",
                            newActiveState ? "active" : "inactive"
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Creates a SceneComponent configured for the debug bar scene.
        /// </summary>
        /// <returns>A SceneComponent configured for the debug bar.</returns>
        private SceneComponent CreateDebugBarSceneComponent()
        {
            return new SceneComponent
            {
                SceneId = DebugBarSceneId,
                Priority = ScenePriorities.DebugOverlay, // Higher priority than game scene to render last (on top)
                CameraMode = SceneCameraMode.ScreenCamera,
                BlocksUpdate = false, // Don't block game updates
                BlocksDraw = false, // Don't block game rendering
                BlocksInput = false, // Don't block input
                IsActive = true,
                IsPaused = false,
            };
        }
    }
}
