using System;
using MonoBall.Core.ECS.Systems;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Bundle interface for rendering systems.
/// </summary>
public interface IRenderingSystems : IDisposable
{
    /// <summary>
    ///     Gets the elevation renderer system (unified renderer for all map content).
    /// </summary>
    ElevationRendererSystem? ElevationRendererSystem { get; }

    /// <summary>
    ///     Gets the map border renderer system.
    /// </summary>
    MapBorderRendererSystem? MapBorderRendererSystem { get; }

    /// <summary>
    ///     Gets whether rendering systems are available.
    /// </summary>
    bool IsAvailable { get; }
}
