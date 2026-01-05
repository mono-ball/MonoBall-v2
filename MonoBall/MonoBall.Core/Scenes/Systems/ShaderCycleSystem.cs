using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Arch.System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Rendering;
using Serilog;

namespace MonoBall.Core.Scenes.Systems;

/// <summary>
///     System that cycles through available shaders when F4 (layer shaders) or F5 (player shaders) is pressed.
///     Consolidates shader cycling logic for both layer-wide and per-entity shaders.
///     Dynamically discovers available shaders from the mod registry.
/// </summary>
public class ShaderCycleSystem : BaseSystem<World, float>, IPrioritizedSystem
{
    private readonly QueryDescription _combinedShaderQuery;

    // Dynamically populated from mod registry
    private readonly List<string?> _entityShaders = new() { null }; // null = disabled
    private readonly GraphicsDevice _graphicsDevice;
    private readonly List<string?> _layerShaders = new() { null }; // null = disabled

    private readonly IInputBindingService _inputBindingService;
    private readonly ILogger _logger;
    private readonly IModManager? _modManager;
    private readonly PlayerSystem? _playerSystem;
    private readonly ShaderManager _shaderManagerSystem;
    private readonly IShaderService? _shaderService;

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
    /// <param name="graphicsDevice">The graphics device for getting viewport dimensions.</param>
    /// <param name="modManager">The mod manager for discovering available shaders.</param>
    /// <param name="shaderService">The shader service for validating shaders exist.</param>
    /// <param name="playerSystem">The player system for getting the player entity (optional, needed for F5).</param>
    /// <param name="logger">The logger for logging operations.</param>
    public ShaderCycleSystem(
        World world,
        IInputBindingService inputBindingService,
        ShaderManager shaderManagerSystem,
        GraphicsDevice graphicsDevice,
        IModManager? modManager = null,
        IShaderService? shaderService = null,
        PlayerSystem? playerSystem = null,
        ILogger? logger = null
    )
        : base(world)
    {
        _inputBindingService =
            inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
        _shaderManagerSystem =
            shaderManagerSystem ?? throw new ArgumentNullException(nameof(shaderManagerSystem));
        _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        _modManager = modManager;
        _shaderService = shaderService;
        _playerSystem = playerSystem;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _combinedShaderQuery = new QueryDescription().WithAll<RenderingShaderComponent>();

        // Discover available shaders from the mod registry
        DiscoverAvailableShaders();
    }

    /// <summary>
    ///     Discovers available shaders from the mod registry and populates the shader lists.
    ///     Shaders in Definitions/Assets/Shaders/Screen/ are layer shaders (F4).
    ///     Shaders in Definitions/Assets/Shaders/Entity/ are entity shaders (F5).
    /// </summary>
    private void DiscoverAvailableShaders()
    {
        if (_modManager?.Registry == null)
        {
            _logger.Warning(
                "ModManager or Registry not available - shader cycling will only have 'disabled' option"
            );
            return;
        }

        // Debug: Log all available definition types in the registry
        var allTypes = _modManager.Registry.GetDefinitionTypes().ToList();
        _logger.Debug(
            "Registry contains {Count} definition types: {Types}",
            allTypes.Count,
            string.Join(", ", allTypes.Take(20))
        );

        // Get all shader definitions - type is "ShaderAsset" (from Definitions/Assets/Shaders/)
        var shaderTypes = new[] { "ShaderAsset" };
        var discoveredLayerShaders = new List<string>();
        var discoveredEntityShaders = new List<string>();

        foreach (var shaderType in shaderTypes)
        {
            var shaderIds = _modManager.Registry.GetByType(shaderType).ToList();
            _logger.Information(
                "Shader discovery: Found {Count} definitions of type '{Type}'",
                shaderIds.Count,
                shaderType
            );

            foreach (var shaderId in shaderIds)
            {
                var metadata = _modManager.Registry.GetById(shaderId);
                if (metadata == null)
                {
                    _logger.Warning("Shader {ShaderId} has no metadata, skipping", shaderId);
                    continue;
                }

                // Verify the shader can actually be loaded
                if (_shaderService != null && !_shaderService.HasShader(shaderId))
                {
                    _logger.Warning(
                        "Skipping shader {ShaderId} - HasShader returned false (SourcePath: {Path})",
                        shaderId,
                        metadata.SourcePath
                    );
                    continue;
                }

                // Determine if it's a layer shader or entity shader based on source path
                // Entity shaders are in /Entity/ folder, everything else is layer shader
                var isEntityShader =
                    metadata.SourcePath?.Contains("/Entity/", StringComparison.OrdinalIgnoreCase)
                    ?? false;

                if (isEntityShader)
                {
                    discoveredEntityShaders.Add(shaderId);
                    _logger.Debug(
                        "Discovered entity shader: {ShaderId} (path: {Path})",
                        shaderId,
                        metadata.SourcePath
                    );
                }
                else
                {
                    discoveredLayerShaders.Add(shaderId);
                    _logger.Debug(
                        "Discovered layer shader: {ShaderId} (path: {Path})",
                        shaderId,
                        metadata.SourcePath
                    );
                }
            }
        }

        // Sort alphabetically for consistent ordering
        discoveredLayerShaders.Sort();
        discoveredEntityShaders.Sort();

        // Organize layer shaders: non-stacked shaders first, then each stack's components followed by the stack
        OrganizeLayerShadersWithStacks(discoveredLayerShaders);

        // Add entity shaders
        _entityShaders.AddRange(discoveredEntityShaders);

        _logger.Information(
            "Shader cycling initialized: {LayerCount} layer shaders (F4), {EntityCount} entity shaders (F5)",
            _layerShaders.Count - 1, // -1 for null option
            _entityShaders.Count - 1
        );

        // Log entity shader IDs if any were found
        if (_entityShaders.Count > 1)
        {
            _logger.Information(
                "Entity shaders available: {ShaderIds}",
                string.Join(", ", _entityShaders.Where(s => s != null))
            );
        }
    }

    /// <summary>
    ///     Organizes layer shaders so that:
    ///     1. Shaders not in any stack come first (alphabetically)
    ///     2. For each stack: component shaders in render order, then the stack preset
    /// </summary>
    private void OrganizeLayerShadersWithStacks(List<string> availableShaders)
    {
        // Collect all shaders that are part of any valid stack
        var shadersInStacks = new HashSet<string>();
        var validStacks =
            new List<(string StackId, List<(string ShaderId, int RenderOrder)> Components)>();

        foreach (var (stackId, stackDef) in _shaderStacks)
        {
            var allShadersAvailable = stackDef.All(s => availableShaders.Contains(s.ShaderId));
            if (allShadersAvailable)
            {
                validStacks.Add((stackId, stackDef));
                foreach (var (shaderId, _) in stackDef)
                    shadersInStacks.Add(shaderId);
            }
            else
            {
                var missingShadersInStack = stackDef
                    .Where(s => !availableShaders.Contains(s.ShaderId))
                    .Select(s => s.ShaderId);
                _logger.Debug(
                    "Skipping shader stack {StackId} - missing shaders: {MissingShaders}",
                    stackId,
                    string.Join(", ", missingShadersInStack)
                );
            }
        }

        // Add shaders that are NOT part of any stack first
        foreach (var shader in availableShaders)
            if (!shadersInStacks.Contains(shader))
                _layerShaders.Add(shader);

        // For each valid stack: add component shaders (in render order), then the stack preset
        foreach (var (stackId, components) in validStacks)
        {
            // Add component shaders sorted by render order
            foreach (var (shaderId, renderOrder) in components.OrderBy(c => c.RenderOrder))
                _layerShaders.Add(shaderId);

            // Add the stack preset itself
            _layerShaders.Add(stackId);
            _logger.Debug(
                "Added shader stack: {StackId} (components: {Components})",
                stackId,
                string.Join(" -> ", components.OrderBy(c => c.RenderOrder).Select(c => c.ShaderId))
            );
        }
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
            _logger.Information(
                "F4 pressed - cycling layer shaders ({Count} available)",
                _layerShaders.Count - 1
            );
            CycleLayerShader();
        }

        // Check if CyclePlayerShader action was just pressed (F5 - player shaders)
        if (_inputBindingService.IsActionJustPressed(InputAction.CyclePlayerShader))
        {
            _logger.Information(
                "F5 pressed - cycling player shaders ({Count} available)",
                _entityShaders.Count - 1
            );
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
            }

            _logger.Information("Layer shader cycling: disabled (no shader)");
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
                    var animationComponent = GetAnimationComponentForShader(shaderId);
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

                // Remove old animation component and add new one if needed
                if (World.Has<ShaderParameterAnimationComponent>(playerEntity.Value))
                    World.Remove<ShaderParameterAnimationComponent>(playerEntity.Value);

                var animationComponent = GetAnimationComponentForShader(nextShaderId);
                if (animationComponent.HasValue)
                {
                    World.Add(playerEntity.Value, animationComponent.Value);
                    _logger.Information(
                        "Updated animation component for player shader {ShaderId}",
                        nextShaderId
                    );
                }

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
                var animationComponent = GetAnimationComponentForShader(nextShaderId);
                if (animationComponent.HasValue)
                {
                    World.Add(playerEntity.Value, animationComponent.Value);
                    _logger.Information(
                        "Added animation component for player shader {ShaderId}",
                        nextShaderId
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
                { "ScreenSize", GetViewportSize() },
            },
            "base:shader:wavedistortion" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", GetViewportSize() },
                { "WaveAmplitude", 0.025f },
                { "WaveFrequency", 8.0f },
                { "TurbulenceStrength", 0.5f },
                { "TurbulenceScale", 4.0f },
            },
            "base:shader:kaleidoscope" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", GetViewportSize() },
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
                { "ScreenSize", GetViewportSize() },
            },
            "base:shader:spooky" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", GetViewportSize() },
                { "VignetteIntensity", 0.8f },
                { "VignetteRadius", 0.75f },
                { "VignetteSoftness", 0.45f },
                { "Desaturation", 0.4f },
                { "TintColor", new Vector3(0.6f, 0.4f, 0.8f) },
                { "TintStrength", 0.25f },
                { "ChromaticAberration", 0.003f },
                { "ChromaticPulse", 0.5f },
                { "DarknessPulseSpeed", 1.5f },
                { "DarknessPulseAmount", 0.15f },
                { "GrainIntensity", 0.08f },
                { "GrainSpeed", 15.0f },
                { "FogIntensity", 0.15f },
                { "FogSpeed", 0.3f },
                { "FogScale", 3.0f },
            },
            "base:shader:glitch" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", GetViewportSize() },
                { "GlitchIntensity", 0.8f },
                { "ScanlineJitter", 0.02f },
                { "ColorDrift", 0.01f },
                { "StaticIntensity", 0.1f },
                { "RGBSplitAmount", 0.005f },
            },
            "base:shader:underwater" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", GetViewportSize() },
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
                { "ScreenSize", GetViewportSize() },
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
                { "ScreenSize", GetViewportSize() },
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
                { "ScreenSize", GetViewportSize() },
                { "GridScale", 40.0f },
                { "LineThickness", 0.08f },
                { "GridColor", new Vector3(0.0f, 1.0f, 0.9f) },
                { "GridOpacity", 0.15f },
                { "PulseSpeed", 2.0f },
            },
            "base:shader:datastream" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ScreenSize", GetViewportSize() },
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
                { "ScreenSize", GetViewportSize() },
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
                { "ScreenSize", GetViewportSize() },
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
                { "FlameColor1", new Vector3(1.0f, 0.9f, 0.2f) },
                { "FlameColor2", new Vector3(1.0f, 0.4f, 0.0f) },
                { "FlameColor3", new Vector3(0.8f, 0.1f, 0.0f) },
                { "FlameSpeed", 4.0f },
                { "FlameIntensity", 0.7f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:electric" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "ElectricColor", new Vector3(0.5f, 0.8f, 1.0f) },
                { "CoreColor", new Vector3(1.0f, 1.0f, 1.0f) },
                { "Intensity", 1.0f },
                { "FlashRate", 10.0f },
                { "SpriteSize", new Vector2(32.0f, 32.0f) },
            },
            "base:shader:frozen" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "IceColor", new Vector3(0.6f, 0.85f, 1.0f) },
                { "FrostColor", new Vector3(0.95f, 0.98f, 1.0f) },
                { "FrostAmount", 0.6f },
                { "ShimmerSpeed", 3.0f },
            },
            "base:shader:ghost" => new Dictionary<string, object>
            {
                { "Time", 0.0f },
                { "GhostTint", new Vector3(0.7f, 0.85f, 1.0f) },
                { "Transparency", 0.5f },
                { "WaveSpeed", 2.0f },
                { "FlickerSpeed", 8.0f },
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
    ///     Gets the current viewport dimensions as a Vector2.
    ///     Used to pass screen size to shaders that need it.
    /// </summary>
    /// <returns>Viewport dimensions (width, height).</returns>
    private Vector2 GetViewportSize()
    {
        var viewport = _graphicsDevice.Viewport;
        return new Vector2(viewport.Width, viewport.Height);
    }

    /// <summary>
    ///     Gets animation component for a shader if it needs time-based animation.
    ///     Checks if the shader has a "Time" parameter in its definition.
    /// </summary>
    /// <param name="shaderId">The shader ID.</param>
    /// <returns>Animation component, or null if shader doesn't need animation.</returns>
    private ShaderParameterAnimationComponent? GetAnimationComponentForShader(string shaderId)
    {
        if (_modManager == null)
            return null;

        // Get shader definition to check for Time parameter
        var shaderDef = _modManager.GetDefinition<ShaderDefinition>(shaderId);
        if (shaderDef?.Parameters == null)
            return null;

        // Check if shader has a Time parameter - if so, it needs animation
        var hasTimeParam = shaderDef.Parameters.Exists(p =>
            p.Name == "Time" && (p.Type == "float" || p.Type == "float1")
        );

        if (!hasTimeParam)
            return null;

        // All animated shaders use Time parameter with continuous linear progression
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
    }
}
