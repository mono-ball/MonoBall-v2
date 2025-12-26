using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using Arch.Core;
using MonoBall.Core.Scripting.Utilities;

/// <summary>
/// Copy Player behavior - NPC copies the player's facing direction.
/// Supports modes: normal (same direction), opposite (reverse), clockwise/counterclockwise.
/// </summary>
public class CopyPlayerBehavior : ScriptBase
{
    private Direction _lastPlayerDirection = Direction.South;
    private Direction _currentFacing = Direction.South;
    private string _copyMode = "normal";

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Get copy mode parameter
        _copyMode = Context.GetParameter<string>("copyMode", "normal");
        
        // Load persisted state
        _currentFacing = GetDirection("currentFacing", Direction.South);
        
        Context.Logger.Information(
            "Copy player behavior initialized. Mode: {Mode}",
            _copyMode
        );
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to player movement completion to detect direction changes
        On<MovementCompletedEvent>(OnMovementCompleted);
        
        // Also update immediately on initialization
        UpdateFacingDirection();
        
        Context.Logger.Debug("Copy player behavior: Event handlers registered");
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        // Check if this is the player entity
        var playerEntity = Context.Apis.Player.GetPlayerEntity();
        if (!playerEntity.HasValue || evt.Entity.Id != playerEntity.Value.Id)
        {
            return; // Not the player, ignore
        }

        // Player moved - update our facing direction
        // Don't update _lastPlayerDirection here - let UpdateFacingDirection() handle it
        UpdateFacingDirection();
    }

    private void UpdateFacingDirection()
    {
        // Get player's facing direction by querying for player entity with GridMovement
        Direction playerDirection = Direction.South;
        bool foundPlayer = false;
        
        Context.Query<PlayerComponent, GridMovement>((Entity playerEntity, ref PlayerComponent player, ref GridMovement movement) =>
        {
            playerDirection = movement.FacingDirection;
            foundPlayer = true;
        });
        
        if (!foundPlayer)
        {
            return; // No player found
        }
        
        // Only update if player direction changed (compare BEFORE updating)
        if (playerDirection == _lastPlayerDirection)
        {
            return; // No change
        }
        
        _lastPlayerDirection = playerDirection;
        
        // Apply copy mode transformation
        _currentFacing = _copyMode switch
        {
            "normal" => playerDirection,
            "opposite" => DirectionHelper.GetOpposite(playerDirection),
            "clockwise" => DirectionHelper.RotateClockwise(playerDirection),
            "counterclockwise" => DirectionHelper.RotateCounterClockwise(playerDirection),
            _ => playerDirection,
        };

        // Update NPC facing direction
        if (Context.Entity.HasValue)
        {
            SetFacingDirection(_currentFacing);
        }

        Context.Logger.Debug(
            "Copied player direction {PlayerDir} -> {NpcDir} (mode: {Mode})",
            playerDirection,
            _currentFacing,
            _copyMode
        );
        
        SaveState();
    }

    public override void OnUnload()
    {
        SaveState();
        Context.Logger.Debug("Copy player behavior unloaded");
        base.OnUnload();
    }

    private void SaveState()
    {
        SetDirection("currentFacing", _currentFacing);
        SetDirection("lastPlayerDirection", _lastPlayerDirection);
        Set("copyMode", _copyMode);
    }
}
