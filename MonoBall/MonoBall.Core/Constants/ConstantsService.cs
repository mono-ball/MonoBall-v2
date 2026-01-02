using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Utilities;
using Serilog;

namespace MonoBall.Core.Constants;

/// <summary>
///     Service for accessing game constants loaded from mods.
///     Wraps the mod definition registry (like FontService and SpriteLoaderService) and provides
///     fast, type-safe access to constants. Caches deserialized values to avoid allocations in hot paths.
///     This service follows the same pattern as other definition services:
///     - Uses IModManager.GetDefinition&lt;T&gt;() to access definitions
///     - Uses IModManager.GetDefinitionMetadata() for metadata
///     - Caches frequently accessed values
///     - Provides domain-specific access methods
/// </summary>
public class ConstantsService : IConstantsService
{
    private readonly ILogger _logger;
    private readonly IModManager _modManager;
    private readonly Dictionary<string, JsonElement> _rawConstants;
    private readonly Dictionary<string, object> _valueCache;

    /// <summary>
    ///     Initializes a new instance of ConstantsService.
    /// </summary>
    /// <param name="modManager">The mod manager to load constants from. Must not be null.</param>
    /// <param name="logger">The logger instance. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if modManager or logger is null.</exception>
    public ConstantsService(IModManager modManager, ILogger logger)
    {
        _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rawConstants = new Dictionary<string, JsonElement>();
        _valueCache = new Dictionary<string, object>();

        LoadConstantsFromMods(_modManager);
    }

    /// <summary>
    ///     Validates that required constants exist. Call after service creation to fail-fast.
    /// </summary>
    /// <param name="requiredKeys">The keys that must exist.</param>
    /// <exception cref="InvalidOperationException">Thrown if any required constants are missing.</exception>
    public void ValidateRequiredConstants(IEnumerable<string> requiredKeys)
    {
        var missing = requiredKeys.Where(k => !Contains(k)).ToList();
        if (missing.Any())
            throw new InvalidOperationException(
                $"Required constants are missing: {string.Join(", ", missing)}. "
                    + "Ensure they are defined in the core mod."
            );
    }

    /// <summary>
    ///     Validates all constants against their validation rules (if defined).
    ///     Call after service creation to fail-fast on invalid values.
    ///     Validates the final merged constants (after all mod overrides), not individual definitions.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if any constants fail validation.</exception>
    public void ValidateConstants()
    {
        var constantDefinitions = _modManager.Registry.GetByType("Constants");
        var validationRules = new Dictionary<string, ConstantValidationRule>();

        // Collect all validation rules from all definitions (later mods override earlier ones)
        foreach (var defId in constantDefinitions)
        {
            var definition = _modManager.GetDefinition<ConstantDefinition>(defId);
            if (definition?.ValidationRules == null)
                continue;

            // Merge validation rules (later mods override earlier ones, same as constants)
            foreach (var ruleKvp in definition.ValidationRules)
                validationRules[ruleKvp.Key] = ruleKvp.Value;
        }

        // Validate final merged constants against collected validation rules
        var validationErrors = new List<string>();
        foreach (var ruleKvp in validationRules)
        {
            var constantKey = ruleKvp.Key;
            var rule = ruleKvp.Value;

            if (!_rawConstants.TryGetValue(constantKey, out var element))
                continue; // Constant not found, skip (existence is validated separately)

            // Validate numeric constants
            if (element.ValueKind == JsonValueKind.Number)
            {
                var numericValue = element.GetDouble();

                if (rule.Min.HasValue && numericValue < rule.Min.Value)
                    validationErrors.Add(
                        $"Constant '{constantKey}' value {numericValue} is below minimum {rule.Min.Value}. "
                            + $"Value must be >= {rule.Min.Value}. "
                            + (
                                string.IsNullOrEmpty(rule.Description)
                                    ? ""
                                    : $"({rule.Description})"
                            )
                    );

                if (rule.Max.HasValue && numericValue > rule.Max.Value)
                    validationErrors.Add(
                        $"Constant '{constantKey}' value {numericValue} is above maximum {rule.Max.Value}. "
                            + $"Value must be <= {rule.Max.Value}. "
                            + (
                                string.IsNullOrEmpty(rule.Description)
                                    ? ""
                                    : $"({rule.Description})"
                            )
                    );
            }
        }

        if (validationErrors.Any())
            throw new InvalidOperationException(
                "Constant validation failed:\n" + string.Join("\n", validationErrors)
            );
    }

    /// <summary>
    ///     Gets a constant value by key, throwing if not found.
    /// </summary>
    /// <typeparam name="T">The expected type of the constant.</typeparam>
    /// <param name="key">The constant key (e.g., "TileChunkSize").</param>
    /// <returns>The constant value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the constant is not found.</exception>
    /// <exception cref="InvalidCastException">Thrown if the constant cannot be converted to T.</exception>
    public T Get<T>(string key)
        where T : struct
    {
        ValidateKey(key);

        // Check cache first (avoid deserialization)
        if (TryGetCached(key, out T cachedValue))
            return cachedValue;

        if (!_rawConstants.TryGetValue(key, out var element))
            throw new KeyNotFoundException(
                $"Constant '{key}' not found. Ensure it is defined in a mod's Constants definition."
            );

        var value = DeserializeAndValidate<T>(key, element);
        _valueCache[key] = value; // Cache for future access
        return value;
    }

    /// <summary>
    ///     Gets a string constant value, throwing if not found.
    /// </summary>
    /// <param name="key">The constant key.</param>
    /// <returns>The constant value.</returns>
    /// <exception cref="KeyNotFoundException">Thrown if the constant is not found.</exception>
    public string GetString(string key)
    {
        ValidateKey(key);

        // Check cache first
        if (TryGetCached(key, out string? cachedString) && cachedString != null)
            return cachedString;

        if (!_rawConstants.TryGetValue(key, out var element))
            throw new KeyNotFoundException(
                $"Constant '{key}' not found. Ensure it is defined in a mod's Constants definition."
            );

        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidCastException(
                $"Constant '{key}' is not a string. Found: {element.ValueKind}"
            );

        var value =
            element.GetString()
            ?? throw new InvalidOperationException($"Constant '{key}' has null string value.");

        _valueCache[key] = value; // Cache for future access
        return value;
    }

    /// <summary>
    ///     Tries to get a constant value, returning false if not found.
    /// </summary>
    /// <typeparam name="T">The expected type of the constant.</typeparam>
    /// <param name="key">The constant key.</param>
    /// <param name="value">The constant value if found.</param>
    /// <returns>True if the constant was found, false otherwise.</returns>
    public bool TryGet<T>(string key, out T value)
        where T : struct
    {
        value = default;
        if (string.IsNullOrEmpty(key) || !_rawConstants.TryGetValue(key, out var element))
            return false;

        // Check cache first
        if (_valueCache.TryGetValue(key, out var cached) && cached is T typed)
        {
            value = typed;
            return true;
        }

        try
        {
            value = DeserializeAndValidate<T>(key, element);
            _valueCache[key] = value; // Cache for future access
            return true;
        }
        catch (InvalidCastException)
        {
            // Type mismatch is expected, return false
            return false;
        }
        catch (JsonException ex)
        {
            // Log JSON errors but don't fail
            _logger.Warning("Failed to deserialize constant '{Key}': {Error}", key, ex.Message);
            return false;
        }
        // Let other exceptions propagate (fail-fast)
    }

    /// <summary>
    ///     Tries to get a string constant value, returning false if not found.
    /// </summary>
    /// <param name="key">The constant key.</param>
    /// <param name="value">The constant value if found.</param>
    /// <returns>True if the constant was found, false otherwise.</returns>
    public bool TryGetString(string key, out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(key))
            return false;

        if (!_rawConstants.TryGetValue(key, out var element))
            return false;

        // Check cache first
        if (TryGetCached(key, out value))
            return true;

        if (element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString();
        if (value != null)
            _valueCache[key] = value; // Cache for future access
        return value != null;
    }

    /// <summary>
    ///     Checks if a constant exists.
    /// </summary>
    /// <param name="key">The constant key.</param>
    /// <returns>True if the constant exists, false otherwise.</returns>
    public bool Contains(string key)
    {
        return !string.IsNullOrEmpty(key) && _rawConstants.ContainsKey(key);
    }

    private void LoadConstantsFromMods(IModManager modManager)
    {
        var constantDefinitions = modManager.Registry.GetByType("Constants");

        foreach (var defId in constantDefinitions)
        {
            // Use GetDefinitionMetadata() for consistency with other services
            var metadata = modManager.GetDefinitionMetadata(defId);
            if (metadata == null)
            {
                _logger.Warning("Constants definition metadata not found for '{DefId}'", defId);
                continue;
            }

            // Use GetDefinition<T>() for consistency with other services
            var definition = modManager.GetDefinition<ConstantDefinition>(defId);
            if (definition == null)
            {
                _logger.Warning("Failed to load constants definition '{DefId}'", defId);
                continue;
            }

            // Merge constants into flat dictionary (later mods override earlier ones)
            // This flattens the constants dictionary from all definition files
            foreach (var kvp in definition.Constants)
                _rawConstants[kvp.Key] = kvp.Value;
        }

        _logger.Information(
            "Loaded {Count} constants from {DefCount} definition(s)",
            _rawConstants.Count,
            constantDefinitions.Count()
        );
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));
    }

    /// <summary>
    ///     Tries to get a cached value for the specified key.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The constant key.</param>
    /// <param name="value">The cached value if found.</param>
    /// <returns>True if a cached value was found, false otherwise.</returns>
    private bool TryGetCached<T>(string key, out T value)
    {
        if (_valueCache.TryGetValue(key, out var cached) && cached is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    private T DeserializeAndValidate<T>(string key, JsonElement element)
        where T : struct
    {
        // Validate number precision for integer types
        if (typeof(T) == typeof(int) && element.ValueKind == JsonValueKind.Number)
        {
            var dbl = element.GetDouble();
            if (dbl != Math.Floor(dbl))
                throw new InvalidCastException(
                    $"Constant '{key}' must be an integer. Found: {dbl}"
                );
        }

        try
        {
            return JsonSerializer.Deserialize<T>(
                element.GetRawText(),
                JsonSerializerOptionsFactory.Default
            );
        }
        catch (JsonException ex)
        {
            throw new InvalidCastException(
                $"Failed to deserialize constant '{key}' to type {typeof(T).Name}. "
                    + $"Value: {element.GetRawText()}. Error: {ex.Message}",
                ex
            );
        }
    }
}
