using Arch.Core;

namespace MonoBall.Core.Scripting.Runtime;

/// <summary>
///     Interface for querying entities in the ECS world.
///     Provides methods to find and iterate over entities with specific component combinations.
/// </summary>
public interface IEntityQuery
{
    /// <summary>
    ///     Delegate type for querying entities with a single component.
    /// </summary>
    /// <typeparam name="T1">The component type.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="component1">The component reference.</param>
    delegate void QueryAction<T1>(Entity entity, ref T1 component1)
        where T1 : struct;

    /// <summary>
    ///     Delegate type for querying entities with two components.
    /// </summary>
    /// <typeparam name="T1">The first component type.</typeparam>
    /// <typeparam name="T2">The second component type.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="component1">The first component reference.</param>
    /// <param name="component2">The second component reference.</param>
    delegate void QueryAction<T1, T2>(Entity entity, ref T1 component1, ref T2 component2)
        where T1 : struct
        where T2 : struct;

    /// <summary>
    ///     Delegate type for querying entities with three components.
    /// </summary>
    /// <typeparam name="T1">The first component type.</typeparam>
    /// <typeparam name="T2">The second component type.</typeparam>
    /// <typeparam name="T3">The third component type.</typeparam>
    /// <param name="entity">The entity.</param>
    /// <param name="component1">The first component reference.</param>
    /// <param name="component2">The second component reference.</param>
    /// <param name="component3">The third component reference.</param>
    delegate void QueryAction<T1, T2, T3>(
        Entity entity,
        ref T1 component1,
        ref T2 component2,
        ref T3 component3
    )
        where T1 : struct
        where T2 : struct
        where T3 : struct;

    /// <summary>
    ///     Queries entities with a single component type.
    /// </summary>
    /// <typeparam name="T1">The component type.</typeparam>
    /// <param name="action">The action to execute for each matching entity.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if action is null.</exception>
    void Query<T1>(QueryAction<T1> action)
        where T1 : struct;

    /// <summary>
    ///     Queries entities with multiple components.
    /// </summary>
    /// <typeparam name="T1">The first component type.</typeparam>
    /// <typeparam name="T2">The second component type.</typeparam>
    /// <param name="action">The action to execute for each matching entity.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if action is null.</exception>
    void Query<T1, T2>(QueryAction<T1, T2> action)
        where T1 : struct
        where T2 : struct;

    /// <summary>
    ///     Queries entities with three components.
    /// </summary>
    /// <typeparam name="T1">The first component type.</typeparam>
    /// <typeparam name="T2">The second component type.</typeparam>
    /// <typeparam name="T3">The third component type.</typeparam>
    /// <param name="action">The action to execute for each matching entity.</param>
    /// <exception cref="System.ArgumentNullException">Thrown if action is null.</exception>
    void Query<T1, T2, T3>(QueryAction<T1, T2, T3> action)
        where T1 : struct
        where T2 : struct
        where T3 : struct;
}
