using System;
using System.Collections.Generic;

namespace MonoBall.Core.Mods.Utilities;

/// <summary>
///     Utility class for case-insensitive dictionary operations.
///     Helps with cross-platform compatibility where Windows is case-insensitive but macOS/Linux are case-sensitive.
/// </summary>
public static class CaseInsensitiveDictionaryHelper
{
    /// <summary>
    ///     Builds a case-insensitive lookup dictionary from a case-sensitive dictionary.
    ///     Maps normalized paths (case-insensitive) to their actual paths (case-sensitive).
    /// </summary>
    /// <typeparam name="TValue">The type of values in the source dictionary.</typeparam>
    /// <param name="sourceDictionary">The source dictionary with case-sensitive keys.</param>
    /// <returns>A dictionary that maps case-insensitive keys to case-sensitive keys.</returns>
    /// <exception cref="ArgumentNullException">Thrown when sourceDictionary is null.</exception>
    public static Dictionary<string, string> BuildCaseInsensitiveLookup<TValue>(
        IReadOnlyDictionary<string, TValue> sourceDictionary
    )
    {
        if (sourceDictionary == null)
            throw new ArgumentNullException(nameof(sourceDictionary));

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in sourceDictionary.Keys)
        {
            // Map the key to itself (case-insensitive lookup will find it)
            lookup[key] = key;
        }

        return lookup;
    }

    /// <summary>
    ///     Tries to get a value from a dictionary using case-insensitive key matching.
    ///     First tries exact match (fast path), then falls back to case-insensitive search.
    /// </summary>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <param name="dictionary">The dictionary to search.</param>
    /// <param name="caseInsensitiveLookup">The case-insensitive lookup dictionary (from BuildCaseInsensitiveLookup).</param>
    /// <param name="key">The key to find (with potentially incorrect casing).</param>
    /// <param name="value">When this method returns, contains the value associated with the key, if found; otherwise, the default value.</param>
    /// <returns>True if a value was found; otherwise, false.</returns>
    public static bool TryGetValueCaseInsensitive<TValue>(
        IReadOnlyDictionary<string, TValue> dictionary,
        IReadOnlyDictionary<string, string> caseInsensitiveLookup,
        string key,
        out TValue value
    )
    {
        // Try exact match first (fast path)
        if (dictionary.TryGetValue(key, out value))
            return true;

        // Case-insensitive lookup
        if (caseInsensitiveLookup.TryGetValue(key, out var actualKey) && actualKey != null)
        {
            return dictionary.TryGetValue(actualKey, out value);
        }

        value = default!;
        return false;
    }

    /// <summary>
    ///     Checks if a dictionary contains a key using case-insensitive matching.
    ///     First tries exact match (fast path), then falls back to case-insensitive search.
    /// </summary>
    /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
    /// <param name="dictionary">The dictionary to search.</param>
    /// <param name="caseInsensitiveLookup">The case-insensitive lookup dictionary (from BuildCaseInsensitiveLookup).</param>
    /// <param name="key">The key to find (with potentially incorrect casing).</param>
    /// <returns>True if the key exists; otherwise, false.</returns>
    public static bool ContainsKeyCaseInsensitive<TValue>(
        IReadOnlyDictionary<string, TValue> dictionary,
        IReadOnlyDictionary<string, string> caseInsensitiveLookup,
        string key
    )
    {
        // Try exact match first (fast path)
        if (dictionary.ContainsKey(key))
            return true;

        // Case-insensitive lookup
        return caseInsensitiveLookup.ContainsKey(key);
    }
}
