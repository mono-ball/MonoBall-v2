namespace Porycon3.Services;

/// <summary>
/// Script-related ID transformations (scripts, behaviors, flags, trainers, variables).
/// </summary>
public static partial class IdTransformer
{
    #region Flag Prefixes

    private static readonly Dictionary<string, string> FlagPrefixes = new()
    {
        ["FLAG_HIDE_"] = "visibility",
        ["FLAG_HIDDEN_ITEM_"] = "hidden_item",
        ["FLAG_ITEM_"] = "item",
        ["FLAG_TEMP_"] = "temporary",
        ["FLAG_DECORATION_"] = "decoration",
        ["FLAG_DEFEATED_"] = "defeated",
        ["FLAG_TRAINER_"] = "trainer",
        ["FLAG_BADGE_"] = "badge",
        ["FLAG_RECEIVED_"] = "received",
        ["FLAG_DAILY_"] = "daily",
        ["FLAG_ENCOUNTERED_"] = "encountered",
        ["FLAG_UNLOCKED_"] = "unlock",
        ["FLAG_COMPLETED_"] = "story",
        ["FLAG_TRIGGERED_"] = "trigger",
        ["FLAG_INTERACTED_"] = "interaction",
        ["FLAG_CAUGHT_"] = "collection"
    };

    #endregion

    #region Behavior IDs

    /// <summary>
    /// Transform movement type to behavior ID.
    /// MOVEMENT_TYPE_LOOK_AROUND -> base:script:behavior/look_around
    /// </summary>
    public static string BehaviorId(string movementType)
    {
        if (string.IsNullOrEmpty(movementType))
            return "";

        var name = movementType;
        if (name.StartsWith("MOVEMENT_TYPE_", StringComparison.OrdinalIgnoreCase))
            name = name[14..];

        return CreateId("script", "behavior", name);
    }

    #endregion

    #region Flag IDs

    /// <summary>
    /// Transform flag to unified format.
    /// FLAG_HIDE_LITTLEROOT_TOWN_FAT_MAN -> base:flag:visibility/littleroot_town_fat_man
    /// </summary>
    public static string FlagId(string pokeemeraldFlag)
    {
        if (string.IsNullOrEmpty(pokeemeraldFlag) || pokeemeraldFlag == "0")
            return "";

        var flagName = pokeemeraldFlag;
        var category = "misc";

        // Determine category from prefix
        foreach (var (prefix, cat) in FlagPrefixes)
        {
            if (flagName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                flagName = flagName[prefix.Length..];
                category = cat;
                break;
            }
        }

        // Handle generic FLAG_ prefix
        if (category == "misc" && flagName.StartsWith("FLAG_", StringComparison.OrdinalIgnoreCase))
        {
            flagName = flagName[5..];
        }

        return CreateId("flag", category, flagName);
    }

    #endregion

    #region Script IDs

    /// <summary>
    /// Transform script reference to script ID.
    /// LittlerootTown_EventScript_Twin -> base:script:map/littleroot_town_event_script_twin
    /// </summary>
    public static string ScriptId(string pokeemeraldScript)
    {
        if (string.IsNullOrEmpty(pokeemeraldScript) ||
            pokeemeraldScript == "NULL" ||
            pokeemeraldScript == "0x0" ||
            pokeemeraldScript == "0")
            return "";

        return CreateId("script", "map", pokeemeraldScript);
    }

    /// <summary>
    /// Transform script reference to interaction ID.
    /// LittlerootTown_EventScript_Twin -> base:script:interaction/littleroot_town_event_script_twin
    /// </summary>
    public static string InteractionId(string pokeemeraldScript)
    {
        if (string.IsNullOrEmpty(pokeemeraldScript) ||
            pokeemeraldScript == "NULL" ||
            pokeemeraldScript == "0x0" ||
            pokeemeraldScript == "0")
            return "";

        return CreateId("script", "interaction", pokeemeraldScript);
    }

    #endregion

    #region Trainer IDs

    /// <summary>
    /// Transform trainer type to trainer ID.
    /// TRAINER_TYPE_NORMAL -> base:trainer:normal/default
    /// </summary>
    public static string TrainerId(string trainerType)
    {
        if (string.IsNullOrEmpty(trainerType) || trainerType == "TRAINER_TYPE_NONE")
            return "";

        var name = trainerType;
        if (name.StartsWith("TRAINER_TYPE_", StringComparison.OrdinalIgnoreCase))
            name = name[13..];
        else if (name.StartsWith("TRAINER_", StringComparison.OrdinalIgnoreCase))
            name = name[8..];

        var normalized = Normalize(name);
        var parts = normalized.Split('_', 2);

        if (parts.Length == 2)
            return CreateId("trainer", parts[0], parts[1]);

        return CreateId("trainer", parts[0], "default");
    }

    #endregion

    #region Variable IDs

    /// <summary>
    /// Transform variable to unified format.
    /// VAR_ROUTE101_STATE -> base:variable:hoenn/route101_state
    /// </summary>
    public static string VariableId(string pokeemeraldVar, string? region = null)
    {
        if (string.IsNullOrEmpty(pokeemeraldVar))
            return "";

        var name = pokeemeraldVar;
        if (name.StartsWith("VAR_", StringComparison.OrdinalIgnoreCase))
            name = name[4..];

        return CreateId("variable", region ?? Region, name);
    }

    #endregion
}
