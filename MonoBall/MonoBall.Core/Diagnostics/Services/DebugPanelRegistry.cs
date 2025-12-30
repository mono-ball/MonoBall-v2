namespace MonoBall.Core.Diagnostics.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using Events;
using MonoBall.Core.ECS;
using Panels;

/// <summary>
/// Default implementation of the debug panel registry.
/// </summary>
public sealed class DebugPanelRegistry : IDebugPanelRegistry, IDisposable
{
    private readonly Dictionary<string, IDebugPanel> _panelsById = new();
    private readonly List<IDebugPanel> _panels = new();
    private readonly List<string> _categories = new();
    private readonly Dictionary<string, List<IDebugPanel>> _panelsByCategory = new();
    private IDisposable? _toggleSubscription;
    private bool _disposed;

    /// <summary>
    /// Initializes a new debug panel registry.
    /// </summary>
    public DebugPanelRegistry()
    {
        _toggleSubscription = EventBus.Subscribe<DebugPanelToggleEvent>(OnPanelToggle);
    }

    /// <inheritdoc />
    public IReadOnlyList<IDebugPanel> Panels => _panels;

    /// <inheritdoc />
    public IReadOnlyList<string> Categories => _categories;

    /// <inheritdoc />
    public void Register(IDebugPanel panel)
    {
        if (panel == null)
            throw new ArgumentNullException(nameof(panel));

        if (_panelsById.ContainsKey(panel.Id))
            throw new ArgumentException(
                $"Panel with ID '{panel.Id}' is already registered.",
                nameof(panel)
            );

        _panelsById[panel.Id] = panel;
        _panels.Add(panel);

        if (!_panelsByCategory.TryGetValue(panel.Category, out var categoryPanels))
        {
            categoryPanels = new List<IDebugPanel>();
            _panelsByCategory[panel.Category] = categoryPanels;
            _categories.Add(panel.Category);
            _categories.Sort();
        }

        categoryPanels.Add(panel);
        categoryPanels.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

        if (panel is IDebugPanelLifecycle lifecycle)
        {
            lifecycle.Initialize();
        }
    }

    /// <inheritdoc />
    public bool Unregister(string panelId)
    {
        if (string.IsNullOrEmpty(panelId))
            return false;

        if (!_panelsById.TryGetValue(panelId, out var panel))
            return false;

        _panelsById.Remove(panelId);
        _panels.Remove(panel);

        if (_panelsByCategory.TryGetValue(panel.Category, out var categoryPanels))
        {
            categoryPanels.Remove(panel);
            if (categoryPanels.Count == 0)
            {
                _panelsByCategory.Remove(panel.Category);
                _categories.Remove(panel.Category);
            }
        }

        if (panel is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return true;
    }

    /// <inheritdoc />
    public IDebugPanel? GetPanel(string panelId)
    {
        if (string.IsNullOrEmpty(panelId))
            return null;

        return _panelsById.TryGetValue(panelId, out var panel) ? panel : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<IDebugPanel> GetPanelsByCategory(string category)
    {
        if (string.IsNullOrEmpty(category))
            return Array.Empty<IDebugPanel>();

        return _panelsByCategory.TryGetValue(category, out var panels)
            ? panels
            : Array.Empty<IDebugPanel>();
    }

    /// <inheritdoc />
    public bool SetPanelVisibility(string panelId, bool visible)
    {
        var panel = GetPanel(panelId);
        if (panel == null)
            return false;

        panel.IsVisible = visible;
        return true;
    }

    /// <inheritdoc />
    public bool? TogglePanelVisibility(string panelId)
    {
        var panel = GetPanel(panelId);
        if (panel == null)
            return null;

        panel.IsVisible = !panel.IsVisible;
        return panel.IsVisible;
    }

    /// <summary>
    /// Updates all panels that implement IDebugPanelLifecycle.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    public void Update(float deltaTime)
    {
        foreach (var panel in _panels)
        {
            if (panel is IDebugPanelLifecycle lifecycle)
            {
                lifecycle.Update(deltaTime);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _toggleSubscription?.Dispose();
        _toggleSubscription = null;

        foreach (var panel in _panels.ToList())
        {
            if (panel is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _panels.Clear();
        _panelsById.Clear();
        _panelsByCategory.Clear();
        _categories.Clear();

        _disposed = true;
    }

    private void OnPanelToggle(DebugPanelToggleEvent evt)
    {
        if (evt.Show.HasValue)
        {
            SetPanelVisibility(evt.PanelId, evt.Show.Value);
        }
        else
        {
            TogglePanelVisibility(evt.PanelId);
        }
    }
}
