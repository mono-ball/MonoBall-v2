# Scene System Design

## Overview

The scene system provides a layered rendering and update architecture that allows multiple scenes to coexist, with scenes controlling their rendering context (camera-based vs screen-space) and blocking behavior (whether scenes below can update/draw).

## Core Concepts

### Scene Rendering Modes

1. **Camera-Based Rendering**: Scene renders using the active camera's transform matrix and viewport. Useful for game world rendering that should scale with camera zoom/position.
2. **Screen-Space Rendering**: Scene renders directly to the full window without camera transform. Useful for UI overlays, menus, and HUD elements.

### Scene Blocking

Scenes can control whether scenes below them in the stack can:
- **Update**: Block lower scenes from receiving Update() calls
- **Draw**: Block lower scenes from receiving Draw() calls

This allows modal scenes (menus, dialogs) to pause gameplay while still allowing rendering of background scenes if desired.

### Scene Priority

Scenes are ordered by priority (higher priority = rendered/updated first). Within the same priority, scenes are ordered by creation time (newer scenes on top).

## Architecture

### Components

#### SceneComponent
Stores scene state data:
- `SceneId` (string): Unique identifier for the scene
- `Priority` (int): Rendering/update priority (higher = first)
- `RenderingMode` (enum): CameraBased or ScreenSpace
- `BlocksUpdate` (bool): Whether this scene blocks lower scenes from updating
- `BlocksDraw` (bool): Whether this scene blocks lower scenes from drawing
- `IsActive` (bool): Whether the scene is active
- `IsPaused` (bool): Whether the scene is paused
- `CameraEntityId` (int?): Optional camera entity ID for camera-based rendering (null = use active camera)

### Systems

#### SceneManagerSystem
Manages the scene lifecycle:
- Creates/destroys scenes
- Maintains scene priority stack
- Handles scene activation/deactivation
- Processes scene events (pause, resume, destroy, etc.)
- Updates scenes in priority order (respecting blocking flags)
- Renders scenes in priority order (respecting blocking flags)

#### SceneRendererSystem
Handles scene rendering:
- Renders scenes based on their rendering mode
- Applies camera transforms for camera-based scenes
- Renders screen-space scenes without transforms
- Respects scene blocking flags

### Events

#### SceneLifecycle Events
- `SceneCreatedEvent`: Fired when a scene is created
- `SceneDestroyedEvent`: Fired when a scene is destroyed
- `SceneActivatedEvent`: Fired when a scene becomes active
- `SceneDeactivatedEvent`: Fired when a scene becomes inactive
- `ScenePausedEvent`: Fired when a scene is paused
- `SceneResumedEvent`: Fired when a scene resumes
- `ScenePriorityChangedEvent`: Fired when a scene's priority changes

#### SceneControl Events
- `SceneMessageEvent`: Generic message event for inter-scene communication
  - `SourceSceneId` (string): Scene sending the message
  - `TargetSceneId` (string?): Target scene (null = broadcast)
  - `MessageType` (string): Type of message (e.g., "pause", "resume", "destroy", "custom")
  - `MessageData` (object?): Optional message payload

## Design Patterns

### Scene Stack Management

Scenes are stored as entities with SceneComponent. The SceneManagerSystem maintains a priority-ordered list for efficient iteration:

```csharp
// Priority-ordered scene list (highest priority first)
// Used for both update and render ordering
private List<Entity> _sceneStack = new List<Entity>();
```

### Update/Draw Flow

1. **Update Flow**:
   - Iterate scenes from highest to lowest priority
   - For each scene:
     - If scene is not active or is paused, skip
     - Update scene
     - If scene blocks update, stop iterating (lower scenes don't update)

2. **Draw Flow**:
   - Iterate scenes from highest to lowest priority
   - For each scene:
     - If scene is not active, skip
     - If scene blocks draw, don't render lower scenes
     - Render scene (camera-based or screen-space)

### Camera Selection

For camera-based scenes:
- If `SceneComponent.CameraEntityId` is set, use that camera
- Otherwise, use the active camera (CameraComponent.IsActive == true)
- If no camera available, skip rendering (log warning)

### Event-Driven Communication

Scenes communicate through events, not direct references:
- Scene A wants to pause Scene B: Fire `SceneMessageEvent` with `MessageType="pause"`
- SceneManagerSystem listens for `SceneMessageEvent` and processes it
- This keeps scenes decoupled and testable

## Example Use Cases

### 1. Main Game Scene + Pause Menu
```
Scene Stack:
- PauseMenuScene (Priority: 100, BlocksUpdate: true, BlocksDraw: false, ScreenSpace)
  └─ MainGameScene (Priority: 50, BlocksUpdate: false, BlocksDraw: false, CameraBased)
```
Result: Game renders in background, but doesn't update. Menu overlays on top.

### 2. Game World + UI HUD
```
Scene Stack:
- HudScene (Priority: 100, BlocksUpdate: false, BlocksDraw: false, ScreenSpace)
  └─ GameWorldScene (Priority: 50, BlocksUpdate: false, BlocksDraw: false, CameraBased)
```
Result: Both update and draw. HUD renders on top in screen space.

### 3. Full-Screen Dialog
```
Scene Stack:
- DialogScene (Priority: 100, BlocksUpdate: true, BlocksDraw: true, ScreenSpace)
  └─ GameWorldScene (Priority: 50, BlocksUpdate: false, BlocksDraw: false, CameraBased)
```
Result: Game is completely paused, dialog is full-screen.

## Implementation Details

### Scene Creation
1. Create entity with SceneComponent
2. Set initial properties (priority, rendering mode, blocking flags)
3. Fire `SceneCreatedEvent`
4. Add to scene stack (sorted by priority)

### Scene Destruction
1. Fire `SceneDestroyedEvent`
2. Remove from scene stack
3. Destroy entity

### Scene Rendering
- Camera-based: Set GraphicsDevice.Viewport to camera.VirtualViewport, use camera transform matrix
- Screen-space: Use full window viewport, no transform matrix (identity)

### Priority Management
- Priority can be changed at runtime
- Changing priority triggers `ScenePriorityChangedEvent` and re-sorts stack
- Lower priority scenes (e.g., background) have lower numbers

## File Structure

```
MonoBall.Core/
├── Scenes/
│   ├── Components/
│   │   └── SceneComponent.cs
│   ├── Events/
│   │   ├── SceneCreatedEvent.cs
│   │   ├── SceneDestroyedEvent.cs
│   │   ├── SceneActivatedEvent.cs
│   │   ├── SceneDeactivatedEvent.cs
│   │   ├── ScenePausedEvent.cs
│   │   ├── SceneResumedEvent.cs
│   │   ├── ScenePriorityChangedEvent.cs
│   │   └── SceneMessageEvent.cs
│   ├── Systems/
│   │   ├── SceneManagerSystem.cs
│   │   └── SceneRendererSystem.cs
│   └── SceneRenderingMode.cs (enum)
└── SCENE_SYSTEM_DESIGN.md (this file)
```

## Integration Points

### With SystemManager
- SceneManagerSystem and SceneRendererSystem should be added to SystemManager
- SceneManagerSystem updates in Update() phase
- SceneRendererSystem renders in Draw() phase (after MapRendererSystem)

### With Camera System
- SceneRendererSystem queries for CameraComponent when rendering camera-based scenes
- Uses active camera or scene-specified camera entity

### With EventBus
- All scene events use Arch.EventBus for publishing/subscribing
- SceneManagerSystem subscribes to SceneMessageEvent
- Other systems can subscribe to scene lifecycle events

## Future Enhancements

- Scene transitions (fade in/out, slide, etc.)
- Scene groups (manage multiple scenes as one unit)
- Scene layers within a scene (for complex scenes)
- Scene state serialization (save/load scene state)
- Scene templates (pre-configured scene types)

