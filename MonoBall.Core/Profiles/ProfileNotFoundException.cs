using System;

namespace MonoBall.Core.Profiles;

/// <summary>
///     Exception thrown when a profile is not found in the registry.
/// </summary>
public class ProfileNotFoundException : InvalidOperationException
{
    /// <summary>
    ///     Gets the profile ID that was not found.
    /// </summary>
    public string ProfileId { get; }

    /// <summary>
    ///     Initializes a new instance of the ProfileNotFoundException class.
    /// </summary>
    /// <param name="profileId">The profile ID that was not found.</param>
    /// <exception cref="ArgumentNullException">Thrown if profileId is null.</exception>
    public ProfileNotFoundException(string profileId)
        : base($"Profile '{profileId}' not found in registry. Ensure the profile definition is loaded from mods.")
    {
        ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
    }

    /// <summary>
    ///     Initializes a new instance of the ProfileNotFoundException class with a custom message.
    /// </summary>
    /// <param name="profileId">The profile ID that was not found.</param>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <exception cref="ArgumentNullException">Thrown if profileId is null.</exception>
    public ProfileNotFoundException(string profileId, string message)
        : base(message)
    {
        ProfileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
    }
}
