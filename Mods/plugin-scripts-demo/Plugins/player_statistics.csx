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
        // Build statistics message demonstrating text control codes and effects:
        // Control codes:
        // - \n = newline (continues on same page if space available)
        // - \l = scroll (wait, then scroll up keeping previous line visible)
        // - \p = page break (wait, then clear and start fresh)
        // Text effects (use full definition ID):
        // - {FX:base:texteffect:rainbow} = rainbow color cycling
        // - {FX:base:texteffect:wave} = wavy text animation
        // - {FX:base:texteffect:bouncy} = bouncing text
        // - {FX:base:texteffect:shake} = shaking text
        // - {FX:base:texteffect:scary} = shaking + dark red cycling
        // - {FX:base:texteffect:dizzy} = wave + sidestep combo
        // - {/FX} = end effect
        // Color/style codes:
        // - {COLOR:r,g,b} = change text color
        // - {RESET} = reset to defaults
        //
        // Page 1: Rainbow title with wave effect
        // Page 2: Statistics with colored numbers and effects
        var message =
            "{FX:base:texteffect:spooky}Player Statistics{/FX}\\p" +                          // Rainbow title
            "Total Steps: {FX:base:texteffect:wave}{COLOR:50,205,50}" + _totalSteps + "{/FX}{RESET}\\l" +  // Green wavy steps
            "Maps Visited: {FX:base:texteffect:bouncy}{COLOR:100,149,237}" + _mapsVisited + "{/FX}{RESET}\\l" +  // Blue bouncy maps
            "{FX:base:texteffect:pulse}Keep exploring!{/FX}";                                    // Wavy encouragement

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

