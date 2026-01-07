using System;
using System.Collections.Generic;
using System.Linq;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for filtering mod file paths by content folder.
/// </summary>
public static class ModPathFilter
{
    /// <summary>
    ///     Filters file paths to match a content folder path.
    /// </summary>
    /// <param name="paths">The paths to filter.</param>
    /// <param name="contentFolderPath">The content folder path to match.</param>
    /// <returns>Filtered paths that belong to the content folder.</returns>
    public static IEnumerable<string> FilterByContentFolder(
        IEnumerable<string> paths,
        string contentFolderPath
    )
    {
        if (paths == null)
            throw new ArgumentNullException(nameof(paths));

        if (contentFolderPath == null)
            throw new ArgumentNullException(nameof(contentFolderPath));

        var normalized = ModPathNormalizer.Normalize(contentFolderPath);
        return paths.Where(p =>
            p.StartsWith(normalized + "/", StringComparison.Ordinal)
            || p == normalized
            || p.StartsWith(normalized + "\\", StringComparison.Ordinal)
        );
    }
}
