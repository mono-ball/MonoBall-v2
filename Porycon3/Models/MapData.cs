namespace Porycon3.Models;

public sealed class MapData
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required MapLayout Layout { get; init; }
    public MapMetadata Metadata { get; init; } = new();
    public List<ObjectEvent> ObjectEvents { get; init; } = [];
    public List<MapWarp> Warps { get; init; } = [];
    public List<CoordEvent> CoordEvents { get; init; } = [];
    public List<BgEvent> BgEvents { get; init; } = [];
    public List<MapConnection> Connections { get; init; } = [];
}

public sealed class MapMetadata
{
    public string Music { get; init; } = "";
    public string RegionMapSection { get; init; } = "";
    public string Weather { get; init; } = "";
    public string MapType { get; init; } = "";
    public string BattleScene { get; init; } = "";
    public bool RequiresFlash { get; init; }
    public bool AllowCycling { get; init; }
    public bool AllowEscaping { get; init; }
    public bool AllowRunning { get; init; }
    public bool ShowMapName { get; init; }
}

public sealed class MapLayout
{
    public required string Id { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int BorderWidth { get; init; }
    public required int BorderHeight { get; init; }
    public required string PrimaryTileset { get; init; }
    public required string SecondaryTileset { get; init; }
    public string BlockdataPath { get; init; } = "";
}

public sealed class ObjectEvent
{
    public string? LocalId { get; init; }
    public required string GraphicsId { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public int Elevation { get; init; }
    public string MovementType { get; init; } = "";
    public int MovementRangeX { get; init; }
    public int MovementRangeY { get; init; }
    public string TrainerType { get; init; } = "";
    public string TrainerSightOrBerryTreeId { get; init; } = "";
    public string Script { get; init; } = "";
    public string Flag { get; init; } = "";
}

public sealed class CoordEvent
{
    public required string Type { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public int Elevation { get; init; }
    public string Var { get; init; } = "";
    public string VarValue { get; init; } = "";
    public string Script { get; init; } = "";
}

public sealed class BgEvent
{
    public required string Type { get; init; }
    public required int X { get; init; }
    public required int Y { get; init; }
    public int Elevation { get; init; }
    public string PlayerFacingDir { get; init; } = "";
    public string Script { get; init; } = "";
    // For hidden items
    public string Item { get; init; } = "";
    public string HiddenItemId { get; init; } = "";
}

public sealed class MapWarp
{
    public required int X { get; init; }
    public required int Y { get; init; }
    public int Elevation { get; init; }
    public required string DestMap { get; init; }
    public required int DestWarpId { get; init; }
}

public sealed class MapConnection
{
    public required string Direction { get; init; }
    public required int Offset { get; init; }
    public required string MapId { get; init; }
}
