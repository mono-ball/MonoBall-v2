using System.Text.Json;
using System.Text.RegularExpressions;
using Porycon3.Services.Extraction;
using static Porycon3.Infrastructure.StringUtilities;

namespace Porycon3.Services;

/// <summary>
/// Extracts map section (MAPSEC) definitions and popup theme mappings from pokeemerald.
/// Matches porycon2's section_extractor.py output format.
/// </summary>
public class MapSectionExtractor : ExtractorBase
{
    public override string Name => "Map Sections";
    public override string Description => "Extracts map section definitions and popup themes";

    private readonly string _region;

    // Theme ID mapping (from pokeemerald/src/map_name_popup.c)
    private static readonly Dictionary<int, string> ThemeNames = new()
    {
        [0] = "wood",
        [1] = "marble",
        [2] = "stone",
        [3] = "brick",
        [4] = "underwater",
        [5] = "stone2",
        [6] = "bw_default"
    };

    // Theme display names and descriptions
    private static readonly Dictionary<string, (string Display, string Description)> ThemeInfo = new()
    {
        ["wood"] = ("Wood", "Default wooden frame - used for towns, land routes, woods"),
        ["marble"] = ("Marble", "Marble frame - used for major cities"),
        ["stone"] = ("Stone", "Stone frame - used for caves and dungeons"),
        ["brick"] = ("Brick", "Brick frame - used for some cities"),
        ["underwater"] = ("Underwater", "Underwater frame - used for water routes"),
        ["stone2"] = ("Stone 2", "Stone variant 2 - used for underwater areas"),
        ["bw_default"] = ("BW Default", "Black/white default frame - used for special areas")
    };

    public MapSectionExtractor(string inputPath, string outputPath, string region, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
        _region = region;
    }

    protected override int ExecuteExtraction()
    {
        // Extract section definitions from JSON
        Dictionary<string, JsonElement> sections = new();
        Dictionary<string, string> themeMapping = new();

        WithStatus("Parsing section data...", _ =>
        {
            sections = ExtractSections();
            themeMapping = ExtractThemeMapping();
        });

        if (sections.Count == 0)
        {
            LogWarning("No map sections found in source data");
            return 0;
        }

        LogVerbose($"Found {sections.Count} sections, {themeMapping.Count} theme mappings");

        // Merge section data with theme mappings
        var completeSections = MergeSectionData(sections, themeMapping);

        int sectionCount = 0;
        int themeCount = 0;

        // Save sections
        var sectionList = completeSections.OrderBy(x => x.Key).ToList();
        WithProgress("Extracting map sections", sectionList, (kvp, task) =>
        {
            SetTaskDescription(task, $"[cyan]Creating[/] [yellow]{kvp.Value.Name}[/]");
            SaveSection(kvp.Key, kvp.Value);
            sectionCount++;
        });

        // Save themes
        var themeList = ThemeNames.Values.ToList();
        WithProgress("Extracting popup themes", themeList, (themeName, task) =>
        {
            SetTaskDescription(task, $"[cyan]Creating[/] [yellow]{themeName}[/]");
            SaveTheme(themeName);
            themeCount++;
        });

        SetCount("Themes", themeCount);
        return sectionCount;
    }

    private Dictionary<string, JsonElement> ExtractSections()
    {
        var sectionsFile = Path.Combine(InputPath, "src", "data", "region_map", "region_map_sections.json");
        if (!File.Exists(sectionsFile))
        {
            LogWarning($"Sections file not found: {sectionsFile}");
            return new Dictionary<string, JsonElement>();
        }

        try
        {
            var json = File.ReadAllText(sectionsFile);
            var doc = JsonDocument.Parse(json);
            var sections = new Dictionary<string, JsonElement>();

            if (doc.RootElement.TryGetProperty("map_sections", out var mapSections))
            {
                foreach (var section in mapSections.EnumerateArray())
                {
                    if (section.TryGetProperty("id", out var idProp))
                    {
                        var id = idProp.GetString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            sections[id] = section.Clone();
                        }
                    }
                }
            }

            return sections;
        }
        catch (Exception ex)
        {
            AddError("sections", $"Failed to parse sections: {ex.Message}", ex);
            return new Dictionary<string, JsonElement>();
        }
    }

    private Dictionary<string, string> ExtractThemeMapping()
    {
        var popupCFile = Path.Combine(InputPath, "src", "map_name_popup.c");
        if (!File.Exists(popupCFile))
        {
            LogWarning($"Popup C file not found: {popupCFile}");
            return new Dictionary<string, string>();
        }

        try
        {
            var content = File.ReadAllText(popupCFile);

            // Pattern: [MAPSEC_NAME] = MAPPOPUP_THEME_NAME,
            var pattern = new Regex(@"\[MAPSEC_([A-Z0-9_]+)(?:\s*-\s*KANTO_MAPSEC_COUNT)?\]\s*=\s*MAPPOPUP_THEME_([A-Z0-9_]+)");
            var matches = pattern.Matches(content);

            var themeMapping = new Dictionary<string, string>();
            foreach (Match match in matches)
            {
                var mapsecName = $"MAPSEC_{match.Groups[1].Value}";
                var themeName = match.Groups[2].Value.ToLowerInvariant();
                themeMapping[mapsecName] = themeName;
            }

            return themeMapping;
        }
        catch (Exception ex)
        {
            AddError("themes", $"Failed to parse theme mapping: {ex.Message}", ex);
            return new Dictionary<string, string>();
        }
    }

    private Dictionary<string, MapSectionData> MergeSectionData(
        Dictionary<string, JsonElement> sections,
        Dictionary<string, string> themeMapping)
    {
        var merged = new Dictionary<string, MapSectionData>();

        foreach (var (sectionId, sectionData) in sections)
        {
            // Transform to unified ID format: base:section:hoenn/name
            var sectionName = sectionId.ToLowerInvariant().Replace("mapsec_", "");
            var unifiedId = $"{IdTransformer.Namespace}:section:{_region}/{sectionName}";

            // Get theme from mapping or default to wood
            var themeName = themeMapping.GetValueOrDefault(sectionId, "wood");
            var unifiedTheme = $"{IdTransformer.Namespace}:theme:popup/{themeName}";

            // Get display name
            string? displayName = null;
            if (sectionData.TryGetProperty("name", out var nameProp))
            {
                displayName = nameProp.GetString();
            }
            displayName ??= FormatDisplayName(sectionName);

            var data = new MapSectionData
            {
                Id = unifiedId,
                Name = displayName,
                Theme = unifiedTheme
            };

            // Add region map coordinates if they exist
            if (sectionData.TryGetProperty("x", out var xProp))
                data.X = xProp.GetInt32();
            if (sectionData.TryGetProperty("y", out var yProp))
                data.Y = yProp.GetInt32();
            if (sectionData.TryGetProperty("width", out var wProp))
                data.Width = wProp.GetInt32();
            if (sectionData.TryGetProperty("height", out var hProp))
                data.Height = hProp.GetInt32();

            merged[sectionId] = data;
        }

        // Also add sections that only exist in theme mapping
        foreach (var (sectionId, theme) in themeMapping)
        {
            if (!merged.ContainsKey(sectionId))
            {
                var sectionName = sectionId.ToLowerInvariant().Replace("mapsec_", "");
                merged[sectionId] = new MapSectionData
                {
                    Id = $"{IdTransformer.Namespace}:section:{_region}/{sectionName}",
                    Name = FormatDisplayName(sectionName),
                    Theme = $"{IdTransformer.Namespace}:theme:popup/{theme}"
                };
            }
        }

        return merged;
    }

    private void SaveSection(string sectionId, MapSectionData sectionData)
    {
        var outputDir = GetEntityPath("MapSections");
        EnsureDirectory(outputDir);

        // Convert MAPSEC_ABANDONED_SHIP to AbandonedShip (PascalCase, no prefix)
        var baseName = sectionId.Replace("MAPSEC_", "");
        var filename = ToPascalCase(baseName) + ".json";
        var filepath = Path.Combine(outputDir, filename);

        var definition = new Dictionary<string, object?>
        {
            ["id"] = sectionData.Id,
            ["name"] = sectionData.Name,
            ["theme"] = sectionData.Theme
        };

        if (sectionData.X.HasValue) definition["x"] = sectionData.X.Value;
        if (sectionData.Y.HasValue) definition["y"] = sectionData.Y.Value;
        if (sectionData.Width.HasValue) definition["width"] = sectionData.Width.Value;
        if (sectionData.Height.HasValue) definition["height"] = sectionData.Height.Value;

        File.WriteAllText(filepath, JsonSerializer.Serialize(definition, JsonOptions.Default));
        LogVerbose($"Saved section: {sectionData.Name}");
    }

    private void SaveTheme(string themeName)
    {
        var outputDir = GetEntityPath("PopupThemes");
        EnsureDirectory(outputDir);

        var (displayName, description) = ThemeInfo.GetValueOrDefault(themeName, (themeName.Replace("_", " ").ToUpperInvariant(), ""));

        var definition = new
        {
            id = $"{IdTransformer.Namespace}:theme:popup/{themeName}",
            name = displayName,
            description,
            background = $"{IdTransformer.Namespace}:popup:background/{themeName}",
            outline = $"{IdTransformer.Namespace}:popup:outline/{themeName}"
        };

        // Use PascalCase filename (e.g., "Wood.json", "BwDefault.json")
        var filename = ToPascalCase(themeName.ToUpperInvariant()) + ".json";
        var filepath = Path.Combine(outputDir, filename);
        File.WriteAllText(filepath, JsonSerializer.Serialize(definition, JsonOptions.Default));
        LogVerbose($"Saved theme: {displayName}");
    }

    private class MapSectionData
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Theme { get; init; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
    }
}
