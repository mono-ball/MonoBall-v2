using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that cycles through available combined layer shaders when F4 is pressed.
    /// </summary>
    public class ShaderCycleSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly IInputBindingService _inputBindingService;
        private readonly ShaderManagerSystem _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _combinedShaderQuery;

        // Available shader IDs to cycle through
        private readonly List<string?> _availableShaders = new()
        {
            null, // No shader (disabled)
            "base:shader:pixelation",
            "base:shader:crt",
            "base:shader:wavedistortion",
            "base:shader:kaleidoscope",
            "base:shader:grayscale",
            "base:shader:bloom",
        };

        // Start at index 0 (no shader) since no default shader is created at startup
        private int _currentShaderIndex = 0;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.ShaderCycle;

        /// <summary>
        /// Initializes a new instance of the ShaderCycleSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="inputBindingService">The input binding service for checking input.</param>
        /// <param name="shaderManagerSystem">The shader manager system for managing shaders.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderCycleSystem(
            World world,
            IInputBindingService inputBindingService,
            ShaderManagerSystem shaderManagerSystem,
            ILogger logger
        )
            : base(world)
        {
            _inputBindingService =
                inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _shaderManagerSystem =
                shaderManagerSystem ?? throw new ArgumentNullException(nameof(shaderManagerSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _combinedShaderQuery = new QueryDescription().WithAll<RenderingShaderComponent>();
        }

        /// <summary>
        /// Updates the system, checking for F4 key press to cycle shaders.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Check if CycleShader action was just pressed
            if (_inputBindingService.IsActionJustPressed(InputAction.CycleShader))
            {
                _logger.Debug("F4 key pressed - cycling shaders");

                // Cycle to next shader
                _currentShaderIndex = (_currentShaderIndex + 1) % _availableShaders.Count;
                string? nextShaderId = _availableShaders[_currentShaderIndex];

                // Find existing combined layer shader entity
                Entity? existingShaderEntity = null;
                World.Query(
                    in _combinedShaderQuery,
                    (Entity entity, ref RenderingShaderComponent shader) =>
                    {
                        if (shader.Layer == ShaderLayer.CombinedLayer)
                        {
                            existingShaderEntity = entity;
                        }
                    }
                );

                if (nextShaderId == null)
                {
                    // Disable all combined layer shaders
                    if (existingShaderEntity.HasValue)
                    {
                        // Remove animation component if it exists
                        if (
                            World.Has<ShaderParameterAnimationComponent>(existingShaderEntity.Value)
                        )
                        {
                            World.Remove<ShaderParameterAnimationComponent>(
                                existingShaderEntity.Value
                            );
                        }

                        ref var shader = ref World.Get<RenderingShaderComponent>(
                            existingShaderEntity.Value
                        );
                        shader.IsEnabled = false;
                        _shaderManagerSystem.MarkShadersDirty();
                        _logger.Information("Disabled combined layer shader");
                    }
                }
                else
                {
                    // Update or create shader entity
                    Entity shaderEntity;
                    if (existingShaderEntity.HasValue)
                    {
                        // Update existing shader
                        shaderEntity = existingShaderEntity.Value;
                        ref var shader = ref World.Get<RenderingShaderComponent>(shaderEntity);
                        shader.ShaderId = nextShaderId;
                        shader.IsEnabled = true;
                        shader.Parameters = GetDefaultParametersForShader(nextShaderId); // Update parameters

                        // Remove old animation components if they exist
                        if (World.Has<ShaderParameterAnimationComponent>(shaderEntity))
                        {
                            World.Remove<ShaderParameterAnimationComponent>(shaderEntity);
                        }

                        _shaderManagerSystem.MarkShadersDirty();
                        _logger.Information(
                            "Updated combined layer shader to {ShaderId}",
                            nextShaderId
                        );
                    }
                    else
                    {
                        // Create new shader entity
                        var shaderComponent = new RenderingShaderComponent
                        {
                            Layer = ShaderLayer.CombinedLayer,
                            ShaderId = nextShaderId,
                            IsEnabled = true,
                            RenderOrder = 0,
                            Parameters = GetDefaultParametersForShader(nextShaderId),
                        };

                        shaderEntity = World.Create(shaderComponent);
                        _shaderManagerSystem.MarkShadersDirty();
                        _logger.Information(
                            "Created combined layer shader entity with shader {ShaderId}",
                            nextShaderId
                        );
                    }

                    // Add animation component if this shader needs animation
                    var animationComponent = GetAnimationComponentForShader(nextShaderId);
                    if (animationComponent.HasValue)
                    {
                        World.Add(shaderEntity, animationComponent.Value);
                        _logger.Debug(
                            "Added animation component for shader {ShaderId}, parameter {ParameterName}",
                            nextShaderId,
                            animationComponent.Value.ParameterName
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Gets default parameters for a shader based on its ID.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>Default parameters dictionary, or null if no parameters needed.</returns>
        private Dictionary<string, object>? GetDefaultParametersForShader(string shaderId)
        {
            return shaderId switch
            {
                "base:shader:pixelation" => new Dictionary<string, object>
                {
                    { "PixelSize", 8.0f },
                },
                "base:shader:crt" => new Dictionary<string, object>
                {
                    { "Curvature", 0.1f },
                    { "ScanlineIntensity", 0.3f },
                    { "ScanlineCount", 400.0f },
                    { "ChromaticAberration", 0.003f },
                },
                "base:shader:wavedistortion" => new Dictionary<string, object>
                {
                    { "WaveSpeed", 2.0f },
                    { "WaveFrequency", 10.0f },
                    { "WaveAmplitude", 0.02f },
                    { "Time", 0.0f }, // Will be animated by ShaderParameterAnimationSystem
                },
                "base:shader:kaleidoscope" => new Dictionary<string, object>
                {
                    { "SegmentCount", 6.0f },
                },
                "base:shader:grayscale" => null, // No parameters needed
                "base:shader:bloom" => new Dictionary<string, object>
                {
                    { "BloomIntensity", 1.0f },
                    { "BloomThreshold", 0.7f },
                    { "BloomBlurAmount", 0.005f },
                },
                _ => null,
            };
        }

        /// <summary>
        /// Gets animation component for a shader if it needs animation.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>Animation component, or null if shader doesn't need animation.</returns>
        private ShaderParameterAnimationComponent? GetAnimationComponentForShader(string shaderId)
        {
            return shaderId switch
            {
                "base:shader:wavedistortion" => new ShaderParameterAnimationComponent
                {
                    ParameterName = "Time",
                    StartValue = 0.0f,
                    EndValue = 10000.0f, // Very large value for continuous time
                    Duration = 10000.0f, // Very long duration (~2.7 hours) - effectively infinite for gameplay
                    ElapsedTime = 0.0f,
                    Easing = EasingFunction.Linear, // Linear interpolation for continuous time
                    IsLooping = false, // Don't loop - Time will increase continuously
                    IsEnabled = true,
                    PingPong = false,
                },
                _ => null, // No animation needed
            };
        }
    }
}
