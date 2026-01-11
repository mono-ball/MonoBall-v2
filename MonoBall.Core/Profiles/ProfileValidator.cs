using System;
using System.Collections.Generic;
using System.Linq;
using MonoBall.Core.Mods;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Validates profile definitions for structure and cross-profile references.
///     Used during mod loading validation to catch profile errors early.
/// </summary>
public class ProfileValidator
{
    /// <summary>
    ///     Validates a movement profile definition for structural issues.
    /// </summary>
    /// <param name="profile">The movement profile to validate.</param>
    /// <returns>List of validation issues found. Empty if profile is valid.</returns>
    public List<ValidationIssue> ValidateMovementProfile(MovementProfileDefinition profile)
    {
        var issues = new List<ValidationIssue>();

        if (profile == null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Movement profile definition is null."
            });
            return issues;
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Movement profile ID is required. All profiles must have valid IDs."
            });
        }

        if (profile.Speeds == null || profile.Speeds.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Movement profile '{profile.Id}' has no speeds. All profiles must have at least one speed type."
            });
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultSpeed))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Movement profile '{profile.Id}' has null or empty DefaultSpeed. All profiles must specify a default speed type."
            });
        }
        else if (profile.Speeds != null && !profile.Speeds.ContainsKey(profile.DefaultSpeed))
        {
            var availableTypes = string.Join(", ", profile.Speeds.Keys);
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Movement profile '{profile.Id}' specifies DefaultSpeed '{profile.DefaultSpeed}', but this type doesn't exist in the profile. Available types: {availableTypes}"
            });
        }

        // Validate each speed has required fields
        if (profile.Speeds != null)
        {
            foreach (var (type, speedDef) in profile.Speeds)
            {
                if (speedDef == null)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Movement profile '{profile.Id}' speed type '{type}' has null SpeedDefinition."
                    });
                    continue;
                }

                if (speedDef.Speed <= 0)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Movement profile '{profile.Id}' speed type '{type}' has invalid speed ({speedDef.Speed}). Speed must be positive."
                    });
                }

                if (speedDef.Speed < 0.1f || speedDef.Speed > 100.0f)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Movement profile '{profile.Id}' speed type '{type}' has speed ({speedDef.Speed}) outside recommended range (0.1-100.0 tiles/sec)."
                    });
                }

                if (string.IsNullOrWhiteSpace(speedDef.AnimationType))
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Movement profile '{profile.Id}' speed type '{type}' missing AnimationType. All speeds must specify which animation type to use."
                    });
                }
            }
        }

        return issues;
    }

    /// <summary>
    ///     Validates an animation profile definition for structural issues.
    /// </summary>
    /// <param name="profile">The animation profile to validate.</param>
    /// <returns>List of validation issues found. Empty if profile is valid.</returns>
    public List<ValidationIssue> ValidateAnimationProfile(AnimationProfileDefinition profile)
    {
        var issues = new List<ValidationIssue>();

        if (profile == null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Animation profile definition is null."
            });
            return issues;
        }

        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Animation profile ID is required. All profiles must have valid IDs."
            });
        }

        if (profile.Animations == null || profile.Animations.Count == 0)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Animation profile '{profile.Id}' has no animations. All profiles must have at least one animation type."
            });
        }

        if (string.IsNullOrWhiteSpace(profile.DefaultAnimation))
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Animation profile '{profile.Id}' has null or empty DefaultAnimation. All profiles must specify a default animation type."
            });
        }
        else if (profile.Animations != null && !profile.Animations.ContainsKey(profile.DefaultAnimation))
        {
            var availableTypes = string.Join(", ", profile.Animations.Keys);
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = $"Animation profile '{profile.Id}' specifies DefaultAnimation '{profile.DefaultAnimation}', but this type doesn't exist in the profile. Available types: {availableTypes}"
            });
        }

        // Validate each animation has required fields
        if (profile.Animations != null)
        {
            foreach (var (type, animDef) in profile.Animations)
            {
                if (animDef == null)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Animation profile '{profile.Id}' animation type '{type}' has null AnimationDefinition."
                    });
                    continue;
                }

                if (animDef.Duration <= 0)
                {
                    issues.Add(new ValidationIssue
                    {
                        Severity = ValidationSeverity.Error,
                        Message = $"Animation profile '{profile.Id}' animation type '{type}' has invalid Duration ({animDef.Duration}). Must be positive (seconds)."
                    });
                }

                // Validate frameSequence if present
                if (animDef.FrameSequence != null)
                {
                    foreach (var duration in animDef.FrameSequence)
                    {
                        if (duration <= 0)
                        {
                            issues.Add(new ValidationIssue
                            {
                                Severity = ValidationSeverity.Error,
                                Message = $"Animation profile '{profile.Id}' animation type '{type}' has invalid frameSequence duration ({duration}). All durations must be positive (seconds)."
                            });
                        }
                    }
                }
            }
        }

        return issues;
    }

    /// <summary>
    ///     Validates cross-profile references between movement and animation profiles.
    ///     Checks that all animation types referenced by movement profile exist in animation profile.
    /// </summary>
    /// <param name="movementProfile">The movement profile to validate.</param>
    /// <param name="animationProfile">The animation profile to validate against.</param>
    /// <returns>List of validation issues found. Empty if profiles are compatible.</returns>
    public List<ValidationIssue> ValidateCrossProfileReferences(
        MovementProfileDefinition movementProfile,
        AnimationProfileDefinition animationProfile
    )
    {
        var issues = new List<ValidationIssue>();

        if (movementProfile == null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Movement profile is null. Cannot validate cross-profile references."
            });
            return issues;
        }

        if (animationProfile == null)
        {
            issues.Add(new ValidationIssue
            {
                Severity = ValidationSeverity.Error,
                Message = "Animation profile is null. Cannot validate cross-profile references."
            });
            return issues;
        }

        if (movementProfile.Speeds == null)
            return issues; // Already validated in ValidateMovementProfile

        if (animationProfile.Animations == null)
            return issues; // Already validated in ValidateAnimationProfile

        // Check that all animation types referenced by movement profile exist in animation profile
        foreach (var (movementType, speedDef) in movementProfile.Speeds)
        {
            if (speedDef == null || string.IsNullOrWhiteSpace(speedDef.AnimationType))
                continue; // Already validated in ValidateMovementProfile

            if (!animationProfile.Animations.ContainsKey(speedDef.AnimationType))
            {
                var availableAnimationTypes = string.Join(", ", animationProfile.Animations.Keys);
                issues.Add(new ValidationIssue
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Movement profile '{movementProfile.Id}' references animation type '{speedDef.AnimationType}' " +
                              $"for movement type '{movementType}', but this animation type doesn't exist in animation profile '{animationProfile.Id}'. " +
                              $"Available animation types in profile: {availableAnimationTypes}"
                });
            }
        }

        return issues;
    }
}
