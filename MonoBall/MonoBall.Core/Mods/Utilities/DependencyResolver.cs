using System.Collections.Generic;
using System.Linq;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for resolving mod dependencies and detecting circular dependencies.
/// </summary>
public static class DependencyResolver
{
    /// <summary>
    ///     Detects circular dependencies in a collection of mod manifests.
    /// </summary>
    /// <param name="mods">Dictionary of mod manifests by ID.</param>
    /// <returns>List of mod IDs involved in circular dependencies.</returns>
    public static List<string> DetectCircularDependencies(Dictionary<string, ModManifest> mods)
    {
        var circularDeps = new List<string>();
        var visited = new HashSet<string>();

        foreach (var (modId, manifest) in mods)
        {
            if (!visited.Contains(modId))
            {
                var cycle = DetectCycle(modId, manifest, mods, new HashSet<string>(), visited);
                if (cycle != null)
                    circularDeps.AddRange(cycle);
            }
        }

        return circularDeps;
    }

    /// <summary>
    ///     Recursively detects a cycle starting from a mod.
    /// </summary>
    /// <param name="modId">The current mod ID being checked.</param>
    /// <param name="manifest">The current mod manifest.</param>
    /// <param name="allMods">All available mod manifests.</param>
    /// <param name="currentPath">The current dependency path being explored.</param>
    /// <param name="visited">Set of mod IDs that have been fully processed.</param>
    /// <returns>List of mod IDs in the cycle, or null if no cycle found.</returns>
    private static List<string>? DetectCycle(
        string modId,
        ModManifest manifest,
        Dictionary<string, ModManifest> allMods,
        HashSet<string> currentPath,
        HashSet<string> visited
    )
    {
        if (currentPath.Contains(modId))
        {
            // Found a cycle - return the cycle path
            var cycle = new List<string>(currentPath) { modId };
            return cycle;
        }

        currentPath.Add(modId);
        visited.Add(modId);

        foreach (var depId in manifest.Dependencies)
        {
            if (allMods.TryGetValue(depId, out var depManifest))
            {
                var cycle = DetectCycle(depId, depManifest, allMods, currentPath, visited);
                if (cycle != null)
                    return cycle;
            }
        }

        currentPath.Remove(modId);
        return null;
    }
}
