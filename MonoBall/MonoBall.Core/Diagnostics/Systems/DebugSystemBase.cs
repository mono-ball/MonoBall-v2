namespace MonoBall.Core.Diagnostics.Systems;

using System;
using Arch.Core;
using Arch.System;

/// <summary>
/// Base class for debug systems with standard disposal pattern.
/// </summary>
public abstract class DebugSystemBase : BaseSystem<World, float>, IDisposable
{
    private bool _disposed;

    /// <summary>
    /// Initializes a new debug system.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <exception cref="ArgumentNullException">Thrown when world is null.</exception>
    protected DebugSystemBase(World world)
        : base(world)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    /// Disposes the system and its resources.
    /// </summary>
    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing">True if called from Dispose(), false if from finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            DisposeManagedResources();
        }
        _disposed = true;
    }

    /// <summary>
    /// Override to unsubscribe from events and dispose managed resources.
    /// </summary>
    protected virtual void DisposeManagedResources() { }

    /// <summary>
    /// Throws if this system has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when system is disposed.</exception>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>
    /// Gets whether this system has been disposed.
    /// </summary>
    protected bool IsDisposed => _disposed;
}
