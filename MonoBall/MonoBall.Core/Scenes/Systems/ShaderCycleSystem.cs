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

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that cycles through available shaders when F4 (layer shaders) or F5 (player shaders) is pressed.
///     Consolidates shader cycling logic for both layer-wide and per-entity shaders.
/// </summary>
public class ShaderCycleSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly QueryDescription _combinedShaderQuery;

    // Available per-entity shader IDs to cycle through (F5)
    private readonly List<string?> _entityShaders = new()
    {
        null, // No shader (disabled)
        "base:shader:outline",
        "base:shader:dissolve",
        "base:shader:hologram",
        "base:shader:fire",
        "base:shader:electric",
        "base:shader:frozen",
        "base:shader:ghost",
        "base:shader:silhouette",
    };

    private readonly IInputBindingService _inputBindingService;

    // Available combined layer shader IDs to cycle through (F4)
    // Use "STACK:" prefix for multi-shader presets
    private readonly List<string?> _layerShaders = new()
    {
        null, // No shader (disabled)
        "base:shader:crt",
        "base:shader:spooky",
        "base:shader:kaleidoscope",
        "base:shader:wavedistortion",
        "base:shader:glitch",
        "base:shader:underwater",
        "base:shader:dream",
        "base:shader:heathaze",
        "base:shader:noir",
        "base:shader:neongrade",
        "base:shader:hexgrid",
        "base:shader:datastream",
        "STACK:cyberpunk", // Stacked: NeonGrade + HexGrid + DataStream
        "base:shader:prismgrade",
        "base:shader:scanpulse",
        "base:shader:pixelrain",
        "STACK:vaporwave", // Stacked: PrismGrade + ScanPulse + PixelRain
    };

    private readonly ILogger _logger;
    private readonly PlayerSystem? _playerSystem;
    private readonly ShaderManagerSystem _shaderManagerSystem;

    // Stacked shader presets - each contains shaders with render orders
    private readonly Dictionary<string, List<(string ShaderId, int RenderOrder)>> _shaderStacks =
        new()
        {
            {
                "STACK:cyberpunk",
                new List<(string, int)>
                {
                    ("base:shader:neongrade", 0),
                    ("base:shader:hexgrid", 10),
                    ("base:shader:datastream", 20),
                }
            },
            {
                "STACK:vaporwave",
                new List<(string, int)>
                {
                    ("base:shader:prismgrade", 0), // Color grading base layer
                    ("base:shader:scanpulse", 10), // Scan lines and pulses mid layer
                    ("base:shader:pixelrain", 20), // Pixel rain overlay top layer
                }
            },
        };

    // Track stacked shader entities for cleanup
    private readonly List<Entity> _stackedShaderEntities = new();
    private int _currentEntityShaderIndex;

    // Current indices for cycling
    private int _currentLayerShaderIndex;

    /// <summary>
    ///     Initializes a new instance of the ShaderCycleSystem.
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
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.ShaderCycle;

    /// <summary>
    ///     Updates the system, checking for F4 (layer shaders) or F5 (player shaders) key press to cycle shaders.
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
    ///     Cycles through combined layer shaders (F4).
    /// </summary>
    private void CycleLayerShader()
    {
        // Cycle to next shader
        _currentLayerShaderIndex = (_currentLayerShaderIndex + 1) % _layerShaders.Count;
        var nextShaderId = _layerShaders[_currentLayerShaderIndex];

        // Clean up any existing stacked shader entities first
        ClearStackedShaders();

        // Find existing combined layer shader entity
        Entity? existingShaderEntity = null;
        World.Query(
            in _combinedShaderQuery,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == ShaderLayer.CombinedLayer)
                    existingShaderEntity = entity;
            }
        );

        if (nextShaderId == null)
        {
            // Disable all combined layer shaders
            if (existingShaderEntity.HasValue)
            {
                // Remove animation component if it exists
                if (World.Has<ShaderParameterAnimationComponent>(existingShaderEntity.Value))
                    World.Remove<ShaderParameterAnimationComponent>(existingShaderEntity.Value);

                ref var shader = ref World.Get<RenderingShaderComponent>(
                    existingShaderEntity.Value
                );
                shader.IsEnabled = false;
                _shaderManagerSystem.MarkShadersDirty();
                _logger.Information("Disabled combined layer shader");
            }
        }
        else if (nextShaderId.StartsWith("STACK:"))
        {
            // Handle stacked shader preset
            if (existingShaderEntity.HasValue)
            {
                // Disable the single shader entity
                if (World.Has<ShaderParameterAnimationComponent>(existingShaderEntity.Value))
                    World.Remove<ShaderParameterAnimationComponent>(existingShaderEntity.Value);
                ref var shader = ref World.Get<RenderingShaderComponent>(
                    existingShaderEntity.Value
                );
                shader.IsEnabled = false;
            }

            // Create stacked shaders
            if (_shaderStacks.TryGetValue(nextShaderId, out var stackDefinition))
            {
                foreach (var (shaderId, renderOrder) in stackDefinition)
                {
                    var shaderComponent = new RenderingShaderComponent
                    {
                        Layer = ShaderLayer.CombinedLayer,
                        ShaderId = shaderId,
                        IsEnabled = true,
                        RenderOrder = renderOrder,
                        Parameters = GetDefaultParametersForLayerShader(shaderId),
                    };

                    var stackedEntity = World.Create(shaderComponent);
                    _stackedShaderEntities.Add(stackedEntity);

                    // Add animation component if needed
                    var animationComponent = GetAnimationComponentForLayerShader(shaderId);
                    if (animationComponent.HasValue)
                        World.Add(stackedEntity, animationComponent.Value);

                    _logger.Debug(
                        "Created stacked shader entity with shader {ShaderId}, RenderOrder {RenderOrder}",
                        shaderId,
                        renderOrder
                    );
                }

                _shaderManagerSystem.MarkShadersDirty();
                _logger.Information(
                    "Activated shader stack '{StackName}' with {Count} layered shaders",
                    nextShaderId,
                    stackDefinition.Count
                );
            }
        }
        else
        {
            // Update or create single shader entity
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
                    World.Remove<ShaderParameterAnimationComponent>(shaderEntity);

                _shaderManagerSystem.MarkShadersDirty();
                _logger.Information("Updated combined layer shader to {ShaderId}", nextShaderId);
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
    ///     Clears all stacked shader entities.
    /// </summary>
    private void ClearStackedShaders()
    {
        foreach (var entity in _stackedShaderEntities)
            if (World.IsAlive(entity))
            {
                if (World.Has<ShaderParameterAnimationComponent>(entity))
                    World.Remove<ShaderParameterAnimationComponent>(entity);
                World.Destroy(entity);
            }

        _stackedShaderEntities.Clear();
    }

    /// <summary>
    ///     Cycles through player entity shaders (F5).
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
        var nextShaderId = _entityShaders[_currentEntityShaderIndex];

        if (nextShaderId == null)
        {
            // Remove animation component if it exists
            if (World.Has<ShaderParameterAnimationComponent>(playerEntity.Value))
                World.Remove<ShaderParameterAnimationComponent>(playerEntity.Value);

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
    ///     Gets default parameters for a layer shader based on its ID.
    /// </summary>
    /// <param name="shaderId">The shader ID.</param>
    /// <returns>Default parameters dictionary, or null if no parameters needed.</returns>
    private Dictionary<string, object>? GetDefaultParametersForLayerShader(string shaderId)
    {
        return shaderId switch
        {
            "base:shader:crt" => new Dictionary<string, object>
            {
                { "Curvature", 0.1f },
                { "ScanlineIntensity", 0.3f },
                { "ScanlineCount", 400.0f },
                { "ChromaticAberration", 0.003f },
            },
            "base:shader:wavedistortion" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "WaveAmplitude", 0.025f },
                { "WaveFrequency", 8.0f },
                { "TurbulenceStrength", 0.5f },
                { "TurbulenceScale", 4.0f },
            },
            "base:shader:kaleidoscope" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "SegmentCount", 6.0f },
                { "RotationSpeed", 0.3f },
                { "Zoom", 1.0f },
            },
            "base:shader:noir" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "Contrast", 1.8f },
                { "Brightness", -0.1f },
                { "VignetteIntensity", 0.7f },
                { "GrainAmount", 0.08f },
                { "ShadowTint", new Vector3(0.1f, 0.1f, 0.15f) },
                { "HighlightTint", new Vector3(1.0f, 0.98f, 0.95f) },
                { "ScreenSize", new Vector2(1280.0f, 720.0f) },
            },
            "base:shader:spooky" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "VignetteIntensity", 0.8f },
                { "VignetteRadius", 0.75f },
                { "VignetteSoftness", 0.45f },
                { "Desaturation", 0.5f },
                { "TintColor", new Vector3(0.7f, 0.5f, 1.0f) },
                { "TintStrength", 0.5f },
                { "ChromaticAberration", 0.003f },
                { "ChromaticPulse", 0.5f },
                { "DarknessPulseSpeed", 1.5f },
                { "DarknessPulseAmount", 0.15f },
                { "GrainIntensity", 0.08f },
                { "GrainSpeed", 15.0f },
                { "FogIntensity", 0.35f },
                { "FogSpeed", 0.5f },
                { "FogScale", 2.0f },
            },
            "base:shader:glitch" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "GlitchIntensity", 0.8f },
                { "ScanlineJitter", 0.02f },
                { "ColorDrift", 0.01f },
                { "StaticIntensity", 0.1f },
                { "RGBSplitAmount", 0.005f },
            },
            "base:shader:underwater" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "WaveStrength", 0.02f },
                { "WaveFrequency", 10.0f },
                { "CausticIntensity", 0.3f },
                { "CausticScale", 8.0f },
                { "TintColor", new Vector3(0.3f, 0.5f, 0.8f) },
                { "TintStrength", 0.3f },
                { "FogDensity", 0.2f },
            },
            "base:shader:dream" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "BlurAmount", 0.003f },
                { "GlowIntensity", 0.4f },
                { "VignetteStrength", 0.5f },
                { "ColorShift", 0.1f },
                { "SparkleIntensity", 0.15f },
                { "PulseSpeed", 1.0f },
            },
            "base:shader:heathaze" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "HazeStrength", 0.015f },
                { "RiseSpeed", 2.0f },
                { "WaveFrequency", 20.0f },
                { "DistortionScale", 3.0f },
            },
            "base:shader:neongrade" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ShadowColor", new Vector3(0.1f, 0.0f, 0.2f) },
                { "MidColor", new Vector3(0.0f, 0.8f, 0.9f) },
                { "HighlightColor", new Vector3(1.0f, 0.3f, 0.8f) },
                { "Intensity", 0.6f },
                { "Saturation", 1.3f },
            },
            "base:shader:hexgrid" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", new Vector2(1280.0f, 720.0f) },
                { "GridScale", 40.0f },
                { "LineThickness", 0.08f },
                { "GridColor", new Vector3(0.0f, 1.0f, 0.9f) },
                { "GridOpacity", 0.15f },
                { "PulseSpeed", 2.0f },
            },
            "base:shader:datastream" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", new Vector2(1280.0f, 720.0f) },
                { "StreamSpeed", 1.5f },
                { "StreamDensity", 30.0f },
                { "StreamColor", new Vector3(0.0f, 1.0f, 0.5f) },
                { "StreamOpacity", 0.12f },
                { "TrailLength", 0.3f },
            },
            "base:shader:prismgrade" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ShadowColor", new Vector3(0.15f, 0.0f, 0.25f) },
                { "MidColor", new Vector3(1.0f, 0.4f, 0.8f) },
                { "HighlightColor", new Vector3(0.4f, 1.0f, 1.0f) },
                { "GradeIntensity", 0.65f },
                { "PrismStrength", 0.008f },
                { "ChromaShift", 0.003f },
                { "Saturation", 1.4f },
                { "GlowAmount", 0.25f },
            },
            "base:shader:scanpulse" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", new Vector2(1280.0f, 720.0f) },
                { "ScanlineCount", 180.0f },
                { "ScanlineIntensity", 0.12f },
                { "ScanlineSpeed", 0.0f },
                { "PulseSpeed", 1.2f },
                { "PulseWidth", 0.08f },
                { "PulseGlow", 0.5f },
                { "PulseColor", new Vector3(1.0f, 0.3f, 0.8f) },
                { "WaveSpeed", 0.8f },
                { "WaveIntensity", 0.15f },
                { "WaveColor", new Vector3(0.3f, 1.0f, 1.0f) },
                { "DistortionAmount", 0.004f },
            },
            "base:shader:pixelrain" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", new Vector2(1280.0f, 720.0f) },
                { "PixelDensity", 45.0f },
                { "FallSpeed", 1.8f },
                { "TrailLength", 0.35f },
                { "PrimaryColor", new Vector3(1.0f, 0.4f, 0.9f) },
                { "SecondaryColor", new Vector3(0.4f, 1.0f, 1.0f) },
                { "AccentColor", new Vector3(0.9f, 0.9f, 1.0f) },
                { "RainOpacity", 0.18f },
                { "GlowIntensity", 0.6f },
                { "Sparkle", 0.4f },
            },
            _ => null,
        };
    }

    /// <summary>
    ///     Gets default parameters for an entity shader based on its ID.
    /// </summary>
    /// <param name="shaderId">The shader ID.</param>
    /// <returns>Default parameters dictionary, or null if no parameters needed.</returns>
    private Dictionary<string, object>? GetDefaultParametersForEntityShader(string shaderId)
    {
        return shaderId switch
        {
            "base:shader:outline" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "BaseThickness", 2.0f },
                { "PulseAmount", 1.0f },
                { "PulseSpeed", 3.0f },
                { "RainbowSpeed", 1.0f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:dissolve" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "EdgeWidth", 0.15f },
                { "EdgeColor", new Vector3(1.0f, 0.5f, 0.0f) },
                { "EdgeColor2", new Vector3(1.0f, 0.2f, 0.0f) },
                { "NoiseScale", 6.0f },
                { "CycleSpeed", 0.3f },
            },
            "base:shader:hologram" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "HoloColor", new Vector3(0.3f, 0.7f, 1.0f) },
                { "ScanlineIntensity", 0.3f },
                { "ScanlineSpeed", 2.0f },
                { "ScanlineCount", 30.0f },
                { "FlickerSpeed", 15.0f },
                { "FlickerIntensity", 0.15f },
                { "GlitchIntensity", 0.3f },
                { "Transparency", 0.7f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:fire" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "FlameHeight", 0.15f },
                { "FlameSpeed", 3.0f },
                { "FlameIntensity", 1.0f },
                { "FlameColor1", new Vector3(1.0f, 0.9f, 0.3f) },
                { "FlameColor2", new Vector3(1.0f, 0.4f, 0.0f) },
                { "FlameColor3", new Vector3(0.8f, 0.1f, 0.0f) },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:electric" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ElectricColor", new Vector3(0.5f, 0.8f, 1.0f) },
                { "CoreColor", new Vector3(1.0f, 1.0f, 1.0f) },
                { "Intensity", 1.0f },
                { "ArcFrequency", 8.0f },
                { "SparkRate", 10.0f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:frozen" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "IceColor", new Vector3(0.7f, 0.9f, 1.0f) },
                { "FrostColor", new Vector3(0.9f, 0.95f, 1.0f) },
                { "DeepIceColor", new Vector3(0.3f, 0.5f, 0.8f) },
                { "FrostAmount", 0.5f },
                { "ShimmerSpeed", 2.0f },
                { "CrystalDensity", 15.0f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:ghost" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "GhostTint", new Vector3(0.8f, 0.9f, 1.0f) },
                { "WispColor", new Vector3(0.6f, 0.8f, 1.0f) },
                { "Transparency", 0.6f },
                { "WaveSpeed", 2.0f },
                { "WaveAmount", 0.02f },
                { "WispSpeed", 1.5f },
                { "WispDensity", 5.0f },
                { "FlickerSpeed", 8.0f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:silhouette" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "FillColor", new Vector3(0.05f, 0.05f, 0.1f) },
                { "EdgeColor", new Vector3(1.0f, 0.7f, 0.2f) },
                { "EdgeColor2", new Vector3(1.0f, 0.3f, 0.1f) },
                { "PulseSpeed", 2.0f },
                { "WaveSpeed", 3.0f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            _ => null,
        };
    }

    /// <summary>
    ///     Gets animation component for a layer shader if it needs animation.
    /// </summary>
    /// <param name="shaderId">The shader ID.</param>
    /// <returns>Animation component, or null if shader doesn't need animation.</returns>
    private ShaderParameterAnimationComponent? GetAnimationComponentForLayerShader(string shaderId)
    {
        // All animated shaders use Time parameter with continuous linear progression
        var animatedShaders = new[]
        {
            "base:shader:wavedistortion",
            "base:shader:kaleidoscope",
            "base:shader:spooky",
            "base:shader:glitch",
            "base:shader:underwater",
            "base:shader:dream",
            "base:shader:heathaze",
            "base:shader:noir",
            "base:shader:neongrade",
            "base:shader:hexgrid",
            "base:shader:datastream",
            "base:shader:prismgrade",
            "base:shader:scanpulse",
            "base:shader:pixelrain",
        };

        if (Array.Exists(animatedShaders, s => s == shaderId))
            return new ShaderParameterAnimationComponent
            {
                ParameterName = "Time",
                StartValue = 0.0f,
                EndValue = 10000.0f,
                Duration = 10000.0f,
                ElapsedTime = 0.0f,
                Easing = EasingFunction.Linear,
                IsLooping = false,
                IsEnabled = true,
                PingPong = false,
            };

        return null;
    }

    /// <summary>
    ///     Gets animation component for an entity shader if it needs animation.
    /// </summary>
    /// <param name="shaderId">The shader ID.</param>
    /// <returns>Animation component, or null if shader doesn't need animation.</returns>
    private ShaderParameterAnimationComponent? GetAnimationComponentForEntityShader(string shaderId)
    {
        // All animated entity shaders use Time parameter with continuous linear progression
        var animatedEntityShaders = new[]
        {
            "base:shader:outline",
            "base:shader:dissolve",
            "base:shader:hologram",
            "base:shader:fire",
            "base:shader:electric",
            "base:shader:frozen",
            "base:shader:ghost",
            "base:shader:silhouette",
        };

        if (Array.Exists(animatedEntityShaders, s => s == shaderId))
            return new ShaderParameterAnimationComponent
            {
                ParameterName = "Time",
                StartValue = 0.0f,
                EndValue = 10000.0f,
                Duration = 10000.0f,
                ElapsedTime = 0.0f,
                Easing = EasingFunction.Linear,
                IsLooping = false,
                IsEnabled = true,
                PingPong = false,
            };

        return null;
    }
}
