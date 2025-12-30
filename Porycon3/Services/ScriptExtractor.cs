using System.Text.Json;
using System.Text.RegularExpressions;
using Porycon3.Services.Extraction;

namespace Porycon3.Services;

/// <summary>
/// Extracts interaction definitions from pokeemerald-expansion.
/// Scans map event scripts and metatile behaviors to create definition files.
/// </summary>
public class ScriptExtractor : ExtractorBase
{
    public override string Name => "Interaction Definitions";
    public override string Description => "Extracts NPC and tile interaction definitions";

    // Categories of interactions we extract
    private readonly HashSet<string> _npcInteractions = new();
    private readonly HashSet<string> _triggerScripts = new();
    private readonly HashSet<string> _signScripts = new();

    public ScriptExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
    }

    protected override int ExecuteExtraction()
    {
        // Scan all map scripts to find script references
        WithStatus("Scanning map scripts...", _ => ScanMapScripts());

        // Create output directories
        var npcInteractionsPath = Path.Combine(OutputPath, "Definitions", "Interactions", "NPCs");
        var tileInteractionsPath = Path.Combine(OutputPath, "Definitions", "Interactions", "Tiles");
        var triggersPath = Path.Combine(OutputPath, "Definitions", "Scripts", "Triggers");
        var signsPath = Path.Combine(OutputPath, "Definitions", "Scripts", "Signs");

        EnsureDirectory(npcInteractionsPath);
        EnsureDirectory(tileInteractionsPath);
        EnsureDirectory(triggersPath);
        EnsureDirectory(signsPath);

        int npcCount = 0;
        int tileCount = 0;
        int triggerCount = 0;
        int signCount = 0;

        // Generate NPC interaction definitions (parallel)
        var npcList = _npcInteractions.OrderBy(s => s).ToList();
        if (npcList.Count > 0)
        {
            WithParallelProgress("Extracting NPC interactions", npcList, scriptName =>
            {
                var def = CreateNpcInteractionDefinition(scriptName);
                var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
                File.WriteAllText(Path.Combine(npcInteractionsPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                Interlocked.Increment(ref npcCount);
            });
        }

        // Generate tile interaction definitions from metatile behaviors
        var tileInteractions = GenerateTileInteractionList();
        if (tileInteractions.Count > 0)
        {
            WithParallelProgress("Extracting tile interactions", tileInteractions, name =>
            {
                var def = CreateTileInteractionDefinition(name);
                var fileName = $"{name}.json";
                File.WriteAllText(Path.Combine(tileInteractionsPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                Interlocked.Increment(ref tileCount);
            });
        }

        // Generate trigger script definitions (parallel)
        var triggerList = _triggerScripts.OrderBy(s => s).ToList();
        if (triggerList.Count > 0)
        {
            WithParallelProgress("Extracting trigger scripts", triggerList, scriptName =>
            {
                var def = CreateScriptDefinition(scriptName, "trigger", "Triggers");
                var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
                File.WriteAllText(Path.Combine(triggersPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                Interlocked.Increment(ref triggerCount);
            });
        }

        // Generate sign script definitions (parallel)
        var signList = _signScripts.OrderBy(s => s).ToList();
        if (signList.Count > 0)
        {
            WithParallelProgress("Extracting sign scripts", signList, scriptName =>
            {
                var def = CreateScriptDefinition(scriptName, "sign", "Signs");
                var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
                File.WriteAllText(Path.Combine(signsPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                Interlocked.Increment(ref signCount);
            });
        }

        SetCount("NPC Interactions", npcCount);
        SetCount("Tile Interactions", tileCount);
        SetCount("Triggers", triggerCount);
        SetCount("Signs", signCount);

        return npcCount + tileCount + triggerCount + signCount;
    }

    /// <summary>
    /// Generate list of tile interaction names.
    /// </summary>
    private static List<string> GenerateTileInteractionList() =>
    [
        "tall_grass",
        "very_tall_grass",
        "underwater_grass",
        "shore_water",
        "deep_water",
        "waterfall",
        "ocean_water",
        "pond_water",
        "puddle",
        "no_running",
        "indoor_encounter",
        "mountain",
        "secret_base_hole",
        "footprints",
        "thin_ice",
        "cracked_ice",
        "hot_spring",
        "lava",
        "sand",
        "ash_grass",
        "sand_cave",
        "ledge_south",
        "ledge_north",
        "ledge_east",
        "ledge_west",
        "ledge_southeast",
        "ledge_southwest",
        "ledge_northeast",
        "ledge_northwest",
        "stairs_south",
        "stairs_north",
        "impassable_south",
        "impassable_north",
        "impassable_east",
        "impassable_west",
        "cycling_road_pull_south",
        "cycling_road_pull_east",
        "bump",
        "walk_south",
        "walk_north",
        "walk_east",
        "walk_west",
        "slide_south",
        "slide_north",
        "slide_east",
        "slide_west",
        "trick_house_puzzle_8_floor",
        "muddy_slope",
        "spin_right",
        "spin_left",
        "spin_down",
        "spin_up",
        "ice_spin_right",
        "ice_spin_left",
        "ice_spin_down",
        "ice_spin_up",
        "secret_base_rock_wall",
        "secret_base_shrub",
        "warp_or_bridge",
        "warp_door",
        "pokecenter_sign",
        "pokemart_sign",
        "berry_tree_soil",
        "secret_base_pc",
    ];

    private void ScanMapScripts()
    {
        // Scan converted map definitions to find interaction scripts
        var mapsPath = Path.Combine(OutputPath, "Maps");
        if (!Directory.Exists(mapsPath))
        {
            LogWarning($"Maps output path not found: {mapsPath}");
            return;
        }

        foreach (var mapFile in Directory.GetFiles(mapsPath, "*.json", SearchOption.AllDirectories))
        {
            ScanConvertedMapFile(mapFile);
        }

        LogVerbose($"Found {_npcInteractions.Count} NPC interactions, {_triggerScripts.Count} triggers, {_signScripts.Count} signs");
    }

    private void ScanConvertedMapFile(string filePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            var root = doc.RootElement;

            // Scan interactions - signs have "Sign:" prefix in name
            if (root.TryGetProperty("interactions", out var interactions))
            {
                foreach (var interaction in interactions.EnumerateArray())
                {
                    if (!interaction.TryGetProperty("interactionId", out var idProp)) continue;
                    var interactionId = idProp.GetString();
                    if (string.IsNullOrEmpty(interactionId)) continue;

                    // Extract script name from interactionId (e.g., "base:interaction/npcs/scriptname")
                    var scriptName = ExtractScriptName(interactionId);
                    if (string.IsNullOrEmpty(scriptName)) continue;

                    // Check name prefix to determine type
                    var name = interaction.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                    if (name?.StartsWith("Sign:", StringComparison.OrdinalIgnoreCase) == true)
                        _signScripts.Add(scriptName);
                    else
                        _npcInteractions.Add(scriptName);
                }
            }

            // Scan NPCs for their interaction scripts
            if (root.TryGetProperty("npcs", out var npcs))
            {
                foreach (var npc in npcs.EnumerateArray())
                {
                    if (!npc.TryGetProperty("interactionId", out var idProp)) continue;
                    var interactionId = idProp.GetString();
                    if (string.IsNullOrEmpty(interactionId)) continue;

                    var scriptName = ExtractScriptName(interactionId);
                    if (!string.IsNullOrEmpty(scriptName))
                        _npcInteractions.Add(scriptName);
                }
            }

            // Scan triggers
            if (root.TryGetProperty("triggers", out var triggers))
            {
                foreach (var trigger in triggers.EnumerateArray())
                {
                    if (!trigger.TryGetProperty("scriptId", out var idProp)) continue;
                    var scriptId = idProp.GetString();
                    if (string.IsNullOrEmpty(scriptId)) continue;

                    var scriptName = ExtractScriptName(scriptId);
                    if (!string.IsNullOrEmpty(scriptName))
                        _triggerScripts.Add(scriptName);
                }
            }
        }
        catch (JsonException ex)
        {
            LogWarning($"Failed to parse {filePath}: {ex.Message}");
        }
    }

    private static string? ExtractScriptName(string interactionId)
    {
        // Extract the script name from IDs like "base:interaction/npcs/scriptname"
        var lastSlash = interactionId.LastIndexOf('/');
        if (lastSlash < 0) return null;
        return interactionId[(lastSlash + 1)..];
    }

    private object CreateNpcInteractionDefinition(string scriptName)
    {
        var normalizedName = IdTransformer.Normalize(scriptName);
        var friendlyName = ScriptToFriendlyName(scriptName);

        return new Dictionary<string, object>
        {
            ["id"] = $"{IdTransformer.Namespace}:interaction/npcs/{normalizedName}",
            ["name"] = friendlyName,
            ["description"] = $"NPC interaction: {friendlyName}",
            ["scriptPath"] = $"Scripts/Interactions/NPCs/{normalizedName}.csx",
            ["category"] = "npc",
            ["priority"] = 500,
            ["parameters"] = new List<object>()
        };
    }

    private object CreateTileInteractionDefinition(string behaviorName)
    {
        var friendlyName = BehaviorToFriendlyName(behaviorName);
        var category = GetTileInteractionCategory(behaviorName);

        return new Dictionary<string, object>
        {
            ["id"] = $"{IdTransformer.Namespace}:interaction/tiles/{behaviorName}",
            ["name"] = friendlyName,
            ["description"] = $"Tile interaction: {friendlyName}",
            ["category"] = category,
            ["priority"] = 500
        };
    }

    private object CreateScriptDefinition(string scriptName, string category, string subfolder)
    {
        var normalizedName = IdTransformer.Normalize(scriptName);
        var friendlyName = ScriptToFriendlyName(scriptName);

        return new Dictionary<string, object>
        {
            ["id"] = $"{IdTransformer.Namespace}:script:{category}/{normalizedName}",
            ["name"] = friendlyName,
            ["description"] = $"{category.ToUpper()[0]}{category[1..]} script: {friendlyName}",
            ["scriptPath"] = $"Scripts/{subfolder}/{normalizedName}.csx",
            ["category"] = category,
            ["priority"] = 500,
            ["parameters"] = new List<object>()
        };
    }

    private static string ScriptToFriendlyName(string name)
    {
        // LittlerootTown_EventScript_Boy -> Littleroot Town Boy Interaction
        var cleaned = name
            .Replace("_EventScript_", " ")
            .Replace("EventScript_", "")
            .Replace("_EventScript", "")
            .Replace("EventScript", "")
            .Replace("_Script_", " ")
            .Replace("Script_", "")
            .Replace("_Script", "");

        // Add spaces before capitals
        var result = Regex.Replace(cleaned, @"([a-z])([A-Z])", "$1 $2");

        // Replace underscores with spaces
        result = result.Replace("_", " ");

        // Clean up multiple spaces
        result = Regex.Replace(result, @"\s+", " ").Trim();

        return result;
    }

    private static string BehaviorToFriendlyName(string name)
    {
        // tall_grass -> Tall Grass
        var words = name.Split('_');
        return string.Join(" ", words.Select(w =>
            string.IsNullOrEmpty(w) ? w : char.ToUpper(w[0]) + w[1..]));
    }

    private static string GetTileInteractionCategory(string behaviorName)
    {
        if (behaviorName.Contains("grass") || behaviorName.Contains("encounter"))
            return "encounter";
        if (behaviorName.Contains("water") || behaviorName.Contains("waterfall") || behaviorName.Contains("pond") || behaviorName.Contains("ocean"))
            return "water";
        if (behaviorName.Contains("ledge"))
            return "ledge";
        if (behaviorName.Contains("slide") || behaviorName.Contains("spin") || behaviorName.Contains("walk"))
            return "movement";
        if (behaviorName.Contains("warp") || behaviorName.Contains("door"))
            return "warp";
        if (behaviorName.Contains("ice"))
            return "ice";
        if (behaviorName.Contains("secret_base"))
            return "secret_base";
        return "misc";
    }
}
