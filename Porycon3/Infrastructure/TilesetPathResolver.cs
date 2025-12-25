namespace Porycon3.Infrastructure;

/// <summary>
/// Resolves paths to tileset files in pokeemerald directory structure.
/// </summary>
public class TilesetPathResolver
{
    private readonly string _pokeemeraldPath;

    public TilesetPathResolver(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
    }

    /// <summary>
    /// Find the tileset directory for a given tileset name.
    /// Returns (type, path) where type is "primary" or "secondary".
    /// </summary>
    public (string Type, string Path)? FindTilesetPath(string tilesetName)
    {
        var folderName = NormalizeTilesetName(tilesetName);

        // Check primary tilesets
        var primaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "primary", folderName);
        if (Directory.Exists(primaryPath))
            return ("primary", primaryPath);

        // Check secondary tilesets
        var secondaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "secondary", folderName);
        if (Directory.Exists(secondaryPath))
            return ("secondary", secondaryPath);

        // Try lowercase
        var lowerName = folderName.ToLowerInvariant();
        primaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "primary", lowerName);
        if (Directory.Exists(primaryPath))
            return ("primary", primaryPath);

        secondaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "secondary", lowerName);
        if (Directory.Exists(secondaryPath))
            return ("secondary", secondaryPath);

        return null;
    }

    /// <summary>
    /// Find the tiles.png image for a tileset.
    /// </summary>
    public string? FindTilesetImagePath(string tilesetName)
    {
        var result = FindTilesetPath(tilesetName);
        if (result == null)
            return null;

        var tilesPath = Path.Combine(result.Value.Path, "tiles.png");
        return File.Exists(tilesPath) ? tilesPath : null;
    }

    /// <summary>
    /// Check if tileset is primary or secondary.
    /// </summary>
    public bool IsPrimaryTileset(string tilesetName)
    {
        var result = FindTilesetPath(tilesetName);
        return result?.Type == "primary";
    }

    /// <summary>
    /// Normalize tileset name: gTileset_General -> general
    /// </summary>
    private static string NormalizeTilesetName(string name)
    {
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
            name = name[9..];
        return name.ToLowerInvariant();
    }
}
