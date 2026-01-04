using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Scripting.Utilities;

/// <summary>
/// Copy Player behavior - NPC copies the player's facing direction.
/// Supports modes: normal (same direction), opposite (reverse), clockwise/counterclockwise.
/// </summary>
public class CopyPlayerBehavior : ScriptBase
{
    private string _copyMode = "normal";

    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        _copyMode = Context.GetParameter<string>("copyMode", "normal");
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<MovementCompletedEvent>(OnMovementCompleted);
        UpdateFacingDirection();
    }

    private void OnMovementCompleted(MovementCompletedEvent evt)
    {
        var playerEntity = Context.Apis.Player.GetPlayerEntity();
        if (!playerEntity.HasValue || evt.Entity.Id != playerEntity.Value.Id)
            return;

        UpdateFacingDirection();
    }

    private void UpdateFacingDirection()
    {
        var playerEntity = Context.Apis.Player.GetPlayerEntity();
        if (!playerEntity.HasValue)
            return;

        var playerDirection = Context.Apis.Npc.GetFacingDirection(playerEntity.Value);
        if (!playerDirection.HasValue)
            return;

        var npcDirection = _copyMode switch
        {
            "normal" => playerDirection.Value,
            "opposite" => DirectionHelper.GetOpposite(playerDirection.Value),
            "clockwise" => DirectionHelper.RotateClockwise(playerDirection.Value),
            "counterclockwise" => DirectionHelper.RotateCounterClockwise(playerDirection.Value),
            _ => playerDirection.Value,
        };

        SetFacingDirection(npcDirection);
    }
}
