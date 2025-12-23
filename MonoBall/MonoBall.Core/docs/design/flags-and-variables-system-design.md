# Flags and Variables System Design

**Version:** 1.1.0  
**Status:** Design Specification (Updated - Architecture Issues Fixed)  
**Date:** 2025-01-XX  
**Author:** MonoBall Design Team  
**Last Updated:** 2025-01-XX (Fixed architecture issues per analysis)

---

## Executive Summary

This document defines the architecture for a unified flags and variables system integrated with Arch ECS. The system supports boolean flags (for game state tracking) and typed variables (for dynamic data storage), with full integration into Arch ECS for persistence, queries, and reactive updates.

### Design Principles

1. **ECS-First**: Flags and variables are stored as components on entities, enabling Arch queries and persistence
2. **Performance**: Flags use bitfield storage for memory efficiency; variables use dictionary for flexibility
3. **Moddability**: String-based identifiers support mod-defined flags/variables without code changes
4. **Reactive**: Event-driven updates allow systems to react to flag/variable changes
5. **Persistence-Ready**: Components designed for seamless Arch.Persistence serialization
6. **Pure Data Components**: Components contain only data (properties), all logic is in the service layer

### Architecture Notes

**Component Design**: Following .cursorrules requirements, components are pure data structures with no methods. All flag/variable operations (GetFlag, SetFlag, GetVariable, SetVariable, etc.) are implemented in `FlagVariableService`, which accesses and modifies component data.

**Event System**: Uses static `EventBus` class (not an interface) with `RefAction<T>` delegate pattern for event handlers with ref parameters.

**Query Caching**: `QueryDescription` is cached as a static readonly field to avoid allocations in hot paths, per .cursorrules requirements.

---

## Architecture Overview

### Component Hierarchy

```
GameStateEntity (Singleton)
├── FlagsComponent (bitfield storage for global boolean flags)
├── VariablesComponent (dictionary storage for global typed variables)
└── FlagVariableMetadataComponent (metadata for flags/variables)

Entity-Specific (Optional)
├── EntityFlagsComponent (per-entity flags)
└── EntityVariablesComponent (per-entity variables)
```

### System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Game State Service                        │
│  (IFlagVariableService - Public API)                        │
│  - SetFlag(flagId, value)                                   │
│  - GetFlag(flagId) -> bool                                   │
│  - SetVariable(key, value)                                  │
│  - GetVariable<T>(key) -> T?                                │
│  - SetFlags(Dictionary<string, bool>)                      │
│  - SetVariables(Dictionary<string, object>)                │
│  - Entity flag/variable operations                         │
│  - Validation support                                       │
└───────────────────────┬─────────────────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
        ▼               ▼               ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│   Flags      │ │  Variables   │ │    Events    │
│  Component   │ │  Component   │ │    System    │
│  (Bitfield)  │ │ (Dictionary)  │ │  (Reactive)  │
└──────────────┘ └──────────────┘ └──────────────┘
        │               │               │
        └───────────────┼───────────────┘
                        │
                        ▼
            ┌───────────────────────┐
            │   Arch.Persistence    │
            │   (Save/Load)         │
            └───────────────────────┘
```

---

## Component Design

### 1. FlagsComponent

**Purpose**: Efficient storage of boolean flags using bitfield compression.

**Location**: `MonoBall.Core.ECS.Components.FlagsComponent`

**Design**:
```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores boolean game flags using bitfield compression.
    /// Flags are identified by string IDs (e.g., "base:flag:visibility/npc_birch").
    /// This component is pure data - all logic is handled by FlagVariableService.
    /// </summary>
    public struct FlagsComponent
    {
        /// <summary>
        /// Bitfield storage for flags. Each bit represents one flag.
        /// Size: 313 bytes = 2504 flags (expandable).
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public byte[] Flags { get; set; }

        /// <summary>
        /// Mapping from flag ID string to bit index.
        /// Populated lazily as flags are accessed.
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public Dictionary<string, int> FlagIndices { get; set; }

        /// <summary>
        /// Reverse mapping from bit index to flag ID.
        /// Used for serialization and debugging.
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public Dictionary<int, string> IndexToFlagId { get; set; }

        /// <summary>
        /// Next available bit index for new flags.
        /// </summary>
        public int NextIndex { get; set; }
    }
}
```

**Note**: Components are pure data structures. All flag operations (GetFlag, SetFlag, etc.) are implemented in `FlagVariableService` which accesses and modifies this component's data.

**Memory Efficiency**:
- **Per flag**: ~1 bit + dictionary entry overhead (~24 bytes for first access)
- **2500 flags**: ~313 bytes bitfield + ~60 KB dictionary = ~60 KB total
- **Comparison**: Much better than Dictionary<bool> approach (~125-200 KB)

**Initialization**:
- Components must be initialized with non-null dictionaries and arrays
- `FlagVariableService` provides `CreateFlagsComponent()` and `CreateVariablesComponent()` factory methods
- Components are initialized when the singleton entity is created

**Serialization Considerations**:
- Bitfield array serializes compactly
- Dictionary mappings must be serialized to restore flag ID → index mapping
- Arch.Persistence will serialize both `Flags` and `FlagIndices` properties
- Components must be initialized with non-null dictionaries before use

---

### 2. VariablesComponent

**Purpose**: Flexible storage of typed variables (string, int, float, bool, etc.).

**Location**: `MonoBall.Core.ECS.Components.VariablesComponent`

**Design**:
```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores typed game variables.
    /// Variables are identified by string keys and can store various types.
    /// This component is pure data - all logic is handled by FlagVariableService.
    /// </summary>
    public struct VariablesComponent
    {
        /// <summary>
        /// Dictionary storing variable values as strings (serialized).
        /// Values are deserialized on access based on requested type.
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public Dictionary<string, string> Variables { get; set; }

        /// <summary>
        /// Type information for variables (for proper deserialization).
        /// Key: variable key, Value: type name
        /// Must be initialized (non-null) when component is created.
        /// </summary>
        public Dictionary<string, string> VariableTypes { get; set; }
    }
}
```

**Note**: Components are pure data structures. All variable operations (GetVariable, SetVariable, etc.) are implemented in `FlagVariableService` which accesses and modifies this component's data. Serialization/deserialization logic is also in the service layer.

**Memory Efficiency**:
- **Per variable**: Dictionary entry overhead (~50-80 bytes) + string storage
- **100 variables**: ~5-8 KB (reasonable for dynamic data)
- **Trade-off**: More memory than flags, but necessary for typed flexibility

**Initialization**:
- Components must be initialized with non-null dictionaries
- `FlagVariableService` provides `CreateVariablesComponent()` factory method
- Components are initialized when the singleton entity is created

**Serialization Considerations**:
- Dictionary serializes naturally with Arch.Persistence
- Type information preserved for proper deserialization
- JSON fallback handles complex types

---

## Service Layer Design

### IFlagVariableService

**Purpose**: Public API for setting/getting flags and variables.

**Location**: `MonoBall.Core.ECS.Services.IFlagVariableService`

**Design**:
```csharp
namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Service for managing game flags and variables.
    /// Provides a clean API for setting, getting, and querying flags/variables.
    /// </summary>
    public interface IFlagVariableService
    {
        #region Flags

        /// <summary>
        /// Gets the value of a flag.
        /// </summary>
        /// <param name="flagId">The flag identifier.</param>
        /// <returns>True if the flag is set, false otherwise.</returns>
        bool GetFlag(string flagId);

        /// <summary>
        /// Sets the value of a flag.
        /// </summary>
        /// <param name="flagId">The flag identifier.</param>
        /// <param name="value">The value to set.</param>
        void SetFlag(string flagId, bool value);

        /// <summary>
        /// Checks if a flag exists (has been set at least once).
        /// </summary>
        /// <param name="flagId">The flag identifier.</param>
        /// <returns>True if the flag exists.</returns>
        bool FlagExists(string flagId);

        /// <summary>
        /// Gets all flag IDs that are currently set to true.
        /// </summary>
        /// <returns>Collection of active flag IDs.</returns>
        IEnumerable<string> GetActiveFlags();

        #endregion

        #region Variables

        /// <summary>
        /// Gets a variable value of the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="key">The variable key.</param>
        /// <returns>The variable value, or default(T) if not found.</returns>
        T? GetVariable<T>(string key);

        /// <summary>
        /// Sets a variable value.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="key">The variable key.</param>
        /// <param name="value">The value to store.</param>
        void SetVariable<T>(string key, T value);

        /// <summary>
        /// Checks if a variable exists.
        /// </summary>
        /// <param name="key">The variable key.</param>
        /// <returns>True if the variable exists.</returns>
        bool VariableExists(string key);

        /// <summary>
        /// Deletes a variable.
        /// </summary>
        /// <param name="key">The variable key.</param>
        void DeleteVariable(string key);

        /// <summary>
        /// Gets all variable keys.
        /// </summary>
        /// <returns>Collection of variable keys.</returns>
        IEnumerable<string> GetVariableKeys();

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Sets multiple flags at once.
        /// </summary>
        /// <param name="flags">Dictionary of flag IDs to values.</param>
        void SetFlags(Dictionary<string, bool> flags);

        /// <summary>
        /// Sets multiple variables at once.
        /// </summary>
        /// <param name="variables">Dictionary of variable keys to values.</param>
        void SetVariables<T>(Dictionary<string, T> variables);

        #endregion

        #region Entity-Specific Operations

        /// <summary>
        /// Gets a flag value for a specific entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="flagId">The flag identifier.</param>
        /// <returns>True if the flag is set, false otherwise.</returns>
        bool GetEntityFlag(Entity entity, string flagId);

        /// <summary>
        /// Sets a flag value for a specific entity.
        /// </summary>
        /// <param name="entity">The entity.</param>
        /// <param name="flagId">The flag identifier.</param>
        /// <param name="value">The value to set.</param>
        void SetEntityFlag(Entity entity, string flagId, bool value);

        /// <summary>
        /// Gets a variable value for a specific entity.
        /// </summary>
        /// <typeparam name="T">The type to deserialize to.</typeparam>
        /// <param name="entity">The entity.</param>
        /// <param name="key">The variable key.</param>
        /// <returns>The variable value, or default(T) if not found.</returns>
        T? GetEntityVariable<T>(Entity entity, string key);

        /// <summary>
        /// Sets a variable value for a specific entity.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="entity">The entity.</param>
        /// <param name="key">The variable key.</param>
        /// <param name="value">The value to store.</param>
        void SetEntityVariable<T>(Entity entity, string key, T value);

        #endregion

        #region Metadata

        /// <summary>
        /// Registers metadata for a flag.
        /// </summary>
        /// <param name="metadata">The flag metadata.</param>
        void RegisterFlagMetadata(FlagMetadata metadata);

        /// <summary>
        /// Registers metadata for a variable.
        /// </summary>
        /// <param name="metadata">The variable metadata.</param>
        void RegisterVariableMetadata(VariableMetadata metadata);

        /// <summary>
        /// Gets metadata for a flag.
        /// </summary>
        /// <param name="flagId">The flag identifier.</param>
        /// <returns>The flag metadata, or null if not found.</returns>
        FlagMetadata? GetFlagMetadata(string flagId);

        /// <summary>
        /// Gets metadata for a variable.
        /// </summary>
        /// <param name="key">The variable key.</param>
        /// <returns>The variable metadata, or null if not found.</returns>
        VariableMetadata? GetVariableMetadata(string key);

        #endregion
    }
}
```

### FlagVariableService Implementation

**Location**: `MonoBall.Core.ECS.Services.FlagVariableService`

**Dependencies**:
- `World` - Arch ECS world instance
- `ILogger` - Logging service
- `IFlagVariableValidator` (optional) - Validation service

**Design**:
```csharp
using System;
using System.Collections.Generic;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Logging;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Implementation of IFlagVariableService using Arch ECS components.
    /// All flag and variable logic is implemented here since components are pure data.
    /// </summary>
    public class FlagVariableService : IFlagVariableService
    {
        private static readonly QueryDescription GameStateQuery = new QueryDescription()
            .WithAll<FlagsComponent, VariablesComponent>();

        private readonly World _world;
        private readonly ILogger _logger;
        private readonly IFlagVariableValidator? _validator;
        private Entity _gameStateEntity = Entity.Null;
        private bool _initialized;

        public FlagVariableService(World world, ILogger logger, IFlagVariableValidator? validator = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _validator = validator;
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            // Find or create singleton game state entity
            var found = false;
            _world.Query(in GameStateQuery, (Entity entity) =>
            {
                _gameStateEntity = entity;
                found = true;
            });

            if (!found)
            {
                _gameStateEntity = _world.Create(
                    CreateFlagsComponent(),
                    CreateVariablesComponent(),
                    CreateMetadataComponent()
                );
                _logger.LogInformation("Created game state singleton entity for flags/variables");
            }

            _initialized = true;
        }

        private static FlagVariableMetadataComponent CreateMetadataComponent()
        {
            return new FlagVariableMetadataComponent
            {
                FlagMetadata = new Dictionary<string, FlagMetadata>(),
                VariableMetadata = new Dictionary<string, VariableMetadata>()
            };
        }

        private static FlagsComponent CreateFlagsComponent()
        {
            const int initialCapacity = 313; // 2504 flags
            return new FlagsComponent
            {
                Flags = new byte[initialCapacity],
                FlagIndices = new Dictionary<string, int>(),
                IndexToFlagId = new Dictionary<int, string>(),
                NextIndex = 0
            };
        }

        private static VariablesComponent CreateVariablesComponent()
        {
            return new VariablesComponent
            {
                Variables = new Dictionary<string, string>(),
                VariableTypes = new Dictionary<string, string>()
            };
        }

        public bool GetFlag(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                return false;

            EnsureInitialized();
            ref FlagsComponent flags = ref _world.Get<FlagsComponent>(_gameStateEntity);
            
            if (flags.FlagIndices == null || !flags.FlagIndices.TryGetValue(flagId, out int index))
                return false; // Flag never set

            if (flags.Flags == null)
                return false;

            int byteIndex = index / 8;
            int bitIndex = index % 8;

            if (byteIndex >= flags.Flags.Length)
                return false;

            return (flags.Flags[byteIndex] & (1 << bitIndex)) != 0;
        }

        public void SetFlag(string flagId, bool value)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                throw new ArgumentException("Flag ID cannot be null or empty.", nameof(flagId));

            // Validate flag ID if validator is available
            if (_validator != null && !_validator.IsValidFlagId(flagId))
            {
                string error = _validator.GetFlagIdValidationError(flagId);
                throw new ArgumentException(error, nameof(flagId));
            }

            EnsureInitialized();
            ref FlagsComponent flags = ref _world.Get<FlagsComponent>(_gameStateEntity);

            // Ensure dictionaries are initialized
            flags.FlagIndices ??= new Dictionary<string, int>();
            flags.IndexToFlagId ??= new Dictionary<int, string>();
            flags.Flags ??= new byte[313];

            bool oldValue = GetFlag(flagId);

            // Get or allocate index for this flag
            if (!flags.FlagIndices.TryGetValue(flagId, out int index))
            {
                index = flags.NextIndex++;
                flags.FlagIndices[flagId] = index;
                flags.IndexToFlagId[index] = flagId;

                // Expand bitfield if needed
                int requiredBytes = (index / 8) + 1;
                if (requiredBytes > flags.Flags.Length)
                {
                    int newSize = Math.Max(flags.Flags.Length * 2, requiredBytes);
                    Array.Resize(ref flags.Flags, newSize);
                }
            }

            // Set the bit
            int byteIndex = index / 8;
            int bitIndex = index % 8;

            if (value)
                flags.Flags[byteIndex] |= (byte)(1 << bitIndex);
            else
                flags.Flags[byteIndex] &= (byte)~(1 << bitIndex);

            // Fire event if value changed
            if (oldValue != value)
            {
                var flagChangedEvent = new FlagChangedEvent
                {
                    FlagId = flagId,
                    OldValue = oldValue,
                    NewValue = value
                };
                EventBus.Send(ref flagChangedEvent);
                _logger.LogDebug("Flag {FlagId} changed from {OldValue} to {NewValue}", flagId, oldValue, value);
            }
        }

        public bool FlagExists(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                return false;

            EnsureInitialized();
            ref FlagsComponent flags = ref _world.Get<FlagsComponent>(_gameStateEntity);
            return flags.FlagIndices != null && flags.FlagIndices.ContainsKey(flagId);
        }

        public IEnumerable<string> GetActiveFlags()
        {
            EnsureInitialized();
            ref FlagsComponent flags = ref _world.Get<FlagsComponent>(_gameStateEntity);
            
            if (flags.FlagIndices == null || flags.Flags == null)
                yield break;

            foreach (var kvp in flags.FlagIndices)
            {
                int index = kvp.Value;
                int byteIndex = index / 8;
                int bitIndex = index % 8;

                if (byteIndex < flags.Flags.Length && (flags.Flags[byteIndex] & (1 << bitIndex)) != 0)
                {
                    yield return kvp.Key;
                }
            }
        }

        public T? GetVariable<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return default;

            EnsureInitialized();
            ref VariablesComponent variables = ref _world.Get<VariablesComponent>(_gameStateEntity);
            
            if (variables.Variables == null || !variables.Variables.TryGetValue(key, out string? serializedValue))
                return default;

            return DeserializeValue<T>(serializedValue);
        }

        public void SetVariable<T>(string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Variable key cannot be null or empty.", nameof(key));

            // Validate variable key if validator is available
            if (_validator != null && !_validator.IsValidVariableKey(key))
            {
                string error = _validator.GetVariableKeyValidationError(key);
                throw new ArgumentException(error, nameof(key));
            }

            EnsureInitialized();
            ref VariablesComponent variables = ref _world.Get<VariablesComponent>(_gameStateEntity);

            // Ensure dictionaries are initialized
            variables.Variables ??= new Dictionary<string, string>();
            variables.VariableTypes ??= new Dictionary<string, string>();

            T? oldValue = GetVariable<T>(key);
            string serializedValue = SerializeValue(value);
            variables.Variables[key] = serializedValue;
            variables.VariableTypes[key] = typeof(T).FullName ?? typeof(T).Name;

            // Fire event if value changed
            if (!Equals(oldValue, value))
            {
                var variableChangedEvent = new VariableChangedEvent
                {
                    Key = key,
                    OldValue = oldValue?.ToString() ?? string.Empty,
                    NewValue = value?.ToString() ?? string.Empty
                };
                EventBus.Send(ref variableChangedEvent);
                _logger.LogDebug("Variable {Key} changed from {OldValue} to {NewValue}", key, oldValue, value);
            }
        }

        public bool VariableExists(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            EnsureInitialized();
            ref VariablesComponent variables = ref _world.Get<VariablesComponent>(_gameStateEntity);
            return variables.Variables != null && variables.Variables.ContainsKey(key);
        }

        public void DeleteVariable(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            EnsureInitialized();
            ref VariablesComponent variables = ref _world.Get<VariablesComponent>(_gameStateEntity);
            
            if (variables.Variables != null)
                variables.Variables.Remove(key);
            
            if (variables.VariableTypes != null)
                variables.VariableTypes.Remove(key);
        }

        public IEnumerable<string> GetVariableKeys()
        {
            EnsureInitialized();
            ref VariablesComponent variables = ref _world.Get<VariablesComponent>(_gameStateEntity);
            
            if (variables.Variables == null)
                yield break;

            foreach (var key in variables.Variables.Keys)
            {
                yield return key;
            }
        }

        private static string SerializeValue<T>(T value)
        {
            if (value == null)
                return string.Empty;

            return value switch
            {
                string str => str,
                int i => i.ToString(),
                float f => f.ToString("G9"), // Preserve precision
                bool b => b.ToString(),
                _ => System.Text.Json.JsonSerializer.Serialize(value)
            };
        }

        private static T? DeserializeValue<T>(string serializedValue)
        {
            if (string.IsNullOrEmpty(serializedValue))
                return default;

            Type targetType = typeof(T);

            // Handle common types with direct conversion
            if (targetType == typeof(string))
                return (T)(object)serializedValue;

            if (targetType == typeof(int) && int.TryParse(serializedValue, out int intValue))
                return (T)(object)intValue;

            if (targetType == typeof(float) && float.TryParse(serializedValue, out float floatValue))
                return (T)(object)floatValue;

            if (targetType == typeof(bool) && bool.TryParse(serializedValue, out bool boolValue))
                return (T)(object)boolValue;

            // Fallback to JSON deserialization
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(serializedValue);
            }
            catch
            {
                return default;
            }
        }

        public void SetFlags(Dictionary<string, bool> flags)
        {
            if (flags == null)
                throw new ArgumentNullException(nameof(flags));

            foreach (var kvp in flags)
            {
                SetFlag(kvp.Key, kvp.Value);
            }
        }

        public void SetVariables<T>(Dictionary<string, T> variables)
        {
            if (variables == null)
                throw new ArgumentNullException(nameof(variables));

            foreach (var kvp in variables)
            {
                SetVariable(kvp.Key, kvp.Value);
            }
        }

        public bool GetEntityFlag(Entity entity, string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                return false;

            if (!_world.Has<EntityFlagsComponent>(entity))
                return false;

            ref EntityFlagsComponent flags = ref _world.Get<EntityFlagsComponent>(entity);
            
            if (flags.FlagIndices == null || !flags.FlagIndices.TryGetValue(flagId, out int index))
                return false;

            if (flags.Flags == null)
                return false;

            int byteIndex = index / 8;
            int bitIndex = index % 8;

            if (byteIndex >= flags.Flags.Length)
                return false;

            return (flags.Flags[byteIndex] & (1 << bitIndex)) != 0;
        }

        public void SetEntityFlag(Entity entity, string flagId, bool value)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                throw new ArgumentException("Flag ID cannot be null or empty.", nameof(flagId));

            if (_validator != null && !_validator.IsValidFlagId(flagId))
            {
                string error = _validator.GetFlagIdValidationError(flagId);
                throw new ArgumentException(error, nameof(flagId));
            }

            if (!_world.Has<EntityFlagsComponent>(entity))
            {
                _world.Add(entity, CreateEntityFlagsComponent());
            }

            ref EntityFlagsComponent flags = ref _world.Get<EntityFlagsComponent>(entity);
            flags.FlagIndices ??= new Dictionary<string, int>();
            flags.IndexToFlagId ??= new Dictionary<int, string>();
            flags.Flags ??= new byte[313];

            bool oldValue = GetEntityFlag(entity, flagId);

            if (!flags.FlagIndices.TryGetValue(flagId, out int index))
            {
                index = flags.NextIndex++;
                flags.FlagIndices[flagId] = index;
                flags.IndexToFlagId[index] = flagId;

                int requiredBytes = (index / 8) + 1;
                if (requiredBytes > flags.Flags.Length)
                {
                    int newSize = Math.Max(flags.Flags.Length * 2, requiredBytes);
                    Array.Resize(ref flags.Flags, newSize);
                }
            }

            int byteIndex = index / 8;
            int bitIndex = index % 8;

            if (value)
                flags.Flags[byteIndex] |= (byte)(1 << bitIndex);
            else
                flags.Flags[byteIndex] &= (byte)~(1 << bitIndex);

            if (oldValue != value)
            {
                var flagChangedEvent = new FlagChangedEvent
                {
                    FlagId = flagId,
                    OldValue = oldValue,
                    NewValue = value
                };
                EventBus.Send(ref flagChangedEvent);
            }
        }

        public T? GetEntityVariable<T>(Entity entity, string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return default;

            if (!_world.Has<EntityVariablesComponent>(entity))
                return default;

            ref EntityVariablesComponent variables = ref _world.Get<EntityVariablesComponent>(entity);
            
            if (variables.Variables == null || !variables.Variables.TryGetValue(key, out string? serializedValue))
                return default;

            return DeserializeValue<T>(serializedValue);
        }

        public void SetEntityVariable<T>(Entity entity, string key, T value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Variable key cannot be null or empty.", nameof(key));

            if (_validator != null && !_validator.IsValidVariableKey(key))
            {
                string error = _validator.GetVariableKeyValidationError(key);
                throw new ArgumentException(error, nameof(key));
            }

            if (!_world.Has<EntityVariablesComponent>(entity))
            {
                _world.Add(entity, CreateEntityVariablesComponent());
            }

            ref EntityVariablesComponent variables = ref _world.Get<EntityVariablesComponent>(entity);
            variables.Variables ??= new Dictionary<string, string>();
            variables.VariableTypes ??= new Dictionary<string, string>();

            T? oldValue = GetEntityVariable<T>(entity, key);
            string serializedValue = SerializeValue(value);
            variables.Variables[key] = serializedValue;
            variables.VariableTypes[key] = typeof(T).FullName ?? typeof(T).Name;

            if (!Equals(oldValue, value))
            {
                var variableChangedEvent = new VariableChangedEvent
                {
                    Key = key,
                    OldValue = oldValue?.ToString() ?? string.Empty,
                    NewValue = value?.ToString() ?? string.Empty
                };
                EventBus.Send(ref variableChangedEvent);
            }
        }

        private static EntityFlagsComponent CreateEntityFlagsComponent()
        {
            return new EntityFlagsComponent
            {
                Flags = new byte[313],
                FlagIndices = new Dictionary<string, int>(),
                IndexToFlagId = new Dictionary<int, string>(),
                NextIndex = 0
            };
        }

        private static EntityVariablesComponent CreateEntityVariablesComponent()
        {
            return new EntityVariablesComponent
            {
                Variables = new Dictionary<string, string>(),
                VariableTypes = new Dictionary<string, string>()
            };
        }

        public void RegisterFlagMetadata(FlagMetadata metadata)
        {
            EnsureInitialized();
            ref FlagVariableMetadataComponent metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
            metadataComponent.FlagMetadata ??= new Dictionary<string, FlagMetadata>();
            metadataComponent.FlagMetadata[metadata.FlagId] = metadata;
        }

        public void RegisterVariableMetadata(VariableMetadata metadata)
        {
            EnsureInitialized();
            ref FlagVariableMetadataComponent metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
            metadataComponent.VariableMetadata ??= new Dictionary<string, VariableMetadata>();
            metadataComponent.VariableMetadata[metadata.Key] = metadata;
        }

        public FlagMetadata? GetFlagMetadata(string flagId)
        {
            if (string.IsNullOrWhiteSpace(flagId))
                return null;

            EnsureInitialized();
            ref FlagVariableMetadataComponent metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
            
            if (metadataComponent.FlagMetadata == null || !metadataComponent.FlagMetadata.TryGetValue(flagId, out FlagMetadata metadata))
                return null;

            return metadata;
        }

        public VariableMetadata? GetVariableMetadata(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return null;

            EnsureInitialized();
            ref FlagVariableMetadataComponent metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
            
            if (metadataComponent.VariableMetadata == null || !metadataComponent.VariableMetadata.TryGetValue(key, out VariableMetadata metadata))
                return null;

            return metadata;
        }
    }
}
```

---

## Event System Integration

### FlagChangedEvent

**Location**: `MonoBall.Core.ECS.Events.FlagChangedEvent`

**Design**:
```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a flag value changes.
    /// </summary>
    public struct FlagChangedEvent
    {
        /// <summary>
        /// The flag identifier that changed.
        /// </summary>
        public string FlagId { get; set; }

        /// <summary>
        /// The previous value of the flag.
        /// </summary>
        public bool OldValue { get; set; }

        /// <summary>
        /// The new value of the flag.
        /// </summary>
        public bool NewValue { get; set; }
    }
}
```

### VariableChangedEvent

**Location**: `MonoBall.Core.ECS.Events.VariableChangedEvent`

**Design**:
```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a variable value changes.
    /// </summary>
    public struct VariableChangedEvent
    {
        /// <summary>
        /// The variable key that changed.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// The previous value as a string representation.
        /// </summary>
        public string OldValue { get; set; }

        /// <summary>
        /// The new value as a string representation.
        /// </summary>
        public string NewValue { get; set; }
    }
}
```

---

## Reactive System Example

### VisibilityFlagSystem

**Purpose**: Automatically updates entity visibility based on flag changes.

**Location**: `MonoBall.Core.ECS.Systems.VisibilityFlagSystem`

**Design**:
```csharp
namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that manages entity visibility based on flags.
    /// Subscribes to FlagChangedEvent and updates entities with VisibilityFlag references.
    /// </summary>
    public class VisibilityFlagSystem : BaseSystem<World, float>, IDisposable
    {
        private readonly IFlagVariableService _flagVariableService;
        private readonly QueryDescription _queryDescription;
        private bool _disposed = false;

        public VisibilityFlagSystem(World world, IFlagVariableService flagVariableService) : base(world)
        {
            _flagVariableService = flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
            _queryDescription = new QueryDescription()
                .WithAll<NpcComponent>();

            // Subscribe to flag changes using RefAction delegate
            EventBus.Subscribe<FlagChangedEvent>(OnFlagChanged);
        }

        public override void Update(in float deltaTime)
        {
            // Check all NPCs with visibility flags on each update
            World.Query(in _queryDescription, (ref NpcComponent npc) =>
            {
                if (string.IsNullOrWhiteSpace(npc.VisibilityFlag))
                    return;

                bool flagValue = _flagVariableService.GetFlag(npc.VisibilityFlag);
                // Update RenderableComponent visibility based on flag
                // (Implementation depends on how visibility is managed)
            });
        }

        /// <summary>
        /// Event handler for flag changes. Uses RefAction signature to match EventBus pattern.
        /// </summary>
        private void OnFlagChanged(ref FlagChangedEvent evt)
        {
            // Reactively update entities when their visibility flag changes
            World.Query(in _queryDescription, (Entity entity, ref NpcComponent npc) =>
            {
                if (npc.VisibilityFlag == evt.FlagId)
                {
                    // Update visibility immediately
                    // (Implementation depends on visibility system)
                }
            });
        }

        public new void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                EventBus.Unsubscribe<FlagChangedEvent>(OnFlagChanged);
            }
            _disposed = true;
        }
    }
}
```

---

## Arch Persistence Integration

### Component Registration

Components must be registered with Arch.Persistence for serialization:

```csharp
// During world initialization
world.RegisterComponent<FlagsComponent>();
world.RegisterComponent<VariablesComponent>();
world.RegisterComponent<EntityFlagsComponent>();
world.RegisterComponent<EntityVariablesComponent>();
world.RegisterComponent<FlagVariableMetadataComponent>();

// Optional: Custom serializers for optimization
// (Default serialization should work, but custom can reduce save file size)
```

### Serialization Considerations

**FlagsComponent**:
- `Flags` byte array: Serializes compactly
- `FlagIndices` Dictionary: Must be serialized to restore flag ID → index mapping
- `IndexToFlagId` Dictionary: Can be reconstructed from `FlagIndices` reverse lookup
- **Save file size**: ~313 bytes + dictionary overhead (~60 KB for 2500 flags)
- **Note**: Verify Arch.Persistence serialization support for Dictionary fields

**VariablesComponent**:
- `Variables` Dictionary: Serializes naturally
- `VariableTypes` Dictionary: Preserves type information
- **Save file size**: Depends on variable count and values (~5-10 KB typical)
- **Note**: Verify Arch.Persistence serialization support for Dictionary fields

### Save/Load Pattern

```csharp
// Save
var persistence = new WorldPersistence(world);
persistence.Store("savegame.dat");

// Load
var persistence = new WorldPersistence(world);
persistence.Load("savegame.dat");
// GameStateEntity and all components restored automatically
```

---

## Flag ID Naming Convention

### Format

```
{modId}:flag:{category}/{name}
```

### Examples

- `base:flag:visibility/npc_birch` - Controls NPC visibility
- `base:flag:item/potion_route_102` - Item collection flag
- `base:flag:quest/rival_battle_complete` - Quest progression flag
- `pokemon-emerald:flag:event/team_aqua_defeated` - Mod-specific flag

### Categories

- `visibility` - Entity visibility flags
- `item` - Item collection flags
- `quest` - Quest progression flags
- `event` - Story event flags
- `map` - Map-specific flags
- `battle` - Battle-related flags

---

## Variable Key Naming Convention

### Format

```
{modId}:var:{category}/{name}
```

### Examples

- `base:var:player/rival_name` - Player's rival name
- `base:var:player/starter_pokemon` - Starter Pokemon choice
- `base:var:quest/current_quest_id` - Active quest ID
- `pokemon-emerald:var:team/team_aqua_level` - Mod-specific variable

### Categories

- `player` - Player-related variables
- `quest` - Quest state variables
- `map` - Map-specific variables
- `battle` - Battle state variables
- `ui` - UI state variables

---

## Usage Examples

### Setting Flags

```csharp
// In a script or system
_flagVariableService.SetFlag("base:flag:visibility/npc_birch", true);
_flagVariableService.SetFlag("base:flag:item/potion_route_102", true);
_flagVariableService.SetFlag("base:flag:quest/rival_battle_complete", false);
```

### Checking Flags

```csharp
// Check if flag is set
if (_flagVariableService.GetFlag("base:flag:visibility/npc_birch"))
{
    // NPC should be visible
}

// Check if flag exists (has been set at least once)
if (_flagVariableService.FlagExists("base:flag:item/potion_route_102"))
{
    // Flag has been set (could be true or false)
}
```

### Setting Variables

```csharp
// String variable
_flagVariableService.SetVariable<string>("base:var:player/rival_name", "BLUE");

// Integer variable
_flagVariableService.SetVariable<int>("base:var:quest/current_quest_id", 5);

// Float variable
_flagVariableService.SetVariable<float>("base:var:battle/encounter_rate_multiplier", 1.5f);

// Boolean variable
_flagVariableService.SetVariable<bool>("base:var:ui/dialogue_skip_enabled", true);
```

### Getting Variables

```csharp
// Get typed variable
string? rivalName = _flagVariableService.GetVariable<string>("base:var:player/rival_name");
int questId = _flagVariableService.GetVariable<int>("base:var:quest/current_quest_id") ?? 0;
float multiplier = _flagVariableService.GetVariable<float>("base:var:battle/encounter_rate_multiplier") ?? 1.0f;
```

### Reacting to Changes

```csharp
// In a system constructor
EventBus.Subscribe<FlagChangedEvent>(OnFlagChanged);  // RefAction<T> signature inferred

// Event handler uses RefAction signature (ref parameter)
private void OnFlagChanged(ref FlagChangedEvent evt)
{
    if (evt.FlagId == "base:flag:visibility/npc_birch")
    {
        // Update NPC visibility
        UpdateNpcVisibility(evt.NewValue);
    }
}

// In Dispose method
protected virtual void Dispose(bool disposing)
{
    if (!_disposed && disposing)
    {
        EventBus.Unsubscribe<FlagChangedEvent>(OnFlagChanged);  // Must match subscription
    }
    _disposed = true;
}
```

---

## Integration with Existing Systems

### MapLoaderSystem Integration

When loading NPCs from map definitions, check visibility flags:

```csharp
// In MapLoaderSystem when creating NPC entities
if (!string.IsNullOrWhiteSpace(npcDef.VisibilityFlag))
{
    bool isVisible = _flagVariableService.GetFlag(npcDef.VisibilityFlag);
    if (!isVisible)
    {
        // Don't add RenderableComponent, or add with IsVisible = false
    }
}
```

### Direct Service Usage

Systems and other code can directly use `IFlagVariableService`:

```csharp
// In any system or service that needs flags/variables
public class MySystem : BaseSystem<World, float>
{
    private readonly IFlagVariableService _flagVariableService;

    public MySystem(World world, IFlagVariableService flagVariableService) : base(world)
    {
        _flagVariableService = flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
    }

    public override void Update(in float deltaTime)
    {
        // Use service directly
        bool flagValue = _flagVariableService.GetFlag("base:flag:visibility/npc_birch");
        string? variableValue = _flagVariableService.GetVariable<string>("base:var:player/rival_name");
    }
}
```

---

## Performance Considerations

### FlagsComponent Performance

- **GetFlag**: O(1) dictionary lookup + bit operation (~10-20 ns)
- **SetFlag**: O(1) dictionary lookup + bit operation (~10-20 ns)
- **Memory**: ~60 KB for 2500 flags (acceptable)

### VariablesComponent Performance

- **GetVariable**: O(1) dictionary lookup + deserialization (~50-100 ns)
- **SetVariable**: O(1) dictionary lookup + serialization (~50-100 ns)
- **Memory**: ~5-10 KB for 100 variables (acceptable)

### Query Performance

- **Singleton entity lookup**: O(1) after initialization
- **Event subscription**: O(n) handlers, but events are rare
- **Reactive updates**: Only triggered on changes, not every frame

---

## Additional Features

### Per-Entity Flags/Variables

The system supports flags and variables on individual entities in addition to the global singleton. This allows entities to have their own state (e.g., "entity_is_on_fire", "entity_health", "entity_custom_data").

**Usage Example**:
```csharp
// Set a flag on a specific entity
_flagVariableService.SetEntityFlag(npcEntity, "entity_is_talking", true);

// Get a variable from a specific entity
int health = _flagVariableService.GetEntityVariable<int>(npcEntity, "health") ?? 100;
```

### Validation System

The system includes optional validation support via `IFlagVariableValidator`. When provided, the service validates all flag IDs and variable keys before setting them.

**Usage Example**:
```csharp
// Create validator
public class FlagVariableValidator : IFlagVariableValidator
{
    public bool IsValidFlagId(string flagId)
    {
        // Validate format: {modId}:flag:{category}/{name}
        return flagId.Contains(':') && flagId.StartsWith("base:flag:") || flagId.StartsWith("mod:");
    }
    
    // ... implement other methods
}

// Register validator with service
var service = new FlagVariableService(world, logger, new FlagVariableValidator());
```

### Metadata System

Flags and variables can have associated metadata (description, category, type information) stored in `FlagVariableMetadataComponent`. This is useful for tooling, documentation, and debugging.

**Usage Example**:
```csharp
// Register metadata
_flagVariableService.RegisterFlagMetadata(new FlagMetadata
{
    FlagId = "base:flag:visibility/npc_birch",
    Description = "Controls visibility of Professor Birch NPC",
    Category = "visibility",
    IsModDefined = false
});

// Retrieve metadata
var metadata = _flagVariableService.GetFlagMetadata("base:flag:visibility/npc_birch");
```

### Bulk Operations

The service supports bulk operations for setting multiple flags or variables at once, which is more efficient than individual calls.

**Usage Example**:
```csharp
// Set multiple flags at once
_flagVariableService.SetFlags(new Dictionary<string, bool>
{
    { "base:flag:visibility/npc_birch", true },
    { "base:flag:item/potion_route_102", true },
    { "base:flag:quest/rival_battle_complete", false }
});

// Set multiple variables at once
_flagVariableService.SetVariables<string>(new Dictionary<string, string>
{
    { "base:var:player/rival_name", "BLUE" },
    { "base:var:player/starter_pokemon", "charmander" }
});
```

---

## Migration Path

### From Current System

1. **NpcComponent.VisibilityFlag**: Update systems to use `FlagVariableService` to check flag values
2. **Old flag system**: Remove old implementation, migrate all flags to `FlagsComponent` on singleton entity
3. **Update all call sites**: Replace any direct flag/variable access with `IFlagVariableService` calls

### Implementation Notes

- `VisibilityFlag` property in `NpcComponent` remains as a string reference (flag ID)
- Systems use `FlagVariableService.GetFlag(npc.VisibilityFlag)` to check flag values
- All map definitions continue to use string flag IDs (no breaking changes to map format)
- Old flag/variable systems should be completely removed, not maintained alongside new system

---

## Testing Strategy

### Unit Tests

- Component serialization/deserialization
- Flag bitfield operations
- Variable type conversion
- Service API correctness

### Integration Tests

- Event firing on changes
- Reactive system updates
- Persistence save/load
- Multi-flag/variable operations

### Performance Tests

- Flag access performance (target: <100 ns)
- Variable access performance (target: <200 ns)
- Memory usage validation
- Save file size validation

---

## Summary

This design provides:

1. **Efficient flag storage** using bitfield compression (~60 KB for 2500 flags)
2. **Flexible variable storage** using dictionary with type support
3. **ECS integration** via components on singleton entity
4. **Event-driven reactivity** for systems that need to respond to changes
5. **Persistence support** through Arch.Persistence integration
6. **Moddable design** with string-based identifiers
7. **Clean API** via IFlagVariableService interface
8. **Per-entity flags/variables** for entity-specific state
9. **Validation system** for flag/variable ID validation
10. **Metadata system** for documentation and tooling support
11. **Bulk operations** for efficient batch updates

The system is designed to scale to thousands of flags and hundreds of variables while maintaining good performance and memory efficiency. It supports both global game state and per-entity state, with optional validation and metadata capabilities.

