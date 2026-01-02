using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;

/// <summary>
/// Interaction script for the fat man NPC in Littleroot Town.
/// Shows dialogue when the player interacts with him.
/// </summary>
public class LittlerootTownEventScriptFatMan : ScriptBase
{
    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to interaction events with automatic entity filtering
        OnInteraction(OnInteractionTriggered);
    }

    public override void OnUnload()
    {
        base.OnUnload();
    }

    /// <summary>
    /// Handles the interaction event when the player interacts with this entity.
    /// Shows dialogue based on interaction count.
    /// </summary>
    /// <param name="evt">The interaction triggered event containing player entity information.</param>
    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Face the player when interacted with
        FacePlayer(evt.PlayerEntity);

        // Show dialogue based on interaction count
        ShowDialogueByCount(
            "This is Littleroot Town!",
            "It's a peaceful little town.",
            "Enjoy your stay!"
        );
    }
}

