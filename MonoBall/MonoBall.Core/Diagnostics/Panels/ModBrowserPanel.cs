namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.UI;
using MonoBall.Core.Logging;
using MonoBall.Core.Mods;
using Serilog;

/// <summary>
/// Debug panel for browsing loaded mods, their metadata, definitions, and file contents.
/// </summary>
public sealed class ModBrowserPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly IModManager _modManager;
    private readonly ILogger _logger;

    // Cached data
    private readonly List<ModManifest> _cachedMods = new();
    private readonly List<ModManifest> _filteredMods = new();
    private readonly List<ModManifest> _cachedDependents = new();
    private ModManifest? _selectedMod;

    // Definition cache for selected mod
    private readonly List<DefinitionDisplayInfo> _modDefinitions = new();
    private readonly List<DefinitionDisplayInfo> _filteredDefinitions = new();
    private string[] _definitionTypes = Array.Empty<string>();
    private int _selectedDefinitionTypeIndex;

    // File tree cache
    private FileTreeNode? _fileTreeRoot;

    // Filter state
    private string _modFilter = string.Empty;
    private string _definitionFilter = string.Empty;
    private string _fileFilter = string.Empty;

    // Refresh state
    private float _refreshInterval = 5f;
    private float _timeSinceRefresh;

    // Layout constants
    private const float LeftPaneWidthRatio = 0.35f;
    private const float MinLeftPaneWidth = 150f;

    /// <inheritdoc />
    public string Id => "mod-browser";

    /// <inheritdoc />
    public string DisplayName => "Mod Browser";

    /// <inheritdoc />
    public bool IsVisible { get; set; }

    /// <inheritdoc />
    public string Category => "Content";

    /// <inheritdoc />
    public int SortOrder => 10;

    /// <inheritdoc />
    public Vector2? DefaultSize => new Vector2(700, 500);

    /// <summary>
    /// Initializes the mod browser panel.
    /// </summary>
    /// <param name="modManager">The mod manager for accessing loaded mods.</param>
    /// <exception cref="ArgumentNullException">Thrown when modManager is null.</exception>
    public ModBrowserPanel(IModManager modManager)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = LoggerFactory.CreateLogger<ModBrowserPanel>();
    }

    /// <inheritdoc />
    public void Initialize()
    {
        try
        {
            RefreshModList();
            _logger.Debug("ModBrowserPanel initialized with {ModCount} mods", _cachedMods.Count);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "ModBrowserPanel initialization failed");
        }
    }

    /// <inheritdoc />
    public void Update(float deltaTime)
    {
        if (
            DebugPanelHelpers.UpdateRefreshTimer(ref _timeSinceRefresh, _refreshInterval, deltaTime)
        )
        {
            RefreshModList();
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
        _cachedMods.Clear();
        _filteredMods.Clear();
        _modDefinitions.Clear();
        _filteredDefinitions.Clear();
        _cachedDependents.Clear();
        _fileTreeRoot = null;
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Refresh"))
        {
            RefreshModList();
        }

        ImGui.SameLine();
        ImGui.Text("Filter:");
        ImGui.SameLine();

        ImGui.SetNextItemWidth(150);
        if (ImGui.InputTextWithHint("##modfilter", "Name/Author/ID...", ref _modFilter, 256))
        {
            ApplyModFilter();
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"| Mods: {_filteredMods.Count}");

        ImGui.SameLine();
        DebugPanelHelpers.DrawRefreshSlider(ref _refreshInterval, "modbrowser");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Auto-refresh interval (mods rarely change at runtime)");
        }
    }

    private void DrawTwoPaneLayout()
    {
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var leftPaneWidth = Math.Max(availableWidth * LeftPaneWidthRatio, MinLeftPaneWidth);

        // Left pane - Mod list
        if (
            ImGui.BeginChild(
                "ModList",
                new Vector2(leftPaneWidth, 0),
                DebugPanelHelpers.ResizableChildFlags
            )
        )
        {
            DrawModList();
        }
        ImGui.EndChild();

        ImGui.SameLine();

        // Right pane - Details
        if (ImGui.BeginChild("ModDetails", Vector2.Zero, DebugPanelHelpers.StandardChildFlags))
        {
            DrawModDetails();
        }
        ImGui.EndChild();
    }

    private void DrawModList()
    {
        foreach (var mod in _filteredMods)
        {
            var isSelected = _selectedMod?.Id == mod.Id;
            var isCore = _modManager.IsCoreMod(mod.Id);
            var isCompressed = mod.ModSource?.IsCompressed ?? false;

            // Build display text
            var displayText = $"{mod.Name} v{mod.Version}";
            if (isCompressed)
            {
                displayText = $"[zip] {displayText}";
            }

            // Apply color for core mod
            if (isCore)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, DebugColors.Accent);
            }

            if (ImGui.Selectable(displayText, isSelected))
            {
                SelectMod(mod);
            }

            if (isCore)
            {
                ImGui.PopStyleColor();
            }

            // Tooltip with author and description
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text($"Author: {mod.Author ?? "Unknown"}");
                if (!string.IsNullOrEmpty(mod.Description))
                {
                    ImGui.TextWrapped(
                        mod.Description.Length > 200
                            ? mod.Description[..200] + "..."
                            : mod.Description
                    );
                }
                ImGui.EndTooltip();
            }
        }

        if (_filteredMods.Count == 0)
        {
            DebugPanelHelpers.DrawDisabledText("No mods found");
        }
    }

    private void DrawModDetails()
    {
        if (_selectedMod == null)
        {
            DebugPanelHelpers.DrawDisabledText("Select a mod to view details");
            return;
        }

        if (ImGui.BeginTabBar("ModDetailsTabs"))
        {
            if (ImGui.BeginTabItem("Info"))
            {
                DrawInfoTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Dependencies"))
            {
                DrawDependenciesTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Definitions"))
            {
                DrawDefinitionsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Content"))
            {
                DrawContentTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Files"))
            {
                DrawFilesTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawInfoTab()
    {
        var mod = _selectedMod!;
        var isCore = _modManager.IsCoreMod(mod.Id);

        DebugPanelHelpers.DrawPropertyRow("ID", mod.Id);
        DebugPanelHelpers.DrawPropertyRow("Name", mod.Name);
        DebugPanelHelpers.DrawPropertyRow("Author", mod.Author ?? "Unknown");
        DebugPanelHelpers.DrawPropertyRow("Version", mod.Version ?? "1.0.0");

        var priorityText = isCore ? $"{mod.Priority} (Core)" : mod.Priority.ToString();
        DebugPanelHelpers.DrawPropertyRow(
            "Priority",
            priorityText,
            isCore ? DebugColors.Accent : null
        );

        ImGui.Separator();

        var sourceType = mod.ModSource?.IsCompressed == true ? "Archive (compressed)" : "Directory";
        DebugPanelHelpers.DrawPropertyRow("Source", sourceType);
        DebugPanelHelpers.DrawPropertyRow("Path", mod.ModSource?.SourcePath ?? "N/A");

        ImGui.Separator();

        if (mod.TileWidth > 0 && mod.TileHeight > 0)
        {
            DebugPanelHelpers.DrawPropertyRow("Tile Size", $"{mod.TileWidth} x {mod.TileHeight}");
        }

        ImGui.Separator();
        ImGui.Text("Description:");
        ImGui.Indent();
        if (!string.IsNullOrEmpty(mod.Description))
        {
            ImGui.TextWrapped(mod.Description);
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No description");
        }
        ImGui.Unindent();
    }

    private void DrawDependenciesTab()
    {
        var mod = _selectedMod!;

        // Dependencies
        ImGui.Text("Dependencies:");
        ImGui.Indent();

        if (mod.Dependencies.Count > 0)
        {
            foreach (var dep in mod.Dependencies)
            {
                var isLoaded = _modManager.GetModManifest(dep) != null;
                var icon = isLoaded ? "+" : "x";
                var color = isLoaded ? DebugColors.Success : DebugColors.Error;
                var status = isLoaded ? "(loaded)" : "(not found)";

                ImGui.TextColored(color, $"{icon} {dep} {status}");
            }
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No dependencies");
        }

        ImGui.Unindent();

        ImGui.Separator();

        // Dependents (mods that depend on this one) - use cached list
        ImGui.Text("Dependents:");
        ImGui.Indent();

        if (_cachedDependents.Count > 0)
        {
            foreach (var dependent in _cachedDependents)
            {
                ImGui.BulletText(dependent.Name);
            }
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No mods depend on this");
        }

        ImGui.Unindent();
    }

    private void DrawDefinitionsTab()
    {
        // Toolbar
        ImGui.Text("Filter:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputTextWithHint("##deffilter", "ID...", ref _definitionFilter, 256))
        {
            ApplyDefinitionFilter();
        }

        ImGui.SameLine();
        ImGui.Text("Type:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (
            ImGui.Combo(
                "##deftype",
                ref _selectedDefinitionTypeIndex,
                _definitionTypes,
                _definitionTypes.Length
            )
        )
        {
            ApplyDefinitionFilter();
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"| Count: {_filteredDefinitions.Count}");

        ImGui.Separator();

        // Table
        if (ImGui.BeginTable("DefinitionsTable", 3, DebugPanelHelpers.SortableTableFlags))
        {
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Operation", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();

            foreach (var def in _filteredDefinitions)
            {
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.Text(def.Type);

                ImGui.TableNextColumn();
                ImGui.Text(def.Id);

                // Tooltip with source path
                if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(def.SourcePath))
                {
                    ImGui.SetTooltip($"Source: {def.SourcePath}");
                }

                ImGui.TableNextColumn();
                var opColor = def.Operation switch
                {
                    "Create" => DebugColors.Success,
                    "Modify" => DebugColors.Warning,
                    "Replace" => DebugColors.Error,
                    _ => DebugColors.TextSecondary,
                };
                ImGui.TextColored(opColor, def.Operation);
            }

            ImGui.EndTable();
        }

        if (_filteredDefinitions.Count == 0)
        {
            DebugPanelHelpers.DrawDisabledText("No definitions found");
        }
    }

    private void DrawContentTab()
    {
        var mod = _selectedMod!;

        // Content folders
        ImGui.Text("Content Folders:");
        ImGui.Indent();

        if (mod.ContentFolders.Count > 0)
        {
            foreach (var (name, path) in mod.ContentFolders)
            {
                ImGui.BulletText($"{name} -> {path}");
            }
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No content folders defined");
        }

        ImGui.Unindent();

        ImGui.Separator();

        // Plugins
        ImGui.Text($"Plugins: {mod.Plugins.Count}");
        if (mod.Plugins.Count > 0)
        {
            ImGui.Indent();
            foreach (var plugin in mod.Plugins)
            {
                ImGui.BulletText(plugin);
            }
            ImGui.Unindent();
        }

        // Assemblies
        ImGui.Text($"Assemblies: {mod.Assemblies.Count}");
        if (mod.Assemblies.Count > 0)
        {
            ImGui.Indent();
            foreach (var asm in mod.Assemblies)
            {
                ImGui.BulletText(asm);
            }
            ImGui.Unindent();
        }

        // Patches
        ImGui.Text($"Patches: {mod.Patches.Count}");
        if (mod.Patches.Count > 0)
        {
            ImGui.Indent();
            foreach (var patch in mod.Patches)
            {
                ImGui.BulletText(patch);
            }
            ImGui.Unindent();
        }
    }

    private void DrawFilesTab()
    {
        // Filter toolbar
        ImGui.Text("Filter:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputTextWithHint("##filefilter", "*.json, *.png...", ref _fileFilter, 256))
        {
            // Filter is applied during tree traversal
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh##files"))
        {
            RefreshFileTree();
        }

        var fileCount = _fileTreeRoot?.GetFileCount() ?? 0;
        ImGui.SameLine();
        ImGui.TextDisabled($"| Files: {fileCount}");

        ImGui.Separator();

        // Tree view
        if (_fileTreeRoot != null)
        {
            DrawFileTreeNode(_fileTreeRoot);
        }
        else
        {
            DebugPanelHelpers.DrawDisabledText("No files available");
        }
    }

    private void DrawFileTreeNode(FileTreeNode node)
    {
        // Apply filter
        if (!string.IsNullOrEmpty(_fileFilter) && !node.MatchesFilter(_fileFilter))
        {
            return;
        }

        if (node.IsDirectory)
        {
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick;
            if (node.Children.Count == 0)
            {
                flags |= ImGuiTreeNodeFlags.Leaf;
            }

            var isOpen = ImGui.TreeNodeEx(node.Name, flags);

            if (isOpen)
            {
                // Children are pre-sorted in SortChildren() after tree build
                foreach (var child in node.Children)
                {
                    DrawFileTreeNode(child);
                }
                ImGui.TreePop();
            }
        }
        else
        {
            // Leaf node (file)
            ImGui.TreeNodeEx(
                node.Name,
                ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen
            );

            // Show size in tooltip
            if (ImGui.IsItemHovered() && node.Size > 0)
            {
                ImGui.SetTooltip($"Size: {FormatFileSize(node.Size)}");
            }
        }
    }

    private void RefreshModList()
    {
        _cachedMods.Clear();
        _cachedMods.AddRange(_modManager.LoadedMods);
        ApplyModFilter();

        // If selected mod no longer exists, clear selection
        if (_selectedMod != null && !_cachedMods.Any(m => m.Id == _selectedMod.Id))
        {
            _selectedMod = null;
        }
    }

    private void ApplyModFilter()
    {
        _filteredMods.Clear();

        if (string.IsNullOrWhiteSpace(_modFilter))
        {
            _filteredMods.AddRange(_cachedMods);
        }
        else
        {
            var filter = _modFilter.ToLowerInvariant();
            _filteredMods.AddRange(
                _cachedMods.Where(m =>
                    m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || m.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || (m.Author?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                )
            );
        }

        // Sort: core mod first, then by priority, then by name
        _filteredMods.Sort(
            (a, b) =>
            {
                var aIsCore = _modManager.IsCoreMod(a.Id);
                var bIsCore = _modManager.IsCoreMod(b.Id);

                if (aIsCore != bIsCore)
                    return aIsCore ? -1 : 1;

                var priorityCompare = a.Priority.CompareTo(b.Priority);
                return priorityCompare != 0
                    ? priorityCompare
                    : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            }
        );
    }

    private void SelectMod(ModManifest mod)
    {
        _selectedMod = mod;
        RefreshDefinitions();
        RefreshFileTree();

        // Cache dependents (mods that depend on this one)
        _cachedDependents.Clear();
        foreach (var m in _cachedMods)
        {
            if (m.Dependencies.Contains(mod.Id))
            {
                _cachedDependents.Add(m);
            }
        }
    }

    private void RefreshDefinitions()
    {
        _modDefinitions.Clear();
        _definitionFilter = string.Empty;
        _selectedDefinitionTypeIndex = 0;

        if (_selectedMod == null)
        {
            _definitionTypes = new[] { "All" };
            _filteredDefinitions.Clear();
            return;
        }

        // Get all definitions from this mod
        var allDefinitions = _modManager.Registry.GetAll();
        var modDefinitions = allDefinitions
            .Where(d =>
                d.OriginalModId == _selectedMod.Id || d.LastModifiedByModId == _selectedMod.Id
            )
            .ToList();

        foreach (var def in modDefinitions)
        {
            _modDefinitions.Add(
                new DefinitionDisplayInfo
                {
                    Id = def.Id,
                    Type = def.DefinitionType,
                    Operation = def.Operation.ToString(),
                    SourceMod = def.OriginalModId,
                    SourcePath = def.SourcePath,
                }
            );
        }

        // Build type list
        var types = new HashSet<string> { "All" };
        foreach (var def in _modDefinitions)
        {
            types.Add(def.Type);
        }
        _definitionTypes = types.OrderBy(t => t == "All" ? "" : t).ToArray();

        ApplyDefinitionFilter();
    }

    private void ApplyDefinitionFilter()
    {
        _filteredDefinitions.Clear();

        var typeFilter =
            _selectedDefinitionTypeIndex > 0
            && _selectedDefinitionTypeIndex < _definitionTypes.Length
                ? _definitionTypes[_selectedDefinitionTypeIndex]
                : null;

        foreach (var def in _modDefinitions)
        {
            // Type filter
            if (typeFilter != null && def.Type != typeFilter)
                continue;

            // Text filter
            if (
                !string.IsNullOrWhiteSpace(_definitionFilter)
                && !def.Id.Contains(_definitionFilter, StringComparison.OrdinalIgnoreCase)
            )
                continue;

            _filteredDefinitions.Add(def);
        }
    }

    private void RefreshFileTree()
    {
        _fileTreeRoot = null;

        if (_selectedMod?.ModSource == null)
            return;

        var source = _selectedMod.ModSource;
        _fileTreeRoot = new FileTreeNode(_selectedMod.Name, true);

        try
        {
            foreach (var filePath in source.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                AddFileToTree(_fileTreeRoot, filePath, 0);
            }

            // Sort tree once after building (directories first, then by name)
            _fileTreeRoot.SortChildren();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to enumerate files for mod {ModId}", _selectedMod.Id);
        }
    }

    private static void AddFileToTree(FileTreeNode root, string path, long size)
    {
        var parts = path.Split('/', '\\');
        var current = root;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var isLast = i == parts.Length - 1;

            var child = current.Children.FirstOrDefault(c => c.Name == part);
            if (child == null)
            {
                child = new FileTreeNode(part, !isLast) { Size = isLast ? size : 0 };
                current.Children.Add(child);
            }

            current = child;
        }
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F2} MB",
        };
    }

    /// <summary>
    /// Cached information about a definition for display.
    /// </summary>
    private readonly struct DefinitionDisplayInfo
    {
        public string Id { get; init; }
        public string Type { get; init; }
        public string Operation { get; init; }
        public string SourceMod { get; init; }
        public string SourcePath { get; init; }
    }

    /// <summary>
    /// Tree node for file browser.
    /// </summary>
    private sealed class FileTreeNode
    {
        public string Name { get; }
        public bool IsDirectory { get; }
        public long Size { get; set; }
        public List<FileTreeNode> Children { get; } = new();

        public FileTreeNode(string name, bool isDirectory)
        {
            Name = name;
            IsDirectory = isDirectory;
        }

        public int GetFileCount()
        {
            if (!IsDirectory)
                return 1;

            return Children.Sum(c => c.GetFileCount());
        }

        public bool MatchesFilter(string filter)
        {
            if (IsDirectory)
            {
                return Children.Any(c => c.MatchesFilter(filter));
            }

            // Simple glob matching
            if (filter.StartsWith("*."))
            {
                var ext = filter[1..]; // Remove *
                return Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
            }

            return Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Recursively sorts all children (directories first, then alphabetically).
        /// Call once after building the tree to avoid sorting during Draw.
        /// </summary>
        public void SortChildren()
        {
            if (Children.Count == 0)
                return;

            Children.Sort(
                (a, b) =>
                {
                    // Directories first
                    if (a.IsDirectory != b.IsDirectory)
                        return a.IsDirectory ? -1 : 1;

                    return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                }
            );

            foreach (var child in Children)
            {
                child.SortChildren();
            }
        }
    }
}
