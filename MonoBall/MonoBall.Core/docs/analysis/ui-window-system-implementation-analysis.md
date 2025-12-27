# UI Window System Implementation Analysis

## Overview

This document analyzes the UI Window System implementation for architecture issues, SOLID/DRY violations, Arch ECS/event issues, `.cursorrule` compliance, and definition file inconsistencies.

---

## 🔴 CRITICAL ISSUES

### 1. MessageBoxContentRenderer.SetComponent Pattern Violates Interface Contract

**Location**: `MonoBall.Core/UI/Windows/Content/MessageBoxContentRenderer.cs`

**Issue**: 
- `MessageBoxContentRenderer` implements `IContentRenderer` but requires `SetComponent()` to be called before `RenderContent()`.
- This violates the **Liskov Substitution Principle** - code using `IContentRenderer` cannot use `MessageBoxContentRenderer` without knowing about `SetComponent()`.
- The component state is stored as a mutable field, making the renderer stateful and not thread-safe.

**Impact**: 
- Breaks polymorphism - cannot treat all `IContentRenderer` implementations uniformly.
- Creates hidden dependencies - callers must know about `SetComponent()`.
- Thread-safety concerns if renderers are ever used concurrently.

**Recommendation**:
- **Option A**: Pass component as parameter to `RenderContent()` (requires interface change).
- **Option B**: Create specialized interface `IMessageBoxContentRenderer` with `RenderContent(ref MessageBoxComponent, ...)`.
- **Option C**: Store component reference in constructor (but component changes each frame, so this doesn't work).

**Preferred Solution**: Option B - Create specialized interface since message box rendering is unique.

---

### 2. Renderers Created Every Frame (Performance Issue)

**Location**: 
- `MonoBall.Core/ECS/Systems/MapPopupRendererSystem.cs` (lines 178-203)
- `MonoBall.Core/Scenes/Systems/MessageBoxSceneSystem.cs` (lines 1467-1500)

**Issue**:
- New renderer instances are created every frame in `RenderPopup()` and `RenderMessageBox()`.
- Renderers contain cached data (tile lookups, font systems) that could be reused.
- Unnecessary allocations in hot rendering paths.

**Impact**:
- Performance degradation - allocations every frame.
- Garbage collection pressure.
- Violates DRY - same renderer setup code runs repeatedly.

**Recommendation**:
- Cache renderers as instance fields in systems.
- Recreate renderers only when definitions/textures change.
- For `MessageBoxContentRenderer`, create new instance per message box entity (component state is per-entity).

---

### 3. WindowBounds Calculation Incorrect for MessageBox

**Location**: `MonoBall.Core/Scenes/Systems/MessageBoxSceneSystem.cs` (line 1511)

**Issue**:
- MessageBox border is **non-uniform**: 2 tiles left, 1 tile top/bottom/right.
- `WindowBounds.FromInterior()` uses uniform `borderThickness` parameter.
- Using `tileSize * 2` (maximum thickness) causes incorrect outer bounds calculation.

**Impact**:
- Outer bounds are incorrect (too large on top/right/bottom).
- May cause rendering issues if outer bounds are used for clipping or hit testing.

**Recommendation**:
- **Option A**: Create `WindowBounds.FromInteriorNonUniform()` method.
- **Option B**: Calculate bounds manually in `MessageBoxSceneSystem` (don't use `WindowBounds`).
- **Option C**: Document that `WindowBounds` is approximate for non-uniform borders.

**Preferred Solution**: Option B - Calculate bounds manually since MessageBox is the only non-uniform case.

---

## 🟡 MEDIUM PRIORITY ISSUES

### 4. SimpleTextContentRenderer Scale Calculation is Fragile

**Location**: `MonoBall.Core/UI/Windows/Content/SimpleTextContentRenderer.cs` (lines 98-99)

**Issue**:
```csharp
int baseFontSize = _constants.Get<int>("PopupBaseFontSize");
int scale = baseFontSize > 0 ? _scaledFontSize / baseFontSize : 1;
```
- Calculates scale by dividing scaled font size by base font size.
- Assumes integer division works correctly (may lose precision).
- Fragile - breaks if base font size changes or if scaled size isn't exact multiple.

**Impact**:
- Incorrect scale calculation in edge cases.
- Padding/offset calculations may be wrong.

**Recommendation**:
- Pass scale as constructor parameter (caller already knows scale).
- Remove scale calculation from renderer.

---

### 5. Font Validation Redundancy

**Location**: `MonoBall.Core/UI/Windows/Content/SimpleTextContentRenderer.cs`

**Issue**:
- Font is validated in constructor (throws `InvalidOperationException`).
- Font is checked again in `RenderContent()` (logs warning, returns early).
- Redundant validation - if font doesn't exist, constructor should fail fast.

**Impact**:
- Inconsistent error handling (exception vs. silent failure).
- Redundant checks.

**Recommendation**:
- Remove font check from `RenderContent()` - rely on constructor validation.
- If font system can change at runtime, document this and keep both checks.

---

### 6. TileSheetBackgroundRenderer Hardcoded Default

**Location**: `MonoBall.Core/UI/Windows/Backgrounds/TileSheetBackgroundRenderer.cs` (line 81)

**Issue**:
```csharp
int columns = _tilesheetDef.TileCount > 0
    ? (int)System.Math.Sqrt(_tilesheetDef.TileCount)
    : 7; // Default columns for message box tilesheet
```
- Hardcoded `7` instead of using constants service.
- Should use `DefaultTilesheetColumns` constant.

**Impact**:
- Inconsistent with other code that uses constants.
- Hard to maintain if default changes.

**Recommendation**:
- Pass `IConstantsService` to constructor and use `DefaultTilesheetColumns` constant.

---

### 7. Missing Exception Documentation

**Location**: Multiple renderer classes

**Issue**:
- Some methods don't document all exceptions they can throw.
- `RenderContent()` methods don't document potential exceptions from font/texture operations.

**Impact**:
- Incomplete API documentation.
- Callers may not handle all exceptions.

**Recommendation**:
- Add `<exception>` tags for all possible exceptions.
- Document exceptions from font system, texture operations, etc.

---

## 🟢 MINOR ISSUES / CODE QUALITY

### 8. MessageBoxContentRenderer Missing XML Documentation

**Location**: `MonoBall.Core/UI/Windows/Content/MessageBoxContentRenderer.cs`

**Issue**:
- `SetComponent()` method lacks XML documentation.
- `RenderTextLine()` private method lacks XML documentation.

**Impact**:
- Incomplete documentation.

**Recommendation**:
- Add XML documentation to all public and protected methods.

---

### 9. WindowRenderer Logger Not Used

**Location**: `MonoBall.Core/UI/Windows/WindowRenderer.cs`

**Issue**:
- `_logger` field is stored but never used.
- No logging in `Render()` method.

**Impact**:
- Unused dependency.
- No visibility into rendering issues.

**Recommendation**:
- Either remove logger (if not needed) or add logging for null renderer warnings, rendering errors, etc.

---

### 10. Inconsistent Error Handling

**Location**: Multiple renderer classes

**Issue**:
- Some renderers throw exceptions (constructor validation).
- Some renderers log warnings and return early (`RenderContent()` methods).
- Inconsistent approach to error handling.

**Impact**:
- Unclear error handling contract.
- Some errors are fatal, others are silent.

**Recommendation**:
- Document error handling strategy:
  - Constructor validation: Fail fast with exceptions.
  - Runtime errors (missing font/texture): Log warning and return early (rendering should be resilient).

---

## ✅ SOLID PRINCIPLES ANALYSIS

### Single Responsibility Principle (SRP)
✅ **GOOD**: Each renderer has a single responsibility:
- `PopupTileSheetBorderRenderer` - renders popup borders only.
- `BitmapBackgroundRenderer` - renders bitmap backgrounds only.
- `SimpleTextContentRenderer` - renders simple text only.

⚠️ **ISSUE**: `MessageBoxContentRenderer` handles both component state management and rendering.

### Open/Closed Principle (OCP)
✅ **GOOD**: New renderers can be added without modifying existing code (pluggable interfaces).

### Liskov Substitution Principle (LSP)
❌ **VIOLATION**: `MessageBoxContentRenderer` cannot be substituted for `IContentRenderer` without calling `SetComponent()` first.

### Interface Segregation Principle (ISP)
✅ **GOOD**: Interfaces are focused (`IBorderRenderer`, `IBackgroundRenderer`, `IContentRenderer`).

### Dependency Inversion Principle (DIP)
✅ **GOOD**: Systems depend on interfaces, not concrete implementations.

---

## ✅ DRY ANALYSIS

### Code Duplication
✅ **GOOD**: Rendering logic extracted from systems into reusable renderers.

⚠️ **ISSUE**: Renderer creation code duplicated in `MapPopupRendererSystem` and `MessageBoxSceneSystem`.

**Recommendation**: Extract renderer factory methods or builder pattern.

---

## ✅ .cursorrule COMPLIANCE

### Namespace Structure
✅ **GOOD**: Matches folder structure (`MonoBall.Core.UI.Windows.*`).

### File Organization
✅ **GOOD**: One class per file, PascalCase naming.

### XML Documentation
⚠️ **PARTIAL**: Most public APIs documented, but some methods missing documentation.

### Constructor Validation
✅ **GOOD**: All constructors validate required dependencies with null checks.

### Nullable Types
✅ **GOOD**: Proper use of nullable reference types (`IBorderRenderer?`, etc.).

### Fail-Fast
✅ **GOOD**: Constructor validation throws exceptions for invalid state.

---

## ✅ ARCH ECS / EVENT ANALYSIS

### ECS Integration
✅ **GOOD**: Window system is presentation layer, not directly integrated with ECS components.
- Systems create renderers and call them.
- No ECS-specific code in window system.

### Event System
✅ **GOOD**: No event subscriptions in window system (no disposal needed).

---

## ✅ DEFINITION FILE CONSISTENCY

### Constants Usage
✅ **GOOD**: Constants are used consistently:
- `PopupBaseFontSize`, `PopupTextPadding`, etc. for popups.
- `DefaultFontSize`, `TextPaddingX`, `MaxVisibleLines`, etc. for message boxes.

⚠️ **ISSUE**: `TileSheetBackgroundRenderer` hardcodes default columns instead of using constant.

---

## 📋 SUMMARY OF RECOMMENDATIONS

### High Priority
1. **Fix MessageBoxContentRenderer interface violation** - Create specialized interface.
2. **Cache renderers** - Don't create new instances every frame.
3. **Fix WindowBounds calculation** - Handle non-uniform borders correctly.

### Medium Priority
4. **Pass scale to SimpleTextContentRenderer** - Don't calculate from font size.
5. **Remove redundant font validation** - Rely on constructor validation.
6. **Use constants for default columns** - Don't hardcode values.
7. **Add missing exception documentation** - Complete XML docs.

### Low Priority
8. **Add missing XML documentation** - Complete all method docs.
9. **Use or remove logger** - Either log or remove unused dependency.
10. **Document error handling strategy** - Clarify when to throw vs. log.

---

## 🎯 IMPLEMENTATION PRIORITY

1. **Critical**: Fix MessageBoxContentRenderer interface violation.
2. **Critical**: Cache renderers for performance.
3. **High**: Fix WindowBounds calculation for MessageBox.
4. **Medium**: Pass scale to SimpleTextContentRenderer.
5. **Medium**: Use constants for hardcoded values.
6. **Low**: Complete XML documentation.

