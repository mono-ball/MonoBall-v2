using System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Utilities;

/// <summary>
///     Shared utility methods for shader animation easing and interpolation.
///     Extracted for DRY - used by ShaderParameterAnimationSystem,
///     ShaderMultiParameterAnimationSystem, and ShaderAnimationChainSystem.
/// </summary>
public static class ShaderAnimationUtilities
{
    /// <summary>
    ///     Applies an easing function to a linear progress value.
    /// </summary>
    /// <param name="t">Linear progress from 0.0 to 1.0.</param>
    /// <param name="easing">The easing function to apply.</param>
    /// <returns>The eased progress value.</returns>
    public static float ApplyEasing(float t, EasingFunction easing)
    {
        return easing switch
        {
            EasingFunction.Linear => t,
            EasingFunction.EaseIn => t * t,
            EasingFunction.EaseOut => 1.0f - (1.0f - t) * (1.0f - t),
            EasingFunction.EaseInOut => t < 0.5f
                ? 2.0f * t * t
                : 1.0f - MathF.Pow(-2.0f * t + 2.0f, 2.0f) / 2.0f,
            EasingFunction.SmoothStep => t * t * (3.0f - 2.0f * t),
            _ => t,
        };
    }

    /// <summary>
    ///     Interpolates between two values based on progress.
    ///     Supports float, Vector2, Vector3, Vector4, and Color types.
    /// </summary>
    /// <param name="startValue">The starting value.</param>
    /// <param name="endValue">The ending value.</param>
    /// <param name="t">Progress from 0.0 to 1.0.</param>
    /// <returns>The interpolated value, or null if types are not supported.</returns>
    public static object? Interpolate(object startValue, object endValue, float t)
    {
        // Clamp t to [0, 1]
        t = Math.Clamp(t, 0.0f, 1.0f);

        return (startValue, endValue) switch
        {
            (float start, float end) => MathHelper.Lerp(start, end, t),
            (Vector2 start, Vector2 end) => Vector2.Lerp(start, end, t),
            (Vector3 start, Vector3 end) => Vector3.Lerp(start, end, t),
            (Vector4 start, Vector4 end) => Vector4.Lerp(start, end, t),
            (Color start, Color end) => Color.Lerp(start, end, t),
            _ => null,
        };
    }

    /// <summary>
    ///     Calculates animation progress for a given elapsed time and duration.
    ///     Handles looping, ping-pong, and non-looping cases.
    /// </summary>
    /// <param name="elapsedTime">Current elapsed time (may be modified for looping).</param>
    /// <param name="duration">Animation duration in seconds.</param>
    /// <param name="isLooping">Whether the animation loops.</param>
    /// <param name="pingPong">Whether to ping-pong (requires isLooping).</param>
    /// <param name="isComplete">Output: true if animation completed (non-looping only).</param>
    /// <returns>Progress value from 0.0 to 1.0.</returns>
    public static float CalculateProgress(
        ref float elapsedTime,
        float duration,
        bool isLooping,
        bool pingPong,
        out bool isComplete
    )
    {
        isComplete = false;

        if (duration <= 0)
        {
            isComplete = true;
            return 1.0f;
        }

        if (pingPong)
        {
            // Ping-pong: full cycle is Duration * 2 (forward then back)
            var cycleDuration = duration * 2.0f;
            elapsedTime = elapsedTime % cycleDuration;
            var cycleTime = elapsedTime;

            if (cycleTime > duration)
                // Second half: reverse direction (from 1.0 back to 0.0)
                return 2.0f - cycleTime / duration;

            // First half: forward direction (from 0.0 to 1.0)
            return cycleTime / duration;
        }

        if (isLooping)
        {
            // Looping animation: wrap elapsed time using modulo
            elapsedTime = elapsedTime % duration;
            return elapsedTime / duration;
        }

        // Non-looping animation: clamp to duration
        if (elapsedTime >= duration)
        {
            elapsedTime = duration;
            isComplete = true;
            return 1.0f;
        }

        return elapsedTime / duration;
    }

    /// <summary>
    ///     Updates an animation and calculates the interpolated value.
    /// </summary>
    /// <param name="animation">The animation data to update.</param>
    /// <param name="deltaTime">Delta time in seconds.</param>
    /// <param name="interpolatedValue">Output: the interpolated value.</param>
    /// <param name="isComplete">Output: true if animation completed (non-looping only).</param>
    /// <returns>True if interpolation succeeded, false if types not supported.</returns>
    public static bool UpdateAnimation(
        ref ShaderAnimationData animation,
        float deltaTime,
        out object? interpolatedValue,
        out bool isComplete
    )
    {
        // Use local variable since properties can't be passed by ref
        var elapsedTime = animation.ElapsedTime + deltaTime;

        var progress = CalculateProgress(
            ref elapsedTime,
            animation.Duration,
            animation.IsLooping,
            animation.PingPong,
            out isComplete
        );

        // Write back the potentially modified elapsed time
        animation.ElapsedTime = elapsedTime;

        var easedProgress = ApplyEasing(progress, animation.Easing);

        interpolatedValue = Interpolate(animation.StartValue, animation.EndValue, easedProgress);

        return interpolatedValue != null;
    }
}
