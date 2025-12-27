// Player Statistics Plugin Script
// Demonstrates: Player API usage, state tracking, variable storage
// This script tracks player statistics like steps taken, maps visited, etc.

using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using System;

public class PlayerStatisticsScript : ScriptBase
{
    private int _totalSteps = 0;
    private int _mapsVisited = 0;
    private int _lastMapIdHash = 0;

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Load persisted statistics
        _totalSteps = Get<int>("totalSteps", 0);
        _mapsVisited = Get<int>("mapsVisited", 0);
        
        Context.Logger.Information(
            "Player Statistics initialized. Steps: {Steps}, Maps: {Maps}",
            _totalSteps,
            _mapsVisited
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Track player movement
        On<MovementCompletedEvent>(OnMovementCompleted);
        
        // Track map transitions
        On<MapTransitionEvent>(OnMapTransition);
        
        Context.Logger.Information("Player Statistics: Event handlers registered");
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        // Check if this is the player entity
        var playerEntity = Context.Apis.Player.GetPlayerEntity();
        
        if (playerEntity.HasValue && evt.Entity.Id == playerEntity.Value.Id)
        {
            _totalSteps++;
            
            // Log milestone steps
            if (_totalSteps % 100 == 0)
            {
                Context.Logger.Information(
                    "Player Statistics: {Steps} steps taken!",
                    _totalSteps
                );
            }
            
            // Save state periodically
            if (_totalSteps % 50 == 0)
            {
                SaveStatistics();
            }
        }
    }

    private void OnMapTransition(MapTransitionEvent evt)
    {
        // Track unique maps visited (use target map)
        var currentMapHash = evt.TargetMapId?.GetHashCode() ?? 0;
        
        if (currentMapHash != _lastMapIdHash && !string.IsNullOrEmpty(evt.TargetMapId))
        {
            _mapsVisited++;
            _lastMapIdHash = currentMapHash;
            
            Context.Logger.Information(
                "Player Statistics: Visited map '{MapId}' (Total unique maps: {Count})",
                evt.TargetMapId,
                _mapsVisited
            );
            
            SaveStatistics();
            
            // Show statistics in message box when entering a new map
            ShowStatisticsMessageBox();
        }
    }
    
    private void ShowStatisticsMessageBox()
    {
        // Build statistics message demonstrating text control codes:
        // - \n = newline (continues on same page if space available)
        // - \l = scroll (wait, then scroll up keeping previous line visible)
        // - \p = page break (wait, then clear and start fresh)
        //
        // Page 1: "Player Statistics" (title with \p = fresh page after)
        // Page 2: Lines scroll smoothly using \l:
        //   - "Total Steps: X" + "Maps Visited: X" (2 lines visible)
        //   - After \l, scrolls up: "Maps Visited" stays, "Keep exploring" appears
        var message =
            "Player Statistics\\p" +              // Title alone, then clear
            $"Total Steps: {_totalSteps}\\l" +    // Line 1, scroll up after
            $"Maps Visited: {_mapsVisited}\\l" +  // Line 2 (now line 1), scroll up after
            "Keep exploring!";                     // Line 3 (now line 2), end

        // Show message box (use fast text speed for better UX)
        // Fast speed = 1 frame at 60 FPS = 1/60 = 0.0167 seconds
        Context.Apis.MessageBox.ShowMessage(message, textSpeedOverride: 0.017f);
    }

    private void SaveStatistics()
    {
        // Persist statistics using Get/Set methods
        Set("totalSteps", _totalSteps);
        Set("mapsVisited", _mapsVisited);
        
        // Also store in global variables for other scripts to access
        Context.Apis.Flags.SetVariable("player:stats:totalSteps", _totalSteps.ToString());
        Context.Apis.Flags.SetVariable("player:stats:mapsVisited", _mapsVisited.ToString());
    }

    public override void OnUnload()
    {
        // Save final statistics
        SaveStatistics();
        
        Context.Logger.Information(
            "Player Statistics unloaded. Final stats - Steps: {Steps}, Maps: {Maps}",
            _totalSteps,
            _mapsVisited
        );
        base.OnUnload();
    }
}

