using Arch.Core;

namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// Entity-level shader control API.
    /// Provides methods to enable/disable shaders and manage parameters on specific entities.
    /// Split from IShaderApi per Interface Segregation Principle (ISP).
    /// </summary>
    public interface IShaderEntityApi
    {
        /// <summary>
        /// Enables the shader on an entity with ShaderComponent.
        /// </summary>
        /// <param name="entity">The entity with a shader component.</param>
        /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if entity has no ShaderComponent.</exception>
        void EnableShader(Entity entity);

        /// <summary>
        /// Disables the shader on an entity with ShaderComponent.
        /// </summary>
        /// <param name="entity">The entity with a shader component.</param>
        /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if entity has no ShaderComponent.</exception>
        void DisableShader(Entity entity);

        /// <summary>
        /// Checks if the shader is enabled on an entity.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>True if shader is enabled, false otherwise or if entity has no shader.</returns>
        bool IsShaderEnabled(Entity entity);

        /// <summary>
        /// Sets a shader parameter value on an entity.
        /// </summary>
        /// <param name="entity">The entity with a shader component.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="value">The value to set (float, Vector2, Vector3, Vector4, or Color).</param>
        /// <exception cref="System.ArgumentException">Thrown if entity is not alive.</exception>
        /// <exception cref="System.ArgumentNullException">Thrown if paramName is null.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if entity has no ShaderComponent.</exception>
        void SetParameter(Entity entity, string paramName, object value);

        /// <summary>
        /// Gets a shader parameter value from an entity.
        /// </summary>
        /// <param name="entity">The entity with a shader component.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <returns>The parameter value, or null if not found.</returns>
        object? GetParameter(Entity entity, string paramName);

        /// <summary>
        /// Gets the shader ID from an entity's ShaderComponent.
        /// </summary>
        /// <param name="entity">The entity to check.</param>
        /// <returns>The shader ID, or null if entity has no shader.</returns>
        string? GetShaderId(Entity entity);
    }
}
