namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using Serilog;

/// <summary>
/// Debug panel for browsing all game definitions across all mods,
/// viewing their metadata, content, and modification history.
/// </summary>
public sealed class DefinitionBrowserPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly IModManager _modManager;
    private readonly ILogger _logger;

    // Cached data - all definitions
    private readonly List<DefinitionDisplayInfo> _allDefinitions = new();
    private readonly List<DefinitionDisplayInfo> _filteredDefinitions = new();
    private DefinitionDisplayInfo? _selectedDefinition;

    // Filter state
    private string _textFilter = string.Empty;
    private List<string> _typeOptions = new() { "All" };
    private int _selectedTypeIndex;
    private List<ModFilterOption> _modOptions = new() { new("", "All") };
    private int _selectedModIndex;

    // Cached lookups
    private readonly Dictionary<string, string> _modDisplayNameCache = new();
    private readonly Dictionary<string, string> _modAbbreviationCache = new();

    // Sort state
    private int _sortColumnIndex;
    private bool _sortAscending = true;

    // Reference tracking
    private readonly Dictionary<string, List<ReferenceInfo>> _outgoingRefs = new();
    private readonly Dictionary<string, List<string>> _incomingRefs = new();

    // Refresh
    private float _refreshInterval = 10f;
    private float _timeSinceRefresh;

    // Layout constants
    private const float LeftPaneWidthRatio = 0.40f;
    private const float MinLeftPaneWidth = 200f;
    private const int MaxJsonStringLength = 100;

    // Filter UI constants
    private const int TextFilterMaxLength = 256;
    private const float TextFilterWidth = 120f;
    private const float TypeFilterWidth = 140f;
    private const float ModFilterWidth = 160f;

    // Table column widths
    private const float TypeColumnWidth = 80f;
    private const float ModColumnWidth = 60f;
    private const float PropertyColumnWidth = 150f;

    // Reference field patterns (for detecting definition references in JSON)
    private static readonly string[] ReferenceFieldSuffixes = { "Id" };
    private static readonly string[] ReferenceFieldNames =
    {
        "script",
        "behavior",
        "font",
        "shader",
        "theme",
        "background",
        "outline",
    };
    private static readonly string[] ReferenceArrayFieldSuffixes = { "Ids" };
    private static readonly string[] ReferenceArrayFieldNames =
    {
        "scripts",
        "behaviors",
        "dependencies",
    };

    // Cached indent strings for JSON rendering (avoids allocations)
    private static readonly string[] IndentStrings = Enumerable
        .Range(0, 20)
        .Select(i => new string(' ', i * 2))
        .ToArray();

    // JSON colors
    private static readonly Vector4 JsonKey = new(0.6f, 0.9f, 1.0f, 1.0f);
    private static readonly Vector4 JsonString = new(0.6f, 0.9f, 0.6f, 1.0f);
    private static readonly Vector4 JsonNumber = new(1.0f, 0.9f, 0.5f, 1.0f);
    private static readonly Vector4 JsonBool = new(0.8f, 0.6f, 1.0f, 1.0f);
    private static readonly Vector4 JsonNull = new(0.8f, 0.6f, 1.0f, 1.0f);
    private static readonly Vector4 JsonBracket = new(0.7f, 0.7f, 0.7f, 1.0f);

    /// <inheritdoc />
    public string Id => "definition-browser";

    /// <inheritdoc />
    public string DisplayName => "Definition Browser";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "Content";

    /// <inheritdoc />
    public int SortOrder => 20;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(800, 550);

    /// <summary>
    /// Initializes the definition browser panel.
    /// </summary>
    /// <param name="modManager">The mod manager for accessing definitions.</param>
    /// <exception cref="ArgumentNullException">Thrown when modManager is null.</exception>
    public DefinitionBrowserPanel(IModManager modManager)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = LoggerFactory.CreateLogger<DefinitionBrowserPanel>();
    }

    /// <inheritdoc />
    public void Initialize()
    {
        RefreshDefinitions();
        _logger.Debug(
            "DefinitionBrowserPanel initialized with {Count} definitions",
            _allDefinitions.Count
        );
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        if (
            DebugPanelHelpers.UpdateRefreshTimer(ref _timeSinceRefresh, _refreshInterval, deltaTime)
        )
        {
            RefreshDefinitions();
        }
    }

    /// <inheritdoc />
    public void Draw(float deltaTime)
    {
        DrawToolbar();
        ImGui.Separator();
        DrawTwoPaneLayout();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _allDefinitions.Clear();
        _filteredDefinitions.Clear();
        _outgoingRefs.Clear();
        _incomingRefs.Clear();
        _modDisplayNameCache.Clear();
        _modAbbreviationCache.Clear();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Refresh"))
        {
            RefreshDefinitions();
        }

        ImGui.SameLine();
        ImGui.Text("Filter:");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(TextFilterWidth);
        if (ImGui.InputTextWithHint("##deffilter", "ID...", ref _textFilter, TextFilterMaxLength))
        {
            ApplyFilters();
        }

        ImGui.SameLine();
        ImGui.Text("Type:");
        ImGui.SameLine();
        if (DrawFilterCombo("##typefilter", _typeOptions, ref _selectedTypeIndex, TypeFilterWidth))
        {
            ApplyFilters();
        }

        ImGui.SameLine();
        ImGui.Text("Mod:");
        ImGui.SameLine();
        if (DrawModFilterCombo("##modfilter", ref _selectedModIndex, ModFilterWidth))
        {
            ApplyFilters();
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"| {_filteredDefinitions.Count}/{_allDefinitions.Count}");
    }

    /// <summary>
    /// Draws a filter combo with bounds checking. Returns true if selection changed.
    /// </summary>
    private static bool DrawFilterCombo(
        string id,
        List<string> options,
        ref int selectedIndex,
        float width
    )
    {
        // Bounds check - reset if out of range
        if (selectedIndex < 0 || selectedIndex >= options.Count)
            selectedIndex = 0;

        var changed = false;
        var currentItem = options.Count > 0 ? options[selectedIndex] : "";

        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo(id, currentItem, ImGuiComboFlags.HeightLarge))
        {
            for (var i = 0; i < options.Count; i++)
            {
                var isSelected = selectedIndex == i;
                if (ImGui.Selectable(options[i], isSelected))
                {
                    selectedIndex = i;
                    changed = true;
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    /// <summary>
    /// Draws the mod filter combo with bounds checking. Returns true if selection changed.
    /// </summary>
    private bool DrawModFilterCombo(string id, ref int selectedIndex, float width)
    {
        // Bounds check - reset if out of range
        if (selectedIndex < 0 || selectedIndex >= _modOptions.Count)
            selectedIndex = 0;

        var changed = false;
        var currentItem = _modOptions.Count > 0 ? _modOptions[selectedIndex].DisplayName : "";

        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo(id, currentItem, ImGuiComboFlags.HeightLarge))
        {
            for (var i = 0; i < _modOptions.Count; i++)
            {
                var isSelected = selectedIndex == i;
                if (ImGui.Selectable(_modOptions[i].DisplayName, isSelected))
                {
                    selectedIndex = i;
                    changed = true;
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        return changed;
    }

    private void DrawTwoPaneLayout()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var leftPaneWidth = Math.Max(availableWidth * LeftPaneWidthRatio, MinLeftPaneWidth);

        // Left pane - Definition list
        if (
            ImGui.BeginChild(
                "DefList",
                new Vector2(leftPaneWidth, 0),
                DebugPanelHelpers.ResizableChildFlags
            )
        )
        {
            DrawDefinitionList();
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Right pane - Details
        if (ImGui.BeginChild("DefDetails", Vector2.Zero, DebugPanelHelpers.StandardChildFlags))
        {
            DrawDefinitionDetails();
        }
        ImGui.EndChild();
    }

    private void DrawDefinitionList()
    {
        var tableFlags =
            ImGuiTableFlags.Borders
            | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.Resizable
            | ImGuiTableFlags.ScrollY
            | ImGuiTableFlags.Sortable
            | ImGuiTableFlags.SizingFixedFit;

        if (ImGui.BeginTable("DefsTable", 3, tableFlags))
        {
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, TypeColumnWidth);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Mod", ImGuiTableColumnFlags.WidthFixed, ModColumnWidth);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            // Handle sorting
            HandleTableSort();

            // Use clipper for virtualized rendering (only render visible rows)
            var clipper = new ImGuiListClipper();
            clipper.Begin(_filteredDefinitions.Count);

            while (clipper.Step())
            {
                for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                {
                    var def = _filteredDefinitions[row];
                    ImGui.TableNextRow();

                    var isSelected = _selectedDefinition?.Id == def.Id;
                    var opColor = GetOperationColor(def.Operation);

                    // Type column
                    ImGui.TableNextColumn();
                    ImGui.TextColored(opColor, def.Type);

                    // ID column (selectable)
                    ImGui.TableNextColumn();
                    ImGui.PushID(row); // Unique ID for each row
                    if (ImGui.Selectable(def.Id, isSelected, ImGuiSelectableFlags.SpanAllColumns))
                    {
                        SelectDefinition(def);
                    }
                    ImGui.PopID();

                    // Tooltip
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.Text($"Source: {def.SourcePath}");
                        ImGui.Text($"Operation: {def.Operation}");
                        ImGui.EndTooltip();
                    }

                    // Mod column (abbreviated)
                    ImGui.TableNextColumn();
                    var modAbbrev = GetModAbbreviation(def.ModName);
                    ImGui.TextColored(opColor, modAbbrev);
                }
            }

            clipper.End();
            ImGui.EndTable();
        }

        if (_filteredDefinitions.Count == 0)
        {
            DebugPanelHelpers.DrawDisabledText("No definitions found");
        }
    }

    private unsafe void HandleTableSort()
    {
        var sortSpecs = ImGui.TableGetSortSpecs();

        // Null/validity check - TableGetSortSpecs returns a pointer that could be invalid
        if (sortSpecs.Handle == null || !sortSpecs.SpecsDirty)
            return;

        if (sortSpecs.SpecsCount > 0)
        {
            var spec = sortSpecs.Specs;
            _sortColumnIndex = spec.ColumnIndex;
            _sortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
            SortDefinitions();
            sortSpecs.SpecsDirty = false;
        }
    }

    private void SortDefinitions()
    {
        _filteredDefinitions.Sort(
            (a, b) =>
            {
                var result = _sortColumnIndex switch
                {
                    0 => string.Compare(a.Type, b.Type, StringComparison.Ordinal),
                    1 => string.Compare(a.Id, b.Id, StringComparison.Ordinal),
                    2 => string.Compare(a.ModName, b.ModName, StringComparison.Ordinal),
                    _ => 0,
                };
                return _sortAscending ? result : -result;
            }
        );
    }

    private void DrawDefinitionDetails()
    {
        if (_selectedDefinition == null)
        {
            DebugPanelHelpers.DrawDisabledText("Select a definition to view details");
            return;
        }

        if (ImGui.BeginTabBar("DefDetailsTabs"))
        {
            if (ImGui.BeginTabItem("Info"))
            {
                DrawInfoTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Content"))
            {
                DrawContentTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("References"))
            {
                DrawReferencesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Raw"))
            {
                DrawRawMetadataTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawInfoTab()
    {
        var def = _selectedDefinition!.Value;

        DebugPanelHelpers.DrawPropertyRow("ID", def.Id);
        DebugPanelHelpers.DrawPropertyRow("Type", def.Type);

        // Original mod with display name
        var origModName = GetModDisplayName(def.OriginalModId);
        DebugPanelHelpers.DrawPropertyRow("Original Mod", $"{origModName} ({def.OriginalModId})");

        // Last modified by
        var lastModName = GetModDisplayName(def.LastModifiedByModId);
        if (def.LastModifiedByModId != def.OriginalModId)
        {
            DebugPanelHelpers.DrawPropertyRow("Last Modified", lastModName, DebugColors.Warning);
        }
        else
        {
            DebugPanelHelpers.DrawPropertyRow("Last Modified", lastModName);
        }

        // Operation with color
        var opColor = GetOperationColor(def.Operation);
        DebugPanelHelpers.DrawPropertyRow("Operation", def.Operation, opColor);

        DebugPanelHelpers.DrawPropertyRow("Source Path", def.SourcePath);

        ImGui.Separator();

        // Mod history (simplified - just shows create/modify)
        ImGui.Text("Mod History:");
        ImGui.Indent();
        ImGui.TextColored(
            DebugColors.Success,
            $"1. [Create] {GetModDisplayName(def.OriginalModId)}"
        );
        if (def.LastModifiedByModId != def.OriginalModId)
        {
            ImGui.TextColored(
                DebugColors.Warning,
                $"2. [{def.Operation}] {GetModDisplayName(def.LastModifiedByModId)}"
            );
        }
        ImGui.Unindent();
    }

    private void DrawContentTab()
    {
        var def = _selectedDefinition!.Value;

        // Toolbar
        if (ImGui.Button("Copy JSON"))
        {
            try
            {
                var formatted = JsonSerializer.Serialize(
                    def.Data,
                    new JsonSerializerOptions { WriteIndented = true }
                );
                ImGui.SetClipboardText(formatted);
            }
            catch (JsonException ex)
            {
                _logger.Warning(ex, "Failed to serialize JSON for clipboard");
            }
            catch (NotSupportedException ex)
            {
                _logger.Warning(ex, "Unsupported JSON type when copying to clipboard");
            }
        }

        ImGui.Separator();

        // JSON content with syntax highlighting
        if (ImGui.BeginChild("JsonContent", Vector2.Zero, ImGuiChildFlags.None))
        {
            DrawJsonHighlighted(def.Data, 0);
        }
        ImGui.EndChild();
    }

    private void DrawJsonHighlighted(JsonElement element, int indent, bool trailingComma = false)
    {
        // Use cached indent string to avoid allocations (falls back to dynamic for deep nesting)
        var indentStr =
            indent < IndentStrings.Length ? IndentStrings[indent] : new string(' ', indent * 2);
        var childIndentStr =
            indent + 1 < IndentStrings.Length
                ? IndentStrings[indent + 1]
                : new string(' ', (indent + 1) * 2);

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                ImGui.TextColored(JsonBracket, "{");
                var propCount = 0;
                foreach (var _ in element.EnumerateObject())
                    propCount++;

                var propIndex = 0;
                foreach (var prop in element.EnumerateObject())
                {
                    var isLast = propIndex == propCount - 1;
                    propIndex++;

                    ImGui.Text(childIndentStr);
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(JsonKey, $"\"{prop.Name}\"");
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(JsonBracket, ": ");
                    ImGui.SameLine(0, 0);
                    DrawJsonHighlighted(prop.Value, indent + 1, !isLast);
                }
                ImGui.Text(indentStr);
                ImGui.SameLine(0, 0);
                ImGui.TextColored(JsonBracket, trailingComma ? "}," : "}");
                break;

            case JsonValueKind.Array:
                ImGui.TextColored(JsonBracket, "[");
                var arrayLen = element.GetArrayLength();

                var itemIndex = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var isLast = itemIndex == arrayLen - 1;
                    itemIndex++;

                    ImGui.Text(childIndentStr);
                    ImGui.SameLine(0, 0);
                    DrawJsonHighlighted(item, indent + 1, !isLast);
                }
                ImGui.Text(indentStr);
                ImGui.SameLine(0, 0);
                ImGui.TextColored(JsonBracket, trailingComma ? "]," : "]");
                break;

            case JsonValueKind.String:
                var str = element.GetString() ?? "";
                // Truncate very long strings
                if (str.Length > MaxJsonStringLength)
                {
                    str = str[..MaxJsonStringLength] + "...";
                }
                ImGui.TextColored(JsonString, trailingComma ? $"\"{str}\"," : $"\"{str}\"");
                break;

            case JsonValueKind.Number:
                ImGui.TextColored(
                    JsonNumber,
                    trailingComma ? element.GetRawText() + "," : element.GetRawText()
                );
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                ImGui.TextColored(
                    JsonBool,
                    trailingComma ? element.GetRawText() + "," : element.GetRawText()
                );
                break;

            case JsonValueKind.Null:
                ImGui.TextColored(JsonNull, trailingComma ? "null," : "null");
                break;

            default:
                ImGui.Text(trailingComma ? element.GetRawText() + "," : element.GetRawText());
                break;
        }
    }

    private void DrawReferencesTab()
    {
        var def = _selectedDefinition!.Value;

        // Outgoing references
        ImGui.Text("References (outgoing):");
        ImGui.Indent();

        if (_outgoingRefs.TryGetValue(def.Id, out var outgoing) && outgoing.Count > 0)
        {
            foreach (var refInfo in outgoing)
            {
                // Use cached exists status (computed during RefreshDefinitions)
                var color = refInfo.Exists ? DebugColors.Success : DebugColors.Error;
                var status = refInfo.Exists ? "[loaded]" : "[missing]";
                ImGui.TextColored(color, $"* {refInfo.FieldName}: {refInfo.TargetId} {status}");
            }
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No outgoing references");
        }

        ImGui.Unindent();

        ImGui.Separator();

        // Incoming references
        ImGui.Text("Referenced By (incoming):");
        ImGui.Indent();

        if (_incomingRefs.TryGetValue(def.Id, out var incoming) && incoming.Count > 0)
        {
            foreach (var refId in incoming)
            {
                ImGui.BulletText(refId);
            }
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No incoming references");
        }

        ImGui.Unindent();
    }

    private void DrawRawMetadataTab()
    {
        var def = _selectedDefinition!.Value;

        if (ImGui.BeginTable("MetadataTable", 2, DebugPanelHelpers.StandardTableFlags))
        {
            ImGui.TableSetupColumn(
                "Property",
                ImGuiTableColumnFlags.WidthFixed,
                PropertyColumnWidth
            );
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            DrawMetadataRow("Id", def.Id);
            DrawMetadataRow("DefinitionType", def.Type);
            DrawMetadataRow("OriginalModId", def.OriginalModId);
            DrawMetadataRow("LastModifiedByModId", def.LastModifiedByModId);
            DrawMetadataRow("Operation", def.Operation);
            DrawMetadataRow("SourcePath", def.SourcePath);
            DrawMetadataRow("Data.ValueKind", def.Data.ValueKind.ToString());

            // Property count for objects
            if (def.Data.ValueKind == JsonValueKind.Object)
            {
                var propCount = def.Data.EnumerateObject().Count();
                DrawMetadataRow("Data.PropertyCount", propCount.ToString());
            }
            else if (def.Data.ValueKind == JsonValueKind.Array)
            {
                var arrLen = def.Data.GetArrayLength();
                DrawMetadataRow("Data.ArrayLength", arrLen.ToString());
            }

            ImGui.EndTable();
        }
    }

    private static void DrawMetadataRow(string property, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(property);
        ImGui.TableNextColumn();
        ImGui.TextColored(DebugColors.TextValue, value);
    }

    private void RefreshDefinitions()
    {
        _allDefinitions.Clear();
        _outgoingRefs.Clear();
        _incomingRefs.Clear();
        _modDisplayNameCache.Clear();
        _modAbbreviationCache.Clear();

        var allDefs = _modManager.Registry.GetAll().ToList();

        foreach (var metadata in allDefs)
        {
            var modManifest = _modManager.GetModManifest(metadata.OriginalModId);
            var modName = modManifest?.Name ?? metadata.OriginalModId;

            var displayInfo = new DefinitionDisplayInfo
            {
                Id = metadata.Id,
                Type = metadata.DefinitionType,
                ModName = modName,
                ModId = metadata.OriginalModId,
                Operation = metadata.Operation.ToString(),
                SourcePath = metadata.SourcePath,
                OriginalModId = metadata.OriginalModId,
                LastModifiedByModId = metadata.LastModifiedByModId,
                Data = metadata.Data,
            };

            _allDefinitions.Add(displayInfo);

            // Build reference index
            BuildReferencesForDefinition(displayInfo);
        }

        // Build filter options
        BuildFilterOptions();

        // Apply current filters
        ApplyFilters();
    }

    private void BuildReferencesForDefinition(DefinitionDisplayInfo def)
    {
        var refs = new List<ReferenceInfo>();

        try
        {
            if (def.Data.ValueKind == JsonValueKind.Object)
            {
                ExtractReferences(def.Data, refs);
            }
        }
        catch (JsonException ex)
        {
            _logger.Debug(ex, "Failed to extract references from {DefId}", def.Id);
        }
        catch (InvalidOperationException ex)
        {
            _logger.Debug(ex, "Invalid JSON state when extracting references from {DefId}", def.Id);
        }

        if (refs.Count > 0)
        {
            // Cache exists status for each reference (avoids per-frame registry lookups)
            var refsWithStatus = refs.Select(r => new ReferenceInfo
                {
                    FieldName = r.FieldName,
                    TargetId = r.TargetId,
                    Exists = _modManager.Registry.Contains(r.TargetId),
                })
                .ToList();

            _outgoingRefs[def.Id] = refsWithStatus;

            // Build reverse index
            foreach (var refInfo in refsWithStatus)
            {
                if (!_incomingRefs.TryGetValue(refInfo.TargetId, out var incoming))
                {
                    incoming = new List<string>();
                    _incomingRefs[refInfo.TargetId] = incoming;
                }
                if (!incoming.Contains(def.Id))
                {
                    incoming.Add(def.Id);
                }
            }
        }
    }

    private void ExtractReferences(JsonElement element, List<ReferenceInfo> refs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                // Check if this looks like a reference field
                if (IsReferenceField(prop.Name) && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var targetId = prop.Value.GetString();
                    if (!string.IsNullOrEmpty(targetId) && targetId.Contains(':'))
                    {
                        refs.Add(new ReferenceInfo { FieldName = prop.Name, TargetId = targetId });
                    }
                }
                // Check for array of references
                else if (
                    IsReferenceArrayField(prop.Name)
                    && prop.Value.ValueKind == JsonValueKind.Array
                )
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var targetId = item.GetString();
                            if (!string.IsNullOrEmpty(targetId) && targetId.Contains(':'))
                            {
                                refs.Add(
                                    new ReferenceInfo { FieldName = prop.Name, TargetId = targetId }
                                );
                            }
                        }
                    }
                }
                else
                {
                    // Recurse into nested objects/arrays
                    ExtractReferences(prop.Value, refs);
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ExtractReferences(item, refs);
            }
        }
    }

    private static bool IsReferenceField(string fieldName)
    {
        // Check suffix patterns (e.g., "scriptId", "behaviorId")
        foreach (var suffix in ReferenceFieldSuffixes)
        {
            if (fieldName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check exact field names (e.g., "script", "behavior")
        foreach (var name in ReferenceFieldNames)
        {
            if (fieldName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsReferenceArrayField(string fieldName)
    {
        // Check suffix patterns (e.g., "scriptIds", "behaviorIds")
        foreach (var suffix in ReferenceArrayFieldSuffixes)
        {
            if (fieldName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check exact field names (e.g., "scripts", "behaviors")
        foreach (var name in ReferenceArrayFieldNames)
        {
            if (fieldName.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void BuildFilterOptions()
    {
        // Build type options
        var types = new HashSet<string> { "All" };
        foreach (var def in _allDefinitions)
        {
            types.Add(def.Type);
        }
        _typeOptions = types.OrderBy(t => t == "All" ? "" : t).ToList();

        // Build mod options using the new struct
        var mods = new Dictionary<string, string> { { "", "All" } }; // modId -> displayName
        foreach (var def in _allDefinitions)
        {
            if (!mods.ContainsKey(def.ModId))
            {
                mods[def.ModId] = def.ModName;
            }
        }

        _modOptions = mods.OrderBy(kvp => kvp.Value == "All" ? "" : kvp.Value)
            .Select(kvp => new ModFilterOption(kvp.Key, kvp.Value))
            .ToList();
    }

    private void ApplyFilters()
    {
        _filteredDefinitions.Clear();

        // Bounds-safe filter extraction
        var typeFilter =
            _selectedTypeIndex > 0 && _selectedTypeIndex < _typeOptions.Count
                ? _typeOptions[_selectedTypeIndex]
                : null;
        var modIdFilter =
            _selectedModIndex > 0 && _selectedModIndex < _modOptions.Count
                ? _modOptions[_selectedModIndex].ModId
                : null;

        foreach (var def in _allDefinitions)
        {
            // Type filter
            if (typeFilter != null && def.Type != typeFilter)
                continue;

            // Mod filter
            if (modIdFilter != null && def.ModId != modIdFilter)
                continue;

            // Text filter
            if (
                !string.IsNullOrWhiteSpace(_textFilter)
                && !def.Id.Contains(_textFilter, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            _filteredDefinitions.Add(def);
        }

        // Apply current sort
        SortDefinitions();
    }

    private void SelectDefinition(DefinitionDisplayInfo def)
    {
        _selectedDefinition = def;
    }

    private string GetModDisplayName(string modId)
    {
        // Use cache to avoid repeated manifest lookups
        if (_modDisplayNameCache.TryGetValue(modId, out var cached))
            return cached;

        var manifest = _modManager.GetModManifest(modId);
        var displayName = manifest?.Name ?? modId;
        _modDisplayNameCache[modId] = displayName;
        return displayName;
    }

    private string GetModAbbreviation(string modName)
    {
        // Use cache to avoid allocations per-row
        if (_modAbbreviationCache.TryGetValue(modName, out var cached))
            return cached;

        // Return first letter of each word (up to 3)
        var words = modName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string abbreviation;

        if (words.Length == 0)
        {
            abbreviation = "?";
        }
        else if (words.Length == 1)
        {
            abbreviation =
                words[0].Length >= 2
                    ? words[0][..2].ToUpperInvariant()
                    : words[0].ToUpperInvariant();
        }
        else
        {
            abbreviation = string.Concat(words.Take(3).Select(w => char.ToUpperInvariant(w[0])));
        }

        _modAbbreviationCache[modName] = abbreviation;
        return abbreviation;
    }

    private static Vector4 GetOperationColor(string operation)
    {
        return operation switch
        {
            "Create" => DebugColors.TextPrimary,
            "Modify" => DebugColors.Warning,
            "Extend" => DebugColors.Info,
            "Replace" => new Vector4(1.0f, 0.6f, 0.3f, 1.0f), // Orange
            _ => DebugColors.TextSecondary,
        };
    }

    /// <summary>
    /// Display information for a definition.
    /// </summary>
    private readonly struct DefinitionDisplayInfo
    {
        public string Id { get; init; }
        public string Type { get; init; }
        public string ModName { get; init; }
        public string ModId { get; init; }
        public string Operation { get; init; }
        public string SourcePath { get; init; }
        public string OriginalModId { get; init; }
        public string LastModifiedByModId { get; init; }
        public JsonElement Data { get; init; }
    }

    /// <summary>
    /// Information about a reference from one definition to another.
    /// </summary>
    private readonly struct ReferenceInfo
    {
        public string FieldName { get; init; }
        public string TargetId { get; init; }
        public bool Exists { get; init; }
    }

    /// <summary>
    /// Mod filter dropdown option.
    /// </summary>
    private readonly struct ModFilterOption
    {
        public string ModId { get; }
        public string DisplayName { get; }

        public ModFilterOption(string modId, string displayName)
        {
            ModId = modId;
            DisplayName = displayName;
        }
    }
}
