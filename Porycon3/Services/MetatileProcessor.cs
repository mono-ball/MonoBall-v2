using Porycon3.Models;
using Porycon3.Infrastructure;

namespace Porycon3.Services;

public class MetatileProcessor
{
    /// <summary>
    /// Process map data into Tiled-compatible layers.
    /// Creates 3 layers: Bg3 (ground), Bg2 (objects), Bg1 (overhead)
    /// </summary>
    public List<LayerData> ProcessMap(ushort[] mapBin, List<Metatile> metatiles, int width, int height)
    {
        // Initialize layer data arrays (4 tiles per metatile, so 2x dimensions)
        var tileWidth = width * 2;
        var tileHeight = height * 2;

        var bg3Data = new int[tileWidth * tileHeight];
        var bg2Data = new int[tileWidth * tileHeight];
        var bg1Data = new int[tileWidth * tileHeight];

        // Process each metatile position
        for (int my = 0; my < height; my++)
        {
            for (int mx = 0; mx < width; mx++)
            {
                var mapIndex = my * width + mx;
                var metatileId = MapBinReader.GetMetatileId(mapBin[mapIndex]);

                if (metatileId >= metatiles.Count)
                    continue; // Skip invalid metatile IDs

                var metatile = metatiles[metatileId];

                // Distribute tiles to layers based on layer type
                var distribution = DistributeMetatile(metatile);

                // Write 4 tiles (2x2) to each layer
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
    /// Write 4 tiles (2x2) to a layer array.
    /// </summary>
    private void WriteTiles(int[] layerData, TileData[] tiles, int x, int y, int layerWidth)
    {
        if (tiles.Length == 0)
            return;

        // Tiles are stored in order: TL, TR, BL, BR
        if (tiles.Length >= 1) layerData[y * layerWidth + x] = TileToGid(tiles[0]);
        if (tiles.Length >= 2) layerData[y * layerWidth + x + 1] = TileToGid(tiles[1]);
        if (tiles.Length >= 3) layerData[(y + 1) * layerWidth + x] = TileToGid(tiles[2]);
        if (tiles.Length >= 4) layerData[(y + 1) * layerWidth + x + 1] = TileToGid(tiles[3]);
    }

    /// <summary>
    /// Convert TileData to Tiled GID.
    /// For now, just use tile ID + 1 (Tiled GIDs are 1-based).
    /// TODO: Implement proper GID remapping with TilesetBuilder.
    /// </summary>
    private int TileToGid(TileData tile)
    {
        if (tile.TileId == 0 && tile.PaletteIndex == 0)
            return 0; // Empty tile

        // Basic GID: tile ID + 1, with flip flags in high bits
        var gid = tile.TileId + 1;

        // Tiled flip flags (bits 29-31)
        if (tile.FlipHorizontal) gid |= unchecked((int)0x80000000);
        if (tile.FlipVertical) gid |= 0x40000000;

        return gid;
    }
}

public record LayerDistribution(
    TileData[] Bg3,  // Ground layer (under objects)
    TileData[] Bg2,  // Object layer (player/NPCs)
    TileData[] Bg1   // Overhead layer (treetops, roofs)
);
