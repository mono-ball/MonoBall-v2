using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

/// <summary>
/// Result of processing a map into layers.
/// </summary>
public class ProcessedMapData
{
    public required List<LayerData> Layers { get; init; }
    public required HashSet<TileKey> UsedTiles { get; init; }
}

public class MetatileProcessor
{
    private const int NumTilesInPrimaryVram = 512;

    /// <summary>
    /// Process map data into Tiled-compatible layers and track used tiles.
    /// Creates 3 layers: Bg3 (ground), Bg2 (objects), Bg1 (overhead)
    /// </summary>
    public ProcessedMapData ProcessMap(
        ushort[] mapBin,
        List<Metatile> primaryMetatiles,
        List<Metatile> secondaryMetatiles,
        string primaryTileset,
        string secondaryTileset,
        int width,
        int height)
    {
        // Combine metatiles (primary 0-511, secondary 512+)
        var allMetatiles = primaryMetatiles.Concat(secondaryMetatiles).ToList();

        // Initialize layer data arrays (4 tiles per metatile, so 2x dimensions)
        var tileWidth = width * 2;
        var tileHeight = height * 2;

        var bg3Data = new int[tileWidth * tileHeight];
        var bg2Data = new int[tileWidth * tileHeight];
        var bg1Data = new int[tileWidth * tileHeight];

        var usedTiles = new HashSet<TileKey>();

        // Track tile usage: TileKey -> list of (layer, x, y) positions
        var tilePositions = new Dictionary<TileKey, List<(int[] layer, int index)>>();

        // Process each metatile position
        for (int my = 0; my < height; my++)
        {
            for (int mx = 0; mx < width; mx++)
            {
                var mapIndex = my * width + mx;
                var metatileId = MapBinReader.GetMetatileId(mapBin[mapIndex]);

                if (metatileId >= allMetatiles.Count)
                    continue;

                var metatile = allMetatiles[metatileId];

                // Determine which tileset this metatile belongs to
                var isSecondaryMetatile = metatileId >= primaryMetatiles.Count;
                var metatileTileset = isSecondaryMetatile ? secondaryTileset : primaryTileset;

                // Distribute tiles to layers based on layer type
                var distribution = DistributeMetatile(metatile);

                // Write tiles and collect used tiles
                var baseX = mx * 2;
                var baseY = my * 2;

                WriteTilesAndTrack(bg3Data, distribution.Bg3, baseX, baseY, tileWidth,
                    metatileTileset, primaryTileset, secondaryTileset, usedTiles, tilePositions);
                WriteTilesAndTrack(bg2Data, distribution.Bg2, baseX, baseY, tileWidth,
                    metatileTileset, primaryTileset, secondaryTileset, usedTiles, tilePositions);
                WriteTilesAndTrack(bg1Data, distribution.Bg1, baseX, baseY, tileWidth,
                    metatileTileset, primaryTileset, secondaryTileset, usedTiles, tilePositions);
            }
        }

        return new ProcessedMapData
        {
            Layers = new List<LayerData>
            {
                new() { Name = "Ground", Width = tileWidth, Height = tileHeight, Data = bg3Data },
                new() { Name = "Objects", Width = tileWidth, Height = tileHeight, Data = bg2Data },
                new() { Name = "Overhead", Width = tileWidth, Height = tileHeight, Data = bg1Data }
            },
            UsedTiles = usedTiles
        };
    }

    /// <summary>
    /// Overload for backward compatibility.
    /// </summary>
    public List<LayerData> ProcessMap(ushort[] mapBin, List<Metatile> metatiles, int width, int height)
    {
        var result = ProcessMapLegacy(mapBin, metatiles, width, height);
        return result;
    }

    /// <summary>
    /// Legacy processing without tile tracking.
    /// </summary>
    private List<LayerData> ProcessMapLegacy(ushort[] mapBin, List<Metatile> metatiles, int width, int height)
    {
        var tileWidth = width * 2;
        var tileHeight = height * 2;

        var bg3Data = new int[tileWidth * tileHeight];
        var bg2Data = new int[tileWidth * tileHeight];
        var bg1Data = new int[tileWidth * tileHeight];

        for (int my = 0; my < height; my++)
        {
            for (int mx = 0; mx < width; mx++)
            {
                var mapIndex = my * width + mx;
                var metatileId = MapBinReader.GetMetatileId(mapBin[mapIndex]);

                if (metatileId >= metatiles.Count)
                    continue;

                var metatile = metatiles[metatileId];
                var distribution = DistributeMetatile(metatile);

                WriteTiles(bg3Data, distribution.Bg3, mx * 2, my * 2, tileWidth);
                WriteTiles(bg2Data, distribution.Bg2, mx * 2, my * 2, tileWidth);
                WriteTiles(bg1Data, distribution.Bg1, mx * 2, my * 2, tileWidth);
            }
        }

        return new List<LayerData>
        {
            new() { Name = "Ground", Width = tileWidth, Height = tileHeight, Data = bg3Data },
            new() { Name = "Objects", Width = tileWidth, Height = tileHeight, Data = bg2Data },
            new() { Name = "Overhead", Width = tileWidth, Height = tileHeight, Data = bg1Data }
        };
    }

    /// <summary>
    /// Distributes metatile tiles to layers based on layer type.
    /// </summary>
    private LayerDistribution DistributeMetatile(Metatile metatile)
    {
        return metatile.LayerType switch
        {
            MetatileLayerType.Normal => new LayerDistribution(
                Bg3: Array.Empty<TileData>(),
                Bg2: metatile.BottomTiles,
                Bg1: metatile.TopTiles),

            MetatileLayerType.Covered => new LayerDistribution(
                Bg3: metatile.BottomTiles,
                Bg2: metatile.TopTiles,
                Bg1: Array.Empty<TileData>()),

            MetatileLayerType.Split => new LayerDistribution(
                Bg3: metatile.BottomTiles,
                Bg2: Array.Empty<TileData>(),
                Bg1: metatile.TopTiles),

            _ => new LayerDistribution(
                Bg3: Array.Empty<TileData>(),
                Bg2: metatile.BottomTiles,
                Bg1: metatile.TopTiles)
        };
    }

    /// <summary>
    /// Write tiles and track usage for tileset generation.
    /// </summary>
    private void WriteTilesAndTrack(
        int[] layerData,
        TileData[] tiles,
        int x,
        int y,
        int layerWidth,
        string metatileTileset,
        string primaryTileset,
        string secondaryTileset,
        HashSet<TileKey> usedTiles,
        Dictionary<TileKey, List<(int[] layer, int index)>> tilePositions)
    {
        if (tiles.Length == 0)
            return;

        // Tiles are stored in order: TL, TR, BL, BR
        var positions = new[] { (0, 0), (1, 0), (0, 1), (1, 1) };

        for (int i = 0; i < Math.Min(4, tiles.Length); i++)
        {
            var tile = tiles[i];
            var (dx, dy) = positions[i];
            var index = (y + dy) * layerWidth + (x + dx);

            // Determine which tileset the tile comes from
            // For secondary metatiles: tile IDs 0-511 reference primary tileset
            var actualTileset = GetTilesetForTile(
                tile.TileId,
                metatileTileset,
                primaryTileset,
                secondaryTileset);

            var adjustedTileId = AdjustTileId(
                tile.TileId,
                metatileTileset,
                primaryTileset);

            // Create tile key
            var key = new TileKey(
                actualTileset,
                adjustedTileId,
                tile.PaletteIndex,
                tile.FlipHorizontal,
                tile.FlipVertical);

            // Track usage
            usedTiles.Add(key);

            if (!tilePositions.TryGetValue(key, out var positions_))
            {
                positions_ = new List<(int[] layer, int index)>();
                tilePositions[key] = positions_;
            }
            positions_.Add((layerData, index));

            // Write placeholder GID (will be replaced after tileset is built)
            // Use negative value as marker, with encoded tile info
            layerData[index] = EncodeTemporaryGid(key);
        }
    }

    /// <summary>
    /// Determine which tileset a tile belongs to.
    /// </summary>
    private string GetTilesetForTile(
        int tileId,
        string metatileTileset,
        string primaryTileset,
        string secondaryTileset)
    {
        // If metatile is from primary tileset, tile is from primary
        if (string.Equals(metatileTileset, primaryTileset, StringComparison.OrdinalIgnoreCase))
            return primaryTileset;

        // If metatile is from secondary tileset:
        // - Tile IDs 0-511 reference primary tileset
        // - Tile IDs 512+ reference secondary tileset
        if (tileId < NumTilesInPrimaryVram)
            return primaryTileset;
        else
            return secondaryTileset;
    }

    /// <summary>
    /// Adjust tile ID for secondary tileset tiles.
    /// </summary>
    private int AdjustTileId(int tileId, string metatileTileset, string primaryTileset)
    {
        // If metatile is from secondary and tile ID >= 512, subtract 512
        if (!string.Equals(metatileTileset, primaryTileset, StringComparison.OrdinalIgnoreCase) &&
            tileId >= NumTilesInPrimaryVram)
        {
            return tileId - NumTilesInPrimaryVram;
        }
        return tileId;
    }

    /// <summary>
    /// Encode tile info as temporary GID.
    /// </summary>
    private int EncodeTemporaryGid(TileKey key)
    {
        // For now, just use tile ID + 1 as placeholder
        // This will be replaced with actual GID after tileset generation
        var gid = key.TileId + 1;

        // Tiled flip flags (bits 29-31)
        if (key.FlipH) gid |= unchecked((int)0x80000000);
        if (key.FlipV) gid |= 0x40000000;

        return gid;
    }

    /// <summary>
    /// Write 4 tiles (2x2) to a layer array (legacy method).
    /// </summary>
    private void WriteTiles(int[] layerData, TileData[] tiles, int x, int y, int layerWidth)
    {
        if (tiles.Length == 0)
            return;

        if (tiles.Length >= 1) layerData[y * layerWidth + x] = TileToGid(tiles[0]);
        if (tiles.Length >= 2) layerData[y * layerWidth + x + 1] = TileToGid(tiles[1]);
        if (tiles.Length >= 3) layerData[(y + 1) * layerWidth + x] = TileToGid(tiles[2]);
        if (tiles.Length >= 4) layerData[(y + 1) * layerWidth + x + 1] = TileToGid(tiles[3]);
    }

    /// <summary>
    /// Convert TileData to Tiled GID (legacy method).
    /// </summary>
    private int TileToGid(TileData tile)
    {
        if (tile.TileId == 0 && tile.PaletteIndex == 0)
            return 0;

        var gid = tile.TileId + 1;

        if (tile.FlipHorizontal) gid |= unchecked((int)0x80000000);
        if (tile.FlipVertical) gid |= 0x40000000;

        return gid;
    }

    /// <summary>
    /// Update layer data with actual GIDs from tileset mapping.
    /// </summary>
    public void ApplyTilesetMapping(
        List<LayerData> layers,
        Dictionary<TileKey, int> mapping,
        ushort[] mapBin,
        List<Metatile> primaryMetatiles,
        List<Metatile> secondaryMetatiles,
        string primaryTileset,
        string secondaryTileset,
        int width,
        int height)
    {
        var allMetatiles = primaryMetatiles.Concat(secondaryMetatiles).ToList();
        var tileWidth = width * 2;

        for (int my = 0; my < height; my++)
        {
            for (int mx = 0; mx < width; mx++)
            {
                var mapIndex = my * width + mx;
                var metatileId = MapBinReader.GetMetatileId(mapBin[mapIndex]);

                if (metatileId >= allMetatiles.Count)
                    continue;

                var metatile = allMetatiles[metatileId];
                var isSecondaryMetatile = metatileId >= primaryMetatiles.Count;
                var metatileTileset = isSecondaryMetatile ? secondaryTileset : primaryTileset;

                var distribution = DistributeMetatile(metatile);
                var baseX = mx * 2;
                var baseY = my * 2;

                ApplyTilesToLayer(layers[0].Data, distribution.Bg3, baseX, baseY, tileWidth,
                    metatileTileset, primaryTileset, secondaryTileset, mapping);
                ApplyTilesToLayer(layers[1].Data, distribution.Bg2, baseX, baseY, tileWidth,
                    metatileTileset, primaryTileset, secondaryTileset, mapping);
                ApplyTilesToLayer(layers[2].Data, distribution.Bg1, baseX, baseY, tileWidth,
                    metatileTileset, primaryTileset, secondaryTileset, mapping);
            }
        }
    }

    private void ApplyTilesToLayer(
        int[] layerData,
        TileData[] tiles,
        int x,
        int y,
        int layerWidth,
        string metatileTileset,
        string primaryTileset,
        string secondaryTileset,
        Dictionary<TileKey, int> mapping)
    {
        if (tiles.Length == 0)
            return;

        var positions = new[] { (0, 0), (1, 0), (0, 1), (1, 1) };

        for (int i = 0; i < Math.Min(4, tiles.Length); i++)
        {
            var tile = tiles[i];
            var (dx, dy) = positions[i];
            var index = (y + dy) * layerWidth + (x + dx);

            var actualTileset = GetTilesetForTile(tile.TileId, metatileTileset, primaryTileset, secondaryTileset);
            var adjustedTileId = AdjustTileId(tile.TileId, metatileTileset, primaryTileset);

            var key = new TileKey(actualTileset, adjustedTileId, tile.PaletteIndex, tile.FlipHorizontal, tile.FlipVertical);

            if (mapping.TryGetValue(key, out var gid))
            {
                // Apply flip flags to GID
                if (tile.FlipHorizontal) gid |= unchecked((int)0x80000000);
                if (tile.FlipVertical) gid |= 0x40000000;
                layerData[index] = gid;
            }
            else if (tile.TileId == 0 && tile.PaletteIndex == 0)
            {
                layerData[index] = 0;
            }
        }
    }
}

public record LayerDistribution(
    TileData[] Bg3,  // Ground layer (under objects)
    TileData[] Bg2,  // Object layer (player/NPCs)
    TileData[] Bg1   // Overhead layer (treetops, roofs)
);
