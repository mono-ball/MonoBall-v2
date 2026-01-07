namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Numerics;

/// <summary>
/// Interface for debug panels that render ImGui content.
/// </summary>
public interface IDebugPanel
{
    /// <summary>
    /// Gets the unique identifier for this panel.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the display name shown in the panel title bar.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets or sets whether this panel is visible.
    /// </summary>
    bool IsVisible { get; set; }

    /// <summary>
    /// Gets the panel category for grouping in menus.
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Gets the sort order within the category.
    /// Lower values appear first.
    /// </summary>
    int SortOrder { get; }

    /// <summary>
    /// Gets the default window size for this panel.
    /// Returns null to use ImGui's default sizing.
    /// </summary>
    Vector2? DefaultSize => null;

    /// <summary>
    /// Draws the panel content using ImGui.
    /// Only called when IsVisible is true.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    void Draw(float deltaTime);
}

/// <summary>
/// Optional interface for panels that need lifecycle management.
/// </summary>
public interface IDebugPanelLifecycle : IDisposable
{
    /// <summary>
    /// Called when the panel is first registered.
    /// </summary>
    void Initialize();

    /// <summary>
    /// Called every frame before Draw, even when not visible.
    /// Use for updating cached data.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    void Update(float deltaTime);
}

/// <summary>
/// Optional interface for panels that need to draw menu items.
/// </summary>
public interface IDebugPanelMenu
{
    /// <summary>
    /// Draws menu items for this panel in the main debug menu bar.
    /// </summary>
    void DrawMenuItems();
}
