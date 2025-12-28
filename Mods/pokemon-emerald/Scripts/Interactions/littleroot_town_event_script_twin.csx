using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;

/// <summary>
/// Constants for Pokemon Emerald mod flags and variables.
/// Centralizes flag and variable ID strings to prevent duplication and improve maintainability.
/// </summary>
public static class PokemonEmeraldConstants
{
    /// <summary>
    /// Event flags for Pokemon Emerald story progression.
    /// </summary>
    public static class Flags
    {
        /// <summary>
        /// Flag set when the player's adventure has started.
        /// Used to determine if the player has begun their journey.
        /// </summary>
        public const string AdventureStarted = "pokemon-emerald:flag:event/adventure_started";

        /// <summary>
        /// Flag set when the player has rescued Prof. Birch from wild Pokemon.
        /// Used in Littleroot Town events.
        /// </summary>
        public const string RescuedBirch = "pokemon-emerald:flag:event/rescued_birch";
    }

    /// <summary>
    /// Variables for Pokemon Emerald game state.
    /// </summary>
    public static class Variables
    {
        /// <summary>
        /// Variable tracking the state of Littleroot Town events.
        /// 0 = default state, 2 = player has been asked to investigate.
        /// </summary>
        public const string LittlerootTownState = "pokemon-emerald:var:map/littleroot_town_state";
    }
}

/// <summary>
/// Interaction script for the twin NPCs in Littleroot Town.
/// Shows dialogue when the player interacts with them.
/// Based on Pokemon Emerald's LittlerootTown_EventScript_Twin.
/// 
/// Dialogue branches based on game state:
/// - If FLAG_ADVENTURE_STARTED: "Are you going to catch POKéMON? Good luck!"
/// - Else if FLAG_RESCUED_BIRCH: "You saved PROF. BIRCH! I'm so glad!"
/// - Else if VAR_LITTLEROOT_TOWN_STATE != 0: "Can you go see what's happening for me?" (sets state to 2)
/// - Else: "If you go in grass, wild POKéMON will jump out!"
/// </summary>
public class LittlerootTownEventScriptTwin : ScriptBase
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
    /// Shows dialogue based on game state flags and variables.
    /// </summary>
    /// <param name="evt">The interaction triggered event containing player entity information.</param>
    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Face the player when interacted with
        FacePlayer(evt.PlayerEntity);

        // Check flags and variables in priority order (matching original implementation)
        // Control codes:
        // - \n = newline (continues on same page if space available)
        // - \l = scroll (wait, then scroll up keeping previous line visible)
        // - \p = page break (wait, then clear and start fresh)
        if (Context.Apis.Flags.GetFlag(PokemonEmeraldConstants.Flags.AdventureStarted))
        {
            // Adventure has started - show good luck message
            Context.Apis.MessageBox.ShowMessage(
                "Are you going to catch POKéMON?\\l" +
                "Good luck!"
            );
        }
        else if (Context.Apis.Flags.GetFlag(PokemonEmeraldConstants.Flags.RescuedBirch))
        {
            // Player saved Birch - show thank you message
            Context.Apis.MessageBox.ShowMessage(
                "You saved PROF. BIRCH!\\l" +
                "I'm so glad!"
            );
        }
        else
        {
            // Check town state variable
            int townState = Context.Apis.Flags.GetVariable<int>(PokemonEmeraldConstants.Variables.LittlerootTownState);
            
            if (townState != 0)
            {
                // Town state is not 0 - twin wants player to investigate
                Context.Apis.MessageBox.ShowMessage(
                    "Um, hi!\\p" +
                    "There are scary \ue0a7\ue0a8 outside!\\l" +
                    "I can hear their cries!\\p" +
                    "I want to go see what's going on,\\l" +
                    "but I don't have any \ue0a7\ue0a8…\\p" +
                    "Can you go see what's happening\\l" +
                    "for me?"
                );
                
                // Set town state to 2 (player has been asked to investigate)
                Context.Apis.Flags.SetVariable(PokemonEmeraldConstants.Variables.LittlerootTownState, 2);
            }
            else
            {
                // Default message - warning about grass
                Context.Apis.MessageBox.ShowMessage(
                    "Um, um, um!\\p" +
                    "If you go outside and go in the grass,\\l" +
                    "wild \ue0a7\ue0a8 will jump out!"
                );
            }
        }
    }
}

