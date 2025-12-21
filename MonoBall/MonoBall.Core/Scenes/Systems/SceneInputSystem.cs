using System;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework.Input;
using MonoBall.Core.Scenes.Components;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System responsible for processing input for scenes in priority order.
    /// Respects BlocksInput flag to prevent lower-priority scenes from receiving input.
    /// </summary>
    public partial class SceneInputSystem : BaseSystem<World, float>
    {
        private readonly SceneManagerSystem _sceneManagerSystem;

        /// <summary>
        /// Initializes a new instance of the SceneInputSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="sceneManagerSystem">The scene manager system for accessing scene stack.</param>
        public SceneInputSystem(World world, SceneManagerSystem sceneManagerSystem)
            : base(world)
        {
            _sceneManagerSystem =
                sceneManagerSystem ?? throw new ArgumentNullException(nameof(sceneManagerSystem));
        }

        /// <summary>
        /// Processes input for scenes in priority order.
        /// </summary>
        /// <param name="keyboardState">The current keyboard state.</param>
        /// <param name="mouseState">The current mouse state.</param>
        /// <param name="gamePadState">The current gamepad state.</param>
        public void ProcessInput(
            KeyboardState keyboardState,
            MouseState mouseState,
            GamePadState gamePadState
        )
        {
            // Iterate scenes using helper method from SceneManagerSystem
            _sceneManagerSystem.IterateScenes(
                (sceneEntity, sceneComponent) =>
                {
                    // Skip inactive scenes
                    if (!sceneComponent.IsActive)
                    {
                        return true; // Continue iterating
                    }

                    // Process input for this scene
                    ProcessSceneInput(
                        sceneEntity,
                        ref sceneComponent,
                        keyboardState,
                        mouseState,
                        gamePadState
                    );

                    // If scene blocks input, stop iterating (lower scenes don't receive input)
                    return !sceneComponent.BlocksInput; // Continue if not blocking
                }
            );
        }

        /// <summary>
        /// Processes input for a single scene.
        /// </summary>
        /// <param name="sceneEntity">The scene entity.</param>
        /// <param name="scene">The scene component.</param>
        /// <param name="keyboardState">The current keyboard state.</param>
        /// <param name="mouseState">The current mouse state.</param>
        /// <param name="gamePadState">The current gamepad state.</param>
        private void ProcessSceneInput(
            Entity sceneEntity,
            ref SceneComponent scene,
            KeyboardState keyboardState,
            MouseState mouseState,
            GamePadState gamePadState
        )
        {
            // TODO: Implement scene-specific input handling
            // This is a placeholder - actual input handling will depend on scene type
            // Different scene types (GameScene, PopupScene, etc.) will handle input differently

            // For now, scenes don't process input - this method is a placeholder for future implementation
        }

        /// <summary>
        /// Update method required by BaseSystem, but input processing is done via ProcessInput().
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Input processing is done via ProcessInput() method called from Game.Update()
            // This Update() method is a no-op for input system
        }
    }
}
