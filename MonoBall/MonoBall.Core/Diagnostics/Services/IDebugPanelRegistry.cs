namespace MonoBall.Core.Diagnostics.Services;

using System;
using System.Collections.Generic;
using Panels;

/// <summary>
/// Service for registering and managing debug panels.
/// </summary>
public interface IDebugPanelRegistry
{
    /// <summary>
    /// Gets all registered panels.
    /// </summary>
    IReadOnlyList<IDebugPanel> Panels { get; }

    /// <summary>
    /// Gets all panel categories.
    /// </summary>
    IReadOnlyList<string> Categories { get; }

    /// <summary>
    /// Registers a debug panel.
    /// </summary>
    /// <param name="panel">The panel to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when panel is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a panel with the same ID is already registered.</exception>
    void Register(IDebugPanel panel);

    /// <summary>
    /// Unregisters a debug panel.
    /// </summary>
    /// <param name="panelId">The ID of the panel to unregister.</param>
    /// <returns>True if the panel was unregistered, false if not found.</returns>
    bool Unregister(string panelId);

    /// <summary>
    /// Gets a panel by ID.
    /// </summary>
    /// <param name="panelId">The panel ID.</param>
    /// <returns>The panel, or null if not found.</returns>
    IDebugPanel? GetPanel(string panelId);

    /// <summary>
    /// Gets panels by category.
    /// </summary>
    /// <param name="category">The category name.</param>
    /// <returns>Panels in the specified category, sorted by SortOrder.</returns>
    IReadOnlyList<IDebugPanel> GetPanelsByCategory(string category);

    /// <summary>
    /// Sets the visibility of a panel by ID.
    /// </summary>
    /// <param name="panelId">The panel ID.</param>
    /// <param name="visible">Whether the panel should be visible.</param>
    /// <returns>True if the panel was found and updated, false otherwise.</returns>
    bool SetPanelVisibility(string panelId, bool visible);

    /// <summary>
    /// Toggles the visibility of a panel by ID.
    /// </summary>
    /// <param name="panelId">The panel ID.</param>
    /// <returns>The new visibility state, or null if panel not found.</returns>
    bool? TogglePanelVisibility(string panelId);
}
