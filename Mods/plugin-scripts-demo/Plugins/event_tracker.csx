// Event Tracker Plugin Script
// Demonstrates: Event subscription, logging, state persistence
// This script tracks and logs all game events for debugging/analysis

using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using System;

public class EventTrackerScript : ScriptBase
{
    private int _eventCount = 0;
    private DateTime _startTime;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Load persisted state
        _eventCount = Get<int>("eventCount", 0);
        _startTime = DateTime.UtcNow;
        
        Context.Logger.Information(
            "Event Tracker initialized. Previous event count: {EventCount}",
            _eventCount
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to various game events
        On<MapLoadedEvent>(OnMapLoaded);
        On<MapUnloadedEvent>(OnMapUnloaded);
        On<MapTransitionEvent>(OnMapTransition);
        On<MovementCompletedEvent>(OnMovementCompleted);
        On<MovementStartedEvent>(OnMovementStarted);
        On<ScriptLoadedEvent>(OnScriptLoaded);
        On<ScriptUnloadedEvent>(OnScriptUnloaded);
        On<ScriptErrorEvent>(OnScriptError);
        
        Context.Logger.Information("Event Tracker: Subscribed to {Count} event types", 8);
    }

    private void OnMapLoaded(MapLoadedEvent evt)
    {
        _eventCount++;
        Context.Logger.Information(
            "[Event #{Count}] MapLoaded: {MapId}",
            _eventCount,
            evt.MapId
        );
        SaveState();
    }

    private void OnMapUnloaded(MapUnloadedEvent evt)
    {
        _eventCount++;
        Context.Logger.Information(
            "[Event #{Count}] MapUnloaded: {MapId}",
            _eventCount,
            evt.MapId
        );
        SaveState();
    }

    private void OnMapTransition(MapTransitionEvent evt)
    {
        _eventCount++;
        Context.Logger.Information(
            "[Event #{Count}] MapTransition: {SourceMapId} -> {TargetMapId}",
            _eventCount,
            evt.SourceMapId,
            evt.TargetMapId
        );
        SaveState();
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        _eventCount++;
        // Only log every 10th movement to avoid spam
        if (_eventCount % 10 == 0)
        {
            Context.Logger.Debug(
                "[Event #{Count}] MovementCompleted: Entity {EntityId}",
                _eventCount,
                evt.Entity.Id
            );
        }
        SaveState();
    }

    private void OnMovementStarted(MovementStartedEvent evt)
    {
        _eventCount++;
        // Only log every 10th movement to avoid spam
        if (_eventCount % 10 == 0)
        {
            Context.Logger.Debug(
                "[Event #{Count}] MovementStarted: Entity {EntityId}",
                _eventCount,
                evt.Entity.Id
            );
        }
    }

    private void OnScriptLoaded(ScriptLoadedEvent evt)
    {
        _eventCount++;
        Context.Logger.Information(
            "[Event #{Count}] ScriptLoaded: {ScriptId} on Entity {EntityId}",
            _eventCount,
            evt.ScriptDefinitionId,
            evt.Entity.Id
        );
        SaveState();
    }

    private void OnScriptUnloaded(ScriptUnloadedEvent evt)
    {
        _eventCount++;
        Context.Logger.Information(
            "[Event #{Count}] ScriptUnloaded: {ScriptId} from Entity {EntityId}",
            _eventCount,
            evt.ScriptDefinitionId,
            evt.Entity.Id
        );
        SaveState();
    }

    private void OnScriptError(ScriptErrorEvent evt)
    {
        _eventCount++;
        Context.Logger.Error(
            "[Event #{Count}] ScriptError: {ScriptId} - {ErrorMessage}",
            _eventCount,
            evt.ScriptDefinitionId,
            evt.ErrorMessage
        );
        SaveState();
    }

    private void SaveState()
    {
        // Persist state using Get/Set methods (stored in global variables for plugin scripts)
        Set("eventCount", _eventCount);
    }

    public override void OnUnload()
    {
        var runtime = DateTime.UtcNow - _startTime;
        Context.Logger.Information(
            "Event Tracker unloaded. Total events tracked: {EventCount} over {Runtime}",
            _eventCount,
            runtime
        );
        SaveState();
        base.OnUnload();
    }
}

