# Comprehensive Testing Strategy for MonoBall Debug Panel System

**Version**: 1.0
**Date**: 2026-01-05
**Agent**: Tester (Hive Mind Swarm)
**Status**: Ready for Implementation

---

## Executive Summary

This document outlines a comprehensive testing strategy for the MonoBall debug panel system, covering unit tests, integration tests, performance benchmarks, and manual testing procedures. The strategy ensures high code quality, performance, and reliability across all debug panel features.

---

## 1. Testing Architecture Overview

### 1.1 Testing Pyramid

```
           /\
          /E2E\          <- 5% (Manual UI Testing)
         /------\
        /Integr.\        <- 20% (ECS + Event Integration)
       /----------\
      /    Unit    \     <- 75% (Component Logic)
     /--------------\
```

### 1.2 Test Technology Stack

- **Unit Testing**: xUnit or NUnit (C# standard)
- **Mocking**: NSubstitute or Moq
- **ECS Testing**: Arch.Core test utilities
- **Performance**: BenchmarkDotNet
- **UI Testing**: Manual + ImGui screenshot comparison
- **Coverage Target**: >85% for critical paths

---

## 2. Unit Testing Strategy

### 2.1 Core Panel Interface Tests

**File**: `tests/Diagnostics/Panels/IDebugPanelTests.cs`

**Test Cases**:
- Panel properties (Id, DisplayName, Category, SortOrder)
- IsVisible state management
- DefaultSize configuration
- Draw method lifecycle

**Example Test Structure**:
```csharp
public class PerformancePanelTests
{
    private PerformancePanel _panel;

    [SetUp]
    public void Setup()
    {
        _panel = new PerformancePanel();
        _panel.Initialize();
    }

    [Test]
    public void Id_ReturnsCorrectValue()
    {
        Assert.AreEqual("performance", _panel.Id);
    }

    [Test]
    public void IsVisible_TogglesBehavior()
    {
        _panel.IsVisible = true;
        Assert.IsTrue(_panel.IsVisible);
        _panel.IsVisible = false;
        Assert.IsFalse(_panel.IsVisible);
    }

    [Test]
    public void Update_AccumulatesFrameTimeData()
    {
        _panel.Update(0.016f); // 16ms
        _panel.Update(0.016f);
        _panel.Update(0.016f);
        // Verify internal frame time history updated
    }

    [TearDown]
    public void Teardown()
    {
        _panel.Dispose();
    }
}
```

### 2.2 Debug Panel Registry Tests

**File**: `tests/Diagnostics/Services/DebugPanelRegistryTests.cs`

**Test Cases**:
- Register panel successfully
- Prevent duplicate panel IDs
- Unregister panel and cleanup
- Get panel by ID
- Get panels by category
- Set/toggle panel visibility
- Event subscription handling
- Lifecycle method invocation
- Category sorting and ordering

**Critical Tests**:
```csharp
[Test]
public void Register_ThrowsOnDuplicateId()
{
    var panel1 = new MockPanel { Id = "test" };
    var panel2 = new MockPanel { Id = "test" };

    _registry.Register(panel1);

    Assert.Throws<ArgumentException>(() =>
        _registry.Register(panel2)
    );
}

[Test]
public void Register_CallsInitializeOnLifecyclePanel()
{
    var lifecyclePanel = Substitute.For<IDebugPanel, IDebugPanelLifecycle>();
    lifecyclePanel.Id.Returns("lifecycle-test");
    lifecyclePanel.Category.Returns("Test");

    _registry.Register(lifecyclePanel);

    ((IDebugPanelLifecycle)lifecyclePanel)
        .Received(1)
        .Initialize();
}

[Test]
public void Unregister_DisposesPanel()
{
    var disposablePanel = Substitute.For<IDebugPanel, IDisposable>();
    disposablePanel.Id.Returns("disposable-test");
    disposablePanel.Category.Returns("Test");

    _registry.Register(disposablePanel);
    _registry.Unregister("disposable-test");

    ((IDisposable)disposablePanel).Received(1).Dispose();
}
```

### 2.3 Panel Render System Tests

**File**: `tests/Diagnostics/Systems/DebugPanelRenderSystemTests.cs`

**Test Cases**:
- Update skips when ImGui not visible
- Update skips when frame not active
- Main menu bar rendering
- Dockspace creation
- Panel window creation
- Panel Draw() invocation
- Panel visibility state synchronization

**Mock-Heavy Tests**:
```csharp
[Test]
public void Update_SkipsWhenImGuiNotVisible()
{
    _lifecycleSystem.IsVisible.Returns(false);
    _lifecycleSystem.IsFrameActive.Returns(true);

    _renderSystem.Update(0.016f);

    // Verify no ImGui calls made
    _registry.DidNotReceive().Update(Arg.Any<float>());
}

[Test]
public void Update_DrawsVisiblePanelsOnly()
{
    var visiblePanel = CreateMockPanel("visible", true);
    var hiddenPanel = CreateMockPanel("hidden", false);

    _registry.Panels.Returns(new[] { visiblePanel, hiddenPanel });
    _lifecycleSystem.IsVisible.Returns(true);
    _lifecycleSystem.IsFrameActive.Returns(true);

    _renderSystem.Update(0.016f);

    visiblePanel.Received(1).Draw(0.016f);
    hiddenPanel.DidNotReceive().Draw(Arg.Any<float>());
}
```

### 2.4 Performance Panel Tests

**File**: `tests/Diagnostics/Panels/PerformancePanelTests.cs`

**Test Cases**:
- Frame time tracking accuracy
- FPS calculation correctness
- Min/max/avg calculations
- Memory statistics retrieval
- GC collection counting
- Frame time history buffer wraparound
- Refresh interval timing

**Precision Tests**:
```csharp
[Test]
public void Update_CalculatesFPSCorrectly()
{
    var deltaTime = 0.016666f; // 60 FPS

    _panel.Update(deltaTime);
    // Wait for refresh interval
    Thread.Sleep(500);
    _panel.Update(deltaTime);

    // FPS should be approximately 60
    var fps = GetPrivateField<float>(_panel, "_fps");
    Assert.That(fps, Is.InRange(59f, 61f));
}

[Test]
public void Update_TracksMinMaxFrameTimes()
{
    _panel.Update(0.010f); // 10ms
    _panel.Update(0.050f); // 50ms (spike)
    _panel.Update(0.015f); // 15ms

    Thread.Sleep(500); // Trigger stats update
    _panel.Update(0.016f);

    var min = GetPrivateField<float>(_panel, "_minFrameTime");
    var max = GetPrivateField<float>(_panel, "_maxFrameTime");

    Assert.That(min, Is.LessThanOrEqualTo(10f));
    Assert.That(max, Is.GreaterThanOrEqualTo(50f));
}
```

### 2.5 Entity Inspector Panel Tests

**File**: `tests/Diagnostics/Panels/EntityInspectorPanelTests.cs`

**Test Cases**:
- Entity list caching
- Component type discovery
- Search filtering
- Component filtering (Any/All modes)
- Entity selection state
- Component value rendering
- Refresh interval behavior
- Null entity handling

**ECS Integration Tests**:
```csharp
[Test]
public void RefreshEntityList_CollectsAllEntities()
{
    var world = World.Create();
    var entity1 = world.Create();
    var entity2 = world.Create();
    var entity3 = world.Create();

    var panel = new EntityInspectorPanel(world);
    panel.Initialize();

    var entities = GetPrivateField<List<Entity>>(panel, "_cachedEntities");

    Assert.AreEqual(3, entities.Count);
    Assert.Contains(entity1, entities);
    Assert.Contains(entity2, entities);
    Assert.Contains(entity3, entities);

    world.Dispose();
}

[Test]
public void ComponentFilter_Any_MatchesCorrectly()
{
    var world = World.Create();
    var entity1 = world.Create<ComponentA, ComponentB>();
    var entity2 = world.Create<ComponentB, ComponentC>();
    var entity3 = world.Create<ComponentC>();

    var panel = new EntityInspectorPanel(world);
    panel.Initialize();

    // Select ComponentA and ComponentB for filtering
    var filters = GetPrivateField<HashSet<Type>>(
        panel,
        "_selectedComponentFilters"
    );
    filters.Add(typeof(ComponentA));
    filters.Add(typeof(ComponentB));

    InvokePrivateMethod(panel, "UpdateFilteredEntities");

    var filtered = GetPrivateField<List<Entity>>(
        panel,
        "_filteredEntities"
    );

    // Any mode: entity1 and entity2 should match
    Assert.AreEqual(2, filtered.Count);
    Assert.Contains(entity1, filtered);
    Assert.Contains(entity2, filtered);

    world.Dispose();
}
```

---

## 3. Integration Testing Strategy

### 3.1 ECS System Integration Tests

**File**: `tests/Diagnostics/Integration/ECSIntegrationTests.cs`

**Test Cases**:
- Debug panel system registration
- System update order
- World lifecycle integration
- Component queries from panels
- Entity creation/destruction events

**Example**:
```csharp
[Test]
public void DebugPanelRenderSystem_IntegratesWithWorld()
{
    var world = World.Create();
    var registry = new DebugPanelRegistry();
    var lifecycleSystem = new ImGuiLifecycleSystem(world);
    var renderSystem = new DebugPanelRenderSystem(
        world,
        registry,
        lifecycleSystem
    );

    // Add test entities
    var entity = world.Create<TestComponent>();

    // Register test panel
    var panel = new EntityInspectorPanel(world);
    registry.Register(panel);
    panel.IsVisible = true;

    lifecycleSystem.IsVisible = true;
    lifecycleSystem.IsFrameActive = true;

    // Update systems
    renderSystem.Update(0.016f);

    // Verify panel received update
    var entities = GetPrivateField<List<Entity>>(
        panel,
        "_cachedEntities"
    );
    Assert.Contains(entity, entities);

    world.Dispose();
}
```

### 3.2 Event System Integration Tests

**File**: `tests/Diagnostics/Integration/EventIntegrationTests.cs`

**Test Cases**:
- DebugPanelToggleEvent handling
- Event subscription lifecycle
- Panel visibility changes via events
- Event propagation timing

**Example**:
```csharp
[Test]
public void DebugPanelToggleEvent_TogglesVisibility()
{
    var registry = new DebugPanelRegistry();
    var panel = new MockPanel { Id = "test", IsVisible = false };
    registry.Register(panel);

    var evt = new DebugPanelToggleEvent
    {
        PanelId = "test",
        Show = null // Toggle
    };

    EventBus.Send(ref evt);

    Assert.IsTrue(panel.IsVisible);

    EventBus.Send(ref evt);

    Assert.IsFalse(panel.IsVisible);
}

[Test]
public void DebugPanelToggleEvent_SetsExplicitVisibility()
{
    var registry = new DebugPanelRegistry();
    var panel = new MockPanel { Id = "test", IsVisible = false };
    registry.Register(panel);

    EventBus.Send(new DebugPanelToggleEvent
    {
        PanelId = "test",
        Show = true
    });

    Assert.IsTrue(panel.IsVisible);
}
```

### 3.3 ImGui Lifecycle Integration

**File**: `tests/Diagnostics/Integration/ImGuiIntegrationTests.cs`

**Test Cases**:
- Frame lifecycle synchronization
- Input capture states
- Menu bar integration
- Dockspace creation
- Window management

**Note**: These tests require ImGui context mocking or manual verification.

---

## 4. Performance Testing Strategy

### 4.1 Performance Benchmarks

**File**: `tests/Diagnostics/Performance/DebugPanelBenchmarks.cs`

**Benchmark Categories**:
- Panel registration overhead
- Entity query performance
- Frame time tracking overhead
- Memory allocation patterns
- Large entity list rendering

**BenchmarkDotNet Example**:
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class EntityInspectorBenchmarks
{
    private World _world;
    private EntityInspectorPanel _panel;

    [Params(10, 100, 1000, 10000)]
    public int EntityCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = World.Create();

        // Create entities with random components
        for (int i = 0; i < EntityCount; i++)
        {
            var entity = _world.Create<TestComponent>();
            _world.Add(entity, new TestComponent { Value = i });
        }

        _panel = new EntityInspectorPanel(_world);
        _panel.Initialize();
    }

    [Benchmark]
    public void RefreshEntityList()
    {
        InvokePrivateMethod(_panel, "RefreshEntityList");
    }

    [Benchmark]
    public void UpdateFilteredEntities()
    {
        var filters = GetPrivateField<HashSet<Type>>(
            _panel,
            "_selectedComponentFilters"
        );
        filters.Add(typeof(TestComponent));

        InvokePrivateMethod(_panel, "UpdateFilteredEntities");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _panel.Dispose();
        _world.Dispose();
    }
}
```

### 4.2 Performance Acceptance Criteria

| Operation | Max Time | Max Allocations |
|-----------|----------|-----------------|
| Panel Registration | <1ms | <10KB |
| Entity List Refresh (1000 entities) | <5ms | <100KB |
| Component Filter Update | <2ms | <50KB |
| Frame Time Update | <0.1ms | <1KB |
| Panel Draw (visible) | <2ms | <50KB |
| Event Processing | <0.1ms | <1KB |

### 4.3 Memory Leak Detection

**Test Cases**:
- Panel disposal cleanup
- Event subscription cleanup
- Entity list cache clearing
- Component type collection disposal

**Example**:
```csharp
[Test]
public void DebugPanelRegistry_DisposalCleansUpMemory()
{
    var initialMemory = GC.GetTotalMemory(true);

    var registry = new DebugPanelRegistry();

    // Register 100 panels
    for (int i = 0; i < 100; i++)
    {
        registry.Register(new MockPanel
        {
            Id = $"panel-{i}",
            Category = "Test"
        });
    }

    var afterRegistration = GC.GetTotalMemory(false);

    // Dispose registry
    registry.Dispose();
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var afterDisposal = GC.GetTotalMemory(true);

    // Memory should return close to initial
    var memoryIncrease = afterDisposal - initialMemory;
    Assert.That(memoryIncrease, Is.LessThan(1024 * 1024)); // <1MB
}
```

---

## 5. Edge Case Testing

### 5.1 Boundary Conditions

**Test Cases**:
- Empty world (no entities)
- Single entity
- Maximum entities (stress test with 100,000+)
- Empty panels (no visible panels)
- All panels visible simultaneously
- Null component values
- Malformed panel IDs

**Example**:
```csharp
[Test]
public void EntityInspector_HandlesEmptyWorld()
{
    var world = World.Create();
    var panel = new EntityInspectorPanel(world);

    panel.Initialize();
    panel.Update(0.016f);

    var entities = GetPrivateField<List<Entity>>(
        panel,
        "_cachedEntities"
    );

    Assert.AreEqual(0, entities.Count);
    Assert.DoesNotThrow(() => panel.Draw(0.016f));

    world.Dispose();
}

[Test]
public void Registry_HandlesNullPanelGracefully()
{
    var registry = new DebugPanelRegistry();

    Assert.Throws<ArgumentNullException>(() =>
        registry.Register(null)
    );
}
```

### 5.2 Concurrent Operations

**Test Cases**:
- Rapid panel visibility toggling
- Entity creation during panel refresh
- Component modification during inspection
- Multiple event sends

**Example**:
```csharp
[Test]
public void Registry_HandlesConcurrentVisibilityToggles()
{
    var registry = new DebugPanelRegistry();
    var panel = new MockPanel { Id = "test", IsVisible = false };
    registry.Register(panel);

    var tasks = new List<Task>();
    for (int i = 0; i < 100; i++)
    {
        tasks.Add(Task.Run(() =>
            registry.TogglePanelVisibility("test")
        ));
    }

    Task.WaitAll(tasks.ToArray());

    // Should complete without exceptions
    Assert.Pass();
}
```

### 5.3 Error Recovery

**Test Cases**:
- Panel throws during Draw()
- Panel throws during Update()
- Invalid entity references
- Missing components
- Disposed world access

---

## 6. Manual Testing Procedures

### 6.1 UI Interaction Testing

**Test Procedure**: `Manual_UI_Testing.md`

**Checklist**:
- [ ] Menu bar displays all panel categories
- [ ] Panel windows open/close correctly
- [ ] Panel docking works smoothly
- [ ] Panel resizing maintains layout
- [ ] Search filters work as expected
- [ ] Component filters update live
- [ ] Performance graphs render correctly
- [ ] Entity selection highlights properly
- [ ] Tooltips appear on hover
- [ ] Keyboard shortcuts work (if implemented)

### 6.2 Visual Regression Testing

**Procedure**:
1. Launch application with debug panels
2. Open all panels
3. Capture screenshots
4. Compare with baseline images
5. Flag any visual differences

**Tools**: Manual screenshot comparison or automated tools like Applitools

### 6.3 Performance Testing (Manual)

**Procedure**:
1. Create test world with 10,000 entities
2. Open Entity Inspector
3. Measure frame time impact
4. Verify FPS remains above 55
5. Check memory usage stays stable
6. Toggle panels rapidly
7. Monitor for stuttering or lag

**Acceptance Criteria**:
- Debug panels add <5ms to frame time
- No visible stuttering when toggling panels
- Memory usage increases <100MB with all panels open
- No memory leaks after 5 minutes of use

---

## 7. Test Automation Strategy

### 7.1 Continuous Integration Setup

**CI Pipeline**:
```yaml
name: Debug Panel Tests

on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: '8.0.x'
      - name: Restore dependencies
        run: dotnet restore
      - name: Build
        run: dotnet build --no-restore
      - name: Run unit tests
        run: dotnet test --no-build --verbosity normal
      - name: Generate coverage report
        run: dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
      - name: Upload coverage
        uses: codecov/codecov-action@v2
```

### 7.2 Pre-Commit Hooks

**Hook Script**: `.git/hooks/pre-commit`
```bash
#!/bin/bash
# Run tests before commit
dotnet test tests/Diagnostics --verbosity quiet
if [ $? -ne 0 ]; then
    echo "Tests failed. Commit aborted."
    exit 1
fi
```

### 7.3 Test Naming Convention

**Pattern**: `[MethodName]_[Scenario]_[ExpectedResult]`

**Examples**:
- `Register_DuplicateId_ThrowsArgumentException`
- `TogglePanelVisibility_NullId_ReturnsFalse`
- `Update_LargeEntityCount_CompletesUnder5ms`

---

## 8. Test Coverage Requirements

### 8.1 Coverage Targets

| Component | Line Coverage | Branch Coverage |
|-----------|---------------|-----------------|
| IDebugPanel implementations | >90% | >85% |
| DebugPanelRegistry | >95% | >90% |
| DebugPanelRenderSystem | >85% | >80% |
| Event handlers | >90% | >85% |
| Lifecycle methods | >95% | >90% |
| **Overall Target** | **>85%** | **>80%** |

### 8.2 Critical Paths (100% Coverage Required)

- Panel registration/unregistration
- Event subscription/disposal
- Entity query logic
- Visibility state management
- Null/error handling

---

## 9. Test Data and Mocks

### 9.1 Mock Panel Implementation

**File**: `tests/Diagnostics/Mocks/MockPanel.cs`

```csharp
public class MockPanel : IDebugPanel
{
    public string Id { get; set; } = "mock";
    public string DisplayName { get; set; } = "Mock Panel";
    public bool IsVisible { get; set; }
    public string Category { get; set; } = "Test";
    public int SortOrder { get; set; } = 0;
    public int DrawCallCount { get; private set; }

    public void Draw(float deltaTime)
    {
        DrawCallCount++;
    }
}
```

### 9.2 Test Component Types

```csharp
public struct TestComponent
{
    public int Value;
}

public struct ComponentA { }
public struct ComponentB { }
public struct ComponentC { }
```

---

## 10. Acceptance Criteria Summary

### 10.1 Feature Completion Criteria

- [ ] All unit tests pass (>85% coverage)
- [ ] All integration tests pass
- [ ] Performance benchmarks meet targets
- [ ] Manual UI testing checklist completed
- [ ] No memory leaks detected
- [ ] No critical bugs in issue tracker
- [ ] Documentation updated

### 10.2 Performance Criteria

- [ ] Panel registration: <1ms
- [ ] Entity refresh (1000 entities): <5ms
- [ ] Frame time overhead: <5ms
- [ ] Memory overhead: <100MB
- [ ] No FPS drops below 55 with all panels open

### 10.3 Quality Criteria

- [ ] Zero crashes during testing
- [ ] All edge cases handled gracefully
- [ ] Error messages are clear and actionable
- [ ] UI is responsive and intuitive
- [ ] Code follows project conventions

---

## 11. Test Execution Schedule

### 11.1 Development Phase

- **Daily**: Run unit tests locally
- **Pre-commit**: Run fast unit tests (<30s)
- **PR Submission**: Run full test suite
- **Merge to main**: Run full suite + benchmarks

### 11.2 Release Phase

- **Feature Complete**: Run all tests + manual checklist
- **Code Freeze**: Performance benchmarks + stress tests
- **Release Candidate**: Full manual testing + regression tests
- **Production Release**: Smoke tests + monitoring

---

## 12. Known Limitations and Future Work

### 12.1 Current Limitations

- ImGui context mocking is complex (may require manual testing)
- Visual regression testing requires manual comparison
- Performance tests depend on hardware

### 12.2 Future Enhancements

- Automated UI testing with screenshot comparison
- Load testing with realistic workloads
- Cross-platform performance validation
- Accessibility testing

---

## 13. Test Maintenance

### 13.1 Review Cadence

- **Weekly**: Review failing tests
- **Monthly**: Update acceptance criteria
- **Quarterly**: Audit coverage gaps
- **Per Release**: Update test data and scenarios

### 13.2 Test Refactoring

- Keep tests DRY (Don't Repeat Yourself)
- Extract common setup to helper methods
- Use test fixtures for complex scenarios
- Document non-obvious test logic

---

## Conclusion

This testing strategy provides comprehensive coverage for the MonoBall debug panel system, ensuring high quality, performance, and maintainability. The combination of unit tests, integration tests, performance benchmarks, and manual testing procedures guarantees that all features work correctly individually and as a cohesive system.

**Next Steps**:
1. Implement unit test framework setup
2. Create mock implementations
3. Write priority test cases (critical paths first)
4. Set up CI pipeline
5. Execute manual testing checklist

**Coordination**: This strategy will be stored in Hive Mind memory for the collective to reference during implementation phases.
