using System.Text.Json;
using System.Text.RegularExpressions;

namespace Porycon3.Services;

/// <summary>
/// Extracts Pokemon species data from pokeemerald-expansion.
/// Parses species_info headers, level_up_learnsets, teachable_learnsets, and egg_moves
/// to generate unified Pokemon species JSON definitions.
/// </summary>
public class SpeciesExtractor
{
    private readonly string _inputPath;
    private readonly string _outputPath;
    private readonly bool _verbose;

    private readonly string _speciesInfoPath;
    private readonly string _levelUpLearnsetsPath;
    private readonly string _teachableLearnsetsPath;
    private readonly string _eggMovesPath;
    private readonly string _formChangeTablesPath;
    private readonly string _outputData;
    private readonly string _spriteDefinitionsPath;
    private readonly string _criesPath;

    // Form change data: maps species name to (item, change type)
    private Dictionary<string, (string? Item, string ChangeType)> _formChangeData = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Regex patterns for parsing C header files
    private static readonly Regex SpeciesStartRegex = new(
        @"\[SPECIES_(\w+)\]\s*=\s*\{",
        RegexOptions.Compiled);

    private static readonly Regex PropertyRegex = new(
        @"\.(\w+)\s*=\s*([^,\n]+)",
        RegexOptions.Compiled);

    private static readonly Regex TypesRegex = new(
        @"MON_TYPES\s*\(\s*TYPE_(\w+)\s*(?:,\s*TYPE_(\w+))?\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex EggGroupsRegex = new(
        @"MON_EGG_GROUPS\s*\(\s*EGG_GROUP_(\w+)\s*(?:,\s*EGG_GROUP_(\w+))?\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex AbilitiesRegex = new(
        @"\{\s*ABILITY_(\w+)\s*,\s*ABILITY_(\w+)\s*,\s*ABILITY_(\w+)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex EvolutionRegex = new(
        @"EVOLUTION\s*\(\s*\{([^}]+)\}\s*(?:,\s*\{([^}]+)\})?\s*(?:,\s*\{([^}]+)\})?\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex SingleEvolutionRegex = new(
        @"(EVO_\w+)\s*,\s*(\d+|ITEM_\w+|SPECIES_\w+|MOVE_\w+|ABILITY_\w+|TYPE_\w+)\s*,\s*SPECIES_(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex LevelUpMoveRegex = new(
        @"LEVEL_UP_MOVE\s*\(\s*(\d+)\s*,\s*MOVE_(\w+)\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex TeachableLearnsetRegex = new(
        @"static\s+const\s+u16\s+s(\w+)TeachableLearnset\[\]\s*=\s*\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EggMoveLearnsetRegex = new(
        @"static\s+const\s+u16\s+s(\w+)EggMoveLearnset\[\]\s*=\s*\{([^}]+)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MoveConstantRegex = new(
        @"MOVE_(\w+)",
        RegexOptions.Compiled);

    private static readonly Regex LevelUpLearnsetRegex = new(
        @"static\s+const\s+struct\s+LevelUpMove\s+s(\w+)LevelUpLearnset\[\]\s*=\s*\{([^}]+(?:LEVEL_UP_END[^}]*))\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public SpeciesExtractor(string inputPath, string outputPath, bool verbose = false)
    {
        _inputPath = inputPath;
        _outputPath = outputPath;
        _verbose = verbose;

        _speciesInfoPath = Path.Combine(inputPath, "src", "data", "pokemon", "species_info");
        _levelUpLearnsetsPath = Path.Combine(inputPath, "src", "data", "pokemon", "level_up_learnsets");
        _teachableLearnsetsPath = Path.Combine(inputPath, "src", "data", "pokemon", "teachable_learnsets.h");
        _eggMovesPath = Path.Combine(inputPath, "src", "data", "pokemon", "egg_moves.h");
        _formChangeTablesPath = Path.Combine(inputPath, "src", "data", "pokemon", "form_change_tables.h");
        _outputData = Path.Combine(outputPath, "Definitions", "Entities", "Pokemon");
        _spriteDefinitionsPath = Path.Combine(outputPath, "Definitions", "Assets", "Pokemon");
        _criesPath = Path.Combine(outputPath, "Audio", "SFX", "Cries");
    }

    /// <summary>
    /// Extract all Pokemon species data.
    /// </summary>
    public (int Species, int Forms) ExtractAll()
    {
        if (!Directory.Exists(_speciesInfoPath))
        {
            Console.WriteLine($"[SpeciesExtractor] Species info not found: {_speciesInfoPath}");
            return (0, 0);
        }

        Directory.CreateDirectory(_outputData);

        // Parse all data sources
        var levelUpLearnsets = ParseAllLevelUpLearnsets();
        var teachableLearnsets = ParseTeachableLearnsets();
        var eggMoveLearnsets = ParseEggMoveLearnsets();
        _formChangeData = ParseFormChangeTables();

        if (_verbose)
        {
            Console.WriteLine($"[SpeciesExtractor] Parsed {levelUpLearnsets.Count} level-up learnsets");
            Console.WriteLine($"[SpeciesExtractor] Parsed {teachableLearnsets.Count} teachable learnsets");
            Console.WriteLine($"[SpeciesExtractor] Parsed {eggMoveLearnsets.Count} egg move learnsets");
            Console.WriteLine($"[SpeciesExtractor] Parsed {_formChangeData.Count} form change entries");
        }

        // Collect ALL species data first
        var allSpecies = new List<SpeciesData>();
        var genFiles = Directory.GetFiles(_speciesInfoPath, "gen_*_families.h");

        foreach (var genFile in genFiles)
        {
            var speciesList = CollectSpeciesFromFile(genFile, levelUpLearnsets, teachableLearnsets, eggMoveLearnsets);
            allSpecies.AddRange(speciesList);
        }

        // Separate base species from forms
        var baseSpecies = allSpecies.Where(s => !IsForm(s.OriginalSpeciesName!)).ToList();
        var forms = allSpecies.Where(s => IsForm(s.OriginalSpeciesName!)).ToList();

        if (_verbose)
        {
            Console.WriteLine($"[SpeciesExtractor] Found {baseSpecies.Count} base species, {forms.Count} forms");
        }

        // Group forms with their base species
        foreach (var form in forms)
        {
            var baseSpeciesName = GetBaseSpeciesNameFromOriginal(form.OriginalSpeciesName!);
            var baseSpec = baseSpecies.FirstOrDefault(s =>
                s.OriginalSpeciesName!.Equals(baseSpeciesName, StringComparison.OrdinalIgnoreCase));

            if (baseSpec != null)
            {
                baseSpec.Forms ??= new List<PokemonFormDto>();
                baseSpec.Forms.Add(ConvertToFormDto(form, baseSpeciesName));
            }
            else if (_verbose)
            {
                Console.WriteLine($"[SpeciesExtractor] Warning: No base species found for form {form.OriginalSpeciesName}");
            }
        }

        // Write only base species (with embedded forms)
        foreach (var species in baseSpecies)
        {
            WriteSpeciesJson(species);
        }

        Console.WriteLine($"[SpeciesExtractor] Extracted {baseSpecies.Count} species ({forms.Count} forms)");
        return (baseSpecies.Count, forms.Count);
    }

    private string GetBaseSpeciesNameFromOriginal(string speciesName)
    {
        // VENUSAUR_MEGA -> VENUSAUR
        // CHARIZARD_MEGA_X -> CHARIZARD
        var parts = speciesName.Split('_');
        return parts[0];
    }

    private PokemonFormDto ConvertToFormDto(SpeciesData form, string baseSpeciesName)
    {
        // Extract form key from the original name
        // VENUSAUR_MEGA -> mega
        // CHARIZARD_MEGA_X -> mega-x
        var parts = form.OriginalSpeciesName!.Split('_');
        var formParts = parts.Skip(1).Select(p => p.ToLowerInvariant());
        var formKey = string.Join("-", formParts);

        var baseName = baseSpeciesName.ToLowerInvariant();

        // Determine form type and region
        var (formType, region, transformMethod) = DetermineFormType(form);

        // Get transformation item from form change tables
        string? transformItem = null;
        if (_formChangeData.TryGetValue(form.OriginalSpeciesName!, out var formChangeInfo))
        {
            transformItem = formChangeInfo.Item;
        }

        return new PokemonFormDto
        {
            Id = $"{IdTransformer.Namespace}:pokemon_form:{baseName}/{formKey}",
            Name = form.Name,
            FormKey = formKey,
            BaseStats = form.BaseStats,
            Types = form.Types,
            Abilities = form.Abilities,
            FormType = formType,
            Region = region,
            TransformMethod = transformMethod,
            TransformItem = transformItem
        };
    }

    private (string? formType, string? region, string? transformMethod) DetermineFormType(SpeciesData form)
    {
        // Check for transformation forms
        if (form.IsMegaEvolution == true)
            return ($"{IdTransformer.Namespace}:form_type:mega", null, $"{IdTransformer.Namespace}:transform_method:mega_evolution");
        if (form.IsGigantamax == true)
            return ($"{IdTransformer.Namespace}:form_type:gigantamax", null, $"{IdTransformer.Namespace}:transform_method:gigantamax");
        if (form.IsPrimalReversion == true)
            return ($"{IdTransformer.Namespace}:form_type:primal", null, $"{IdTransformer.Namespace}:transform_method:primal_reversion");
        if (form.IsUltraBurst == true)
            return ($"{IdTransformer.Namespace}:form_type:ultra_burst", null, $"{IdTransformer.Namespace}:transform_method:ultra_burst");
        if (form.IsTeraForm == true)
            return ($"{IdTransformer.Namespace}:form_type:tera", null, $"{IdTransformer.Namespace}:transform_method:terastallization");

        // Check for regional variants
        if (form.IsAlolanForm == true)
            return ($"{IdTransformer.Namespace}:form_type:regional", $"{IdTransformer.Namespace}:region:alola", null);
        if (form.IsGalarianForm == true)
            return ($"{IdTransformer.Namespace}:form_type:regional", $"{IdTransformer.Namespace}:region:galar", null);
        if (form.IsHisuianForm == true)
            return ($"{IdTransformer.Namespace}:form_type:regional", $"{IdTransformer.Namespace}:region:hisui", null);
        if (form.IsPaldeanForm == true)
            return ($"{IdTransformer.Namespace}:form_type:regional", $"{IdTransformer.Namespace}:region:paldea", null);

        // Check for totem forms
        if (form.IsTotem == true)
            return ($"{IdTransformer.Namespace}:form_type:totem", null, null);

        // No special form type detected
        return (null, null, null);
    }

    private List<SpeciesData> CollectSpeciesFromFile(
        string filePath,
        Dictionary<string, List<LevelUpMove>> levelUpLearnsets,
        Dictionary<string, List<string>> teachableLearnsets,
        Dictionary<string, List<string>> eggMoveLearnsets)
    {
        var content = File.ReadAllText(filePath);
        var blocks = ExtractSpeciesBlocks(content);
        var speciesList = new List<SpeciesData>();

        foreach (var (speciesName, blockContent) in blocks)
        {
            if (speciesName == "NONE") continue;

            try
            {
                var species = ParseSpeciesBlock(speciesName, blockContent, levelUpLearnsets, teachableLearnsets, eggMoveLearnsets);
                if (species != null)
                {
                    speciesList.Add(species);
                }
            }
            catch (Exception ex)
            {
                if (_verbose)
                    Console.WriteLine($"[SpeciesExtractor] Error parsing {speciesName}: {ex.Message}");
            }
        }

        return speciesList;
    }

    /// <summary>
    /// Extract species blocks using bracket counting to handle nested structures.
    /// </summary>
    private List<(string SpeciesName, string BlockContent)> ExtractSpeciesBlocks(string content)
    {
        var blocks = new List<(string, string)>();
        var matches = SpeciesStartRegex.Matches(content);

        foreach (Match match in matches)
        {
            var speciesName = match.Groups[1].Value;
            var startIndex = match.Index + match.Length;

            // Count braces to find the matching closing brace
            var depth = 1;
            var endIndex = startIndex;

            while (endIndex < content.Length && depth > 0)
            {
                var c = content[endIndex];
                if (c == '{') depth++;
                else if (c == '}') depth--;
                endIndex++;
            }

            if (depth == 0)
            {
                var blockContent = content.Substring(startIndex, endIndex - startIndex - 1);
                blocks.Add((speciesName, blockContent));
            }
        }

        return blocks;
    }

    private SpeciesData? ParseSpeciesBlock(
        string speciesName,
        string blockContent,
        Dictionary<string, List<LevelUpMove>> levelUpLearnsets,
        Dictionary<string, List<string>> teachableLearnsets,
        Dictionary<string, List<string>> eggMoveLearnsets)
    {
        var normalizedName = NormalizeName(speciesName);
        var learnsetKey = GetLearnsetKey(speciesName);

        var species = new SpeciesData
        {
            Id = $"{IdTransformer.Namespace}:pokemon:{normalizedName}",
            Name = ParseSpeciesName(blockContent) ?? FormatDisplayName(speciesName),
            OriginalSpeciesName = speciesName
        };

        // Parse base stats
        species.BaseStats = new BaseStats
        {
            Hp = ParseIntProperty(blockContent, "baseHP"),
            Attack = ParseIntProperty(blockContent, "baseAttack"),
            Defense = ParseIntProperty(blockContent, "baseDefense"),
            Speed = ParseIntProperty(blockContent, "baseSpeed"),
            SpAttack = ParseIntProperty(blockContent, "baseSpAttack"),
            SpDefense = ParseIntProperty(blockContent, "baseSpDefense")
        };

        // Parse types
        species.Types = ParseTypes(blockContent);

        // Parse catch/exp
        species.CatchRate = ParseIntProperty(blockContent, "catchRate");
        species.ExpYield = ParseIntProperty(blockContent, "expYield");

        // Parse EV yield
        species.EvYield = new EvYield
        {
            Hp = ParseIntProperty(blockContent, "evYield_HP"),
            Attack = ParseIntProperty(blockContent, "evYield_Attack"),
            Defense = ParseIntProperty(blockContent, "evYield_Defense"),
            Speed = ParseIntProperty(blockContent, "evYield_Speed"),
            SpAttack = ParseIntProperty(blockContent, "evYield_SpAttack"),
            SpDefense = ParseIntProperty(blockContent, "evYield_SpDefense")
        };

        // Parse breeding data
        species.GenderRatio = ParseGenderRatio(blockContent);
        species.EggCycles = ParseIntProperty(blockContent, "eggCycles");
        species.EggGroups = ParseEggGroups(blockContent);

        // Parse friendship and growth
        species.Friendship = ParseFriendship(blockContent);
        species.GrowthRate = ParseGrowthRate(blockContent);

        // Parse abilities
        species.Abilities = ParseAbilities(blockContent);

        // Parse visual data
        species.BodyColor = ParseBodyColor(blockContent);
        species.NoFlip = ParseBoolProperty(blockContent, "noFlip");

        // Parse pokedex data (will be moved to separate Pokedex entity later)
        species.NatDexNum = ParseNatDexNum(blockContent);
        species.CategoryId = ParseCategoryName(blockContent);
        species.Height = ParseIntProperty(blockContent, "height");
        species.Weight = ParseIntProperty(blockContent, "weight");
        species.Description = ParseDescription(blockContent);

        // Parse evolutions
        species.Evolutions = ParseEvolutions(blockContent);

        // Parse classification flags (for base species)
        species.IsLegendary = ParseBoolProperty(blockContent, "isLegendary");
        species.IsMythical = ParseBoolProperty(blockContent, "isMythical");
        species.IsUltraBeast = ParseBoolProperty(blockContent, "isUltraBeast");
        species.IsParadox = ParseBoolProperty(blockContent, "isParadox");
        species.IsFrontierBanned = ParseBoolProperty(blockContent, "isFrontierBanned");

        // Parse form-related properties (for forms)
        species.IsMegaEvolution = ParseBoolProperty(blockContent, "isMegaEvolution");
        species.IsGigantamax = ParseBoolProperty(blockContent, "isGigantamax");
        species.IsPrimalReversion = ParseBoolProperty(blockContent, "isPrimalReversion");
        species.IsUltraBurst = ParseBoolProperty(blockContent, "isUltraBurst");
        species.IsTeraForm = ParseBoolProperty(blockContent, "isTeraForm");

        // Parse regional form flags
        species.IsAlolanForm = ParseBoolProperty(blockContent, "isAlolanForm");
        species.IsGalarianForm = ParseBoolProperty(blockContent, "isGalarianForm");
        species.IsHisuianForm = ParseBoolProperty(blockContent, "isHisuianForm");
        species.IsPaldeanForm = ParseBoolProperty(blockContent, "isPaldeanForm");
        species.IsTotem = ParseBoolProperty(blockContent, "isTotem");

        // Add learnsets
        if (levelUpLearnsets.TryGetValue(learnsetKey, out var levelUp))
        {
            species.LevelUpMoves = levelUp.Select(m => new LevelUpMoveDto
            {
                Level = m.Level,
                MoveId = $"{IdTransformer.Namespace}:move:{m.Move.ToLowerInvariant()}"
            }).ToList();
        }

        if (teachableLearnsets.TryGetValue(learnsetKey, out var teachable))
        {
            species.TeachableMoves = teachable.Select(m => $"{IdTransformer.Namespace}:move:{m.ToLowerInvariant()}").ToList();
        }

        if (eggMoveLearnsets.TryGetValue(learnsetKey, out var eggMoves))
        {
            species.EggMoves = eggMoves.Select(m => $"{IdTransformer.Namespace}:move:{m.ToLowerInvariant()}").ToList();
        }

        return species;
    }

    private string NormalizeName(string speciesName)
    {
        // Convert SPECIES_BULBASAUR to bulbasaur
        // Handle forms like VENUSAUR_MEGA -> venusaur_mega
        return speciesName.ToLowerInvariant().Replace("_", "-");
    }

    private string GetLearnsetKey(string speciesName)
    {
        // Convert VENUSAUR_MEGA to Venusaur (learnsets use base species name in PascalCase)
        var baseName = speciesName.Split('_')[0];
        return char.ToUpper(baseName[0]) + baseName.Substring(1).ToLower();
    }

    private bool IsForm(string speciesName)
    {
        // Forms contain underscores after the base name
        // e.g., VENUSAUR_MEGA, CHARIZARD_MEGA_X, PIKACHU_COSPLAY
        return speciesName.Contains('_') &&
               !speciesName.StartsWith("MR_") &&
               !speciesName.StartsWith("MIME_") &&
               !speciesName.StartsWith("TYPE_");
    }

    private string FormatDisplayName(string speciesName)
    {
        // Convert BULBASAUR to Bulbasaur, VENUSAUR_MEGA to Mega Venusaur
        var parts = speciesName.Split('_');
        if (parts.Length == 1)
        {
            return char.ToUpper(parts[0][0]) + parts[0].Substring(1).ToLower();
        }

        // Handle form names
        var baseName = char.ToUpper(parts[0][0]) + parts[0].Substring(1).ToLower();
        var formParts = parts.Skip(1).Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower());
        return string.Join(" ", formParts) + " " + baseName;
    }

    private string? ParseSpeciesName(string content)
    {
        var match = Regex.Match(content, @"\.speciesName\s*=\s*_\(\""([^""]+)\""\)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private int? ParseIntProperty(string content, string propertyName)
    {
        var match = Regex.Match(content, $@"\.{propertyName}\s*=\s*(\d+)");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var value))
            return value;
        return null;
    }

    private bool? ParseBoolProperty(string content, string propertyName)
    {
        var match = Regex.Match(content, $@"\.{propertyName}\s*=\s*(TRUE|FALSE)");
        if (match.Success)
            return match.Groups[1].Value == "TRUE";
        return null;
    }

    private List<string>? ParseTypes(string content)
    {
        var match = TypesRegex.Match(content);
        if (!match.Success) return null;

        var types = new List<string> { $"{IdTransformer.Namespace}:type:{match.Groups[1].Value.ToLowerInvariant()}" };
        if (match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value))
        {
            types.Add($"{IdTransformer.Namespace}:type:{match.Groups[2].Value.ToLowerInvariant()}");
        }
        return types;
    }

    private List<string>? ParseEggGroups(string content)
    {
        var match = EggGroupsRegex.Match(content);
        if (!match.Success) return null;

        var groups = new List<string> { $"{IdTransformer.Namespace}:egg_group:{match.Groups[1].Value.ToLowerInvariant()}" };
        if (match.Groups[2].Success && !string.IsNullOrEmpty(match.Groups[2].Value))
        {
            groups.Add($"{IdTransformer.Namespace}:egg_group:{match.Groups[2].Value.ToLowerInvariant()}");
        }
        return groups;
    }

    private List<string?>? ParseAbilities(string content)
    {
        var match = AbilitiesRegex.Match(content);
        if (!match.Success) return null;

        var abilities = new List<string?>();
        for (int i = 1; i <= 3; i++)
        {
            var ability = match.Groups[i].Value;
            if (ability == "NONE")
                abilities.Add(null);
            else
                abilities.Add($"{IdTransformer.Namespace}:ability:{ability.ToLowerInvariant()}");
        }
        return abilities;
    }

    private int? ParseGenderRatio(string content)
    {
        // Handle PERCENT_FEMALE(12.5) = 31
        var percentMatch = Regex.Match(content, @"\.genderRatio\s*=\s*PERCENT_FEMALE\s*\(\s*([\d.]+)\s*\)");
        if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out var percent))
        {
            return (int)Math.Min(254, (percent * 255) / 100);
        }

        // Handle MON_FEMALE (254)
        if (content.Contains(".genderRatio = MON_FEMALE"))
            return 254;

        // Handle MON_GENDERLESS (255)
        if (content.Contains(".genderRatio = MON_GENDERLESS"))
            return 255;

        // Handle direct numeric value
        return ParseIntProperty(content, "genderRatio");
    }

    private int? ParseFriendship(string content)
    {
        if (content.Contains(".friendship = STANDARD_FRIENDSHIP"))
            return 70;
        return ParseIntProperty(content, "friendship");
    }

    private string? ParseGrowthRate(string content)
    {
        var match = Regex.Match(content, @"\.growthRate\s*=\s*GROWTH_(\w+)");
        if (match.Success)
            return $"{IdTransformer.Namespace}:growth_rate:{match.Groups[1].Value.ToLowerInvariant()}";
        return null;
    }

    private string? ParseBodyColor(string content)
    {
        var match = Regex.Match(content, @"\.bodyColor\s*=\s*BODY_COLOR_(\w+)");
        if (match.Success)
            return $"{IdTransformer.Namespace}:body_color:{match.Groups[1].Value.ToLowerInvariant()}";
        return null;
    }

    private int? ParseNatDexNum(string content)
    {
        var match = Regex.Match(content, @"\.natDexNum\s*=\s*NATIONAL_DEX_(\w+)");
        // The actual number would need a constants lookup, for now just return null
        // and let the caller handle it
        return null;
    }

    private string? ParseCategoryName(string content)
    {
        var match = Regex.Match(content, @"\.categoryName\s*=\s*_\(\""([^""]+)\""\)");
        if (!match.Success) return null;

        // Convert to standard ID format: "Seed" -> "base:category:seed"
        var categoryName = match.Groups[1].Value
            .ToLowerInvariant()
            .Replace(" ", "_")
            .Replace("'", "");
        return $"{IdTransformer.Namespace}:category:{categoryName}";
    }

    private string? ParseDescription(string content)
    {
        var match = Regex.Match(content, @"\.description\s*=\s*COMPOUND_STRING\s*\(\s*([\s\S]*?)\s*\),", RegexOptions.Multiline);
        if (!match.Success) return null;

        // Parse multi-line description strings
        var descContent = match.Groups[1].Value;
        var lines = Regex.Matches(descContent, @"\""([^""]+)\""");
        return string.Join(" ", lines.Cast<Match>().Select(m => m.Groups[1].Value.Replace("\\n", " "))).Trim();
    }

    private List<EvolutionDto>? ParseEvolutions(string content)
    {
        // Find the start of EVOLUTION(
        var evoStart = content.IndexOf(".evolutions = EVOLUTION(", StringComparison.Ordinal);
        if (evoStart < 0) return null;

        // Extract full EVOLUTION(...) content using bracket counting
        var parenStart = content.IndexOf('(', evoStart);
        if (parenStart < 0) return null;

        var depth = 1;
        var endIndex = parenStart + 1;
        while (endIndex < content.Length && depth > 0)
        {
            var c = content[endIndex];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            endIndex++;
        }

        if (depth != 0) return null;

        var evoContent = content.Substring(parenStart + 1, endIndex - parenStart - 2);

        // Remove preprocessor directives and comments
        evoContent = Regex.Replace(evoContent, @"#if\s+\w+", "");
        evoContent = Regex.Replace(evoContent, @"#endif", "");
        evoContent = Regex.Replace(evoContent, @"//.*", "");

        // Find all evolution entries {EVO_xxx, param, SPECIES_xxx}
        var evolutions = new List<EvolutionDto>();
        var evoMatches = Regex.Matches(evoContent, @"\{(EVO_\w+)\s*,\s*([^,]+)\s*,\s*SPECIES_(\w+)");

        foreach (Match match in evoMatches)
        {
            var method = match.Groups[1].Value;
            var param = match.Groups[2].Value.Trim();
            var target = match.Groups[3].Value;

            evolutions.Add(new EvolutionDto
            {
                Method = $"{IdTransformer.Namespace}:evolution_method:{method.Substring(4).ToLowerInvariant()}", // Remove EVO_ prefix
                Parameter = ParseEvolutionParameter(param),
                ParameterName = IsNumeric(param) ? null : param,
                TargetSpeciesId = $"{IdTransformer.Namespace}:pokemon:{target.ToLowerInvariant().Replace("_", "-")}"
            });
        }

        return evolutions.Count > 0 ? evolutions : null;
    }

    private int? ParseEvolutionParameter(string param)
    {
        if (int.TryParse(param, out var value))
            return value;
        return null;
    }

    private bool IsNumeric(string value)
    {
        return int.TryParse(value, out _);
    }

    #region Learnset Parsing

    private Dictionary<string, List<LevelUpMove>> ParseAllLevelUpLearnsets()
    {
        var learnsets = new Dictionary<string, List<LevelUpMove>>();

        if (!Directory.Exists(_levelUpLearnsetsPath))
            return learnsets;

        var files = Directory.GetFiles(_levelUpLearnsetsPath, "*.h");
        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var matches = LevelUpLearnsetRegex.Matches(content);

            foreach (Match match in matches)
            {
                var pokemonName = match.Groups[1].Value;
                var movesContent = match.Groups[2].Value;

                var moves = new List<LevelUpMove>();
                var moveMatches = LevelUpMoveRegex.Matches(movesContent);

                foreach (Match moveMatch in moveMatches)
                {
                    if (int.TryParse(moveMatch.Groups[1].Value, out var level))
                    {
                        moves.Add(new LevelUpMove
                        {
                            Level = level,
                            Move = moveMatch.Groups[2].Value
                        });
                    }
                }

                if (moves.Count > 0)
                    learnsets[pokemonName] = moves;
            }
        }

        return learnsets;
    }

    private Dictionary<string, List<string>> ParseTeachableLearnsets()
    {
        var learnsets = new Dictionary<string, List<string>>();

        if (!File.Exists(_teachableLearnsetsPath))
            return learnsets;

        var content = File.ReadAllText(_teachableLearnsetsPath);
        var matches = TeachableLearnsetRegex.Matches(content);

        foreach (Match match in matches)
        {
            var pokemonName = match.Groups[1].Value;
            var movesContent = match.Groups[2].Value;

            var moves = new List<string>();
            var moveMatches = MoveConstantRegex.Matches(movesContent);

            foreach (Match moveMatch in moveMatches)
            {
                var move = moveMatch.Groups[1].Value;
                if (move != "UNAVAILABLE")
                    moves.Add(move);
            }

            if (moves.Count > 0)
                learnsets[pokemonName] = moves;
        }

        return learnsets;
    }

    private Dictionary<string, List<string>> ParseEggMoveLearnsets()
    {
        var learnsets = new Dictionary<string, List<string>>();

        if (!File.Exists(_eggMovesPath))
            return learnsets;

        var content = File.ReadAllText(_eggMovesPath);
        var matches = EggMoveLearnsetRegex.Matches(content);

        foreach (Match match in matches)
        {
            var pokemonName = match.Groups[1].Value;
            var movesContent = match.Groups[2].Value;

            var moves = new List<string>();
            var moveMatches = MoveConstantRegex.Matches(movesContent);

            foreach (Match moveMatch in moveMatches)
            {
                var move = moveMatch.Groups[1].Value;
                if (move != "UNAVAILABLE")
                    moves.Add(move);
            }

            if (moves.Count > 0)
                learnsets[pokemonName] = moves;
        }

        return learnsets;
    }

    /// <summary>
    /// Parse form_change_tables.h to extract transformation requirements.
    /// Maps species names to their transformation items and change types.
    /// </summary>
    private Dictionary<string, (string? Item, string ChangeType)> ParseFormChangeTables()
    {
        var formChanges = new Dictionary<string, (string? Item, string ChangeType)>();

        if (!File.Exists(_formChangeTablesPath))
            return formChanges;

        var content = File.ReadAllText(_formChangeTablesPath);

        // Regex to match form change entries like:
        // {FORM_CHANGE_BATTLE_MEGA_EVOLUTION_ITEM, SPECIES_VENUSAUR_MEGA, ITEM_VENUSAURITE},
        // {FORM_CHANGE_BATTLE_GIGANTAMAX, SPECIES_VENUSAUR_GMAX},
        var formChangeRegex = new Regex(
            @"\{(FORM_CHANGE_[A-Z_]+),\s*SPECIES_(\w+)(?:,\s*(ITEM_\w+))?\}",
            RegexOptions.Compiled);

        var matches = formChangeRegex.Matches(content);

        foreach (Match match in matches)
        {
            var changeType = match.Groups[1].Value;
            var speciesName = match.Groups[2].Value;
            var item = match.Groups[3].Success ? match.Groups[3].Value : null;

            // Only track transformation forms we care about
            if (changeType.Contains("MEGA_EVOLUTION") ||
                changeType.Contains("GIGANTAMAX") ||
                changeType.Contains("PRIMAL") ||
                changeType.Contains("ULTRA_BURST") ||
                changeType.Contains("TERASTALLIZATION"))
            {
                // Convert item constant to ID: ITEM_VENUSAURITE -> base:item:venusaurite
                string? itemId = null;
                if (!string.IsNullOrEmpty(item))
                {
                    var itemName = item.Replace("ITEM_", "").ToLowerInvariant();
                    itemId = $"{IdTransformer.Namespace}:item:{itemName}";
                }

                formChanges[speciesName] = (itemId, changeType);
            }
        }

        return formChanges;
    }

    #endregion

    private void WriteSpeciesJson(SpeciesData species)
    {
        // All species go in their own directory (forms are embedded in base species JSON)
        var pascalName = ToPascalCase(species.Id!.Split(':').Last());
        var speciesDir = Path.Combine(_outputData, pascalName);
        Directory.CreateDirectory(speciesDir);
        var outputPath = Path.Combine(speciesDir, $"{pascalName}.json");

        // Generate sprite references for base species
        var (sprites, hasGenderDifferences) = GenerateSpriteReferences(pascalName, "");
        species.Sprites = sprites;
        species.HasGenderDifferences = hasGenderDifferences ? true : null;

        // Resolve cry ID for base species
        species.CryId = ResolveCryId(pascalName, null);

        // Generate sprite references and cry IDs for forms
        if (species.Forms != null)
        {
            foreach (var form in species.Forms)
            {
                // Convert form key to PascalCase suffix: "mega-x" -> "MegaX"
                var formSuffix = string.Join("", form.FormKey!.Split('-')
                    .Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));
                var (formSprites, formHasGenderDifferences) = GenerateSpriteReferences(pascalName, formSuffix);
                form.Sprites = formSprites;
                form.HasGenderDifferences = formHasGenderDifferences ? true : null;

                // Resolve cry ID for form (will fallback to base if no form-specific cry)
                form.CryId = ResolveCryId(pascalName, formSuffix);
            }
        }

        var json = JsonSerializer.Serialize(species, JsonOptions);
        File.WriteAllText(outputPath, json);
    }

    /// <summary>
    /// Generate sprite references using sprite definition IDs.
    /// Pattern: base:pokemon:sprite/{species}/{spritename}
    /// Example: base:pokemon:sprite/bulbasaur/bulbasaurfront
    /// </summary>
    private (SpriteReferences Sprites, bool HasGenderDifferences) GenerateSpriteReferences(string baseName, string formSuffix)
    {
        var speciesLower = baseName.ToLowerInvariant();
        var spriteName = $"{baseName}{formSuffix}".ToLowerInvariant();
        var spriteFileName = $"{baseName}{formSuffix}";

        // Check if female sprite definitions exist
        var spriteDir = Path.Combine(_spriteDefinitionsPath, baseName);
        var hasFrontFemale = File.Exists(Path.Combine(spriteDir, $"{spriteFileName}FrontFemale.json"));
        var hasBackFemale = File.Exists(Path.Combine(spriteDir, $"{spriteFileName}BackFemale.json"));
        var hasGenderDifferences = hasFrontFemale || hasBackFemale;

        var sprites = new SpriteReferences
        {
            Front = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}front",
            FrontShiny = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}frontshiny",
            FrontFemale = hasFrontFemale ? $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}frontfemale" : null,
            FrontFemaleShiny = hasFrontFemale ? $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}frontfemaleshiny" : null,
            Back = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}back",
            BackShiny = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}backshiny",
            BackFemale = hasBackFemale ? $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}backfemale" : null,
            BackFemaleShiny = hasBackFemale ? $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}backfemaleshiny" : null,
            Icon = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}icon",
            Overworld = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}overworld",
            OverworldShiny = $"{IdTransformer.Namespace}:pokemon:sprite/{speciesLower}/{spriteName}overworldshiny"
        };

        return (sprites, hasGenderDifferences);
    }

    private string ToPascalCase(string name)
    {
        // Convert kebab-case to PascalCase
        return string.Join("", name.Split('-').Select(p => char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

    /// <summary>
    /// Resolve the cry ID for a Pokemon, checking if the cry file exists.
    /// </summary>
    private string? ResolveCryId(string pascalPokemon, string? formSuffix)
    {
        if (!string.IsNullOrEmpty(formSuffix))
        {
            // Check if form-specific cry exists (e.g., CharizardMegaX.wav)
            var formCryPath = Path.Combine(_criesPath, $"{pascalPokemon}{formSuffix}.wav");
            if (File.Exists(formCryPath))
            {
                var kebabName = ToKebabCase($"{pascalPokemon}{formSuffix}");
                return $"{IdTransformer.Namespace}:audio:sfx/cries/{kebabName}";
            }
        }

        // Check if base Pokemon cry exists
        var baseCryPath = Path.Combine(_criesPath, $"{pascalPokemon}.wav");
        if (File.Exists(baseCryPath))
        {
            var kebabBaseName = ToKebabCase(pascalPokemon);
            return $"{IdTransformer.Namespace}:audio:sfx/cries/{kebabBaseName}";
        }

        return null;
    }

    private static string ToKebabCase(string pascalName)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < pascalName.Length; i++)
        {
            var c = pascalName[i];
            if (i > 0 && char.IsUpper(c))
                result.Append('-');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }

    #region Data Classes

    private class LevelUpMove
    {
        public int Level { get; set; }
        public string Move { get; set; } = "";
    }

    private class SpeciesData
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? CryId { get; set; }
        public BaseStats? BaseStats { get; set; }
        public List<string>? Types { get; set; }
        public int? CatchRate { get; set; }
        public int? ExpYield { get; set; }
        public EvYield? EvYield { get; set; }
        public int? GenderRatio { get; set; }
        public int? EggCycles { get; set; }
        public List<string>? EggGroups { get; set; }
        public int? Friendship { get; set; }
        public string? GrowthRate { get; set; }
        public List<string?>? Abilities { get; set; }
        public string? BodyColor { get; set; }
        public bool? NoFlip { get; set; }
        public int? NatDexNum { get; set; }
        public string? CategoryId { get; set; }
        public int? Height { get; set; }
        public int? Weight { get; set; }
        public string? Description { get; set; }
        public List<EvolutionDto>? Evolutions { get; set; }
        public List<LevelUpMoveDto>? LevelUpMoves { get; set; }
        public List<string>? TeachableMoves { get; set; }
        public List<string>? EggMoves { get; set; }
        public SpriteReferences? Sprites { get; set; }
        public bool? HasGenderDifferences { get; set; }
        public List<PokemonFormDto>? Forms { get; set; }

        // Classification flags (serialized)
        public bool? IsLegendary { get; set; }
        public bool? IsMythical { get; set; }
        public bool? IsUltraBeast { get; set; }
        public bool? IsParadox { get; set; }
        public bool? IsFrontierBanned { get; set; }

        // Internal tracking (not serialized)
        [System.Text.Json.Serialization.JsonIgnore]
        public string? OriginalSpeciesName { get; set; }

        // Form flags (tracked for form conversion, not serialized on base species)
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsMegaEvolution { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsGigantamax { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsPrimalReversion { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsUltraBurst { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsTeraForm { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsAlolanForm { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsGalarianForm { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsHisuianForm { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsPaldeanForm { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public bool? IsTotem { get; set; }
    }

    private class SpriteReferences
    {
        public string? Front { get; set; }
        public string? FrontShiny { get; set; }
        public string? FrontFemale { get; set; }
        public string? FrontFemaleShiny { get; set; }
        public string? Back { get; set; }
        public string? BackShiny { get; set; }
        public string? BackFemale { get; set; }
        public string? BackFemaleShiny { get; set; }
        public string? Icon { get; set; }
        public string? Overworld { get; set; }
        public string? OverworldShiny { get; set; }
    }

    private class PokemonFormDto
    {
        public string? Id { get; set; }  // "base:pokemon_form:venusaur/mega"
        public string? Name { get; set; }  // "Mega Venusaur"
        public string? CryId { get; set; }  // "base:audio:sfx/cries/venusaur-mega"
        public string? FormKey { get; set; }  // "mega"
        public BaseStats? BaseStats { get; set; }
        public List<string>? Types { get; set; }
        public List<string?>? Abilities { get; set; }
        public SpriteReferences? Sprites { get; set; }
        public bool? HasGenderDifferences { get; set; }

        // Form type - indicates what kind of transformation this is
        public string? FormType { get; set; }  // "mega", "gmax", "primal", "ultra-burst", "tera", "regional", "totem", "battle", null for standard

        // Regional variant info
        public string? Region { get; set; }  // "alola", "galar", "hisui", "paldea" for regional forms

        // Transformation requirements (from form_change_tables)
        public string? TransformItem { get; set; }  // e.g., "base:item:venusaurite" for mega evolution
        public string? TransformMethod { get; set; }  // e.g., "mega-evolution", "gigantamax", "primal-reversion"
    }

    private class BaseStats
    {
        public int? Hp { get; set; }
        public int? Attack { get; set; }
        public int? Defense { get; set; }
        public int? Speed { get; set; }
        public int? SpAttack { get; set; }
        public int? SpDefense { get; set; }
    }

    private class EvYield
    {
        public int? Hp { get; set; }
        public int? Attack { get; set; }
        public int? Defense { get; set; }
        public int? Speed { get; set; }
        public int? SpAttack { get; set; }
        public int? SpDefense { get; set; }
    }

    private class EvolutionDto
    {
        public string? Method { get; set; }
        public int? Parameter { get; set; }
        public string? ParameterName { get; set; }
        public string? TargetSpeciesId { get; set; }
    }

    private class LevelUpMoveDto
    {
        public int? Level { get; set; }
        public string? MoveId { get; set; }
    }

    #endregion
}
