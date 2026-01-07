using System;
using MonoBall.Core.ECS.Systems;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Bundle implementation for rendering systems.
///     Owns and disposes all rendering systems.
/// </summary>
public sealed class RenderingSystems : IRenderingSystems
{
    private bool _isDisposed;

    /// <summary>
    ///     Initializes a new instance of the RenderingSystems bundle.
    /// </summary>
    public RenderingSystems(
        ElevationRendererSystem? elevationRendererSystem,
        MapBorderRendererSystem? mapBorderRendererSystem
    )
    {
        ElevationRendererSystem = elevationRendererSystem;
        MapBorderRendererSystem = mapBorderRendererSystem;
        IsAvailable = elevationRendererSystem != null;
    }

    /// <inheritdoc />
    public ElevationRendererSystem? ElevationRendererSystem { get; }

    /// <inheritdoc />
    public MapBorderRendererSystem? MapBorderRendererSystem { get; }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        // ElevationRendererSystem and MapBorderRendererSystem don't implement IDisposable
        // They're managed by the graphics device lifecycle
    }
}
