# MessageBoxSceneSystem Responsibilities Analysis

## Current Responsibilities

`MessageBoxSceneSystem` handles the following responsibilities:

1. **Event Subscription/Handling** (3 event handlers)
    - `OnMessageBoxShow()` - Creates message box scene
    - `OnMessageBoxHide()` - Destroys message box scene
    - `OnMessageBoxTextAdvance()` - Handles text advancement

2. **Scene Lifecycle Management**
    - Creating scene entities via `_sceneManager.CreateScene()`
    - Destroying scene entities via `_sceneManager.DestroyScene()`
    - Tracking active message box entity

3. **Text Processing** (2 methods)
    - `ParseControlCodes()` - Parses text into tokens (now uses strategy pattern)
    - `WrapText()` - Wraps text into lines based on width

4. **State Machine Management** (2 methods)
    - `Update()` - Main state machine loop (HandleChar, Wait, Scrolling, Paused, Finished)
    - `ProcessCharacter()` - Processes individual tokens in state machine

5. **Input Handling** (1 method)
    - `HandleInput()` - Handles A/B button presses for speed-up and advancement

6. **Rendering** (5 methods)
    - `RenderScene()` - ISceneSystem interface method
    - `RenderMessageBox()` - Main rendering coordinator
    - `RenderText()` - Renders text content
    - `RenderTextLine()` - Renders single line with shadow
    - `RenderDownArrow()` - Renders blinking arrow indicator
    - `DrawDialogueFrame()` - Renders message box frame tiles

7. **Texture Management** (2 methods)
    - `LoadMessageBoxTexture()` - Loads and caches tilesheet texture
    - `LoadTextureFromDefinition()` - Helper for texture loading

8. **Helper Methods** (6 methods)
    - `ValidateAndGetFont()` - Font validation
    - `ValidateAndGetTilesheet()` - Tilesheet validation
    - `GetPlayerTextSpeed()` - Gets player preference
    - `GetScrollSpeed()` - Maps text speed to scroll speed
    - `AdvanceToNextPage()` - Page break logic
    - `StartScrollAnimation()` - Scroll animation logic

**Total**: ~21 methods, ~2100 lines

---

## Comparison with MapPopupSceneSystem

`MapPopupSceneSystem` handles similar responsibilities:

1. **Event Subscription/Handling** (2 event handlers)
    - `OnMapPopupShow()` - Creates popup scene
    - `OnMapPopupHide()` - Destroys popup scene

2. **Scene Lifecycle Management**
    - Creating/destroying scene entities
    - Tracking current popup entity

3. **Animation State Management** (1 method)
    - `Update()` - Updates popup animation states

4. **Rendering** (multiple methods)
    - `RenderScene()` - ISceneSystem interface method
    - `RenderMapPopupScene()` - Main rendering coordinator
    - `DrawPopupFrame()` - Renders popup frame
    - `RenderText()` - Renders text content
    - `RenderTextLine()` - Renders single line

5. **Texture Management** (multiple methods)
    - Texture loading and caching
    - `LoadTextureFromDefinition()` - Helper for texture loading

**Total**: Similar number of responsibilities, similar complexity

---

## Is This Actually a Problem?

### Arguments FOR splitting (Single Responsibility Principle):

- System is large (~2100 lines)
- Multiple concerns mixed together
- Harder to test individual components
- Harder to reuse text processing logic elsewhere

### Arguments AGAINST splitting (Pragmatic considerations):

- **Consistent with codebase pattern**: `MapPopupSceneSystem` has similar responsibilities
- **Cohesive domain**: All responsibilities are related to message box functionality
- **Performance**: Keeping everything together avoids cross-system communication overhead
- **Simplicity**: Single system is easier to understand than multiple systems/services
- **ECS pattern**: Scene systems are meant to handle all aspects of their scene type

### Key Insight:

The "mixed responsibilities" concern is **theoretical** (violates SRP) but **pragmatic** (follows established codebase
pattern). This is a **design trade-off**, not a bug.

---

## Recommendation

**Status**: ⚠️ **Acceptable Trade-off** - Not a critical issue

The system follows the same pattern as `MapPopupSceneSystem` and other scene systems. While it violates strict SRP,
it's:

- Consistent with codebase architecture
- Cohesive (all responsibilities are message box-related)
- Performant (no cross-system overhead)
- Maintainable (everything in one place)

**If refactoring is desired**, the split would be:

1. `MessageBoxTextProcessor` service - Text parsing/wrapping (reusable)
2. `MessageBoxRenderer` service - Rendering logic (could be shared with other UI)
3. `MessageBoxSceneSystem` - Lifecycle, state machine, input handling

**However**, this would:

- Require significant refactoring
- Break consistency with `MapPopupSceneSystem`
- Add complexity (more classes, more dependencies)
- Potentially hurt performance (more indirection)

**Conclusion**: This is acceptable as-is. The system is well-organized internally with clear method separation. The "
mixed responsibilities" is a theoretical concern that doesn't cause practical problems.

