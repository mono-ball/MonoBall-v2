# Console Command Architecture Design

## Overview

This document outlines the architecture for extending the MonoBall debug console with comprehensive ECS, performance, event, and debug commands. The design builds upon the existing command system while maintaining consistency with current patterns.

## Current Architecture Analysis

### Existing Patterns

The console system follows these established patterns:

1. **Command Interface**: `IConsoleCommand` with properties:
   - `Name`, `Description`, `Usage`, `Category`
   - `Aliases` for alternative names
   - `ExecuteAsync()` for command execution
   - `GetCompletions()` for auto-completion

2. **Auto-Discovery**: Commands marked with `[ConsoleCommand]` attribute are automatically registered

3. **Service Dependencies**: Commands access engine services via `IConsoleContext`:
   - `CommandRegistry` - Command lookup
   - `PerformanceStats` - Performance metrics
   - `TimeControl` - Time manipulation

4. **Output Levels**: Color-coded output using `ConsoleOutputLevel`:
   - Normal, Success, Warning, Error, System

5. **Event Integration**: Commands integrate with `EventBus` for decoupled communication

## Command Categories

### 1. ECS Commands (Category: "ECS")

Commands for inspecting and manipulating the Entity-Component-System architecture.

#### 1.1 Entity List Command

**Command**: `entity list [filter]`
**Aliases**: `entities`, `ent`
**Description**: Lists all entities or filters by component type

**Syntax**:
```
entity list                    # List all entities
entity list PlayerComponent    # Filter by component
entity list Position,Renderable # Filter by multiple components (AND)
entity list --count            # Show only count
entity list --verbose          # Show detailed info
```

**Implementation Details**:
- Query `EcsWorld.Instance` for entities
- Support component filter using `World.Query()` with dynamic component types
- Display: Entity ID, Component count, Has tags
- Use `ConsoleOutputLevel.System` for headers
- Color code by entity state (active/inactive)

**Auto-Completion**:
- Position 0: Suggest `list`, `inspect`, `destroy`, `create`
- Position 1: Suggest component type names from reflection
- Position 2+: Additional component names for filtering

#### 1.2 Entity Inspect Command

**Command**: `entity inspect <id>`
**Aliases**: `ei`, `inspect`
**Description**: Shows detailed information about a specific entity

**Syntax**:
```
entity inspect 42              # Inspect entity 42
entity inspect 42 --components # Show only components
entity inspect 42 --json       # Output as JSON
```

**Display Format**:
```
Entity: #42
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Alive: Yes
Component Count: 5

Components:
  ├─ PositionComponent
  │  └─ X: 10.5, Y: 20.3, Z: 0.0
  ├─ RenderableComponent
  │  └─ Visible: true, Layer: 1
  ├─ PlayerComponent
  └─ DirectionComponent
     └─ Current: South
```

**Implementation Details**:
- Use `World.Has<T>()` to check component presence
- Reflection to enumerate all component types
- Format component data using `ToString()` or custom formatting
- Handle components without public constructors
- Error handling for invalid entity IDs

**Auto-Completion**:
- Position 1: Suggest recent entity IDs from query cache
- Position 2: Suggest `--components`, `--json`, `--verbose`

#### 1.3 Component Management Commands

**Command**: `component add <entity> <type> [values]`
**Aliases**: `comp`
**Description**: Adds a component to an entity

**Syntax**:
```
component add 42 PlayerComponent
component add 42 PositionComponent x=10 y=20 z=0
component add 42 DirectionComponent direction=North
```

**Command**: `component remove <entity> <type>`
**Description**: Removes a component from an entity

**Syntax**:
```
component remove 42 PlayerComponent
```

**Command**: `component set <entity> <type> <field> <value>`
**Description**: Sets a component field value

**Syntax**:
```
component set 42 PositionComponent X 100
component set 42 RenderableComponent Visible false
```

**Implementation Details**:
- Use `World.Add<T>(entity)` and `World.Remove<T>(entity)`
- Parse key=value pairs for component initialization
- Use reflection for field/property assignment
- Type conversion for numeric, string, enum values
- Validate entity exists before modification
- Fire appropriate events (if components have events)

**Auto-Completion**:
- Position 1: Entity IDs
- Position 2: Component type names
- Position 3+: Component field names (from reflection)

#### 1.4 System Management Commands

**Command**: `system list [--verbose]`
**Aliases**: `systems`, `sys`
**Description**: Lists all registered ECS systems

**Display Format**:
```
Active Systems (12):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  [✓] InputSystem              Priority: 100  Enabled
  [✓] MovementSystem            Priority: 90   Enabled
  [✓] CameraSystem              Priority: 80   Enabled
  [×] DebugOverlaySystem        Priority: 10   Disabled
```

**Command**: `system toggle <name>`
**Description**: Enables/disables a specific system

**Syntax**:
```
system toggle MovementSystem   # Toggle state
system enable MovementSystem   # Explicitly enable
system disable MovementSystem  # Explicitly disable
```

**Command**: `system info <name>`
**Description**: Shows detailed system information

**Implementation Details**:
- Query `EcsService` for registered systems
- Track system state (enabled/disabled)
- Use `IPrioritizedSystem` interface if available
- Display update frequency, last execution time
- Warning when disabling critical systems

**Auto-Completion**:
- Position 1: System names from registry
- Position 2: `enable`, `disable`, `toggle`, `info`

### 2. Performance Commands (Category: "Performance")

Commands for profiling and performance analysis.

#### 2.1 Performance Stats Command

**Command**: `perf stats [category]`
**Aliases**: `stats` (already exists - extend)
**Description**: Display performance statistics

**Enhanced Categories**:
```
perf stats fps      # Frame rate
perf stats frame    # Frame time breakdown
perf stats memory   # Memory usage
perf stats gc       # Garbage collection
perf stats ecs      # ECS-specific metrics
perf stats render   # Rendering statistics
perf stats all      # Comprehensive view
```

**ECS Metrics Display**:
```
ECS Statistics:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Entities:        1,245
  Archetypes:         42
  Active Systems:     18
  Queries/Frame:   2,156
  Avg Query Time:  0.12ms
```

#### 2.2 Performance Profile Command

**Command**: `perf profile [duration]`
**Aliases**: `profile`
**Description**: Records performance data for analysis

**Syntax**:
```
perf profile          # Profile for 5 seconds
perf profile 10       # Profile for 10 seconds
perf profile --systems # Profile system execution times
perf profile --events  # Profile event dispatch times
```

**Implementation Details**:
- Start profiling timer
- Collect frame time samples
- Track system execution times
- Monitor event dispatch performance
- Generate summary report with percentiles (p50, p95, p99)

**Output Format**:
```
Performance Profile (10.0s, 600 frames):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Frame Time:
  Min:     12.3 ms
  Max:     45.6 ms
  Average: 16.7 ms
  P50:     16.2 ms
  P95:     22.1 ms
  P99:     35.8 ms

Top Systems by Time:
  1. RenderingSystem     8.2 ms (49%)
  2. PhysicsSystem       3.1 ms (19%)
  3. MovementSystem      1.8 ms (11%)
```

#### 2.3 Memory Command

**Command**: `perf memory [--gc]`
**Description**: Display detailed memory usage

**Enhanced Output**:
```
Memory Statistics:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Managed Heap:     128.5 MB
  Gen 0:            12.3 MB
  Gen 1:            24.5 MB
  Gen 2:            91.7 MB

ECS Memory:
  Entities:          2.1 MB
  Components:       15.4 MB
  Chunk Memory:      8.9 MB

Collections:
  Gen 0:           1,234 (↑12 since last check)
  Gen 1:             156 (↑2)
  Gen 2:              23 (→0)
```

**Command**: `perf gc`
**Description**: Force garbage collection

### 3. Event Commands (Category: "Events")

Commands for inspecting and manipulating the event system.

#### 3.1 Event List Command

**Command**: `event list [--active]`
**Aliases**: `events`
**Description**: Lists all registered event types

**Display Format**:
```
Registered Event Types (45):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  MovementCompletedEvent          3 subscribers
  MapLoadedEvent                  5 subscribers
  InteractionTriggeredEvent       2 subscribers
  ConsoleToggledEvent             1 subscriber
  CommandExecutedEvent            0 subscribers
```

**Filters**:
- `--active`: Only show events with subscribers
- `--category <name>`: Filter by event namespace
- `--recent`: Show recently fired events (requires monitoring)

**Implementation Details**:
- Call `EventBus.GetRegisteredEventTypes()`
- Display subscriber counts
- Group by namespace/category
- Color code by activity (green=active, gray=unused)

#### 3.2 Event Send Command

**Command**: `event send <type> [data]`
**Aliases**: `fire`, `emit`
**Description**: Manually fires an event

**Syntax**:
```
event send GamePausedEvent
event send MapTransitionEvent mapId=2 x=10 y=20
event send FlagChangedEvent flagName=test_flag value=true
```

**Implementation Details**:
- Parse event type name
- Create event struct using reflection
- Parse key=value pairs for fields
- Call `EventBus.Send<T>()`
- Validate event type exists
- Show confirmation and subscriber count

**Safety Features**:
- Warn when sending events with no subscribers
- Validate required fields are provided
- Confirm for potentially dangerous events

**Auto-Completion**:
- Position 1: Event type names from reflection
- Position 2+: Field names for the event type

#### 3.3 Event Subscribe Command

**Command**: `event subscribe <type>`
**Description**: Monitor when an event is fired

**Syntax**:
```
event subscribe MovementCompletedEvent
event subscribe MapLoadedEvent --once
event unsubscribe MovementCompletedEvent
event unsubscribe --all
```

**Implementation Details**:
- Subscribe to event with logging handler
- Store subscription handle for cleanup
- Display event data when fired
- Support one-time subscriptions
- Maintain list of active monitors

**Output Example**:
```
[Event] MovementCompletedEvent fired:
  Entity: 42
  OldPosition: (10, 20)
  NewPosition: (11, 20)
  Direction: East
  Success: true
```

#### 3.4 Event Stats Command

**Command**: `event stats [type]`
**Description**: Shows event dispatch statistics

**Syntax**:
```
event stats                    # All events
event stats MovementCompletedEvent
event stats --reset            # Reset counters
```

**Display Format**:
```
Event Statistics:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
MovementCompletedEvent:
  Fired:           1,234 times
  Subscribers:         3
  Avg Dispatch:    0.08 ms
  Max Dispatch:    0.45 ms
  Last Fired:      2.3s ago
```

**Implementation Details**:
- Hook into `EventDispatchHook`
- Track fire count, timing, frequency
- Maintain rolling window of recent events
- Identify slow event handlers

### 4. Debug Commands (Category: "Debug")

Commands for debugging and visualization.

#### 4.1 Time Commands (Already Implemented - Enhance)

**Existing Commands**: `time`, `pause`, `resume`, `step`
**Enhancements**:
- Add `time bookmark` - Save/restore time states
- Add `time replay` - Frame-by-frame replay
- Add `time profile` - Profile time-dependent code

#### 4.2 FPS Command

**Command**: `debug fps [target]`
**Aliases**: `fps`
**Description**: Sets target FPS or displays current

**Syntax**:
```
debug fps             # Show current FPS
debug fps 30          # Set target to 30 FPS
debug fps 60          # Set target to 60 FPS
debug fps unlimited   # Remove FPS cap
```

#### 4.3 Overlay Command

**Command**: `debug overlay <panel>`
**Aliases**: `overlay`
**Description**: Toggles debug overlay panels

**Syntax**:
```
debug overlay list            # List available panels
debug overlay fps             # Toggle FPS counter
debug overlay position        # Toggle position display
debug overlay ecs             # Toggle ECS stats
debug overlay events          # Toggle event monitor
debug overlay all             # Toggle all panels
debug overlay none            # Hide all panels
```

**Available Panels**:
- `fps` - Frame rate display
- `position` - Player position
- `ecs` - Entity/system stats
- `events` - Recent events
- `memory` - Memory usage
- `input` - Input state
- `collisions` - Collision bounds

#### 4.4 Screenshot Command

**Command**: `debug screenshot [filename]`
**Aliases**: `screenshot`, `ss`
**Description**: Captures a screenshot

**Syntax**:
```
debug screenshot              # Auto-named file
debug screenshot test.png     # Specific name
debug screenshot --clipboard  # Copy to clipboard
```

#### 4.5 Logging Command

**Command**: `debug log [level] [filter]`
**Description**: Controls logging output

**Syntax**:
```
debug log                     # Show current level
debug log debug               # Set to debug level
debug log info                # Set to info level
debug log filter ECS          # Filter by category
debug log clear               # Clear log buffer
```

## Command Interface Design

### Base Command Structure

All commands follow this pattern:

```csharp
namespace MonoBall.Core.Diagnostics.Console.Commands;

[ConsoleCommand]
public sealed class EntityListCommand : IConsoleCommand
{
    public string Name => "entity";
    public string Description => "Entity management and inspection";
    public string Usage => "entity [list|inspect|add|remove] ...";
    public string Category => "ECS";
    public string[] Aliases => ["entities", "ent"];

    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        // Implementation
    }

    public IEnumerable<string> GetCompletions(string[] args, int argIndex)
    {
        // Return context-aware completions
    }
}
```

### Sub-Command Pattern

For commands with multiple sub-commands (like `entity`, `event`, `system`):

```csharp
public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
{
    if (args.Length == 0)
    {
        ShowHelp(context);
        return Task.FromResult(false);
    }

    var subCommand = args[0].ToLowerInvariant();
    var subArgs = args.Skip(1).ToArray();

    return subCommand switch
    {
        "list" => ExecuteList(context, subArgs),
        "inspect" => ExecuteInspect(context, subArgs),
        "add" => ExecuteAdd(context, subArgs),
        "remove" => ExecuteRemove(context, subArgs),
        _ => HandleUnknownSubCommand(context, subCommand)
    };
}
```

## Auto-Completion Strategy

### Hierarchical Completion

Completions are context-aware based on argument position:

```csharp
public IEnumerable<string> GetCompletions(string[] args, int argIndex)
{
    if (argIndex == 0)
    {
        // First argument: sub-commands
        return ["list", "inspect", "add", "remove"];
    }

    var subCommand = args[0].ToLowerInvariant();

    return subCommand switch
    {
        "list" => CompleteForList(args, argIndex),
        "inspect" => CompleteForInspect(args, argIndex),
        "add" => CompleteForAdd(args, argIndex),
        _ => Enumerable.Empty<string>()
    };
}
```

### Completion Sources

1. **Static Lists**: Pre-defined values (sub-commands, flags)
2. **Reflection**: Component/System type names
3. **Runtime Queries**: Entity IDs, event types
4. **Historical Data**: Recently used values

### Smart Completion Features

- **Fuzzy Matching**: Partial name matching
- **Type-Aware**: Suggest valid values for typed parameters
- **Context Filtering**: Filter by current game state
- **Rich Descriptions**: Show descriptions alongside completions

## Help System Design

### Multi-Level Help

Commands provide help at multiple levels:

1. **Command List**: `help` shows all commands by category
2. **Command Help**: `help entity` shows command overview
3. **Sub-Command Help**: `entity help` shows sub-commands
4. **Usage Examples**: `entity list --help` shows detailed usage

### Help Format

```
Command: entity
Category: ECS
Description: Entity management and inspection

Usage:
  entity list [filter]           List entities
  entity inspect <id>            Show entity details
  entity add <type>              Create entity
  entity remove <id>             Destroy entity

Examples:
  entity list PlayerComponent    Find player entities
  entity inspect 42              View entity #42
  entity add PlayerComponent     Create player entity

Aliases: entities, ent

See also: component, system
```

### Help Command Enhancement

```csharp
[ConsoleCommand]
public sealed class HelpCommand : IConsoleCommand
{
    // Enhanced to show:
    // - Command categories with counts
    // - Recent commands
    // - Most used commands
    // - Quick start guide
}
```

## Integration with ECS/Event Systems

### ECS Service Access

Commands require access to the ECS world:

```csharp
public interface IConsoleContext
{
    // Existing properties
    IConsoleCommandRegistry CommandRegistry { get; }
    IPerformanceStats? PerformanceStats { get; }
    ITimeControl? TimeControl { get; }

    // New property for ECS access
    IEcsService? EcsService { get; }

    // Output methods...
}
```

### Event Monitoring

Commands can subscribe to events for monitoring:

```csharp
private readonly Dictionary<string, IDisposable> _eventSubscriptions = new();

private void MonitorEvent<T>(string name) where T : struct
{
    var subscription = EventBus.Subscribe<T>(evt =>
    {
        _context.WriteSystem($"[Event] {name}:");
        // Display event fields
    });

    _eventSubscriptions[name] = subscription;
}

private void StopMonitoring(string name)
{
    if (_eventSubscriptions.TryGetValue(name, out var sub))
    {
        sub.Dispose();
        _eventSubscriptions.Remove(name);
    }
}
```

### Safe Type Resolution

Use reflection safely for dynamic component/event access:

```csharp
private Type? ResolveComponentType(string name)
{
    // Try exact match first
    var type = Type.GetType($"MonoBall.Core.ECS.Components.{name}");
    if (type != null) return type;

    // Try partial match
    var assembly = typeof(EcsWorld).Assembly;
    return assembly.GetTypes()
        .Where(t => t.Namespace?.StartsWith("MonoBall.Core.ECS.Components") ?? false)
        .FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
```

## Error Handling & Validation

### Input Validation

All commands perform validation before execution:

```csharp
private bool ValidateEntityId(IConsoleContext context, string idStr, out int entityId)
{
    if (!int.TryParse(idStr, out entityId))
    {
        context.WriteError($"Invalid entity ID: {idStr}");
        return false;
    }

    var entity = new Entity(entityId, 0); // Create entity reference
    if (!EcsWorld.Instance.IsAlive(entity))
    {
        context.WriteError($"Entity {entityId} does not exist");
        return false;
    }

    return true;
}
```

### Safe Execution

Wrap potentially failing operations:

```csharp
try
{
    var component = new PositionComponent { X = x, Y = y, Z = z };
    EcsWorld.Instance.Add(entity, component);
    context.WriteSuccess($"Added PositionComponent to entity {entityId}");
    return true;
}
catch (Exception ex)
{
    context.WriteError($"Failed to add component: {ex.Message}");
    Logger.Error(ex, "Component add failed");
    return false;
}
```

## Performance Considerations

### Query Caching

Cache expensive queries for reuse:

```csharp
private static class EntityQueryCache
{
    private static DateTime _lastUpdate;
    private static List<int> _cachedEntityIds = new();

    public static IEnumerable<int> GetAllEntityIds()
    {
        if (DateTime.Now - _lastUpdate > TimeSpan.FromSeconds(1))
        {
            RefreshCache();
        }
        return _cachedEntityIds;
    }

    private static void RefreshCache()
    {
        _cachedEntityIds.Clear();
        var query = new QueryDescription().WithAll<PositionComponent>();
        EcsWorld.Instance.Query(in query, (Entity entity) =>
        {
            _cachedEntityIds.Add(entity.Id);
        });
        _lastUpdate = DateTime.Now;
    }
}
```

### Lazy Evaluation

Only compute what's needed:

```csharp
public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
{
    var showVerbose = args.Contains("--verbose");
    var showCount = args.Contains("--count");

    if (showCount)
    {
        // Fast path: just count
        var count = CountEntities();
        context.WriteLine($"Total entities: {count}");
        return Task.FromResult(true);
    }

    // Full enumeration only if needed
    var entities = GetEntities(showVerbose);
    // ...
}
```

### Async Operations

Use async for long-running operations:

```csharp
public async Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
{
    context.WriteSystem("Profiling for 10 seconds...");

    var profiler = new SystemProfiler();
    await profiler.ProfileAsync(TimeSpan.FromSeconds(10));

    var results = profiler.GetResults();
    DisplayResults(context, results);

    return true;
}
```

## Implementation Priority

### Phase 1: Core ECS Commands (High Priority)
1. ✅ `entity list` - Essential for debugging
2. ✅ `entity inspect` - Most used command
3. ✅ `system list` - System visibility
4. ✅ `system toggle` - Enable/disable systems

### Phase 2: Event & Performance (Medium Priority)
5. ✅ `event list` - Event system inspection
6. ✅ `event send` - Manual event testing
7. ✅ `perf profile` - Performance analysis
8. ✅ Enhanced `stats` command - Better metrics

### Phase 3: Advanced Features (Lower Priority)
9. ⚙️ `component add/remove` - Entity manipulation
10. ⚙️ `event subscribe` - Event monitoring
11. ⚙️ `debug overlay` - Visual debugging
12. ⚙️ Advanced completions - UX improvement

## Testing Strategy

### Unit Tests

Each command should have tests for:
- Argument parsing
- Validation logic
- Output formatting
- Error handling

### Integration Tests

Test command interaction with:
- ECS World queries
- EventBus operations
- System management
- Performance stats

### Manual Testing

Create test scenarios:
- Create entities, add components, inspect
- Fire events, monitor subscriptions
- Profile during gameplay
- Test completions in real console

## Documentation

### In-Code Documentation

All commands require:
- XML doc comments
- Usage examples
- Parameter descriptions
- Return value documentation

### User Documentation

Create user guide covering:
- Command reference (auto-generated from code)
- Common workflows
- Troubleshooting tips
- Performance best practices

## Future Enhancements

### Scripting Support

Allow command scripts:
```
# setup_test.console
entity add PlayerComponent
component set 0 Position X=10 Y=20
event subscribe MovementCompletedEvent
time pause
```

Execute with: `script run setup_test.console`

### Command History

Enhanced history features:
- Search history by pattern
- Save favorite commands
- Command bookmarks
- History replay

### Remote Console

Network-accessible console:
- WebSocket connection
- Web UI for command execution
- Real-time event streaming
- Remote profiling

### AI Assistant

Natural language command parsing:
```
> find all entities with player components
Executing: entity list PlayerComponent

> show me entity 42's position
Executing: entity inspect 42 --filter PositionComponent
```

## Conclusion

This architecture provides a comprehensive, extensible console command system that integrates seamlessly with the existing MonoBall engine architecture. The design prioritizes:

- **Consistency**: Follows established patterns
- **Usability**: Rich auto-completion and help
- **Safety**: Validation and error handling
- **Performance**: Efficient queries and caching
- **Extensibility**: Easy to add new commands

The phased implementation allows for incremental development while maintaining a stable console system throughout development.

## File Organization

Commands should be organized as:

```
MonoBall.Core/
└── Diagnostics/
    └── Console/
        └── Commands/
            ├── IConsoleCommand.cs (existing)
            ├── BuiltInCommands.cs (existing)
            ├── DebugCommands.cs (existing - enhance)
            ├── EcsCommands.cs (new)
            ├── EventCommands.cs (new)
            ├── PerformanceCommands.cs (new - extend existing)
            └── OverlayCommands.cs (new)
```

## Dependencies

New dependencies required:
- None - all commands use existing engine APIs

Optional enhancements:
- JSON serialization library for `--json` output
- Chart/graph library for performance visualization
- Scripting engine for command scripts
