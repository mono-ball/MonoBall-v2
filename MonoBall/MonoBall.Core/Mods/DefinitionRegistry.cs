using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MonoBall.Core.Mods
{
    /// <summary>
    /// Registry for storing and querying loaded definitions. Read-only after initial load.
    /// </summary>
    public class DefinitionRegistry
    {
        private readonly Dictionary<string, DefinitionMetadata> _definitions =
            new Dictionary<string, DefinitionMetadata>();
        private readonly Dictionary<string, List<string>> _definitionsByType =
            new Dictionary<string, List<string>>();
        private bool _isLocked = false;

        /// <summary>
        /// Gets the total number of registered definitions.
        /// </summary>
        public int Count => _definitions.Count;

        /// <summary>
        /// Locks the registry to prevent further modifications.
        /// </summary>
        public void Lock()
        {
            _isLocked = true;
        }

        /// <summary>
        /// Registers a definition. Throws if the registry is locked.
        /// </summary>
        /// <param name="metadata">The definition metadata to register.</param>
        /// <exception cref="InvalidOperationException">Thrown if the registry is locked.</exception>
        public void Register(DefinitionMetadata metadata)
        {
            if (_isLocked)
            {
                throw new InvalidOperationException(
                    "DefinitionRegistry is locked and cannot be modified."
                );
            }

            if (string.IsNullOrEmpty(metadata.Id))
            {
                throw new ArgumentException(
                    "Definition ID cannot be null or empty.",
                    nameof(metadata)
                );
            }

            _definitions[metadata.Id] = metadata;

            // Index by type
            if (!string.IsNullOrEmpty(metadata.DefinitionType))
            {
                if (!_definitionsByType.ContainsKey(metadata.DefinitionType))
                {
                    _definitionsByType[metadata.DefinitionType] = new List<string>();
                }
                _definitionsByType[metadata.DefinitionType].Add(metadata.Id);
            }
        }

        /// <summary>
        /// Gets a definition by its ID.
        /// </summary>
        /// <param name="id">The definition ID.</param>
        /// <returns>The definition metadata, or null if not found.</returns>
        public DefinitionMetadata? GetById(string id)
        {
            _definitions.TryGetValue(id, out var metadata);
            return metadata;
        }

        /// <summary>
        /// Gets a definition's data as a strongly-typed object.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the definition data to.</typeparam>
        /// <param name="id">The definition ID.</param>
        /// <returns>The deserialized definition, or null if not found.</returns>
        public T? GetById<T>(string id)
            where T : class
        {
            var metadata = GetById(id);
            if (metadata == null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(metadata.Data.GetRawText());
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets all definitions of a specific type.
        /// </summary>
        /// <param name="definitionType">The definition type.</param>
        /// <returns>List of definition IDs of the specified type.</returns>
        public IEnumerable<string> GetByType(string definitionType)
        {
            return _definitionsByType.TryGetValue(definitionType, out var ids)
                ? ids.ToList()
                : Enumerable.Empty<string>();
        }

        /// <summary>
        /// Gets all definitions.
        /// </summary>
        /// <returns>All registered definitions.</returns>
        public IEnumerable<DefinitionMetadata> GetAll()
        {
            return _definitions.Values;
        }

        /// <summary>
        /// Checks if a definition with the given ID exists.
        /// </summary>
        /// <param name="id">The definition ID.</param>
        /// <returns>True if the definition exists, false otherwise.</returns>
        public bool Contains(string id)
        {
            return _definitions.ContainsKey(id);
        }

        /// <summary>
        /// Gets all definition types that have been registered.
        /// </summary>
        /// <returns>Collection of definition type names.</returns>
        public IEnumerable<string> GetDefinitionTypes()
        {
            return _definitionsByType.Keys;
        }
    }
}
