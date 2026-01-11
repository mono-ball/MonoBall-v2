using System;
using System.Collections.Generic;
using System.Linq;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Service implementation for accessing movement and animation profiles.
///     Loads profiles from mod definitions and provides fast lookups.
///     All methods fail-fast with clear exceptions if profiles or types are missing.
/// </summary>
public class ProfileService : IProfileService
{
    private readonly ILogger _logger;
    private readonly IModManager _modManager;
    private readonly Dictionary<string, MovementProfileDefinition> _movementProfiles = new();
    private readonly Dictionary<string, AnimationProfileDefinition> _animationProfiles = new();

    /// <summary>
    ///     Initializes a new instance of the ProfileService class.
    /// </summary>
    /// <param name="modManager">The mod manager to load profiles from. Must not be null.</param>
    /// <param name="logger">The logger instance. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if modManager or logger is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if profiles fail to load or validate.</exception>
    public ProfileService(IModManager modManager, ILogger logger)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        LoadProfiles();
    }

    private void LoadProfiles()
    {
        // Load movement profiles (same pattern as ConstantsService)
        // Note: Operation merging (Modify/Extend/Replace) is handled by ModLoader during definition loading via JsonElementMerger.
        // JsonElementMerger.Merge() recursively merges nested objects (including dictionaries), so:
        // - Modify operation: Recursively merges nested objects, overriding individual speed entries (e.g., overriding "walk" speed but keeping "run")
        // - Extend operation: Recursively merges nested objects, adding new speed entries (e.g., adding "bike") while preserving existing ones
        // - Replace operation: Completely replaces the entire profile (no merging)
        // Profile operations are fully supported - mods can use $operation: "Modify"/"Extend"/"Replace" to customize profiles.
        // By the time definitions reach ProfileService, they're already merged at JSON level.
        // So we just deserialize the final merged definitions.
        var movementProfileIds = _modManager.Registry.GetByType("MovementProfile").ToList();

        foreach (var profileId in movementProfileIds)
        {
            var profile = _modManager.GetDefinition<MovementProfileDefinition>(profileId);
            if (profile == null)
            {
                _logger.Warning("Failed to load movement profile '{ProfileId}'. Ensure JSON is valid.", profileId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                throw new InvalidOperationException(
                    $"Movement profile definition '{profileId}' has null or empty ID. All profiles must have valid IDs.");
            }

            // Store profile (if same ID appears multiple times, later mods override - already merged by ModLoader)
            _movementProfiles[profile.Id] = profile;
        }

        // Load animation profiles (same pattern)
        var animationProfileIds = _modManager.Registry.GetByType("AnimationProfile").ToList();

        foreach (var profileId in animationProfileIds)
        {
            var profile = _modManager.GetDefinition<AnimationProfileDefinition>(profileId);
            if (profile == null)
            {
                _logger.Warning("Failed to load animation profile '{ProfileId}'. Ensure JSON is valid.", profileId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                throw new InvalidOperationException(
                    $"Animation profile definition '{profileId}' has null or empty ID. All profiles must have valid IDs.");
            }

            // Store profile (if same ID appears multiple times, later mods override - already merged by ModLoader)
            _animationProfiles[profile.Id] = profile;
        }

        // Validate loaded profiles after loading (fail-fast if invalid)
        ValidateLoadedProfiles();

        _logger.Information(
            "Loaded {MovementCount} movement profiles and {AnimationCount} animation profiles",
            _movementProfiles.Count,
            _animationProfiles.Count
        );
    }

    private void ValidateLoadedProfiles()
    {
        // Validate movement profiles
        foreach (var (id, profile) in _movementProfiles)
        {
            if (profile.Speeds.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Movement profile '{id}' has no speeds. All profiles must have at least one speed type.");
            }

            if (string.IsNullOrWhiteSpace(profile.DefaultSpeed))
            {
                throw new InvalidOperationException(
                    $"Movement profile '{id}' has null or empty DefaultSpeed. All profiles must specify a default speed type.");
            }

            if (!profile.Speeds.ContainsKey(profile.DefaultSpeed))
            {
                throw new InvalidOperationException(
                    $"Movement profile '{id}' specifies DefaultSpeed '{profile.DefaultSpeed}', but this type doesn't exist in the profile. Available types: {string.Join(", ", profile.Speeds.Keys)}");
            }

            // Validate each speed has required fields
            foreach (var (type, speedDef) in profile.Speeds)
            {
                if (string.IsNullOrWhiteSpace(speedDef.AnimationType))
                {
                    throw new InvalidOperationException(
                        $"Movement profile '{id}' speed type '{type}' missing AnimationType. All speeds must specify which animation type to use.");
                }
            }
        }

        // Validate animation profiles
        foreach (var (id, profile) in _animationProfiles)
        {
            if (profile.Animations.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Animation profile '{id}' has no animations. All profiles must have at least one animation type.");
            }

            if (string.IsNullOrWhiteSpace(profile.DefaultAnimation))
            {
                throw new InvalidOperationException(
                    $"Animation profile '{id}' has null or empty DefaultAnimation. All profiles must specify a default animation type.");
            }

            if (!profile.Animations.ContainsKey(profile.DefaultAnimation))
            {
                throw new InvalidOperationException(
                    $"Animation profile '{id}' specifies DefaultAnimation '{profile.DefaultAnimation}', but this type doesn't exist in the profile. Available types: {string.Join(", ", profile.Animations.Keys)}");
            }

            // Validate each animation has required fields
            foreach (var (type, animDef) in profile.Animations)
            {
                if (animDef.Duration <= 0)
                {
                    throw new InvalidOperationException(
                        $"Animation profile '{id}' animation type '{type}' has invalid Duration ({animDef.Duration}). Must be positive (seconds).");
                }

                // Validate frameSequence if present
                if (animDef.FrameSequence != null)
                {
                    foreach (var duration in animDef.FrameSequence)
                    {
                        if (duration <= 0)
                        {
                            throw new InvalidOperationException(
                                $"Animation profile '{id}' animation type '{type}' has invalid frameSequence duration ({duration}). All durations must be positive (seconds).");
                        }
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public float GetMovementSpeed(string profileId, string movementType)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(movementType))
            throw new ArgumentException("Movement type cannot be null or empty.", nameof(movementType));

        if (!_movementProfiles.TryGetValue(profileId, out var movementProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Movement profile '{profileId}' not found. Available profiles: {string.Join(", ", _movementProfiles.Keys)}");
        }

        if (!movementProfile.Speeds.TryGetValue(movementType, out var speedDef))
        {
            var availableTypes = string.Join(", ", movementProfile.Speeds.Keys);
            throw new KeyNotFoundException(
                $"Movement type '{movementType}' not found in profile '{profileId}'. Available types: {availableTypes}");
        }

        return speedDef.Speed;
    }

    /// <inheritdoc />
    public string GetAnimationTypeForMovementType(string profileId, string movementType)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(movementType))
            throw new ArgumentException("Movement type cannot be null or empty.", nameof(movementType));

        if (!_movementProfiles.TryGetValue(profileId, out var movementProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Movement profile '{profileId}' not found. Available profiles: {string.Join(", ", _movementProfiles.Keys)}");
        }

        if (!movementProfile.Speeds.TryGetValue(movementType, out var speedDef))
        {
            var availableTypes = string.Join(", ", movementProfile.Speeds.Keys);
            throw new KeyNotFoundException(
                $"Movement type '{movementType}' not found in profile '{profileId}'. Available types: {availableTypes}");
        }

        return speedDef.AnimationType;
    }

    /// <inheritdoc />
    public string GetMovementTypeForSpeed(string profileId, float speed, float tolerance = 0.1f)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));

        if (!_movementProfiles.TryGetValue(profileId, out var movementProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Movement profile '{profileId}' not found. Available profiles: {string.Join(", ", _movementProfiles.Keys)}");
        }

        // Find movement type that matches current speed (within tolerance)
        foreach (var (type, speedDef) in movementProfile.Speeds)
        {
            if (Math.Abs(speedDef.Speed - speed) < tolerance)
                return type;
        }

        // No match found - return default movement type
        if (string.IsNullOrWhiteSpace(movementProfile.DefaultSpeed))
        {
            throw new InvalidOperationException(
                $"Movement profile '{profileId}' has null or empty DefaultSpeed. Cannot determine movement type for speed {speed}.");
        }

        if (!movementProfile.Speeds.ContainsKey(movementProfile.DefaultSpeed))
        {
            throw new InvalidOperationException(
                $"Movement profile '{profileId}' specifies DefaultSpeed '{movementProfile.DefaultSpeed}', but this type doesn't exist in the profile.");
        }

        return movementProfile.DefaultSpeed;
    }

    /// <inheritdoc />
    public float GetDefaultMovementSpeed(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));

        if (!_movementProfiles.TryGetValue(profileId, out var movementProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Movement profile '{profileId}' not found. Available profiles: {string.Join(", ", _movementProfiles.Keys)}");
        }

        if (string.IsNullOrWhiteSpace(movementProfile.DefaultSpeed))
        {
            throw new InvalidOperationException(
                $"Movement profile '{profileId}' has null or empty DefaultSpeed. All profiles must specify a default speed type.");
        }

        try
        {
            return GetMovementSpeed(profileId, movementProfile.DefaultSpeed);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidOperationException(
                $"Movement profile '{profileId}' specifies DefaultSpeed '{movementProfile.DefaultSpeed}', but this type doesn't exist in the profile.",
                ex);
        }
    }

    /// <inheritdoc />
    public double[] CalculateAnimationDurations(
        string profileId,
        string animationType,
        int frameCount,
        double[]? frameSequenceOverride = null
    )
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));
        if (string.IsNullOrWhiteSpace(animationType))
            throw new ArgumentException("Animation type cannot be null or empty.", nameof(animationType));
        if (frameCount <= 0)
            throw new ArgumentException("Frame count must be positive.", nameof(frameCount));

        if (!_animationProfiles.TryGetValue(profileId, out var animationProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Animation profile '{profileId}' not found. Available profiles: {string.Join(", ", _animationProfiles.Keys)}");
        }

        if (!animationProfile.Animations.TryGetValue(animationType, out var animDef))
        {
            var availableTypes = string.Join(", ", animationProfile.Animations.Keys);
            throw new KeyNotFoundException(
                $"Animation type '{animationType}' not found in profile '{profileId}'. Available types: {availableTypes}");
        }

        // Priority: frameSequenceOverride > profile.frameSequence > profile.duration
        var durations = new double[frameCount];

        // Check if animation definition has frameSequence override (in seconds)
        if (frameSequenceOverride != null && frameSequenceOverride.Length > 0)
        {
            if (frameSequenceOverride.Length != frameCount)
            {
                throw new InvalidOperationException(
                    $"FrameSequence override length ({frameSequenceOverride.Length}) doesn't match frameCount ({frameCount}) for animation type '{animationType}' in profile '{profileId}'.");
            }

            // frameSequenceOverride is already in seconds
            Array.Copy(frameSequenceOverride, durations, frameCount);
        }
        // Check if profile has frameSequence (in seconds)
        else if (animDef.FrameSequence != null && animDef.FrameSequence.Length > 0)
        {
            if (animDef.FrameSequence.Length != frameCount)
            {
                throw new InvalidOperationException(
                    $"Profile frameSequence length ({animDef.FrameSequence.Length}) doesn't match frameCount ({frameCount}) for animation type '{animationType}' in profile '{profileId}'. FrameSequence must have length matching frameCount.");
            }

            // profile.frameSequence is already in seconds
            Array.Copy(animDef.FrameSequence, durations, frameCount);
        }
        // Use duration for all frames (in seconds)
        else
        {
            // duration is already in seconds
            var baseDurationSeconds = animDef.Duration;

            for (var i = 0; i < frameCount; i++)
                durations[i] = baseDurationSeconds;
        }

        return durations;
    }

    /// <inheritdoc />
    public bool HasMovementProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return false;
        return _movementProfiles.ContainsKey(profileId);
    }

    /// <inheritdoc />
    public bool HasAnimationProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return false;
        return _animationProfiles.ContainsKey(profileId);
    }

    /// <inheritdoc />
    public MovementProfileDefinition GetMovementProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));

        if (!_movementProfiles.TryGetValue(profileId, out var movementProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Movement profile '{profileId}' not found. Available profiles: {string.Join(", ", _movementProfiles.Keys)}");
        }

        return movementProfile;
    }

    /// <inheritdoc />
    public AnimationProfileDefinition GetAnimationProfile(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            throw new ArgumentException("Profile ID cannot be null or empty.", nameof(profileId));

        if (!_animationProfiles.TryGetValue(profileId, out var animationProfile))
        {
            throw new ProfileNotFoundException(
                profileId,
                $"Animation profile '{profileId}' not found. Available profiles: {string.Join(", ", _animationProfiles.Keys)}");
        }

        return animationProfile;
    }
}
