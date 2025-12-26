# Plugin Scripts Demo Mod

This mod demonstrates the plugin scripting capabilities of MonoBall's scripting system. Plugin scripts are standalone scripts that run independently of entities and can:

- Subscribe to global events
- Create and destroy entities
- Query entities in the world
- Use game APIs (Player, Map, Movement, Camera, etc.)
- Store persistent state
- Create new game functionality

## Scripts Included

### 1. Event Tracker (`event_tracker.csx`)
**Demonstrates**: Event subscription, logging, state persistence

- Subscribes to multiple game events (MapLoaded, MapTransition, MovementCompleted, etc.)
- Logs all events with counters
- Persists event count across game sessions
- Shows how to subscribe to multiple event types

### 2. Entity Spawner (`entity_spawner.csx`)
**Demonstrates**: Entity creation, component manipulation

- Periodically spawns test entities near the player
- Creates entities with PositionComponent and RenderableComponent
- Uses Player API to get player position
- Shows how to create entities dynamically

### 3. Player Statistics (`player_statistics.csx`)
**Demonstrates**: Player API usage, state tracking, variable storage

- Tracks total steps taken by the player
- Tracks unique maps visited
- Stores statistics in both script state and global variables
- Shows how to use Player API to identify player entity

### 4. Map Explorer (`map_explorer.csx`)
**Demonstrates**: Map API usage, entity querying, component access

- Lists all loaded maps
- Queries entities when maps are loaded
- Counts NPCs and players in each map
- Shows how to use Map API and query entities

## Key Features Demonstrated

### Event Subscription
```csharp
public override void RegisterEventHandlers(ScriptContext context)
{
    On<MapLoadedEvent>(OnMapLoaded);
    On<MovementCompletedEvent>(OnMovementCompleted);
}
```

### Entity Creation
```csharp
var entity = Context.CreateEntity(
    new PositionComponent { Position = spawnPosition },
    new RenderableComponent { IsVisible = true }
);
```

### Entity Querying
```csharp
Context.Query<PositionComponent>((entity, ref PositionComponent pos) =>
{
    // Process each entity with PositionComponent
});
```

### State Persistence
```csharp
// Store state
Set("totalSteps", _totalSteps);

// Load state
_totalSteps = Get<int>("totalSteps", 0);
```

### API Usage
```csharp
// Player API
var playerEntity = Context.Apis.Player.GetPlayerEntity();
if (playerEntity.HasValue)
{
    var playerPos = Context.Apis.Player.GetPlayerPosition();
    if (playerPos.HasValue)
    {
        // Access player position
        var x = playerPos.Value.PixelX;
        var y = playerPos.Value.PixelY;
    }
}

// Map API
var loadedMaps = Context.Apis.Map.GetLoadedMapIds();
var mapEntity = Context.Apis.Map.GetMapEntity(mapId);
if (mapEntity.HasValue)
{
    // Use map entity
}
```

## Installation

1. Place this mod folder in your `Mods/` directory
2. The mod will be automatically loaded on game start
3. Plugin scripts will initialize after all systems are ready
4. Check the game logs to see script activity

## Notes

- Plugin scripts have no entity context (`Context.Entity == null`)
- State is stored in global variables (not entity-specific)
- Scripts persist state across game sessions
- All event subscriptions are automatically cleaned up on unload
- Scripts can be hot-reloaded during development

## Extending

To add your own plugin script:

1. Create a `.csx` file in the `Scripts/` directory
2. Inherit from `ScriptBase`
3. Implement `Initialize()` and `RegisterEventHandlers()`
4. Add the script path to `mod.json` under `"plugins"` array
5. Return an instance of your script class at the end of the file

Example:
```csharp
public class MyPluginScript : ScriptBase
{
    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        Context.Logger.Information("My plugin script initialized!");
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<MapLoadedEvent>(evt => {
            Context.Logger.Information("Map loaded: {MapId}", evt.MapId);
        });
    }
}

return new MyPluginScript();
```

