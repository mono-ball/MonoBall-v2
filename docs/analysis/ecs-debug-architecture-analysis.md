# ECS and Debug Panel Architecture Analysis

**Analysis Date:** 2026-01-05
**Analyst:** Hive Mind Analyst Agent
**Session ID:** swarm-1767656815173-mi8dx9vr3

---

## Executive Summary

This comprehensive analysis examines the MonoBall.Core ECS architecture, event handling system, and ImGui-based debug panel integration to design improved debug panel features. The architecture demonstrates a well-structured, performance-optimized system with clear separation of concerns and robust lifecycle management.

### Key Findings

1. **Mature ECS Architecture**: Built on Arch.Core with priority-based system execution
2. **High-Performance Event Bus**: Lock-free, zero-allocation hot path with thread-safe cross-thread dispatch
3. **Clean Separation**: Debug systems are completely decoupled from game logic
4. **ImGui Lifecycle Management**: Proper frame lifecycle with input capture detection
5. **Extensible Panel Registry**: Dynamic registration with category-based organization

---

## 1. ECS Architecture Analysis

### 1.1 Core Components

**EcsWorld** (`/MonoBall/MonoBall.Core/ECS/EcsWorld.cs`)
- **Pattern**: Singleton accessor for Arch.Core World instance
- **Lifecycle**: Lazy initialization with explicit Reset() for cleanup
- **Thread Safety**: Not thread-safe (expected for single-threaded ECS access)

```csharp
// Simple singleton pattern
public static World Instance => _instance ??= World.Create();
```

### 1.2 System Management

**SystemManager** (`/MonoBall/MonoBall.Core/ECS/SystemManager.cs`)
- **Responsibilities**: System registration, initialization, update orchestration
- **Priority-Based Execution**: Systems sorted by priority (0-700+)
- **Lifecycle Phases**:
  1. **Initialization** (0-35): Map loading, entity creation, script lifecycle
  2. **Input Processing** (40-50): User input capture
  3. **Entity Processing** (100-200): Movement, camera, animation
  4. **Scene Management** (300-420): UI and scene systems
  5. **Effects** (500-510): Shader animations
  6. **Audio** (600): Sound playback
  7. **Cleanup** (700+): Active map management

**System Priority Constants** (`/MonoBall/MonoBall.Core/ECS/SystemPriority.cs`)
```csharp
// Clear priority organization
public const int MapLoader = 0;          // Phase 1: Creation
public const int Player = 30;            // Phase 2: Initialization
public const int Input = 40;             // Phase 3: Input
public const int Movement = 100;         // Phase 4: Processing
public const int Scene = 300;            // Phase 5: UI
public const int ShaderParameterAnimation = 500; // Phase 6: Effects
public const int Audio = 600;            // Phase 7: Audio
public const int ActiveMapManagement = 700; // Phase 8: Cleanup
```

### 1.3 System Registration Pattern

```csharp
private void RegisterUpdateSystem(BaseSystem<World, float> system)
{
    // Type safety enforced
    if (system is not IPrioritizedSystem prioritizedSystem)
        throw new ArgumentException("System must implement IPrioritizedSystem");

    _registeredUpdateSystems.Add(system);
    // Sorting deferred until all systems registered
}
```

### 1.4 Update Orchestration

**Two Execution Modes:**

1. **Normal Mode** (No profiling):
```csharp
_updateSystems.BeforeUpdate(in deltaTime);
_updateSystems.Update(in deltaTime);
_updateSystems.AfterUpdate(in deltaTime);
```

2. **Profiling Mode** (When SystemTimingHook subscribers exist):
```csharp
// Individual system timing
foreach (var system in _registeredUpdateSystems)
{
    stopwatch.Restart();
    system.Update(in deltaTime);
    stopwatch.Stop();
    SystemProfiler.RecordTiming(system.GetType().Name, stopwatch.Elapsed.TotalMilliseconds);
}
```

**Performance Optimization**: Zero-overhead profiling when no listeners subscribed (checked via `SystemTimingHook.HasSubscribers`).

---

## 2. Event Bus Architecture

### 2.1 Design Principles

**EventBus** (`/MonoBall/MonoBall.Core/ECS/EventBus.cs`)
- **Thread Safety**: Lock-free read path using ConcurrentDictionary + cached arrays
- **Performance**: Zero-allocation hot path, copy-on-write for subscriptions
- **Cross-Thread**: Safe dispatch from background threads via main thread queue

### 2.2 Core Architecture

```csharp
// Handler storage: thread-safe dictionary
private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<int, HandlerEntry>> _handlers;

// Cached arrays for lock-free iteration
private static readonly ConcurrentDictionary<Type, HandlerCache> _cache;

// Main thread queue for cross-thread events
private static readonly ConcurrentQueue<Action> _mainThreadQueue;
```

### 2.3 Subscription Management

**IDisposable Pattern** (Per .cursorrules requirements):
```csharp
var subscription = EventBus.Subscribe<MapLoadedEvent>(OnMapLoaded);
// ...
subscription.Dispose(); // Automatic cleanup
```

**Ref vs Copy Handlers**:
```csharp
// Copy handler (modifiable by handler)
EventBus.Subscribe<MyEvent>(evt => { /* copy */ });

// Ref handler (zero-copy, can modify original)
EventBus.Subscribe<MyEvent>((ref MyEvent evt) => { /* ref */ });
```

### 2.4 Dispatch Performance

**Hot Path Optimization**:
```csharp
public static void Send<T>(ref T eventData) where T : struct
{
    // Fast cache lookup
    if (!_cache.TryGetValue(typeof(T), out var cache) || cache.IsEmpty)
        return; // No handlers, immediate return

    // Zero-allocation iteration over cached snapshot
    var handlers = cache.Handlers;
    for (var i = 0; i < handlers.Length; i++)
    {
        ref readonly var entry = ref handlers[i];
        // Direct invocation, no LINQ, no allocations
        ((Action<T>)entry.Handler)(eventData);
    }
}
```

**Benchmarks** (from code comments):
- Publish: O(n) handlers, zero allocations
- Subscribe/Unsubscribe: O(n) cache rebuild (cold path)
- Cross-thread: O(1) queue enqueue

### 2.5 Cross-Thread Safety

```csharp
// Automatic main thread dispatch
EventBus.SendOnMainThread(new MyEvent { Data = value });

// Deferred execution (even on main thread)
EventBus.SendNextFrame(new ExpensiveEvent { ... });

// Main loop processing (called in Update)
EventBus.ProcessMainThreadQueue();
```

### 2.6 Debug Hook Integration

**EventDispatchHook** (`/MonoBall/MonoBall.Core/Diagnostics/DebugHooks.cs`):
```csharp
// Zero-overhead when no subscribers
var hasHooks = EventDispatchHook.HasSubscribers;
if (hasHooks)
{
    var startTicks = Stopwatch.GetTimestamp();
    // ... dispatch handlers ...
    EventDispatchHook.Notify(eventType, subscriberCount, elapsedMs);
}
```

**Hook Subscription Pattern**:
```csharp
private EventDispatchHook? _subscription;

public void Initialize()
{
    _subscription = EventDispatchHook.Subscribe((type, count, ms) =>
    {
        RecordDispatch(type, count, ms);
    });
}

public void Dispose()
{
    _subscription?.Dispose(); // MUST dispose per .cursorrules
}
```

---

## 3. ImGui Integration Architecture

### 3.1 Lifecycle Management

**ImGuiLifecycleSystem** (`/MonoBall/MonoBall.Core/Diagnostics/Systems/ImGuiLifecycleSystem.cs`)

**Responsibilities:**
1. Frame lifecycle (BeginFrame → EndFrame → Render)
2. Visibility toggling via DebugToggleEvent
3. Input capture detection (keyboard/mouse/text)

**Lifecycle Pattern**:
```csharp
// In Update (start of frame)
public void BeginFrame(float deltaTime)
{
    if (!_isVisible || !_renderer.IsInitialized)
        return;

    // Prevent double BeginFrame
    if (_frameStarted)
        return;

    _renderer.BeginFrame(deltaTime);
    _frameStarted = true;
}

// In Update (end of frame, after ImGui calls)
public void EndFrame()
{
    if (!_frameStarted)
        return;

    _renderer.EndFrame();
    _frameStarted = false;
}

// In Draw
public void Render()
{
    if (!_isVisible || !_renderer.IsInitialized)
        return;

    _renderer.Render();
}
```

**Input Capture Detection**:
```csharp
public bool WantsCaptureKeyboard =>
    _isVisible && _frameStarted && ImGui.GetIO().WantCaptureKeyboard;

public bool WantsCaptureMouse =>
    _isVisible && _frameStarted && ImGui.GetIO().WantCaptureMouse;
```

### 3.2 Debug Overlay Service

**DebugOverlayService** (`/MonoBall/MonoBall.Core/Diagnostics/Services/DebugOverlayService.cs`)

**Facade Pattern**: Simplifies debug system initialization and usage

**Initialization Flow**:
```csharp
public void Initialize(Game game, IResourceManager? resourceManager, ...)
{
    // 1. Create renderer
    _renderer = new MonoGameImGuiRenderer();
    _renderer.Initialize(game, resourceManager);

    // 2. Create registry and systems
    _panelRegistry = new DebugPanelRegistry();
    _lifecycleSystem = new ImGuiLifecycleSystem(_world, _renderer);
    _inputBridgeSystem = new ImGuiInputBridgeSystem(_world, _lifecycleSystem);
    _panelRenderSystem = new DebugPanelRenderSystem(_world, _panelRegistry, _lifecycleSystem);

    // 3. Hook text input
    _inputBridgeSystem.HookTextInput(game.Window);

    // 4. Register default panels
    RegisterDefaultPanels();
}
```

**Update Pattern**:
```csharp
// Start of Update
public void BeginUpdate(GameTime gameTime)
{
    _lifecycleSystem.BeginFrame(deltaTime);
    _inputBridgeSystem.Update(in deltaTime); // Before panels
}

// End of Update
public void EndUpdate(GameTime gameTime)
{
    _panelRenderSystem.Update(in deltaTime); // Draw panels
    _lifecycleSystem.EndFrame();
}

// In Draw
public void Draw()
{
    _lifecycleSystem.Render();
}
```

---

## 4. Debug Panel System

### 4.1 Panel Interface

**IDebugPanel** (`/MonoBall/MonoBall.Core/Diagnostics/Panels/IDebugPanel.cs`)

```csharp
public interface IDebugPanel
{
    string Id { get; }              // Unique identifier
    string DisplayName { get; }     // Window title
    bool IsVisible { get; set; }    // Show/hide state
    string Category { get; }        // Menu grouping
    int SortOrder { get; }          // Category ordering
    Vector2? DefaultSize { get; }   // Initial window size

    void Draw(float deltaTime);     // ImGui rendering
}

// Optional lifecycle management
public interface IDebugPanelLifecycle : IDisposable
{
    void Initialize();              // Called on registration
    void Update(float deltaTime);   // Every frame (even when hidden)
}

// Optional menu customization
public interface IDebugPanelMenu
{
    void DrawMenuItems();           // Custom menu bar items
}
```

### 4.2 Panel Registry

**DebugPanelRegistry** (`/MonoBall/MonoBall.Core/Diagnostics/Services/DebugPanelRegistry.cs`)

**Data Structures**:
```csharp
private readonly Dictionary<string, IDebugPanel> _panelsById;
private readonly List<IDebugPanel> _panels;
private readonly List<string> _categories;
private readonly Dictionary<string, List<IDebugPanel>> _panelsByCategory;
```

**Registration Pattern**:
```csharp
public void Register(IDebugPanel panel)
{
    // Validate uniqueness
    if (_panelsById.ContainsKey(panel.Id))
        throw new ArgumentException($"Panel '{panel.Id}' already registered");

    // Store by ID and in ordered list
    _panelsById[panel.Id] = panel;
    _panels.Add(panel);

    // Update category groupings
    if (!_panelsByCategory.TryGetValue(panel.Category, out var categoryPanels))
    {
        categoryPanels = new List<IDebugPanel>();
        _panelsByCategory[panel.Category] = categoryPanels;
        _categories.Add(panel.Category);
        _categories.Sort();
    }

    // Sort within category by SortOrder
    categoryPanels.Add(panel);
    categoryPanels.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));

    // Initialize lifecycle
    if (panel is IDebugPanelLifecycle lifecycle)
        lifecycle.Initialize();
}
```

**Event-Based Toggling**:
```csharp
private IDisposable? _toggleSubscription;

public DebugPanelRegistry()
{
    _toggleSubscription = EventBus.Subscribe<DebugPanelToggleEvent>(OnPanelToggle);
}

private void OnPanelToggle(DebugPanelToggleEvent evt)
{
    if (evt.Show.HasValue)
        SetPanelVisibility(evt.PanelId, evt.Show.Value);
    else
        TogglePanelVisibility(evt.PanelId);
}
```

### 4.3 Panel Rendering

**DebugPanelRenderSystem** (`/MonoBall/MonoBall.Core/Diagnostics/Systems/DebugPanelRenderSystem.cs`)

**Rendering Flow**:
```csharp
public override void Update(in float deltaTime)
{
    // Safety check: must have active ImGui frame
    if (!_lifecycleSystem.IsVisible || !_lifecycleSystem.IsFrameActive)
        return;

    // Update all panels (even hidden ones)
    _registry.Update(deltaTime);

    // Draw menu bar
    if (_showMainMenuBar)
        DrawMainMenuBar();

    // Create dockspace
    DrawDockSpace();

    // Draw visible panels
    DrawPanels(deltaTime);
}
```

**Docking Integration**:
```csharp
private void DrawDockSpace()
{
    // Seamless integration with menu bar
    var flags = ImGuiDockNodeFlags.PassthruCentralNode;
    _dockspaceId = ImGui.DockSpaceOverViewport(0, null, flags);
}

private void DrawPanels(float deltaTime)
{
    foreach (var panel in _registry.Panels)
    {
        if (!panel.IsVisible)
            continue;

        // Auto-dock new windows
        ImGui.SetNextWindowDockID(_dockspaceId, ImGuiCond.FirstUseEver);

        // Default size on first use
        if (panel.DefaultSize.HasValue)
            ImGui.SetNextWindowSize(panel.DefaultSize.Value, ImGuiCond.FirstUseEver);

        var isOpen = panel.IsVisible;
        if (ImGui.Begin(panel.DisplayName, ref isOpen))
        {
            panel.Draw(deltaTime);
        }
        ImGui.End();

        panel.IsVisible = isOpen; // Sync close button
    }
}
```

### 4.4 Event Inspector Panel (Example)

**EventInspectorPanel** (`/MonoBall/MonoBall.Core/Diagnostics/Panels/EventInspectorPanel.cs`)

**Architecture Highlights**:

1. **Hook Integration**:
```csharp
private EventDispatchHook? _dispatchHookSubscription;

public void Initialize()
{
    _dispatchHookSubscription = EventDispatchHook.Subscribe(
        (eventType, subscriberCount, elapsedMs) =>
        {
            RecordDispatch(eventType, subscriberCount, elapsedMs);
        }
    );
}

public void Dispose()
{
    _dispatchHookSubscription?.Dispose(); // Required by .cursorrules
}
```

2. **Metrics Tracking**:
```csharp
private readonly Dictionary<string, EventMetrics> _eventMetrics = new();
private readonly MetricsTracker tracker = new(30); // 30-sample rolling window

public void RecordDispatch(string eventType, int subscriberCount, double elapsedMs)
{
    if (!_eventMetrics.TryGetValue(eventType, out var metrics))
    {
        metrics = new EventMetrics(eventType);
        _eventMetrics[eventType] = metrics;
    }

    metrics.RecordDispatch(subscriberCount, elapsedMs);
}
```

3. **Table Rendering with Sorting**:
```csharp
private readonly TableSortState<EventMetrics> _sortState;

private void DrawEventTable()
{
    if (!ImGui.BeginTable("EventTable", 5, DebugPanelHelpers.SortableTableFlags))
        return;

    _sortState.SetupColumns();
    _sortState.HandleSortSpecs();

    UpdateSortedMetrics(); // Apply current sort

    foreach (var metrics in _sortedMetrics)
    {
        DrawEventRow(metrics);
    }

    ImGui.EndTable();
}
```

---

## 5. Integration Patterns

### 5.1 Service Registration Pattern

**SystemManager Integration**:
```csharp
// In CreateSceneSpecificSystems()
_debugOverlayService = new DebugOverlayService(_world);
_debugOverlayService.Initialize(_game, _resourceManager, _sceneSystem, _modManager);

var debugMenuSceneSystem = new DebugMenuSceneSystem(
    _world,
    _sceneSystem,
    _inputBindingService,
    _debugOverlayService
);

_sceneSystem.SetDebugMenuSceneSystem(debugMenuSceneSystem);
RegisterUpdateSystem(debugMenuSceneSystem);
```

**Scene System Priority**: `SystemPriority.DebugMenuScene = 370` (runs after game scenes, before audio)

### 5.2 Panel Registration Pattern

**Default Panels**:
```csharp
private void RegisterDefaultPanels()
{
    _panelRegistry.Register(new PerformancePanel());
    _panelRegistry.Register(new EntityInspectorPanel(_world));
    _panelRegistry.Register(new SceneInspectorPanel(_world));
    _panelRegistry.Register(new SystemProfilerPanel());
    _panelRegistry.Register(new EventInspectorPanel());

    // Logs panel with Serilog integration
    _logsPanel = new LogsPanel();
    _panelRegistry.Register(_logsPanel);
    ImGuiLogSink.SetLogsPanel(_logsPanel);

    // Console panel with service wiring
    _consoleService = new ConsoleService
    {
        PerformanceStats = new PerformanceStatsAdapter(),
        TimeControl = new TimeControlService(_sceneSystem),
    };
    _panelRegistry.Register(new ConsolePanel(_consoleService));

    // Optional panels based on availability
    if (_modManager != null)
    {
        _panelRegistry.Register(new ModBrowserPanel(_modManager));
        _panelRegistry.Register(new DefinitionBrowserPanel(_modManager));
    }
}
```

### 5.3 Custom Panel Template

```csharp
public sealed class MyCustomPanel : IDebugPanel, IDebugPanelLifecycle
{
    public string Id => "my-custom-panel";
    public string DisplayName => "My Custom Panel";
    public bool IsVisible { get; set; }
    public string Category => "Custom";
    public int SortOrder => 100;
    public Vector2? DefaultSize => new Vector2(400, 300);

    private IDisposable? _eventSubscription;

    public void Initialize()
    {
        // Subscribe to events, initialize resources
        _eventSubscription = EventBus.Subscribe<MyEvent>(OnMyEvent);
    }

    public void Update(float deltaTime)
    {
        // Update cached data (called every frame, even when hidden)
    }

    public void Draw(float deltaTime)
    {
        // Draw ImGui content (only when visible)
        ImGui.Text($"Delta: {deltaTime:F3}s");

        if (ImGui.Button("Do Something"))
        {
            // Handle button click
        }
    }

    public void Dispose()
    {
        _eventSubscription?.Dispose(); // Required by .cursorrules
    }

    private void OnMyEvent(MyEvent evt)
    {
        // Handle event
    }
}
```

---

## 6. Performance Considerations

### 6.1 Zero-Overhead Profiling

**Conditional Profiling**:
```csharp
var profilingEnabled = SystemTimingHook.HasSubscribers;

if (profilingEnabled)
{
    UpdateWithProfiling(deltaTime); // Individual system timing
}
else
{
    // Fast path: Group batch update
    _updateSystems.Update(in deltaTime);
}
```

**Hook Check Performance**: O(1) lock-based check, minimal overhead when disabled.

### 6.2 Event Bus Performance

**Hot Path Optimization**:
- **Zero allocations**: Cached handler arrays, no LINQ
- **Lock-free reads**: ConcurrentDictionary + array snapshot
- **Early exit**: Immediate return when no handlers

**Cold Path (Subscription Changes)**:
- **Copy-on-write**: Rebuild handler array on subscribe/unsubscribe
- **Acceptable overhead**: Infrequent operation (initialization phase)

### 6.3 ImGui Frame Management

**Safety Checks**:
```csharp
// Every ImGui call requires active frame
if (!_lifecycleSystem.IsVisible || !_lifecycleSystem.IsFrameActive)
    return;
```

**Prevents**:
- Double BeginFrame (duplicate input)
- ImGui calls without active frame (crash)
- Context destruction mid-frame (crash)

### 6.4 Memory Efficiency

**Panel Lifecycle**:
- Panels only created once (persistent state)
- Update called every frame for cache refresh
- Draw only called when visible
- Proper disposal via IDebugPanelLifecycle

---

## 7. Architectural Strengths

### 7.1 Separation of Concerns

1. **ECS Layer**: Game logic in systems
2. **Event Layer**: Decoupled communication
3. **Debug Layer**: Completely independent overlay
4. **No Coupling**: Debug systems don't affect game performance when disabled

### 7.2 Extensibility

1. **Panel Registration**: Dynamic, runtime registration
2. **Hook System**: Subscribe-based profiling
3. **Event Bus**: Any system can emit/receive events
4. **Category Organization**: Automatic menu grouping

### 7.3 Performance Optimization

1. **Conditional Profiling**: Zero overhead when disabled
2. **Lock-Free Event Dispatch**: High-throughput event handling
3. **Priority-Based Systems**: Optimal execution order
4. **Cached Queries**: Avoid repeated Arch queries

### 7.4 Robustness

1. **IDisposable Everywhere**: Proper resource cleanup
2. **Null Safety**: Nullable reference types used correctly
3. **Exception Handling**: Event handler errors don't crash game
4. **Frame Safety**: ImGui lifecycle strictly enforced

---

## 8. Recommended Improvements

### 8.1 Debug Panel Features

**Priority: High**

1. **Entity Search/Filter**:
   - Text search by component types
   - Filter by archetype
   - Tag-based filtering

2. **Component Inspector**:
   - Editable component fields (reflection-based)
   - Copy/paste component values
   - Watch expressions

3. **Event Timeline**:
   - Visual timeline of event dispatches
   - Event frequency heatmap
   - Causality tracking (which event triggered which)

**Priority: Medium**

4. **System Dependency Graph**:
   - Visualize system priorities
   - Show component read/write dependencies
   - Highlight potential race conditions

5. **Memory Profiler**:
   - Archetype memory usage
   - Component memory breakdown
   - Allocation hotspots

**Priority: Low**

6. **Replay System**:
   - Record event stream
   - Replay from saved session
   - Step-by-step debugging

### 8.2 Integration Enhancements

**New Hook Types**:
```csharp
// Component modification tracking
public sealed class ComponentChangeHook : IDisposable
{
    public static ComponentChangeHook Subscribe(
        Action<Entity, Type, string> callback); // entity, type, changeType
}

// Entity lifecycle tracking
public sealed class EntityLifecycleHook : IDisposable
{
    public static EntityLifecycleHook Subscribe(
        Action<Entity, string> callback); // entity, lifecycle event
}
```

**Event Causality Tracking**:
```csharp
// Add to EventBus
private static readonly AsyncLocal<Stack<string>> _eventStack = new();

public static void Send<T>(ref T eventData)
{
    var stack = _eventStack.Value ??= new Stack<string>();
    stack.Push(typeof(T).Name);
    try
    {
        // ... dispatch ...
    }
    finally
    {
        stack.Pop();
    }
}
```

### 8.3 Performance Enhancements

**Panel Update Throttling**:
```csharp
public interface IDebugPanelThrottled : IDebugPanel
{
    float UpdateInterval { get; } // Update every N seconds
}

// In DebugPanelRegistry
private readonly Dictionary<IDebugPanel, float> _updateTimers = new();

public void Update(float deltaTime)
{
    foreach (var panel in _panels)
    {
        if (panel is IDebugPanelThrottled throttled)
        {
            var timer = _updateTimers.GetValueOrDefault(panel);
            timer += deltaTime;
            if (timer < throttled.UpdateInterval)
                continue;
            _updateTimers[panel] = 0f;
        }

        if (panel is IDebugPanelLifecycle lifecycle)
            lifecycle.Update(deltaTime);
    }
}
```

**Conditional Hook Activation**:
```csharp
// Only enable hooks when relevant panels are visible
public sealed class EventInspectorPanel : IDebugPanel, IDebugPanelLifecycle
{
    private EventDispatchHook? _hook;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            if (value && _hook == null)
                _hook = EventDispatchHook.Subscribe(RecordDispatch);
            else if (!value && _hook != null)
            {
                _hook.Dispose();
                _hook = null;
            }
        }
    }
}
```

---

## 9. Implementation Roadmap

### Phase 1: Foundation (Week 1-2)

1. **Entity Search Panel**:
   - Text-based component type search
   - Entity list with component counts
   - Basic filtering

2. **Enhanced Event Inspector**:
   - Event frequency visualization
   - Subscriber details expanded view
   - Filter by event name/category

### Phase 2: Inspection (Week 3-4)

3. **Component Inspector Panel**:
   - Reflection-based component viewer
   - Read-only mode initially
   - Hierarchical property display

4. **System Profiler Enhancements**:
   - Dependency graph visualization
   - Priority timeline view
   - Hot/cold system highlighting

### Phase 3: Advanced Features (Week 5-6)

5. **Memory Profiler**:
   - Archetype memory usage
   - Component size breakdown
   - Memory allocation tracking

6. **Event Timeline**:
   - Visual timeline with zoom
   - Event filtering
   - Causality tracking

### Phase 4: Polish (Week 7-8)

7. **Performance Optimization**:
   - Panel update throttling
   - Conditional hook activation
   - Cached query optimization

8. **Documentation & Testing**:
   - Panel usage guides
   - Integration tests
   - Performance benchmarks

---

## 10. Conclusion

The MonoBall.Core architecture demonstrates excellent design principles:

1. **Clean Separation**: ECS, events, and debug systems are fully decoupled
2. **Performance First**: Zero-overhead when features disabled
3. **Extensibility**: Hook and panel systems allow runtime extension
4. **Robustness**: Proper lifecycle management with IDisposable pattern

The debug panel system is production-ready and can be extended with minimal risk to game performance. The hook-based profiling system enables deep inspection without modifying core systems.

**Recommended Next Steps**:
1. Implement entity search panel (highest user value)
2. Add component inspector (most requested feature)
3. Enhance event inspector with timeline visualization
4. Add system dependency graph for optimization insights

---

## Appendix A: File Locations

### Core ECS
- `/MonoBall/MonoBall.Core/ECS/EcsWorld.cs` - World singleton
- `/MonoBall/MonoBall.Core/ECS/EventBus.cs` - Event system
- `/MonoBall/MonoBall.Core/ECS/SystemManager.cs` - System orchestration
- `/MonoBall/MonoBall.Core/ECS/SystemPriority.cs` - Priority constants

### Debug System
- `/MonoBall/MonoBall.Core/Diagnostics/Systems/ImGuiLifecycleSystem.cs` - Frame management
- `/MonoBall/MonoBall.Core/Diagnostics/Systems/DebugPanelRenderSystem.cs` - Panel rendering
- `/MonoBall/MonoBall.Core/Diagnostics/Services/DebugOverlayService.cs` - Facade
- `/MonoBall/MonoBall.Core/Diagnostics/Services/DebugPanelRegistry.cs` - Panel management
- `/MonoBall/MonoBall.Core/Diagnostics/Panels/IDebugPanel.cs` - Panel interface
- `/MonoBall/MonoBall.Core/Diagnostics/DebugHooks.cs` - Profiling hooks

### Example Panels
- `/MonoBall/MonoBall.Core/Diagnostics/Panels/EventInspectorPanel.cs`
- `/MonoBall/MonoBall.Core/Diagnostics/Panels/EntityInspectorPanel.cs`
- `/MonoBall/MonoBall.Core/Diagnostics/Panels/SystemProfilerPanel.cs`

---

**End of Analysis**
