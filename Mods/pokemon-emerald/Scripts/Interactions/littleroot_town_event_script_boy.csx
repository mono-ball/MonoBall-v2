using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;

/// <summary>
/// Interaction script for the boy NPC in Littleroot Town.
/// Shows dialogue when the player interacts with him.
/// Based on Pokemon Emerald's LittlerootTown_EventScript_Boy.
/// </summary>
public class LittlerootTownEventScriptBoy : ScriptBase
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
    /// Shows dialogue about Prof. Birch.
    /// </summary>
    /// <param name="evt">The interaction triggered event containing player entity information.</param>
    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Face the player when interacted with
        FacePlayer(evt.PlayerEntity);

        // Show dialogue - exact text from Pokemon Emerald
        // Control codes:
        // - \n = newline (continues on same page if space available)
        // - \l = scroll (wait, then scroll up keeping previous line visible)
        // - \p = page break (wait, then clear and start fresh)
        // All text on same page except "When does PROF. BIRCH" which starts a new page
        Context.Apis.MessageBox.ShowMessage(
            "PROF. BIRCH spends days in his LAB\\l" +
            "studying, then he'll suddenly go out in\\l" +
            "the wild to do more research...\\p" +
            "When does PROF. BIRCH spend time\\l" +
            "at home?"
        );
    }
}

