using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     Helper class for managing animation state changes based on movement state.
///     This logic is kept separate from MovementSystem for organization, but must be called
///     atomically with movement state updates to prevent timing bugs.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why this is separate from SpriteAnimationSystem:</b>
///         Animation state changes must happen atomically with movement state changes to prevent
///         race conditions and frame delays. For example:
///         - When movement completes, we must check for next movement BEFORE switching to idle animation
///         - Turn-in-place must check animation completion to transition states correctly
///         - Walk animation must start immediately when movement begins
///     </para>
///     <para>
///         If this logic were in SpriteAnimationSystem (which runs after MovementSystem), there would
///         be a frame delay where movement state and animation state are out of sync, causing visual bugs.
///     </para>
///     <para>
///         This helper class improves code organization while maintaining the required atomicity.
///     </para>
/// </remarks>
internal static class MovementAnimationHelper
{
    /// <summary>
    ///     Updates animation state when movement completes.
    ///     Checks if there's a pending movement request to determine if idle animation should play.
    /// </summary>
    /// <param name="animation">The animation component to update.</param>
    /// <param name="movement">The movement component to check.</param>
    /// <param name="hasNextMovement">Whether there's a pending movement request.</param>
    public static void OnMovementComplete(
        ref SpriteAnimationComponent animation,
        ref GridMovement movement,
        bool hasNextMovement
    )
    {
        if (!hasNextMovement)
            // No more movement - switch to idle animation
            ChangeAnimation(
                ref animation,
                movement.FacingDirection.ToIdleAnimation(),
                false,
                false
            );
        // else: Keep walk animation playing - next movement will continue it
    }

    /// <summary>
    ///     Updates animation state during movement to ensure walk animation is playing.
    /// </summary>
    /// <param name="animation">The animation component to update.</param>
    /// <param name="movement">The movement component to check.</param>
    public static void OnMovementInProgress(
        ref SpriteAnimationComponent animation,
        ref GridMovement movement
    )
    {
        // Ensure walk animation is playing
        // Only change animation if switching from different animation
        var expectedAnimation = movement.FacingDirection.ToWalkAnimation();
        if (animation.CurrentAnimationName != expectedAnimation)
            ChangeAnimation(ref animation, expectedAnimation);
    }

    /// <summary>
    ///     Updates animation state for turn-in-place behavior (Pokemon Emerald style).
    /// </summary>
    /// <param name="animation">The animation component to update.</param>
    /// <param name="movement">The movement component to check.</param>
    /// <returns>True if turn-in-place animation has completed, false otherwise.</returns>
    public static bool OnTurnInPlace(
        ref SpriteAnimationComponent animation,
        ref GridMovement movement
    )
    {
        // Play turn animation with PlayOnce=true
        var turnAnimation = movement.FacingDirection.ToTurnAnimation();
        if (animation.CurrentAnimationName != turnAnimation || !animation.PlayOnce)
            ChangeAnimation(ref animation, turnAnimation, true, true);

        // Check if turn animation has completed
        if (animation.IsComplete)
        {
            // Transition to idle animation
            ChangeAnimation(ref animation, movement.FacingDirection.ToIdleAnimation());
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Ensures idle animation is playing when not moving and not turning.
    /// </summary>
    /// <param name="animation">The animation component to update.</param>
    /// <param name="movement">The movement component to check.</param>
    public static void OnIdle(ref SpriteAnimationComponent animation, ref GridMovement movement)
    {
        // Not turning - ensure idle animation is playing
        var expectedAnimation = movement.FacingDirection.ToIdleAnimation();
        if (animation.CurrentAnimationName != expectedAnimation)
            ChangeAnimation(ref animation, expectedAnimation);
    }

    /// <summary>
    ///     Changes the animation for an entity's SpriteAnimationComponent.
    ///     Handles animation state changes directly (components are pure data).
    /// </summary>
    /// <param name="animation">The animation component to modify.</param>
    /// <param name="animationName">The new animation name.</param>
    /// <param name="forceRestart">Whether to restart even if already playing this animation.</param>
    /// <param name="playOnce">Whether to play the animation once (ignoring manifest Loop setting).</param>
    private static void ChangeAnimation(
        ref SpriteAnimationComponent animation,
        string animationName,
        bool forceRestart = false,
        bool playOnce = false
    )
    {
        if (animation.CurrentAnimationName != animationName || forceRestart)
        {
            animation.CurrentAnimationName = animationName;
            animation.CurrentAnimationFrameIndex = 0;
            animation.ElapsedTime = 0f;
            animation.IsPlaying = true;
            animation.IsComplete = false;
            animation.PlayOnce = playOnce;
            animation.TriggeredEventFrames = 0;
        }
        else if (playOnce && !animation.PlayOnce)
        {
            // Same animation but switching to PlayOnce mode - don't reset frame
            animation.PlayOnce = true;
        }
    }
}
