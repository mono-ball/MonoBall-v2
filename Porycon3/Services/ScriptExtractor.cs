using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Extracts script definitions from pokeemerald-expansion.
/// Scans map event scripts and creates definition files for interactions, triggers, and other scripts.
/// </summary>
public class ScriptExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly bool _verbose;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Categories of scripts we extract
    private readonly HashSet<string> _interactionScripts = new();
    private readonly HashSet<string> _triggerScripts = new();
    private readonly HashSet<string> _signScripts = new();
    private readonly HashSet<string> _npcScripts = new();
    private readonly HashSet<string> _itemScripts = new();
    private readonly HashSet<string> _weatherScripts = new();

    public ScriptExtractor(string inputPath, string outputPath, bool verbose = false)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _verbose = verbose;
    }

    public (int Interactions, int Triggers, int Signs, int Total) ExtractAll()
    {
        // Scan all map scripts to find script references
        ScanMapScripts();

        // Create output directories
        var interactionsPath = Path.Combine(_outputPath, "Definitions", "Scripts", "Interactions");
        var triggersPath = Path.Combine(_outputPath, "Definitions", "Scripts", "Triggers");
        var signsPath = Path.Combine(_outputPath, "Definitions", "Scripts", "Signs");

        Directory.CreateDirectory(interactionsPath);
        Directory.CreateDirectory(triggersPath);
        Directory.CreateDirectory(signsPath);

        int interactionCount = 0;
        int triggerCount = 0;
        int signCount = 0;

        // Generate interaction script definitions
        foreach (var scriptName in _interactionScripts)
        {
            var def = CreateScriptDefinition(scriptName, "interaction", "Interactions");
            var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
            File.WriteAllText(Path.Combine(interactionsPath, fileName), JsonSerializer.Serialize(def, JsonOptions));
            interactionCount++;
        }

        // Generate trigger script definitions
        foreach (var scriptName in _triggerScripts)
        {
            var def = CreateScriptDefinition(scriptName, "trigger", "Triggers");
            var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
            File.WriteAllText(Path.Combine(triggersPath, fileName), JsonSerializer.Serialize(def, JsonOptions));
            triggerCount++;
        }

        // Generate sign script definitions
        foreach (var scriptName in _signScripts)
        {
            var def = CreateScriptDefinition(scriptName, "sign", "Signs");
            var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
            File.WriteAllText(Path.Combine(signsPath, fileName), JsonSerializer.Serialize(def, JsonOptions));
            signCount++;
        }

        if (_verbose)
        {
            Console.WriteLine($"  Extracted {interactionCount} interaction scripts");
            Console.WriteLine($"  Extracted {triggerCount} trigger scripts");
            Console.WriteLine($"  Extracted {signCount} sign scripts");
        }

        return (interactionCount, triggerCount, signCount, interactionCount + triggerCount + signCount);
    }

    private void ScanMapScripts()
    {
        var mapsPath = Path.Combine(_inputPath, "data", "maps");
        if (!Directory.Exists(mapsPath))
            return;

        foreach (var mapDir in Directory.GetDirectories(mapsPath))
        {
            var scriptsFile = Path.Combine(mapDir, "scripts.inc");
            if (File.Exists(scriptsFile))
            {
                ScanScriptFile(scriptsFile);
            }

            // Also check for scripts.pory (poryscript format)
            var poryFile = Path.Combine(mapDir, "scripts.pory");
            if (File.Exists(poryFile))
            {
                ScanPoryScriptFile(poryFile);
            }
        }

        // Scan common scripts
        var commonScriptsPath = Path.Combine(_inputPath, "data", "scripts");
        if (Directory.Exists(commonScriptsPath))
        {
            foreach (var file in Directory.GetFiles(commonScriptsPath, "*.inc", SearchOption.AllDirectories))
            {
                ScanScriptFile(file);
            }
        }
    }

    private void ScanScriptFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var mapName = GetMapNameFromPath(filePath);

        // Find script labels (script definitions start with label::)
        var labelRegex = new Regex(@"^(\w+)::.*$", RegexOptions.Multiline);

        foreach (Match match in labelRegex.Matches(content))
        {
            var scriptName = match.Groups[1].Value;
            CategorizeScript(scriptName, mapName);
        }
    }

    private void ScanPoryScriptFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var mapName = GetMapNameFromPath(filePath);

        // Find script definitions in poryscript format
        var scriptRegex = new Regex(@"script\s+(\w+)\s*\{", RegexOptions.Multiline);

        foreach (Match match in scriptRegex.Matches(content))
        {
            var scriptName = match.Groups[1].Value;
            CategorizeScript(scriptName, mapName);
        }
    }

    private string GetMapNameFromPath(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath) ?? "";
        return Path.GetFileName(dir);
    }

    private void CategorizeScript(string scriptName, string mapName)
    {
        var lower = scriptName.ToLowerInvariant();

        // Skip internal/helper scripts
        if (lower.StartsWith("common_") ||
            lower.StartsWith("std_") ||
            lower.Contains("_movement") ||
            lower.EndsWith("_text") ||
            lower.EndsWith("_msgtext"))
        {
            return;
        }

        // Categorize based on naming patterns
        if (lower.Contains("_sign") || lower.Contains("sign_"))
        {
            _signScripts.Add(scriptName);
        }
        else if (lower.Contains("trigger") || lower.Contains("_trig"))
        {
            _triggerScripts.Add(scriptName);
        }
        else if (lower.Contains("_npc") || lower.Contains("npc_") ||
                 lower.Contains("_man") || lower.Contains("_woman") ||
                 lower.Contains("_boy") || lower.Contains("_girl") ||
                 lower.Contains("_twin") || lower.Contains("_fat") ||
                 lower.Contains("_mom") || lower.Contains("_rival") ||
                 lower.Contains("_birch") || lower.Contains("_script_"))
        {
            _interactionScripts.Add(scriptName);
        }
        else if (lower.Contains("eventscript") || lower.Contains("event_script"))
        {
            // General event scripts - categorize as interactions by default
            _interactionScripts.Add(scriptName);
        }
    }

    private object CreateScriptDefinition(string scriptName, string category, string subfolder)
    {
        // Use IdTransformer.Normalize to match the format used in map definitions
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
        // Remove common suffixes
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

    /// <summary>
    /// Extract scripts referenced in already-converted map definitions.
    /// Call this after map conversion to find all script IDs used.
    /// </summary>
    public void ExtractFromMapDefinitions(string mapsDefinitionPath)
    {
        if (!Directory.Exists(mapsDefinitionPath))
            return;

        foreach (var mapFile in Directory.GetFiles(mapsDefinitionPath, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                var json = File.ReadAllText(mapFile);

                // Find all script ID references
                var interactionRegex = new Regex(@"""interactionId"":\s*""[^:]+:script:interaction/([^""]+)""");
                var triggerRegex = new Regex(@"""triggerId"":\s*""[^:]+:script:trigger/([^""]+)""");

                foreach (Match match in interactionRegex.Matches(json))
                {
                    _interactionScripts.Add(match.Groups[1].Value);
                }

                foreach (Match match in triggerRegex.Matches(json))
                {
                    _triggerScripts.Add(match.Groups[1].Value);
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }
    }
}
