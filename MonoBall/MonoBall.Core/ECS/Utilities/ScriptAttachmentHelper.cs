using System;
using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.ECS.Utilities;

/// <summary>
///     Helper methods for attaching scripts to entities.
///     Ensures consistency and automatically marks ScriptChangeTracker dirty.
/// </summary>
public static class ScriptAttachmentHelper
{
    /// <summary>
    ///     Helper method to set script attachment and mark dirty.
    ///     Use this instead of directly modifying ScriptAttachmentComponent.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The entity to attach script to.</param>
    /// <param name="scriptId">The script definition ID.</param>
    /// <param name="data">The script attachment data.</param>
    /// <exception cref="ArgumentNullException">Thrown when world or data is null.</exception>
    public static void SetScriptAttachment(
        World world,
        Entity entity,
        string scriptId,
        ScriptAttachmentData data
    )
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("Script ID cannot be null or empty.", nameof(scriptId));

        if (!world.Has<ScriptAttachmentComponent>(entity))
        {
            world.Add(entity, new ScriptAttachmentComponent());
        }

        ref var component = ref world.Get<ScriptAttachmentComponent>(entity);
        if (component.Scripts == null)
            component.Scripts = new Dictionary<string, ScriptAttachmentData>();

        component.Scripts[scriptId] = data;
        ScriptChangeTracker.MarkDirty(); // Automatic dirty marking
    }

    /// <summary>
    ///     Sets a script's IsActive state and marks the tracker dirty.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The entity with ScriptAttachmentComponent.</param>
    /// <param name="scriptDefinitionId">The script definition ID to modify.</param>
    /// <param name="isActive">The new active state.</param>
    /// <returns>True if the script was found and modified, false otherwise.</returns>
    public static bool SetScriptActive(
        World world,
        Entity entity,
        string scriptDefinitionId,
        bool isActive
    )
    {
        if (!world.Has<ScriptAttachmentComponent>(entity))
            return false;

        ref var component = ref world.Get<ScriptAttachmentComponent>(entity);
        if (component.Scripts == null || !component.Scripts.ContainsKey(scriptDefinitionId))
            return false;

        var attachment = component.Scripts[scriptDefinitionId];
        attachment.IsActive = isActive;
        component.Scripts[scriptDefinitionId] = attachment;
        world.Set(entity, component);
        ScriptChangeTracker.MarkDirty();
        return true;
    }

    /// <summary>
    ///     Pauses a script by setting IsActive to false.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The entity with ScriptAttachmentComponent.</param>
    /// <param name="scriptDefinitionId">The script definition ID to pause.</param>
    /// <returns>True if the script was found and paused, false otherwise.</returns>
    public static bool PauseScript(World world, Entity entity, string scriptDefinitionId)
    {
        return SetScriptActive(world, entity, scriptDefinitionId, false);
    }

    /// <summary>
    ///     Resumes a script by setting IsActive to true.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="entity">The entity with ScriptAttachmentComponent.</param>
    /// <param name="scriptDefinitionId">The script definition ID to resume.</param>
    /// <returns>True if the script was found and resumed, false otherwise.</returns>
    public static bool ResumeScript(World world, Entity entity, string scriptDefinitionId)
    {
        return SetScriptActive(world, entity, scriptDefinitionId, true);
    }
}
