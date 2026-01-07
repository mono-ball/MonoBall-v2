using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.UI.Windows.Animations;

/// <summary>
///     Helper methods for creating common window animation configurations.
/// </summary>
public static class WindowAnimationHelper
{
    /// <summary>
    ///     Validates that a float value is positive and finite.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <param name="description">Description of what the value represents.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not positive and finite.</exception>
    private static void ValidatePositiveFinite(float value, string paramName, string description)
    {
        if (value <= 0f || !float.IsFinite(value))
            throw new ArgumentOutOfRangeException(
                paramName,
                $"{description} must be positive and finite."
            );
    }

    /// <summary>
    ///     Validates that a float value is non-negative and finite.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="paramName">The parameter name for the exception.</param>
    /// <param name="description">Description of what the value represents.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is not non-negative and finite.</exception>
    private static void ValidateNonNegativeFinite(float value, string paramName, string description)
    {
        if (value < 0f || !float.IsFinite(value))
            throw new ArgumentOutOfRangeException(
                paramName,
                $"{description} must be non-negative and finite."
            );
    }

    /// <summary>
    ///     Creates a slide down → pause → slide up animation (for map popups).
    /// </summary>
    /// <param name="slideDownDuration">Duration of slide down phase in seconds. Must be positive and finite.</param>
    /// <param name="pauseDuration">Duration of pause phase in seconds. Must be non-negative and finite.</param>
    /// <param name="slideUpDuration">Duration of slide up phase in seconds. Must be positive and finite.</param>
    /// <param name="windowHeight">Height of the window in pixels. Must be positive and finite.</param>
    /// <param name="destroyOnComplete">Whether to destroy the window when animation completes.</param>
    /// <returns>A configured WindowAnimationConfig for slide down/up animation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="slideDownDuration" /> is not positive and finite.
    ///     Thrown when <paramref name="pauseDuration" /> is not non-negative and finite.
    ///     Thrown when <paramref name="slideUpDuration" /> is not positive and finite.
    ///     Thrown when <paramref name="windowHeight" /> is not positive and finite.
    /// </exception>
    public static WindowAnimationConfig CreateSlideDownUpAnimation(
        float slideDownDuration,
        float pauseDuration,
        float slideUpDuration,
        float windowHeight,
        bool destroyOnComplete = true
    )
    {
        ValidatePositiveFinite(slideDownDuration, nameof(slideDownDuration), "Slide down duration");
        ValidateNonNegativeFinite(pauseDuration, nameof(pauseDuration), "Pause duration");
        ValidatePositiveFinite(slideUpDuration, nameof(slideUpDuration), "Slide up duration");
        ValidatePositiveFinite(windowHeight, nameof(windowHeight), "Window height");

        return new WindowAnimationConfig
        {
            Phases = new List<WindowAnimationPhase>
            {
                new()
                {
                    Type = WindowAnimationType.Slide,
                    Duration = slideDownDuration,
                    Easing = WindowEasingType.EaseOutCubic,
                    StartValue = new Vector3(0, -windowHeight, 0),
                    EndValue = new Vector3(0, 0, 0),
                },
                new()
                {
                    Type = WindowAnimationType.Pause,
                    Duration = pauseDuration,
                    Easing = WindowEasingType.Linear,
                    StartValue = new Vector3(0, 0, 0),
                    EndValue = new Vector3(0, 0, 0),
                },
                new()
                {
                    Type = WindowAnimationType.Slide,
                    Duration = slideUpDuration,
                    Easing = WindowEasingType.EaseInCubic,
                    StartValue = new Vector3(0, 0, 0),
                    EndValue = new Vector3(0, -windowHeight, 0),
                },
            },
            WindowSize = new Vector2(0, windowHeight),
            InitialPosition = Vector2.Zero,
            Loop = false,
            DestroyOnComplete = destroyOnComplete,
        };
    }

    /// <summary>
    ///     Creates a fade in animation.
    /// </summary>
    /// <param name="duration">Duration of fade in phase in seconds. Must be positive and finite.</param>
    /// <returns>A configured WindowAnimationConfig for fade in animation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <paramref name="duration" /> is not positive and finite.
    /// </exception>
    public static WindowAnimationConfig CreateFadeInAnimation(float duration)
    {
        ValidatePositiveFinite(duration, nameof(duration), "Duration");

        return new WindowAnimationConfig
        {
            Phases = new List<WindowAnimationPhase>
            {
                new()
                {
                    Type = WindowAnimationType.Fade,
                    Duration = duration,
                    Easing = WindowEasingType.EaseOut,
                    StartValue = new Vector3(0, 0, 0f),
                    EndValue = new Vector3(0, 0, 1f),
                },
            },
            WindowSize = Vector2.Zero,
            InitialPosition = Vector2.Zero,
            Loop = false,
            DestroyOnComplete = false,
        };
    }
}
