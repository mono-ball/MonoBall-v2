# Diagnostic Panel UX Implementation Specification
## Technical Guide for MonoBall Development Team

**Date:** 2025-12-30
**Status:** READY FOR IMPLEMENTATION
**Target MonoBall Version:** Post v1.0

---

## Quick Reference: 16 UX Patterns Overview

| # | Pattern | Priority | Complexity | Est. Time |
|---|---------|----------|-----------|-----------|
| 1 | Multi-layer access system | HIGH | MEDIUM | 3 days |
| 2 | Icon-based toolbar with labels | HIGH | LOW | 2 days |
| 3 | Visual status indicators & badges | HIGH | MEDIUM | 3 days |
| 4 | Command palette & fuzzy search | HIGH | MEDIUM | 4 days |
| 5 | Workspace/layout presets | MEDIUM | MEDIUM | 5 days |
| 6 | Floating/pinned quick-access | MEDIUM | LOW | 2 days |
| 7 | Keyboard shortcuts | HIGH | LOW | 1 day |
| 8 | Mini-widgets & at-a-glance info | MEDIUM | MEDIUM | 3 days |
| 9 | Contextual quick actions | MEDIUM | LOW | 2 days |
| 10 | Dockbar with icon-only mode | LOW | MEDIUM | 4 days |
| 11 | Search & discovery improvements | MEDIUM | LOW | 2 days |
| 12 | Panel state persistence | MEDIUM | LOW | 2 days |
| 13 | Progressive disclosure | LOW | LOW | 2 days |
| 14 | Responsive panel sizing | LOW | LOW | 1 day |
| 15 | Dark/light theme toggle | LOW | LOW | 1 day |
| 16 | Accessibility features | LOW | MEDIUM | 3 days |

---

## Phase 1 Implementation: Quick Wins (Recommended Start)

These 5 features provide 80% of UX improvement with 20% of effort.

### 1.1 Pattern #2: Icon-Based Toolbar

**File:** `/MonoBall.Core/Diagnostics/UI/DebugToolbar.cs`

```csharp
namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.Panels;

/// <summary>
/// Renders a toolbar with icons for quick panel access.
/// </summary>
public class DebugToolbar
{
    private readonly IDebugPanelRegistry _registry;
    private readonly List<ToolbarButton> _buttons;
    private bool _showLabels = true;
    private const float IconSize = 32f;
    private const float Spacing = 4f;

    public DebugToolbar(IDebugPanelRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _buttons = new List<ToolbarButton>();
        InitializeButtons();
    }

    private void InitializeButtons()
    {
        // Add buttons for each panel (in order of usage frequency)
        AddButton("Performance", "◆", DebugColors.Accent);
        AddButton("Console", "▌", DebugColors.Info);
        AddButton("Entity Inspector", "◇", DebugColors.Highlight);
        AddButton("Scene Inspector", "▼", DebugColors.Success);
        AddButton("Logs", "📋", DebugColors.Blocking);
        AddButton("System Profiler", "⏱", DebugColors.Highlight);
        AddButton("Event Inspector", "⚡", DebugColors.Accent);
        AddButton("Mod Browser", "📦", DebugColors.Info);
        AddButton("Definition Browser", "∫", DebugColors.Success);
    }

    private void AddButton(string panelName, string icon, Vector4 color)
    {
        _buttons.Add(new ToolbarButton
        {
            PanelName = panelName,
            Icon = icon,
            Color = color,
            IsActive = false
        });
    }

    /// <summary>
    /// Draws the toolbar. Call from DebugPanelRenderSystem.
    /// </summary>
    public void Draw()
    {
        ImGui.SetNextWindowPos(new Vector2(8, 32)); // Below menu bar
        ImGui.SetNextWindowSize(new Vector2(
            _showLabels ? 400 : 50,
            IconSize + 16
        ));

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.AlwaysAutoResize;

        if (ImGui.Begin("##DebugToolbar", flags))
        {
            float cursorX = 4;

            foreach (var button in _buttons)
            {
                DrawToolbarButton(button, ref cursorX);
            }

            // Toggle label visibility
            ImGui.SameLine();
            ImGui.Spacing();
            ImGui.SameLine();
            ImGui.SetCursorPosX(cursorX + Spacing);

            ImGui.PushStyleColor(ImGuiCol.Button,
                _showLabels ? DebugColors.Accent : DebugColors.BackgroundSecondary);

            if (ImGui.Button("</> ", new Vector2(32, 32)))
                _showLabels = !_showLabels;

            ImGui.PopStyleColor();

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(_showLabels ? "Hide labels" : "Show labels");
        }
        ImGui.End();
    }

    private void DrawToolbarButton(ToolbarButton button, ref float cursorX)
    {
        var panel = _registry.GetPanel(button.PanelName);
        if (panel == null)
            return;

        ImGui.SetCursorPosX(cursorX);

        // Determine button appearance based on panel visibility
        var buttonColor = panel.IsVisible
            ? button.Color
            : DebugColors.BackgroundSecondary;

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor with { W = 0.8f });
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, buttonColor);

        string buttonLabel = _showLabels
            ? $"{button.Icon} {button.PanelName}##toolbar_{button.PanelName}"
            : $"{button.Icon}##toolbar_{button.PanelName}";

        if (ImGui.Button(buttonLabel, new Vector2(
            _showLabels ? 100 : IconSize,
            IconSize)))
        {
            panel.IsVisible = !panel.IsVisible;
        }

        ImGui.PopStyleColor(3);

        // Tooltip on hover
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{button.PanelName} (Ctrl+{GetPanelNumber(button.PanelName)})");

        // Badge (error/warning count) - see Pattern #3
        if (panel is IStatusProvider statusProvider)
        {
            var badge = statusProvider.GetBadge();
            if (badge != null)
                DrawBadge(badge);
        }

        cursorX += _showLabels ? 105 : IconSize + Spacing * 2;
        ImGui.SameLine();
    }

    private void DrawBadge(PanelStatusBadge badge)
    {
        // Position badge at top-right of button
        var buttonSize = ImGui.GetItemRectSize();
        var pos = ImGui.GetItemRectMin();

        ImGui.SetCursorScreenPos(
            pos + new Vector2(buttonSize.X - 10, pos.Y - 5)
        );

        ImGui.PushStyleColor(ImGuiCol.Text, badge.Color);
        ImGui.Text("●"); // Dot indicator
        ImGui.PopStyleColor();
    }

    private int GetPanelNumber(string panelName)
    {
        return panelName switch
        {
            "Performance" => 1,
            "Console" => 2,
            "Entity Inspector" => 3,
            "Scene Inspector" => 4,
            "Logs" => 5,
            "System Profiler" => 6,
            "Event Inspector" => 7,
            "Mod Browser" => 8,
            "Definition Browser" => 9,
            _ => 0
        };
    }

    private class ToolbarButton
    {
        public string PanelName { get; set; }
        public string Icon { get; set; }
        public Vector4 Color { get; set; }
        public bool IsActive { get; set; }
    }
}

/// <summary>
/// Optional interface for panels that provide status information.
/// </summary>
public interface IStatusProvider
{
    PanelStatusBadge? GetBadge();
}

/// <summary>
/// Status information to display as a badge on toolbar icons.
/// </summary>
public class PanelStatusBadge
{
    public string Text { get; set; }
    public Vector4 Color { get; set; }
    public int Priority { get; set; }
}
```

**Integration in DebugPanelRenderSystem:**

```csharp
// In DebugPanelRenderSystem.cs

private DebugToolbar? _toolbar;

public DebugPanelRenderSystem(
    World world,
    IDebugPanelRegistry registry,
    ImGuiLifecycleSystem lifecycleSystem
)
    : base(world)
{
    _registry = registry;
    _lifecycleSystem = lifecycleSystem;
    _toolbar = new DebugToolbar(registry);
}

public override void Update(in float deltaTime)
{
    // ... existing code ...

    if (_showMainMenuBar)
    {
        DrawMainMenuBar();
    }

    // Draw toolbar
    _toolbar?.Draw();

    DrawDockSpace();
    DrawPanels(deltaTime);
}
```

---

### 1.2 Pattern #7: Keyboard Shortcuts

**File:** `/MonoBall.Core/Diagnostics/UI/DebugKeyboardHandler.cs`

```csharp
namespace MonoBall.Core.Diagnostics.UI;

using System;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.Panels;

/// <summary>
/// Handles keyboard shortcuts for debug panel access.
/// </summary>
public class DebugKeyboardHandler
{
    private readonly IDebugPanelRegistry _registry;
    private Action<string>? _onCommandPaletteRequested;

    public DebugKeyboardHandler(IDebugPanelRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Register callback for command palette requests.
    /// </summary>
    public void OnCommandPaletteRequested(Action<string> callback)
    {
        _onCommandPaletteRequested = callback;
    }

    /// <summary>
    /// Call this from ImGuiLifecycleSystem or update loop.
    /// </summary>
    public void HandleInput()
    {
        // Only process when ImGui wants keyboard input
        var io = ImGui.GetIO();
        if (!io.WantCaptureKeyboard)
            return;

        // Panel shortcuts: Ctrl+1 through Ctrl+9
        for (int i = 1; i <= 9; i++)
        {
            var key = (ImGuiKey)(ImGuiKey.Keypad1 + (i - 1));

            if (ImGui.IsKeyPressed(key, false) &&
                ImGui.IsKeyDown(ImGuiKey.ModCtrl))
            {
                TogglePanelByNumber(i);
                return; // Consume input
            }
        }

        // Command palette: Ctrl+Shift+P
        if (ImGui.IsKeyPressed(ImGuiKey.P, false) &&
            ImGui.IsKeyDown(ImGuiKey.ModCtrl) &&
            ImGui.IsKeyDown(ImGuiKey.ModShift))
        {
            _onCommandPaletteRequested?.Invoke("");
            return;
        }

        // Master toggle: Ctrl+D
        if (ImGui.IsKeyPressed(ImGuiKey.D, false) &&
            ImGui.IsKeyDown(ImGuiKey.ModCtrl))
        {
            ToggleAllPanels();
            return;
        }
    }

    private void TogglePanelByNumber(int number)
    {
        var panelName = number switch
        {
            1 => "Performance",
            2 => "Console",
            3 => "Entity Inspector",
            4 => "Scene Inspector",
            5 => "Logs",
            6 => "System Profiler",
            7 => "Event Inspector",
            8 => "Mod Browser",
            9 => "Definition Browser",
            _ => null
        };

        if (panelName != null)
        {
            var panel = _registry.GetPanel(panelName);
            if (panel != null)
                panel.IsVisible = !panel.IsVisible;
        }
    }

    private void ToggleAllPanels()
    {
        // Toggle all panels together
        bool allVisible = _registry.Panels.All(p => p.IsVisible);

        foreach (var panel in _registry.Panels)
        {
            panel.IsVisible = !allVisible;
        }
    }
}
```

**Integration in ImGuiLifecycleSystem:**

```csharp
private DebugKeyboardHandler? _keyboardHandler;

public void Initialize(IDebugPanelRegistry registry)
{
    _keyboardHandler = new DebugKeyboardHandler(registry);
}

public void Update(in float deltaTime)
{
    _keyboardHandler?.HandleInput();
    // ... rest of update ...
}
```

---

### 1.3 Pattern #4: Command Palette

**File:** `/MonoBall.Core/Diagnostics/UI/CommandPalette.cs`

```csharp
namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.Panels;

/// <summary>
/// Command palette for fuzzy-search panel opening and other debug actions.
/// Inspired by VS Code's command palette.
/// </summary>
public class CommandPalette
{
    private readonly IDebugPanelRegistry _registry;
    private string _searchText = "";
    private List<CommandItem> _allCommands;
    private int _selectedIndex = 0;
    private bool _isOpen = false;

    public bool IsOpen => _isOpen;

    public CommandPalette(IDebugPanelRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _allCommands = new List<CommandItem>();
        InitializeCommands();
    }

    private void InitializeCommands()
    {
        // Panel open commands
        foreach (var panel in _registry.Panels)
        {
            _allCommands.Add(new CommandItem
            {
                Id = $"panel.open.{panel.Id}",
                DisplayName = $"Open: {panel.DisplayName}",
                Category = "Panels",
                Shortcut = GetShortcutForPanel(panel.DisplayName),
                Execute = () => panel.IsVisible = true,
                Priority = 100
            });

            _allCommands.Add(new CommandItem
            {
                Id = $"panel.close.{panel.Id}",
                DisplayName = $"Close: {panel.DisplayName}",
                Category = "Panels",
                Execute = () => panel.IsVisible = false,
                Priority = 50
            });
        }

        // General actions
        _allCommands.Add(new CommandItem
        {
            Id = "debug.toggle",
            DisplayName = "Toggle All Panels",
            Category = "Debug",
            Shortcut = "Ctrl+D",
            Execute = ToggleAllPanels,
            Priority = 100
        });

        _allCommands.Add(new CommandItem
        {
            Id = "debug.close-all",
            DisplayName = "Close All Panels",
            Category = "Debug",
            Execute = CloseAllPanels,
            Priority = 80
        });

        // Sort by priority
        _allCommands = _allCommands.OrderByDescending(c => c.Priority).ToList();
    }

    public void Toggle()
    {
        _isOpen = !_isOpen;
        _searchText = "";
        _selectedIndex = 0;
        ImGui.OpenPopup("##CommandPalette");
    }

    public void Open()
    {
        _isOpen = true;
        _searchText = "";
        _selectedIndex = 0;
        ImGui.OpenPopup("##CommandPalette");
    }

    public void Draw()
    {
        if (!_isOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(500, 300));
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(),
            ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("Command Palette##CommandPalette",
            ref _isOpen, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Search input
            ImGui.SetKeyboardFocusHere(0);

            if (ImGui.InputTextWithHint("##search", "Type to search...",
                ref _searchText, 256, ImGuiInputTextFlags.AutoSelectAll))
            {
                _selectedIndex = 0; // Reset selection on search change
            }

            ImGui.Separator();

            // Get filtered commands
            var filtered = GetFilteredCommands(_searchText);

            // Display filtered commands
            if (ImGui.BeginListBox("##commands", new Vector2(-1, 200)))
            {
                for (int i = 0; i < filtered.Count; i++)
                {
                    var cmd = filtered[i];
                    bool isSelected = i == _selectedIndex;

                    if (ImGui.Selectable(GetCommandLabel(cmd), isSelected))
                    {
                        cmd.Execute?.Invoke();
                        ImGui.CloseCurrentPopup();
                        _isOpen = false;
                    }

                    if (isSelected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndListBox();
            }

            // Keyboard navigation
            HandleKeyboardInput(filtered);

            ImGui.EndPopup();
        }
    }

    private List<CommandItem> GetFilteredCommands(string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return _allCommands;

        var searchLower = search.ToLower();
        return _allCommands
            .Where(c => FuzzyMatch(c.DisplayName, searchLower))
            .ToList();
    }

    private bool FuzzyMatch(string text, string pattern)
    {
        int patternIdx = 0;
        int textIdx = 0;

        while (patternIdx < pattern.Length && textIdx < text.Length)
        {
            if (char.ToLower(text[textIdx]) == pattern[patternIdx])
                patternIdx++;

            textIdx++;
        }

        return patternIdx == pattern.Length;
    }

    private string GetCommandLabel(CommandItem cmd)
    {
        var label = cmd.DisplayName;

        if (!string.IsNullOrEmpty(cmd.Shortcut))
            label = $"{label,-40} [{cmd.Shortcut}]";

        return label;
    }

    private void HandleKeyboardInput(List<CommandItem> filtered)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
            _selectedIndex = Math.Min(filtered.Count - 1, _selectedIndex + 1);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            if (_selectedIndex >= 0 && _selectedIndex < filtered.Count)
            {
                filtered[_selectedIndex].Execute?.Invoke();
                ImGui.CloseCurrentPopup();
                _isOpen = false;
            }
        }
    }

    private string? GetShortcutForPanel(string panelName)
    {
        return panelName switch
        {
            "Performance" => "Ctrl+1",
            "Console" => "Ctrl+2",
            "Entity Inspector" => "Ctrl+3",
            "Scene Inspector" => "Ctrl+4",
            "Logs" => "Ctrl+5",
            "System Profiler" => "Ctrl+6",
            "Event Inspector" => "Ctrl+7",
            "Mod Browser" => "Ctrl+8",
            "Definition Browser" => "Ctrl+9",
            _ => null
        };
    }

    private void ToggleAllPanels()
    {
        bool allVisible = _registry.Panels.All(p => p.IsVisible);
        foreach (var panel in _registry.Panels)
            panel.IsVisible = !allVisible;
    }

    private void CloseAllPanels()
    {
        foreach (var panel in _registry.Panels)
            panel.IsVisible = false;
    }

    private class CommandItem
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Category { get; set; }
        public string? Shortcut { get; set; }
        public Action? Execute { get; set; }
        public int Priority { get; set; }
    }
}
```

---

### 1.4 Pattern #3: Status Badges

**File:** `/MonoBall.Core/Diagnostics/Panels/IStatusProvider.cs`

```csharp
namespace MonoBall.Core.Diagnostics.Panels;

using System.Numerics;

/// <summary>
/// Optional interface for panels that want to display status information.
/// </summary>
public interface IStatusProvider
{
    /// <summary>
    /// Get status information to display as a badge.
    /// </summary>
    /// <returns>Badge information, or null if no status to display.</returns>
    PanelStatusBadge? GetBadge();
}

/// <summary>
/// Status information for panel badges.
/// </summary>
public class PanelStatusBadge
{
    /// <summary>
    /// Text to display in the badge (e.g., "60 FPS" or "3 errors").
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Color of the badge indicator.
    /// Use DebugColors for consistency.
    /// </summary>
    public Vector4 Color { get; set; }

    /// <summary>
    /// Priority for display (higher = more important).
    /// </summary>
    public int Priority { get; set; } = 0;
}
```

**Example: PerformancePanel with status badge**

```csharp
public class PerformancePanel : IDebugPanel, IStatusProvider
{
    private float _currentFps;

    public string Id => "performance";
    public string DisplayName => "Performance";
    public bool IsVisible { get; set; }
    public string Category => "Monitoring";
    public int SortOrder => 0;

    public PanelStatusBadge? GetBadge()
    {
        return new PanelStatusBadge
        {
            Text = $"{(int)_currentFps} FPS",
            Color = _currentFps > 60 ? DebugColors.Success
                   : _currentFps > 30 ? DebugColors.Highlight
                   : DebugColors.Accent,
            Priority = 100 // High priority - show first
        };
    }

    public void Draw(float deltaTime)
    {
        // Calculate FPS
        _currentFps = 1f / deltaTime;

        ImGui.Text($"FPS: {_currentFps:F1}");
        ImGui.Text($"Frame Time: {deltaTime * 1000:F2}ms");
    }
}
```

---

### 1.5 Pattern #12: Panel State Persistence

**File:** `/MonoBall.Core/Diagnostics/UI/PanelStateManager.cs`

```csharp
namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using MonoBall.Core.Diagnostics.Panels;

/// <summary>
/// Manages saving and loading of panel visibility, position, and size.
/// </summary>
public class PanelStateManager
{
    private readonly string _stateFilePath;
    private readonly IDebugPanelRegistry _registry;

    public PanelStateManager(IDebugPanelRegistry registry, string stateFilePath = "diagnostic_state.json")
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _stateFilePath = Path.Combine(AppContext.BaseDirectory, stateFilePath);
    }

    /// <summary>
    /// Load saved panel state from disk.
    /// </summary>
    public void LoadState()
    {
        if (!File.Exists(_stateFilePath))
            return;

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            var states = JsonSerializer.Deserialize<List<PanelState>>(json);

            if (states == null)
                return;

            foreach (var state in states)
            {
                var panel = _registry.GetPanel(state.PanelId);
                if (panel != null)
                {
                    panel.IsVisible = state.IsVisible;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load panel state: {ex}");
        }
    }

    /// <summary>
    /// Save current panel state to disk.
    /// </summary>
    public void SaveState()
    {
        try
        {
            var states = new List<PanelState>();

            foreach (var panel in _registry.Panels)
            {
                states.Add(new PanelState
                {
                    PanelId = panel.Id,
                    IsVisible = panel.IsVisible
                });
            }

            var json = JsonSerializer.Serialize(states,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save panel state: {ex}");
        }
    }

    private class PanelState
    {
        public string PanelId { get; set; }
        public bool IsVisible { get; set; }
    }
}
```

**Usage in DebugOverlayService:**

```csharp
public sealed class DebugOverlayService : IDebugOverlayService
{
    private PanelStateManager? _stateManager;

    public void Initialize(Game game, IResourceManager? resourceManager = null,
        SceneSystem? sceneSystem = null, IModManager? modManager = null)
    {
        // ... existing initialization ...

        _stateManager = new PanelStateManager(_panelRegistry!);
        _stateManager.LoadState();
    }

    public void Dispose()
    {
        // Save state before disposing
        _stateManager?.SaveState();

        // ... rest of dispose ...
    }
}
```

---

## Phase 2 Implementation: Enhanced Features

### 2.1 Pattern #5: Workspace/Layout Presets

**File:** `/MonoBall.Core/Diagnostics/UI/WorkspaceManager.cs`

```csharp
namespace MonoBall.Core.Diagnostics.UI;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using MonoBall.Core.Diagnostics.Panels;

/// <summary>
/// Manages multiple workspace layouts with save/load functionality.
/// </summary>
public class WorkspaceManager
{
    private readonly IDebugPanelRegistry _registry;
    private readonly string _layoutsDirectory;
    private Dictionary<string, WorkspaceLayout> _layouts;

    public IReadOnlyDictionary<string, WorkspaceLayout> Layouts => _layouts;

    public WorkspaceManager(IDebugPanelRegistry registry,
        string layoutsDirectory = "diagnostic_layouts")
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _layoutsDirectory = Path.Combine(AppContext.BaseDirectory, layoutsDirectory);
        _layouts = new Dictionary<string, WorkspaceLayout>();

        Directory.CreateDirectory(_layoutsDirectory);
        CreateDefaultLayouts();
        LoadAllLayouts();
    }

    private void CreateDefaultLayouts()
    {
        // Development layout (most panels)
        SaveLayout(new WorkspaceLayout
        {
            Name = "Development",
            IsDefault = true,
            PanelVisibility = new Dictionary<string, bool>
            {
                ["Performance"] = true,
                ["Console"] = true,
                ["Entity Inspector"] = true,
                ["Logs"] = true,
                ["Scene Inspector"] = false,
                ["Event Inspector"] = false,
                ["System Profiler"] = false,
                ["Mod Browser"] = false,
                ["Definition Browser"] = false,
            }
        });

        // Performance Analysis layout
        SaveLayout(new WorkspaceLayout
        {
            Name = "Performance Analysis",
            PanelVisibility = new Dictionary<string, bool>
            {
                ["Performance"] = true,
                ["System Profiler"] = true,
                ["Logs"] = true,
                ["Console"] = false,
                ["Entity Inspector"] = false,
                ["Scene Inspector"] = false,
                ["Event Inspector"] = false,
                ["Mod Browser"] = false,
                ["Definition Browser"] = false,
            }
        });

        // Scene Editing layout
        SaveLayout(new WorkspaceLayout
        {
            Name = "Scene Editing",
            PanelVisibility = new Dictionary<string, bool>
            {
                ["Scene Inspector"] = true,
                ["Entity Inspector"] = true,
                ["Performance"] = true,
                ["Console"] = false,
                ["Logs"] = false,
                ["Event Inspector"] = false,
                ["System Profiler"] = false,
                ["Mod Browser"] = false,
                ["Definition Browser"] = false,
            }
        });

        // Debugging layout
        SaveLayout(new WorkspaceLayout
        {
            Name = "Debugging",
            PanelVisibility = new Dictionary<string, bool>
            {
                ["Console"] = true,
                ["Logs"] = true,
                ["Event Inspector"] = true,
                ["Entity Inspector"] = true,
                ["Performance"] = false,
                ["Scene Inspector"] = false,
                ["System Profiler"] = false,
                ["Mod Browser"] = false,
                ["Definition Browser"] = false,
            }
        });

        // Minimal layout (compact)
        SaveLayout(new WorkspaceLayout
        {
            Name = "Minimal",
            PanelVisibility = new Dictionary<string, bool>
            {
                ["Console"] = true,
                ["Performance"] = false,
                ["Entity Inspector"] = false,
                ["Scene Inspector"] = false,
                ["Logs"] = false,
                ["Event Inspector"] = false,
                ["System Profiler"] = false,
                ["Mod Browser"] = false,
                ["Definition Browser"] = false,
            }
        });
    }

    public void SaveLayout(WorkspaceLayout layout)
    {
        _layouts[layout.Name] = layout;
        PersistLayout(layout);
    }

    public void LoadLayout(string layoutName)
    {
        if (!_layouts.TryGetValue(layoutName, out var layout))
            return;

        // Apply panel visibility from layout
        foreach (var kvp in layout.PanelVisibility)
        {
            var panel = _registry.GetPanel(kvp.Key);
            if (panel != null)
                panel.IsVisible = kvp.Value;
        }
    }

    public void DeleteLayout(string layoutName)
    {
        if (layoutName == "Development" || layoutName == "Minimal")
            return; // Prevent deleting built-in layouts

        _layouts.Remove(layoutName);

        var filePath = Path.Combine(_layoutsDirectory, $"{layoutName}.json");
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private void LoadAllLayouts()
    {
        if (!Directory.Exists(_layoutsDirectory))
            return;

        foreach (var file in Directory.GetFiles(_layoutsDirectory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var layout = System.Text.Json.JsonSerializer
                    .Deserialize<WorkspaceLayout>(json);

                if (layout != null)
                    _layouts[layout.Name] = layout;
            }
            catch { }
        }
    }

    private void PersistLayout(WorkspaceLayout layout)
    {
        var filePath = Path.Combine(_layoutsDirectory, $"{layout.Name}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(layout,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public class WorkspaceLayout
    {
        public string Name { get; set; }
        public bool IsDefault { get; set; }
        public Dictionary<string, bool> PanelVisibility { get; set; } = new();
    }
}
```

---

## Integration Checklist for Phase 1

### Files to Create:
- [ ] `/MonoBall.Core/Diagnostics/UI/DebugToolbar.cs`
- [ ] `/MonoBall.Core/Diagnostics/UI/DebugKeyboardHandler.cs`
- [ ] `/MonoBall.Core/Diagnostics/UI/CommandPalette.cs`
- [ ] `/MonoBall.Core/Diagnostics/UI/PanelStateManager.cs`
- [ ] `/MonoBall.Core/Diagnostics/Panels/IStatusProvider.cs`

### Files to Modify:
- [ ] `/MonoBall.Core/Diagnostics/Systems/DebugPanelRenderSystem.cs`
  - Add toolbar rendering
  - Add command palette drawing
  - Add keyboard handler updates

- [ ] `/MonoBall.Core/Diagnostics/Services/DebugOverlayService.cs`
  - Initialize panel state manager
  - Load/save state in lifecycle

- [ ] `/MonoBall.Core/Diagnostics/ImGui/ImGuiLifecycleSystem.cs`
  - Initialize keyboard handler
  - Call handle input in update

- [ ] Each panel implementing `IStatusProvider`
  - Add `GetBadge()` method

### Dependencies:
- System.Text.Json (already in .NET 10)
- Hexa.NET.ImGui (already referenced)

---

## Testing Strategy

### Unit Tests
```csharp
[TestClass]
public class CommandPaletteTests
{
    [TestMethod]
    public void FuzzyMatch_ShouldMatchPartialStrings()
    {
        var palette = new CommandPalette(_mockRegistry);
        Assert.IsTrue(palette.FuzzyMatch("Performance", "perf"));
        Assert.IsTrue(palette.FuzzyMatch("Entity Inspector", "entinsp"));
        Assert.IsFalse(palette.FuzzyMatch("Console", "xyz"));
    }
}

[TestClass]
public class PanelStateManagerTests
{
    [TestMethod]
    public void SaveLoad_PreservesPanelVisibility()
    {
        var manager = new PanelStateManager(_mockRegistry);

        // Set panel visibility
        _mockRegistry.GetPanel("Performance").IsVisible = true;
        manager.SaveState();

        // Clear and reload
        _mockRegistry.GetPanel("Performance").IsVisible = false;
        manager.LoadState();

        Assert.IsTrue(_mockRegistry.GetPanel("Performance").IsVisible);
    }
}
```

### Manual Testing
1. Toolbar icons display correctly
2. Clicking toolbar toggles panels
3. Ctrl+1-9 opens/closes panels
4. Ctrl+Shift+P opens command palette
5. Command palette fuzzy search works
6. Badges display on toolbar
7. Panel visibility persists after close/reopen
8. Keyboard shortcuts show in tooltips

---

## Performance Considerations

- Command palette filters using O(n) string matching - acceptable for <100 items
- Badge rendering is minimal (single char per icon)
- Keyboard handling uses ImGui's built-in input system
- State file is small JSON (< 1KB)

---

## Next Steps

1. Review this specification with the team
2. Assign team members to each file
3. Start with Pattern #2 (toolbar) as it unblocks others
4. Follow Phase 1 order for dependencies
5. Test each component before integration
6. Get feedback and iterate

---

**Total Estimated Time:** 2-3 weeks for Phase 1
**Complexity:** MEDIUM (straightforward ImGui usage)
**Risk:** LOW (additive features, no breaking changes)
**ROI:** VERY HIGH (4x-6x faster panel access)

