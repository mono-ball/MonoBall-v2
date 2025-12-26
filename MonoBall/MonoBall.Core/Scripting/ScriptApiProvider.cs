using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.Mods;
using MonoBall.Core.Scripting.Utilities;

namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// Implementation of IScriptApiProvider that wraps existing game systems.
    /// Provides script-safe access to game functionality.
    /// </summary>
    public class ScriptApiProvider : IScriptApiProvider
    {
        private readonly World _world;
        private PlayerSystem? _playerSystem;
        private MapLoaderSystem? _mapLoaderSystem;
        private MovementSystem? _movementSystem;
        private readonly ICameraService? _cameraService;
        private readonly IFlagVariableService _flagVariableService;
        private readonly DefinitionRegistry _definitionRegistry;
        private IPlayerApi? _playerApi;
        private IMapApi? _mapApi;
        private IMovementApi? _movementApi;
        private ICameraApi? _cameraApi;
        private INpcApi? _npcApi;

        /// <summary>
        /// Initializes a new instance of the ScriptApiProvider class.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="playerSystem">Optional player system.</param>
        /// <param name="mapLoaderSystem">Optional map loader system.</param>
        /// <param name="movementSystem">Optional movement system.</param>
        /// <param name="cameraService">Optional camera service.</param>
        /// <param name="flagVariableService">The flag/variable service.</param>
        /// <param name="definitionRegistry">The definition registry.</param>
        public ScriptApiProvider(
            World world,
            PlayerSystem? playerSystem,
            MapLoaderSystem? mapLoaderSystem,
            MovementSystem? movementSystem,
            ICameraService? cameraService,
            IFlagVariableService flagVariableService,
            DefinitionRegistry definitionRegistry
        )
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _playerSystem = playerSystem;
            _mapLoaderSystem = mapLoaderSystem;
            _movementSystem = movementSystem;
            _cameraService = cameraService;
            _flagVariableService =
                flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
            _definitionRegistry =
                definitionRegistry ?? throw new ArgumentNullException(nameof(definitionRegistry));
        }

        /// <summary>
        /// Gets the player API.
        /// </summary>
        public IPlayerApi Player
        {
            get
            {
                if (_playerApi == null)
                {
                    _playerApi = new PlayerApiImpl(_world, _playerSystem);
                }
                return _playerApi;
            }
        }

        /// <summary>
        /// Gets the map API.
        /// </summary>
        public IMapApi Map
        {
            get
            {
                if (_mapApi == null)
                {
                    _mapApi = new MapApiImpl(_world, _mapLoaderSystem);
                }
                return _mapApi;
            }
        }

        /// <summary>
        /// Gets the movement API.
        /// </summary>
        public IMovementApi Movement
        {
            get
            {
                if (_movementApi == null)
                {
                    _movementApi = new MovementApiImpl(_world, _movementSystem);
                }
                return _movementApi;
            }
        }

        /// <summary>
        /// Gets the camera API.
        /// </summary>
        public ICameraApi Camera
        {
            get
            {
                if (_cameraApi == null)
                {
                    _cameraApi = new CameraApiImpl(_cameraService);
                }
                return _cameraApi;
            }
        }

        /// <summary>
        /// Gets the flags/variables service.
        /// </summary>
        public IFlagVariableService Flags => _flagVariableService;

        /// <summary>
        /// Gets the definition registry.
        /// </summary>
        public DefinitionRegistry Definitions => _definitionRegistry;

        /// <summary>
        /// Gets the NPC API.
        /// </summary>
        public INpcApi Npc
        {
            get
            {
                if (_npcApi == null)
                {
                    _npcApi = new NpcApiImpl(_world);
                }

                return _npcApi;
            }
        }

        /// <summary>
        /// Updates the system references. Called after systems are fully initialized.
        /// </summary>
        /// <param name="playerSystem">The player system.</param>
        /// <param name="mapLoaderSystem">The map loader system.</param>
        /// <param name="movementSystem">The movement system.</param>
        public void UpdateSystems(
            PlayerSystem? playerSystem,
            MapLoaderSystem? mapLoaderSystem,
            MovementSystem? movementSystem
        )
        {
            _playerSystem = playerSystem;
            _mapLoaderSystem = mapLoaderSystem;
            _movementSystem = movementSystem;
            // Clear cached APIs so they're recreated with new system references
            _playerApi = null;
            _mapApi = null;
            _movementApi = null;
        }

        // API implementations
        private class PlayerApiImpl : IPlayerApi
        {
            private readonly World _world;
            private readonly PlayerSystem? _playerSystem;

            public PlayerApiImpl(World world, PlayerSystem? playerSystem)
            {
                _world = world;
                _playerSystem = playerSystem;
            }

            public Entity? GetPlayerEntity()
            {
                if (_playerSystem == null)
                    return null;
                // PlayerSystem has a private _playerEntity field, so we need to query
                var query = new Arch.Core.QueryDescription().WithAll<PlayerComponent>();
                Entity? playerEntity = null;
                _world.Query(
                    query,
                    (Entity entity) =>
                    {
                        playerEntity = entity;
                    }
                );
                return playerEntity;
            }

            public PositionComponent? GetPlayerPosition()
            {
                var playerEntity = GetPlayerEntity();
                if (!playerEntity.HasValue || !_world.Has<PositionComponent>(playerEntity.Value))
                {
                    return null;
                }
                return _world.Get<PositionComponent>(playerEntity.Value);
            }

            public string? GetPlayerMapId()
            {
                var playerEntity = GetPlayerEntity();
                if (!playerEntity.HasValue || !_world.Has<MapComponent>(playerEntity.Value))
                {
                    return null;
                }
                return _world.Get<MapComponent>(playerEntity.Value).MapId;
            }

            public bool PlayerExists()
            {
                return GetPlayerEntity().HasValue;
            }
        }

        private class MapApiImpl : IMapApi
        {
            private readonly World _world;
            private readonly MapLoaderSystem? _mapLoaderSystem;

            public MapApiImpl(World world, MapLoaderSystem? mapLoaderSystem)
            {
                _world = world;
                _mapLoaderSystem = mapLoaderSystem;
            }

            public void LoadMap(string mapId, Vector2? tilePosition = null)
            {
                _mapLoaderSystem?.LoadMap(mapId, tilePosition);
            }

            public void UnloadMap(string mapId)
            {
                _mapLoaderSystem?.UnloadMap(mapId);
            }

            public bool IsMapLoaded(string mapId)
            {
                // Query for map entity
                var query = new Arch.Core.QueryDescription().WithAll<MapComponent>();
                bool found = false;
                _world.Query(
                    query,
                    (ref MapComponent mapComp) =>
                    {
                        if (mapComp.MapId == mapId)
                        {
                            found = true;
                        }
                    }
                );
                return found;
            }

            public Entity? GetMapEntity(string mapId)
            {
                var query = new Arch.Core.QueryDescription().WithAll<MapComponent>();
                Entity? mapEntity = null;
                _world.Query(
                    query,
                    (Entity entity, ref MapComponent mapComp) =>
                    {
                        if (mapComp.MapId == mapId)
                        {
                            mapEntity = entity;
                        }
                    }
                );
                return mapEntity;
            }

            public System.Collections.Generic.IEnumerable<string> GetLoadedMapIds()
            {
                var query = new Arch.Core.QueryDescription().WithAll<MapComponent>();
                var mapIds = new System.Collections.Generic.List<string>();
                _world.Query(
                    query,
                    (ref MapComponent mapComp) =>
                    {
                        mapIds.Add(mapComp.MapId);
                    }
                );
                return mapIds;
            }

            public System.Collections.Generic.IEnumerable<string> GetActiveMapIds()
            {
                // For now, return all loaded maps
                // TODO: Filter by ActiveMapEntity tag if needed
                return GetLoadedMapIds();
            }
        }

        private class MovementApiImpl : IMovementApi
        {
            private readonly World _world;
            private readonly MovementSystem? _movementSystem;

            public MovementApiImpl(World world, MovementSystem? movementSystem)
            {
                _world = world;
                _movementSystem = movementSystem;
            }

            public bool RequestMovement(Entity entity, Direction direction)
            {
                if (!_world.IsAlive(entity) || !_world.Has<GridMovement>(entity))
                {
                    return false;
                }

                // Add MovementRequest component
                var request = new MovementRequest(direction, active: true);
                if (_world.Has<MovementRequest>(entity))
                {
                    _world.Set(entity, request);
                }
                else
                {
                    _world.Add(entity, request);
                }
                return true;
            }

            public bool IsMoving(Entity entity)
            {
                if (!_world.IsAlive(entity) || !_world.Has<GridMovement>(entity))
                {
                    return false;
                }
                return _world.Get<GridMovement>(entity).IsMoving;
            }

            public void LockMovement(Entity entity)
            {
                if (!_world.IsAlive(entity) || !_world.Has<GridMovement>(entity))
                {
                    return;
                }
                ref var movement = ref _world.Get<GridMovement>(entity);
                movement.MovementLocked = true;
            }

            public void UnlockMovement(Entity entity)
            {
                if (!_world.IsAlive(entity) || !_world.Has<GridMovement>(entity))
                {
                    return;
                }
                ref var movement = ref _world.Get<GridMovement>(entity);
                movement.MovementLocked = false;
            }

            public bool IsMovementLocked(Entity entity)
            {
                if (!_world.IsAlive(entity) || !_world.Has<GridMovement>(entity))
                {
                    return false;
                }
                return _world.Get<GridMovement>(entity).MovementLocked;
            }
        }

        private class CameraApiImpl : ICameraApi
        {
            private readonly ICameraService? _cameraService;

            public CameraApiImpl(ICameraService? cameraService)
            {
                _cameraService = cameraService;
            }

            public CameraComponent? GetActiveCamera()
            {
                return _cameraService?.GetActiveCamera();
            }

            public Vector2 GetCameraPosition()
            {
                var camera = GetActiveCamera();
                return camera?.Position ?? Vector2.Zero;
            }

            public Vector2 GetCameraViewport()
            {
                var camera = GetActiveCamera();
                if (!camera.HasValue)
                {
                    return Vector2.Zero;
                }
                return new Vector2(camera.Value.Viewport.Width, camera.Value.Viewport.Height);
            }
        }

        private class NpcApiImpl : INpcApi
        {
            private readonly World _world;

            public NpcApiImpl(World world)
            {
                _world = world;
            }

            public void FaceDirection(Entity npc, Direction direction)
            {
                if (!_world.IsAlive(npc))
                {
                    throw new ArgumentException($"Entity {npc.Id} is not alive.", nameof(npc));
                }

                if (!_world.Has<GridMovement>(npc))
                {
                    throw new InvalidOperationException(
                        $"Entity {npc.Id} does not have GridMovement component. "
                            + "Cannot set facing direction without GridMovement component."
                    );
                }

                ref var movement = ref _world.Get<GridMovement>(npc);
                movement.FacingDirection = direction;
            }

            public Direction? GetFacingDirection(Entity npc)
            {
                if (!_world.IsAlive(npc) || !_world.Has<GridMovement>(npc))
                {
                    return null;
                }

                return _world.Get<GridMovement>(npc).FacingDirection;
            }

            public void FaceEntity(Entity npc, Entity target)
            {
                if (!_world.IsAlive(npc))
                {
                    throw new ArgumentException($"Entity {npc.Id} is not alive.", nameof(npc));
                }

                if (!_world.IsAlive(target))
                {
                    throw new ArgumentException(
                        $"Entity {target.Id} is not alive.",
                        nameof(target)
                    );
                }

                if (!_world.Has<PositionComponent>(npc))
                {
                    throw new InvalidOperationException(
                        $"Entity {npc.Id} does not have PositionComponent. "
                            + "Cannot calculate direction to face without position component."
                    );
                }

                if (!_world.Has<PositionComponent>(target))
                {
                    throw new InvalidOperationException(
                        $"Entity {target.Id} does not have PositionComponent. "
                            + "Cannot calculate direction to face without target position component."
                    );
                }

                var npcPos = _world.Get<PositionComponent>(npc);
                var targetPos = _world.Get<PositionComponent>(target);

                var direction = DirectionHelper.GetDirectionTo(
                    npcPos.X,
                    npcPos.Y,
                    targetPos.X,
                    targetPos.Y
                );

                FaceDirection(npc, direction);
            }

            public PositionComponent? GetPosition(Entity npc)
            {
                if (!_world.IsAlive(npc) || !_world.Has<PositionComponent>(npc))
                {
                    return null;
                }

                return _world.Get<PositionComponent>(npc);
            }

            public void SetMovementState(Entity npc, RunningState state)
            {
                if (!_world.IsAlive(npc))
                {
                    throw new ArgumentException($"Entity {npc.Id} is not alive.", nameof(npc));
                }

                if (!_world.Has<GridMovement>(npc))
                {
                    throw new InvalidOperationException(
                        $"Entity {npc.Id} does not have GridMovement component. "
                            + "Cannot set movement state without GridMovement component."
                    );
                }

                ref var movement = ref _world.Get<GridMovement>(npc);
                movement.RunningState = state;
            }
        }
    }
}
