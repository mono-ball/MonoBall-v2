using System;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for parsing mod:// virtual paths.
/// </summary>
public static class ModPathParser
{
    /// <summary>
    ///     Parses a mod:// virtual path into mod ID and relative path.
    /// </summary>
    /// <param name="virtualPath">The virtual path in format mod://{modId}/{relativePath}.</param>
    /// <returns>Tuple of (modId, relativePath).</returns>
    /// <exception cref="ArgumentException">Thrown when virtualPath is not a valid mod:// path.</exception>
    public static (string modId, string relativePath) ParseModPath(string virtualPath)
    {
        if (string.IsNullOrEmpty(virtualPath))
            throw new ArgumentException(
                "Virtual path cannot be null or empty.",
                nameof(virtualPath)
            );

        if (!virtualPath.StartsWith("mod://", StringComparison.Ordinal))
            throw new ArgumentException(
                $"Invalid virtual path format. Expected mod://{{modId}}/{{path}}, got: {virtualPath}",
                nameof(virtualPath)
            );

        var pathWithoutScheme = virtualPath.Substring(6); // Remove "mod://"
        var firstSlashIndex = pathWithoutScheme.IndexOf('/', StringComparison.Ordinal);

        if (firstSlashIndex < 0)
            throw new ArgumentException(
                $"Invalid virtual path format. Missing mod ID or path separator: {virtualPath}",
                nameof(virtualPath)
            );

        var modId = pathWithoutScheme.Substring(0, firstSlashIndex);
        var relativePath = pathWithoutScheme.Substring(firstSlashIndex + 1);

        if (string.IsNullOrEmpty(modId))
            throw new ArgumentException(
                $"Invalid virtual path format. Mod ID is empty: {virtualPath}",
                nameof(virtualPath)
            );

        if (string.IsNullOrEmpty(relativePath))
            throw new ArgumentException(
                $"Invalid virtual path format. Relative path is empty: {virtualPath}",
                nameof(virtualPath)
            );

        return (modId, relativePath);
    }
}
