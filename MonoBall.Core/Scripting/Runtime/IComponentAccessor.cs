namespace MonoBall.Core.Scripting.Runtime;

/// <summary>
///     Interface for accessing components on the attached entity.
///     Provides read/write access to ECS components.
/// </summary>
public interface IComponentAccessor
{
    /// <summary>
    ///     Gets a component value from the attached entity.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>The component value.</returns>
    /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
    T GetComponent<T>()
        where T : struct;

    /// <summary>
    ///     Sets a component value on the attached entity.
    ///     Adds the component if it doesn't exist, updates it if it does.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="component">The component value to set.</param>
    /// <exception cref="InvalidOperationException">Thrown if this is a plugin script (no entity).</exception>
    void SetComponent<T>(T component)
        where T : struct;

    /// <summary>
    ///     Checks if the attached entity has a component.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <returns>True if the entity has the component, false otherwise.</returns>
    bool HasComponent<T>()
        where T : struct;
}
