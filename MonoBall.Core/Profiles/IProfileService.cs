namespace MonoBall.Core.Profiles;

/// <summary>
///     Service for accessing movement and animation profiles.
///     All methods fail-fast with exceptions if profiles or types are missing.
/// </summary>
public interface IProfileService
{
    /// <summary>
    ///     Gets a movement speed for a specific movement type from a profile.
    /// </summary>
    /// <param name="profileId">The movement profile ID (e.g., "pokeemerald:profile:movement/player"). Must not be null or empty.</param>
    /// <param name="movementType">The movement type (e.g., "walk", "run", "bike"). Must not be null or empty.</param>
    /// <returns>The movement speed in tiles per second.</returns>
    /// <exception cref="System.ArgumentNullException">If profileId or movementType is null or empty.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the movement type doesn't exist in the profile.</exception>
    float GetMovementSpeed(string profileId, string movementType);

    /// <summary>
    ///     Gets the animation type for a specific movement type from a profile.
    /// </summary>
    /// <param name="profileId">The movement profile ID. Must not be null or empty.</param>
    /// <param name="movementType">The movement type (e.g., "walk", "run", "bike"). Must not be null or empty.</param>
    /// <returns>The animation type (e.g., "go", "go_fast", "run").</returns>
    /// <exception cref="System.ArgumentNullException">If profileId or movementType is null or empty.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the movement type doesn't exist in the profile.</exception>
    string GetAnimationTypeForMovementType(string profileId, string movementType);

    /// <summary>
    ///     Gets the movement type that matches a given speed (within tolerance).
    ///     Used to determine CurrentMovementType from current MovementSpeed.
    /// </summary>
    /// <param name="profileId">The movement profile ID. Must not be null or empty.</param>
    /// <param name="speed">The current movement speed in tiles per second.</param>
    /// <param name="tolerance">Tolerance for speed matching (default: 0.1 tiles/sec).</param>
    /// <returns>The movement type that matches the speed, or the default movement type if no match found.</returns>
    /// <exception cref="System.ArgumentNullException">If profileId is null or empty.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    string GetMovementTypeForSpeed(string profileId, float speed, float tolerance = 0.1f);

    /// <summary>
    ///     Gets the default movement speed from a profile.
    /// </summary>
    /// <param name="profileId">The movement profile ID. Must not be null or empty.</param>
    /// <returns>The default movement speed in tiles per second.</returns>
    /// <exception cref="System.ArgumentNullException">If profileId is null or empty.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    /// <exception cref="System.InvalidOperationException">If the profile's defaultSpeed type doesn't exist in the profile.</exception>
    float GetDefaultMovementSpeed(string profileId);

    /// <summary>
    ///     Calculates animation frame durations for a specific animation type from a profile.
    ///     This method is called at sprite load time to pre-calculate durations (not during animation playback).
    /// </summary>
    /// <param name="profileId">The animation profile ID (e.g., "pokeemerald:profile:animation/standard"). Must not be null or empty.</param>
    /// <param name="animationType">The animation type (e.g., "face", "go", "go_fast", "run"). Must not be null or empty.</param>
    /// <param name="frameCount">The number of frames in the animation sequence. Must be positive.</param>
    /// <param name="frameSequenceOverride">Optional per-frame durations in seconds from animation definition (overrides profile). If null, uses profile's frameSequence or duration.</param>
    /// <returns>Array of frame durations in seconds. Length matches frameCount.</returns>
    /// <exception cref="System.ArgumentNullException">If profileId or animationType is null or empty.</exception>
    /// <exception cref="System.ArgumentException">If frameCount is not positive.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">If the animation type doesn't exist in the profile.</exception>
    double[] CalculateAnimationDurations(
        string profileId,
        string animationType,
        int frameCount,
        double[]? frameSequenceOverride = null
    );

    /// <summary>
    ///     Checks if a movement profile exists.
    /// </summary>
    /// <param name="profileId">The movement profile ID to check.</param>
    /// <returns>True if the profile exists, false otherwise.</returns>
    bool HasMovementProfile(string profileId);

    /// <summary>
    ///     Checks if an animation profile exists.
    /// </summary>
    /// <param name="profileId">The animation profile ID to check.</param>
    /// <returns>True if the profile exists, false otherwise.</returns>
    bool HasAnimationProfile(string profileId);

    /// <summary>
    ///     Gets a movement profile definition by ID.
    ///     Used for advanced operations like profile merging or validation.
    /// </summary>
    /// <param name="profileId">The movement profile ID.</param>
    /// <returns>The movement profile definition.</returns>
    /// <exception cref="System.ArgumentNullException">If profileId is null or empty.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    MovementProfileDefinition GetMovementProfile(string profileId);

    /// <summary>
    ///     Gets an animation profile definition by ID.
    ///     Used for advanced operations like profile merging or validation.
    /// </summary>
    /// <param name="profileId">The animation profile ID.</param>
    /// <returns>The animation profile definition.</returns>
    /// <exception cref="System.ArgumentNullException">If profileId is null or empty.</exception>
    /// <exception cref="ProfileNotFoundException">If the profile doesn't exist.</exception>
    AnimationProfileDefinition GetAnimationProfile(string profileId);
}
