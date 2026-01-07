using System;
using System.Collections.Generic;
using System.Text.Json;
using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Implementation of IFlagVariableService using Arch ECS components.
///     All flag and variable logic is implemented here since components are pure data.
/// </summary>
public class FlagVariableService : IFlagVariableService
{
    private static readonly QueryDescription GameStateQuery = new QueryDescription().WithAll<
        FlagsComponent,
        VariablesComponent,
        FlagVariableMetadataComponent
    >();

    private readonly ILogger _logger;
    private readonly IFlagVariableValidator? _validator;

    private readonly World _world;
    private Entity _gameStateEntity = Entity.Null;
    private bool _initialized;

    public FlagVariableService(
        World world,
        ILogger logger,
        IFlagVariableValidator? validator = null
    )
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _validator = validator;
    }

    public bool GetFlag(string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            return false;

        EnsureInitialized();
        ref var flags = ref _world.Get<FlagsComponent>(_gameStateEntity);
        return GetFlagValue(flags.Flags, flags.FlagIndices, flagId);
    }

    public void SetFlag(string flagId, bool value)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            throw new ArgumentException("Flag ID cannot be null or empty.", nameof(flagId));

        // Validate flag ID if validator is available
        if (_validator != null && !_validator.IsValidFlagId(flagId))
        {
            var error = _validator.GetFlagIdValidationError(flagId);
            throw new ArgumentException(error, nameof(flagId));
        }

        EnsureInitialized();
        ref var flags = ref _world.Get<FlagsComponent>(_gameStateEntity);

        // Get old value before setting (helper method checks existence)
        var flagExists = flags.FlagIndices != null && flags.FlagIndices.ContainsKey(flagId);
        var oldValue = flagExists ? GetFlagValue(flags.Flags, flags.FlagIndices, flagId) : false;

        // Set the flag value
        var valueChanged = SetFlagValue(ref flags, flagId, value);

        // Fire event if value changed
        if (valueChanged)
        {
            var flagChangedEvent = new FlagChangedEvent
            {
                FlagId = flagId,
                Entity = null, // Global flag
                OldValue = oldValue,
                NewValue = value,
            };
            EventBus.Send(ref flagChangedEvent);
            _logger.Debug(
                "Flag {FlagId} changed from {OldValue} to {NewValue}",
                flagId,
                oldValue,
                value
            );
        }
    }

    public bool FlagExists(string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            return false;

        EnsureInitialized();
        ref var flags = ref _world.Get<FlagsComponent>(_gameStateEntity);
        return flags.FlagIndices != null && flags.FlagIndices.ContainsKey(flagId);
    }

    public IEnumerable<string> GetActiveFlags()
    {
        EnsureInitialized();
        ref var flags = ref _world.Get<FlagsComponent>(_gameStateEntity);

        if (flags.FlagIndices == null || flags.Flags == null)
            yield break;

        // Copy to local variables to avoid ref across yield boundary
        var flagIndices = flags.FlagIndices;
        var flagsArray = flags.Flags;

        foreach (var kvp in flagIndices)
        {
            var index = kvp.Value;
            var byteIndex = index / 8;
            var bitIndex = index % 8;

            if (byteIndex < flagsArray.Length && (flagsArray[byteIndex] & (1 << bitIndex)) != 0)
                yield return kvp.Key;
        }
    }

    public T? GetVariable<T>(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return default;

        EnsureInitialized();
        ref var variables = ref _world.Get<VariablesComponent>(_gameStateEntity);

        if (
            variables.Variables == null
            || !variables.Variables.TryGetValue(key, out var serializedValue)
        )
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
            var error = _validator.GetVariableKeyValidationError(key);
            throw new ArgumentException(error, nameof(key));
        }

        EnsureInitialized();
        ref var variables = ref _world.Get<VariablesComponent>(_gameStateEntity);

        // Ensure dictionaries are initialized
        EnsureVariablesComponentInitialized(ref variables);

        var oldValue = GetVariable<T>(key);
        var oldSerializedValue =
            variables.Variables != null && variables.Variables.TryGetValue(key, out var oldVal)
                ? oldVal
                : null;
        var oldType =
            variables.VariableTypes != null
            && variables.VariableTypes.TryGetValue(key, out var oldTypeVal)
                ? oldTypeVal
                : null;

        var serializedValue = SerializeValue(value);
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        variables.Variables![key] = serializedValue;
        variables.VariableTypes![key] = typeName;

        // Fire event if value changed (using proper comparison)
        if (ValuesChanged(oldValue, value))
        {
            var variableChangedEvent = new VariableChangedEvent
            {
                Key = key,
                Entity = null, // Global variable
                OldValue = oldSerializedValue,
                NewValue = serializedValue,
                OldType = oldType,
                NewType = typeName,
            };
            EventBus.Send(ref variableChangedEvent);
            _logger.Debug(
                "Variable {Key} changed from {OldValue} to {NewValue}",
                key,
                oldValue,
                value
            );
        }
    }

    public bool VariableExists(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        EnsureInitialized();
        ref var variables = ref _world.Get<VariablesComponent>(_gameStateEntity);
        return variables.Variables != null && variables.Variables.ContainsKey(key);
    }

    public void DeleteVariable(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        EnsureInitialized();
        ref var variables = ref _world.Get<VariablesComponent>(_gameStateEntity);

        if (variables.Variables != null && variables.Variables.TryGetValue(key, out var oldValue))
        {
            var oldType =
                variables.VariableTypes != null
                && variables.VariableTypes.TryGetValue(key, out var type)
                    ? type
                    : null;

            variables.Variables.Remove(key);
            variables.VariableTypes?.Remove(key);

            // Fire deletion event
            var variableDeletedEvent = new VariableDeletedEvent
            {
                Key = key,
                Entity = null, // Global variable
                OldValue = oldValue,
                OldType = oldType,
            };
            EventBus.Send(ref variableDeletedEvent);
            _logger.Debug("Variable {Key} deleted", key);
        }
    }

    public IEnumerable<string> GetVariableKeys()
    {
        EnsureInitialized();
        ref var variables = ref _world.Get<VariablesComponent>(_gameStateEntity);

        if (variables.Variables == null)
            yield break;

        foreach (var key in variables.Variables.Keys)
            yield return key;
    }

    public void SetFlags(Dictionary<string, bool> flags)
    {
        if (flags == null)
            throw new ArgumentNullException(nameof(flags));

        foreach (var kvp in flags)
            SetFlag(kvp.Key, kvp.Value);
    }

    public void SetVariables<T>(Dictionary<string, T> variables)
    {
        if (variables == null)
            throw new ArgumentNullException(nameof(variables));

        foreach (var kvp in variables)
            SetVariable(kvp.Key, kvp.Value);
    }

    public bool GetEntityFlag(Entity entity, string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            return false;

        if (!_world.IsAlive(entity) || !_world.Has<EntityFlagsComponent>(entity))
            return false;

        ref var flags = ref _world.Get<EntityFlagsComponent>(entity);
        return GetFlagValue(flags.Flags, flags.FlagIndices, flagId);
    }

    public void SetEntityFlag(Entity entity, string flagId, bool value)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            throw new ArgumentException("Flag ID cannot be null or empty.", nameof(flagId));

        if (!_world.IsAlive(entity))
            throw new ArgumentException("Entity is not alive.", nameof(entity));

        if (_validator != null && !_validator.IsValidFlagId(flagId))
        {
            var error = _validator.GetFlagIdValidationError(flagId);
            throw new ArgumentException(error, nameof(flagId));
        }

        if (!_world.Has<EntityFlagsComponent>(entity))
            _world.Add(entity, CreateEntityFlagsComponent());

        ref var flags = ref _world.Get<EntityFlagsComponent>(entity);

        // Get old value before setting (helper method checks existence)
        var flagExists = flags.FlagIndices != null && flags.FlagIndices.ContainsKey(flagId);
        var oldValue = flagExists ? GetFlagValue(flags.Flags, flags.FlagIndices, flagId) : false;

        // Set the flag value
        var valueChanged = SetEntityFlagValue(ref flags, flagId, value);

        // Fire event if value changed
        if (valueChanged)
        {
            var flagChangedEvent = new FlagChangedEvent
            {
                FlagId = flagId,
                Entity = entity, // Entity-specific flag
                OldValue = oldValue,
                NewValue = value,
            };
            EventBus.Send(ref flagChangedEvent);
            _logger.Debug(
                "Entity flag {FlagId} on entity {EntityId} changed from {OldValue} to {NewValue}",
                flagId,
                entity.Id,
                oldValue,
                value
            );
        }
    }

    public T? GetEntityVariable<T>(Entity entity, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return default;

        if (!_world.IsAlive(entity) || !_world.Has<EntityVariablesComponent>(entity))
            return default;

        ref var variables = ref _world.Get<EntityVariablesComponent>(entity);

        if (
            variables.Variables == null
            || !variables.Variables.TryGetValue(key, out var serializedValue)
        )
            return default;

        return DeserializeValue<T>(serializedValue);
    }

    public void SetEntityVariable<T>(Entity entity, string key, T value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Variable key cannot be null or empty.", nameof(key));

        if (!_world.IsAlive(entity))
            throw new ArgumentException("Entity is not alive.", nameof(entity));

        if (_validator != null && !_validator.IsValidVariableKey(key))
        {
            var error = _validator.GetVariableKeyValidationError(key);
            throw new ArgumentException(error, nameof(key));
        }

        if (!_world.Has<EntityVariablesComponent>(entity))
            _world.Add(entity, CreateEntityVariablesComponent());

        ref var variables = ref _world.Get<EntityVariablesComponent>(entity);
        EnsureEntityVariablesComponentInitialized(ref variables);

        var oldValue = GetEntityVariable<T>(entity, key);
        var oldSerializedValue =
            variables.Variables != null && variables.Variables.TryGetValue(key, out var oldVal)
                ? oldVal
                : null;
        var oldType =
            variables.VariableTypes != null
            && variables.VariableTypes.TryGetValue(key, out var oldTypeVal)
                ? oldTypeVal
                : null;

        var serializedValue = SerializeValue(value);
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        variables.Variables![key] = serializedValue;
        variables.VariableTypes![key] = typeName;

        // Fire event if value changed (using proper comparison)
        if (ValuesChanged(oldValue, value))
        {
            var variableChangedEvent = new VariableChangedEvent
            {
                Key = key,
                Entity = entity, // Entity-specific variable
                OldValue = oldSerializedValue,
                NewValue = serializedValue,
                OldType = oldType,
                NewType = typeName,
            };
            EventBus.Send(ref variableChangedEvent);
            _logger.Debug(
                "Entity variable {Key} on entity {EntityId} changed from {OldValue} to {NewValue}",
                key,
                entity.Id,
                oldValue,
                value
            );
        }
    }

    public void RegisterFlagMetadata(FlagMetadata metadata)
    {
        EnsureInitialized();
        ref var metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
        metadataComponent.FlagMetadata ??= new Dictionary<string, FlagMetadata>();
        metadataComponent.FlagMetadata[metadata.FlagId] = metadata;
    }

    public void RegisterVariableMetadata(VariableMetadata metadata)
    {
        EnsureInitialized();
        ref var metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);
        metadataComponent.VariableMetadata ??= new Dictionary<string, VariableMetadata>();
        metadataComponent.VariableMetadata[metadata.Key] = metadata;
    }

    public FlagMetadata? GetFlagMetadata(string flagId)
    {
        if (string.IsNullOrWhiteSpace(flagId))
            return null;

        EnsureInitialized();
        ref var metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);

        if (
            metadataComponent.FlagMetadata == null
            || !metadataComponent.FlagMetadata.TryGetValue(flagId, out var metadata)
        )
            return null;

        return metadata;
    }

    public VariableMetadata? GetVariableMetadata(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        EnsureInitialized();
        ref var metadataComponent = ref _world.Get<FlagVariableMetadataComponent>(_gameStateEntity);

        if (
            metadataComponent.VariableMetadata == null
            || !metadataComponent.VariableMetadata.TryGetValue(key, out var metadata)
        )
            return null;

        return metadata;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            // Validate entity still exists
            if (!_world.IsAlive(_gameStateEntity))
            {
                _initialized = false;
                _gameStateEntity = Entity.Null;
            }
            else
            {
                return;
            }
        }

        // Find or create singleton game state entity
        // Note: Arch ECS Query doesn't support early termination, but we optimize by
        // checking found flag and only processing first match
        var found = false;
        var matchCount = 0;
        _world.Query(
            in GameStateQuery,
            entity =>
            {
                matchCount++;
                if (!found)
                {
                    // Only process first match (singleton should only have one)
                    _gameStateEntity = entity;
                    found = true;
                }
            }
        );

        // Warn if multiple entities match (shouldn't happen for singleton)
        if (matchCount > 1)
            _logger.Warning(
                "Found {Count} entities matching game state query, expected singleton. Using first match.",
                matchCount
            );

        if (!found)
        {
            _gameStateEntity = _world.Create(
                CreateFlagsComponent(),
                CreateVariablesComponent(),
                CreateMetadataComponent()
            );
            _logger.Information("Created game state singleton entity for flags/variables");
        }

        _initialized = true;
    }

    private static FlagVariableMetadataComponent CreateMetadataComponent()
    {
        return new FlagVariableMetadataComponent
        {
            FlagMetadata = new Dictionary<string, FlagMetadata>(),
            VariableMetadata = new Dictionary<string, VariableMetadata>(),
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
            NextIndex = 0,
        };
    }

    private static VariablesComponent CreateVariablesComponent()
    {
        return new VariablesComponent
        {
            Variables = new Dictionary<string, string>(),
            VariableTypes = new Dictionary<string, string>(),
        };
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
            _ => JsonSerializer.Serialize(value),
        };
    }

    private static T? DeserializeValue<T>(string serializedValue)
    {
        if (string.IsNullOrEmpty(serializedValue))
            return default;

        var targetType = typeof(T);

        // Handle common types with direct conversion
        if (targetType == typeof(string))
            return (T)(object)serializedValue;

        if (targetType == typeof(int) && int.TryParse(serializedValue, out var intValue))
            return (T)(object)intValue;

        if (targetType == typeof(float) && float.TryParse(serializedValue, out var floatValue))
            return (T)(object)floatValue;

        if (targetType == typeof(bool) && bool.TryParse(serializedValue, out var boolValue))
            return (T)(object)boolValue;

        // Fallback to JSON deserialization
        try
        {
            return JsonSerializer.Deserialize<T>(serializedValue);
        }
        catch
        {
            return default;
        }
    }

    private static EntityFlagsComponent CreateEntityFlagsComponent()
    {
        return new EntityFlagsComponent
        {
            Flags = new byte[313],
            FlagIndices = new Dictionary<string, int>(),
            IndexToFlagId = new Dictionary<int, string>(),
            NextIndex = 0,
        };
    }

    private static EntityVariablesComponent CreateEntityVariablesComponent()
    {
        return new EntityVariablesComponent
        {
            Variables = new Dictionary<string, string>(),
            VariableTypes = new Dictionary<string, string>(),
        };
    }

    #region Helper Methods

    /// <summary>
    ///     Gets a flag value from bitfield storage.
    /// </summary>
    private static bool GetFlagValue(
        byte[]? flags,
        Dictionary<string, int>? flagIndices,
        string flagId
    )
    {
        if (flagIndices == null || !flagIndices.TryGetValue(flagId, out var index))
            return false;

        if (flags == null)
            return false;

        var byteIndex = index / 8;
        var bitIndex = index % 8;

        if (byteIndex >= flags.Length)
            return false;

        return (flags[byteIndex] & (1 << bitIndex)) != 0;
    }

    /// <summary>
    ///     Sets a flag value in bitfield storage for FlagsComponent. Returns true if value actually changed.
    /// </summary>
    private static bool SetFlagValue(ref FlagsComponent flags, string flagId, bool value)
    {
        // Ensure component is initialized
        EnsureFlagsComponentInitialized(ref flags);

        // Check if flag exists before getting old value
        var flagExists = flags.FlagIndices!.ContainsKey(flagId);
        var oldValue = flagExists ? GetFlagValue(flags.Flags, flags.FlagIndices, flagId) : false;

        // Get or allocate index for this flag
        if (!flags.FlagIndices.TryGetValue(flagId, out var index))
        {
            index = flags.NextIndex++;
            flags.FlagIndices[flagId] = index;
            flags.IndexToFlagId![index] = flagId;

            // Expand bitfield if needed
            var flagsArray = flags.Flags!;
            ExpandBitfieldIfNeeded(ref flagsArray, index);
            flags.Flags = flagsArray;
        }

        // Set the bit
        var byteIndex = index / 8;
        var bitIndex = index % 8;

        if (value)
            flags.Flags![byteIndex] |= (byte)(1 << bitIndex);
        else
            flags.Flags![byteIndex] &= (byte)~(1 << bitIndex);

        return oldValue != value;
    }

    /// <summary>
    ///     Sets a flag value in bitfield storage for EntityFlagsComponent. Returns true if value actually changed.
    /// </summary>
    private static bool SetEntityFlagValue(
        ref EntityFlagsComponent flags,
        string flagId,
        bool value
    )
    {
        // Ensure component is initialized
        EnsureEntityFlagsComponentInitialized(ref flags);

        // Check if flag exists before getting old value
        var flagExists = flags.FlagIndices!.ContainsKey(flagId);
        var oldValue = flagExists ? GetFlagValue(flags.Flags, flags.FlagIndices, flagId) : false;

        // Get or allocate index for this flag
        if (!flags.FlagIndices.TryGetValue(flagId, out var index))
        {
            index = flags.NextIndex++;
            flags.FlagIndices[flagId] = index;
            flags.IndexToFlagId![index] = flagId;

            // Expand bitfield if needed
            var flagsArray = flags.Flags!;
            ExpandBitfieldIfNeeded(ref flagsArray, index);
            flags.Flags = flagsArray;
        }

        // Set the bit
        var byteIndex = index / 8;
        var bitIndex = index % 8;

        if (value)
            flags.Flags![byteIndex] |= (byte)(1 << bitIndex);
        else
            flags.Flags![byteIndex] &= (byte)~(1 << bitIndex);

        return oldValue != value;
    }

    /// <summary>
    ///     Expands bitfield array if needed to accommodate the given index.
    /// </summary>
    private static void ExpandBitfieldIfNeeded(ref byte[] flags, int index)
    {
        var requiredBytes = index / 8 + 1;
        if (requiredBytes > flags.Length)
        {
            var newSize = Math.Max(flags.Length * 2, requiredBytes);
            var newFlags = new byte[newSize];
            Array.Copy(flags, newFlags, flags.Length);
            flags = newFlags;
        }
    }

    /// <summary>
    ///     Ensures flags component is initialized with non-null collections.
    /// </summary>
    private static void EnsureFlagsComponentInitialized(ref FlagsComponent flags)
    {
        flags.FlagIndices ??= new Dictionary<string, int>();
        flags.IndexToFlagId ??= new Dictionary<int, string>();
        flags.Flags ??= new byte[313];
    }

    /// <summary>
    ///     Ensures entity flags component is initialized with non-null collections.
    /// </summary>
    private static void EnsureEntityFlagsComponentInitialized(ref EntityFlagsComponent flags)
    {
        flags.FlagIndices ??= new Dictionary<string, int>();
        flags.IndexToFlagId ??= new Dictionary<int, string>();
        flags.Flags ??= new byte[313];
    }

    /// <summary>
    ///     Ensures variables component is initialized with non-null collections.
    /// </summary>
    private static void EnsureVariablesComponentInitialized(ref VariablesComponent variables)
    {
        variables.Variables ??= new Dictionary<string, string>();
        variables.VariableTypes ??= new Dictionary<string, string>();
    }

    /// <summary>
    ///     Ensures entity variables component is initialized with non-null collections.
    /// </summary>
    private static void EnsureEntityVariablesComponentInitialized(
        ref EntityVariablesComponent variables
    )
    {
        variables.Variables ??= new Dictionary<string, string>();
        variables.VariableTypes ??= new Dictionary<string, string>();
    }

    /// <summary>
    ///     Compares two values, handling floating-point precision issues.
    /// </summary>
    private static bool ValuesChanged<T>(T? oldValue, T? newValue)
    {
        if (oldValue == null && newValue == null)
            return false;

        if (oldValue == null || newValue == null)
            return true;

        // Handle floating-point types with epsilon comparison
        return oldValue switch
        {
            float f when newValue is float f2 => Math.Abs(f - f2) > float.Epsilon,
            double d when newValue is double d2 => Math.Abs(d - d2) > double.Epsilon,
            _ => !Equals(oldValue, newValue),
        };
    }

    #endregion
}
