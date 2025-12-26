// Entity Spawner Plugin Script
// Demonstrates: Entity creation, component manipulation
// This script spawns a test entity when a map is loaded to demonstrate entity creation

using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using Microsoft.Xna.Framework;
using System;

public class EntitySpawnerScript : ScriptBase
{
    private int _spawnedCount = 0;
    private Random _random = new Random();

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Load persisted state
        _spawnedCount = Get<int>("spawnedCount", 0);
        
        Context.Logger.Information(
            "Entity Spawner initialized. Previously spawned: {Count} entities",
            _spawnedCount
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Spawn a test entity when a map is loaded
        On<MapLoadedEvent>(OnMapLoaded);
        
        Context.Logger.Information("Entity Spawner: Event handlers registered");
    }

    private void OnMapLoaded(MapLoadedEvent evt)
    {
        // Spawn a test entity when a new map loads
        SpawnTestEntity();
    }

    private void SpawnTestEntity()
    {
        try
        {
            // Get player position to spawn near player
            var playerEntity = Context.Apis.Player.GetPlayerEntity();
            Vector2 spawnPosition = new Vector2(100, 100); // Default spawn position
            
            if (playerEntity.HasValue)
            {
                var playerPos = Context.Apis.Player.GetPlayerPosition();
                if (playerPos.HasValue)
                {
                    // Spawn 2 tiles away from player in random direction
                    var offsetX = (_random.Next(-2, 3)) * 16; // 16 pixels per tile
                    var offsetY = (_random.Next(-2, 3)) * 16;
                    spawnPosition = new Vector2(
                        playerPos.Value.PixelX + offsetX,
                        playerPos.Value.PixelY + offsetY
                    );
                }
            }

            // Create a test entity with basic components
            var entity = Context.CreateEntity(
                new PositionComponent
                {
                    PixelX = spawnPosition.X,
                    PixelY = spawnPosition.Y,
                    X = (int)(spawnPosition.X / 16), // Assuming 16x16 tiles
                    Y = (int)(spawnPosition.Y / 16)
                },
                new RenderableComponent
                {
                    IsVisible = true
                }
            );

            _spawnedCount++;
            Set("spawnedCount", _spawnedCount);
            
            Context.Logger.Information(
                "Spawned test entity #{Count} at ({X}, {Y})",
                _spawnedCount,
                spawnPosition.X,
                spawnPosition.Y
            );
        }
        catch (Exception ex)
        {
            Context.Logger.Error(ex, "Error spawning test entity");
        }
    }

    public override void OnUnload()
    {
        Context.Logger.Information(
            "Entity Spawner unloaded. Total entities spawned: {Count}",
            _spawnedCount
        );
        base.OnUnload();
    }
}

