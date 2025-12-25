using Porycon3.Models;

namespace Porycon3.Infrastructure;

public class MetatileBinReader
{
    private readonly string _pokeemeraldPath;

    public MetatileBinReader(string pokeemeraldPath)
    {
        _pokeemeraldPath = pokeemeraldPath;
    }

    /// <summary>
    /// Reads metatiles from a tileset's metatiles.bin file.
    /// Each metatile is 16 bytes (8 tiles x 2 bytes each).
    /// </summary>
    public List<Metatile> ReadMetatiles(string tilesetName)
    {
        if (string.IsNullOrEmpty(tilesetName))
            return new List<Metatile>();

        var metatilePath = FindMetatilePath(tilesetName);
        if (metatilePath == null)
            return new List<Metatile>();

        var attributesPath = metatilePath.Replace("metatiles.bin", "metatile_attributes.bin");

        var metatileBytes = File.ReadAllBytes(metatilePath);
        var attributeBytes = File.Exists(attributesPath) ? File.ReadAllBytes(attributesPath) : null;

        var metatiles = new List<Metatile>();
        const int bytesPerMetatile = 16; // 8 tiles x 2 bytes
        var metatileCount = metatileBytes.Length / bytesPerMetatile;

        for (int i = 0; i < metatileCount; i++)
        {
            var offset = i * bytesPerMetatile;

            // Read 8 tile entries (4 bottom + 4 top)
            var bottomTiles = new TileData[4];
            var topTiles = new TileData[4];

            for (int t = 0; t < 4; t++)
            {
                var tileOffset = offset + (t * 2);
                var rawBottom = BitConverter.ToUInt16(metatileBytes, tileOffset);
                bottomTiles[t] = TileData.FromRaw(rawBottom);
            }

            for (int t = 0; t < 4; t++)
            {
                var tileOffset = offset + 8 + (t * 2);
                var rawTop = BitConverter.ToUInt16(metatileBytes, tileOffset);
                topTiles[t] = TileData.FromRaw(rawTop);
            }

            // Read attributes (behavior, terrain type)
            // In pokeemerald-expansion, attributes are 4 bytes per metatile
            int behavior = 0, terrainType = 0;
            if (attributeBytes != null)
            {
                // Try 4-byte format first (newer pokeemerald-expansion)
                if (i * 4 + 3 < attributeBytes.Length)
                {
                    var attrOffset = i * 4;
                    // 4-byte format: behavior (2 bytes) + encounter type (1 byte) + terrain (1 byte)
                    behavior = BitConverter.ToUInt16(attributeBytes, attrOffset);
                    terrainType = attributeBytes[attrOffset + 3];
                }
                // Fallback to 2-byte format (older)
                else if (i * 2 + 1 < attributeBytes.Length)
                {
                    var attrOffset = i * 2;
                    behavior = attributeBytes[attrOffset];
                    terrainType = attributeBytes[attrOffset + 1];
                }
            }

            metatiles.Add(new Metatile
            {
                Id = i,
                BottomTiles = bottomTiles,
                TopTiles = topTiles,
                Behavior = behavior,
                TerrainType = terrainType
            });
        }

        return metatiles;
    }

    private string? FindMetatilePath(string tilesetName)
    {
        // Handle gTileset_XXX format -> convert to folder name
        var folderName = TilesetNameToFolder(tilesetName);

        // Check primary tilesets
        var primaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "primary",
            folderName, "metatiles.bin");
        if (File.Exists(primaryPath))
            return primaryPath;

        // Check secondary tilesets
        var secondaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "secondary",
            folderName, "metatiles.bin");
        if (File.Exists(secondaryPath))
            return secondaryPath;

        // Try lowercase
        var lowerFolder = folderName.ToLowerInvariant();
        primaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "primary",
            lowerFolder, "metatiles.bin");
        if (File.Exists(primaryPath))
            return primaryPath;

        secondaryPath = Path.Combine(_pokeemeraldPath, "data", "tilesets", "secondary",
            lowerFolder, "metatiles.bin");
        if (File.Exists(secondaryPath))
            return secondaryPath;

        return null;
    }

    /// <summary>
    /// Converts gTileset_General -> general, gTileset_Petalburg -> petalburg
    /// </summary>
    private string TilesetNameToFolder(string tilesetName)
    {
        var name = tilesetName;

        // Remove gTileset_ prefix
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(9);
        }

        return name.ToLowerInvariant();
    }
}
