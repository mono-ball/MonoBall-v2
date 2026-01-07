using System;

namespace MonoBall.Core.ECS.Systems.Animation;

/// <summary>
///     Bundle implementation for animation and visibility systems.
///     Owns and disposes all animation systems.
/// </summary>
public sealed class AnimationSystems : IAnimationSystems
{
    // Concrete types for disposal
    private readonly SpriteAnimationSystem? _spriteAnimationSystemConcrete;
    private readonly SpriteSheetSystem? _spriteSheetSystemConcrete;
    private readonly VisibilityFlagSystem? _visibilityFlagSystemConcrete;
    private bool _isDisposed;

    /// <summary>
    ///     Initializes a new instance of the AnimationSystems bundle.
    /// </summary>
    public AnimationSystems(
        AnimatedTileSystem? animatedTileSystem,
        SpriteAnimationSystem? spriteAnimationSystem,
        SpriteSheetSystem? spriteSheetSystem,
        VisibilityFlagSystem? visibilityFlagSystem,
        PerformanceStatsSystem? performanceStatsSystem
    )
    {
        // Store concrete types for disposal
        _spriteAnimationSystemConcrete = spriteAnimationSystem;
        _spriteSheetSystemConcrete = spriteSheetSystem;
        _visibilityFlagSystemConcrete = visibilityFlagSystem;

        // Expose as interfaces
        AnimatedTileSystem = animatedTileSystem;
        SpriteAnimationSystem = spriteAnimationSystem;
        SpriteSheetSystem = spriteSheetSystem;
        VisibilityFlagSystem = visibilityFlagSystem;
        PerformanceStatsSystem = performanceStatsSystem;
        IsAvailable = animatedTileSystem != null;
    }

    /// <inheritdoc />
    public IAnimatedTileSystem? AnimatedTileSystem { get; }

    /// <inheritdoc />
    public ISpriteAnimationSystem? SpriteAnimationSystem { get; }

    /// <inheritdoc />
    public ISpriteSheetSystem? SpriteSheetSystem { get; }

    /// <inheritdoc />
    public IVisibilityFlagSystem? VisibilityFlagSystem { get; }

    /// <inheritdoc />
    public IPerformanceStatsSystem? PerformanceStatsSystem { get; }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;

        // Dispose systems that implement IDisposable
        _visibilityFlagSystemConcrete?.Dispose();
        _spriteSheetSystemConcrete?.Dispose();
        _spriteAnimationSystemConcrete?.Dispose();
        // AnimatedTileSystem and PerformanceStatsSystem don't implement IDisposable
    }
}
