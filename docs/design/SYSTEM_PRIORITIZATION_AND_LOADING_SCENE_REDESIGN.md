# System Prioritization and Loading Scene Redesign

## Executive Summary

This document analyzes the current system prioritization/handling and loading scene architecture, identifies inefficiencies compared to oldmonoball, and proposes a redesigned architecture following .cursorrules and Arch ECS best practices.

## Current Architecture Analysis

### System Prioritization Issues

**Current Implementation:**
- Systems are hardcoded in a list within `SystemManager.Initialize()` (lines 641-718)
- Order is implicit based on list position, not explicit priority values
- Duplicate code paths for shader vs non-shader scenarios
- No way to query or modify system priorities without code changes
- Systems cannot declare their own priority

**Problems:**
1. **Maintainability**: Adding/removing/reordering systems requires modifying SystemManager
2. **Testability**: Cannot easily test different system orderings
3. **Modding**: Mods cannot register systems with custom priorities
4. **Clarity**: Priority dependencies are implicit (comments only)
5. **DRY Violation**: Duplicate system list creation logic

### Loading Scene Issues

**Current Implementation:**
- Early loading scene system created before SystemManager (`_earlyLoadingSceneSystem`)
- Regular loading scene system created in SystemManager (`_loadingSceneSystem`)
- Progress updates processed in `MonoBallGame.Update()` via queue
- `LoadingSceneSystem.Update()` is empty (doesn't process progress)
- Loading scene blocking logic checked in `SceneRendererSystem`, not in `LoadingSceneSystem`

**Problems:**
1. **Separation of Concerns**: Progress processing happens outside the system
2. **Duplication**: Two loading scene systems (early vs regular)
3. **Event Handling**: Progress updates use queue instead of events
4. **System Responsibility**: LoadingSceneSystem doesn't own its update logic

## Oldmonoball Architecture (Reference)

### System Prioritization

**Strengths:**
- `SystemPriority` constants class with explicit priority values
- `IUpdateSystem` and `IRenderSystem` interfaces with `Priority` property
- `SystemManager.RegisterUpdateSystem()` - systems register themselves
- Automatic sorting by priority on registration
- Clear separation: update systems vs render systems vs event-driven systems

**Key Files:**
- `SystemPriority.cs` - Priority constants (0-1000 range)
- `IUpdateSystem.cs` - Interface with `Priority` property
- `IRenderSystem.cs` - Interface with `RenderOrder` property
- `SystemManager.cs` - Registration-based system management

### Loading Scene

**Strengths:**
- Single loading scene system
- Clean initialization flow
- Event-driven progress updates

## Proposed Redesign

### 1. System Prioritization Architecture

#### 1.1 Create SystemPriority Constants

```csharp
namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Defines system execution priorities.
    /// Lower numbers execute first.
    /// </summary>
    public static class SystemPriority
    {
        // Map and world management (must run first)
        public const int MapLoader = 0;
        public const int MapConnection = 10;
        public const int ActiveMapManagement = 20;

        // Player initialization (runs once, no per-frame updates)
        public const int Player = 30;

        // Input processing
        public const int Input = 40;

        // Movement and physics
        public const int Movement = 50;
        public const int MapTransitionDetection = 60;

        // Camera (runs after movement)
        public const int Camera = 70;
        public const int CameraViewport = 75;

        // Animation
        public const int AnimatedTile = 100;
        public const int SpriteAnimation = 110;
        public const int SpriteSheet = 120;

        // Visibility and flags
        public const int VisibilityFlag = 130;

        // Performance tracking
        public const int PerformanceStats = 200;

        // Scene management
        public const int Scene = 300;
        public const int SceneInput = 310;
        public const int GameScene = 320;
        public const int LoadingScene = 330;
        public const int DebugBarScene = 340;

        // Popups and UI
        public const int MapPopupOrchestrator = 400;
        public const int MapPopup = 410;
        public const int DebugBarToggle = 420;

        // Shader effects
        public const int ShaderParameterAnimation = 500;
        public const int ShaderCycle = 510;
        public const int PlayerShaderCycle = 520;
    }
}
```

#### 1.2 Create System Priority Interface/Attribute

**Option A: Interface-based (matches oldmonoball)**
```csharp
namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Interface for systems that have an execution priority.
    /// </summary>
    public interface IPrioritizedSystem
    {
        /// <summary>
        /// Gets the execution priority. Lower values execute first.
        /// </summary>
        int Priority { get; }
    }
}
```

**Option B: Attribute-based (more flexible)**
```csharp
namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Attribute that specifies the execution priority of a system.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SystemPriorityAttribute : Attribute
    {
        public int Priority { get; }

        public SystemPriorityAttribute(int priority)
        {
            Priority = priority;
        }
    }
}
```

**Recommendation**: Use Option A (interface) for consistency with oldmonoball and explicit priority declaration.

#### 1.3 Refactor SystemManager to Use Registration Pattern

**Key Changes:**
1. Add `RegisterUpdateSystem()` method
2. Systems register themselves with their priority
3. Automatic sorting by priority
4. Remove hardcoded system list
5. Support for conditional system registration (shader systems)

**Benefits:**
- Systems declare their own priority
- Easy to add/remove systems
- Mods can register custom systems
- Testable system ordering
- No duplicate code paths

### 2. Loading Scene Architecture

#### 2.1 Simplify Loading Scene Initialization

**Current Flow:**
1. Create early loading scene system in `MonoBallGame.LoadContent()`
2. Create regular loading scene system in `SystemManager.Initialize()`
3. Switch from early to regular after initialization

**Proposed Flow:**
1. Create loading scene system once in `SystemManager.Initialize()`
2. Use events for progress updates (already partially implemented)
3. `LoadingSceneSystem` processes progress events in `Update()`

#### 2.2 Event-Driven Progress Updates

**Current:**
- Progress updates via `ConcurrentQueue` in `MonoBallGame.Update()`
- Manual queue processing

**Proposed:**
- Use `LoadingProgressUpdatedEvent` (already exists)
- `LoadingSceneSystem` subscribes to event in constructor
- Processes events in `Update()` method
- Unsubscribes in `Dispose()`

#### 2.3 Loading Scene Blocking Logic

**Current:**
- `SceneRendererSystem` checks `BlocksDraw` flag
- `LoadingSceneSystem` doesn't own blocking logic

**Proposed:**
- `LoadingSceneSystem` checks `BlocksUpdate`/`BlocksDraw` in its own `Update()`/`RenderScene()`
- Early return if blocked (fail-fast pattern)
- Clearer responsibility boundaries

## Implementation Plan

### Phase 1: System Priority Infrastructure

1. **Create `SystemPriority.cs`**
   - Define priority constants
   - Document priority ranges and dependencies

2. **Create `IPrioritizedSystem` interface**
   - Add `Priority` property
   - Document usage

3. **Update systems to implement `IPrioritizedSystem`**
   - Add `Priority` property returning appropriate constant
   - Update all update systems

### Phase 2: SystemManager Refactoring

1. **Add registration methods to SystemManager**
   - `RegisterUpdateSystem(BaseSystem<World, float> system)`
   - `RegisterRenderSystem(...)` (if needed)
   - Internal priority-based sorting

2. **Refactor `Initialize()` method**
   - Create systems
   - Register systems with priorities
   - Remove hardcoded system list
   - Remove duplicate code paths

3. **Update system creation**
   - Systems implement `IPrioritizedSystem`
   - Registration happens after creation

### Phase 3: Loading Scene Refactoring

1. **Remove early loading scene system**
   - Remove `_earlyLoadingSceneSystem` from `MonoBallGame`
   - Use regular `LoadingSceneSystem` from SystemManager

2. **Refactor `LoadingSceneSystem`**
   - Subscribe to `LoadingProgressUpdatedEvent` in constructor
   - Process events in `Update()` method
   - Unsubscribe in `Dispose()`
   - Handle blocking logic internally

3. **Update `GameInitializationService`**
   - Remove progress queue (use events only)
   - Fire `LoadingProgressUpdatedEvent` instead of queueing

4. **Update `MonoBallGame`**
   - Remove progress queue processing
   - Remove early loading scene system creation
   - Use SystemManager's loading scene system

## Architecture Benefits

### System Prioritization

1. **Maintainability**: Clear priority values, easy to modify
2. **Testability**: Can test different system orderings
3. **Modding**: Mods can register systems with custom priorities
4. **Clarity**: Explicit priority dependencies
5. **DRY**: Single registration path, no duplication

### Loading Scene

1. **Separation of Concerns**: System owns its update logic
2. **Event-Driven**: Uses Arch EventBus for decoupling
3. **Simplicity**: Single loading scene system
4. **Testability**: Can test loading scene independently

## Migration Strategy

1. **Backward Compatibility**: None required (per .cursorrules)
2. **Incremental Migration**: Can be done system-by-system
3. **Testing**: Test each phase before proceeding
4. **Documentation**: Update system documentation with priorities

## Open Questions

1. **Render System Priorities**: Do we need render order constants?
   - Currently handled by `SceneRendererSystem` coordination
   - May not need explicit render priorities

2. **System Groups**: Should we keep `Group<float>` or use flat list?
   - `Group<float>` provides lifecycle methods (`BeforeUpdate`, `AfterUpdate`)
   - May need to maintain groups for lifecycle hooks

3. **Conditional Systems**: How to handle optional systems (shader systems)?
   - Register conditionally based on service availability
   - Use null checks before registration

## Conclusion

This redesign addresses the architectural inefficiencies identified in the current implementation:

- **System Prioritization**: Moves from hardcoded lists to registration-based priority system
- **Loading Scene**: Moves from queue-based to event-driven architecture
- **Code Quality**: Follows DRY, SOLID, and .cursorrules principles
- **Modding Support**: Enables mods to register custom systems
- **Testability**: Makes system ordering testable and configurable

The proposed architecture aligns with oldmonoball's proven patterns while maintaining compatibility with Arch ECS and MonoGame conventions.

