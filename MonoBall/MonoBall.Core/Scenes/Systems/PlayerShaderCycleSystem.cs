using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using Serilog;

namespace MonoBall.Core.Scenes.Systems
{
    /// <summary>
    /// System that cycles through available per-entity shaders for the player when F5 is pressed.
    /// </summary>
    public class PlayerShaderCycleSystem : BaseSystem<World, float>
    {
        private readonly IInputBindingService _inputBindingService;
        private readonly PlayerSystem _playerSystem;
        private readonly ShaderManagerSystem _shaderManagerSystem;
        private readonly ILogger _logger;
        private readonly QueryDescription _playerQuery;

        // Available per-entity shader IDs to cycle through
        private readonly List<string?> _availableShaders = new()
        {
            null, // No shader (disabled)
            "PerEntityGlow",
            "PerEntityOutline",
            "PerEntityRainbow",
            "PerEntityPulsingGlow",
            "PerEntityInvert",
        };

        // Start at index 0 (no shader) since player doesn't have shader by default
        private int _currentShaderIndex = 0;

        /// <summary>
        /// Initializes a new instance of the PlayerShaderCycleSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="inputBindingService">The input binding service for checking input.</param>
        /// <param name="playerSystem">The player system for getting the player entity.</param>
        /// <param name="shaderManagerSystem">The shader manager system for managing shaders.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public PlayerShaderCycleSystem(
            World world,
            IInputBindingService inputBindingService,
            PlayerSystem playerSystem,
            ShaderManagerSystem shaderManagerSystem,
            ILogger logger
        )
            : base(world)
        {
            _inputBindingService =
                inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _playerSystem = playerSystem ?? throw new ArgumentNullException(nameof(playerSystem));
            _shaderManagerSystem =
                shaderManagerSystem ?? throw new ArgumentNullException(nameof(shaderManagerSystem));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _playerQuery = new QueryDescription().WithAll<PlayerComponent>();
        }

        /// <summary>
        /// Updates the system, checking for F5 key press to cycle player shaders.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            // Check if CyclePlayerShader action was just pressed
            if (_inputBindingService.IsActionJustPressed(InputAction.CyclePlayerShader))
            {
                _logger.Debug("F5 key pressed - cycling player shaders");

                // Get player entity
                var playerEntity = _playerSystem.GetPlayerEntity();
                if (!playerEntity.HasValue)
                {
                    _logger.Warning("Cannot cycle player shader - player entity not found");
                    return;
                }

                // Cycle to next shader
                _currentShaderIndex = (_currentShaderIndex + 1) % _availableShaders.Count;
                string? nextShaderId = _availableShaders[_currentShaderIndex];

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
                        shader.Parameters = GetDefaultParametersForShader(nextShaderId);
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
                            Parameters = GetDefaultParametersForShader(nextShaderId),
                        };

                        World.Add(playerEntity.Value, shaderComponent);

                        // Add animation component if this shader needs animation
                        var animationComponent = GetAnimationComponentForShader(nextShaderId);
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
                        _logger.Information(
                            "Added shader {ShaderId} to player entity",
                            nextShaderId
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
                "PerEntityGlow" => new Dictionary<string, object>
                {
                    { "GlowColor", new Vector4(1.0f, 1.0f, 0.0f, 1.0f) }, // Yellow glow
                    { "GlowIntensity", 0.5f },
                },
                "PerEntityOutline" => new Dictionary<string, object>
                {
                    { "OutlineColor", new Vector4(1.0f, 0.0f, 1.0f, 1.0f) }, // Magenta outline
                    { "OutlineThickness", 2.0f },
                    { "ScreenSize", new Vector2(800.0f, 600.0f) }, // Will be updated dynamically
                },
                "PerEntityRainbow" => new Dictionary<string, object>
                {
                    { "Intensity", 0.5f },
                    { "Speed", 1.0f },
                    { "Time", 0.0f }, // Will be animated by ShaderParameterAnimationSystem
                },
                "PerEntityPulsingGlow" => new Dictionary<string, object>
                {
                    { "GlowColor", new Vector4(1.0f, 0.5f, 0.0f, 1.0f) }, // Orange glow
                    { "BaseIntensity", 0.3f },
                    { "PulseIntensity", 0.4f },
                    { "PulseSpeed", 2.0f },
                    { "Time", 0.0f }, // Will be animated by ShaderParameterAnimationSystem
                },
                "PerEntityInvert" => new Dictionary<string, object>
                {
                    { "Intensity", 1.0f }, // Full inversion
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
                "PerEntityRainbow" => new ShaderParameterAnimationComponent
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
                "PerEntityPulsingGlow" => new ShaderParameterAnimationComponent
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
