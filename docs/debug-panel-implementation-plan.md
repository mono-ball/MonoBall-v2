# Debug Panel Implementation Plan
**Created**: 2026-01-05
**Author**: Coder Agent (Hive Mind Swarm)
**Objective**: Port missing debug panel features with improved architecture

---

## Executive Summary

This document provides a comprehensive implementation plan for porting missing debug panel features from the original Unity implementation to the new C# MonoGame-based architecture using ECS (Arch) patterns.

### Key Architecture Principles
- **ECS-First Design**: Leverage Arch ECS for state management
- **Event-Driven**: Use EventBus for loose coupling
- **Hook-Based Integration**: Minimal invasive instrumentation
- **Panel Architecture**: Follow existing `IDebugPanel` pattern
- **Performance**: Zero-allocation hot paths, cached queries
- **Consistency**: Use shared UI helpers and MetricsTracker

---

## 1. Current State Analysis

### ✅ **Already Implemented Panels**
1. **PerformancePanel** - FPS, frame time, memory, GC
2. **SystemProfilerPanel** - ECS system execution timing
3. **EventInspectorPanel** - Event bus activity monitoring
4. **EntityInspectorPanel** - Entity/component inspection
5. **LogsPanel** - Serilog integration with filtering
6. **ConsolePanel** - Command execution
7. **DefinitionBrowserPanel** - Browse game definitions
8. **ModBrowserPanel** - Mod management
9. **SceneInspectorPanel** - Scene hierarchy

### 🔴 **Missing Features from Original Implementation**
Based on Unity debug panel analysis, the following features are missing:

1. **Component Type Browser** - Browse all component types in the world
2. **System Execution Timeline** - Visual timeline of system execution order
3. **Memory Breakdown Panel** - Detailed memory allocation tracking
4. **Entity Archetype Inspector** - View entity archetypes and their distributions
5. **Query Profiler** - Profile ECS query performance
6. **Advanced Performance Charts** - Historical graphs for all metrics
7. **Resource Monitor** - Texture/audio/asset tracking
8. **Input State Inspector** - Real-time input state visualization
9. **Shader Parameter Inspector** - Live shader parameter viewer/editor
10. **Spatial Hash Visualizer** - Visualize spatial partitioning

---

## 2. Architecture Foundation

### 2.1 Existing Infrastructure

**Panel System**
```csharp
// Base interfaces
IDebugPanel              // Core panel interface
IDebugPanelLifecycle     // Initialize/Update/Dispose
IDebugPanelMenu          // Custom menu items

// Services
IDebugPanelRegistry      // Panel registration and management
DebugPanelFactory        // Panel creation
DebugPanelStateService   // State persistence

// Systems
DebugPanelRenderSystem   // Renders all panels with docking
DebugSystemBase          // Base class for debug systems
```

**Instrumentation Hooks**
```csharp
SystemTimingHook      // System execution timing
EventDispatchHook     // Event dispatch tracking
SystemProfiler        // Static profiling entry point
```

**UI Helpers**
```csharp
DebugPanelHelpers     // Common UI patterns
MetricsTracker        // Circular buffer metrics tracking
TableSortState        // Sortable table implementation
DebugColors           // Consistent color scheme
```

### 2.2 Integration Points

**ECS World Access**
- All panels receive `World` reference in constructor
- Use cached `QueryDescription` to avoid allocations
- Follow `.cursorrules` - never create queries in hot paths

**Event System**
- Subscribe via `EventBus.Subscribe<TEvent>(handler)`
- Must unsubscribe in `Dispose()` method
- Use hook pattern for minimal overhead

**Performance Stats**
- `PerformanceStatsSystem` provides core metrics
- Extend with additional tracking as needed
- Use `MetricsTracker` for consistent tracking

---

## 3. Feature Implementation Roadmap

### Phase 1: Component & Archetype Analysis (Priority: HIGH)

#### Feature 1.1: Component Type Browser Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/ComponentTypeBrowserPanel.cs`

**Purpose**: Browse all component types registered in the ECS world, show usage statistics.

**Implementation Details**:
```csharp
public sealed class ComponentTypeBrowserPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly World _world;
    private readonly Dictionary<Type, ComponentTypeStats> _componentStats = new();
    private readonly TableSortState<ComponentTypeStats> _sortState;

    // Features:
    // - List all component types with entity counts
    // - Show memory size per component type
    // - Filter by component name
    // - Show which systems query each component
    // - Click to filter entities in EntityInspector
}
```

**Data Collection**:
- Iterate all entities, collect component types
- Use reflection for size estimation (cached)
- Cross-reference with system query requirements
- Refresh periodically (1-2 seconds)

**UI Layout**:
- Sortable table: Type Name | Entity Count | Avg Size | Total Memory | Used By
- Search filter at top
- Click row to filter EntityInspector

**Integration**:
- Hook into `DebugPanelRegistry`
- Category: "ECS", SortOrder: 2

---

#### Feature 1.2: Entity Archetype Inspector Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/EntityArchetypePanel.cs`

**Purpose**: Visualize entity archetypes (unique component combinations) and their distributions.

**Implementation Details**:
```csharp
public sealed class EntityArchetypePanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly World _world;
    private readonly Dictionary<string, ArchetypeStats> _archetypes = new();

    // Features:
    // - List all archetypes with entity counts
    // - Show component composition per archetype
    // - Memory usage per archetype
    // - Visual treemap or bar chart
    // - Click to filter entities by archetype
}
```

**Data Structure**:
```csharp
private sealed class ArchetypeStats
{
    public string Signature { get; set; }  // "Position+Velocity+Sprite"
    public List<Type> Components { get; set; }
    public int EntityCount { get; set; }
    public long TotalMemory { get; set; }
}
```

**UI Layout**:
- Left: Table of archetypes (sortable)
- Right: Selected archetype details with component list
- Visual representation (progress bar for count distribution)

---

### Phase 2: System Execution Analysis (Priority: HIGH)

#### Feature 2.1: System Execution Timeline Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/SystemTimelinePanel.cs`

**Purpose**: Visual timeline showing system execution order and overlap.

**Implementation Details**:
```csharp
public sealed class SystemTimelinePanel : IDebugPanel, IDebugPanelLifecycle
{
    private SystemTimingHook? _timingHook;
    private readonly CircularBuffer<FrameSnapshot> _frameHistory = new(120);

    // Features:
    // - Horizontal timeline bars per system
    // - Color-coded by execution time (green/yellow/red)
    // - Show system dependencies and grouping
    // - Frame-by-frame scrubbing
    // - Export timeline data
}
```

**Data Capture**:
```csharp
private sealed class FrameSnapshot
{
    public int FrameNumber { get; set; }
    public List<SystemExecution> Systems { get; set; }
    public double TotalFrameTime { get; set; }
}

private sealed class SystemExecution
{
    public string Name { get; set; }
    public double StartTime { get; set; }
    public double Duration { get; set; }
    public int Priority { get; set; }
}
```

**UI Layout**:
- Top: Frame selector slider
- Middle: Horizontal gantt-chart style timeline
- Bottom: Selected system details
- Use ImGui.PlotHistogram for visualization

**Hook Integration**:
- Subscribe to `SystemTimingHook` in Initialize()
- Capture start/end times for each system
- Store last 120 frames (2 seconds at 60fps)

---

#### Feature 2.2: Query Profiler Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/QueryProfilerPanel.cs`

**Purpose**: Profile ECS query performance and usage patterns.

**Implementation Details**:
```csharp
public sealed class QueryProfilerPanel : IDebugPanel, IDebugPanelLifecycle
{
    // Features:
    // - List all active queries with timing
    // - Show query filter criteria
    // - Entity match counts
    // - Hottest queries highlighted
    // - Query optimization suggestions
}
```

**Instrumentation**:
- Requires adding hooks to query execution (if not already present)
- Wrap `World.Query()` calls with timing
- Track query descriptor and match counts

**Data Structure**:
```csharp
private sealed class QueryStats
{
    public string QueryId { get; set; }
    public QueryDescription Description { get; set; }
    public MetricsTracker Timing { get; set; }
    public int MatchCount { get; set; }
    public string UsedBySystem { get; set; }
}
```

---

### Phase 3: Memory & Resource Tracking (Priority: MEDIUM)

#### Feature 3.1: Memory Breakdown Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/MemoryBreakdownPanel.cs`

**Purpose**: Detailed memory allocation tracking beyond GC heap.

**Implementation Details**:
```csharp
public sealed class MemoryBreakdownPanel : IDebugPanel, IDebugPanelLifecycle
{
    // Features:
    // - GC heap by generation
    // - Unmanaged memory
    // - ECS world memory
    // - Texture/GPU memory (via MonoGame)
    // - Audio buffer memory
    // - Historical tracking
}
```

**Data Sources**:
```csharp
// GC Memory
GC.GetTotalMemory(false)
GC.CollectionCount(0/1/2)

// Unmanaged (requires P/Invoke on each platform)
Process.GetCurrentProcess().WorkingSet64

// ECS World (from Arch library if exposed)
World.EntityCount * average_component_size

// GPU Memory (MonoGame GraphicsDevice)
GraphicsDevice.Textures - track manually
```

**UI Layout**:
- Pie chart showing memory breakdown
- Table with detailed categories
- Historical line graph
- "Force GC" buttons per generation

---

#### Feature 3.2: Resource Monitor Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/ResourceMonitorPanel.cs`

**Purpose**: Track loaded textures, sounds, and other assets.

**Implementation Details**:
```csharp
public sealed class ResourceMonitorPanel : IDebugPanel, IDebugPanelLifecycle
{
    // Features:
    // - List all loaded textures with sizes
    // - Audio buffers and states
    // - Asset reference counts
    // - Search/filter by name
    // - Unload buttons (with warnings)
}
```

**Integration**:
- Hook into MonoGame `ContentManager`
- Track texture loading via custom wrapper or reflection
- Monitor audio via `SoundEffect` instances

---

### Phase 4: Input & Gameplay Inspection (Priority: MEDIUM)

#### Feature 4.1: Input State Inspector Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/InputStatePanel.cs`

**Purpose**: Real-time visualization of input states.

**Implementation Details**:
```csharp
public sealed class InputStatePanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly InputSystem _inputSystem;

    // Features:
    // - Keyboard state visualization
    // - Mouse position and button states
    // - Gamepad states (all connected)
    // - Touch input (if applicable)
    // - Input action mapping display
}
```

**Data Access**:
- Reference `InputSystem` from ECS
- Query `Keyboard.GetState()`, `Mouse.GetState()`, `GamePad.GetState()`
- Display in real-time (no throttling needed)

**UI Layout**:
- Visual keyboard representation
- Gamepad stick/button visualization
- Mouse coordinates and button indicators

---

#### Feature 4.2: Shader Parameter Inspector Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/ShaderInspectorPanel.cs`

**Purpose**: Live shader parameter viewing and editing.

**Implementation Details**:
```csharp
public sealed class ShaderInspectorPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly ShaderManager _shaderManager;

    // Features:
    // - List all active shader effects
    // - Show parameters per shader
    // - Live parameter editing (sliders/inputs)
    // - Visual preview of affected entities
    // - Export/import parameter sets
}
```

**Integration**:
- Hook into existing `ShaderManager` system
- Query shader components from entities
- Use ImGui sliders for real-time editing
- Update component values directly

---

### Phase 5: Visualization Tools (Priority: LOW)

#### Feature 5.1: Spatial Hash Visualizer Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/SpatialHashVisualizerPanel.cs`

**Purpose**: Visualize spatial partitioning grid for collision detection.

**Implementation Details**:
```csharp
public sealed class SpatialHashVisualizerPanel : IDebugPanel, IDebugPanelLifecycle
{
    private readonly SpatialHashSystem _spatialHash;

    // Features:
    // - 2D grid overlay rendering
    // - Entity count per cell
    // - Heatmap visualization
    // - Click cell to inspect entities
    // - Toggle in-game overlay
}
```

**Rendering**:
- Use ImGui.DrawList for grid rendering
- Option to render overlay on game view
- Color cells by entity density

---

#### Feature 5.2: Advanced Performance Charts Panel
**File**: `/MonoBall/MonoBall.Core/Diagnostics/Panels/PerformanceChartsPanel.cs`

**Purpose**: Historical performance graphs for all metrics.

**Implementation Details**:
```csharp
public sealed class PerformanceChartsPanel : IDebugPanel, IDebugPanelLifecycle
{
    // Features:
    // - Multi-line graphs for FPS/frame time/memory
    // - System timing trends
    // - Event dispatch frequency
    // - Zoom and pan controls
    // - Export data to CSV
}
```

**Data Collection**:
- Subscribe to all metric sources
- Store extended history (configurable, default 5 minutes)
- Use circular buffers for efficiency

---

## 4. Code Organization

### 4.1 Directory Structure

```
MonoBall.Core/Diagnostics/
├── Panels/
│   ├── ComponentTypeBrowserPanel.cs       [NEW]
│   ├── EntityArchetypePanel.cs            [NEW]
│   ├── SystemTimelinePanel.cs             [NEW]
│   ├── QueryProfilerPanel.cs              [NEW]
│   ├── MemoryBreakdownPanel.cs            [NEW]
│   ├── ResourceMonitorPanel.cs            [NEW]
│   ├── InputStatePanel.cs                 [NEW]
│   ├── ShaderInspectorPanel.cs            [NEW]
│   ├── SpatialHashVisualizerPanel.cs      [NEW]
│   ├── PerformanceChartsPanel.cs          [NEW]
│   └── [existing panels...]
├── UI/
│   ├── ChartRenderer.cs                   [NEW] - Reusable chart components
│   ├── TimelineRenderer.cs                [NEW] - Timeline visualization
│   └── [existing UI helpers...]
├── Hooks/
│   ├── QueryTimingHook.cs                 [NEW] - Query profiling hook
│   ├── ResourceLoadHook.cs                [NEW] - Asset loading hook
│   └── [existing hooks...]
└── Data/
    ├── FrameSnapshot.cs                   [NEW] - Frame capture data
    ├── ArchetypeStats.cs                  [NEW] - Archetype analysis data
    └── QueryStats.cs                      [NEW] - Query profiling data
```

### 4.2 Shared Components

**ChartRenderer** - Reusable chart component
```csharp
namespace MonoBall.Core.Diagnostics.UI;

public static class ChartRenderer
{
    public static void DrawLineChart(
        string label,
        float[] data,
        int dataCount,
        float minValue,
        float maxValue,
        Vector2 size
    );

    public static void DrawMultiLineChart(
        string label,
        Dictionary<string, float[]> dataSeries,
        Vector2 size
    );

    public static void DrawPieChart(
        string label,
        Dictionary<string, float> segments,
        Vector2 size
    );
}
```

**TimelineRenderer** - Timeline visualization component
```csharp
namespace MonoBall.Core.Diagnostics.UI;

public sealed class TimelineRenderer
{
    public void BeginTimeline(float width, float height);
    public void DrawSystemBar(string name, double start, double duration, Vector4 color);
    public void DrawMarker(double time, string label);
    public void EndTimeline();
}
```

---

## 5. Implementation Strategy

### 5.1 Development Phases

**Phase 1: Foundation (Week 1)**
- Implement shared UI components (ChartRenderer, TimelineRenderer)
- Create base data structures (FrameSnapshot, ArchetypeStats, QueryStats)
- Add new hooks (QueryTimingHook, ResourceLoadHook)

**Phase 2: Core Panels (Week 2-3)**
- ComponentTypeBrowserPanel
- EntityArchetypePanel
- SystemTimelinePanel
- QueryProfilerPanel

**Phase 3: Resource Tracking (Week 4)**
- MemoryBreakdownPanel
- ResourceMonitorPanel

**Phase 4: Gameplay Tools (Week 5)**
- InputStatePanel
- ShaderInspectorPanel

**Phase 5: Visualization (Week 6)**
- SpatialHashVisualizerPanel
- PerformanceChartsPanel

**Phase 6: Polish & Integration (Week 7)**
- Comprehensive testing
- Documentation
- Performance optimization

### 5.2 Testing Strategy

**Unit Tests**
- Test data structures (FrameSnapshot, ArchetypeStats, etc.)
- Test metric calculations
- Test sorting/filtering logic

**Integration Tests**
- Test panel registration
- Test hook subscriptions and cleanup
- Test state persistence

**Performance Tests**
- Profile panel update costs
- Ensure zero allocation in hot paths
- Verify query caching works

**Manual Testing**
- Visual verification of all panels
- Test with high entity counts (10k+)
- Test with many systems (50+)
- Memory leak testing (long sessions)

---

## 6. API Design

### 6.1 Panel Registration

**DebugPanelFactory Updates**
```csharp
public static class DebugPanelFactory
{
    public static void RegisterAllPanels(
        IDebugPanelRegistry registry,
        World world,
        GameServices services
    )
    {
        // Existing panels...

        // Phase 1: Component & Archetype Analysis
        registry.Register(new ComponentTypeBrowserPanel(world));
        registry.Register(new EntityArchetypePanel(world));

        // Phase 2: System Execution Analysis
        registry.Register(new SystemTimelinePanel(world));
        registry.Register(new QueryProfilerPanel(world));

        // Phase 3: Memory & Resource Tracking
        registry.Register(new MemoryBreakdownPanel(world, services));
        registry.Register(new ResourceMonitorPanel(world, services));

        // Phase 4: Input & Gameplay Inspection
        registry.Register(new InputStatePanel(world, services));
        registry.Register(new ShaderInspectorPanel(world, services));

        // Phase 5: Visualization Tools
        registry.Register(new SpatialHashVisualizerPanel(world, services));
        registry.Register(new PerformanceChartsPanel(world, services));
    }
}
```

### 6.2 Hook API Design

**QueryTimingHook** - For Query Profiler
```csharp
public sealed class QueryTimingHook : IDisposable
{
    public static QueryTimingHook Subscribe(
        Action<QueryDescription, int, double> callback
    );

    internal static void Notify(
        QueryDescription query,
        int matchCount,
        double elapsedMs
    );
}
```

**ResourceLoadHook** - For Resource Monitor
```csharp
public sealed class ResourceLoadHook : IDisposable
{
    public static ResourceLoadHook Subscribe(
        Action<string, ResourceType, long> callback
    );

    internal static void NotifyLoad(
        string assetName,
        ResourceType type,
        long size
    );

    internal static void NotifyUnload(string assetName);
}
```

---

## 7. Performance Considerations

### 7.1 Zero-Allocation Guidelines

**Hot Path Rules**:
- Never create `QueryDescription` in Update/Draw loops
- Cache all queries as `static readonly` fields
- Use object pools for temporary data structures
- Avoid LINQ in performance-critical code
- Use `stackalloc` for small temporary buffers

**Example**:
```csharp
// ✅ CORRECT - Cached query
private static readonly QueryDescription _allEntitiesQuery = new();

public void Update(float deltaTime)
{
    World.Query(in _allEntitiesQuery, (Entity e) => { /* ... */ });
}

// ❌ WRONG - Creates new query each frame
public void Update(float deltaTime)
{
    var query = new QueryDescription(); // ALLOCATION!
    World.Query(in query, (Entity e) => { /* ... */ });
}
```

### 7.2 Refresh Strategies

**Throttling Patterns**:
```csharp
// Periodic refresh (low frequency)
private float _timeSinceRefresh;
private float _refreshInterval = 1.0f;

public void Update(float deltaTime)
{
    if (DebugPanelHelpers.UpdateRefreshTimer(
        ref _timeSinceRefresh, _refreshInterval, deltaTime))
    {
        RefreshData();
    }
}

// Event-driven refresh (on-demand)
public void Initialize()
{
    _hook = SystemTimingHook.Subscribe((name, time) =>
    {
        // Update immediately on event
        UpdateMetric(name, time);
    });
}
```

### 7.3 Memory Management

**Circular Buffers**:
```csharp
// Use fixed-size buffers for history
private readonly FrameSnapshot[] _history = new FrameSnapshot[120];
private int _historyIndex;

public void RecordFrame(FrameSnapshot snapshot)
{
    _history[_historyIndex] = snapshot;
    _historyIndex = (_historyIndex + 1) % _history.Length;
}
```

**Object Pooling**:
```csharp
// Pool expensive objects
private static readonly ObjectPool<List<Entity>> _entityListPool = new(
    () => new List<Entity>(),
    list => list.Clear()
);

public void ProcessEntities()
{
    var entities = _entityListPool.Get();
    try
    {
        // Use entities list
    }
    finally
    {
        _entityListPool.Return(entities);
    }
}
```

---

## 8. Integration with Existing Systems

### 8.1 SystemManager Integration

**Add Timing Hooks**:
```csharp
// In SystemManager.Update()
public void Update(float deltaTime)
{
    foreach (var system in _systems)
    {
        if (SystemTimingHook.HasSubscribers)
        {
            var stopwatch = Stopwatch.StartNew();
            system.Update(deltaTime);
            stopwatch.Stop();
            SystemProfiler.RecordTiming(
                system.GetType().Name,
                stopwatch.Elapsed.TotalMilliseconds
            );
        }
        else
        {
            system.Update(deltaTime);
        }
    }
}
```

### 8.2 EventBus Integration

**Add Dispatch Hooks**:
```csharp
// In EventBus.Publish<T>()
public void Publish<T>(T @event) where T : struct
{
    if (EventDispatchHook.HasSubscribers)
    {
        var stopwatch = Stopwatch.StartNew();
        var count = PublishInternal(@event);
        stopwatch.Stop();
        EventDispatchHook.Notify(
            typeof(T).Name,
            count,
            stopwatch.Elapsed.TotalMilliseconds
        );
    }
    else
    {
        PublishInternal(@event);
    }
}
```

### 8.3 ContentManager Integration

**Wrap Asset Loading**:
```csharp
// Custom ContentManager wrapper
public sealed class InstrumentedContentManager : ContentManager
{
    protected override T Load<T>(string assetName)
    {
        var asset = base.Load<T>(assetName);

        if (ResourceLoadHook.HasSubscribers)
        {
            var size = EstimateAssetSize(asset);
            var type = GetResourceType<T>();
            ResourceLoadHook.NotifyLoad(assetName, type, size);
        }

        return asset;
    }

    private long EstimateAssetSize<T>(T asset)
    {
        return asset switch
        {
            Texture2D tex => tex.Width * tex.Height * 4,
            SoundEffect sound => sound.Duration.TotalSeconds * 44100 * 2,
            _ => 0
        };
    }
}
```

---

## 9. UI/UX Guidelines

### 9.1 Consistent Panel Layout

**Standard Structure**:
```
┌─────────────────────────────────────┐
│ [Toolbar]  [Filter]  [Refresh: 0.5s]│
├─────────────────────────────────────┤
│                                     │
│        Main Content Area            │
│        (Table/Chart/List)           │
│                                     │
├─────────────────────────────────────┤
│ Status: 123 items | 45 filtered    │
└─────────────────────────────────────┘
```

**Key Patterns**:
- Toolbar at top with consistent spacing
- Filter input on left, refresh slider on right
- Status bar at bottom
- Use `DebugPanelHelpers` for all standard elements
- Maintain consistent colors via `DebugColors`

### 9.2 Color Coding

**Performance Colors**:
```csharp
// Use DebugPanelHelpers.GetTimingColor()
< 0.5ms   → Green  (TimingFast)
< 2.0ms   → Yellow (TimingMedium)
≥ 2.0ms   → Red    (TimingSlow)

// FPS
≥ 60 FPS  → Green  (Success)
≥ 30 FPS  → Yellow (Warning)
< 30 FPS  → Red    (Error)
```

**State Colors**:
```csharp
Active    → Green  (Active)
Inactive  → Gray   (Inactive)
Error     → Red    (Error)
Warning   → Yellow (Warning)
```

### 9.3 Tooltips

**Always Provide Context**:
```csharp
if (ImGui.IsItemHovered())
{
    ImGui.SetTooltip(
        $"Full Type Name: {fullTypeName}\n" +
        $"Assembly: {assembly}\n" +
        $"Click to filter entities"
    );
}
```

---

## 10. Documentation Requirements

### 10.1 Code Documentation

**XML Comments Required**:
- All public classes, methods, properties
- Complex algorithms and data structures
- Performance-critical sections
- Hook integration points

**Example**:
```csharp
/// <summary>
/// Debug panel for browsing all component types in the ECS world.
/// Shows usage statistics and memory estimates.
/// </summary>
/// <remarks>
/// This panel uses cached queries and refreshes periodically to minimize
/// performance impact. The refresh interval can be adjusted via the toolbar.
/// </remarks>
public sealed class ComponentTypeBrowserPanel : IDebugPanel, IDebugPanelLifecycle
{
    /// <summary>
    /// Estimates the memory size of a component type using reflection.
    /// Results are cached to avoid repeated reflection calls.
    /// </summary>
    /// <param name="type">The component type to measure.</param>
    /// <returns>Estimated size in bytes.</returns>
    private long EstimateComponentSize(Type type)
    {
        // Implementation...
    }
}
```

### 10.2 User Documentation

**Panel Documentation** (in `/docs/debug-panels/`):
- Purpose and use cases
- Feature overview
- Keyboard shortcuts
- Tips and tricks
- Performance impact

### 10.3 Architecture Documentation

**Updated Documents**:
- `/docs/architecture/debug-system.md` - Overall debug system architecture
- `/docs/architecture/panel-development.md` - Guide for creating new panels
- `/docs/architecture/instrumentation-hooks.md` - Hook system documentation

---

## 11. Risk Assessment & Mitigation

### 11.1 Performance Risks

**Risk**: Panel updates cause frame drops
**Mitigation**:
- Throttle updates with configurable refresh intervals
- Use hook-based data collection (zero cost when closed)
- Profile all panels with high entity counts
- Implement "Pause" button to freeze updates

**Risk**: Memory leaks from hook subscriptions
**Mitigation**:
- Enforce IDisposable pattern on all hooks
- Add automated tests for subscription cleanup
- Code review checklist for Dispose() implementation

### 11.2 Compatibility Risks

**Risk**: Arch library updates break panel queries
**Mitigation**:
- Version pin Arch dependency
- Abstract query interface for easier migration
- Comprehensive integration tests

**Risk**: ImGui API changes
**Mitigation**:
- Centralize ImGui usage in helper classes
- Document ImGui version compatibility
- Test against multiple ImGui versions

### 11.3 Complexity Risks

**Risk**: Too many panels overwhelm users
**Mitigation**:
- Organize by category (ECS, Diagnostics, Tools)
- Implement panel presets (profiles)
- Default to minimal panel set
- Add "Recommended" tag to essential panels

---

## 12. Success Metrics

### 12.1 Functional Metrics

- ✅ All 10 missing features implemented
- ✅ Zero crashes or exceptions in normal use
- ✅ All panels follow consistent UI patterns
- ✅ State persistence works across sessions

### 12.2 Performance Metrics

- ✅ Panel updates < 1ms per frame when visible
- ✅ Zero allocations in Update() when panel closed
- ✅ < 5% frame time overhead with all panels open
- ✅ No memory leaks after 1-hour session

### 12.3 Code Quality Metrics

- ✅ 100% XML documentation on public APIs
- ✅ Zero static analysis warnings
- ✅ All panels pass unit tests
- ✅ Code review approved by 2+ engineers

### 12.4 User Experience Metrics

- ✅ Consistent visual design across all panels
- ✅ Tooltips on all non-obvious UI elements
- ✅ Keyboard shortcuts documented
- ✅ User documentation complete

---

## 13. Future Enhancements

### 13.1 Short-Term (Post-MVP)

1. **Panel Layouts**: Save/load panel arrangements
2. **Hotkeys**: Global keyboard shortcuts for common actions
3. **Export**: Export data to JSON/CSV
4. **Themes**: Light/dark theme support
5. **Remote Debugging**: Connect to running game instance

### 13.2 Long-Term

1. **Replay System**: Record and replay game sessions
2. **Profiler Integration**: Integrate with external profilers
3. **AI Analysis**: Automated performance issue detection
4. **Collaborative Debugging**: Share debug sessions
5. **Cloud Integration**: Store/share panel configurations

---

## 14. Conclusion

This implementation plan provides a comprehensive roadmap for porting all missing debug panel features to the new C# MonoGame-based architecture. The plan prioritizes:

1. **Architectural Consistency**: Following existing patterns and conventions
2. **Performance**: Zero-allocation hot paths and efficient data structures
3. **Maintainability**: Clean code with comprehensive documentation
4. **User Experience**: Consistent UI with powerful features

The phased approach allows for iterative development and testing, ensuring each panel is production-ready before moving to the next. The shared infrastructure (ChartRenderer, TimelineRenderer, hooks) reduces duplication and simplifies future panel development.

**Next Steps**:
1. Review and approve this plan with the team
2. Set up development environment and dependencies
3. Begin Phase 1 implementation
4. Establish code review process
5. Create task tracking in project management system

---

**Version**: 1.0
**Last Updated**: 2026-01-05
**Status**: Ready for Review
