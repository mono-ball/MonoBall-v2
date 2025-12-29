using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Systems;

/// <summary>
///     System that detects when the player enters or exits shader regions.
///     Applies/reverts shaders based on region configuration.
///     Saved shader states are stored externally to avoid Dictionary in ECS components.
/// </summary>
public class ShaderRegionDetectionSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
{
    private readonly List<ShaderRegionEnteredEvent> _enteredEvents = new();
    private readonly List<ShaderRegionExitedEvent> _exitedEvents = new();
    private readonly ILogger _logger;
    private readonly QueryDescription _playerQuery;
    private readonly QueryDescription _regionQuery;

    // External storage for saved shader states (avoids Dictionary in component)
    private readonly Dictionary<Entity, Dictionary<ShaderLayer, string?>> _savedStates = new();
    private readonly ShaderTransitionSystem? _transitionSystem;

    // Track current active region per player
    private Entity? _currentActiveRegion;
    private int _currentRegionPriority = int.MinValue;

    /// <summary>
    ///     Initializes a new instance of the ShaderRegionDetectionSystem.
    /// </summary>
    public ShaderRegionDetectionSystem(
        World world,
        ShaderTransitionSystem? transitionSystem,
        ILogger logger
    )
        : base(world)
    {
        _transitionSystem = transitionSystem;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _playerQuery = new QueryDescription().WithAll<PlayerComponent, PositionComponent>();
        _regionQuery = new QueryDescription().WithAll<ShaderRegionComponent>();
    }

    /// <summary>
    ///     Disposes of system resources.
    /// </summary>
    public new void Dispose()
    {
        _savedStates.Clear();
        _enteredEvents.Clear();
        _exitedEvents.Clear();
        _currentActiveRegion = null;
        _currentRegionPriority = int.MinValue;
    }

    /// <summary>
    ///     Gets the execution priority for this system.
    /// </summary>
    public int Priority => SystemPriority.ShaderRegionDetection;

    /// <inheritdoc />
    public override void Update(in float deltaTime)
    {
        _enteredEvents.Clear();
        _exitedEvents.Clear();

        // Get player position
        Entity? playerEntity = null;
        var playerTileX = 0;
        var playerTileY = 0;
        string? playerMapId = null;

        World.Query(
            in _playerQuery,
            (Entity entity, ref PositionComponent pos) =>
            {
                playerEntity = entity;
                playerTileX = pos.X;
                playerTileY = pos.Y;

                if (World.Has<MapComponent>(entity))
                    playerMapId = World.Get<MapComponent>(entity).MapId;
            }
        );

        if (!playerEntity.HasValue)
            return;

        // Find all regions player is in, sorted by priority
        var regionsPlayerIsIn = new List<(Entity Entity, ShaderRegionComponent Region)>();

        World.Query(
            in _regionQuery,
            (Entity entity, ref ShaderRegionComponent region) =>
            {
                // Only check regions on player's current map
                if (region.MapId != playerMapId)
                    return;

                var wasInside = region.IsPlayerInside;
                var isNowInside = region.Contains(playerTileX, playerTileY);

                if (isNowInside)
                    regionsPlayerIsIn.Add((entity, region));

                if (!wasInside && isNowInside)
                    // Player entered region
                    region.IsPlayerInside = true;
                else if (wasInside && !isNowInside)
                    // Player exited region
                    region.IsPlayerInside = false;
            }
        );

        // Sort by priority (descending) to find highest priority region
        regionsPlayerIsIn.Sort((a, b) => b.Region.Priority.CompareTo(a.Region.Priority));

        Entity? newActiveRegion = regionsPlayerIsIn.Count > 0 ? regionsPlayerIsIn[0].Entity : null;
        var newPriority =
            regionsPlayerIsIn.Count > 0 ? regionsPlayerIsIn[0].Region.Priority : int.MinValue;

        // Check if active region changed
        if (newActiveRegion != _currentActiveRegion)
        {
            // Exit old region
            if (_currentActiveRegion.HasValue && World.IsAlive(_currentActiveRegion.Value))
            {
                var oldRegion = World.Get<ShaderRegionComponent>(_currentActiveRegion.Value);

                // Revert to saved shader state
                RevertShaderState(oldRegion);

                _exitedEvents.Add(
                    new ShaderRegionExitedEvent
                    {
                        RegionEntity = _currentActiveRegion.Value,
                        PlayerEntity = playerEntity.Value,
                        MapId = oldRegion.MapId,
                        RegionId = oldRegion.RegionId,
                        ShaderId = oldRegion.LayerShaderId,
                        Layer = oldRegion.TargetLayer,
                    }
                );
            }

            // Enter new region
            if (newActiveRegion.HasValue)
            {
                var newRegion = regionsPlayerIsIn[0].Region;

                // Save current shader state before applying region shader
                SaveCurrentShaderState(newActiveRegion.Value, newRegion);

                // Apply region shader
                ApplyRegionShader(newRegion);

                _enteredEvents.Add(
                    new ShaderRegionEnteredEvent
                    {
                        RegionEntity = newActiveRegion.Value,
                        PlayerEntity = playerEntity.Value,
                        MapId = newRegion.MapId,
                        RegionId = newRegion.RegionId,
                        ShaderId = newRegion.LayerShaderId,
                        Layer = newRegion.TargetLayer,
                    }
                );
            }

            _currentActiveRegion = newActiveRegion;
            _currentRegionPriority = newPriority;
        }

        // Fire events AFTER processing (Arch ECS constraint)
        foreach (var evt in _enteredEvents)
        {
            var e = evt;
            EventBus.Send(ref e);
        }

        foreach (var evt in _exitedEvents)
        {
            var e = evt;
            EventBus.Send(ref e);
        }
    }

    private void SaveCurrentShaderState(Entity regionEntity, ShaderRegionComponent region)
    {
        // Find current shader on target layer
        string? currentShaderId = null;
        var layerQuery = new QueryDescription().WithAll<RenderingShaderComponent>();

        World.Query(
            in layerQuery,
            (ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == region.TargetLayer && shader.IsEnabled)
                    currentShaderId = shader.ShaderId;
            }
        );

        if (!_savedStates.ContainsKey(regionEntity))
            _savedStates[regionEntity] = new Dictionary<ShaderLayer, string?>();

        _savedStates[regionEntity][region.TargetLayer] = currentShaderId;

        _logger.Debug(
            "Saved shader state for region {RegionId}: {ShaderId}",
            region.RegionId,
            currentShaderId ?? "none"
        );
    }

    private void ApplyRegionShader(ShaderRegionComponent region)
    {
        if (string.IsNullOrEmpty(region.LayerShaderId))
            return;

        // Find shader entity on target layer
        Entity? shaderEntity = null;
        var layerQuery = new QueryDescription().WithAll<RenderingShaderComponent>();

        World.Query(
            in layerQuery,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == region.TargetLayer)
                    shaderEntity = entity;
            }
        );

        if (shaderEntity.HasValue && _transitionSystem != null)
        {
            var currentShader = World.Get<RenderingShaderComponent>(shaderEntity.Value);
            _transitionSystem.StartTransition(
                shaderEntity.Value,
                currentShader.ShaderId,
                region.LayerShaderId,
                region.TransitionDuration,
                region.TransitionEasing
            );
        }
        else
        {
            _logger.Warning(
                "Could not find shader entity on layer {Layer} to apply region shader",
                region.TargetLayer
            );
        }
    }

    private void RevertShaderState(ShaderRegionComponent region)
    {
        if (!_currentActiveRegion.HasValue)
            return;

        if (!_savedStates.TryGetValue(_currentActiveRegion.Value, out var states))
            return;

        if (!states.TryGetValue(region.TargetLayer, out var savedShaderId))
            return;

        // Find shader entity on target layer
        Entity? shaderEntity = null;
        var layerQuery = new QueryDescription().WithAll<RenderingShaderComponent>();

        World.Query(
            in layerQuery,
            (Entity entity, ref RenderingShaderComponent shader) =>
            {
                if (shader.Layer == region.TargetLayer)
                    shaderEntity = entity;
            }
        );

        if (
            shaderEntity.HasValue
            && _transitionSystem != null
            && !string.IsNullOrEmpty(savedShaderId)
        )
        {
            var currentShader = World.Get<RenderingShaderComponent>(shaderEntity.Value);
            _transitionSystem.StartTransition(
                shaderEntity.Value,
                currentShader.ShaderId,
                savedShaderId,
                region.TransitionDuration,
                region.TransitionEasing
            );
        }

        // Clean up saved state
        states.Remove(region.TargetLayer);
        if (states.Count == 0)
            _savedStates.Remove(_currentActiveRegion.Value);

        _logger.Debug(
            "Reverted shader state for region {RegionId} to {ShaderId}",
            region.RegionId,
            savedShaderId ?? "none"
        );
    }
}
