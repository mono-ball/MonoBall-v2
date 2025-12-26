using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Mods;
using MonoBall.Core.Mods.Definitions;
using MonoBall.Core.Scripting;
using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.Scripting.Services;
using MonoBall.Core.Scripting.Utilities;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for managing script lifecycle: initialization, cleanup, hot-reload, and component removal detection.
    /// </summary>
    public class ScriptLifecycleSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable
    {
        private readonly ScriptLoaderService _scriptLoader;
        private readonly IScriptApiProvider _apiProvider;
        private readonly DefinitionRegistry _registry;
        private readonly ILogger _logger;
        private readonly QueryDescription _queryDescription;
        private readonly Dictionary<
            (Entity Entity, string ScriptDefinitionId),
            ScriptBase
        > _scriptInstances = new();
        private readonly HashSet<(Entity Entity, string ScriptDefinitionId)> _initializedScripts =
            new();
        private readonly HashSet<(Entity Entity, string ScriptDefinitionId)> _previousAttachments =
            new();
        private bool _disposed = false;

        /// <summary>
        /// Gets the execution priority for this system.
        /// </summary>
        public int Priority => SystemPriority.ScriptLifecycle;

        /// <summary>
        /// Initializes a new instance of the ScriptLifecycleSystem class.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="scriptLoader">The script loader service.</param>
        /// <param name="apiProvider">The script API provider.</param>
        /// <param name="registry">The definition registry.</param>
        /// <param name="logger">The logger instance.</param>
        public ScriptLifecycleSystem(
            World world,
            ScriptLoaderService scriptLoader,
            IScriptApiProvider apiProvider,
            DefinitionRegistry registry,
            ILogger logger
        )
            : base(world)
        {
            _scriptLoader = scriptLoader ?? throw new ArgumentNullException(nameof(scriptLoader));
            _apiProvider = apiProvider ?? throw new ArgumentNullException(nameof(apiProvider));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Cache QueryDescription in constructor
            _queryDescription = new QueryDescription().WithAll<ScriptAttachmentComponent>();

            // Subscribe to EntityDestroyedEvent
            EventBus.Subscribe<EntityDestroyedEvent>(OnEntityDestroyed);
        }

        /// <summary>
        /// Updates script lifecycle: initializes new scripts, cleans up removed ones.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update.</param>
        public override void Update(in float deltaTime)
        {
            var currentAttachments = new HashSet<(Entity Entity, string ScriptDefinitionId)>();

            // Query entities with ScriptAttachmentComponent
            World.Query(
                in _queryDescription,
                (Entity entity, ref ScriptAttachmentComponent attachment) =>
                {
                    if (!attachment.IsActive)
                    {
                        return; // Skip inactive scripts
                    }

                    var key = (entity, attachment.ScriptDefinitionId);
                    currentAttachments.Add(key);

                    // Check if script needs initialization
                    if (!_initializedScripts.Contains(key))
                    {
                        InitializeScript(entity, attachment);
                    }
                }
            );

            // Cleanup scripts that were removed (component removed or entity destroyed)
            var scriptsToRemove = new List<(Entity Entity, string ScriptDefinitionId)>();
            foreach (var key in _previousAttachments)
            {
                if (!currentAttachments.Contains(key))
                {
                    scriptsToRemove.Add(key);
                }
            }

            foreach (var key in scriptsToRemove)
            {
                CleanupScript(key.Entity, key.ScriptDefinitionId);
            }

            // Update previous attachments for next frame
            _previousAttachments.Clear();
            foreach (var key in currentAttachments)
            {
                _previousAttachments.Add(key);
            }
        }

        /// <summary>
        /// Initializes a script for an entity.
        /// </summary>
        private void InitializeScript(Entity entity, ScriptAttachmentComponent attachment)
        {
            try
            {
                // Get script definition
                var scriptDef = _registry.GetById<ScriptDefinition>(attachment.ScriptDefinitionId);
                if (scriptDef == null)
                {
                    _logger.Warning(
                        "Script definition not found: {ScriptDefinitionId} for entity {EntityId}",
                        attachment.ScriptDefinitionId,
                        entity.Id
                    );
                    return;
                }

                // Create script instance
                var scriptInstance = _scriptLoader.CreateScriptInstance(
                    attachment.ScriptDefinitionId
                );
                if (scriptInstance == null)
                {
                    _logger.Warning(
                        "Failed to create script instance for {ScriptDefinitionId} on entity {EntityId}",
                        attachment.ScriptDefinitionId,
                        entity.Id
                    );
                    return;
                }

                // Build parameters from definition and EntityVariablesComponent
                var parameters = BuildScriptParameters(entity, scriptDef);

                // Create ScriptContext
                var context = new ScriptContext(
                    world: World,
                    entity: entity,
                    logger: _logger,
                    apis: _apiProvider,
                    scriptDefinitionId: attachment.ScriptDefinitionId,
                    parameters: parameters
                );

                // Initialize script
                scriptInstance.Initialize(context);
                scriptInstance.RegisterEventHandlers(context);

                // Store instance
                var key = (entity, attachment.ScriptDefinitionId);
                _scriptInstances[key] = scriptInstance;
                _initializedScripts.Add(key);

                // Mark as initialized in component (internal flag)
                attachment.IsInitialized = true;
                World.Set(entity, attachment);

                // Fire ScriptLoadedEvent
                var loadedEvent = new ScriptLoadedEvent
                {
                    Entity = entity,
                    ScriptDefinitionId = attachment.ScriptDefinitionId,
                    LoadedAt = DateTime.UtcNow,
                };
                EventBus.Send(ref loadedEvent);

                _logger.Debug(
                    "Initialized script {ScriptDefinitionId} on entity {EntityId}",
                    attachment.ScriptDefinitionId,
                    entity.Id
                );
            }
            catch (Exception ex)
            {
                _logger.Error(
                    ex,
                    "Error initializing script {ScriptDefinitionId} on entity {EntityId}",
                    attachment.ScriptDefinitionId,
                    entity.Id
                );

                // Fire ScriptErrorEvent
                var errorEvent = new ScriptErrorEvent
                {
                    Entity = entity,
                    ScriptDefinitionId = attachment.ScriptDefinitionId,
                    Exception = ex,
                    ErrorMessage = ex.Message,
                    ErrorAt = DateTime.UtcNow,
                };
                EventBus.Send(ref errorEvent);
            }
        }

        /// <summary>
        /// Builds script parameters from definition defaults and EntityVariablesComponent overrides.
        /// </summary>
        private Dictionary<string, object> BuildScriptParameters(
            Entity entity,
            ScriptDefinition scriptDef
        )
        {
            // Start with definition default values
            var parameters = ScriptParameterResolver.GetDefaults(scriptDef);

            // Apply EntityVariablesComponent overrides
            ScriptParameterResolver.ApplyEntityVariableOverrides(
                parameters,
                entity,
                World,
                scriptDef
            );

            // Validate parameter types match
            ScriptParameterResolver.ValidateEntityVariableTypes(entity, World, scriptDef);

            // Validate parameter constraints (min/max)
            ScriptParameterResolver.ValidateParameters(parameters, scriptDef);

            return parameters;
        }

        /// <summary>
        /// Cleans up a script for an entity.
        /// </summary>
        private void CleanupScript(Entity entity, string scriptDefinitionId)
        {
            var key = (entity, scriptDefinitionId);
            if (_scriptInstances.TryGetValue(key, out var scriptInstance))
            {
                try
                {
                    scriptInstance.OnUnload();
                }
                catch (Exception ex)
                {
                    _logger.Error(
                        ex,
                        "Error unloading script {ScriptDefinitionId} on entity {EntityId}",
                        scriptDefinitionId,
                        entity.Id
                    );
                }

                _scriptInstances.Remove(key);
                _initializedScripts.Remove(key);

                // Fire ScriptUnloadedEvent
                var unloadedEvent = new ScriptUnloadedEvent
                {
                    Entity = entity,
                    ScriptDefinitionId = scriptDefinitionId,
                    UnloadedAt = DateTime.UtcNow,
                };
                EventBus.Send(ref unloadedEvent);

                _logger.Debug(
                    "Cleaned up script {ScriptDefinitionId} on entity {EntityId}",
                    scriptDefinitionId,
                    entity.Id
                );
            }
        }

        /// <summary>
        /// Handles entity destruction - cleans up all scripts attached to the entity.
        /// </summary>
        private void OnEntityDestroyed(EntityDestroyedEvent evt)
        {
            var scriptsToRemove = new List<(Entity Entity, string ScriptDefinitionId)>();
            foreach (var key in _scriptInstances.Keys)
            {
                if (key.Entity.Id == evt.Entity.Id)
                {
                    scriptsToRemove.Add(key);
                }
            }

            foreach (var key in scriptsToRemove)
            {
                CleanupScript(key.Entity, key.ScriptDefinitionId);
            }
        }

        /// <summary>
        /// Disposes the system and cleans up all scripts.
        /// </summary>
        public new void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes the system and cleans up all scripts.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Unsubscribe from events
                EventBus.Unsubscribe<EntityDestroyedEvent>(OnEntityDestroyed);

                // Cleanup all scripts
                foreach (var scriptInstance in _scriptInstances.Values)
                {
                    try
                    {
                        scriptInstance.OnUnload();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error disposing script instance");
                    }
                }

                _scriptInstances.Clear();
                _initializedScripts.Clear();
                _previousAttachments.Clear();
            }

            _disposed = true;
        }
    }
}
