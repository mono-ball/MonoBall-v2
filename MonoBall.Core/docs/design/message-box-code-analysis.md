# Message Box Code Analysis

Comprehensive analysis of message box implementation for architecture issues, Arch ECS/event issues, SOLID/DRY
principles, and bugs.

## Table of Contents

1. [Architecture Issues](#architecture-issues)
2. [Arch ECS/Event Issues](#arch-ecsevent-issues)
3. [SOLID/DRY Violations](#soliddry-violations)
4. [Bugs](#bugs)
5. [Recommendations](#recommendations)

---

## Architecture Issues

### 1. **System Not Following Standard Pattern**

**Location**: `MessageBoxSceneSystem.cs:23-27, 161-165, 198-298`

**Issue**: The system implements both `ISceneSystem` and `IPrioritizedSystem`, but doesn't override the standard
`Update(in float deltaTime)` from `BaseSystem`. Instead, all logic is in `ProcessInternal()`.

**Current Code**:

```csharp
public class MessageBoxSceneSystem
    : BaseSystem<World, float>,
        IPrioritizedSystem,
        IDisposable,
        ISceneSystem
{
    public void Update(Entity sceneEntity, float deltaTime) { ... } // Empty - satisfies ISceneSystem
    public void ProcessInternal(float deltaTime) { ... } // All logic here
    // Missing: public override void Update(in float deltaTime) { ... }
}
```

**Comparison with other scene systems**:

- `MapPopupSceneSystem`: Overrides `Update(in float deltaTime)` with logic, `ProcessInternal()` delegates to it
- `GameSceneSystem`: Overrides `Update(in float deltaTime)` with logic, `ProcessInternal()` is empty
- `MessageBoxSceneSystem`: **Doesn't override `Update(in float deltaTime)`**, all logic in `ProcessInternal()`

**Problem**:

- SystemManager registers `MessageBoxSceneSystem` as an update system (line 911 in SystemManager.cs)
- When SystemManager calls `Update(in float deltaTime)`, nothing happens (not overridden)
- Logic only runs when `SceneSystem` calls `ProcessInternal()` (line 663, 670 in SceneSystem.cs)
- This is inconsistent with other scene systems and breaks the standard Arch ECS pattern

**Recommendation**: Follow the pattern used by `MapPopupSceneSystem`:

- Override `Update(in float deltaTime)` with the actual logic
- Have `ProcessInternal()` delegate to `Update(in float deltaTime)`
- This ensures the system works whether called via SystemManager or SceneSystem

---

### 2. **Mixed Responsibilities in Single System**

**Location**: `MessageBoxSceneSystem.cs` (entire file)

**Issue**: The system handles:

- Event subscription/handling
- Scene creation/destruction
- Text parsing
- Text wrapping
- Text rendering
- Input handling
- State machine management
- Texture loading/caching

**Problem**: This violates Single Responsibility Principle. The system is doing too much.

**Recommendation**: Split into:

- `MessageBoxSceneSystem` - Scene lifecycle and event handling
- `MessageBoxTextProcessor` - Text parsing and wrapping (separate service)
- `MessageBoxRenderer` - Rendering logic (separate service or system)
- `MessageBoxInputHandler` - Input handling (could stay in system)

---

### 3. **Direct Texture Loading in System**

**Location**: `MessageBoxSceneSystem.cs:1480-1582`

**Issue**: The system directly loads textures from the file system using `Texture2D.FromFile()`. This pattern is also
used in `MapPopupSceneSystem` and other systems.

**Problem**: Systems should not handle resource loading directly. This should be handled by a resource manager or
service. However, this appears to be a codebase-wide pattern.

**Recommendation**: Consider creating a `TextureService` or `ResourceManager` for texture loading, but note this would
require refactoring multiple systems. For now, this is consistent with the codebase pattern.

---

### 4. **Hardcoded Camera Query Logic**

**Location**: `MessageBoxSceneSystem.cs:1288-1338`

**Issue**: Camera retrieval logic is duplicated and embedded in the render method. The codebase already has
`ICameraService` available.

**Problem**: Camera retrieval should use the existing `ICameraService` instead of duplicating query logic.

**Current Code**: Uses `GetActiveGameCamera()` method that queries World directly.

**Recommendation**: Inject `ICameraService` and use it instead of direct World queries. This matches the pattern used in
other systems like `SpriteRendererSystem` and `MapRendererSystem`.

---

## Arch ECS/Event Issues

### 1. **Event Handler Not Using Ref Correctly**

**Location**: `MessageBoxSceneSystem.cs:101-103`

**Issue**: Event subscriptions use method group conversion, but handlers receive `ref` parameters.

**Current Code**:

```csharp
EventBus.Subscribe<MessageBoxShowEvent>(OnMessageBoxShow);
// ...
private void OnMessageBoxShow(ref MessageBoxShowEvent evt) { ... }
```

**Problem**: This works but is inconsistent with Arch.EventBus patterns. Should verify this is the correct pattern.

**Status**: ✅ **Actually Correct** - Arch.EventBus supports this pattern for struct events.

---

### 2. **Event Subscription Disposal**

**Location**: `MessageBoxSceneSystem.cs:2026-2045`

**Issue**: ✅ **Correctly implemented** - System implements `IDisposable` and unsubscribes from events.

**Status**: ✅ **No Issue** - Follows best practices.

---

### 3. **QueryDescription Caching**

**Location**: `MessageBoxSceneSystem.cs:44-45, 93-98`

**Issue**: ✅ **Correctly cached** - QueryDescriptions are created in constructor and cached.

**Status**: ✅ **No Issue** - Follows best practices.

---

### 4. **Event Firing After State Changes**

**Location**: `MessageBoxSceneSystem.cs:636-637`

**Issue**: ✅ **Correctly implemented** - `MessageBoxTextFinishedEvent` is fired after state is set to `Finished`.

**Status**: ✅ **No Issue** - Follows best practices.

---

### 5. **Circular Event Dependency Risk**

**Location**: `MessageBoxSceneSystem.cs:277-278`

**Issue**: When destroying existing message box, the system fires `MessageBoxHideEvent`, which triggers
`OnMessageBoxHide()`, which could theoretically cause issues if the event handler modifies state.

**Current Code**:

```csharp
if (_activeMessageBoxSceneEntity.HasValue)
{
    var hideEvent = new MessageBoxHideEvent { WindowId = 0 };
    EventBus.Send(ref hideEvent);
}
```

**Problem**: This creates a recursive call path. The hide handler will destroy the scene, but we're about to create a
new one. This should be safe, but it's a potential race condition.

**Recommendation**: Consider direct scene destruction instead of firing an event, or ensure the hide handler is
idempotent.

---

## SOLID/DRY Violations

### 1. **DRY: Duplicated Font Validation**

**Location**:

- `MessageBoxSceneSystem.cs:286-293` (OnMessageBoxShow)
- `MessageBoxSceneSystem.cs:1137-1144` (WrapText)

**Issue**: Font validation logic is duplicated in two places.

**Current Code**:

```csharp
// In OnMessageBoxShow:
var fontSystem = _fontService.GetFontSystem(fontId);
if (fontSystem == null)
{
    throw new InvalidOperationException($"Font '{fontId}' not found...");
}

// In WrapText:
var fontSystem = _fontService.GetFontSystem(fontId);
if (fontSystem == null)
{
    throw new InvalidOperationException($"Font '{fontId}' not found...");
}
```

**Recommendation**: Extract to helper method:

```csharp
private FontSystem ValidateAndGetFont(string fontId)
{
    var fontSystem = _fontService.GetFontSystem(fontId);
    if (fontSystem == null)
    {
        throw new InvalidOperationException(
            $"Font '{fontId}' not found. Cannot create message box without valid font. " +
            $"Font must exist in mod registry."
        );
    }
    return fontSystem;
}
```

---

### 2. **DRY: Duplicated Tilesheet Validation**

**Location**:

- `MessageBoxSceneSystem.cs:296-305` (OnMessageBoxShow)
- `MessageBoxSceneSystem.cs:1489-1500` (LoadMessageBoxTexture)
- `MessageBoxSceneSystem.cs:1389-1399` (RenderMessageBox)

**Issue**: Tilesheet validation/retrieval is duplicated in three places.

**Recommendation**: Extract to helper method:

```csharp
private PopupOutlineDefinition ValidateAndGetTilesheet()
{
    var tilesheet = _modManager.GetDefinition<PopupOutlineDefinition>(
        MessageBoxConstants.MessageBoxTilesheetId
    );
    if (tilesheet == null)
    {
        throw new InvalidOperationException(
            $"Tilesheet '{MessageBoxConstants.MessageBoxTilesheetId}' not found. " +
            $"Cannot create message box without tilesheet. Tilesheet must exist in mod registry."
        );
    }
    return tilesheet;
}
```

---

### 3. **DRY: Duplicated Scroll Logic**

**Location**:

- `MessageBoxSceneSystem.cs:462-480` (OnMessageBoxTextAdvance)
- `MessageBoxSceneSystem.cs:581-602` (HandleInput)

**Issue**: Scroll animation start logic is duplicated.

**Current Code**:

```csharp
// In OnMessageBoxTextAdvance:
if (visibleLineCount >= MessageBoxConstants.MaxVisibleLines)
{
    msgBox.ScrollDistanceRemaining =
        MessageBoxConstants.DefaultScrollDistance + msgBox.LineSpacing;
    msgBox.ScrollOffset = 0;
    msgBox.IsWaitingForInput = false;
    msgBox.State = MessageBoxRenderState.Scrolling;
}

// In HandleInput (same logic):
if (visibleLineCount >= MessageBoxConstants.MaxVisibleLines)
{
    msgBox.ScrollDistanceRemaining =
        MessageBoxConstants.DefaultScrollDistance + msgBox.LineSpacing;
    msgBox.ScrollOffset = 0;
    msgBox.IsWaitingForInput = false;
    msgBox.State = MessageBoxRenderState.Scrolling;
}
```

**Recommendation**: Extract to helper method:

```csharp
private void StartScrollAnimation(ref MessageBoxComponent msgBox)
{
    int visibleLineCount = msgBox.CurrentY - msgBox.PageStartLine;
    if (visibleLineCount >= MessageBoxConstants.MaxVisibleLines)
    {
        msgBox.ScrollDistanceRemaining =
            MessageBoxConstants.DefaultScrollDistance + msgBox.LineSpacing;
        msgBox.ScrollOffset = 0;
        msgBox.IsWaitingForInput = false;
        msgBox.State = MessageBoxRenderState.Scrolling;
    }
    else
    {
        msgBox.IsWaitingForInput = false;
        msgBox.State = MessageBoxRenderState.HandleChar;
    }
}
```

---

### 4. **DRY: Duplicated Page Break Logic**

**Location**:

- `MessageBoxSceneSystem.cs:455-461` (OnMessageBoxTextAdvance)
- `MessageBoxSceneSystem.cs:572-580` (HandleInput)

**Issue**: Page break logic is duplicated.

**Recommendation**: Extract to helper method:

```csharp
private void AdvanceToNextPage(ref MessageBoxComponent msgBox)
{
    msgBox.PageStartLine = msgBox.CurrentY;
    msgBox.IsWaitingForInput = false;
    msgBox.State = MessageBoxRenderState.HandleChar;
    msgBox.HasBeenSpedUp = false; // Reset speed-up
}
```

---

### 5. **Single Responsibility: TextToken Has Behavior**

**Location**: `TextToken.cs:29-59`

**Issue**: `TextToken` is a struct (component-like) but has methods (`GetPauseSeconds()`, `GetColor()`, etc.).

**Problem**: According to ECS best practices, components should be pure data. However, since `TextToken` is not an ECS
component (it's a helper type), this is acceptable. But it violates the "data not behavior" principle.

**Status**: ⚠️ **Minor Issue** - Not a critical violation since it's not an ECS component, but could be improved by
using extension methods or a separate token processor.

---

### 6. **Open/Closed: Hardcoded Control Code Parsing**

**Location**: `MessageBoxSceneSystem.cs:942-1098`

**Issue**: Control code parsing uses a large if-else chain that would require modification to add new control codes.

**Problem**: Violates Open/Closed Principle - should be open for extension, closed for modification.

**Recommendation**: Use strategy pattern or dictionary of control code handlers.

---

## Bugs

### 1. **Potential Null Reference in IsMessageBoxVisible**

**Location**: `MessageBoxApi.cs:68-83`

**Issue**: The query callback doesn't check if `msgBox` is valid or if the entity is alive.

**Current Code**:

```csharp
public bool IsMessageBoxVisible()
{
    bool found = false;
    _world.Query(
        in _messageBoxQuery,
        (ref MessageBoxComponent msgBox) =>
        {
            if (msgBox.IsVisible && msgBox.State != MessageBoxRenderState.Hidden)
            {
                found = true;
            }
        }
    );
    return found;
}
```

**Problem**: If the entity is destroyed but component still exists (shouldn't happen, but defensive), this could access
invalid data.

**Status**: ⚠️ **Low Risk** - Arch ECS should handle this, but defensive checks wouldn't hurt.

---

### 2. **Race Condition in Hide Event Handler**

**Location**: `MessageBoxSceneSystem.cs:402-423`

**Issue**: When `OnMessageBoxHide` is called, it checks `_activeMessageBoxSceneEntity.HasValue`, but if another thread
or event handler modifies this between check and use, there could be a race condition.

**Current Code**:

```csharp
private void OnMessageBoxHide(ref MessageBoxHideEvent evt)
{
    if (evt.WindowId == 0)
    {
        if (_activeMessageBoxSceneEntity.HasValue)
        {
            var sceneEntity = _activeMessageBoxSceneEntity.Value;
            _sceneManager.DestroyScene(sceneEntity);
            _activeMessageBoxSceneEntity = null;
        }
    }
}
```

**Problem**: In a single-threaded game loop, this should be safe, but the pattern is fragile.

**Status**: ⚠️ **Low Risk** - Should be safe in MonoGame's single-threaded context.

---

### 3. **Missing Validation in ProcessCharacter**

**Location**: `MessageBoxSceneSystem.cs:631-646`

**Issue**: `ProcessCharacter` accesses `msgBox.ParsedText[msgBox.CurrentTokenIndex]` without bounds checking after the
initial check.

**Current Code**:

```csharp
if (msgBox.ParsedText == null || msgBox.CurrentTokenIndex >= msgBox.ParsedText.Count)
{
    // Handle finished
    return;
}

// Later:
var token = msgBox.ParsedText[msgBox.CurrentTokenIndex]; // Could still be out of bounds if ParsedText is modified
```

**Problem**: If `ParsedText` is modified externally (shouldn't happen, but defensive), this could throw
`IndexOutOfRangeException`.

**Status**: ⚠️ **Low Risk** - Should be safe, but defensive check wouldn't hurt.

---

### 4. **Incorrect Arrow Blink Calculation**

**Location**: `MessageBoxSceneSystem.cs:1982-1989`

**Issue**: Arrow blink uses frame-based calculation but assumes 60 FPS.

**Current Code**:

```csharp
int totalFrames = (int)(gameTime.TotalGameTime.TotalSeconds * 60);
bool isVisible = (totalFrames / MessageBoxConstants.ArrowBlinkFrames) % 2 == 0;
```

**Problem**: If the game runs at a different frame rate, the blink timing will be incorrect. Should use time-based
calculation.

**Recommendation**:

```csharp
double blinkInterval = MessageBoxConstants.ArrowBlinkFrames / 60.0; // Convert frames to seconds
bool isVisible = ((int)(gameTime.TotalGameTime.TotalSeconds / blinkInterval)) % 2 == 0;
```

---

### 5. **Potential Memory Leak: Texture Not Disposed**

**Location**: `MessageBoxSceneSystem.cs:48, 1560-1564`

**Issue**: `_messageBoxTexture` is cached but never disposed when the system is disposed.

**Current Code**:

```csharp
private Texture2D? _messageBoxTexture;

// In LoadTextureFromDefinition:
if (definitionId == MessageBoxConstants.MessageBoxTilesheetId)
{
    _messageBoxTexture = texture; // Cache message box texture
}
```

**Problem**: `Texture2D` implements `IDisposable`. If the system is disposed and recreated, the old texture won't be
disposed.

**Recommendation**: Dispose texture in `Dispose()` method:

```csharp
protected virtual void Dispose(bool disposing)
{
    if (!_disposed && disposing)
    {
        // ... existing unsubscribe code ...
        
        // Dispose cached texture
        _messageBoxTexture?.Dispose();
        _messageBoxTexture = null;
    }
    _disposed = true;
}
```

---

### 6. **Incorrect Text Color Default**

**Location**: `MessageBoxShowEvent.cs:36-38`

**Issue**: XML comment says default is "White" but code uses dark gray.

**Current Code**:

```csharp
/// <summary>
/// Text color (foreground). Null = use default (White).
/// </summary>
public Color? TextColor { get; set; }
```

**But in MessageBoxSceneSystem.cs:344**:

```csharp
Color initialTextColor = evt.TextColor ?? new Color(98, 98, 98, 255); // Dark gray text
```

**Problem**: Documentation doesn't match implementation.

**Recommendation**: Fix XML comment to match implementation.

---

### 7. **Missing Null Check in RenderText**

**Location**: `MessageBoxSceneSystem.cs:1763-1768`

**Issue**: `GetFontSystem` could return null, but code only logs warning and continues.

**Current Code**:

```csharp
var fontSystem = _fontService.GetFontSystem(msgBox.FontId);
if (fontSystem == null)
{
    _logger.Warning("Font '{FontId}' not found, cannot render text", msgBox.FontId);
    return; // Early return - correct
}
```

**Status**: ✅ **Actually Correct** - Early return prevents null reference.

---

### 8. **Potential Division by Zero in GetScrollSpeed**

**Location**: `MessageBoxSceneSystem.cs:516-535`

**Issue**: If `textSpeed` is negative or extremely large, the comparisons might not work as expected.

**Current Code**:

```csharp
private float GetScrollSpeed(float textSpeed)
{
    if (textSpeed >= MessageBoxConstants.TextSpeedSlowSeconds)
    {
        return MessageBoxConstants.ScrollSpeedSlowPixelsPerSecond;
    }
    // ... etc
}
```

**Problem**: If `textSpeed` is negative, it will fall through to instant speed, which might not be intended.

**Status**: ⚠️ **Low Risk** - Text speed should always be positive, but validation wouldn't hurt.

---

## Recommendations

### High Priority

1. **Fix memory leak**: Dispose cached texture in `Dispose()` method
2. **Fix arrow blink timing**: Use time-based calculation instead of frame-based
3. **Extract duplicated code**: Create helper methods for font/tilesheet validation, scroll logic, page break logic

### Medium Priority

4. **Split system responsibilities**: Consider separating text processing, rendering, and input handling
5. **Fix documentation**: Update XML comments to match implementation (text color default)
6. **Add defensive checks**: Add null/range checks in critical paths

### Low Priority

7. **Refactor control code parsing**: Use strategy pattern for extensibility
8. **Extract texture loading**: Move to resource manager/service
9. **Extract camera logic**: Move to camera service/utility

---

## Summary

### Architecture Issues: 4

- System inheritance pattern
- Mixed responsibilities
- Direct texture loading
- Hardcoded camera logic

### Arch ECS/Event Issues: 1

- Potential circular event dependency

### SOLID/DRY Violations: 6

- Duplicated font validation
- Duplicated tilesheet validation
- Duplicated scroll logic
- Duplicated page break logic
- TextToken has behavior
- Hardcoded control code parsing

### Bugs: 8

- Potential null reference (low risk)
- Race condition (low risk)
- Missing validation (low risk)
- Incorrect arrow blink calculation
- Memory leak (texture not disposed)
- Documentation mismatch
- Potential division issues (low risk)
- Missing null checks (actually correct)

**Total Issues**: 19 (4 architecture, 1 ECS/event, 6 SOLID/DRY, 8 bugs)

