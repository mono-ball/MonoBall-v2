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
    /// System that cycles through available shaders when F4 (layer shaders) or F5 (player shaders) is pressed.
    /// Consolidates shader cycling logic for both layer-wide and per-entity shaders.
    /// </summary>
    public class ShaderCycleSystem : BaseSystem<World, float>, IPrioritizedSystem
    {
        private readonly IInputBindingService _inputBindingService;
        private readonly PlayerSystem? _playerSystem;
        private readonly ShaderManagerSystem _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _combinedShaderQuery;

        // Available combined layer shader IDs to cycle through (F4)
        private readonly List<string?> _layerShaders = new()
        {
            null, // No shader (disabled)
            "base:shader:pixelation",
            "base:shader:crt",
            "base:shader:wavedistortion",
            "base:shader:kaleidoscope",
            "base:shader:grayscale",
            "base:shader:bloom",
        };

        // Available per-entity shader IDs to cycle through (F5)
        private readonly List<string?> _entityShaders = new()
        {
            null, // No shader (disabled)
            "base:shader:glow",
            "base:shader:outline",
            "base:shader:rainbow",
            "base:shader:pulsingglow",
            "base:shader:invert",
        };

        // Current indices for cycling
        private int _currentLayerShaderIndex = 0;
        private int _currentEntityShaderIndex = 0;

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
        /// <param name="playerSystem">The player system for getting the player entity (optional, needed for F5).</param>
        /// <param name="logger">The logger for logging operations.</param>
        public ShaderCycleSystem(
            World world,
            IInputBindingService inputBindingService,
            ShaderManagerSystem shaderManagerSystem,
            PlayerSystem? playerSystem = null,
            ILogger? logger = null
        )
            : base(world)
        {
            _inputBindingService =
                inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _shaderManagerSystem =
                shaderManagerSystem ?? throw new ArgumentNullException(nameof(shaderManagerSystem));
            _playerSystem = playerSystem;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _combinedShaderQuery = new QueryDescription().WithAll<RenderingShaderComponent>();
        }

        /// <summary>
        /// Updates the system, checking for F4 (layer shaders) or F5 (player shaders) key press to cycle shaders.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Check if CycleShader action was just pressed (F4 - layer shaders)
            if (_inputBindingService.IsActionJustPressed(InputAction.CycleShader))
            {
                _logger.Debug("F4 key pressed - cycling layer shaders");
                CycleLayerShader();
            }

            // Check if CyclePlayerShader action was just pressed (F5 - player shaders)
            if (_inputBindingService.IsActionJustPressed(InputAction.CyclePlayerShader))
            {
                _logger.Debug("F5 key pressed - cycling player shaders");
                CyclePlayerShader();
            }
        }

        /// <summary>
        /// Cycles through combined layer shaders (F4).
        /// </summary>
        private void CycleLayerShader()
        {
            // Cycle to next shader
            _currentLayerShaderIndex = (_currentLayerShaderIndex + 1) % _layerShaders.Count;
            string? nextShaderId = _layerShaders[_currentLayerShaderIndex];

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
                    if (World.Has<ShaderParameterAnimationComponent>(existingShaderEntity.Value))
                    {
                        World.Remove<ShaderParameterAnimationComponent>(existingShaderEntity.Value);
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
                    shader.Parameters = GetDefaultParametersForLayerShader(nextShaderId);

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
                        Parameters = GetDefaultParametersForLayerShader(nextShaderId),
                    };

                    shaderEntity = World.Create(shaderComponent);
                    _shaderManagerSystem.MarkShadersDirty();
                    _logger.Information(
                        "Created combined layer shader entity with shader {ShaderId}",
                        nextShaderId
                    );
                }

                // Add animation component if this shader needs animation
                var animationComponent = GetAnimationComponentForLayerShader(nextShaderId);
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

        /// <summary>
        /// Cycles through player entity shaders (F5).
        /// </summary>
        private void CyclePlayerShader()
        {
            // Get player entity
            if (_playerSystem == null)
            {
                _logger.Warning("Cannot cycle player shader - PlayerSystem not available");
                return;
            }

            var playerEntity = _playerSystem.GetPlayerEntity();
            if (!playerEntity.HasValue)
            {
                _logger.Warning("Cannot cycle player shader - player entity not found");
                return;
            }

            // Cycle to next shader
            _currentEntityShaderIndex = (_currentEntityShaderIndex + 1) % _entityShaders.Count;
            string? nextShaderId = _entityShaders[_currentEntityShaderIndex];

            if (nextShaderId == null)
            {
                // Remove animation component if it exists
                if (World.Has<ShaderParameterAnimationComponent>(playerEntity.Value))
                {
                    World.Remove<ShaderParameterAnimationComponent>(playerEntity.Value);
                }

                // Remove shader component if it exists
                if (World.Has<ShaderComponent>(playerEntity.Value))
                {
                    World.Remove<ShaderComponent>(playerEntity.Value);
                    _shaderManagerSystem.MarkShadersDirty();
                    _logger.Information("Removed shader from player entity");
                }
            }
            else
            {
                // Update or create shader component
                if (World.Has<ShaderComponent>(playerEntity.Value))
                {
                    // Update existing shader component
                    ref var shader = ref World.Get<ShaderComponent>(playerEntity.Value);
                    shader.ShaderId = nextShaderId;
                    shader.IsEnabled = true;
                    shader.Parameters = GetDefaultParametersForEntityShader(nextShaderId);
                    _shaderManagerSystem.MarkShadersDirty();
                    _logger.Information("Updated player shader to {ShaderId}", nextShaderId);
                }
                else
                {
                    // Create new shader component
                    var shaderComponent = new ShaderComponent
                    {
                        ShaderId = nextShaderId,
                        IsEnabled = true,
                        RenderOrder = 0,
                        Parameters = GetDefaultParametersForEntityShader(nextShaderId),
                    };

                    World.Add(playerEntity.Value, shaderComponent);

                    // Add animation component if this shader needs animation
                    var animationComponent = GetAnimationComponentForEntityShader(nextShaderId);
                    if (animationComponent.HasValue)
                    {
                        World.Add(playerEntity.Value, animationComponent.Value);
                        _logger.Debug(
                            "Added animation component for player shader {ShaderId}, parameter {ParameterName}",
                            nextShaderId,
                            animationComponent.Value.ParameterName
                        );
                    }

                    _shaderManagerSystem.MarkShadersDirty();
                    _logger.Information("Added shader {ShaderId} to player entity", nextShaderId);
                }
            }
        }

        /// <summary>
        /// Gets default parameters for a layer shader based on its ID.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>Default parameters dictionary, or null if no parameters needed.</returns>
        private Dictionary<string, object>? GetDefaultParametersForLayerShader(string shaderId)
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
        /// Gets default parameters for an entity shader based on its ID.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>Default parameters dictionary, or null if no parameters needed.</returns>
        private Dictionary<string, object>? GetDefaultParametersForEntityShader(string shaderId)
        {
            return shaderId switch
            {
                "base:shader:glow" => new Dictionary<string, object>
                {
                    { "GlowColor", new Vector4(1.0f, 1.0f, 0.0f, 1.0f) }, // Yellow glow
                    { "GlowIntensity", 0.5f },
                },
                "base:shader:outline" => new Dictionary<string, object>
                {
                    { "OutlineColor", new Vector4(1.0f, 0.0f, 1.0f, 1.0f) }, // Magenta outline
                    { "OutlineThickness", 2.0f },
                    { "ScreenSize", new Vector2(800.0f, 600.0f) }, // Will be updated dynamically
                },
                "base:shader:rainbow" => new Dictionary<string, object>
                {
                    { "Intensity", 0.5f },
                    { "Speed", 1.0f },
                    { "Time", 0.0f }, // Will be animated by ShaderParameterAnimationSystem
                },
                "base:shader:pulsingglow" => new Dictionary<string, object>
                {
                    { "GlowColor", new Vector4(1.0f, 0.5f, 0.0f, 1.0f) }, // Orange glow
                    { "BaseIntensity", 0.3f },
                    { "PulseIntensity", 0.4f },
                    { "PulseSpeed", 2.0f },
                    { "Time", 0.0f }, // Will be animated by ShaderParameterAnimationSystem
                },
                "base:shader:invert" => new Dictionary<string, object>
                {
                    { "Intensity", 1.0f }, // Full inversion
                },
                _ => null,
            };
        }

        /// <summary>
        /// Gets animation component for a layer shader if it needs animation.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>Animation component, or null if shader doesn't need animation.</returns>
        private ShaderParameterAnimationComponent? GetAnimationComponentForLayerShader(
            string shaderId
        )
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

        /// <summary>
        /// Gets animation component for an entity shader if it needs animation.
        /// </summary>
        /// <param name="shaderId">The shader ID.</param>
        /// <returns>Animation component, or null if shader doesn't need animation.</returns>
        private ShaderParameterAnimationComponent? GetAnimationComponentForEntityShader(
            string shaderId
        )
        {
            return shaderId switch
            {
                "base:shader:rainbow" => new ShaderParameterAnimationComponent
                {
                    ParameterName = "Time",
                    StartValue = 0.0f,
                    EndValue = 10000.0f, // Very large value for continuous time
                    Duration = 10000.0f, // Very long duration (~2.7 hours)
                    ElapsedTime = 0.0f,
                    Easing = EasingFunction.Linear, // Linear interpolation for continuous time
                    IsLooping = false, // Don't loop - Time will increase continuously
                    IsEnabled = true,
                    PingPong = false,
                },
                "base:shader:pulsingglow" => new ShaderParameterAnimationComponent
                {
                    ParameterName = "Time",
                    StartValue = 0.0f,
                    EndValue = 10000.0f, // Very large value for continuous time
                    Duration = 10000.0f, // Very long duration (~2.7 hours)
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
