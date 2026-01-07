namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;
using MonoBall.Core.Scenes.Components;

/// <summary>
/// Debug panel for inspecting the scene stack and scene properties.
/// Shows active scenes, their priorities, and blocking states.
/// </summary>
public sealed class SceneInspectorPanel : IDebugPanel, IDebugPanelLifecycle
{
    // Cached QueryDescription for scene entities
    private static readonly QueryDescription SceneQuery =
        new QueryDescription().WithAll<SceneComponent>();

    private readonly World _world;
    private readonly List<SceneInfo> _scenes = new();
    private Entity? _selectedScene;
    private float _refreshInterval = 0.5f;
    private float _timeSinceRefresh;
    private string _filterText = string.Empty;

    /// <inheritdoc />
    public string Id => "scene-inspector";

    /// <inheritdoc />
    public string DisplayName => "Scene Inspector";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "Scenes";

    /// <inheritdoc />
    public int SortOrder => 0;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(500, 450);

    /// <summary>
    /// Initializes the scene inspector panel.
    /// </summary>
    /// <param name="world">The ECS world containing scene entities.</param>
    /// <exception cref="ArgumentNullException">Thrown when world is null.</exception>
    public SceneInspectorPanel(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <inheritdoc />
    public void Initialize()
    {
        RefreshSceneList();
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        _timeSinceRefresh += deltaTime;
        if (_timeSinceRefresh >= _refreshInterval)
        {
            _timeSinceRefresh = 0;
            RefreshSceneList();
        }
    }

    /// <inheritdoc />
    public void Draw(float deltaTime)
    {
        DrawToolbar();
        ImGui.Separator();

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var listWidth = availableWidth * 0.4f;

        // Scene list on the left (resizable)
        ImGui.BeginChild(
            "SceneList",
            new Vector2(listWidth, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX
        );
        DrawSceneStack();
        ImGui.EndChild();

        ImGui.SameLine();

        // Scene details on the right
        ImGui.BeginChild("SceneDetails", new Vector2(0, 0), ImGuiChildFlags.Borders);
        DrawSceneDetails();
        ImGui.EndChild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _scenes.Clear();
        _selectedScene = null;
        GC.SuppressFinalize(this);
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Refresh"))
        {
            RefreshSceneList();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##filter", "Filter scenes...", ref _filterText, 128);

        ImGui.SameLine();
        ImGui.Text($"Scenes: {_scenes.Count}");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##refresh", ref _refreshInterval, 0.1f, 2f, "%.1fs");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Refresh interval");
        }
    }

    private void DrawSceneStack()
    {
        var tableFlags =
            ImGuiTableFlags.Borders
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.Resizable
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.SizingFixedFit;

        if (!ImGui.BeginTable("SceneTable", 4, tableFlags))
            return;

        // Setup columns with initial widths
        ImGui.TableSetupColumn("Priority", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Scene ID", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("Blocks", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableHeadersRow();

        foreach (var scene in _scenes)
        {
            // Apply filter
            if (
                !string.IsNullOrEmpty(_filterText)
                && !scene.Component.SceneId.Contains(
                    _filterText,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;

            var isSelected = _selectedScene.HasValue && _selectedScene.Value.Id == scene.Entity.Id;
            var stateColor = GetStateColor(scene);

            ImGui.TableNextRow();

            // Use entity ID for unique ImGui ID (prevents conflicts when scenes share same priority)
            ImGui.PushID(scene.Entity.Id);

            // Priority
            ImGui.TableNextColumn();
            if (
                ImGui.Selectable(
                    scene.Component.Priority.ToString(),
                    isSelected,
                    ImGuiSelectableFlags.SpanAllColumns
                )
            )
            {
                _selectedScene = scene.Entity;
            }

            // Scene ID
            ImGui.TableNextColumn();
            ImGui.TextColored(stateColor, scene.Component.SceneId);

            // State
            ImGui.TableNextColumn();
            var stateText = GetStateText(scene);
            ImGui.TextColored(stateColor, stateText);

            // Blocks
            ImGui.TableNextColumn();
            var blocksText = GetBlocksText(scene);
            if (!string.IsNullOrEmpty(blocksText))
            {
                ImGui.TextColored(DebugColors.Blocking, blocksText);
            }
            else
            {
                ImGui.TextDisabled("-");
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawSceneDetails()
    {
        if (!_selectedScene.HasValue)
        {
            ImGui.TextDisabled("Select a scene to view details");
            return;
        }

        var entity = _selectedScene.Value;
        if (!_world.IsAlive(entity))
        {
            ImGui.TextColored(DebugColors.Error, "Scene no longer exists");
            _selectedScene = null;
            return;
        }

        if (!_world.Has<SceneComponent>(entity))
        {
            ImGui.TextColored(DebugColors.Error, "Entity is not a scene");
            _selectedScene = null;
            return;
        }

        var component = _world.Get<SceneComponent>(entity);

        ImGui.Text($"Entity ID: {entity.Id}");
        ImGui.Separator();

        // Scene properties
        DrawPropertyRow("Scene ID", component.SceneId);
        DrawPropertyRow("Priority", component.Priority.ToString());
        DrawPropertyRow("Camera Mode", component.CameraMode.ToString());
        ImGui.Separator();

        // State
        DrawPropertyRow(
            "Is Active",
            component.IsActive.ToString(),
            component.IsActive ? DebugColors.Active : DebugColors.Inactive
        );
        DrawPropertyRow(
            "Is Paused",
            component.IsPaused.ToString(),
            component.IsPaused ? DebugColors.Paused : DebugColors.Active
        );
        ImGui.Separator();

        // Blocking
        DrawPropertyRow(
            "Blocks Update",
            component.BlocksUpdate.ToString(),
            component.BlocksUpdate ? DebugColors.Blocking : DebugColors.Inactive
        );
        DrawPropertyRow(
            "Blocks Draw",
            component.BlocksDraw.ToString(),
            component.BlocksDraw ? DebugColors.Blocking : DebugColors.Inactive
        );
        DrawPropertyRow(
            "Blocks Input",
            component.BlocksInput.ToString(),
            component.BlocksInput ? DebugColors.Blocking : DebugColors.Inactive
        );
        ImGui.Separator();

        // Background color
        var bgColor = component.BackgroundColor;
        var bgText = bgColor.HasValue
            ? $"R:{bgColor.Value.R} G:{bgColor.Value.G} B:{bgColor.Value.B} A:{bgColor.Value.A}"
            : "(none)";
        DrawPropertyRow("Background", bgText);

        // Show attached marker components
        ImGui.Separator();
        ImGui.Text("Marker Components:");
        ImGui.Indent();

        var hasMarkers = false;
        if (_world.Has<GameSceneComponent>(entity))
        {
            ImGui.BulletText("GameSceneComponent");
            hasMarkers = true;
        }
        if (_world.Has<DebugMenuSceneComponent>(entity))
        {
            ImGui.BulletText("DebugMenuSceneComponent");
            hasMarkers = true;
        }
        if (_world.Has<MessageBoxSceneComponent>(entity))
        {
            ImGui.BulletText("MessageBoxSceneComponent");
            hasMarkers = true;
        }
        if (_world.Has<LoadingSceneComponent>(entity))
        {
            ImGui.BulletText("LoadingSceneComponent");
            hasMarkers = true;
        }
        if (_world.Has<MapPopupSceneComponent>(entity))
        {
            ImGui.BulletText("MapPopupSceneComponent");
            hasMarkers = true;
        }
        if (_world.Has<DebugBarSceneComponent>(entity))
        {
            ImGui.BulletText("DebugBarSceneComponent");
            hasMarkers = true;
        }

        if (!hasMarkers)
        {
            ImGui.TextDisabled("(none)");
        }

        ImGui.Unindent();
    }

    private static void DrawPropertyRow(string label, string value, Vector4? color = null)
    {
        ImGui.Text($"{label}:");
        ImGui.SameLine(150);
        if (color.HasValue)
        {
            ImGui.TextColored(color.Value, value);
        }
        else
        {
            ImGui.TextColored(DebugColors.TextValue, value);
        }
    }

    private void RefreshSceneList()
    {
        _scenes.Clear();

        _world.Query(
            in SceneQuery,
            (Entity entity, ref SceneComponent scene) =>
            {
                _scenes.Add(new SceneInfo { Entity = entity, Component = scene });
            }
        );

        // Sort by priority (highest first)
        _scenes.Sort((a, b) => b.Component.Priority.CompareTo(a.Component.Priority));
    }

    private static Vector4 GetStateColor(SceneInfo scene)
    {
        if (!scene.Component.IsActive)
            return DebugColors.Inactive;
        if (scene.Component.IsPaused)
            return DebugColors.Paused;
        return DebugColors.Active;
    }

    private static string GetStateText(SceneInfo scene)
    {
        if (!scene.Component.IsActive)
            return "Inactive";
        if (scene.Component.IsPaused)
            return "Paused";
        return "Active";
    }

    private static string GetBlocksText(SceneInfo scene)
    {
        var u = scene.Component.BlocksUpdate;
        var d = scene.Component.BlocksDraw;
        var i = scene.Component.BlocksInput;

        // Avoid allocation by using predefined strings for common cases
        return (u, d, i) switch
        {
            (true, true, true) => "U/D/I",
            (true, true, false) => "U/D",
            (true, false, true) => "U/I",
            (false, true, true) => "D/I",
            (true, false, false) => "U",
            (false, true, false) => "D",
            (false, false, true) => "I",
            _ => "",
        };
    }

    private struct SceneInfo
    {
        public Entity Entity;
        public SceneComponent Component;
    }
}
