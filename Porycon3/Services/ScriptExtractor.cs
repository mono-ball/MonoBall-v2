using System.Text.Json;
using System.Text.RegularExpressions;
using Porycon3.Services.Extraction;

namespace Porycon3.Services;

/// <summary>
/// Extracts script definitions from pokeemerald-expansion.
/// Scans map event scripts and creates definition files for interactions, triggers, and other scripts.
/// </summary>
public class ScriptExtractor : ExtractorBase
{
    public override string Name => "Script Definitions";
    public override string Description => "Extracts script references from map event data";

    // Categories of scripts we extract
    private readonly HashSet<string> _interactionScripts = new();
    private readonly HashSet<string> _triggerScripts = new();
    private readonly HashSet<string> _signScripts = new();
    private readonly HashSet<string> _npcScripts = new();
    private readonly HashSet<string> _itemScripts = new();
    private readonly HashSet<string> _weatherScripts = new();

    public ScriptExtractor(string inputPath, string outputPath, bool verbose = false)
        : base(inputPath, outputPath, verbose)
    {
    }

    protected override int ExecuteExtraction()
    {
        // Scan all map scripts to find script references
        WithStatus("Scanning map scripts...", _ => ScanMapScripts());

        // Create output directories
        var interactionsPath = Path.Combine(OutputPath, "Definitions", "Scripts", "Interactions");
        var triggersPath = Path.Combine(OutputPath, "Definitions", "Scripts", "Triggers");
        var signsPath = Path.Combine(OutputPath, "Definitions", "Scripts", "Signs");

        EnsureDirectory(interactionsPath);
        EnsureDirectory(triggersPath);
        EnsureDirectory(signsPath);

        int interactionCount = 0;
        int triggerCount = 0;
        int signCount = 0;

        // Generate interaction script definitions
        var interactionList = _interactionScripts.OrderBy(s => s).ToList();
        if (interactionList.Count > 0)
        {
            WithProgress("Extracting interaction scripts", interactionList, (scriptName, task) =>
            {
                SetTaskDescription(task, $"[cyan]Creating[/] [yellow]{scriptName}[/]");

                var def = CreateScriptDefinition(scriptName, "interaction", "Interactions");
                var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
                File.WriteAllText(Path.Combine(interactionsPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                interactionCount++;
            });
        }

        // Generate trigger script definitions
        var triggerList = _triggerScripts.OrderBy(s => s).ToList();
        if (triggerList.Count > 0)
        {
            WithProgress("Extracting trigger scripts", triggerList, (scriptName, task) =>
            {
                SetTaskDescription(task, $"[cyan]Creating[/] [yellow]{scriptName}[/]");

                var def = CreateScriptDefinition(scriptName, "trigger", "Triggers");
                var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
                File.WriteAllText(Path.Combine(triggersPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                triggerCount++;
            });
        }

        // Generate sign script definitions
        var signList = _signScripts.OrderBy(s => s).ToList();
        if (signList.Count > 0)
        {
            WithProgress("Extracting sign scripts", signList, (scriptName, task) =>
            {
                SetTaskDescription(task, $"[cyan]Creating[/] [yellow]{scriptName}[/]");

                var def = CreateScriptDefinition(scriptName, "sign", "Signs");
                var fileName = $"{IdTransformer.Normalize(scriptName)}.json";
                File.WriteAllText(Path.Combine(signsPath, fileName), JsonSerializer.Serialize(def, JsonOptions.Default));
                signCount++;
            });
        }

        SetCount("Interactions", interactionCount);
        SetCount("Triggers", triggerCount);
        SetCount("Signs", signCount);

        return interactionCount + triggerCount + signCount;
    }

    private void ScanMapScripts()
    {
        var mapsPath = Path.Combine(InputPath, "data", "maps");
        if (!Directory.Exists(mapsPath))
        {
            LogWarning($"Maps path not found: {mapsPath}");
            return;
        }

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
        var commonScriptsPath = Path.Combine(InputPath, "data", "scripts");
        if (Directory.Exists(commonScriptsPath))
        {
            foreach (var file in Directory.GetFiles(commonScriptsPath, "*.inc", SearchOption.AllDirectories))
            {
                ScanScriptFile(file);
            }
        }

        LogVerbose($"Found {_interactionScripts.Count} interactions, {_triggerScripts.Count} triggers, {_signScripts.Count} signs");
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
