using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services.Builders;

/// <summary>
/// Transforms movement types to behavior IDs and builds behavior parameters.
/// </summary>
public static class BehaviorTransformer
{
    /// <summary>
    /// Transform movement type to behavior ID.
    /// </summary>
    public static string TransformBehaviorId(string movementType)
    {
        if (string.IsNullOrEmpty(movementType))
            return $"{IdTransformer.Namespace}:script/movement/npcs/stationary";

        var name = movementType.StartsWith("MOVEMENT_TYPE_", StringComparison.OrdinalIgnoreCase)
            ? movementType[14..].ToLowerInvariant()
            : movementType.ToLowerInvariant();

        // Categorize movement types - reference script definitions directly
        if (name.StartsWith("walk_sequence_"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/patrol";
        if (name.Contains("wander"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/wander";
        if (name.Contains("stationary") || name.StartsWith("face_") || name.Contains("look_around"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/stationary";
        if (name.Contains("walk") || name.Contains("pace"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/walk";
        if (name.Contains("jog") || name.Contains("run"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/jog";
        if (name.Contains("copy_player") || name.Contains("follow"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/follow";
        if (name.Contains("invisible"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/invisible";
        if (name.Contains("buried"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/buried";
        if (name.Contains("tree_disguise"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/disguise_tree";
        if (name.Contains("rock_disguise"))
            return $"{IdTransformer.Namespace}:script/movement/npcs/disguise_rock";

        return $"{IdTransformer.Namespace}:script/movement/npcs/{name}";
    }

    /// <summary>
    /// Build behavior parameters for the given movement type.
    /// </summary>
    public static object? BuildBehaviorParameters(string movementType, int startX, int startY, int? rangeX, int? rangeY)
    {
        if (string.IsNullOrEmpty(movementType))
            return null;

        var name = movementType.StartsWith("MOVEMENT_TYPE_", StringComparison.OrdinalIgnoreCase)
            ? movementType[14..].ToLowerInvariant()
            : movementType.ToLowerInvariant();

        // Patrol behavior - calculate waypoint grid positions from direction sequence
        if (name.StartsWith("walk_sequence_"))
        {
            var waypoints = CalculatePatrolWaypoints(name, startX, startY, rangeX ?? 1, rangeY ?? 1);
            if (waypoints != null && waypoints.Length > 0)
                return new { waypoints };
            return null;
        }

        // Wander/Walk behaviors use range parameters
        if (name.Contains("wander") || name.Contains("walk") || name.Contains("pace"))
        {
            if ((rangeX.HasValue && rangeX.Value > 0) || (rangeY.HasValue && rangeY.Value > 0))
                return new { rangeX = rangeX ?? 0, rangeY = rangeY ?? 0 };
        }

        return null;
    }

    /// <summary>
    /// Extract facing direction from movement type.
    /// </summary>
    public static string? ExtractDirection(string movementType)
    {
        if (string.IsNullOrEmpty(movementType))
            return null;

        var lower = movementType.ToLowerInvariant();
        if (lower.Contains("_up") || lower.Contains("face_up")) return "up";
        if (lower.Contains("_down") || lower.Contains("face_down")) return "down";
        if (lower.Contains("_left") || lower.Contains("face_left")) return "left";
        if (lower.Contains("_right") || lower.Contains("face_right")) return "right";
        return null;
    }

    /// <summary>
    /// Calculate patrol waypoints from a walk sequence movement type.
    /// </summary>
    private static object[]? CalculatePatrolWaypoints(string name, int startX, int startY, int rangeX, int rangeY)
    {
        var sequence = name.StartsWith("walk_sequence_") ? name[14..] : name;

        // Parse the direction sequence (e.g., "up_right_left_down")
        var directions = new List<string>();
        var parts = sequence.Split('_');
        foreach (var part in parts)
        {
            if (part is "up" or "down" or "left" or "right")
                directions.Add(part);
        }

        if (directions.Count == 0)
            return null;

        // Calculate waypoints by following the direction sequence
        var waypoints = new List<object>();
        int currentX = startX;
        int currentY = startY;

        foreach (var dir in directions)
        {
            switch (dir)
            {
                case "up":
                    currentY -= rangeY * MetatileSize;
                    break;
                case "down":
                    currentY += rangeY * MetatileSize;
                    break;
                case "left":
                    currentX -= rangeX * MetatileSize;
                    break;
                case "right":
                    currentX += rangeX * MetatileSize;
                    break;
            }
            waypoints.Add(new { x = currentX, y = currentY });
        }

        return waypoints.ToArray();
    }
}
