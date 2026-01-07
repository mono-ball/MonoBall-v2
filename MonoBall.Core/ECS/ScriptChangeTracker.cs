namespace MonoBall.Core.ECS;

/// <summary>
///     Tracks when script attachments change to optimize ScriptLifecycleSystem queries.
///     Uses a dirty flag pattern to avoid querying every frame when scripts haven't changed.
/// </summary>
public static class ScriptChangeTracker
{
    /// <summary>
    ///     Dirty flag indicating script attachments have changed.
    ///     Starts as true to ensure first Update() call processes existing entities
    ///     (entities created before system initialization won't trigger EntityCreatedEvent).
    /// </summary>
    private static volatile bool _isDirty = true;

    /// <summary>
    ///     Marks that script attachments have changed and need processing.
    /// </summary>
    public static void MarkDirty()
    {
        _isDirty = true;
    }

    /// <summary>
    ///     Checks if script attachments have changed since last check.
    /// </summary>
    /// <returns>True if dirty, false if clean.</returns>
    public static bool IsDirty()
    {
        return _isDirty;
    }

    /// <summary>
    ///     Marks script attachments as clean (no changes).
    /// </summary>
    public static void MarkClean()
    {
        _isDirty = false;
    }

    /// <summary>
    ///     Resets the tracker to its initial state (dirty=true).
    ///     Call this during scene transitions or in tests to ensure scripts are re-processed.
    /// </summary>
    public static void Reset()
    {
        _isDirty = true;
    }
}
