using System;
using System.IO;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Resources;

/// <summary>
///     Service for resolving resource file paths from mod definitions.
///     Fails fast with exceptions per .cursorrules (no fallback code).
/// </summary>
public class ResourcePathResolver : IResourcePathResolver
{
    private readonly ILogger _logger;
    private readonly IModManager _modManager;

    /// <summary>
    ///     Initializes a new instance of the ResourcePathResolver.
    /// </summary>
    /// <param name="modManager">The mod manager for accessing mod manifests.</param>
    /// <param name="logger">The logger for logging operations.</param>
    /// <exception cref="ArgumentNullException">Thrown when modManager or logger is null.</exception>
    public ResourcePathResolver(IModManager modManager, ILogger logger)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Resolves the virtual mod:// path for a resource definition.
    ///     Fails fast with exceptions per .cursorrules (no fallback code).
    /// </summary>
    /// <param name="resourceId">The resource definition ID.</param>
    /// <param name="relativePath">The relative path from the definition (e.g., TexturePath, FontPath).</param>
    /// <returns>The virtual mod:// path in format mod://{modId}/{relativePath}.</returns>
    /// <exception cref="ArgumentException">Thrown when resourceId or relativePath is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when mod manifest cannot be found or ModSource is null.</exception>
    /// <exception cref="FileNotFoundException">Thrown when resolved file does not exist.</exception>
    public string ResolveResourcePath(string resourceId, string relativePath)
    {
        if (string.IsNullOrEmpty(resourceId))
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));

        if (string.IsNullOrEmpty(relativePath))
            throw new ArgumentException(
                "Relative path cannot be null or empty.",
                nameof(relativePath)
            );

        // Get mod manifest - fail fast if not found (no fallback code per .cursorrules)
        var modManifest = GetResourceModManifest(resourceId);

        // Fail fast if ModSource is null
        if (modManifest.ModSource == null)
            throw new InvalidOperationException(
                $"Mod '{modManifest.Id}' has no ModSource. Cannot resolve resource path for '{resourceId}'."
            );

        // Fail fast if file doesn't exist
        if (!modManifest.ModSource.FileExists(relativePath))
            throw new FileNotFoundException(
                $"Resource file not found: {relativePath} (resource: {resourceId}, mod: {modManifest.Id})"
            );

        // Return virtual mod:// path
        var virtualPath = $"mod://{modManifest.Id}/{relativePath}";

        _logger.Debug(
            "Resolved resource path: {ResourceId} -> {VirtualPath}",
            resourceId,
            virtualPath
        );

        return virtualPath;
    }

    /// <summary>
    ///     Gets the mod manifest that owns a resource definition.
    ///     Fails fast with exception if not found (no fallback code).
    /// </summary>
    /// <param name="resourceId">The resource definition ID.</param>
    /// <returns>The mod manifest.</returns>
    /// <exception cref="ArgumentException">Thrown when resourceId is null/empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when mod manifest cannot be found.</exception>
    public ModManifest GetResourceModManifest(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId))
            throw new ArgumentException("Resource ID cannot be null or empty.", nameof(resourceId));

        // Use GetModManifestByDefinitionId - fail fast if not found (no fallback code)
        var modManifest = _modManager.GetModManifestByDefinitionId(resourceId);
        if (modManifest == null)
            throw new InvalidOperationException(
                $"Mod manifest not found for resource '{resourceId}'. "
                    + "Ensure the resource is defined in a loaded mod."
            );

        return modManifest;
    }
}
