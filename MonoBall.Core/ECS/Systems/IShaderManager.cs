using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Interface for shader management operations.
///     Manages shader effects and updates their parameters.
/// </summary>
public interface IShaderManager
{
    /// <summary>
    ///     Updates shader state. Called in Render phase, just before rendering systems need shaders.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, includes global shaders only.</param>
    void UpdateShaderState(Entity? sceneEntity = null);

    /// <summary>
    ///     Gets the active shader stack for tile layer rendering.
    ///     Returns all enabled shaders sorted by RenderOrder.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
    IReadOnlyList<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> GetTileLayerShaderStack(Entity? sceneEntity = null);

    /// <summary>
    ///     Gets the active shader stack for sprite layer rendering.
    ///     Returns all enabled shaders sorted by RenderOrder.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
    IReadOnlyList<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> GetSpriteLayerShaderStack(Entity? sceneEntity = null);

    /// <summary>
    ///     Gets the active shader stack for combined layer rendering (post-processing).
    ///     Returns all enabled shaders sorted by RenderOrder.
    /// </summary>
    /// <param name="sceneEntity">Optional scene entity to filter shaders. If null, returns global shaders only.</param>
    IReadOnlyList<(
        Effect effect,
        ShaderBlendMode blendMode,
        Entity entity
    )> GetCombinedLayerShaderStack(Entity? sceneEntity = null);

    /// <summary>
    ///     Gets the active shader for tile layer rendering (backward compatibility).
    ///     Returns the first shader from the stack, or null if no shaders.
    /// </summary>
    Effect? GetTileLayerShader();

    /// <summary>
    ///     Gets the active shader for sprite layer rendering (backward compatibility).
    ///     Returns the first shader from the stack, or null if no shaders.
    /// </summary>
    Effect? GetSpriteLayerShader();

    /// <summary>
    ///     Gets the active shader for combined layer rendering (backward compatibility).
    ///     Returns the first shader from the stack, or null if no shaders.
    /// </summary>
    Effect? GetCombinedLayerShader();

    /// <summary>
    ///     Forces update of all parameters for all active combined layer shaders.
    ///     Called right before SpriteBatch.Begin() in Immediate mode to ensure parameters are set.
    /// </summary>
    void ForceUpdateCombinedLayerParameters();

    /// <summary>
    ///     Updates dynamic parameters for all active combined layer shaders.
    ///     Called before applying post-processing to ensure correct parameter values.
    /// </summary>
    /// <param name="parameters">Dictionary of parameter names to values.</param>
    void UpdateCombinedLayerDynamicParameters(Dictionary<string, object> parameters);

    /// <summary>
    ///     Marks shaders as dirty, forcing an update on next UpdateShaderState() call.
    ///     Called when components are added/removed/modified.
    /// </summary>
    void MarkShadersDirty();

    /// <summary>
    ///     Checks if two shaders are compatible with each other.
    /// </summary>
    /// <param name="shaderId1">First shader ID.</param>
    /// <param name="shaderId2">Second shader ID.</param>
    /// <returns>True if shaders are compatible, false otherwise.</returns>
    bool AreCompatible(string shaderId1, string shaderId2);

    /// <summary>
    ///     Validates an entire shader stack for compatibility.
    ///     Logs warnings for incompatible combinations.
    /// </summary>
    /// <param name="shaderIds">List of shader IDs in the stack.</param>
    /// <returns>True if all shaders are compatible, false otherwise.</returns>
    bool ValidateShaderStack(IReadOnlyList<string> shaderIds);
}
