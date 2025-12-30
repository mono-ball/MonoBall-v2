namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.Core.Extensions;
using Hexa.NET.ImGui;

/// <summary>
/// Debug panel for inspecting ECS entities and their components.
/// </summary>
public sealed class EntityInspectorPanel : IDebugPanel, IDebugPanelLifecycle
{
    // Cached QueryDescription (per .cursorrules - never create in hot paths)
    private static readonly QueryDescription AllEntitiesQuery = new();

    private readonly World _world;
    private readonly List<Entity> _cachedEntities = new();
    private readonly List<Entity> _filteredEntities = new();
    private readonly HashSet<Type> _knownComponentTypes = new();
    private readonly HashSet<Type> _selectedComponentFilters = new();
    private Entity? _selectedEntity;
    private string _searchFilter = string.Empty;
    private string _componentSearchFilter = string.Empty;
    private float _refreshInterval = 1f;
    private float _timeSinceRefresh;
    private bool _showComponentFilter;
    private ComponentFilterMode _filterMode = ComponentFilterMode.Any;

    private enum ComponentFilterMode
    {
        Any, // Entity has ANY of the selected components
        All, // Entity has ALL of the selected components
    }

    /// <inheritdoc />
    public string Id => "entity-inspector";

    /// <inheritdoc />
    public string DisplayName => "Entity Inspector";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "ECS";

    /// <inheritdoc />
    public int SortOrder => 0;

    /// <summary>
    /// Initializes the entity inspector panel.
    /// </summary>
    /// <param name="world">The ECS world to inspect.</param>
    /// <exception cref="ArgumentNullException">Thrown when world is null.</exception>
    public EntityInspectorPanel(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <inheritdoc />
    public void Initialize()
    {
        RefreshEntityList();
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        _timeSinceRefresh += deltaTime;
        if (_timeSinceRefresh >= _refreshInterval)
        {
            _timeSinceRefresh = 0;
            RefreshEntityList();
        }
    }

    /// <inheritdoc />
    public void Draw(float deltaTime)
    {
        DrawToolbar();
        ImGui.Separator();

        var availableWidth = ImGui.GetContentRegionAvail().X;
        var listWidth = availableWidth * 0.35f;

        // Entity list on the left (resizable)
        ImGui.BeginChild(
            "EntityList",
            new System.Numerics.Vector2(listWidth, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX
        );
        DrawEntityList();
        ImGui.EndChild();

        ImGui.SameLine();

        // Component inspector on the right
        ImGui.BeginChild(
            "ComponentInspector",
            new System.Numerics.Vector2(0, 0),
            ImGuiChildFlags.Borders
        );
        DrawComponentInspector();
        ImGui.EndChild();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cachedEntities.Clear();
        _filteredEntities.Clear();
        _knownComponentTypes.Clear();
        _selectedComponentFilters.Clear();
        _selectedEntity = null;
        GC.SuppressFinalize(this);
    }

    private void DrawToolbar()
    {
        // First row: main controls
        if (ImGui.Button("Refresh"))
        {
            RefreshEntityList();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        ImGui.InputTextWithHint("##search", "Search...", ref _searchFilter, 256);

        ImGui.SameLine();
        var filterButtonLabel =
            _selectedComponentFilters.Count > 0
                ? $"Components ({_selectedComponentFilters.Count})"
                : "Components";
        if (ImGui.Button(filterButtonLabel))
        {
            _showComponentFilter = !_showComponentFilter;
        }

        ImGui.SameLine();
        var countText =
            _selectedComponentFilters.Count > 0
                ? $"{_filteredEntities.Count}/{_cachedEntities.Count}"
                : $"{_cachedEntities.Count}";
        ImGui.Text($"Entities: {countText}");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        ImGui.SliderFloat("##refresh", ref _refreshInterval, 0.1f, 5f, "%.1fs");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Auto-refresh interval");
        }

        // Component filter popup
        if (_showComponentFilter)
        {
            DrawComponentFilterPopup();
        }
    }

    private void DrawComponentFilterPopup()
    {
        ImGui.SetNextWindowSize(new Vector2(250, 300), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Component Filter", ref _showComponentFilter, ImGuiWindowFlags.NoCollapse))
        {
            // Filter mode
            ImGui.Text("Match:");
            ImGui.SameLine();
            if (ImGui.RadioButton("Any", _filterMode == ComponentFilterMode.Any))
            {
                _filterMode = ComponentFilterMode.Any;
                UpdateFilteredEntities();
            }
            ImGui.SameLine();
            if (ImGui.RadioButton("All", _filterMode == ComponentFilterMode.All))
            {
                _filterMode = ComponentFilterMode.All;
                UpdateFilteredEntities();
            }

            // Search within components
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint(
                "##compsearch",
                "Filter components...",
                ref _componentSearchFilter,
                128
            );

            ImGui.Separator();

            // Clear all button
            if (_selectedComponentFilters.Count > 0)
            {
                if (ImGui.Button("Clear All"))
                {
                    _selectedComponentFilters.Clear();
                    UpdateFilteredEntities();
                }
                ImGui.Separator();
            }

            // Component list
            ImGui.BeginChild("ComponentList", Vector2.Zero, ImGuiChildFlags.None);
            var sortedTypes = _knownComponentTypes.OrderBy(t => t.Name).ToList();

            foreach (var componentType in sortedTypes)
            {
                var typeName = componentType.Name;

                // Filter by search
                if (
                    !string.IsNullOrEmpty(_componentSearchFilter)
                    && !typeName.Contains(
                        _componentSearchFilter,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    continue;

                var isSelected = _selectedComponentFilters.Contains(componentType);
                if (ImGui.Checkbox(typeName, ref isSelected))
                {
                    if (isSelected)
                        _selectedComponentFilters.Add(componentType);
                    else
                        _selectedComponentFilters.Remove(componentType);
                    UpdateFilteredEntities();
                }
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }

    private void DrawEntityList()
    {
        var entitiesToShow =
            _selectedComponentFilters.Count > 0 ? _filteredEntities : _cachedEntities;

        foreach (var entity in entitiesToShow)
        {
            if (!_world.IsAlive(entity))
                continue;

            var entityName = GetEntityDisplayName(entity);

            if (
                !string.IsNullOrEmpty(_searchFilter)
                && !entityName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            var isSelected = _selectedEntity.HasValue && _selectedEntity.Value.Id == entity.Id;
            if (ImGui.Selectable(entityName, isSelected))
            {
                _selectedEntity = entity;
            }

            // Tooltip with component list
            if (ImGui.IsItemHovered())
            {
                var components = entity.GetComponentTypes();
                var names = new List<string>();
                foreach (var c in components)
                {
                    names.Add(c.Type.Name);
                }
                ImGui.SetTooltip($"Components ({names.Count}):\n{string.Join("\n", names)}");
            }
        }
    }

    private void DrawComponentInspector()
    {
        if (!_selectedEntity.HasValue)
        {
            ImGui.TextDisabled("Select an entity to inspect");
            return;
        }

        var entity = _selectedEntity.Value;
        if (!_world.IsAlive(entity))
        {
            ImGui.TextColored(
                new System.Numerics.Vector4(1, 0.4f, 0.4f, 1),
                "Entity no longer exists"
            );
            _selectedEntity = null;
            return;
        }

        ImGui.Text($"Entity ID: {entity.Id}");
        ImGui.Separator();

        var componentTypes = entity.GetComponentTypes();
        foreach (var componentType in componentTypes)
        {
            // ComponentType wraps a Type, get the actual Type
            var type = componentType.Type;
            if (ImGui.CollapsingHeader(type.Name, ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                DrawComponent(entity, type);
                ImGui.Unindent();
            }
        }
    }

    private void DrawComponent(Entity entity, Type componentType)
    {
        try
        {
            var component = _world.Get(entity, componentType);
            if (component == null)
            {
                ImGui.TextDisabled("(null)");
                return;
            }

            var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var properties = componentType.GetProperties(
                BindingFlags.Public | BindingFlags.Instance
            );

            foreach (var field in fields)
            {
                var value = field.GetValue(component);
                DrawFieldValue(field.Name, field.FieldType, value);
            }

            foreach (var property in properties)
            {
                if (!property.CanRead)
                    continue;

                try
                {
                    var value = property.GetValue(component);
                    DrawFieldValue(property.Name, property.PropertyType, value);
                }
                catch
                {
                    ImGui.TextColored(
                        new System.Numerics.Vector4(1, 0.7f, 0.4f, 1),
                        $"{property.Name}: <error reading>"
                    );
                }
            }

            if (fields.Length == 0 && properties.Length == 0)
            {
                ImGui.TextDisabled("(marker component)");
            }
        }
        catch (Exception ex)
        {
            ImGui.TextColored(
                new System.Numerics.Vector4(1, 0.4f, 0.4f, 1),
                $"Error: {ex.Message}"
            );
        }
    }

    private static void DrawFieldValue(string name, Type type, object? value)
    {
        var displayValue = value switch
        {
            null => "(null)",
            string s => $"\"{s}\"",
            bool b => b.ToString().ToLower(),
            float f => f.ToString("F3"),
            double d => d.ToString("F3"),
            Entity e => $"Entity({e.Id})",
            _ => value.ToString() ?? "(null)",
        };

        ImGui.Text($"{name}:");
        ImGui.SameLine();
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.9f, 1f, 1f), displayValue);
    }

    private void RefreshEntityList()
    {
        _cachedEntities.Clear();
        _knownComponentTypes.Clear();

        // Use cached QueryDescription (per .cursorrules - never create in hot paths)
        _world.Query(
            in AllEntitiesQuery,
            (Entity entity) =>
            {
                _cachedEntities.Add(entity);

                // Collect all component types
                var componentTypes = entity.GetComponentTypes();
                foreach (var componentType in componentTypes)
                {
                    _knownComponentTypes.Add(componentType.Type);
                }
            }
        );

        UpdateFilteredEntities();
    }

    private void UpdateFilteredEntities()
    {
        _filteredEntities.Clear();

        if (_selectedComponentFilters.Count == 0)
            return;

        foreach (var entity in _cachedEntities)
        {
            if (!_world.IsAlive(entity))
                continue;

            var entityComponents = new HashSet<Type>();
            foreach (var componentType in entity.GetComponentTypes())
            {
                entityComponents.Add(componentType.Type);
            }

            var matches =
                _filterMode == ComponentFilterMode.Any
                    ? _selectedComponentFilters.Any(f => entityComponents.Contains(f))
                    : _selectedComponentFilters.All(f => entityComponents.Contains(f));

            if (matches)
            {
                _filteredEntities.Add(entity);
            }
        }
    }

    private string GetEntityDisplayName(Entity entity)
    {
        var componentTypes = entity.GetComponentTypes();

        // Count components and find first/primary component
        var componentCount = 0;
        string? firstComponentName = null;
        string? primaryComponent = null;

        foreach (var componentType in componentTypes)
        {
            componentCount++;
            var name = componentType.Type.Name;

            // Track first component for fallback display
            firstComponentName ??= name;

            // Try to find a meaningful name - skip common/generic components
            if (
                primaryComponent == null
                && !name.EndsWith("Component")
                && !name.StartsWith("Transform")
                && !name.StartsWith("Position")
            )
            {
                primaryComponent = name;
            }
        }

        if (primaryComponent != null)
        {
            return $"{entity.Id}: {primaryComponent} (+{componentCount - 1})";
        }

        return componentCount > 0
            ? $"{entity.Id}: [{firstComponentName}] ({componentCount})"
            : $"{entity.Id}: (empty)";
    }
}
