using System.Collections.Generic;

namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Component that attaches scripts to an entity.
///     Supports multiple scripts per entity via a collection-based approach.
///     Note: Contains a reference type (Dictionary) but this is necessary to support multiple scripts
///     since Arch ECS doesn't support multiple instances of the same component type.
///     Script instances are stored in ScriptLifecycleSystem, not in this component.
/// </summary>
public struct ScriptAttachmentComponent
{
    /// <summary>
    ///     Dictionary of script attachments keyed by ScriptDefinitionId.
    ///     Allows multiple scripts to be attached to the same entity.
    ///     Must be initialized before use (use EnsureInitialized() or constructor).
    /// </summary>
    public Dictionary<string, ScriptAttachmentData> Scripts { get; set; }

    /// <summary>
    ///     Initializes a new instance of ScriptAttachmentComponent with an empty scripts dictionary.
    /// </summary>
    public ScriptAttachmentComponent()
    {
        Scripts = new Dictionary<string, ScriptAttachmentData>();
    }

    /// <summary>
    ///     Ensures the Scripts dictionary is initialized.
    ///     Call this before accessing Scripts if the component might not be initialized.
    /// </summary>
    public void EnsureInitialized()
    {
        if (Scripts == null)
            Scripts = new Dictionary<string, ScriptAttachmentData>();
    }
}

/// <summary>
///     Data for a single script attachment.
/// </summary>
public struct ScriptAttachmentData
{
    /// <summary>
    ///     The script definition ID (e.g., "base:script:behavior/stationary").
    ///     References a ScriptDefinition in the DefinitionRegistry.
    /// </summary>
    public string ScriptDefinitionId { get; set; }

    /// <summary>
    ///     Execution priority (higher = executes first).
    ///     Defaults to priority from ScriptDefinition if not set.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    ///     Whether this script is active. Inactive scripts are skipped.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    ///     The mod ID that owns this script. Used for script resolution.
    /// </summary>
    public string ModId { get; set; }

    /// <summary>
    ///     Internal: Whether the script has been initialized.
    ///     Used by ScriptLifecycleSystem to track initialization state.
    /// </summary>
    internal bool IsInitialized { get; set; }
}
