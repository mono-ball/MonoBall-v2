using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Maps;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System responsible for listening to map transitions and triggering popup display.
    /// Resolves map section and popup theme definitions and fires MapPopupShowEvent.
    /// </summary>
    public class MapPopupOrchestratorSystem : BaseSystem<World, float>, IDisposable
    {
        private readonly IModManager _modManager;
        private readonly ILogger _logger;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the MapPopupOrchestratorSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="modManager">The mod manager for accessing definitions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public MapPopupOrchestratorSystem(World world, IModManager modManager, ILogger logger)
            : base(world)
        {
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Subscribe to MapTransitionEvent and GameEnteredEvent using RefAction pattern
            EventBus.Subscribe<MapTransitionEvent>(OnMapTransition);
            EventBus.Subscribe<GameEnteredEvent>(OnGameEntered);
        }

        /// <summary>
        /// Handles GameEnteredEvent by showing popup for the initial map.
        /// </summary>
        /// <param name="evt">The game entered event.</param>
        private void OnGameEntered(ref GameEnteredEvent evt)
        {
            _logger.Debug(
                "Received GameEnteredEvent for initial map {InitialMapId}",
                evt.InitialMapId
            );

            if (string.IsNullOrEmpty(evt.InitialMapId))
            {
                _logger.Debug("GameEnteredEvent has empty InitialMapId, skipping popup");
                return;
            }

            // Show popup for the initial map (same logic as map transition)
            ShowPopupForMap(evt.InitialMapId);
        }

        /// <summary>
        /// Handles MapTransitionEvent by resolving map section and theme definitions, then firing MapPopupShowEvent.
        /// </summary>
        /// <param name="evt">The map transition event.</param>
        private void OnMapTransition(ref MapTransitionEvent evt)
        {
            _logger.Debug(
                "Received MapTransitionEvent from {SourceMapId} to {TargetMapId}",
                evt.SourceMapId ?? "(null)",
                evt.TargetMapId ?? "(null)"
            );

            if (string.IsNullOrEmpty(evt.TargetMapId))
            {
                _logger.Debug("MapTransitionEvent has empty TargetMapId, skipping popup");
                return;
            }

            // Show popup for the target map
            ShowPopupForMap(evt.TargetMapId);
        }

        /// <summary>
        /// Shows a popup for the specified map by resolving map section and theme definitions, then firing MapPopupShowEvent.
        /// </summary>
        /// <param name="mapId">The map ID to show a popup for.</param>
        private void ShowPopupForMap(string mapId)
        {
            // Look up MapDefinition by mapId
            var mapDefinition = _modManager.GetDefinition<MapDefinition>(mapId);
            if (mapDefinition == null)
            {
                _logger.Warning("Map definition not found for {MapId}", mapId);
                return;
            }

            // Check if map name display is enabled for this map
            if (!mapDefinition.ShowMapName)
            {
                _logger.Debug("Map {MapId} has ShowMapName=false, skipping popup", mapId);
                return;
            }

            // Get MapSectionId from MapDefinition
            if (string.IsNullOrEmpty(mapDefinition.MapSectionId))
            {
                _logger.Debug("Map {MapId} has no MapSectionId, skipping popup", mapId);
                return;
            }

            // Look up MapSectionDefinition by MapSectionId
            var mapSectionDefinition = _modManager.GetDefinition<MapSectionDefinition>(
                mapDefinition.MapSectionId
            );
            if (mapSectionDefinition == null)
            {
                _logger.Warning(
                    "MapSection definition not found for {MapSectionId}",
                    mapDefinition.MapSectionId
                );
                return;
            }

            // Get PopupTheme from MapSectionDefinition
            if (string.IsNullOrEmpty(mapSectionDefinition.PopupTheme))
            {
                _logger.Warning(
                    "MapSection {MapSectionId} has no PopupTheme, skipping popup",
                    mapSectionDefinition.Id
                );
                return;
            }

            // Look up PopupThemeDefinition to validate it exists
            var popupThemeDefinition = _modManager.GetDefinition<PopupThemeDefinition>(
                mapSectionDefinition.PopupTheme
            );
            if (popupThemeDefinition == null)
            {
                _logger.Warning(
                    "PopupTheme definition not found for {ThemeId}, skipping popup",
                    mapSectionDefinition.PopupTheme
                );
                return;
            }

            // Fire MapPopupShowEvent with resolved data
            var showEvent = new MapPopupShowEvent
            {
                MapSectionId = mapSectionDefinition.Id,
                MapSectionName = mapSectionDefinition.Name,
                ThemeId = mapSectionDefinition.PopupTheme,
            };
            EventBus.Send(ref showEvent);

            _logger.Information(
                "Fired MapPopupShowEvent for {MapSectionName} with theme {ThemeId}",
                mapSectionDefinition.Name,
                mapSectionDefinition.PopupTheme
            );
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <remarks>
        /// Implements IDisposable to properly clean up event subscriptions.
        /// Uses standard dispose pattern without finalizer since only managed resources are disposed.
        /// Uses 'new' keyword because BaseSystem may have a Dispose() method with different signature.
        /// </remarks>
        public new void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Disposes the system and unsubscribes from events.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events using RefAction pattern
                    EventBus.Unsubscribe<MapTransitionEvent>(OnMapTransition);
                    EventBus.Unsubscribe<GameEnteredEvent>(OnGameEntered);
                }
                _disposed = true;
            }
        }
    }
}
