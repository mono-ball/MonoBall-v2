# Message Box System Design Analysis

## Overview

This document analyzes the message box system design for architecture issues, Arch ECS/event problems, .cursorrule
violations, and forward-thinking concerns.

---

## 🔴 Critical Issues

### 1. Event Subscription Disposal Missing

**Issue**: `MessageBoxSceneSystem` subscribes to events but doesn't show proper disposal pattern.

**Current Design**:

```csharp
// Event subscriptions shown but no Dispose() implementation
EventBus.Subscribe<MessageBoxShowEvent>(OnMessageBoxShow);
EventBus.Subscribe<MessageBoxHideEvent>(OnMessageBoxHide);
EventBus.Subscribe<MessageBoxTextAdvanceEvent>(OnMessageBoxTextAdvance);
```

**Problem**:

- Violates `.cursorrules` rule #5: "Event Subscriptions: MUST implement `IDisposable` and unsubscribe in `Dispose()` to
  prevent leaks"
- Memory leaks if system is disposed without unsubscribing
- Event handlers may hold references to disposed system

**Fix Required**:

```csharp
public class MessageBoxSceneSystem : BaseSystem<World, float>, IPrioritizedSystem, IDisposable, ISceneSystem
{
    private bool _disposed = false;

    public MessageBoxSceneSystem(...)
    {
        EventBus.Subscribe<MessageBoxShowEvent>(OnMessageBoxShow);
        EventBus.Subscribe<MessageBoxHideEvent>(OnMessageBoxHide);
        EventBus.Subscribe<MessageBoxTextAdvanceEvent>(OnMessageBoxTextAdvance);
    }

    public new void Dispose() => Dispose(true);

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            EventBus.Unsubscribe<MessageBoxShowEvent>(OnMessageBoxShow);
            EventBus.Unsubscribe<MessageBoxHideEvent>(OnMessageBoxHide);
            EventBus.Unsubscribe<MessageBoxTextAdvanceEvent>(OnMessageBoxTextAdvance);
            // Clear cached collections, dispose other resources
        }
        _disposed = true;
    }
}
```

---

### 2. QueryDescription Not Cached

**Issue**: Queries shown in code examples but `QueryDescription` not cached in constructor.

**Current Design**:

```csharp
public void ProcessInternal(float deltaTime)
{
    // Query shown but QueryDescription not cached
    World.Query(in _messageBoxScenesQuery, ...);
}
```

**Problem**:

- Violates `.cursorrules` rule #3: "Cache `QueryDescription` in constructor, never create queries in Update/Render"
- Allocations in hot path (every frame)
- Performance degradation

**Fix Required**:

```csharp
public class MessageBoxSceneSystem : BaseSystem<World, float>, ...
{
    private readonly QueryDescription _messageBoxScenesQuery;

    public MessageBoxSceneSystem(...)
    {
        // Cache QueryDescription in constructor
        _messageBoxScenesQuery = new QueryDescription()
            .WithAll<SceneComponent, MessageBoxSceneComponent, MessageBoxComponent>();
    }
}
```

---

### 3. Missing Null Validation and Error Handling

**Issue**: No validation for required dependencies, missing components, or null checks.

**Problems**:

- Violates `.cursorrules` rule #7: "Dependency Injection: Required dependencies in constructor, throw
  `ArgumentNullException` for null"
- Violates `.cursorrules` rule #2: "NO FALLBACK CODE - Fail fast with clear exceptions"
- No validation in `RenderScene()` when checking for `MessageBoxComponent`
- No validation in `OnMessageBoxShow()` for missing services

**Fix Required**:

```csharp
public MessageBoxSceneSystem(
    World world,
    ISceneManager sceneManager,
    FontService fontService,
    // ... other dependencies
)
{
    _sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
    _fontService = fontService ?? throw new ArgumentNullException(nameof(fontService));
    // ... validate all required dependencies
}

public void RenderScene(Entity sceneEntity, GameTime gameTime)
{
    if (!World.Has<MessageBoxComponent>(sceneEntity))
    {
        throw new InvalidOperationException(
            $"Scene entity {sceneEntity.Id} does not have MessageBoxComponent. " +
            "Cannot render message box without component."
        );
    }
    // ... rest of rendering
}
```

---

### 4. Multiple Message Box Handling

**Issue**: Design mentions `WindowId` for future multiple boxes but doesn't handle concurrent message boxes.

**Problems**:

- No enforcement of single message box (should only one be active at a time?)
- `MessageBoxHideEvent` with `WindowId = 0` hides "all" but no tracking of active boxes
- Race conditions if multiple `MessageBoxShowEvent` fired simultaneously
- No clear policy: allow stacking or enforce single?

**Recommendation**:

- **Option A (Enforce Single)**: Track active scene entity, destroy existing before creating new
- **Option B (Allow Stacking)**: Properly track multiple scene entities, handle input for topmost only

**Fix Required**:

```csharp
public class MessageBoxSceneSystem : ...
{
    private Entity? _activeMessageBoxSceneEntity; // Track active scene

    private void OnMessageBoxShow(ref MessageBoxShowEvent evt)
    {
        // Option A: Enforce single message box
        if (_activeMessageBoxSceneEntity.HasValue)
        {
            // Hide existing message box first
            var hideEvent = new MessageBoxHideEvent { WindowId = 0 };
            EventBus.Send(ref hideEvent);
        }

        // Create new scene...
    }
}
```

---

## ⚠️ Architecture Issues

### 5. Scene Entity Tracking

**Issue**: System needs to track which scene entity is the active message box.

**Problem**:

- `MessageBoxHideEvent` with `WindowId = 0` needs to know which scene to destroy
- No mapping from `WindowId` to scene entity
- `IsMessageBoxVisible()` query inefficient (queries all entities)

**Fix Required**:

```csharp
public class MessageBoxSceneSystem : ...
{
    private Entity? _activeMessageBoxSceneEntity;
    private readonly Dictionary<int, Entity> _windowIdToSceneEntity = new();

    private void OnMessageBoxShow(ref MessageBoxShowEvent evt)
    {
        var sceneEntity = _sceneManager.CreateScene(...);
        _activeMessageBoxSceneEntity = sceneEntity;
        _windowIdToSceneEntity[msgBox.WindowId] = sceneEntity;
    }

    private void OnMessageBoxHide(ref MessageBoxHideEvent evt)
    {
        if (evt.WindowId == 0)
        {
            // Hide all (use tracked entity)
            if (_activeMessageBoxSceneEntity.HasValue)
            {
                _sceneManager.DestroyScene(_activeMessageBoxSceneEntity.Value);
                _activeMessageBoxSceneEntity = null;
            }
        }
        else
        {
            // Hide specific window
            if (_windowIdToSceneEntity.TryGetValue(evt.WindowId, out var sceneEntity))
            {
                _sceneManager.DestroyScene(sceneEntity);
                _windowIdToSceneEntity.Remove(evt.WindowId);
            }
        }
    }
}
```

---

### 6. Input Handling Race Conditions

**Issue**: Input handled in `ProcessInternal()` but no protection against multiple message boxes.

**Problem**:

- If multiple message boxes exist, which one receives input?
- No priority system for input handling
- Button press might affect wrong message box

**Fix Required**:

- Only handle input for the topmost/active message box scene
- Query scenes by priority, handle input for highest priority active scene only

---

### 7. Text Speed Storage Location

**Issue**: Design mentions "player text speed preference" but doesn't specify where it's stored.

**Problem**:

- No clear storage mechanism (save data? global variables? flags?)
- `GetPlayerTextSpeed()` method shown but not implemented
- No fallback if player preference doesn't exist

**Fix Required**:

- Use `IFlagVariableService` to store player text speed preference
- Store as global variable (e.g., `"player:textSpeed"`)
- Default to Medium speed if not set
- Document storage location in design

---

### 8. Component Contains Reference Types

**Issue**: `MessageBoxComponent` contains `string Text` and `string? SpeakerName` (reference types).

**Problem**:

- `.cursorrules` says "Components are value types (`struct`) - store data, not behavior"
- Structs can contain reference types, but need to be careful about:
    - Equality comparison (struct equality uses reference equality for strings)
    - Memory (strings are heap-allocated)
    - Copy semantics (struct copies share string references)

**Assessment**:

- ✅ **Acceptable** - Strings in structs are common pattern in ECS
- ⚠️ **Note**: Be aware of copy semantics (struct copies share string references, which is usually desired)

**No Fix Required** - This is acceptable, but document the behavior.

---

## 🟡 .cursorrule Violations

### 9. Missing XML Documentation

**Issue**: Code examples lack XML documentation comments.

**Problem**:

- Violates `.cursorrules` rule #8: "XML Documentation: Document all public APIs with XML comments"
- Missing `<summary>`, `<param>`, `<returns>`, `<exception>` tags

**Fix Required**: Add XML documentation to all public methods and properties.

---

### 10. Magic Numbers

**Issue**: Hard-coded values like `ScenePriorities.GameScene + 20`, delay values (8, 4, 2 frames).

**Problem**:

- Violates DRY principle
- Hard to maintain and adjust

**Fix Required**:

```csharp
public static class MessageBoxConstants
{
    public const int ScenePriorityOffset = 20; // Above GameScene
    public const int TextSpeedSlowFrames = 8;
    public const int TextSpeedMediumFrames = 4;
    public const int TextSpeedFastFrames = 2;
    public const int TextSpeedInstantFrames = 0;
}
```

---

### 11. Namespace Mismatch

**Issue**: `MessageBoxComponent` in `Scenes.Components` but events in `ECS.Events`.

**Assessment**:

- ✅ **Correct** - Components follow scene structure, events are global ECS events
- No fix needed, but document the reasoning

---

## 🔵 Forward-Thinking Issues

### 12. Control Code Parsing Implementation

**Issue**: Control codes mentioned but parsing implementation not detailed.

**Problems**:

- No state machine for parsing control codes
- No handling of malformed control codes
- No escape sequence handling details
- Performance concerns (parsing every character every frame?)

**Recommendation**:

- Pre-parse control codes when message box is created (store parsed tokens)
- Don't parse during character-by-character printing
- Use token list: `List<TextToken>` where `TextToken` is `{ char, controlCode, position }`

---

### 13. Font Error Handling

**Issue**: What if `FontId` doesn't exist or font loading fails?

**Problem**:

- No error handling specified
- Rendering will fail if font missing
- Must follow `.cursorrules` rule #2: NO FALLBACK CODE

**Recommendation**:

- **Fail fast**: Throw `InvalidOperationException` if font not found
- Validate font exists during scene creation (before creating component)
- Clear error message indicating font must exist in mod registry
- No fallback - per `.cursorrules` rule #2

---

### 14. Tilesheet Error Handling

**Issue**: What if `base:textwindow:tilesheet/message_box` doesn't exist?

**Problem**:

- System will fail if tilesheet missing
- Must follow `.cursorrules` rule #2: NO FALLBACK CODE

**Recommendation**:

- **Fail fast**: Throw `InvalidOperationException` if tilesheet not found
- Validate tilesheet exists during scene creation (before creating component)
- Clear error message indicating tilesheet must exist in mod registry
- No fallback - per `.cursorrules` rule #2

---

### 15. Text Wrapping Algorithm

**Issue**: Text wrapping mentioned but algorithm not specified.

**Problems**:

- How to handle words that don't fit on line?
- How to handle very long words?
- How to calculate line width (character count vs pixel width)?

**Recommendation**:

- Use pixel-based wrapping (measure string width with font)
- Break words if necessary (hyphenate or truncate)
- Store wrapped lines in component to avoid re-wrapping every frame

---

### 16. Speaker Name Box Rendering

**Issue**: Speaker name mentioned but rendering details not specified.

**Problems**:

- Where does speaker name box render? (Above message box? Left side?)
- What if speaker name is very long?
- Does speaker name box use same tilesheet or different?

**Recommendation**:

- Render speaker name box above message box (Pokemon Emerald style)
- Truncate long names with ellipsis
- Use same tilesheet or create separate speaker name tilesheet definition

---

### 17. Multiple Message Box Stacking (Future)

**Issue**: Design mentions future support for multiple boxes but doesn't design it.

**Problems**:

- No priority system for stacked boxes
- No input handling policy (which box receives input?)
- No rendering order (which box renders on top?)

**Recommendation**:

- Use scene priority for stacking order
- Handle input for highest priority active scene
- Document stacking behavior in future enhancements section

---

### 18. Text Speed Modifiers

**Issue**: Text speed modifiers mentioned but not designed.

**Problems**:

- Where are modifiers stored? (Config file? Mod definitions?)
- How are modifiers applied? (Multiplicative? Additive?)
- Can mods override modifiers?

**Recommendation**:

- Store modifiers in mod definitions or config
- Apply multiplicatively: `actualDelay = baseDelay * modifier`
- Allow mods to override via definition files

---

### 19. Localization Support

**Issue**: Future enhancement mentions localization but no design.

**Problems**:

- How to handle right-to-left languages?
- How to handle different character widths?
- How to handle control codes in localized text?

**Recommendation**:

- Design text direction system (LTR/RTL)
- Support text direction in `MessageBoxComponent`
- Handle control codes regardless of text direction

---

### 20. Performance Considerations

**Issue**: No performance analysis or optimization strategies.

**Problems**:

- Character-by-character rendering every frame
- String operations in hot path
- Font measurement calls every frame

**Recommendations**:

- Pre-calculate text layout (wrapped lines, positions) when message box created
- Cache font measurements
- Batch character rendering (render substring up to `CurrentCharIndex` instead of individual chars)
- Use `StringBuilder` for text manipulation if needed

---

## 📋 Summary of Required Fixes

### Critical (Must Fix Before Implementation)

1. ✅ Add `IDisposable` implementation with event unsubscription
2. ✅ Cache `QueryDescription` in constructor
3. ✅ Add null validation for all dependencies
4. ✅ Add error handling for missing components
5. ✅ Design multiple message box handling policy
6. ✅ Track scene entity for active message box

### Important (Should Fix)

7. ✅ Add XML documentation to all public APIs
8. ✅ Extract magic numbers to constants
9. ✅ Design text speed storage mechanism
10. ✅ Design control code parsing implementation
11. ✅ Design font/tilesheet fallback strategies

### Nice to Have (Future)

12. ⚠️ Design text wrapping algorithm
13. ⚠️ Design speaker name box rendering
14. ⚠️ Design multiple message box stacking
15. ⚠️ Design text speed modifiers system
16. ⚠️ Performance optimization strategies

---

## 🎯 Recommended Next Steps

1. **Update Design Document** with fixes for Critical and Important issues
2. **Create Constants File** for magic numbers
3. **Design Control Code Parser** as separate utility class
4. **Design Text Wrapping Algorithm** with pixel-based measurement
5. **Create Test Cases** for edge cases (missing fonts, long text, control codes)

