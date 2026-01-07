# Debug Panel Feature Analysis - Port from OldMonoBall to New Framework
**Research Date:** 2026-01-05
**Researcher:** Research Agent (Hive Mind)
**Task ID:** research-1
**Status:** COMPLETE

---

## Executive Summary

Comprehensive analysis of OldMonoBall's diagnostic debug panel system to identify ALL features that need to be ported to the new TypeScript/WebGPU framework. The C# implementation has 9 fully-featured panels with sophisticated UX patterns, while the new framework currently has NO debug panel implementation.

**Key Finding:** OldMonoBall has an enterprise-grade debug panel system (similar to Unity/Unreal editors) that provides real-time diagnostics, performance monitoring, ECS inspection, and developer tools. The new framework needs ALL of these capabilities ported.

---

## Current Implementation Inventory (OldMonoBall C#)

### Panel Count: 9 Major Panels
Located: `/MonoBall/MonoBall.Core/Diagnostics/Panels/`

1. **PerformancePanel** - Real-time performance metrics
2. **ConsolePanel** - Interactive command console
3. **EntityInspectorPanel** - ECS entity/component browser
4. **LogsPanel** - Application log viewer with filtering
5. **SystemProfilerPanel** - ECS system execution timing
6. **EventInspectorPanel** - Event bus monitoring
7. **SceneInspectorPanel** - Scene stack visualization
8. **ModBrowserPanel** - Mod/plugin manager
9. **DefinitionBrowserPanel** - Data definition viewer

### Architecture Components

**Core Services:**
- `DebugPanelRegistry` - Panel lifecycle management
- `DebugOverlayService` - Main debug UI coordinator
- `DebugPanelRenderSystem` - ImGui rendering system
- `ImGuiLifecycleSystem` - ImGui integration
- `DebugPanelStateService` - Panel state persistence

**UI Infrastructure:**
- `DebugToolbar` - Quick access toolbar
- `DebugColors` - Pokéball-themed color scheme
- `DebugUIIndicators` - Visual status indicators
- `MetricsTracker` - Shared performance tracking
- `TableSortHelper` - Sortable table utilities
- `NerdFontIcons` - Icon system

**Console System:**
- `ConsoleService` - Command execution engine
- `ConsoleCommandRegistry` - Command registration
- `ConsoleHistory` - Command history navigation
- `CompletionItem` - Auto-completion system
- Built-in commands for debugging

---

## Feature Inventory by Panel

### 1. PerformancePanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Real-time FPS display with color coding (green >60, yellow 30-60, red <30)
- ✅ Frame time graph (120-sample history) with sparklines
- ✅ Min/Max/Average frame time tracking
- ✅ GC heap memory monitoring (MB)
- ✅ Garbage collection statistics (Gen0/Gen1/Gen2 counts)
- ✅ Adjustable refresh interval (0.1s - 2s)
- ✅ Manual GC trigger button
- ✅ Target frame time comparison (60 FPS / 16.67ms)
- ✅ Automatic color-coded warnings for performance issues

**Implementation Details:**
- Ring buffer for frame history (prevents allocations)
- Stopwatch-based timing (high precision)
- IDebugPanelLifecycle with Update() for continuous monitoring
- ImGui PlotLines for visualization
- Frame time budget indicators

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 2. ConsolePanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Interactive command-line interface
- ✅ Auto-completion popup with fuzzy matching
- ✅ Command history navigation (Up/Down arrows)
- ✅ Multi-line completion items (name + description)
- ✅ Color-coded output by severity
- ✅ Scrollable output buffer (ring buffer, 1000 entries)
- ✅ Auto-scroll to bottom on new messages
- ✅ Category badges for completion items (command/alias/arg)
- ✅ Keyboard shortcuts (Tab=complete, Enter=submit, Escape=close)
- ✅ Thread-safe command execution
- ✅ Welcome message on startup
- ✅ Support for commands with arguments

**Implementation Details:**
- Unsafe byte buffer for text input (4KB max)
- ImGui InputText with callback system
- Smart completion dismissal (space/semicolon/braces)
- Prevents double-submit bug (completion vs submit)
- ConsoleService backend with command registry
- Event-driven architecture

**Built-in Commands:**
```csharp
- help [command]
- clear
- spawn <entity>
- list [systems|events|entities]
- set <property> <value>
- time <scale|pause|resume>
```

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 3. EntityInspectorPanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Real-time entity list with component counts
- ✅ Component filtering (Any/All modes)
- ✅ Search by entity name/ID
- ✅ Master-detail layout (entity list + component inspector)
- ✅ Reflection-based component property display
- ✅ Nested property inspection
- ✅ Auto-refresh with configurable interval
- ✅ Component type discovery and filtering
- ✅ Resizable split panels
- ✅ Entity tooltip showing all components
- ✅ Marker component detection (empty components)
- ✅ Special handling for Entity references

**Implementation Details:**
- Arch ECS QueryDescription caching (performance)
- Ring buffer prevents GC pressure
- Component type registry (HashSet<Type>)
- Reflection for property/field inspection
- Graceful error handling for inaccessible properties

**Component Filter UI:**
- Fuzzy search for component types
- Clear All button
- Match mode toggle (Any vs All)
- Dynamic component type discovery

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 4. LogsPanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Ring buffer (1000 entries max)
- ✅ Thread-safe log ingestion
- ✅ Level filtering (Verbose, Debug, Info, Warning, Error, Fatal)
- ✅ Search filtering
- ✅ Category filtering
- ✅ Auto-scroll to bottom
- ✅ Toggle timestamp/level/category display
- ✅ Color-coded by severity
- ✅ Status bar with counts (total, errors, warnings)
- ✅ Clear logs button
- ✅ Lazy filter updates (performance optimization)
- ✅ Serilog integration via ImGuiLogSink

**Implementation Details:**
- Lock-based synchronization for thread safety
- Deferred filtering (only on draw)
- _filterDirty flag prevents unnecessary recomputation
- TextUnformatted for safe rendering (avoids printf issues)
- Pipeline stats tracking (Sink -> Panel)

**Status Bar Shows:**
- Sink emit count
- Total logs received
- Buffer size
- Filtered count
- Error/warning counts
- Current min level

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 5. SystemProfilerPanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Per-system timing breakdown
- ✅ Last/Avg/Max execution time tracking
- ✅ Sortable table (by name, time, etc.)
- ✅ Active-only filter (hide inactive systems)
- ✅ Search/filter by system name
- ✅ Frame budget usage visualization (progress bars)
- ✅ Color-coded timing (fast/medium/slow)
- ✅ Summary stats (total time, budget %, slowest system)
- ✅ Auto-refresh with configurable interval
- ✅ Activity tracking (shows which systems ran recently)

**Implementation Details:**
- SystemTimingHook subscription (event-based)
- MetricsTracker for rolling averages
- TableSortState for multi-column sorting
- Frame budget calculation (% of 16.67ms)
- Inactive system dimming

**Status Bar Shows:**
- Active system count / Total systems
- Total frame time
- Budget percentage (color-coded)
- Slowest system name

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 6. EventInspectorPanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Event type list with dispatch counts
- ✅ Subscriber count per event
- ✅ Dispatch timing (Last/Avg/Max)
- ✅ Dispatches per second tracking
- ✅ Subscriber details panel (names of subscribed systems)
- ✅ Sortable table
- ✅ Search/filter by event type
- ✅ Color-coded by timing (<0.1ms fast, 0.5ms medium, >0.5ms slow)
- ✅ Master-detail layout
- ✅ Auto-refresh
- ✅ Short name display (strips namespace)

**Implementation Details:**
- EventDispatchHook subscription
- MetricsTracker for timing stats
- Dynamic event discovery
- Time-windowed dispatches/sec calculation
- IDisposable pattern for cleanup

**Detail Panel Shows:**
- Full event type name
- Subscriber count
- Total dispatches
- Dispatches per second
- Timing breakdown
- List of subscriber names

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 7. SceneInspectorPanel - MISSING IN NEW FRAMEWORK

**Features:**
- ✅ Scene stack visualization (priority-ordered)
- ✅ Scene state display (Active/Paused/Inactive)
- ✅ Blocking indicators (Update/Draw/Input)
- ✅ Priority sorting (highest first)
- ✅ Scene details panel
- ✅ Camera mode display
- ✅ Background color visualization
- ✅ Marker component detection
- ✅ Resizable split layout
- ✅ Search/filter by scene ID
- ✅ Auto-refresh

**Implementation Details:**
- Arch ECS query for SceneComponent
- Priority-based sorting
- Compact blocking notation (U/D/I)
- Color-coded state (green=active, yellow=paused, gray=inactive)
- Marker component inspection (GameScene, DebugMenu, MessageBox, etc.)

**Blocking Indicators:**
- U = BlocksUpdate
- D = BlocksDraw
- I = BlocksInput
- Combinations shown (e.g., "U/D/I")

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 8. ModBrowserPanel - MISSING IN NEW FRAMEWORK

**Status:** Panel exists but implementation not analyzed in detail
**Purpose:** Browse and manage mods/plugins
**Likely Features:**
- Mod list with status
- Enable/disable toggles
- Mod metadata display
- Dependency visualization

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

### 9. DefinitionBrowserPanel - MISSING IN NEW FRAMEWORK

**Status:** Panel exists but implementation not analyzed in detail
**Purpose:** Browse game data definitions (items, skills, etc.)
**Likely Features:**
- Definition type browser
- Search/filter
- JSON viewer
- Validation status

**Status:** 🔴 **NOT IMPLEMENTED in new framework**

---

## UX Features (From Research Documents)

### From DIAGNOSTIC_UX_RESEARCH.md

**16 Proven UX Patterns Identified:**

1. **Multi-layer access system** - Toolbar + Menu + Keyboard + Command Palette
2. **Icon-based toolbar** - Visual quick access with badges
3. **Visual status indicators** - At-a-glance health (FPS, errors, warnings)
4. **Command palette** - Ctrl+Shift+P fuzzy search
5. **Workspace presets** - Save/load panel layouts
6. **Floating quick-access** - Persistent status widgets
7. **Keyboard shortcuts** - Ctrl+1-9 for panels, Ctrl+D toggle all
8. **Mini-widgets** - Compact status displays
9. **Contextual quick actions** - Right-click menus, panel buttons
10. **Dockbar with icon-only mode** - Compact vertical/horizontal bar
11. **Search & discovery** - Filter panels, fuzzy matching
12. **Panel state persistence** - Remember visibility, position, size
13. **Progressive disclosure** - Collapsible sections
14. **Responsive sizing** - Content-aware defaults
15. **Theme support** - Dark/light/colorblind variants
16. **Accessibility** - Font scaling, keyboard navigation

**Current Implementation Status in OldMonoBall:**
- ✅ Implemented: #12 (partial), #13 (collapsible headers), #15 (Pokéball theme)
- ❌ Missing: #1-11, #14, #16

**Current Status in New Framework:**
- ❌ **NONE IMPLEMENTED** - New framework has zero debug UI

---

### From diagnostic-panel-ux-analysis.md

**Pain Points Identified:**

1. **"I don't know what tools are available"**
   - Solution: Tooltips, help panel, command palette

2. **"Opening panels takes too many clicks"**
   - Solution: Keyboard shortcuts, icon dock bar

3. **"I can't see system health without opening panels"**
   - Solution: Status bar with FPS/memory/errors

4. **"I lose my layout when I close the window"**
   - Solution: Auto-save/restore, workspace presets

5. **"Related panels don't talk to each other"**
   - Solution: Panel linking via events

**Quick Wins (1-4 hours each):**
1. Keyboard shortcuts for 5 most-used panels
2. Status badge in menu bar
3. Icon dock bar
4. Tooltips on menu items
5. Layout save/load

---

## Architecture Analysis

### Current C# Architecture (OldMonoBall)

**Strengths:**
- ✅ Clean separation of concerns
- ✅ IDebugPanel interface is extensible
- ✅ Lifecycle management (Initialize/Update/Draw/Dispose)
- ✅ Category-based organization
- ✅ Event-driven panel toggling
- ✅ Shared utilities (MetricsTracker, TableSortHelper)
- ✅ Thread-safe where needed (LogsPanel)
- ✅ Performance-conscious (ring buffers, cached queries)
- ✅ ImGui integration mature and polished

**Dependencies:**
- Hexa.NET.ImGui - ImGui bindings for .NET
- Arch.Core - ECS framework
- Serilog - Logging framework
- MonoGame - Graphics framework

**Key Patterns:**
- Interface-based design (IDebugPanel, IDebugPanelLifecycle, IDebugPanelMenu)
- Optional interfaces for features (lifecycle, menus, status)
- Registry pattern for panel management
- Factory pattern for panel creation
- Observer pattern (event hooks)
- Ring buffers for performance
- Cached query descriptions (ECS optimization)

---

### New Framework Requirements (TypeScript/WebGPU)

**Target Stack:**
- TypeScript - Primary language
- WebGPU - Graphics API
- bitECS or similar - ECS framework
- ImGui.js (imgui-js or dear-imgui-wasm) - ImGui for web

**Architecture Recommendations:**

```typescript
// Core interfaces
interface IDebugPanel {
  id: string;
  displayName: string;
  category: string;
  sortOrder: number;
  isVisible: boolean;
  defaultSize?: [number, number];

  initialize?(): void;
  update?(deltaTime: number): void;
  draw(deltaTime: number): void;
  dispose?(): void;
}

interface IStatusProvider {
  getBadge(): PanelStatusBadge | null;
}

// Panel registry
class DebugPanelRegistry {
  private panels: Map<string, IDebugPanel>;
  private categories: Map<string, IDebugPanel[]>;

  register(panel: IDebugPanel): void;
  unregister(id: string): boolean;
  getPanel(id: string): IDebugPanel | null;
  getPanelsByCategory(category: string): IDebugPanel[];
  toggleVisibility(id: string): boolean;
}

// Example panel
class PerformancePanel implements IDebugPanel, IStatusProvider {
  private frameTimeHistory: Float32Array;
  private frameTimeIndex: number;

  getBadge(): PanelStatusBadge {
    return {
      text: `${this.fps.toFixed(0)} FPS`,
      color: this.fps > 60 ? [0,1,0,1] : [1,1,0,1],
      priority: 100
    };
  }

  draw(deltaTime: number): void {
    // ImGui rendering code
  }
}
```

**WebGPU Considerations:**
- ImGui renderer needs WebGPU backend (imgui-js supports WebGL, may need custom backend)
- Performance monitoring via Performance API
- Memory monitoring via performance.memory (Chrome only)
- Thread-safe logging (use worker threads if needed)

---

## Missing Features Summary

### Critical Missing Features (P0 - Must Have)

1. **PerformancePanel** - No performance monitoring at all
   - FPS/frame time tracking
   - Memory monitoring
   - Performance graphs

2. **ConsolePanel** - No developer console
   - Command execution
   - Auto-completion
   - History navigation

3. **Basic Debug UI** - No debug overlay exists
   - Panel rendering system
   - ImGui integration
   - Toolbar/menu structure

### High Priority Missing Features (P1 - Should Have)

4. **EntityInspectorPanel** - Can't inspect ECS entities
   - Entity list
   - Component viewer
   - Property inspection

5. **SystemProfilerPanel** - Can't profile systems
   - System timing
   - Bottleneck detection

6. **LogsPanel** - No log viewer
   - Log streaming
   - Filtering
   - Search

### Medium Priority Missing Features (P2 - Nice to Have)

7. **EventInspectorPanel** - Event bus visibility
8. **SceneInspectorPanel** - Scene stack visualization
9. **Keyboard Shortcuts** - Quick panel access
10. **Status Bar** - At-a-glance metrics
11. **Panel State Persistence** - Remember layouts

### Low Priority Missing Features (P3 - Future)

12. **ModBrowserPanel** - Mod management
13. **DefinitionBrowserPanel** - Data browser
14. **Command Palette** - Fuzzy search
15. **Workspace Presets** - Saved layouts
16. **Theme System** - Custom color schemes

---

## Implementation Recommendations

### Phase 1: Foundation (Week 1-2)
**Goal:** Basic debug UI infrastructure

1. **ImGui Integration**
   - Choose: imgui-js (WebGL) or custom WebGPU backend
   - Set up rendering pipeline
   - Input handling
   - Theme/styling

2. **Core Architecture**
   - Implement IDebugPanel interface
   - Create DebugPanelRegistry
   - Basic panel lifecycle (init/update/draw/dispose)
   - Simple menu bar

3. **First Panel: PerformancePanel**
   - FPS counter
   - Frame time graph
   - Memory monitor (if available)

### Phase 2: Developer Tools (Week 3-4)
**Goal:** Essential debugging features

1. **ConsolePanel**
   - Command execution engine
   - Basic commands (help, clear, list)
   - Input handling
   - Output buffer

2. **LogsPanel**
   - Log sink integration
   - Level filtering
   - Basic search

3. **EntityInspectorPanel (Basic)**
   - Entity list
   - Component count display
   - Simple property viewer

### Phase 3: Profiling & Inspection (Week 5-6)
**Goal:** Performance analysis tools

1. **SystemProfilerPanel**
   - System timing hooks
   - Sortable table
   - Budget visualization

2. **EventInspectorPanel**
   - Event dispatch tracking
   - Subscriber list

3. **SceneInspectorPanel**
   - Scene stack view
   - State display

### Phase 4: UX Enhancements (Week 7-8)
**Goal:** Professional polish

1. **Keyboard Shortcuts**
   - Ctrl+1-9 panel access
   - Ctrl+Shift+P command palette

2. **Status Bar**
   - FPS/memory display
   - Error count badges

3. **Panel State Persistence**
   - Save/load via localStorage
   - Remember visibility

4. **Icon Toolbar**
   - Quick access buttons
   - Status indicators

### Phase 5: Advanced Features (Week 9-10)
**Goal:** Power user features

1. **Command Palette**
   - Fuzzy search
   - Panel quick open

2. **Workspace Presets**
   - Save layouts
   - Default layouts (Dev, Debug, Performance)

3. **Auto-Completion**
   - Console commands
   - Smart suggestions

---

## File Structure Recommendation

```
src/debug/
  ├── core/
  │   ├── IDebugPanel.ts
  │   ├── DebugPanelRegistry.ts
  │   ├── DebugPanelRenderSystem.ts
  │   └── DebugOverlayService.ts
  │
  ├── panels/
  │   ├── PerformancePanel.ts
  │   ├── ConsolePanel.ts
  │   ├── EntityInspectorPanel.ts
  │   ├── LogsPanel.ts
  │   ├── SystemProfilerPanel.ts
  │   ├── EventInspectorPanel.ts
  │   └── SceneInspectorPanel.ts
  │
  ├── ui/
  │   ├── DebugToolbar.ts
  │   ├── DebugColors.ts
  │   ├── MetricsTracker.ts
  │   ├── TableSortHelper.ts
  │   └── StatusIndicators.ts
  │
  ├── console/
  │   ├── ConsoleService.ts
  │   ├── CommandRegistry.ts
  │   ├── ConsoleHistory.ts
  │   └── commands/
  │       ├── BuiltInCommands.ts
  │       └── DebugCommands.ts
  │
  └── imgui/
      ├── ImGuiRenderer.ts
      ├── ImGuiTheme.ts
      └── ImGuiInputBridge.ts
```

---

## Risk Assessment

### Technical Risks

1. **ImGui WebGPU Backend**
   - **Risk:** No official WebGPU backend exists
   - **Mitigation:** Use imgui-js (WebGL) initially, or implement custom backend
   - **Impact:** Medium - May affect rendering performance

2. **Performance Monitoring**
   - **Risk:** Limited browser APIs for memory/CPU monitoring
   - **Mitigation:** Use Performance API, performance.memory (Chrome), estimate memory
   - **Impact:** Low - Can use approximations

3. **TypeScript Port Complexity**
   - **Risk:** C# patterns don't translate 1:1
   - **Mitigation:** Adapt architecture, use TypeScript idioms
   - **Impact:** Medium - Requires design adjustments

### Schedule Risks

1. **Feature Creep**
   - **Risk:** Attempting to port all 9 panels + UX enhancements at once
   - **Mitigation:** Phased approach (Foundation -> Tools -> Profiling -> UX)
   - **Impact:** High - Could delay by months

2. **ImGui Learning Curve**
   - **Risk:** Team unfamiliar with ImGui immediate-mode paradigm
   - **Mitigation:** Start with simple panels, reference OldMonoBall examples
   - **Impact:** Low-Medium - ImGui is straightforward

---

## Success Metrics

**Must Have (P0):**
- ✅ FPS/frame time visible in PerformancePanel
- ✅ Console can execute basic commands
- ✅ Debug UI can be toggled on/off

**Should Have (P1):**
- ✅ Entity inspector shows component data
- ✅ System profiler identifies slow systems
- ✅ Logs panel shows application logs

**Nice to Have (P2):**
- ✅ Keyboard shortcuts work (Ctrl+1-9)
- ✅ Panel layouts persist across sessions
- ✅ Status bar shows real-time metrics

**Future (P3):**
- ✅ Command palette with fuzzy search
- ✅ Workspace presets
- ✅ Mod/definition browsers

---

## Conclusion

OldMonoBall has a **mature, enterprise-grade debug panel system** comparable to professional game engines (Unity/Unreal). The new TypeScript/WebGPU framework currently has **ZERO debug UI**.

**Recommended Action:**
1. Start with Phase 1 (Foundation) - 2 weeks
2. Implement PerformancePanel + ConsolePanel first (highest value)
3. Add profiling tools incrementally (SystemProfiler, EventInspector)
4. Polish UX in later phases (shortcuts, status bar, persistence)

**Estimated Total Effort:** 8-10 weeks for full feature parity + UX enhancements

**Priority:** **CRITICAL** - Debug tooling is essential for development velocity

---

## References

### Source Documents
- `/docs/DIAGNOSTIC_UX_RESEARCH.md` - 16 UX patterns from industry leaders
- `/docs/DIAGNOSTIC_UX_IMPLEMENTATION_SPEC.md` - Technical implementation guide
- `/docs/diagnostic-panel-ux-analysis.md` - Pain points and solutions

### Source Code
- `/MonoBall/MonoBall.Core/Diagnostics/Panels/*.cs` - All panel implementations
- `/MonoBall/MonoBall.Core/Diagnostics/Services/*.cs` - Core services
- `/MonoBall/MonoBall.Core/Diagnostics/Systems/*.cs` - ECS systems
- `/MonoBall/MonoBall.Core/Diagnostics/UI/*.cs` - UI utilities

### Key Insights
1. OldMonoBall uses **ring buffers everywhere** for performance (frame history, logs, console)
2. **MetricsTracker** is shared utility - reuse pattern
3. **TableSortHelper** provides sortable tables - reusable component
4. **Color-coding is critical** for at-a-glance understanding (FPS, timing, state)
5. **Master-detail layouts** work well (entity list + inspector, event list + details)
6. **Lifecycle management** (Initialize/Update/Draw/Dispose) keeps code clean
7. **Event-driven** architecture (hooks, subscriptions) for loose coupling

---

**End of Research Report**
*Stored in memory for Hive Mind coordination: `hive/research/debug-panel-analysis`*
