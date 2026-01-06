using System;

namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Bundle interface for animation and visibility systems.
///     Exposes interfaces, not concrete types, for proper dependency inversion.
/// </summary>
public interface IAnimationSystems : IDisposable
{
    /// <summary>
    ///     Gets the animated tile system.
    /// </summary>
    IAnimatedTileSystem? AnimatedTileSystem { get; }

    /// <summary>
    ///     Gets the sprite animation system.
    /// </summary>
    ISpriteAnimationSystem? SpriteAnimationSystem { get; }

    /// <summary>
    ///     Gets the sprite sheet system.
    /// </summary>
    ISpriteSheetSystem? SpriteSheetSystem { get; }

    /// <summary>
    ///     Gets the visibility flag system.
    /// </summary>
    IVisibilityFlagSystem? VisibilityFlagSystem { get; }

    /// <summary>
    ///     Gets the performance stats system.
    /// </summary>
    IPerformanceStatsSystem? PerformanceStatsSystem { get; }

    /// <summary>
    ///     Gets whether animation systems are available.
    /// </summary>
    bool IsAvailable { get; }
}
