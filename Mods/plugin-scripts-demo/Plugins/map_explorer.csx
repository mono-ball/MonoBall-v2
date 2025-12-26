// Map Explorer Plugin Script
// Demonstrates: Map API usage, entity querying, component access
// This script provides information about loaded maps and entities

using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using Arch.Core;
using System.Collections.Generic;
using System.Linq;

public class MapExplorerScript : ScriptBase
{
    private int _queryCount = 0;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        Context.Logger.Information("Map Explorer initialized");
        
        // Log initial map information
        LogMapInformation();
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Track when maps are loaded/unloaded
        On<MapLoadedEvent>(OnMapLoaded);
        On<MapUnloadedEvent>(OnMapUnloaded);
        
        Context.Logger.Information("Map Explorer: Event handlers registered");
    }

    private void OnMapLoaded(MapLoadedEvent evt)
    {
        Context.Logger.Information(
            "Map Explorer: Map '{MapId}' loaded",
            evt.MapId
        );
        
        // Query entities in the newly loaded map
        QueryMapEntities(evt.MapId);
    }

    private void OnMapUnloaded(MapUnloadedEvent evt)
    {
        Context.Logger.Information(
            "Map Explorer: Map '{MapId}' unloaded",
            evt.MapId
        );
    }

    private void LogMapInformation()
    {
        // Get all loaded map IDs
        var loadedMaps = Context.Apis.Map.GetLoadedMapIds().ToList();
        
        Context.Logger.Information(
            "Map Explorer: {Count} map(s) currently loaded",
            loadedMaps.Count
        );
        
        foreach (var mapId in loadedMaps)
        {
            var mapEntity = Context.Apis.Map.GetMapEntity(mapId);
            if (mapEntity.HasValue)
            {
                Context.Logger.Information(
                    "  - Map '{MapId}' (Entity ID: {EntityId})",
                    mapId,
                    mapEntity.Value.Id
                );
            }
        }
    }

    private void QueryMapEntities(string mapId)
    {
        _queryCount++;
        
        // Query all entities with PositionComponent (most entities have this)
        int entityCount = 0;
        int npcCount = 0;
        int playerCount = 0;
        
        Context.Query<PositionComponent>((Entity entity, ref PositionComponent pos) =>
        {
            entityCount++;
        });
        
        // Query entities with both PositionComponent and NpcComponent
        Context.Query<PositionComponent, NpcComponent>((Entity entity, ref PositionComponent pos, ref NpcComponent npc) =>
        {
            npcCount++;
        });
        
        // Query entities with both PositionComponent and PlayerComponent
        Context.Query<PositionComponent, PlayerComponent>((Entity entity, ref PositionComponent pos, ref PlayerComponent player) =>
        {
            playerCount++;
        });
        
        Context.Logger.Information(
            "Map Explorer [Query #{Count}]: Map '{MapId}' contains {Total} entities ({NPCs} NPCs, {Players} players)",
            _queryCount,
            mapId,
            entityCount,
            npcCount,
            playerCount
        );
    }

    public override void OnUnload()
    {
        Context.Logger.Information(
            "Map Explorer unloaded. Performed {Count} entity queries",
            _queryCount
        );
        base.OnUnload();
    }
}

