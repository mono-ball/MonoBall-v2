using Porycon3.Models;
using Porycon3.Services.Interfaces;

namespace Porycon3.Infrastructure;

public class MetatileBinReader : IMetatileReader
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

            // Read attributes (behavior + layer type)
            // Format: 16-bit value with behavior (bits 0-7) and layer type (bits 12-15)
            // Note: There is no terrain type in metatile attributes; terrain is derived from behavior
            int behavior = 0, terrainType = 0;
            if (attributeBytes != null)
            {
                // Detect format based on file size vs metatile count
                var attrBytesPerMetatile = attributeBytes.Length / metatileCount;

                if (attrBytesPerMetatile >= 2 && i * 2 + 1 < attributeBytes.Length)
                {
                    var attrOffset = i * 2;
                    // Read 16-bit attribute value containing behavior (0-7) and layer type (12-15)
                    behavior = BitConverter.ToUInt16(attributeBytes, attrOffset);
                    // Derive terrain from behavior (e.g., tall_grass -> grass, water behaviors -> water)
                    terrainType = DeriveTerrain(behavior & 0xFF);
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
    /// Converts gTileset_General -> general, gTileset_EverGrande -> ever_grande
    /// Handles PascalCase to snake_case conversion for multi-word tileset names.
    /// </summary>
    private static string TilesetNameToFolder(string tilesetName)
    {
        var name = tilesetName;

        // Remove gTileset_ prefix
        if (name.StartsWith("gTileset_", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(9);
        }

        // Convert PascalCase to snake_case
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Derive terrain type from behavior.
    /// Maps behavior values to terrain categories for encounter/footstep purposes.
    /// </summary>
    private static int DeriveTerrain(int behavior)
    {
        return behavior switch
        {
            // Grass-related behaviors -> grass terrain (1)
            0x01 => 1, // MB_TALL_GRASS
            0x02 => 1, // MB_VERY_TALL_GRASS
            0x14 => 1, // MB_ASH_GRASS

            // Water-related behaviors -> water terrain (2)
            0x04 => 2, // MB_SHORE_WATER
            0x05 => 2, // MB_DEEP_WATER
            0x07 => 2, // MB_OCEAN_WATER
            0x08 => 2, // MB_POND_WATER
            0x09 => 2, // MB_PUDDLE
            0x81 => 2, // MB_DEEP_WATER_2

            // Waterfall -> waterfall terrain (3)
            0x06 => 3, // MB_WATERFALL

            // Sand-related behaviors -> sand terrain (6)
            0x13 => 6, // MB_SAND
            0x15 => 6, // MB_SAND_CAVE

            // Mountain-related behaviors -> mountain terrain (7)
            0x0C => 7, // MB_MOUNTAIN

            // Default -> normal terrain (0)
            _ => 0
        };
    }
}
