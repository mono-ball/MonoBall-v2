using SixLabors.ImageSharp;
using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services.Builders;

/// <summary>
/// Builds tilesheet JSON output structure.
/// </summary>
public class TilesheetOutputBuilder
{
    private readonly string _outputPath;

    public TilesheetOutputBuilder(string outputPath)
    {
        _outputPath = outputPath;
    }

    /// <summary>
    /// Save tilesheet image and JSON definition.
    /// </summary>
    public void SaveTilesheet(SharedTilesetResult result)
    {
        SaveTilesheetImage(result);
        SaveTilesheetDefinition(result);
        result.TilesheetImage.Dispose();
    }

    private void SaveTilesheetImage(SharedTilesetResult result)
    {
        var graphicsDir = Path.Combine(_outputPath, "Graphics", "Tilesets",
            result.TilesetType == "primary" ? "Primary" : "Secondary");
        Directory.CreateDirectory(graphicsDir);
        var imagePath = Path.Combine(graphicsDir, $"{result.TilesetName}.png");
        result.TilesheetImage.SaveAsPng(imagePath);
    }

    private void SaveTilesheetDefinition(SharedTilesetResult result)
    {
        var defsDir = Path.Combine(_outputPath, "Definitions", "Assets", "Tilesets", result.TilesetType);
        Directory.CreateDirectory(defsDir);

        var tilesArray = BuildTilesArray(result);
        var texturePath = $"Graphics/Tilesets/{(result.TilesetType == "primary" ? "Primary" : "Secondary")}/{result.TilesetName}.png";

        var tilesetJson = new
        {
            id = result.TilesetId,
            name = result.TilesetName,
            type = result.TilesetType,
            texturePath,
            tileWidth = MetatileSize,
            tileHeight = MetatileSize,
            tileCount = result.TileCount,
            columns = result.Columns,
            imageWidth = result.TilesheetImage.Width,
            imageHeight = result.TilesheetImage.Height,
            spacing = 0,
            margin = 0,
            tiles = tilesArray
        };

        var jsonPath = Path.Combine(defsDir, $"{result.TilesetName}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(tilesetJson, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(jsonPath, json);
    }

    private static object[]? BuildTilesArray(SharedTilesetResult result)
    {
        if (result.TileProperties.Count == 0 && result.Animations.Count == 0)
            return null;

        // Build animation lookup by localTileId
        var animationsByTile = result.Animations
            .GroupBy(a => a.LocalTileId)
            .ToDictionary(g => g.Key, g => g.First());

        var tiles = new List<object>();

        // Add all tile properties (with animations if present)
        foreach (var prop in result.TileProperties)
        {
            object? animation = null;
            if (animationsByTile.TryGetValue(prop.LocalTileId, out var anim))
            {
                animation = anim.Frames.Select(f => new
                {
                    tileId = f.TileId,
                    durationMs = f.DurationMs
                });
            }

            tiles.Add(new
            {
                localTileId = prop.LocalTileId,
                interactionId = prop.InteractionId,
                terrainId = prop.TerrainId,
                collisionId = prop.CollisionId,
                animation
            });
        }

        // Add animation-only tiles (no properties)
        var propertyTileIds = result.TileProperties.Select(p => p.LocalTileId).ToHashSet();
        foreach (var anim in result.Animations.Where(a => !propertyTileIds.Contains(a.LocalTileId)))
        {
            tiles.Add(new
            {
                localTileId = anim.LocalTileId,
                interactionId = (string?)null,
                terrainId = (string?)null,
                collisionId = (string?)null,
                animation = anim.Frames.Select(f => new
                {
                    tileId = f.TileId,
                    durationMs = f.DurationMs
                })
            });
        }

        return tiles.OrderBy(t => ((dynamic)t).localTileId).ToArray();
    }
}
