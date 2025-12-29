namespace MonoBall.Core.ECS;

/// <summary>
///     Interface for systems that have an execution priority.
/// </summary>
public interface IPrioritizedSystem
{
    /// <summary>
    ///     Gets the execution priority. Lower values execute first.
    /// </summary>
    int Priority { get; }
}
