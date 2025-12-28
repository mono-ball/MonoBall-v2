# InteractionId Design Document

## Executive Summary

This document designs the `interactionId` system to replace `interactionScript` in NPC definitions. The new system uses definition IDs to reference `ScriptDefinition` objects, providing better type safety, validation, and integration with the existing scripting architecture. Interactions are properties of NPCs (and potentially other entities), not separate map entities.

---

## Current State

### Current Implementation

**NPC Interactions** (in `NpcDefinition`):
- Each NPC has:
  - `interactionScript`: Script path/ID (e.g., `"base:behavior:interaction/littleroot_town_event_script_twin"`) - **TO BE REPLACED**
  - Other NPC properties (sprite, behavior, position, etc.)

**Note**: Maps themselves do not have interactions. Interactions are properties of entities (NPCs, objects, etc.), not separate map entities.

### Problems with Current Approach

1. **Inconsistent Naming**: Uses `interactionScript` which implies a script file path, but should reference a definition ID
2. **No Type Safety**: Direct string references without validation
3. **Missing Integration**: Not integrated with the scripting system's `ScriptDefinition` architecture
4. **No Validation**: Cannot verify that referenced scripts exist at load time

---

## Design Goals

1. **Unified ID System**: Use `interactionId` consistently for NPCs and other entities
2. **Definition-Based**: Reference `ScriptDefinition` objects via ID
3. **Type Safety**: Validate interaction IDs at entity creation time
4. **Script Integration**: Leverage existing `ScriptAttachmentComponent` and `ScriptLifecycleSystem`
5. **Fail Fast**: Throw clear exceptions when interaction definitions are missing
6. **No Backward Compatibility**: Remove `interactionScript` immediately - clean break

---

## Proposed Design

### 1. NpcDefinition Changes

Replace `interactionScript` with `interactionId`:

```csharp
public class NpcDefinition
{
    // ... existing properties ...
    
    /// <summary>
    /// The interaction script definition ID for this NPC.
    /// References a ScriptDefinition in the DefinitionRegistry.
    /// Format: "base:script:hoenn/Interactions/LittlerootTown_EventScript_Twin"
    /// Replaces the old "interactionScript" field.
    /// </summary>
    [JsonPropertyName("interactionId")]
    public string? InteractionId { get; set; }
}
```

### 2. ECS Component Design

Create an `InteractionComponent` for entities that can be interacted with:

```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that marks an entity as interactable (NPCs, objects, etc.).
    /// When the player interacts with this entity, the referenced script is executed.
    /// </summary>
    public struct InteractionComponent
    {
        /// <summary>
        /// The unique identifier for this interaction.
        /// For NPCs: typically the NPC ID (e.g., "base:npc:hoenn/littleroot_town/localid_littleroot_twin")
        /// </summary>
        public string InteractionId { get; set; }

        /// <summary>
        /// The script definition ID to execute when this interaction is triggered.
        /// References a ScriptDefinition in the DefinitionRegistry.
        /// </summary>
        public string? ScriptDefinitionId { get; set; }

        /// <summary>
        /// The width of the interaction area in tiles.
        /// Default: 1 tile for NPCs and most interactions.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// The height of the interaction area in tiles.
        /// Default: 1 tile for NPCs and most interactions.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// The elevation (z-order) of the interaction.
        /// For NPCs: matches NpcComponent.Elevation
        /// </summary>
        public int Elevation { get; set; }

        /// <summary>
        /// Optional facing direction requirement for interaction.
        /// If specified, player must be facing this direction to interact.
        /// Values: null (any direction), "up", "down", "left", "right"
        /// </summary>
        public string? RequiredFacing { get; set; }

        /// <summary>
        /// Whether this interaction is currently enabled.
        /// Can be toggled by scripts to enable/disable interaction.
        /// </summary>
        public bool IsEnabled { get; set; }
    }
}
```

**Design Decision**: NPCs use `InteractionComponent` for interaction handling. This:
- **Enables composition**: NPCs have both `NpcComponent` (NPC-specific data) and `InteractionComponent` (interaction data)
- **Unified handling**: All interactable entities use the same component and system

### 3. MapLoaderSystem Changes

Update `CreateNpcEntity` to add `InteractionComponent` and interaction script when `InteractionId` is specified:

```csharp
// In CreateNpcEntity method, after creating the NPC entity:

if (!string.IsNullOrWhiteSpace(npcDef.InteractionId))
{
    // Validate ScriptDefinition exists
    var interactionScriptDef = _registry.GetById<Mods.Definitions.ScriptDefinition>(
        npcDef.InteractionId
    );
    if (interactionScriptDef == null)
    {
        throw new InvalidOperationException(
            $"ScriptDefinition '{npcDef.InteractionId}' not found for NPC interaction '{npcDef.NpcId}'. " +
            "Ensure the script definition exists and is loaded."
        );
    }

    // Add InteractionComponent to NPC for unified interaction handling
    // NPCs use InteractionComponent just like map interactions
    World.Add(
        npcEntity,
        new InteractionComponent
        {
            InteractionId = npcDef.NpcId, // Use NPC ID as interaction identifier
            ScriptDefinitionId = interactionScriptDef.Id,
            Width = 1, // Default: 1 tile for NPCs
            Height = 1, // Default: 1 tile for NPCs
            Elevation = npcDef.Elevation,
            RequiredFacing = null, // NPCs can be interacted with from any direction
            IsEnabled = true,
        }
    );

    // Attach interaction script as a second ScriptAttachmentComponent
    // This allows NPCs to have both behavior scripts and interaction scripts
    var interactionScriptMetadata = _registry.GetById(interactionScriptDef.Id);
    if (interactionScriptMetadata == null)
    {
        throw new InvalidOperationException(
            $"Script definition metadata not found for interaction script {interactionScriptDef.Id} on NPC {npcDef.NpcId}."
        );
    }
    var interactionModId = interactionScriptMetadata.OriginalModId;

    World.Add(
        npcEntity,
        new ScriptAttachmentComponent
        {
            ScriptDefinitionId = interactionScriptDef.Id,
            Priority = interactionScriptDef.Priority,
            IsActive = true,
            ModId = interactionModId,
            IsInitialized = false,
        }
    );

    _logger.Debug(
        "Added InteractionComponent and interaction script {ScriptId} to NPC {NpcId}",
        interactionScriptDef.Id,
        npcDef.NpcId
    );
}
```

### 5. Interaction System Design

Create an `InteractionSystem` to handle player interactions with map objects:

```csharp
namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that handles player interactions with map objects (signs, objects, etc.).
    /// Detects when player presses interact button near an interaction entity and triggers the script.
    /// </summary>
    public class InteractionSystem : BaseSystem<World, float>
    {
        private readonly QueryDescription _interactionQuery;
        private readonly QueryDescription _playerQuery;
        private readonly QueryDescription _scriptAttachmentQuery;
        private readonly IInputBindingService _inputBindingService;
        private readonly DefinitionRegistry _registry;
        private readonly ILogger _logger;
        
        // Track active interactions to prevent duplicate events
        private readonly HashSet<Entity> _activeInteractions = new();

        public InteractionSystem(
            World world,
            IInputBindingService inputBindingService,
            DefinitionRegistry registry,
            ILogger logger
        ) : base(world)
        {
            _inputBindingService = inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Unified query: both map interactions and NPCs use InteractionComponent
            _interactionQuery = new QueryDescription()
                .WithAll<InteractionComponent, PositionComponent>();
            
            _playerQuery = new QueryDescription()
                .WithAll<PlayerComponent, PositionComponent, GridMovement>();
            
            // Cached query for script pausing/resuming
            _scriptAttachmentQuery = new QueryDescription()
                .WithAll<ScriptAttachmentComponent>();
            
            // Subscribe to events
            EventBus.Subscribe<InteractionEndedEvent>(OnInteractionEnded);
            EventBus.Subscribe<MessageBoxClosedEvent>(OnMessageBoxClosed);
        }

        public override void Update(in float deltaTime)
        {
            // Check if interact button was just pressed (edge-triggered, not level-triggered)
            bool interactJustPressed = _inputBindingService.IsActionJustPressed(InputAction.Interact);
            if (!interactJustPressed)
            {
                return; // Player didn't press interact button this frame
            }

            // Get player entity and tile position
            Entity? playerEntity = null;
            int playerTileX = 0;
            int playerTileY = 0;
            Direction playerFacing = Direction.Down;

            World.Query(in _playerQuery, (ref PlayerComponent player, ref PositionComponent pos, ref GridMovement movement) =>
            {
                playerEntity = World.Reference(player);
                // Use tile coordinates directly from PositionComponent
                playerTileX = pos.X;
                playerTileY = pos.Y;
                playerFacing = movement.FacingDirection;
            });

            if (playerEntity == null)
            {
                return; // No player, nothing to do
            }

            // Check if player is already in an interaction
            if (World.Has<InteractionStateComponent>(playerEntity.Value))
            {
                return; // Player is already interacting with something
            }

            // Check for interactions (unified: both map interactions and NPCs use InteractionComponent)
            World.Query(in _interactionQuery, (ref InteractionComponent interaction, ref PositionComponent pos) =>
            {
                if (!interaction.IsEnabled)
                {
                    return; // Interaction disabled
                }

                // Check if this interaction is already active
                Entity interactionEntity = World.Reference(interaction);
                if (_activeInteractions.Contains(interactionEntity))
                {
                    return; // Already interacting with this
                }

                // Check if player is within interaction range using TILE-BASED distance
                // In Pokemon Emerald, interactions work when player is on an adjacent tile (1 tile away)
                // This is hardcoded in the game logic, not defined in map data
                // The width/height define the interaction hitbox, but interaction distance is always 1 tile
                
                // Use tile coordinates directly from PositionComponent
                // PositionComponent stores both tile (X, Y) and pixel (PixelX, PixelY) coordinates
                int interactionTileX = pos.X;
                int interactionTileY = pos.Y;
                
                // Calculate tile distance using Manhattan distance for 4-directional movement
                // Pokemon games only support 4-directional movement (up, down, left, right), not diagonals
                int tileDistanceX = Math.Abs(playerTileX - interactionTileX);
                int tileDistanceY = Math.Abs(playerTileY - interactionTileY);
                int tileDistance = tileDistanceX + tileDistanceY; // Manhattan distance
                
                // Interaction range: 1 tile (player must be adjacent in one of 4 cardinal directions)
                // Manhattan distance == 1 means: adjacent orthogonally (not diagonal)
                const int interactionRangeTiles = 1;
                
                if (tileDistance > interactionRangeTiles)
                {
                    return; // Too far away
                }

                // Check facing requirement if specified
                if (!string.IsNullOrEmpty(interaction.RequiredFacing))
                {
                    Direction requiredDirection = ParseDirection(interaction.RequiredFacing);
                    if (playerFacing != requiredDirection)
                    {
                        return; // Wrong facing direction
                    }
                }

                // Trigger interaction event (works for both map interactions and NPCs)
                TriggerInteraction(interactionEntity, playerEntity.Value, interaction.InteractionId, interaction.ScriptDefinitionId);
            });
        }

        /// <summary>
        /// Triggers an interaction event and handles behavior script pausing.
        /// </summary>
        private void TriggerInteraction(Entity interactionEntity, Entity playerEntity, string interactionId, string? scriptDefinitionId)
        {
            // Mark as active to prevent duplicate events
            _activeInteractions.Add(interactionEntity);

            // Pause behavior script if this is an NPC interaction
            if (World.Has<NpcComponent>(interactionEntity))
            {
                var npcComponent = World.Get<NpcComponent>(interactionEntity);
                
                // Get behavior script ID from NPC's behavior definition
                if (!string.IsNullOrEmpty(npcComponent.BehaviorId))
                {
                    var behaviorDef = _registry.GetById<BehaviorDefinition>(npcComponent.BehaviorId);
                    if (behaviorDef != null && !string.IsNullOrEmpty(behaviorDef.ScriptId))
                    {
                        PauseScript(interactionEntity, behaviorDef.ScriptId);
                        _logger.Debug(
                            "Paused behavior script {ScriptId} for NPC {NpcId} during interaction",
                            behaviorDef.ScriptId,
                            npcComponent.NpcId
                        );
                    }
                }

                // Add InteractionStateComponent to track this interaction
                World.Add(playerEntity, new InteractionStateComponent
                {
                    BehaviorScriptId = npcComponent.BehaviorId,
                    InteractionEntity = interactionEntity,
                    PlayerEntity = playerEntity
                });
            }

            // Fire InteractionTriggeredEvent
            var interactionEvent = new InteractionTriggeredEvent
            {
                InteractionEntity = interactionEntity,
                PlayerEntity = playerEntity,
                InteractionId = interactionId,
                ScriptDefinitionId = scriptDefinitionId,
            };
            EventBus.Send(ref interactionEvent);

            // Fire InteractionStartedEvent
            var startedEvent = new InteractionStartedEvent
            {
                InteractionEntity = interactionEntity,
                PlayerEntity = playerEntity,
                InteractionId = interactionId,
            };
            EventBus.Send(ref startedEvent);

            _logger.Debug(
                "Interaction triggered: {InteractionId} by player",
                interactionId
            );
        }

        /// <summary>
        /// Handles interaction end event.
        /// </summary>
        private void OnInteractionEnded(InteractionEndedEvent evt)
        {
            _activeInteractions.Remove(evt.InteractionEntity);
        }

        /// <summary>
        /// Handles message box closed event - resumes behavior scripts.
        /// </summary>
        private void OnMessageBoxClosed(MessageBoxClosedEvent evt)
        {
            // Find player entity with InteractionStateComponent
            World.Query(
                new QueryDescription().WithAll<PlayerComponent, InteractionStateComponent>(),
                (Entity e, ref PlayerComponent player, ref InteractionStateComponent state) =>
                {
                    // Resume behavior script
                    if (!string.IsNullOrEmpty(state.BehaviorScriptId))
                    {
                        var behaviorDef = _registry.GetById<BehaviorDefinition>(state.BehaviorScriptId);
                        if (behaviorDef != null && !string.IsNullOrEmpty(behaviorDef.ScriptId))
                        {
                            ResumeScript(state.InteractionEntity, behaviorDef.ScriptId);
                            _logger.Debug(
                                "Resumed behavior script {ScriptId} for NPC after interaction",
                                behaviorDef.ScriptId
                            );
                        }
                    }

                    // Fire InteractionEndedEvent
                    var endedEvent = new InteractionEndedEvent
                    {
                        InteractionEntity = state.InteractionEntity,
                        PlayerEntity = state.PlayerEntity,
                        InteractionId = string.Empty, // Can be retrieved from entity if needed
                    };
                    EventBus.Send(ref endedEvent);

                    // Remove state component
                    World.Remove<InteractionStateComponent>(e);
                    
                    // Remove from active interactions
                    _activeInteractions.Remove(state.InteractionEntity);
                }
            );
        }

        private Direction ParseDirection(string direction)
        {
            return direction.ToLowerInvariant() switch
            {
                "up" => Direction.Up,
                "down" => Direction.Down,
                "left" => Direction.Left,
                "right" => Direction.Right,
                _ => Direction.Down,
            };
        }
    }
}
```

### 6. Event Design

Create `InteractionTriggeredEvent`:

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when a player interacts with a map interaction object (sign, object, etc.).
    /// Scripts attached to the interaction entity can subscribe to this event to handle the interaction.
    /// </summary>
    public struct InteractionTriggeredEvent
    {
        /// <summary>
        /// The entity representing the interaction object.
        /// </summary>
        public Entity InteractionEntity { get; set; }

        /// <summary>
        /// The player entity that triggered the interaction.
        /// </summary>
        public Entity PlayerEntity { get; set; }

        /// <summary>
        /// The unique identifier for this interaction.
        /// </summary>
        public string InteractionId { get; set; }

        /// <summary>
        /// The script definition ID that should handle this interaction.
        /// </summary>
        public string? ScriptDefinitionId { get; set; }
    }
}
```

**New Events:**

```csharp
namespace MonoBall.Core.ECS.Events
{
    /// <summary>
    /// Event fired when an interaction starts (after behavior scripts are paused).
    /// </summary>
    public struct InteractionStartedEvent
    {
        /// <summary>
        /// The entity representing the interaction object.
        /// </summary>
        public Entity InteractionEntity { get; set; }

        /// <summary>
        /// The player entity that triggered the interaction.
        /// </summary>
        public Entity PlayerEntity { get; set; }

        /// <summary>
        /// The unique identifier for this interaction.
        /// </summary>
        public string InteractionId { get; set; }
    }

    /// <summary>
    /// Event fired when an interaction ends (before behavior scripts are resumed).
    /// </summary>
    public struct InteractionEndedEvent
    {
        /// <summary>
        /// The entity representing the interaction object.
        /// </summary>
        public Entity InteractionEntity { get; set; }

        /// <summary>
        /// The player entity that triggered the interaction.
        /// </summary>
        public Entity PlayerEntity { get; set; }

        /// <summary>
        /// The unique identifier for this interaction.
        /// </summary>
        public string InteractionId { get; set; }
    }

    /// <summary>
    /// Event fired when a message box closes.
    /// Used by InteractionSystem to resume behavior scripts.
    /// </summary>
    public struct MessageBoxClosedEvent
    {
        /// <summary>
        /// The message box entity that was closed.
        /// </summary>
        public Entity MessageBoxEntity { get; set; }
    }
}
```

---

## Integration with Scripting System

### Script Helper Methods

`ScriptBase` provides several helper methods to simplify interaction script writing. These methods follow the established API design principles:
- **ScriptBase**: Entity-specific convenience methods (operates on script's own entity)
- **APIs**: Cross-entity operations and game system access (via `Context.Apis.*`)

#### Interaction Event Handling (ScriptBase)

- **`OnInteraction(Action<InteractionTriggeredEvent>)`** - Subscribes to `InteractionTriggeredEvent` with automatic entity filtering. No need to manually check `IsEventForThisEntity()`.

#### Player Interaction (ScriptBase - Delegates to API)

- **`FacePlayer(Entity playerEntity)`** - Makes this entity face toward the player entity. Convenience wrapper that delegates to `Context.Apis.Npc.FaceEntity()`.

#### Position Access (ScriptBase - Own Entity Only)

- **`GetTilePosition()`** - Gets the tile position of this script's entity as `(int X, int Y)?`. For other entities, use `Context.Apis.Npc.GetPosition(entity)`.

#### Interaction State Tracking (ScriptBase)

- **`GetInteractionCount()`** - Gets the number of times this entity has been interacted with.
- **`IncrementInteractionCount()`** - Increments and returns the interaction count for this entity.

#### Dialogue Display (ScriptBase - Combines Operations)

- **`ShowDialogueByCount(string first, string second, string default)`** - Shows dialogue based on interaction count. Automatically increments the count and selects the appropriate message. Combines state tracking with message display.

#### API Methods (Cross-Entity Operations)

For cross-entity operations, use the APIs directly:

- **`Context.Apis.Player.GetPlayerEntity()`** - Gets the player entity from the world (to be added to `IPlayerApi`).
- **`Context.Apis.Npc.FaceEntity(Entity npc, Entity target)`** - Makes an NPC face toward another entity.
- **`Context.Apis.Npc.GetPosition(Entity npc)`** - Gets the position of any entity.
- **`Context.Apis.MessageBox.ShowMessage(string message)`** - Shows a message box.

**Example Usage:**

```csharp
public override void RegisterEventHandlers(ScriptContext context)
{
    // Automatic entity filtering (ScriptBase helper)
    OnInteraction(OnInteractionTriggered);
}

private void OnInteractionTriggered(InteractionTriggeredEvent evt)
{
    // Face player (ScriptBase convenience wrapper - delegates to API)
    FacePlayer(evt.PlayerEntity);
    
    // Get player entity (API - cross-entity operation)
    var player = Context.Apis.Player.GetPlayerEntity();
    
    // Show dialogue based on count (ScriptBase - combines state + API call)
    ShowDialogueByCount(
        "First time talking!",
        "Second time talking!",
        "Default message"
    );
    
    // Or show simple message (API - game system operation)
    Context.Apis.MessageBox.ShowMessage("Hello!");
}
```

**API Design Compliance:**

- ✅ **ScriptBase methods**: Operate on script's own entity, convenience wrappers, reduce boilerplate
- ✅ **API methods**: Cross-entity operations, game system access, stateless
- ✅ **Delegation**: ScriptBase convenience methods delegate to APIs when appropriate

See `MonoBall/docs/design/interaction-script-api-improvements.md` for detailed API documentation and `MonoBall/docs/design/interaction-id-api-location-analysis.md` for API location analysis.

### Script Lifecycle

1. **Map Load**: `MapLoaderSystem` creates interaction entities with `InteractionComponent` and `ScriptAttachmentComponent`
2. **Script Initialization**: `ScriptLifecycleSystem` detects `ScriptAttachmentComponent` and initializes scripts
3. **Event Subscription**: Interaction scripts subscribe to `InteractionTriggeredEvent` in their `RegisterEventHandlers()` method
4. **Interaction Trigger**: `InteractionSystem` detects player interaction and fires `InteractionTriggeredEvent`
5. **Behavior Pause**: When interaction starts, behavior scripts are paused (see "Behavior Script Pausing" below)
6. **Script Execution**: Scripts handle the event and execute interaction logic (show message box, give item, etc.)
7. **Behavior Resume**: When interaction ends (message box closes), behavior scripts are resumed

### Behavior Script Pausing

In Pokemon Emerald, when a player interacts with an NPC, the NPC's behavior script (movement) is paused during the interaction. This is implemented as follows:

#### Problem
- NPCs have two scripts: behavior script (movement) and interaction script (dialogue)
- When interaction starts, behavior script should pause
- When interaction ends, behavior script should resume
- Both scripts are attached to the same entity via `ScriptAttachmentComponent`

#### Solution: Script Pausing via `IsActive` Flag

The `ScriptAttachmentComponent` has an `IsActive` property that controls whether a script is executed. When `IsActive = false`, `ScriptLifecycleSystem` skips the script (line 84-87).

**Implementation Approach:**

1. **Identify Behavior Script**: Behavior scripts are identified by their `ScriptDefinitionId` matching the NPC's `BehaviorId` → `BehaviorDefinition.ScriptId` chain.

2. **Pause on Interaction Start**: When `InteractionTriggeredEvent` is fired:
   - Find the behavior script's `ScriptAttachmentComponent` on the entity
   - Set `IsActive = false` to pause it
   - Fire `InteractionStartedEvent` to notify scripts

3. **Resume on Interaction End**: When message box closes (or interaction completes):
   - Find the behavior script's `ScriptAttachmentComponent` on the entity
   - Set `IsActive = true` to resume it
   - Fire `InteractionEndedEvent` to notify scripts

**Helper Method for Script Pausing:**

```csharp
/// <summary>
/// Pauses a script by setting its ScriptAttachmentComponent.IsActive to false.
/// Uses cached QueryDescription for performance.
/// </summary>
/// <param name="entity">The entity that has the script.</param>
/// <param name="scriptDefinitionId">The script definition ID to pause.</param>
/// <returns>True if the script was found and paused, false otherwise.</returns>
private bool PauseScript(Entity entity, string scriptDefinitionId)
{
    if (!World.Has<ScriptAttachmentComponent>(entity))
    {
        return false;
    }

    // Query all ScriptAttachmentComponents (Arch ECS allows multiple of same type)
    // Filter by entity ID and script definition ID
    bool found = false;
    World.Query(
        in _scriptAttachmentQuery,
        (Entity e, ref ScriptAttachmentComponent attachment) =>
        {
            // Early exit if not our entity
            if (e.Id != entity.Id)
            {
                return;
            }

            if (attachment.ScriptDefinitionId == scriptDefinitionId)
            {
                attachment.IsActive = false;
                World.Set(e, attachment);
                found = true;
            }
        }
    );

    return found;
}

/// <summary>
/// Resumes a script by setting its ScriptAttachmentComponent.IsActive to true.
/// Uses cached QueryDescription for performance.
/// </summary>
/// <param name="entity">The entity that has the script.</param>
/// <param name="scriptDefinitionId">The script definition ID to resume.</param>
/// <returns>True if the script was found and resumed, false otherwise.</returns>
private bool ResumeScript(Entity entity, string scriptDefinitionId)
{
    if (!World.Has<ScriptAttachmentComponent>(entity))
    {
        return false;
    }

    // Query all ScriptAttachmentComponents (Arch ECS allows multiple of same type)
    // Filter by entity ID and script definition ID
    bool found = false;
    World.Query(
        in _scriptAttachmentQuery,
        (Entity e, ref ScriptAttachmentComponent attachment) =>
        {
            // Early exit if not our entity
            if (e.Id != entity.Id)
            {
                return;
            }

            if (attachment.ScriptDefinitionId == scriptDefinitionId)
            {
                attachment.IsActive = true;
                World.Set(e, attachment);
                found = true;
            }
        }
    );

    return found;
}
```

**Note:** The interaction triggering and state management is now handled in the `Update()` method and helper methods shown above. The `TriggerInteraction()` method handles pausing behavior scripts and adding `InteractionStateComponent`, while `OnMessageBoxClosed()` handles resuming behavior scripts and cleanup.

**Alternative: Event-Based Pausing**

Behavior scripts can also listen for `InteractionStartedEvent` and pause themselves:

```csharp
// In behavior script (e.g., wander_behavior.csx)
public override void RegisterEventHandlers(ScriptContext context)
{
    On<MovementCompletedEvent>(OnMovementCompleted);
    On<InteractionStartedEvent>(OnInteractionStarted); // Pause when interaction starts
    On<InteractionEndedEvent>(OnInteractionEnded);     // Resume when interaction ends
}

private void OnInteractionStarted(InteractionStartedEvent evt)
{
    if (!IsEventForThisEntity(evt))
    {
        return;
    }

    // Pause this script by setting IsActive = false
    // Note: Scripts cannot directly modify their own ScriptAttachmentComponent
    // This requires a helper API or system-level support
    Context.Apis.Script.SetScriptActive(Context.Entity.Value, Context.ScriptDefinitionId, false);
}

private void OnInteractionEnded(InteractionEndedEvent evt)
{
    if (!IsEventForThisEntity(evt))
    {
        return;
    }

    // Resume this script
    Context.Apis.Script.SetScriptActive(Context.Entity.Value, Context.ScriptDefinitionId, true);
}
```

**Recommended Approach:**

Use **system-level pausing** (first approach) because:
1. More reliable - doesn't depend on behavior scripts implementing pause logic
2. Centralized - all pausing logic in one place
3. Works with any behavior script - no need to modify existing scripts
4. Clear separation of concerns - interaction system handles pausing, behavior scripts just run

**Tracking Interaction State:**

We use **component-based tracking** for ECS consistency and automatic cleanup when entities are destroyed:

```csharp
namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that tracks an active interaction state.
    /// Added to the player entity when an interaction starts, removed when it ends.
    /// Used to resume behavior scripts when message boxes close.
    /// </summary>
    public struct InteractionStateComponent
    {
        /// <summary>
        /// The behavior script ID that was paused for this interaction.
        /// Used to resume the script when interaction ends.
        /// </summary>
        public string BehaviorScriptId { get; set; }

        /// <summary>
        /// The interaction entity (NPC or map interaction).
        /// </summary>
        public Entity InteractionEntity { get; set; }

        /// <summary>
        /// The player entity that triggered this interaction.
        /// </summary>
        public Entity PlayerEntity { get; set; }
    }
}
```

**Usage:**
- Added to player entity when interaction starts (in `TriggerInteraction()`)
- Removed when message box closes (in `OnMessageBoxClosed()`)
- Automatically cleaned up when player entity is destroyed (Arch ECS handles this)

**New Events:**

```csharp
/// <summary>
/// Event fired when an interaction starts (after behavior scripts are paused).
/// </summary>
public struct InteractionStartedEvent
{
    public Entity InteractionEntity { get; set; }
    public Entity PlayerEntity { get; set; }
    public string InteractionId { get; set; }
}

/// <summary>
/// Event fired when an interaction ends (before behavior scripts are resumed).
/// </summary>
public struct InteractionEndedEvent
{
    public Entity InteractionEntity { get; set; }
    public Entity PlayerEntity { get; set; }
    public string InteractionId { get; set; }
}

/// <summary>
/// Event fired when a message box closes.
/// Used by InteractionSystem to resume behavior scripts.
/// </summary>
public struct MessageBoxClosedEvent
{
    /// <summary>
    /// The message box entity that was closed.
    /// </summary>
    public Entity MessageBoxEntity { get; set; }
}
```

### Example Interaction Scripts

#### Map Interaction Script (Sign)

**Simplified Version (Recommended):**

```csharp
// Script: base:script:hoenn/Interactions/LittlerootTown_EventScript_TownSign.csx
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.Scripting.Api;

/// <summary>
/// Interaction script for the Littleroot Town sign.
/// Shows a message when the player interacts with the town sign.
/// </summary>
public class LittlerootTownSignScript : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // OnInteraction() automatically filters by entity - no need for IsEventForThisEntity() check
        OnInteraction(OnInteractionTriggered);
    }

    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Show message box with town sign text
        // Use API directly for game system operations
        Context.Apis.MessageBox.ShowMessage("LITTLEROOT TOWN\n\"The town where the\nadventure begins.\"");
    }
}
```

**Alternative (Manual Entity Check):**

```csharp
// If you need more control, you can still use the manual approach:
public override void RegisterEventHandlers(ScriptContext context)
{
    On<InteractionTriggeredEvent>(OnInteractionTriggered);
}

private void OnInteractionTriggered(InteractionTriggeredEvent evt)
{
    if (!IsEventForThisEntity(evt))
    {
        return;
    }
    Context.Apis.MessageBox.ShowMessage("LITTLEROOT TOWN\n\"The town where the\nadventure begins.\"");
}
```

#### NPC Interaction Script (Twin)

**Simplified Version (Recommended):**

```csharp
// Script: base:script:hoenn/Interactions/LittlerootTown_EventScript_Twin.csx
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.Scripting.Api;

/// <summary>
/// Interaction script for the twin NPC in Littleroot Town.
/// Shows dialogue when the player interacts with the twin.
/// Note: Behavior script is automatically paused by InteractionSystem when interaction starts.
/// </summary>
public class LittlerootTownTwinScript : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        // OnInteraction() automatically filters by entity
        OnInteraction(OnInteractionTriggered);
        On<InteractionStartedEvent>(OnInteractionStarted);
    }

    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        // Behavior script is automatically paused by InteractionSystem
        // before InteractionStartedEvent is fired
    }

    private void OnInteractionStarted(InteractionStartedEvent evt)
    {
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        // Face the player when interacting (ScriptBase convenience wrapper)
        // Delegates to Context.Apis.Npc.FaceEntity() internally
        FacePlayer(evt.PlayerEntity);

        // Show dialogue based on interaction count (ScriptBase helper)
        // Combines IncrementInteractionCount() + Context.Apis.MessageBox.ShowMessage()
        ShowDialogueByCount(
            "Hi! We're twins!\nWe just moved here!",           // First interaction
            "This is LITTLEROOT TOWN.\nIt's a nice place!",    // Second interaction
            "Have fun exploring!"                              // Default for 3+
        );
    }
}
```

**Alternative (Manual Implementation):**

```csharp
// If you need more control, you can use the manual approach:
public class LittlerootTownTwinScript : ScriptBase
{
    private const string InteractionCountKey = "interactionCount";

    public override void RegisterEventHandlers(ScriptContext context)
    {
        On<InteractionTriggeredEvent>(OnInteractionTriggered);
        On<InteractionStartedEvent>(OnInteractionStarted);
    }

    private void OnInteractionTriggered(InteractionTriggeredEvent evt)
    {
        if (!IsEventForThisEntity(evt))
        {
            return;
        }
    }

    private void OnInteractionStarted(InteractionStartedEvent evt)
    {
        if (!IsEventForThisEntity(evt))
        {
            return;
        }

        // Manual interaction count tracking
        var interactionCount = Get<int>(InteractionCountKey, 0);
        interactionCount++;
        Set(InteractionCountKey, interactionCount);

        // Manual player facing using API
        Context.Apis.Npc.FaceEntity(Context.Entity.Value, evt.PlayerEntity);

        // Manual dialogue selection
        string message;
        if (interactionCount == 1)
        {
            message = "Hi! We're twins!\nWe just moved here!";
        }
        else if (interactionCount == 2)
        {
            message = "This is LITTLEROOT TOWN.\nIt's a nice place!";
        }
        else
        {
            message = "Have fun exploring!";
        }

        Context.Apis.MessageBox.ShowMessage(message);
    }
}
```

**Key Points:**
- Behavior script is automatically paused by `InteractionSystem` when interaction starts
- Behavior script is automatically resumed by `InteractionSystem` when message box closes
- Interaction script doesn't need to manage behavior script pausing/resuming
- Script can optionally face the player during interaction using `FacePlayer()` (delegates to API)
- Script can track interaction state (count, flags, etc.) using `IncrementInteractionCount()`
- Uses `OnInteraction()` helper for automatic entity filtering (no manual `IsEventForThisEntity()` check needed)
- Uses `ShowDialogueByCount()` convenience method (combines state tracking + API call)
- Uses APIs directly for cross-entity operations (`Context.Apis.MessageBox.ShowMessage()`, etc.)
- Simplified from ~80 lines to ~25 lines with helper methods
- Follows Pokemon Emerald dialogue style
- **Compliant with API design principles**: ScriptBase for entity-specific convenience, APIs for cross-entity operations

**Available Helper Methods in ScriptBase:**

The following helper methods are available to simplify interaction script writing (entity-specific convenience):

- **`OnInteraction(Action<InteractionTriggeredEvent>)`** - Subscribes to interaction events with automatic entity filtering
- **`FacePlayer(Entity playerEntity)`** - Makes this entity face toward the player (delegates to `Context.Apis.Npc.FaceEntity()`)
- **`ShowDialogueByCount(string first, string second, string default)`** - Shows dialogue based on interaction count (auto-increments, combines state + API call)
- **`IncrementInteractionCount()`** - Increments and returns interaction count for this entity
- **`GetInteractionCount()`** - Gets the current interaction count for this entity
- **`GetTilePosition()`** - Gets tile position of this entity as `(int X, int Y)?` (for other entities, use API)

**Available API Methods (Cross-Entity Operations):**

- **`Context.Apis.Player.GetPlayerEntity()`** - Gets the player entity from the world (to be added to `IPlayerApi`)
- **`Context.Apis.Npc.FaceEntity(Entity npc, Entity target)`** - Makes an NPC face toward another entity
- **`Context.Apis.Npc.GetPosition(Entity npc)`** - Gets the position of any entity
- **`Context.Apis.MessageBox.ShowMessage(string message)`** - Shows a message box

See `MonoBall/docs/design/interaction-script-api-improvements.md` for detailed API documentation and `MonoBall/docs/design/interaction-id-api-location-analysis.md` for API location compliance analysis.

---

## Implementation Notes

### No Backward Compatibility

This design follows the project's "NO BACKWARD COMPATIBILITY" rule:
- `interactionScript` is removed immediately - no migration period
- All JSON files must use `interactionId` (for NPCs) or `scriptDefinitionId` (for interactions)
- Code fails fast with clear exceptions if old property names are used

### Converter Updates

Update `porycon2` converter (`porycon2/porycon/definition_converter.py`) to:
- Output `scriptDefinitionId` instead of `interactionScript` in the interactions array
- Output `interactionId` instead of `interactionScript` for NPCs
- Remove `.csx` extension from script IDs when converting
- Remove all fallback code that checks for `interactionScript`
- Ensure script IDs follow the format: `base:script:{region}/{category}/{script_name}`

**Note**: The current `porycon2` converter outputs `interactionId` for the script reference in interactions (see `definition_converter.py` line 556). This should be changed to `scriptDefinitionId` to avoid confusion with the interaction entity's unique identifier.

### JSON File Updates

All map JSON files must be updated:
- **Interactions array**: Change `"interactionScript"` → `"scriptDefinitionId"`, remove `.csx` extensions
- **NPCs array**: Change `"interactionScript"` → `"interactionId"`, remove `.csx` extensions if present

---

## ID Format Standards

### Interaction Entity IDs

Format: `base:interaction:{region}/{map}/{name}`

Example: `base:interaction:hoenn/littleroot_town/sign_littleroottown_eventscript_townsign`

### Script Definition IDs

Format: `base:script:{region}/{category}/{script_name}`

Examples:
- `base:script:hoenn/Interactions/LittlerootTown_EventScript_TownSign`
- `base:script:hoenn/Interactions/LittlerootTown_EventScript_BirchsLabSign`
- `base:script:hoenn/Interactions/LittlerootTown_EventScript_Twin`

---

## Interaction Distance

### Pokemon Emerald Behavior

In Pokemon Emerald (and pokeemerald-expansion), **interactions do NOT define an explicit interaction distance in the map files**. Instead:

1. **Interaction Distance is Hardcoded**: The game logic uses a fixed interaction distance of **1 tile** - player must be on an adjacent tile to interact.

2. **Width/Height Define Hitbox**: The `width` and `height` fields in `InteractionComponent` define the interaction hitbox area in tiles (typically 1x1 tile), but the actual interaction distance is always 1 tile adjacent.

3. **Facing Requirement**: The `player_facing_dir` field (mapped to `facing` in our system) optionally requires the player to face a specific direction.

### Our Implementation

Our `InteractionSystem` should match this behavior:
- **Default Interaction Distance**: 1 tile adjacent to the interaction area
- **Calculation**: Use **tile-based distance** (Chebyshev distance), not pixel distance
- **No Distance Field Needed**: We don't need an `interactionDistance` field in `InteractionComponent` because it's always 1 tile

**Tile-Based Distance Calculation:**
- Use tile coordinates directly from `PositionComponent` (X, Y properties)
- Use **Manhattan distance**: `|playerTileX - interactionTileX| + |playerTileY - interactionTileY|`
- Interaction range: 1 tile (player must be adjacent in one of 4 cardinal directions)
- **No diagonal interactions**: Pokemon games only support 4-directional movement (up, down, left, right)

**Example Calculation (Tile-Based, 4-Directional):**
- Interaction at tile (6, 6)
- Player at tile (5, 6) → Manhattan distance = |5-6| + |6-6| = 1 + 0 = 1 → **Can interact** (adjacent, left)
- Player at tile (7, 6) → Manhattan distance = |7-6| + |6-6| = 1 + 0 = 1 → **Can interact** (adjacent, right)
- Player at tile (6, 5) → Manhattan distance = |6-6| + |5-6| = 0 + 1 = 1 → **Can interact** (adjacent, up)
- Player at tile (6, 7) → Manhattan distance = |6-6| + |7-6| = 0 + 1 = 1 → **Can interact** (adjacent, down)
- Player at tile (4, 6) → Manhattan distance = |4-6| + |6-6| = 2 + 0 = 2 → **Cannot interact** (too far)
- Player at tile (7, 7) → Manhattan distance = |7-6| + |7-6| = 1 + 1 = 2 → **Cannot interact** (diagonal, not allowed)
- Player at tile (6, 6) → Manhattan distance = |6-6| + |6-6| = 0 + 0 = 0 → **Can interact** (same tile)

**Why Tile-Based:**
- Matches Pokemon Emerald's tile-based movement system
- More consistent with grid-based game logic
- Avoids floating-point precision issues
- Easier to reason about and debug

## Validation Rules

1. **Required Fields**: For NPCs with `InteractionId`, the `InteractionId` must reference an existing `ScriptDefinition` in the registry
2. **Script Existence**: `ScriptDefinitionId` in `InteractionComponent` must reference an existing `ScriptDefinition` in the registry
3. **Size Validation**: Width and height in `InteractionComponent` must be positive
4. **Facing Validation**: If specified, `RequiredFacing` must be one of: "up", "down", "left", "right"

---

## Error Handling

All validation failures throw `InvalidOperationException` with clear messages:

```csharp
throw new InvalidOperationException(
    $"ScriptDefinition '{npcDef.InteractionId}' not found for NPC interaction '{npcDef.NpcId}'. " +
    "Ensure the script definition exists and is loaded."
);
```

No fallback code - fail fast with clear errors.

---

## Testing Considerations

1. **Unit Tests**: Test `NpcDefinition` deserialization with `interactionId`
2. **Integration Tests**: Test NPC creation with interaction components
3. **Script Tests**: Test interaction scripts handle `InteractionTriggeredEvent` correctly
4. **Validation Tests**: Test all validation rules throw appropriate exceptions

---

## Future Enhancements

1. **Interaction Types**: Support different interaction types (sign, object, NPC, etc.) with type-specific behavior
2. **Conditional Interactions**: Support interactions that only trigger under certain conditions (flags, variables)
3. **Interaction Groups**: Support grouping interactions for batch operations
4. **Visual Feedback**: Show interaction hints when player is near interactable objects

---

## Summary

The `interactionId` system provides:

1. **Type Safety**: References to `ScriptDefinition` objects instead of string paths
2. **Validation**: Runtime validation that scripts exist
3. **Integration**: Seamless integration with existing scripting system
4. **Consistency**: Unified approach for entity interactions (NPCs, objects, etc.)
5. **Maintainability**: Clear separation of concerns and fail-fast error handling

This design aligns with the project's architecture principles:
- **No Backward Compatibility**: Clean break from `interactionScript` to `interactionId`
- **No Fallback Code**: Fail fast with clear exceptions
- **No Unnecessary Abstractions**: Properties read directly from JSON, mapped to components - no intermediate definition class
- **ECS Architecture**: Uses components and systems appropriately
- **Definition-Based**: References definitions via registry
- **Event-Driven**: Uses events for decoupled communication

