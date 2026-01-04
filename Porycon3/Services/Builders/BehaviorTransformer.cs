using static Porycon3.Infrastructure.TileConstants;

namespace Porycon3.Services.Builders;

/// <summary>
/// Transforms movement types to behavior IDs and builds behavior parameters.
/// </summary>
public static class BehaviorTransformer
{
    /// <summary>
    /// Transform movement type to behavior script ID.
    /// Uses IdTransformer.MovementScriptId to ensure consistency with definition files.
    /// </summary>
    public static string TransformBehaviorId(string movementType)
    {
        if (string.IsNullOrEmpty(movementType))
            return IdTransformer.MovementScriptId("MOVEMENT_TYPE_STATIONARY");

        // Extract base name from MOVEMENT_TYPE_ prefix if present
        var name = movementType.StartsWith("MOVEMENT_TYPE_", StringComparison.OrdinalIgnoreCase)
            ? movementType[14..]
            : movementType;

        // Map movement types to script definition names (matching actual definition file names)
        var scriptName = name.ToLowerInvariant() switch
        {
            var n when n.StartsWith("walk_sequence_") => "patrol",
            var n when n.Contains("wander") => "wander",
            var n when n.Contains("stationary") || n.StartsWith("face_") || n.Contains("look_around") => "stationary",
            var n when (n.Contains("walk") || n.Contains("pace")) && !n.Contains("sequence") => "walk",
            var n when n.Contains("jog") || n.Contains("run") => "jog",
            var n when n.Contains("copy_player") || n.Contains("follow") => "follow",
            var n when n.Contains("invisible") => "invisible",
            var n when n.Contains("buried") => "buried",
            var n when n.Contains("tree_disguise") => "disguise_tree",
            var n when n.Contains("rock_disguise") => "disguise_rock",
            _ => name.ToLowerInvariant() // Use normalized name as-is
        };

        // Use MovementScriptId which will normalize and format correctly
        return IdTransformer.MovementScriptId($"MOVEMENT_TYPE_{scriptName.ToUpperInvariant()}");
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
