using System;
using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using Serilog;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Helper class for applying shader templates to layers.
///     Templates are pre-configured combinations of shaders that can be applied together.
/// </summary>
public class ShaderTemplateSystem
{
    // Cached query description to avoid allocations in hot paths
    private static readonly QueryDescription _renderingShaderQuery =
        new QueryDescription().WithAll<RenderingShaderComponent>();

    private readonly ILogger _logger;
    private readonly IModManager _modManager;
    private readonly World _world;

    /// <summary>
    ///     Initializes a new instance of the ShaderTemplateSystem.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="modManager">The mod manager for accessing shader definitions.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public ShaderTemplateSystem(World world, IModManager modManager, ILogger logger)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Applies a shader template to the specified layer.
    ///     Creates or updates RenderingShaderComponent entities for each shader in the template.
    /// </summary>
    /// <param name="templateId">The template ID to apply (e.g., "base:template:nighttime").</param>
    /// <param name="layer">The layer to apply the template to.</param>
    /// <returns>True if the template was successfully applied, false otherwise.</returns>
    public bool ApplyTemplate(string templateId, ShaderLayer layer)
    {
        if (string.IsNullOrEmpty(templateId))
            throw new ArgumentException("Template ID cannot be null or empty.", nameof(templateId));

        // Get template from registry
        var template = _modManager.Registry.GetById<ShaderTemplate>(templateId);
        if (template == null)
        {
            _logger.Warning("Shader template {TemplateId} not found in registry.", templateId);
            return false;
        }

        if (template.Shaders == null || template.Shaders.Count == 0)
        {
            _logger.Warning("Shader template {TemplateId} has no shaders.", templateId);
            return false;
        }

        // Find existing layer shader entities for this layer
        var existingEntities = new List<Entity>();
        _world.Query(
            in _renderingShaderQuery,
            (ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == layer)
                {
                    // Get entity from query context (Arch ECS pattern)
                    // Note: This requires accessing the entity from the query, which may need adjustment
                }
            }
        );

        // For now, we'll create new entities for each shader in the template
        // In a full implementation, we might want to update existing entities or clear old ones first
        foreach (var entry in template.Shaders)
        {
            // Validate shader exists
            var shaderDef = _modManager.Registry.GetById<ShaderDefinition>(entry.ShaderId);
            if (shaderDef == null)
            {
                _logger.Warning(
                    "Shader {ShaderId} in template {TemplateId} not found. Skipping.",
                    entry.ShaderId,
                    templateId
                );
                continue;
            }

            // Create entity with RenderingShaderComponent
            var entity = _world.Create(
                new RenderingShaderComponent
                {
                    Layer = layer,
                    ShaderId = entry.ShaderId,
                    IsEnabled = true,
                    RenderOrder = entry.RenderOrder,
                    BlendMode = entry.BlendMode,
                    Parameters =
                        entry.Parameters != null
                            ? new Dictionary<string, object>(entry.Parameters)
                            : null,
                }
            );

            _logger.Debug(
                "Applied shader {ShaderId} from template {TemplateId} to layer {Layer}",
                entry.ShaderId,
                templateId,
                layer
            );
        }

        _logger.Information(
            "Applied shader template {TemplateId} to layer {Layer} with {Count} shaders",
            templateId,
            layer,
            template.Shaders.Count
        );

        return true;
    }

    /// <summary>
    ///     Removes all shaders from a layer (clears the layer's shader stack).
    /// </summary>
    /// <param name="layer">The layer to clear.</param>
    public void ClearLayer(ShaderLayer layer)
    {
        // Query for all entities with RenderingShaderComponent
        var entitiesToRemove = new List<Entity>();

        _world.Query(
            in _renderingShaderQuery,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == layer)
                    entitiesToRemove.Add(entity);
            }
        );

        foreach (var entity in entitiesToRemove)
            _world.Destroy(entity);

        _logger.Debug("Cleared {Count} shaders from layer {Layer}", entitiesToRemove.Count, layer);
    }
}
