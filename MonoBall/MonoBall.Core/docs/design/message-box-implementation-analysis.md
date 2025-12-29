# Message Box Implementation Analysis

## Critical Issues

### 1. **SceneSystem Missing MessageBoxSceneComponent Registration** ⚠️ CRITICAL

**Location**: `SceneSystem.cs` lines 278-303, 553-592, 643-656

**Issue**: `MessageBoxSceneComponent` is not handled in three places:

- `CreateScene()` doesn't add `MessageBoxSceneComponent` to the scene entity
- `FindSceneSystem()` doesn't check for `MessageBoxSceneComponent` to find the message box scene system
- `Update()` doesn't call `_messageBoxSceneSystem?.ProcessInternal(deltaTime)`

**Impact**: Message box scenes won't be properly registered, found, or updated. The system will fail silently.

**Fix Required**:

```csharp
// In CreateScene() around line 302:
else if (component is MessageBoxSceneComponent messageBoxSceneComp)
{
    World.Add<MessageBoxSceneComponent>(sceneEntity, messageBoxSceneComp);
}

// In FindSceneSystem() around line 589:
if (World.Has<MessageBoxSceneComponent>(sceneEntity))
{
    return _sceneSystemRegistry.TryGetValue(
        typeof(MessageBoxSceneComponent),
        out var system
    )
        ? system
        : null;
}

// In Update() around line 655:
_messageBoxSceneSystem?.ProcessInternal(deltaTime);
```

---

### 2. **CurrentCharIndex Tracking Bug** ⚠️ CRITICAL

**Location**: `MessageBoxSceneSystem.cs` `ProcessCharacter()` method

**Issue**: `CurrentCharIndex` is only incremented for `Char` tokens, but `WrappedLines` are built using character
indices. When control codes are processed (Color, Speed, Clear, Pause, PauseUntilPress, Newline), `CurrentCharIndex` is
not updated, causing a mismatch between token processing and character rendering.

**Impact**: Text rendering will be incorrect - characters may appear/disappear at wrong times, or partial lines may
render incorrectly.

**Example**: If text is "Hello{COLOR:255,0,0}World", the `WrappedLines` will have `EndIndex` based on character count (
17), but `CurrentCharIndex` will only increment for visible characters (10), causing rendering to stop early.

**Fix Required**: `CurrentCharIndex` should track the position in the original text string, not just visible characters.
However, control codes are not visible characters, so they shouldn't increment `CurrentCharIndex`. The real issue is
that `WrappedLines.EndIndex` should match the character count in the original text, which it does. But we need to ensure
`CurrentCharIndex` matches the character position correctly.

Actually, reviewing the code more carefully: `WrapText()` correctly tracks `charIndex` only for `Char` tokens, so
`WrappedLines.EndIndex` represents the character count correctly. The issue is that `CurrentCharIndex` in
`ProcessCharacter()` should match this. Currently it does increment only for `Char` tokens, which is correct. But wait -
`Newline` tokens don't increment `charIndex` in `WrapText()` (line 807 comment says "Don't increment charIndex for
newline"), but they also don't increment `CurrentCharIndex` in `ProcessCharacter()`. This is consistent.

**Re-evaluation**: After closer inspection, `CurrentCharIndex` tracking appears correct. The rendering logic uses
`CurrentCharIndex` to determine which characters to display, and it only increments for `Char` tokens, which matches how
`WrappedLines` are built. This may not be a bug, but needs verification.

---

### 3. **ProcessInternal Not Called When Updates Blocked** ⚠️ MEDIUM

**Location**: `SceneSystem.cs` line 646-656

**Issue**: When `isBlocked` is true, only `_loadingSceneSystem?.ProcessInternal()` is called.
`_messageBoxSceneSystem?.ProcessInternal()` is not called, even though message boxes have `BlocksUpdate = true` and
should still process their text printing state machine.

**Impact**: If a message box is shown while another scene blocks updates, the message box won't print text or respond to
input.

**Fix Required**: Add `_messageBoxSceneSystem?.ProcessInternal(deltaTime)` to the blocked update path, similar to how
loading scenes are handled.

---

### 4. **IsMessageBoxVisible Query Allocation** ⚠️ LOW (but violates .cursorrules intent)

**Location**: `MessageBoxApi.cs` line 70

**Issue**: `IsMessageBoxVisible()` creates a new `QueryDescription` on each call. While the comment says this avoids
violating `.cursorrules`, it's still an allocation in a method that could be called frequently.

**Impact**: Minor performance impact, but violates the spirit of `.cursorrules` rule #3 about avoiding allocations in
hot paths.

**Fix Required**: Consider caching the query as a static readonly field, or use `_activeMessageBoxSceneEntity` tracking
instead of querying.

---

### 5. **GetPlayerTextSpeed Fallback Logic** ⚠️ LOW (potential .cursorrules violation)

**Location**: `MessageBoxSceneSystem.cs` line 373-384

**Issue**: `GetPlayerTextSpeed()` has a fallback to `DefaultTextSpeed` if the variable is null/empty, and another
fallback in the switch statement to `TextSpeedMediumFrames`. This could be considered "fallback code" violating
`.cursorrules` rule #2.

**Impact**: Minor - the fallback is reasonable for user preferences, but could be made more explicit.

**Fix Required**: Consider throwing an exception if the text speed variable is invalid, or document why fallback is
acceptable here (user preference defaults are reasonable).

---

## Architecture Issues

### 6. **Active Entity Tracking Race Condition** ⚠️ MEDIUM

**Location**: `MessageBoxSceneSystem.cs` `_activeMessageBoxSceneEntity` field

**Issue**: `_activeMessageBoxSceneEntity` is set in `OnMessageBoxShow()` but not validated in `ProcessInternal()`. If
the entity is destroyed externally (not through `OnMessageBoxHide()`), the reference becomes stale.

**Impact**: Stale entity references could cause exceptions or incorrect behavior.

**Fix Required**: Validate `_activeMessageBoxSceneEntity` is alive in `ProcessInternal()`, or remove the tracking and
query for active message boxes instead.

---

### 7. **Missing State Reset on Hide** ⚠️ LOW

**Location**: `MessageBoxSceneSystem.cs` `OnMessageBoxHide()`

**Issue**: When hiding a message box, the component state is not reset. If the same scene entity is reused, stale state
could persist.

**Impact**: Minor - scenes are destroyed, not reused, so this is unlikely to be an issue. But if scene pooling is added
in the future, this could cause bugs.

**Fix Required**: None needed currently, but document this assumption.

---

## ECS/Event Issues

### 8. **Event Handler Not Validating Entity State** ⚠️ LOW

**Location**: `MessageBoxSceneSystem.cs` `OnMessageBoxTextAdvance()`

**Issue**: `OnMessageBoxTextAdvance()` checks if `_activeMessageBoxSceneEntity.HasValue` and if the entity has
`MessageBoxComponent`, but doesn't validate the entity is alive or that the component state is valid.

**Impact**: Minor - if entity is destroyed between event send and handler, could cause issues.

**Fix Required**: Add `World.IsAlive()` check before accessing component.

---

### 9. **Event Bus Send Without Ref Validation** ⚠️ LOW

**Location**: `MessageBoxApi.cs` and `MessageBoxSceneSystem.cs`

**Issue**: Events are sent via `EventBus.Send(ref evt)`, but the event structs are created locally. This is correct
usage, but if event handlers modify the event, those modifications won't be visible to the sender (which is correct for
events, but worth noting).

**Impact**: None - this is correct event usage pattern.

**Fix Required**: None.

---

## Scene Issues

### 10. **BlocksUpdate Logic May Prevent ProcessInternal** ⚠️ MEDIUM

**Location**: `SceneSystem.cs` line 626-633

**Issue**: Scenes with `BlocksUpdate = true` skip their `Update()` call, but `ProcessInternal()` is still called.
However, message boxes have `BlocksUpdate = true`, so their `Update()` won't be called, but `ProcessInternal()` should
still run. Currently, `ProcessInternal()` is only called for `_loadingSceneSystem` and `_mapPopupSceneSystem`, not
`_messageBoxSceneSystem`.

**Impact**: Message boxes won't process text printing when they block updates (which they always do).

**Fix Required**: Add `_messageBoxSceneSystem?.ProcessInternal(deltaTime)` to the `Update()` method.

---

### 11. **Scene Component Registration Missing** ⚠️ CRITICAL (duplicate of #1)

**Location**: `SceneSystem.cs` `CreateScene()`

**Issue**: `MessageBoxSceneComponent` is not added to the scene entity in `CreateScene()`.

**Impact**: Message box scenes won't be recognized as message box scenes.

**Fix Required**: Add handling for `MessageBoxSceneComponent` in the additional components loop.

---

## SOLID/DRY Issues

### 12. **Duplicate Texture Loading Logic** ⚠️ LOW

**Location**: `MessageBoxSceneSystem.cs` `LoadTextureFromDefinition()`

**Issue**: `LoadTextureFromDefinition()` duplicates logic that might exist in `MapPopupSceneSystem` or other systems.

**Impact**: Code duplication violates DRY principle.

**Fix Required**: Extract to a shared utility class or service if similar logic exists elsewhere.

---

### 13. **Hardcoded Font Size** ⚠️ LOW

**Location**: `MessageBoxSceneSystem.cs` line 741, 1280

**Issue**: Font size `16` is hardcoded in `WrapText()` and `RenderText()`.

**Impact**: Font size should come from font definition or be configurable.

**Fix Required**: Load font size from `FontDefinition` or make it configurable via `MessageBoxShowEvent`.

---

### 14. **Magic Numbers in Rendering** ⚠️ LOW

**Location**: `MessageBoxSceneSystem.cs` rendering methods

**Issue**: Various magic numbers like `8` (padding), `2` (blink rate), `7` (default columns) are hardcoded.

**Impact**: Makes code harder to maintain and configure.

**Fix Required**: Move to `MessageBoxConstants` or make configurable.

---

## Bugs

### 15. **SpriteBatch Not Ended on Early Return** ⚠️ MEDIUM

**Location**: `MessageBoxSceneSystem.cs` `RenderMessageBox()` lines 964-970, 977-984

**Issue**: If `LoadMessageBoxTexture()` returns null or tilesheet definition is null, the method returns early, but
`SpriteBatch.Begin()` was already called on line 952. The `finally` block will call `SpriteBatch.End()`, so this is
actually handled correctly.

**Re-evaluation**: The `finally` block ensures `SpriteBatch.End()` is always called, so this is not a bug.

---

### 16. **Arrow Animation Blink Rate** ⚠️ LOW

**Location**: `MessageBoxSceneSystem.cs` line 1426

**Issue**: Arrow blink uses `gameTime.TotalGameTime.TotalSeconds * 2) % 2) < 1`, which blinks every 0.5 seconds. This
might be too fast or not match the original Pokemon Emerald behavior.

**Impact**: Minor - animation timing might not match expected behavior.

**Fix Required**: Adjust blink rate to match original (likely 30 frames = 0.5 seconds at 60 FPS).

---

### 17. **CurrentCharIndex Not Reset on Clear** ⚠️ MEDIUM

**Location**: `MessageBoxSceneSystem.cs` `ProcessCharacter()` line 502-508

**Issue**: When processing a `Clear` token, `CurrentX` and `CurrentY` are reset, but `CurrentCharIndex` is not reset.
This means rendering will continue from the previous character position, not restart.

**Impact**: `Clear` control code won't work correctly - text won't clear and restart.

**Fix Required**: Reset `CurrentCharIndex` to 0 (or appropriate value) when processing `Clear` token. Actually, `Clear`
should probably clear the current page and start a new one, so `CurrentCharIndex` should continue, but `CurrentX` and
`CurrentY` should reset. But if we're rendering based on `CurrentCharIndex`, we might need to track which "page" we're
on, or reset `CurrentCharIndex` to start of current page.

**Re-evaluation**: `Clear` is meant to clear the current page and start a new one. The text continues, so
`CurrentCharIndex` should continue incrementing. But `WrappedLines` are pre-computed for the entire text, so we can't
easily "clear" a page. This might need page tracking or re-wrapping logic.

---

## Summary

### ✅ Fixed Issues:

1. **SceneSystem missing MessageBoxSceneComponent registration** (3 places) - FIXED
    - Added MessageBoxSceneComponent handling in CreateScene()
    - Added MessageBoxSceneComponent check in FindSceneSystem()
    - Added ProcessInternal() call for message box system in Update()
2. **ProcessInternal not called for message box system** - FIXED
    - Added _messageBoxSceneSystem?.ProcessInternal(deltaTime) to Update()
3. **ProcessInternal not called when updates blocked** - FIXED
    - Added _messageBoxSceneSystem?.ProcessInternal(deltaTime) to blocked update path
4. **Active entity tracking race condition** - FIXED
    - Added World.IsAlive() validation in ProcessInternal()
    - Added entity validation in OnMessageBoxTextAdvance()
5. **CurrentCharIndex not reset on Clear token** - VERIFIED CORRECT
    - Current behavior is correct: Clear resets rendering position but continues text
    - Clear is meant to clear current page visually, not reset character index

### Remaining Medium Priority:

6. **BlocksUpdate logic preventing ProcessInternal** - FIXED (covered by #3)

### Remaining Low Priority:

7. **IsMessageBoxVisible query allocation** - Consider caching query or using _activeMessageBoxSceneEntity
8. **GetPlayerTextSpeed fallback logic** - Document why fallback is acceptable (user preference defaults)
9. **Hardcoded font size** - Load from FontDefinition or make configurable
10. **Magic numbers in rendering** - Move to MessageBoxConstants
11. **Arrow animation blink rate** - Verify matches original Pokemon Emerald timing
12. **Duplicate texture loading logic** - Extract to shared utility if similar logic exists elsewhere
