# Scripting System Design Summary

**Quick Reference**: Key design decisions and improvements over the old system.

---

## 🎯 Design Goals

1. **ECS-First**: Scripts operate on entities/components, not OOP classes
2. **Event-Driven**: Reactive programming model (no polling)
3. **Composition**: Multiple scripts per entity
4. **Mod Integration**: Scripts loaded from mod directories automatically
5. **Type Safety**: Full C# compilation with IntelliSense

---

## 🏗️ Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Game Systems                          │
│  (MapLoaderSystem, MovementSystem, etc.)                │
└────────────────────┬────────────────────────────────────┘
                     │ Publishes Events
                     ▼
┌─────────────────────────────────────────────────────────┐
│                    EventBus                              │
│  (Static event dispatcher)                               │
└────────────────────┬────────────────────────────────────┘
                     │ Dispatches to Subscribers
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Script Event Handlers                       │
│  (Subscribed via ScriptBase.On<T>())                     │
└─────────────────────────────────────────────────────────┘
                     │
                     │ Scripts can publish events
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Other Systems / Scripts                     │
└─────────────────────────────────────────────────────────┘
```

---

## 📦 Key Components

### ScriptDefinition
- **Purpose**: JSON definition for scripts (like other definitions)
- **Features**: References `.csx` file, includes metadata (name, description, priority)
- **Location**: `Mods/Definitions/ScriptDefinition.cs`
- **Example**: `"id": "base:script:behavior/stationary"` references `Scripts/behaviors/stationary.csx`

### ScriptAttachmentComponent
- **Purpose**: Attach scripts to entities via definition ID
- **Features**: Multiple instances per entity (composition), priority-based execution
- **Location**: `ECS/Components/ScriptAttachmentComponent.cs`
- **Uses**: `ScriptDefinitionId` (not file path)

### ScriptBase
- **Purpose**: Base class for all user scripts
- **Features**: Event subscription, state management, automatic cleanup
- **Location**: `Scripting/Runtime/ScriptBase.cs`

### ScriptLifecycleSystem
- **Purpose**: Manages script initialization and cleanup
- **Features**: Resolves scripts from definitions, loads scripts, initialize context, handle hot-reload
- **Location**: `ECS/Systems/ScriptLifecycleSystem.cs`

---

## 🔄 Script Lifecycle

```
1. Entity Created with ScriptAttachmentComponent
   ↓
2. ScriptLifecycleSystem detects attachment
   ↓
3. ScriptCompilerService compiles script (if needed)
   ↓
4. Script instance created
   ↓
5. ScriptBase.Initialize() called
   ↓
6. ScriptBase.RegisterEventHandlers() called
   ↓
7. Script active, receiving events
   ↓
8. (On entity destroy/component remove)
   ↓
9. ScriptBase.OnUnload() called
   ↓
10. Event subscriptions cleaned up
```

---

## 📝 Script Example

```csharp
// Mods/core/Scripts/tiles/ice_tile.csx
using MonoBall.Core.Scripting.Runtime;
using MonoBall.Core.ECS.Events;

public class IceTileScript : ScriptBase
{
    private Entity _tileEntity;

    public override void Initialize(ScriptContext context)
    {
        _tileEntity = context.Entity ?? 
            throw new InvalidOperationException("Ice tile requires entity");
    }

    public override void RegisterEventHandlers(ScriptContext context)
    {
        // Subscribe to movement events
        On<MovementCompletedEvent>(OnMovementComplete);
    }

    private void OnMovementComplete(MovementCompletedEvent evt)
    {
        // Filter: only handle events for this tile
        if (evt.TileEntity != _tileEntity) return;

        // Continue sliding logic...
        var continueMove = new MovementStartedEvent { /* ... */ };
        Publish(ref continueMove);
    }
}

return new IceTileScript();
```

---

## 🆚 Comparison: Old vs New

| Aspect | Old System | New System |
|--------|-----------|------------|
| **Architecture** | OOP classes + ECS | Pure ECS + Events |
| **State Storage** | Instance fields | ECS Components |
| **Event Model** | Mixed (some polling) | Pure event-driven |
| **Composition** | Supported | Enhanced |
| **Mod Integration** | Manual | Automatic |
| **Hot-Reload** | Supported | Enhanced |
| **Type Safety** | Full C# | Full C# |

---

## ✅ Improvements Over Old System

### 1. **Pure ECS Architecture**
- **Old**: Scripts stored class instances in components
- **New**: Scripts operate purely on components (no class instances in components)

### 2. **Event-First Design**
- **Old**: Mixed event-driven and polling (`OnTick()` method)
- **New**: Pure event-driven (subscribe to `TickEvent` if needed)

### 3. **Better State Management**
- **Old**: Instance fields (lost on hot-reload)
- **New**: Component-based state (persists across hot-reload)

### 4. **Automatic Mod Integration**
- **Old**: Manual script discovery
- **New**: Scripts discovered automatically from mod directories

### 5. **Simplified API**
- **Old**: Multiple base classes (`TileBehaviorScriptBase`, `TypeScriptBase`, etc.)
- **New**: Single `ScriptBase` class for all script types

---

## 🚀 Implementation Phases

### Phase 1: Core Infrastructure
- `ScriptDefinition` class
- Components and base classes
- Script compilation service
- Basic event subscription

### Phase 2: Definition Integration
- Script definition loading in `ModLoader`
- Script definition registration in `DefinitionRegistry`
- Definition-based script resolution

### Phase 3: Lifecycle Management
- Script loading from definitions
- Script initialization and cleanup
- Hot-reload support
- Error handling

### Phase 4: API Integration
- `IScriptApiProvider` implementation
- `IPlayerApi` - Player operations
- `IMapApi` - Map loading/management
- `IMovementApi` - Movement operations
- `ICameraApi` - Camera operations
- `IFlagVariableService` - Direct access (already script-safe)
- `DefinitionRegistry` - Direct access for querying definitions

### Phase 5: Entity Integration
- Script attachment from NPC definitions (via `behaviorId`)
- Script attachment from tile definitions
- Composition testing

### Phase 6: Advanced Features
- Debugging support
- Performance profiling
- Optional sandboxing

---

## 📚 Key Files to Create

### Definitions
- `MonoBall.Core/Mods/Definitions/ScriptDefinition.cs`

### Components
- `MonoBall.Core/ECS/Components/ScriptAttachmentComponent.cs`
- `MonoBall.Core/ECS/Components/ScriptStateComponent.cs`

### Systems
- `MonoBall.Core/ECS/Systems/ScriptLoaderSystem.cs`
- `MonoBall.Core/ECS/Systems/ScriptLifecycleSystem.cs`

### Runtime
- `MonoBall.Core/Scripting/Runtime/ScriptBase.cs`
- `MonoBall.Core/Scripting/Runtime/ScriptContext.cs`

### Services
- `MonoBall.Core/Scripting/Services/ScriptCompilerService.cs`
- `MonoBall.Core/Scripting/Services/ScriptCacheService.cs`
- `MonoBall.Core/Scripting/Services/ScriptApiProvider.cs`

### Events
- `MonoBall.Core/ECS/Events/ScriptLoadedEvent.cs`
- `MonoBall.Core/ECS/Events/ScriptUnloadedEvent.cs`
- `MonoBall.Core/ECS/Events/ScriptErrorEvent.cs`

---

## 🎯 Next Steps

1. **Review Design**: Validate architecture decisions
2. **Create Components**: Implement `ScriptAttachmentComponent` and `ScriptStateComponent`
3. **Implement ScriptBase**: Create base class with event subscription
4. **Build Compiler Service**: Integrate Roslyn for script compilation
5. **Create Lifecycle System**: Implement script initialization/cleanup
6. **Test with Examples**: Create example scripts to validate design

---

## 📖 Full Documentation

See [scripting-system-design.md](./scripting-system-design.md) for complete design details.

