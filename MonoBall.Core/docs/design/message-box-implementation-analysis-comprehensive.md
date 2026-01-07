# Message Box Implementation - Comprehensive Analysis

## Executive Summary

The message box implementation has a **fundamental design flaw** in how `CurrentCharIndex` tracks progress through text.
This causes rendering to fail when newlines are encountered, leading to text not advancing to the next line.

## Critical Bugs

### Bug #1: CurrentCharIndex Doesn't Advance for Newlines (CRITICAL)

**Location**: `MessageBoxSceneSystem.ProcessCharacter()` line 502-508

**Problem**:

- When a `Newline` token is processed, `CurrentTokenIndex` advances but `CurrentCharIndex` does NOT advance
- This causes `CurrentCharIndex` to remain at the same value as the previous line's `EndIndex`
- When the next line starts at the same `StartIndex`, rendering logic cannot distinguish between "finished previous
  line" and "starting next line"

**Example**:

```
Text: "Player Statistics\n\nTotal Steps: 0"
After wrapping:
  Line 0: "Player Statistics", StartIndex=0, EndIndex=17
  Line 1: "" (empty), StartIndex=17, EndIndex=17
  Line 2: "Total Steps: 0", StartIndex=17, EndIndex=32

Processing:
  1. Process chars 0-16: CurrentCharIndex = 17 ✓
  2. Process newline token: CurrentCharIndex = 17 (STUCK!) ✗
  3. Process newline token: CurrentCharIndex = 17 (STUCK!) ✗
  4. Process char 'T': CurrentCharIndex = 18 ✓

Rendering with CurrentCharIndex=17:
  - Line 0: 17 == 17 && 0 < 17 → renders ✓
  - Line 1: 17 == 17 && 17 == 17 → empty line, skips ✓
  - Line 2: 17 == 17 && 17 < 32 → tries to render but substringLength = 0 → nothing renders ✗
```

**Root Cause**: The design assumes `CurrentCharIndex` represents "characters processed", but newlines aren't characters,
so they don't advance the index. However, the rendering logic needs `CurrentCharIndex` to represent "position in the
text stream" including newlines.

**Impact**: Text stops rendering after the first line when newlines are present.

### Bug #2: Rendering Logic is Overly Complex and Fragile

**Location**: `MessageBoxSceneSystem.RenderText()` lines 1429-1505

**Problem**:

- Multiple nested conditions trying to handle edge cases
- Logic tries to handle empty lines, partial lines, complete lines, and boundary conditions
- The conditions are hard to reason about and have subtle bugs

**Issues**:

1. Empty lines (`StartIndex == EndIndex`) are handled inconsistently
2. Boundary condition when `CurrentCharIndex == StartIndex == EndIndex` is ambiguous
3. The logic tries to render lines "past" the current position, which is conceptually wrong

### Bug #3: WrapText and Rendering Use Different Indexing Schemes

**Location**: `MessageBoxSceneSystem.WrapText()` vs `RenderText()`

**Problem**:

- `WrapText()` uses `charIndex` that doesn't increment for newlines (correct for wrapping)
- `RenderText()` uses `CurrentCharIndex` that also doesn't increment for newlines
- But rendering needs to know "which line are we on" not "which character index"

**Impact**: The two systems are misaligned, causing rendering to fail.

## Architecture Issues

### Issue #1: Dual Tracking of Active Entity

**Location**: `MessageBoxSceneSystem._activeMessageBoxSceneEntity` + query-based approach

**Problem**:

- System tracks `_activeMessageBoxSceneEntity` for single-message-box policy
- But `ProcessInternal()` queries for ALL message box scenes
- This creates potential inconsistency if multiple scenes exist

**Recommendation**: Either use tracking OR query, not both. Since we enforce single message box, tracking is sufficient.

### Issue #2: CurrentCharIndex vs CurrentTokenIndex Confusion

**Location**: Throughout `MessageBoxComponent` and processing logic

**Problem**:

- `CurrentTokenIndex` tracks position in token list (correct)
- `CurrentCharIndex` tracks position in character stream (incorrect for rendering)
- Rendering uses `CurrentCharIndex` to determine which lines to show, but this doesn't work for newlines

**Recommendation**:

- Option A: Make `CurrentCharIndex` advance for newlines (treat newlines as "characters" for rendering purposes)
- Option B: Track "current line index" separately from character index
- Option C: Use `CurrentTokenIndex` to determine rendering state instead of `CurrentCharIndex`

### Issue #3: Rendering Logic Should Be Token-Based, Not Character-Based

**Location**: `RenderText()` method

**Problem**:

- Rendering tries to use `CurrentCharIndex` to determine which lines to render
- But `CurrentCharIndex` doesn't advance for newlines, so it can't accurately represent "how much text has been
  processed"
- Should instead use `CurrentTokenIndex` to determine which tokens have been processed, then render corresponding lines

**Recommendation**: Refactor rendering to be token-based:

1. Determine which tokens have been processed using `CurrentTokenIndex`
2. Map tokens to lines (pre-compute this during wrapping)
3. Render lines based on token processing state

## ECS/Event Issues

### Issue #1: Event Subscription in Constructor

**Location**: `MessageBoxSceneSystem` constructor line 101-103

**Status**: ✅ CORRECT

- Events are subscribed in constructor
- Unsubscribed in `Dispose()`
- Follows `.cursorrules` requirement

### Issue #2: QueryDescription Caching

**Location**: `MessageBoxSceneSystem` constructor line 93-97

**Status**: ✅ CORRECT

- `_messageBoxScenesQuery` is cached in constructor
- Never created in hot paths
- Follows `.cursorrules` requirement

### Issue #3: Event-Driven Architecture

**Status**: ✅ CORRECT

- `MessageBoxShowEvent` → creates scene
- `MessageBoxHideEvent` → destroys scene
- `MessageBoxTextAdvanceEvent` → advances text
- Clean separation of concerns

## SOLID/DRY Issues

### Issue #1: Rendering Logic Duplication

**Location**: `RenderText()` lines 1435-1505

**Problem**:

- Multiple conditions checking `CurrentCharIndex` vs `line.EndIndex`/`line.StartIndex`
- Similar logic repeated for different cases
- Hard to maintain and test

**Recommendation**: Extract rendering decision logic into separate method:

```csharp
private bool ShouldRenderLine(WrappedLine line, int currentCharIndex, out bool isComplete, out bool isPartial)
```

### Issue #2: Text Processing Logic Scattered

**Location**: `ProcessCharacter()`, `WrapText()`, `RenderText()`

**Problem**:

- Text processing logic is spread across multiple methods
- Hard to understand the flow
- Changes in one place can break others

**Recommendation**: Create a `TextProcessor` class that handles:

- Token parsing
- Text wrapping
- Rendering state calculation
- Single responsibility: text processing

### Issue #3: Magic Numbers in Rendering

**Location**: Various rendering methods

**Status**: ✅ MOSTLY GOOD

- Uses `MessageBoxConstants` for most values
- Some hardcoded values in `DrawDialogueFrame` (tile indices)

## Design vs Implementation Gaps

### Gap #1: Character Index Tracking

**Design**: Assumes `CurrentCharIndex` tracks "characters processed" including newlines
**Implementation**: `CurrentCharIndex` doesn't advance for newlines
**Impact**: Rendering fails for multi-line text

### Gap #2: Line Rendering Strategy

**Design**: Implicitly assumes lines are rendered based on character index
**Implementation**: Tries to render based on character index but fails for newlines
**Impact**: Complex, fragile rendering logic

### Gap #3: Empty Line Handling

**Design**: Empty lines should be preserved (spacing)
**Implementation**: Empty lines are created but rendering logic struggles with them
**Impact**: Inconsistent rendering behavior

## Recommended Fixes

### Fix #1: Make CurrentCharIndex Advance for Newlines (SIMPLEST)

**Change**: In `ProcessCharacter()`, when processing a `Newline` token, increment `CurrentCharIndex`:

```csharp
case TextTokenType.Newline:
    msgBox.CurrentTokenIndex++;
    msgBox.CurrentCharIndex++; // ADD THIS LINE
    msgBox.CurrentY++;
    msgBox.CurrentX = msgBox.StartX;
    msgBox.DelayCounter = msgBox.TextSpeed;
    break;
```

**Rationale**:

- Treats newlines as "characters" for rendering purposes
- Makes `CurrentCharIndex` accurately represent "position in text stream"
- Simplifies rendering logic

**Trade-off**: `CurrentCharIndex` no longer represents "visible characters only", but this is acceptable since it's used
for rendering, not character counting.

### Fix #2: Simplify Rendering Logic

**Change**: After Fix #1, simplify rendering to:

```csharp
foreach (var line in msgBox.WrappedLines)
{
    if (msgBox.CurrentCharIndex > line.EndIndex)
    {
        // Render complete line
        if (!string.IsNullOrEmpty(line.Text))
        {
            RenderTextLine(...);
        }
        lineY += lineSpacing;
    }
    else if (msgBox.CurrentCharIndex > line.StartIndex)
    {
        // Render partial line
        int substringLength = msgBox.CurrentCharIndex - line.StartIndex;
        if (substringLength > 0 && substringLength <= line.Text.Length)
        {
            RenderTextLine(...);
        }
        break;
    }
    else
    {
        // Line not reached yet
        break;
    }
}
```

**Rationale**: With `CurrentCharIndex` advancing for newlines, the logic becomes much simpler.

### Fix #3: Update WrapText to Match

**Change**: In `WrapText()`, increment `charIndex` for newlines:

```csharp
else if (token.TokenType == TextTokenType.Newline)
{
    wrappedLines.Add(...);
    currentLine.Clear();
    currentWidth = 0;
    lineStartIndex = charIndex;
    charIndex++; // ADD THIS LINE - treat newline as character for indexing
}
```

**Rationale**: Makes wrapping consistent with processing - newlines are part of the character stream for indexing
purposes.

## Testing Recommendations

1. **Test Case 1**: Single line text
    - Input: `"Hello"`
    - Expected: Renders correctly

2. **Test Case 2**: Text with newline
    - Input: `"Line 1\nLine 2"`
    - Expected: Both lines render

3. **Test Case 3**: Text with consecutive newlines
    - Input: `"Line 1\n\nLine 3"`
    - Expected: Line 1, empty line (spacing), Line 3

4. **Test Case 4**: Text with newline at start
    - Input: `"\nLine 2"`
    - Expected: Empty line, then Line 2

5. **Test Case 5**: Text with newline at end
    - Input: `"Line 1\n"`
    - Expected: Line 1, then empty line

## Conclusion

The core issue is that `CurrentCharIndex` doesn't advance for newlines, causing rendering to fail. The fix is simple:
treat newlines as "characters" for indexing purposes, even though they're not visible characters. This aligns the
indexing scheme between wrapping, processing, and rendering.


