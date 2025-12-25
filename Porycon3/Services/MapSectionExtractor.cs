using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Extracts map section (MAPSEC) definitions and popup theme mappings from pokeemerald.
/// Matches porycon2's section_extractor.py output format.
/// </summary>
public class MapSectionExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly string _region;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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

    public MapSectionExtractor(string inputPath, string outputPath, string region)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _region = region;
    }

    /// <summary>
    /// Extract all map sections and popup themes.
    /// </summary>
    public (int Sections, int Themes) ExtractAll()
    {
        // Extract section definitions from JSON
        var sections = ExtractSections();
        if (sections.Count == 0)
            return (0, 0);

        // Extract theme mappings from C file
        var themeMapping = ExtractThemeMapping();

        // Merge section data with theme mappings
        var completeSections = MergeSectionData(sections, themeMapping);

        // Save sections
        var sectionCount = SaveSections(completeSections);

        // Save themes
        var themeCount = SaveThemes();

        return (sectionCount, themeCount);
    }

    private Dictionary<string, JsonElement> ExtractSections()
    {
        var sectionsFile = Path.Combine(_inputPath, "src", "data", "region_map", "region_map_sections.json");
        if (!File.Exists(sectionsFile))
            return new Dictionary<string, JsonElement>();

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
        catch
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private Dictionary<string, string> ExtractThemeMapping()
    {
        var popupCFile = Path.Combine(_inputPath, "src", "map_name_popup.c");
        if (!File.Exists(popupCFile))
            return new Dictionary<string, string>();

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
        catch
        {
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
            // Transform to unified ID format: base:mapsec:hoenn/name
            var sectionName = sectionId.ToLowerInvariant().Replace("mapsec_", "");
            var unifiedId = $"base:mapsec:{_region}/{sectionName}";

            // Get theme from mapping or default to wood
            var themeName = themeMapping.GetValueOrDefault(sectionId, "wood");
            var unifiedTheme = $"base:theme:popup/{themeName}";

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
                    Id = $"base:mapsec:{_region}/{sectionName}",
                    Name = FormatDisplayName(sectionName),
                    Theme = $"base:theme:popup/{theme}"
                };
            }
        }

        return merged;
    }

    private int SaveSections(Dictionary<string, MapSectionData> sections)
    {
        var outputDir = Path.Combine(_outputPath, "Definitions", "Maps", "Sections");
        Directory.CreateDirectory(outputDir);

        int count = 0;
        foreach (var (sectionId, sectionData) in sections)
        {
            var filename = sectionId.ToLowerInvariant() + ".json";
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

            File.WriteAllText(filepath, JsonSerializer.Serialize(definition, JsonOptions));
            count++;
        }

        return count;
    }

    private int SaveThemes()
    {
        var outputDir = Path.Combine(_outputPath, "Definitions", "Maps", "Popups", "Themes");
        Directory.CreateDirectory(outputDir);

        int count = 0;
        foreach (var themeName in ThemeNames.Values)
        {
            var (displayName, description) = ThemeInfo.GetValueOrDefault(themeName, (themeName.Replace("_", " ").ToUpperInvariant(), ""));

            var definition = new
            {
                id = $"base:theme:popup/{themeName}",
                name = displayName,
                description,
                background = $"base:popup:background/{themeName}",
                outline = $"base:popup:outline/{themeName}_outline"
            };

            var filepath = Path.Combine(outputDir, $"{themeName}.json");
            File.WriteAllText(filepath, JsonSerializer.Serialize(definition, JsonOptions));
            count++;
        }

        return count;
    }

    private static string FormatDisplayName(string name)
    {
        return string.Join(" ", name.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..].ToLower() : w));
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
