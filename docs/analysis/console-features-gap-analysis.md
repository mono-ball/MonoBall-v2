# Console Panel Features - Gap Analysis
**Analysis Date:** 2026-01-05
**Scope:** Console Debug Panel ONLY
**Focus:** Command execution, auto-completion, history, output formatting

---

## Executive Summary

The OldMonoBall C# implementation has a **fully-featured enterprise-grade console panel** with sophisticated command execution, intelligent auto-completion, history navigation, and rich output formatting. The current TypeScript/WebGPU framework appears to have **NO console panel implementation**.

### Critical Findings

1. **Complete Console System Missing** - No debug console UI exists in new framework
2. **Rich Command Infrastructure** - OldMonoBall has extensible command system with auto-discovery
3. **Advanced Auto-Completion** - Multi-line popup with fuzzy matching and descriptions
4. **Robust History Management** - Full history navigation with search capabilities
5. **Thread-Safe Architecture** - Ring buffers and lock-based synchronization

---

## Current Implementation (OldMonoBall C#)

### Architecture Overview

```
Console System Components:
├── ConsolePanel.cs           - ImGui UI panel with input/output
├── ConsoleService.cs         - Command orchestration service
├── ConsoleCommandRegistry.cs - Command discovery and management
├── ConsoleHistory.cs         - History navigation with search
├── ConsoleBuffer.cs          - Thread-safe output buffer (ring buffer)
├── CompletionItem.cs         - Auto-completion data structures
├── Built-in Commands:
│   ├── HelpCommand           - List commands, show command help
│   ├── ClearCommand          - Clear console output
│   ├── EchoCommand           - Print text
│   ├── HistoryCommand        - Show/search command history
│   ├── VersionCommand        - Show version info
│   └── Debug Commands:
│       ├── StatsCommand      - Performance statistics (fps, memory, gc)
│       ├── TimeCommand       - Time control (pause, resume, step, scale)
│       ├── PauseCommand      - Quick pause
│       ├── ResumeCommand     - Quick resume
│       └── StepCommand       - Frame stepping
└── Events:
    ├── ConsoleToggledEvent   - Console visibility changed
    ├── CommandSubmittedEvent - Command entered
    ├── CommandExecutedEvent  - Command completed
    └── ConsoleOutputEvent    - Output written
```

---

## Feature Inventory

### 1. Console Panel UI (ConsolePanel.cs)

#### ✅ Features Present in OldMonoBall

**Input Area:**
- ✅ **4KB unsafe byte buffer** for text input (performance optimization)
- ✅ **Prompt symbol** (">" with colored styling)
- ✅ **ImGui InputText** with multiple callbacks:
  - `CallbackCompletion` - Tab key handling
  - `CallbackHistory` - Up/Down arrow navigation
  - `CallbackEdit` - Live completion updates
  - `CallbackAlways` - Cursor position management
- ✅ **Enter to submit** commands
- ✅ **Auto-focus** after command submission
- ✅ **Command parsing** with quote support and escape sequences

**Output Area:**
- ✅ **Scrollable output** with horizontal scrollbar support
- ✅ **Color-coded text** by severity level:
  - Normal (white)
  - Command echo (cyan)
  - Success (green)
  - Warning (yellow)
  - Error (red)
  - System (blue/dim)
- ✅ **Auto-scroll to bottom** on new messages
- ✅ **Ring buffer** (1000 lines max, thread-safe)
- ✅ **TextUnformatted rendering** (prevents printf vulnerabilities)
- ✅ **Timestamp support** (stored but not displayed by default)

**Panel Management:**
- ✅ **IDebugPanel interface** integration
- ✅ **IDebugPanelLifecycle** support (Initialize/Update/Draw/Dispose)
- ✅ **Default size** (600x400)
- ✅ **Category** ("Tools")
- ✅ **NerdFont icon** support
- ✅ **Visibility toggle**

#### ❌ Missing in New Framework
- ❌ **NO console UI exists at all**
- ❌ NO input field
- ❌ NO output display
- ❌ NO command execution
- ❌ NO panel integration

---

### 2. Auto-Completion System

#### ✅ Features Present in OldMonoBall

**Completion Popup:**
- ✅ **Smart popup positioning** (above input, full width)
- ✅ **Multi-line completion items** (name + description, 36px height)
- ✅ **Category badges** with color coding:
  - `[command]` - Green (primary commands)
  - `[alias]` - Yellow (command aliases)
  - `[arg]` - Blue (arguments)
- ✅ **Scrollable list** (max 8 visible items, 250px height)
- ✅ **Custom selection styling** (overrides ImGui defaults)
- ✅ **Hover highlighting**

**Keyboard Navigation:**
- ✅ **Tab key** - Cycle through completions (first Tab shows popup)
- ✅ **Up/Down arrows** - Navigate selection
- ✅ **Enter** - Accept selected completion
- ✅ **Escape** - Close popup
- ✅ **Auto-dismiss** on space, semicolon, or braces

**Completion Logic:**
- ✅ **Fuzzy matching** via `StartsWith` (case-insensitive)
- ✅ **Command name completion** (first word)
- ✅ **Argument completion** (subsequent words)
- ✅ **Alias display** (shows "→ command_name: description")
- ✅ **Live updates** as user types (via CallbackEdit)
- ✅ **Single match auto-apply** (immediate completion)
- ✅ **Rich completion items** (CompletionItem struct with text, description, category)

**Smart Behavior:**
- ✅ **Prevents double-Enter bug** (completion vs submit flag)
- ✅ **Cursor positioning** after completion (moves to end)
- ✅ **Spaces automatically added** after completion
- ✅ **Focus management** (returns focus to input)
- ✅ **Scroll-to-selected** in popup

**CompletionItem Structure:**
```csharp
public readonly struct CompletionItem {
    public string Text { get; init; }        // Text to insert
    public string Description { get; init; } // Help text
    public string Category { get; init; }    // command/alias/arg

    // Factory methods:
    static CompletionItem Command(string name, string description)
    static CompletionItem Argument(string text, string description = "")
    static CompletionItem Simple(string text)
}
```

#### ❌ Missing in New Framework
- ❌ **NO auto-completion** at all
- ❌ NO completion popup
- ❌ NO fuzzy matching
- ❌ NO keyboard navigation
- ❌ NO command suggestions

---

### 3. Command History System (ConsoleHistory.cs)

#### ✅ Features Present in OldMonoBall

**History Storage:**
- ✅ **List-based storage** (simple, performant)
- ✅ **Configurable max size** (default 100 entries)
- ✅ **Duplicate suppression** (doesn't re-add same command consecutively)
- ✅ **Automatic trimming** (removes oldest when over limit)

**Navigation:**
- ✅ **Up arrow** - Navigate to previous command
- ✅ **Down arrow** - Navigate to next command
- ✅ **End of history** - Returns to saved input
- ✅ **Input preservation** - Saves current input when starting navigation
- ✅ **Reset on navigation end** - Clears saved input
- ✅ **Circular navigation** - Wraps at ends

**Search:**
- ✅ **Search by substring** (case-insensitive)
- ✅ **Returns matches reversed** (most recent first)
- ✅ **Returns all if no query** (reverse chronological)

**HistoryCommand Integration:**
- ✅ **`history` command** - Shows all history
- ✅ **`history <query>` command** - Searches history
- ✅ **Displays up to 20 entries** with "...and N more" message
- ✅ **Numbered list** (1-indexed)

**State Management:**
```csharp
private List<string> _history;              // Storage
private int _navigationIndex = -1;          // Current position (-1 = not navigating)
private string _savedInput = string.Empty;  // Preserved input

public bool IsNavigating => _navigationIndex >= 0;
public int Count => _history.Count;
public IReadOnlyList<string> GetAll() => _history;
```

#### ❌ Missing in New Framework
- ❌ **NO history system**
- ❌ NO navigation (Up/Down arrows)
- ❌ NO history storage
- ❌ NO search capability
- ❌ NO history command

---

### 4. Command Execution System

#### ✅ Features Present in OldMonoBall

**ConsoleService.cs:**
- ✅ **Async command execution** (`ExecuteCommandAsync`)
- ✅ **Command-line parsing** with quote support:
  - Handles escaped characters (`\"`, `\\`)
  - Respects quoted strings
  - Splits by spaces outside quotes
- ✅ **History tracking** (adds to history before execution)
- ✅ **Command echo** (prints "> command" in cyan)
- ✅ **Error handling** with try-catch
- ✅ **Event bus integration**:
  - CommandSubmittedEvent (on Enter)
  - CommandExecutedEvent (after completion, includes success/error)
  - ConsoleOutputEvent (on WriteLine)
  - ConsoleToggledEvent (on show/hide)
- ✅ **Unknown command handling** (suggests "help" command)

**IConsoleCommand Interface:**
```csharp
interface IConsoleCommand {
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    string Category { get; }
    string[] Aliases { get; } = [];

    Task<bool> ExecuteAsync(IConsoleContext context, string[] args);
    IEnumerable<string> GetCompletions(string[] args, int argIndex) => [];
}
```

**Command Registry (ConsoleCommandRegistry.cs):**
- ✅ **Auto-discovery** via `[ConsoleCommand]` attribute
- ✅ **Case-insensitive** command/alias lookups
- ✅ **Alias system** (multiple names per command)
- ✅ **Category grouping** (for "help" command display)
- ✅ **Rich completions** with descriptions
- ✅ **Reflection-based discovery** (scans assembly on startup)
- ✅ **Runtime registration/unregistration**
- ✅ **Overwrite warnings** (logs conflicts)

**IConsoleContext Interface:**
```csharp
interface IConsoleContext {
    IConsoleCommandRegistry CommandRegistry { get; }
    IPerformanceStats? PerformanceStats { get; }
    ITimeControl? TimeControl { get; }

    void WriteLine(string text, ConsoleOutputLevel level = Normal);
    void WriteSuccess(string text);
    void WriteWarning(string text);
    void WriteError(string text);
    void WriteSystem(string text);
    void Clear();
}
```

#### ❌ Missing in New Framework
- ❌ **NO command execution system**
- ❌ NO command registry
- ❌ NO command discovery
- ❌ NO async execution
- ❌ NO command-line parsing
- ❌ NO event integration

---

### 5. Built-In Commands

#### ✅ Present in OldMonoBall

**General Commands (BuiltInCommands.cs):**

1. **HelpCommand** (`help`, `?`, `h`)
   - Shows all commands grouped by category
   - Shows detailed help for specific command
   - Displays aliases, usage, description
   - Color-coded category headers

2. **ClearCommand** (`clear`, `cls`)
   - Clears console output buffer
   - Simple, no arguments

3. **EchoCommand** (`echo`)
   - Prints text to console
   - Joins all arguments with spaces
   - Basic sanity test for command system

4. **HistoryCommand** (`history`)
   - Shows last 20 commands (numbered)
   - Searches history with optional query
   - Displays "...and N more" if truncated

5. **VersionCommand** (`version`, `ver`)
   - Shows version info (hardcoded "v1.0")
   - Displays runtime version (.NET)
   - Shows OS and architecture (64-bit)

**Debug Commands (DebugCommands.cs):**

6. **StatsCommand** (`stats`, `perf`)
   - **Arguments:** `[fps|frame|memory|gc|all]`
   - **FPS mode:** Current FPS + frame time (color-coded)
   - **Frame mode:** Current/target frame time comparison
   - **Memory mode:** Managed heap, entity count, draw calls
   - **GC mode:** Gen0/1/2 collection counts
   - **All mode:** Comprehensive overview
   - **Color coding:**
     - Green: FPS ≥60, frame time <16.67ms
     - Yellow: FPS 30-60, frame time 16.67-33.33ms
     - Red: FPS <30, frame time >33.33ms

7. **TimeCommand** (`time`)
   - **Subcommands:**
     - `pause` - Pause game
     - `resume` - Resume game
     - `toggle` - Toggle pause state
     - `step [n]` - Step N frames (default 1)
     - `scale <value>` - Set time scale (float)
     - `slowmo <percent>` - Set speed percentage (0-200%)
   - **No args:** Shows current time control state
   - **Completions:** All subcommands
   - **Status display:**
     - State (PAUSED/RUNNING) - color-coded
     - Time scale (1.0 = normal speed)
     - Pending step frames (if any)

8. **PauseCommand** (`pause`)
   - Quick shortcut for `time pause`
   - Shows message if already paused

9. **ResumeCommand** (`resume`, `unpause`)
   - Quick shortcut for `time resume`
   - Shows message if already running

10. **StepCommand** (`step`)
    - Quick shortcut for `time step [n]`
    - Defaults to 1 frame
    - Useful for frame-by-frame debugging

**Command Categories:**
- **General:** help, clear, echo, history, version (5 commands)
- **Debug:** stats, time, pause, resume, step (5 commands)
- **Total:** 10 built-in commands + 8 aliases

#### ❌ Missing in New Framework
- ❌ **NO commands exist**
- ❌ NO help system
- ❌ NO performance stats access
- ❌ NO time control
- ❌ NO debugging utilities

---

### 6. Output Formatting System

#### ✅ Features Present in OldMonoBall

**ConsoleBuffer.cs:**
- ✅ **Thread-safe ring buffer** (lock-based)
- ✅ **Configurable max lines** (default 1000)
- ✅ **Automatic trimming** (removes oldest)
- ✅ **BufferEntry struct:**
  ```csharp
  record struct BufferEntry(
      string Text,
      Vector4 Color,
      DateTime Timestamp
  );
  ```
- ✅ **Efficient iteration** (ForEach without copying)
- ✅ **Snapshot support** (GetEntries returns copy)
- ✅ **Clear operation**

**ConsoleOutputLevel enum:**
```csharp
enum ConsoleOutputLevel {
    Normal,   // White/default
    Command,  // Cyan (echoed commands)
    Success,  // Green
    Warning,  // Yellow
    Error,    // Red
    System    // Blue/dim (help text, status)
}
```

**ConsoleColors class:**
- ✅ **Pokéball-themed color scheme** (matches debug panel theme)
- ✅ **GetColor(ConsoleOutputLevel)** helper
- ✅ **Consistent across all panels**

**Output Methods:**
```csharp
// IConsoleContext methods:
WriteLine(string text, ConsoleOutputLevel level = Normal)
WriteSuccess(string text)    // Green
WriteWarning(string text)    // Yellow
WriteError(string text)      // Red
WriteSystem(string text)     // Blue/dim
Clear()                      // Clear all output
```

#### ❌ Missing in New Framework
- ❌ **NO output buffer**
- ❌ NO color formatting
- ❌ NO severity levels
- ❌ NO thread-safe storage
- ❌ NO ring buffer implementation

---

### 7. Integration Points

#### ✅ Features Present in OldMonoBall

**ECS Integration:**
- ✅ **ConsoleService** registered as service
- ✅ **IConsoleService** interface for DI
- ✅ **Access to performance stats** (IPerformanceStats)
- ✅ **Access to time control** (ITimeControl)
- ✅ **Event bus integration** (EventBus.Send)
- ✅ **Commands can query ECS** (via context)

**Debug Panel Integration:**
- ✅ **IDebugPanel interface** conformance
- ✅ **Lifecycle management** (Initialize/Update/Draw/Dispose)
- ✅ **Panel registry** support
- ✅ **Menu integration** (category: "Tools")
- ✅ **Status badge** potential (not implemented)

**ImGui Integration:**
- ✅ **Unsafe byte buffers** for performance
- ✅ **Callback system** for special keys
- ✅ **Custom styling** (colors, spacing, borders)
- ✅ **Window flags** (resizable, dockable)
- ✅ **Input focus management**
- ✅ **Scroll region management**

**Event System:**
```csharp
// Events fired by console:
struct ConsoleToggledEvent {
    bool IsVisible;
}

struct CommandSubmittedEvent {
    string CommandText;
}

struct CommandExecutedEvent {
    string CommandText;
    bool Success;
    string? ErrorMessage;
}

struct ConsoleOutputEvent {
    string Text;
    ConsoleOutputLevel Level;
}
```

#### ❌ Missing in New Framework
- ❌ **NO ECS integration**
- ❌ NO service registration
- ❌ NO event system integration
- ❌ NO performance stats access
- ❌ NO time control integration

---

## Comparison Matrix

| Feature Category | OldMonoBall C# | New Framework | Gap |
|-----------------|----------------|---------------|-----|
| **Console Panel UI** | ✅ Full ImGui panel | ❌ None | 100% |
| **Input Field** | ✅ 4KB buffer + callbacks | ❌ None | 100% |
| **Output Display** | ✅ Scrollable + colors | ❌ None | 100% |
| **Command Execution** | ✅ Async with parsing | ❌ None | 100% |
| **Command Registry** | ✅ Auto-discovery | ❌ None | 100% |
| **Auto-Completion** | ✅ Rich popup + fuzzy | ❌ None | 100% |
| **History System** | ✅ Navigation + search | ❌ None | 100% |
| **Built-in Commands** | ✅ 10 commands | ❌ None | 100% |
| **Output Formatting** | ✅ Color-coded levels | ❌ None | 100% |
| **Thread Safety** | ✅ Ring buffer + locks | ❌ None | 100% |
| **Event Integration** | ✅ 4 event types | ❌ None | 100% |
| **ECS Access** | ✅ Via IConsoleContext | ❌ None | 100% |

**Overall Gap:** **100% - Complete console system missing**

---

## Critical Missing Features

### P0 - Must Have (Blocks Development)

1. **Console Panel UI**
   - **What:** ImGui-based console window with input/output
   - **Why:** Core debug tool, essential for testing and debugging
   - **Effort:** 2-3 days (ImGui setup + basic UI)

2. **Command Execution System**
   - **What:** ConsoleService, command registry, IConsoleCommand interface
   - **Why:** Foundation for all console functionality
   - **Effort:** 2-3 days (architecture + basic commands)

3. **Output Buffer & Formatting**
   - **What:** Thread-safe ring buffer with color-coded output
   - **Why:** Display command results and system messages
   - **Effort:** 1 day (ring buffer + color system)

4. **Basic Built-in Commands**
   - **What:** help, clear, echo (minimum set)
   - **Why:** Console is unusable without basic commands
   - **Effort:** 1 day (3-5 commands)

---

### P1 - Should Have (Critical for Productivity)

5. **Auto-Completion System**
   - **What:** Tab completion with popup, keyboard navigation
   - **Why:** Significantly improves UX, reduces typing errors
   - **Effort:** 2-3 days (completion logic + popup UI)
   - **Research Note:** Most complex feature due to ImGui callbacks

6. **Command History**
   - **What:** Up/Down navigation, storage, search
   - **Why:** Essential for efficient console usage
   - **Effort:** 1 day (history storage + navigation)

7. **Debug Commands**
   - **What:** stats, time, pause, resume, step
   - **Why:** Core debugging functionality for game dev
   - **Effort:** 2 days (5 commands + integrations)
   - **Dependencies:** Requires IPerformanceStats, ITimeControl interfaces

8. **Event System Integration**
   - **What:** ConsoleToggledEvent, CommandExecutedEvent, etc.
   - **Why:** Allows other systems to react to console activity
   - **Effort:** 1 day (event definitions + dispatch)

---

### P2 - Nice to Have (UX Enhancements)

9. **Advanced Completion Features**
   - **What:** Category badges, multi-line items, rich descriptions
   - **Why:** Professional polish, better discoverability
   - **Effort:** 1-2 days (styling + metadata)

10. **History Search Command**
    - **What:** `history <query>` command with substring matching
    - **Why:** Useful for finding past commands
    - **Effort:** 0.5 days (search logic)

11. **Command Aliases**
    - **What:** Multiple names per command (e.g., `cls` for `clear`)
    - **Why:** Convenience for users familiar with other consoles
    - **Effort:** 0.5 days (alias registry)

12. **Welcome Message**
    - **What:** Startup message with basic instructions
    - **Why:** Helps new users discover features
    - **Effort:** 0.25 days (message formatting)

---

## Architecture Recommendations

### TypeScript Port Considerations

**1. Replace Unsafe C# Buffers:**
```typescript
// C# uses unsafe byte* buffers for performance
// TypeScript should use standard strings or Uint8Array if needed

// OldMonoBall C#:
private readonly byte[] _inputBuffer = new byte[4096];
unsafe fixed (byte* buf = _inputBuffer) { ... }

// TypeScript equivalent:
private inputBuffer: string = "";
// OR for binary safety:
private inputBuffer: Uint8Array = new Uint8Array(4096);
```

**2. Async/Await Pattern:**
```typescript
// Keep async command execution:
interface IConsoleCommand {
    async executeAsync(context: IConsoleContext, args: string[]): Promise<boolean>;
}

// Use Promise.all for parallel command discovery:
const commands = await Promise.all(
    commandModules.map(m => import(m))
);
```

**3. Ring Buffer Implementation:**
```typescript
// Use circular buffer pattern:
class ConsoleBuffer {
    private entries: BufferEntry[] = [];
    private maxLines: number;
    private writeIndex: number = 0;

    append(text: string, color: Vector4) {
        if (this.entries.length < this.maxLines) {
            this.entries.push({ text, color, timestamp: Date.now() });
        } else {
            this.entries[this.writeIndex] = { text, color, timestamp: Date.now() };
            this.writeIndex = (this.writeIndex + 1) % this.maxLines;
        }
    }
}
```

**4. ImGui Integration:**
```typescript
// If using imgui-js or dear-imgui-wasm:
import ImGui from 'imgui-js';

class ConsolePanel implements IDebugPanel {
    draw(deltaTime: number) {
        if (ImGui.Begin("Console", this.isVisibleRef)) {
            // Render output
            ImGui.BeginChild("Output", new ImGui.Vec2(0, -InputHeight));
            this.buffer.forEach(entry => {
                ImGui.PushStyleColor(ImGui.Col.Text, entry.color);
                ImGui.TextUnformatted(entry.text);
                ImGui.PopStyleColor();
            });
            ImGui.EndChild();

            // Render input
            ImGui.Separator();
            if (ImGui.InputText("##input", this.inputBuffer, this.inputFlags, this.inputCallback)) {
                this.submitCommand();
            }
        }
        ImGui.End();
    }
}
```

**5. Command Discovery:**
```typescript
// Use decorator pattern for auto-discovery:
function ConsoleCommand(enabled: boolean = true) {
    return function(target: any) {
        if (enabled) {
            CommandRegistry.register(new target());
        }
    }
}

@ConsoleCommand()
class HelpCommand implements IConsoleCommand {
    name = "help";
    description = "Shows available commands";
    // ...
}
```

---

## Implementation Roadmap

### Phase 1: Foundation (Week 1) - 5 days

**Goal:** Basic console with input/output

- Day 1: ImGui integration setup (if not done)
  - Install imgui-js or dear-imgui-wasm
  - Create ImGuiRenderer for WebGPU
  - Setup input handling bridge

- Day 2-3: Core Architecture
  - Implement IConsoleCommand interface
  - Create ConsoleCommandRegistry (auto-discovery)
  - Build ConsoleService (command execution)
  - Implement ConsoleBuffer (ring buffer)

- Day 4: Console Panel UI
  - Create ConsolePanel class
  - Implement output rendering (scrollable)
  - Implement input field (basic)
  - Add color system (ConsoleOutputLevel)

- Day 5: Basic Commands
  - HelpCommand (show all commands)
  - ClearCommand (clear output)
  - EchoCommand (test command)
  - VersionCommand (show version)

**Deliverable:** Working console that can execute 3-4 basic commands

---

### Phase 2: Essential Features (Week 2) - 5 days

**Goal:** Usable developer tool

- Day 1-2: Command History
  - Implement ConsoleHistory class
  - Add Up/Down arrow navigation
  - Save/restore input state
  - Create HistoryCommand

- Day 3-4: Auto-Completion (Basic)
  - Implement CompletionItem system
  - Add Tab key handling
  - Create completion popup (simple list)
  - Add keyboard navigation (Up/Down/Enter)

- Day 5: Debug Commands (Part 1)
  - Create IPerformanceStats interface
  - Implement StatsCommand (fps, memory, gc)
  - Add color-coded output

**Deliverable:** Console with history + basic tab completion + stats

---

### Phase 3: Advanced Features (Week 3) - 5 days

**Goal:** Professional polish

- Day 1-2: Time Control Commands
  - Create ITimeControl interface
  - Implement TimeCommand (pause/resume/step/scale)
  - Add PauseCommand, ResumeCommand, StepCommand shortcuts

- Day 3-4: Rich Auto-Completion
  - Add multi-line completion items (name + description)
  - Implement category badges (command/alias/arg)
  - Add smart dismiss (space/semicolon/braces)
  - Prevent double-Enter bug

- Day 5: Event System
  - Define console events (Toggled, Submitted, Executed, Output)
  - Integrate with game event bus
  - Add event dispatch to ConsoleService

**Deliverable:** Fully-featured console matching OldMonoBall capabilities

---

### Phase 4: Polish & Optimization (Week 4) - 3 days

**Goal:** Production-ready

- Day 1: Thread Safety
  - Add locks to ConsoleBuffer
  - Test concurrent access from multiple threads
  - Optimize ring buffer performance

- Day 2: Edge Cases
  - Test quote parsing edge cases
  - Handle malformed commands gracefully
  - Add error recovery

- Day 3: Documentation
  - Document command system
  - Create developer guide for adding commands
  - Write API documentation

**Deliverable:** Robust, documented console system

---

## Risk Assessment

### Technical Risks

1. **ImGui WebGPU Backend**
   - **Risk:** No official imgui-js WebGPU support yet
   - **Mitigation:** Use imgui-js with WebGL fallback, or implement custom backend
   - **Impact:** Medium - May need temporary WebGL renderer for ImGui

2. **Performance (TypeScript vs C# Unsafe Code)**
   - **Risk:** TypeScript strings slower than C# unsafe byte buffers
   - **Mitigation:** Use Uint8Array if needed, profile early
   - **Impact:** Low - Console input is not performance-critical

3. **Callback System Complexity**
   - **Risk:** ImGui callbacks for Tab/Up/Down are intricate
   - **Mitigation:** Port C# logic directly, test incrementally
   - **Impact:** Medium - May require debugging

### Schedule Risks

1. **Underestimating ImGui Integration**
   - **Risk:** ImGui setup takes longer than expected
   - **Mitigation:** Start with simple window first, iterate
   - **Impact:** High - Could delay entire console by 1-2 weeks

2. **Completion Popup Complexity**
   - **Risk:** Popup positioning and styling are fiddly
   - **Mitigation:** Ship basic list first, iterate styling
   - **Impact:** Medium - Can defer rich styling to Phase 3

---

## Success Metrics

### Must-Have (P0)
- ✅ Console window can open/close
- ✅ User can type commands and press Enter
- ✅ Commands execute and display output
- ✅ Output is color-coded by severity
- ✅ At least 3 built-in commands work (help, clear, echo)

### Should-Have (P1)
- ✅ Tab key shows completion suggestions
- ✅ Up/Down arrows navigate history
- ✅ Stats command shows FPS and memory
- ✅ Time command can pause/resume/step
- ✅ Commands are discovered via decorators/attributes

### Nice-to-Have (P2)
- ✅ Completion popup has descriptions and badges
- ✅ History command can search past commands
- ✅ Aliases work (e.g., `cls` for `clear`)
- ✅ Console emits events for other systems
- ✅ Thread-safe buffer handles concurrent writes

---

## Feature Parity Checklist

### Core Console (10 items)
- [ ] Console panel UI (ImGui window)
- [ ] Input field with prompt
- [ ] Output buffer (ring buffer, 1000 lines)
- [ ] Color-coded output (6 severity levels)
- [ ] Command execution (async)
- [ ] Command registry (auto-discovery)
- [ ] Command-line parsing (quotes, escapes)
- [ ] Error handling (try-catch, unknown commands)
- [ ] Event system (4 event types)
- [ ] Panel lifecycle (Initialize/Update/Draw/Dispose)

### Auto-Completion (8 items)
- [ ] Tab key triggers completion
- [ ] Popup with scrollable list
- [ ] Keyboard navigation (Tab/Up/Down/Enter/Escape)
- [ ] Fuzzy matching (case-insensitive StartsWith)
- [ ] Command name completion
- [ ] Argument completion
- [ ] Rich completion items (text + description + category)
- [ ] Smart dismiss (space/semicolon/braces)

### History (6 items)
- [ ] Up/Down arrow navigation
- [ ] Input preservation during navigation
- [ ] Duplicate suppression
- [ ] Configurable max size
- [ ] History command (show all)
- [ ] History search (by substring)

### Built-in Commands (10 items)
- [ ] HelpCommand (list all, show specific)
- [ ] ClearCommand (clear output)
- [ ] EchoCommand (print text)
- [ ] HistoryCommand (show/search)
- [ ] VersionCommand (version info)
- [ ] StatsCommand (fps, frame, memory, gc)
- [ ] TimeCommand (pause, resume, step, scale, slowmo)
- [ ] PauseCommand (shortcut)
- [ ] ResumeCommand (shortcut)
- [ ] StepCommand (shortcut)

### Integration (5 items)
- [ ] IPerformanceStats interface (FPS, memory, GC)
- [ ] ITimeControl interface (pause, resume, step, scale)
- [ ] Event bus integration (4 event types)
- [ ] ECS service registration (IConsoleService)
- [ ] Debug panel registry integration

**Total Items:** 39 features to port

---

## Conclusion

The OldMonoBall console panel is a **mature, production-ready developer tool** with:
- **39 distinct features** across 5 major categories
- **10 built-in commands** + 8 aliases
- **Advanced UX** (rich auto-completion, history navigation, color-coded output)
- **Robust architecture** (thread-safe, event-driven, extensible)

The new TypeScript/WebGPU framework has **ZERO console implementation**, creating a **100% feature gap**.

### Recommended Action
1. **Prioritize Phase 1-2** (Weeks 1-2) - Get to basic usability fast
2. **Start with minimal ImGui** - Don't wait for perfect WebGPU backend
3. **Port architecture directly** - The C# design is solid, reuse patterns
4. **Ship incrementally** - Basic console > History > Completion > Polish

### Estimated Effort
- **Phase 1 (Foundation):** 5 days → Basic usable console
- **Phase 2 (Essential):** 5 days → History + basic completion + stats
- **Phase 3 (Advanced):** 5 days → Full feature parity with OldMonoBall
- **Phase 4 (Polish):** 3 days → Production-ready

**Total:** 18 days (3.5 weeks) for complete console system

### Priority Justification
**CRITICAL** - Debug console is the **primary developer interface** for:
- Testing gameplay systems
- Debugging performance issues
- Controlling time (pause/step)
- Inspecting runtime state

Without a console, developers lose **significant productivity** and rely on external tools or rebuilds for basic testing.

---

**End of Console Features Gap Analysis**
*Next Steps: Implement Phase 1 foundation (5 days)*
