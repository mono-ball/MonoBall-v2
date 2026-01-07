# Console Testing Strategy - Comprehensive Test Plan

## Overview
This document outlines the complete testing strategy for the MonoBall Debug Console functionality, covering unit tests, integration tests, and UI tests.

---

## 1. Unit Tests

### 1.1 ConsoleBuffer Tests (`ConsoleBufferTests.cs`)

**Location**: `tests/Unit/Console/Features/ConsoleBufferTests.cs`

#### Test Cases:

##### Basic Operations
- **Test_AppendLine_AddsEntry**
  - Arrange: Create buffer with maxLines=10
  - Act: Append a line with "Test message"
  - Assert: Count == 1, entry text matches

- **Test_AppendLine_WithColor_StoresColor**
  - Arrange: Create buffer
  - Act: Append line with custom Vector4 color
  - Assert: Entry color matches input color

- **Test_AppendLine_WithLevel_UsesCorrectColor**
  - Arrange: Create buffer
  - Act: Append lines with each ConsoleOutputLevel
  - Assert: Each entry has appropriate color from ConsoleColors

##### Buffer Limits
- **Test_AppendLine_ExceedsMaxLines_TrimsOldest**
  - Arrange: Create buffer with maxLines=5
  - Act: Append 10 lines
  - Assert: Count == 5, oldest 5 entries removed, newest 5 retained

- **Test_AppendLine_MultipleOverLimit_MaintainsMaxLines**
  - Arrange: Buffer with maxLines=3
  - Act: Append 100 lines
  - Assert: Count == 3, contains only last 3 entries

##### Thread Safety
- **Test_AppendLine_ConcurrentAccess_ThreadSafe**
  - Arrange: Create buffer
  - Act: Spawn 10 threads, each appending 100 lines
  - Assert: Total count == min(maxLines, 1000), no exceptions

- **Test_ForEach_WhileConcurrentAppends_ThreadSafe**
  - Arrange: Buffer with data
  - Act: Read via ForEach while another thread appends
  - Assert: No exceptions, consistent data

##### Clear and Retrieval
- **Test_Clear_RemovesAllEntries**
  - Arrange: Buffer with 10 entries
  - Act: Call Clear()
  - Assert: Count == 0

- **Test_GetEntries_ReturnsCopy**
  - Arrange: Buffer with entries
  - Act: Get entries, modify list
  - Assert: Original buffer unchanged

- **Test_ForEach_IteratesAllEntries**
  - Arrange: Buffer with 5 entries
  - Act: ForEach with counter
  - Assert: Counter == 5, all entries processed

##### Edge Cases
- **Test_Constructor_InvalidMaxLines_ThrowsException**
  - Arrange: N/A
  - Act: new ConsoleBuffer(maxLines: 0)
  - Assert: ArgumentOutOfRangeException

- **Test_AppendLine_EmptyString_Stores**
  - Arrange: Buffer
  - Act: Append empty string
  - Assert: Entry added with empty text

- **Test_Timestamp_ReflectsAppendTime**
  - Arrange: Buffer
  - Act: Append line, record time
  - Assert: Entry timestamp within 100ms of current time

---

### 1.2 ConsoleHistory Tests (`ConsoleHistoryTests.cs`)

**Location**: `tests/Unit/Console/Features/ConsoleHistoryTests.cs`

#### Test Cases:

##### Adding Commands
- **Test_Add_ValidCommand_AddsToHistory**
  - Arrange: History with maxHistory=10
  - Act: Add("test command")
  - Assert: Count == 1, GetAll() contains command

- **Test_Add_DuplicateLastCommand_DoesNotAdd**
  - Arrange: History with one command
  - Act: Add same command twice
  - Assert: Count == 1

- **Test_Add_WhitespaceOnly_IgnoresCommand**
  - Arrange: History
  - Act: Add("   ")
  - Assert: Count == 0

- **Test_Add_ExceedsMaxHistory_TrimsOldest**
  - Arrange: History with maxHistory=5
  - Act: Add 10 commands
  - Assert: Count == 5, oldest 5 removed

##### Navigation
- **Test_NavigatePrevious_EmptyHistory_ReturnsNull**
  - Arrange: Empty history
  - Act: NavigatePrevious("")
  - Assert: Returns null

- **Test_NavigatePrevious_FirstCall_SavesCurrentInput**
  - Arrange: History with 3 commands
  - Act: NavigatePrevious("current input")
  - Assert: Returns last command, saved input stored

- **Test_NavigatePrevious_MultipleBack_ReturnsCommands**
  - Arrange: History ["cmd1", "cmd2", "cmd3"]
  - Act: NavigatePrevious 3 times
  - Assert: Returns cmd3, cmd2, cmd1 in order

- **Test_NavigatePrevious_AtBeginning_StaysAtFirst**
  - Arrange: History with 2 commands
  - Act: NavigatePrevious 10 times
  - Assert: Returns first command, doesn't wrap

- **Test_NavigateNext_NotNavigating_ReturnsSavedInput**
  - Arrange: History
  - Act: NavigateNext()
  - Assert: Returns empty string

- **Test_NavigateNext_AfterPrevious_ReturnsNextCommand**
  - Arrange: History ["cmd1", "cmd2", "cmd3"]
  - Act: NavigatePrevious twice, then NavigateNext
  - Assert: Returns cmd3

- **Test_NavigateNext_BeyondEnd_ReturnsSavedInput**
  - Arrange: History with 2 commands, navigated back
  - Act: NavigateNext beyond end
  - Assert: Returns original saved input, resets navigation

##### Reset Navigation
- **Test_ResetNavigation_ClearsState**
  - Arrange: History navigating
  - Act: ResetNavigation()
  - Assert: IsNavigating == false, saved input cleared

- **Test_Add_AutomaticallyResetsNavigation**
  - Arrange: History navigating
  - Act: Add new command
  - Assert: IsNavigating == false

##### Search
- **Test_Search_EmptyQuery_ReturnsAllReversed**
  - Arrange: History ["cmd1", "cmd2", "cmd3"]
  - Act: Search("")
  - Assert: Returns [cmd3, cmd2, cmd1]

- **Test_Search_MatchingCommands_ReturnsMatches**
  - Arrange: History ["help", "clear", "help -v"]
  - Act: Search("help")
  - Assert: Returns ["help -v", "help"] in reverse order

- **Test_Search_CaseInsensitive_FindsMatches**
  - Arrange: History ["HELP", "help"]
  - Act: Search("HeLp")
  - Assert: Returns both matches

- **Test_Search_NoMatches_ReturnsEmpty**
  - Arrange: History with commands
  - Act: Search("nonexistent")
  - Assert: Returns empty enumerable

##### Clear
- **Test_Clear_RemovesAllHistory**
  - Arrange: History with 10 commands
  - Act: Clear()
  - Assert: Count == 0, GetAll() empty

- **Test_Clear_ResetsNavigation**
  - Arrange: History navigating
  - Act: Clear()
  - Assert: IsNavigating == false

##### Edge Cases
- **Test_Constructor_InvalidMaxHistory_ThrowsException**
  - Arrange: N/A
  - Act: new ConsoleHistory(maxHistory: 0)
  - Assert: ArgumentOutOfRangeException

- **Test_GetAll_ReturnsReadOnlyList**
  - Arrange: History with commands
  - Act: Get list, try to modify
  - Assert: Modification doesn't affect history

---

### 1.3 ConsoleCommandRegistry Tests (`ConsoleCommandRegistryTests.cs`)

**Location**: `tests/Unit/Console/Commands/ConsoleCommandRegistryTests.cs`

#### Test Cases:

##### Registration
- **Test_RegisterCommand_ValidCommand_Registers**
  - Arrange: Registry, mock command
  - Act: RegisterCommand(mockCommand)
  - Assert: TryGetCommand returns true, command accessible

- **Test_RegisterCommand_Duplicate_Overwrites**
  - Arrange: Registry with command "test"
  - Act: Register another "test" command
  - Assert: Latest command registered, warning logged

- **Test_RegisterCommand_Null_ThrowsException**
  - Arrange: Registry
  - Act: RegisterCommand(null)
  - Assert: ArgumentNullException

- **Test_RegisterCommand_WithAliases_RegistersAliases**
  - Arrange: Command with aliases ["h", "?"]
  - Act: Register command
  - Assert: TryGetCommand works for all aliases

- **Test_RegisterCommand_DuplicateAlias_Overwrites**
  - Arrange: Two commands with same alias
  - Act: Register both
  - Assert: Alias points to second command, warning logged

##### Unregistration
- **Test_UnregisterCommand_ExistingCommand_Removes**
  - Arrange: Registry with "test" command
  - Act: UnregisterCommand("test")
  - Assert: Returns true, command not accessible

- **Test_UnregisterCommand_NonExistent_ReturnsFalse**
  - Arrange: Registry
  - Act: UnregisterCommand("nonexistent")
  - Assert: Returns false

- **Test_UnregisterCommand_RemovesAliases**
  - Arrange: Command with aliases registered
  - Act: Unregister command
  - Assert: Aliases no longer resolve

##### Command Lookup
- **Test_TryGetCommand_ByName_ReturnsCommand**
  - Arrange: Registry with command "help"
  - Act: TryGetCommand("help", out var cmd)
  - Assert: Returns true, cmd not null

- **Test_TryGetCommand_ByAlias_ReturnsCommand**
  - Arrange: Command "help" with alias "?"
  - Act: TryGetCommand("?", out var cmd)
  - Assert: Returns true, cmd is help command

- **Test_TryGetCommand_CaseInsensitive_FindsCommand**
  - Arrange: Command "Help"
  - Act: TryGetCommand("HELP", out var cmd)
  - Assert: Returns true, finds command

- **Test_TryGetCommand_NotFound_ReturnsFalse**
  - Arrange: Registry
  - Act: TryGetCommand("nonexistent", out var cmd)
  - Assert: Returns false, cmd is null

##### Categorization
- **Test_GetCommandsByCategory_GroupsCorrectly**
  - Arrange: Commands in "General" and "Debug" categories
  - Act: GetCommandsByCategory()
  - Assert: Dictionary with 2 keys, commands grouped correctly

- **Test_GetCommandsByCategory_SortedAlphabetically**
  - Arrange: Commands in multiple categories
  - Act: GetCommandsByCategory()
  - Assert: Categories and commands alphabetically sorted

##### Completion
- **Test_GetCompletions_EmptyPartial_ReturnsAllCommands**
  - Arrange: Registry with 5 commands
  - Act: GetCompletions("")
  - Assert: Returns all 5 command names

- **Test_GetCompletions_Prefix_ReturnsMatches**
  - Arrange: Commands ["help", "history", "clear"]
  - Act: GetCompletions("h")
  - Assert: Returns ["help", "history"]

- **Test_GetCompletions_NoMatches_ReturnsEmpty**
  - Arrange: Registry with commands
  - Act: GetCompletions("zzz")
  - Assert: Returns empty enumerable

- **Test_GetRichCompletions_ReturnsDescriptions**
  - Arrange: Commands with descriptions
  - Act: GetRichCompletions("h")
  - Assert: CompletionItems contain text and descriptions

- **Test_GetRichCompletions_IncludesAliases**
  - Arrange: Command with alias
  - Act: GetRichCompletions("?")
  - Assert: Returns alias with description pointing to main command

##### Auto-Discovery
- **Test_Constructor_AutoDiscover_FindsCommands**
  - Arrange: N/A
  - Act: new ConsoleCommandRegistry(autoDiscover: true)
  - Assert: Built-in commands registered (help, clear, etc.)

- **Test_Constructor_NoAutoDiscover_Empty**
  - Arrange: N/A
  - Act: new ConsoleCommandRegistry(autoDiscover: false)
  - Assert: Commands.Count == 0

- **Test_AutoDiscover_DisabledCommands_NotRegistered**
  - Arrange: Command with [ConsoleCommand(Enabled = false)]
  - Act: Auto-discover
  - Assert: Command not registered

---

### 1.4 Command Parsing Tests (`ConsoleServiceParsingTests.cs`)

**Location**: `tests/Unit/Console/Services/ConsoleServiceParsingTests.cs`

#### Test Cases:

##### Basic Parsing
- **Test_ParseCommandLine_SingleWord_ReturnsSinglePart**
  - Arrange: Input "help"
  - Act: Parse
  - Assert: Returns ["help"]

- **Test_ParseCommandLine_MultipleWords_SplitsOnSpace**
  - Arrange: Input "help command test"
  - Act: Parse
  - Assert: Returns ["help", "command", "test"]

- **Test_ParseCommandLine_ExtraSpaces_Ignored**
  - Arrange: Input "help   test"
  - Act: Parse
  - Assert: Returns ["help", "test"]

##### Quote Handling
- **Test_ParseCommandLine_QuotedString_SinglePart**
  - Arrange: Input `echo "hello world"`
  - Act: Parse
  - Assert: Returns ["echo", "hello world"]

- **Test_ParseCommandLine_NestedQuotes_HandlesCorrectly**
  - Arrange: Input `echo "say 'hello'"`
  - Act: Parse
  - Assert: Returns ["echo", "say 'hello'"]

- **Test_ParseCommandLine_UnmatchedQuote_ParsesUntilEnd**
  - Arrange: Input `echo "hello`
  - Act: Parse
  - Assert: Returns ["echo", "hello"]

##### Escape Sequences
- **Test_ParseCommandLine_EscapedSpace_PreservesSpace**
  - Arrange: Input `echo hello\ world`
  - Act: Parse
  - Assert: Returns ["echo", "hello world"]

- **Test_ParseCommandLine_EscapedQuote_PreservesQuote**
  - Arrange: Input `echo \"hello\"`
  - Act: Parse
  - Assert: Returns ["echo", "\"hello\""]

##### Edge Cases
- **Test_ParseCommandLine_Empty_ReturnsEmpty**
  - Arrange: Input ""
  - Act: Parse
  - Assert: Returns empty array

- **Test_ParseCommandLine_OnlySpaces_ReturnsEmpty**
  - Arrange: Input "     "
  - Act: Parse
  - Assert: Returns empty array

---

## 2. Integration Tests

### 2.1 ConsoleService Integration Tests (`ConsoleServiceIntegrationTests.cs`)

**Location**: `tests/Integration/Console/ConsoleServiceIntegrationTests.cs`

#### Test Cases:

##### Command Execution Flow
- **Test_ExecuteCommand_ValidCommand_Executes**
  - Arrange: Service with mock command registry
  - Act: ExecuteCommandAsync("help")
  - Assert: Command executed, output in buffer, history updated

- **Test_ExecuteCommand_InvalidCommand_ShowsError**
  - Arrange: Service
  - Act: ExecuteCommandAsync("invalid")
  - Assert: Error in buffer, "Unknown command" message

- **Test_ExecuteCommand_EmptyString_DoesNothing**
  - Arrange: Service
  - Act: ExecuteCommandAsync("")
  - Assert: No history entry, no output

- **Test_ExecuteCommand_ThrowsException_CatchesAndLogsError**
  - Arrange: Mock command that throws
  - Act: ExecuteCommandAsync("throwing")
  - Assert: Error in buffer, exception logged

##### Event Integration
- **Test_ExecuteCommand_FiresSubmittedEvent**
  - Arrange: Service, event listener
  - Act: ExecuteCommandAsync("test")
  - Assert: CommandSubmittedEvent fired with correct text

- **Test_ExecuteCommand_FiresExecutedEvent**
  - Arrange: Service, event listener
  - Act: ExecuteCommandAsync("help")
  - Assert: CommandExecutedEvent fired with Success=true

- **Test_ExecuteCommand_Failed_FiresEventWithError**
  - Arrange: Service, unknown command
  - Act: ExecuteCommandAsync("invalid")
  - Assert: CommandExecutedEvent fired with Success=false

- **Test_Toggle_FiresToggledEvent**
  - Arrange: Service, event listener
  - Act: Toggle()
  - Assert: ConsoleToggledEvent fired with IsVisible state

- **Test_WriteLine_FiresOutputEvent**
  - Arrange: Service, event listener
  - Act: WriteLine("test")
  - Assert: ConsoleOutputEvent fired with text and level

##### History Integration
- **Test_ExecuteCommand_AddsToHistory**
  - Arrange: Service
  - Act: ExecuteCommandAsync("test")
  - Assert: History.Count == 1, contains "test"

- **Test_ExecuteCommand_ResetsHistoryNavigation**
  - Arrange: Service with navigation active
  - Act: ExecuteCommandAsync("new")
  - Assert: History.IsNavigating == false

##### Completion Integration
- **Test_GetCompletions_WithPartial_ReturnsFromRegistry**
  - Arrange: Service with registered commands
  - Act: GetCompletions("hel")
  - Assert: Returns ["help"]

- **Test_GetCompletions_MultiWord_UsesCommandCompletions**
  - Arrange: Service with command that provides arg completions
  - Act: GetCompletions("command arg")
  - Assert: Returns command-specific completions

- **Test_GetRichCompletions_ReturnsDescriptions**
  - Arrange: Service
  - Act: GetRichCompletions("he")
  - Assert: Returns CompletionItems with descriptions

##### Visibility Management
- **Test_Show_SetsVisible**
  - Arrange: Hidden service
  - Act: Show()
  - Assert: IsVisible == true

- **Test_Hide_ClearsVisible**
  - Arrange: Visible service
  - Act: Hide()
  - Assert: IsVisible == false

- **Test_Toggle_TogglesState**
  - Arrange: Service (visible)
  - Act: Toggle(), then Toggle()
  - Assert: First call hides, second call shows

##### Output Management
- **Test_WriteLine_AppendsToBuffer**
  - Arrange: Service
  - Act: WriteLine("test", ConsoleOutputLevel.Normal)
  - Assert: Buffer contains entry with "test"

- **Test_WriteSuccess_UsesSuccessLevel**
  - Arrange: Service
  - Act: WriteSuccess("ok")
  - Assert: Buffer entry has Success level

- **Test_Clear_ClearsBuffer**
  - Arrange: Service with output
  - Act: Clear()
  - Assert: OutputBuffer.Count == 0

---

### 2.2 ECS Command Integration Tests (`ECSCommandIntegrationTests.cs`)

**Location**: `tests/Integration/Console/ECSCommandIntegrationTests.cs`

#### Test Cases:

##### Entity Commands
- **Test_EntityListCommand_ListsEntities**
  - Arrange: World with 5 entities
  - Act: Execute "entity list"
  - Assert: Output shows 5 entities

- **Test_EntityInfoCommand_ShowsComponents**
  - Arrange: Entity with Transform, Sprite components
  - Act: Execute "entity info <id>"
  - Assert: Output lists both components

- **Test_EntityDestroyCommand_RemovesEntity**
  - Arrange: World with entity
  - Act: Execute "entity destroy <id>"
  - Assert: Entity no longer exists, success message

##### System Commands
- **Test_SystemListCommand_ShowsAllSystems**
  - Arrange: World with 3 systems
  - Act: Execute "system list"
  - Assert: Output shows all 3 systems

- **Test_SystemEnableCommand_EnablesSystem**
  - Arrange: Disabled system
  - Act: Execute "system enable <name>"
  - Assert: System enabled, confirmation message

##### Component Commands
- **Test_ComponentAddCommand_AddsComponent**
  - Arrange: Entity without Transform
  - Act: Execute "component add <id> Transform"
  - Assert: Entity has Transform, success message

- **Test_ComponentRemoveCommand_RemovesComponent**
  - Arrange: Entity with Transform
  - Act: Execute "component remove <id> Transform"
  - Assert: Component removed, success message

##### Completion Integration
- **Test_EntityCommand_CompletesEntityIDs**
  - Arrange: Entities with IDs [1, 2, 3]
  - Act: GetCompletions("entity info ")
  - Assert: Returns ["1", "2", "3"]

- **Test_ComponentCommand_CompletesComponentTypes**
  - Arrange: Component registry
  - Act: GetCompletions("component add 1 ")
  - Assert: Returns component type names

---

### 2.3 Command Registry Integration (`CommandRegistryIntegrationTests.cs`)

**Location**: `tests/Integration/Console/CommandRegistryIntegrationTests.cs`

#### Test Cases:

##### Full Lifecycle
- **Test_RegisterExecuteUnregister_FullFlow**
  - Arrange: Registry, custom command
  - Act: Register, execute via service, unregister
  - Assert: All operations succeed

- **Test_MultipleCommands_DifferentCategories_Organized**
  - Arrange: Commands in 3 categories
  - Act: GetCommandsByCategory
  - Assert: Correctly organized by category

##### Built-in Commands
- **Test_HelpCommand_ListsAllCommands**
  - Arrange: Service with built-ins
  - Act: Execute "help"
  - Assert: Output lists all registered commands

- **Test_ClearCommand_ClearsOutput**
  - Arrange: Service with output
  - Act: Execute "clear"
  - Assert: Buffer emptied

- **Test_EchoCommand_PrintsArguments**
  - Arrange: Service
  - Act: Execute "echo hello world"
  - Assert: Output contains "hello world"

- **Test_HistoryCommand_ShowsPastCommands**
  - Arrange: Service with history
  - Act: Execute "history"
  - Assert: Output shows previous commands

---

## 3. UI Tests

### 3.1 ConsolePanel Input Tests (`ConsolePanelInputTests.cs`)

**Location**: `tests/UI/Console/ConsolePanelInputTests.cs`

#### Test Cases:

##### Basic Input
- **Test_TypeText_UpdatesInputBuffer**
  - Arrange: Panel active
  - Act: Type "help"
  - Assert: Input buffer contains "help"

- **Test_PressEnter_SubmitsCommand**
  - Arrange: Panel with "help" typed
  - Act: Press Enter
  - Assert: Command executed, input cleared

- **Test_PressEnter_EmptyInput_DoesNothing**
  - Arrange: Panel with empty input
  - Act: Press Enter
  - Assert: No command executed

- **Test_MaxInputLength_EnforcesLimit**
  - Arrange: Panel
  - Act: Type 5000 characters
  - Assert: Input truncated at MaxInputLength (4096)

##### Focus Management
- **Test_OpenConsole_FocusesInput**
  - Arrange: Console hidden
  - Act: Open console
  - Assert: Input field has focus

- **Test_SubmitCommand_RetainsFocus**
  - Arrange: Panel with command
  - Act: Submit command
  - Assert: Input field still focused

- **Test_ClickOutput_DoesNotLoseFocus**
  - Arrange: Panel active
  - Act: Click output area
  - Assert: Input can still receive keys

##### Input Editing
- **Test_Backspace_DeletesCharacter**
  - Arrange: Input "help"
  - Act: Press Backspace
  - Assert: Input is "hel"

- **Test_Delete_RemovesCharacterAfterCursor**
  - Arrange: Input "help", cursor at 'e'
  - Act: Press Delete
  - Assert: Input is "hlp"

- **Test_LeftRightArrows_MoveCursor**
  - Arrange: Input "help"
  - Act: Press Left twice, type 'X'
  - Assert: Input is "heXlp"

- **Test_Home_MovesCursorToStart**
  - Arrange: Input "help", cursor at end
  - Act: Press Home
  - Assert: Cursor at position 0

- **Test_End_MovesCursorToEnd**
  - Arrange: Input "help", cursor at start
  - Act: Press End
  - Assert: Cursor at end of input

---

### 3.2 Completion Popup Tests (`CompletionPopupTests.cs`)

**Location**: `tests/UI/Console/CompletionPopupTests.cs`

#### Test Cases:

##### Popup Display
- **Test_PressTab_ShowsCompletions**
  - Arrange: Input "hel"
  - Act: Press Tab
  - Assert: Completion popup visible

- **Test_NoMatches_DoesNotShowPopup**
  - Arrange: Input "zzz"
  - Act: Press Tab
  - Assert: Popup not visible

- **Test_SingleMatch_AppliesImmediately**
  - Arrange: Input "help" (only match)
  - Act: Press Tab
  - Assert: Completion applied, popup not shown

- **Test_PopupPosition_AboveInput**
  - Arrange: Input with completions
  - Act: Show popup
  - Assert: Popup positioned above input area

- **Test_PopupWidth_MatchesWindow**
  - Arrange: Console window 800px wide
  - Act: Show popup
  - Assert: Popup width ≈ window width - margins

##### Popup Navigation
- **Test_UpArrow_SelectsPrevious**
  - Arrange: Popup with 3 items, #2 selected
  - Act: Press Up
  - Assert: #1 selected

- **Test_DownArrow_SelectsNext**
  - Arrange: Popup with 3 items, #1 selected
  - Act: Press Down
  - Assert: #2 selected

- **Test_UpArrow_AtTop_WrapsToBottom**
  - Arrange: Popup with 5 items, #1 selected
  - Act: Press Up
  - Assert: #5 selected

- **Test_DownArrow_AtBottom_WrapsToTop**
  - Arrange: Popup with 5 items, #5 selected
  - Act: Press Down
  - Assert: #1 selected

- **Test_Tab_CyclesNext**
  - Arrange: Popup with 3 items
  - Act: Press Tab twice
  - Assert: Cycles through #1, #2, #3

- **Test_ScrollIntoView_LargeList**
  - Arrange: Popup with 20 items, #1 selected
  - Act: Press Down 15 times
  - Assert: Selected item visible in scrolled area

##### Popup Interaction
- **Test_ClickItem_AppliesCompletion**
  - Arrange: Popup visible
  - Act: Click second item
  - Assert: Completion applied, popup closed

- **Test_EnterKey_AppliesSelected**
  - Arrange: Popup with item selected
  - Act: Press Enter
  - Assert: Completion applied, popup closed

- **Test_EscapeKey_ClosesPopup**
  - Arrange: Popup visible
  - Act: Press Escape
  - Assert: Popup closed, input unchanged

- **Test_TypeSpace_ClosesPopup**
  - Arrange: Popup visible
  - Act: Type space character
  - Assert: Popup closed

- **Test_TypeSemicolon_ClosesPopup**
  - Arrange: Popup visible
  - Act: Type semicolon
  - Assert: Popup closed

##### Completion Application
- **Test_ApplyCompletion_CommandName_AddsSpace**
  - Arrange: Input "hel", completion "help"
  - Act: Apply completion
  - Assert: Input becomes "help "

- **Test_ApplyCompletion_Argument_AddsSpace**
  - Arrange: Input "cmd ar", completion "argName"
  - Act: Apply completion
  - Assert: Input becomes "cmd argName "

- **Test_ApplyCompletion_SetsCursorToEnd**
  - Arrange: Input with completion
  - Act: Apply completion
  - Assert: Cursor at end of input

##### Content Updates
- **Test_TypeCharacter_UpdatesCompletions**
  - Arrange: Popup with "help", "history"
  - Act: Type 'i'
  - Assert: Only "history" remains in list

- **Test_Backspace_UpdatesCompletions**
  - Arrange: Input "help", popup with 1 item
  - Act: Backspace to "hel"
  - Assert: Multiple items appear

- **Test_NoMatches_HidesPopup**
  - Arrange: Popup visible
  - Act: Type until no matches
  - Assert: Popup hidden

##### Display Formatting
- **Test_CompletionItem_ShowsCategory**
  - Arrange: Popup with command items
  - Act: Render
  - Assert: [command] badge displayed

- **Test_CompletionItem_ShowsDescription**
  - Arrange: Popup with item with description
  - Act: Render
  - Assert: Description displayed on second line

- **Test_SelectedItem_HighlightedBlue**
  - Arrange: Popup with item selected
  - Act: Render
  - Assert: Item has blue background (Info color)

- **Test_MaxVisibleItems_ShowsScrollbar**
  - Arrange: Popup with 20 items
  - Act: Render
  - Assert: Scrollbar visible, max 8 items shown

---

### 3.3 History Navigation Tests (`HistoryNavigationUITests.cs`)

**Location**: `tests/UI/Console/HistoryNavigationUITests.cs`

#### Test Cases:

##### Up Arrow Navigation
- **Test_UpArrow_EmptyHistory_DoesNothing**
  - Arrange: Panel with no history
  - Act: Press Up
  - Assert: Input remains empty

- **Test_UpArrow_WithHistory_ShowsPrevious**
  - Arrange: History ["cmd1", "cmd2", "cmd3"]
  - Act: Press Up
  - Assert: Input shows "cmd3"

- **Test_UpArrow_SavesCurrentInput**
  - Arrange: Input "partial", history ["cmd"]
  - Act: Press Up
  - Assert: "partial" saved for later

- **Test_UpArrow_Multiple_NavigatesBackward**
  - Arrange: History ["cmd1", "cmd2", "cmd3"]
  - Act: Press Up 3 times
  - Assert: Shows cmd3, then cmd2, then cmd1

- **Test_UpArrow_AtOldest_StaysAtFirst**
  - Arrange: History ["cmd1", "cmd2"]
  - Act: Press Up 10 times
  - Assert: Input shows "cmd1"

##### Down Arrow Navigation
- **Test_DownArrow_NotNavigating_DoesNothing**
  - Arrange: Panel not in history navigation
  - Act: Press Down
  - Assert: Input unchanged

- **Test_DownArrow_AfterUp_ShowsNewer**
  - Arrange: History ["cmd1", "cmd2"], after Up twice
  - Act: Press Down
  - Assert: Input shows "cmd2"

- **Test_DownArrow_AtNewest_RestoresSavedInput**
  - Arrange: History navigation, at newest
  - Act: Press Down
  - Assert: Original input restored

- **Test_DownArrow_ResetsNavigation**
  - Arrange: History navigation active
  - Act: Press Down to end
  - Assert: Navigation state reset

##### Integration with Completions
- **Test_UpArrow_NoPopup_NavigatesHistory**
  - Arrange: History with commands, no popup
  - Act: Press Up
  - Assert: History navigation works

- **Test_UpArrow_PopupVisible_NavigatesCompletion**
  - Arrange: Popup visible with items
  - Act: Press Up
  - Assert: Selects previous completion item

- **Test_DownArrow_PopupVisible_NavigatesCompletion**
  - Arrange: Popup visible
  - Act: Press Down
  - Assert: Selects next completion item

- **Test_ClosePopup_RestoresHistoryNavigation**
  - Arrange: Popup visible
  - Act: Escape, then Up
  - Assert: History navigation works

##### Combined Navigation
- **Test_UpDown_RoundTrip**
  - Arrange: History ["cmd1", "cmd2"]
  - Act: Up, Up, Down, Down
  - Assert: Returns to original input

- **Test_TypeAfterHistory_ResetsNavigation**
  - Arrange: History navigation active
  - Act: Type character
  - Assert: Navigation reset, new input started

---

### 3.4 Output Display Tests (`OutputDisplayTests.cs`)

**Location**: `tests/UI/Console/OutputDisplayTests.cs`

#### Test Cases:

##### Output Rendering
- **Test_WriteLine_DisplaysText**
  - Arrange: Panel
  - Act: WriteLine("test message")
  - Assert: "test message" visible in output area

- **Test_WriteLine_WithColor_UsesColor**
  - Arrange: Panel
  - Act: WriteLine with Error level
  - Assert: Text displayed in error color (red)

- **Test_MultipleLines_DisplaysAll**
  - Arrange: Panel
  - Act: WriteLine 10 times
  - Assert: All 10 lines visible

##### Auto-scrolling
- **Test_NewOutput_ScrollsToBottom**
  - Arrange: Panel with output, scrolled to top
  - Act: WriteLine new message
  - Assert: Auto-scrolls to show new message

- **Test_UserScrolledUp_DoesNotAutoScroll**
  - Arrange: Panel with output
  - Act: Scroll up, WriteLine
  - Assert: Stays at current scroll position

- **Test_ScrollToBottom_EnablesAutoScroll**
  - Arrange: Panel scrolled up
  - Act: Manually scroll to bottom
  - Assert: Auto-scroll re-enabled

##### Output Limits
- **Test_ExceedsMaxLines_TrimsOldest**
  - Arrange: Buffer with maxLines=100
  - Act: WriteLine 200 times
  - Assert: Only last 100 lines visible

##### Color Coding
- **Test_NormalOutput_DefaultColor**
  - Arrange: Panel
  - Act: WriteLine("text", Normal)
  - Assert: Text in default color

- **Test_SuccessOutput_GreenColor**
  - Arrange: Panel
  - Act: WriteSuccess("ok")
  - Assert: Text in green/success color

- **Test_WarningOutput_YellowColor**
  - Arrange: Panel
  - Act: WriteWarning("warn")
  - Assert: Text in yellow/warning color

- **Test_ErrorOutput_RedColor**
  - Arrange: Panel
  - Act: WriteError("error")
  - Assert: Text in red/error color

- **Test_SystemOutput_GrayColor**
  - Arrange: Panel
  - Act: WriteSystem("info")
  - Assert: Text in gray/system color

##### Command Echo
- **Test_SubmitCommand_EchoesInput**
  - Arrange: Panel
  - Act: Submit "help"
  - Assert: "> help" displayed in output

---

### 3.5 Keyboard Shortcuts Tests (`KeyboardShortcutsTests.cs`)

**Location**: `tests/UI/Console/KeyboardShortcutsTests.cs`

#### Test Cases:

##### Console Toggle
- **Test_TildeKey_TogglesConsole**
  - Arrange: Console hidden
  - Act: Press ~ key
  - Assert: Console becomes visible

- **Test_TildeKey_WhileVisible_Hides**
  - Arrange: Console visible
  - Act: Press ~ key
  - Assert: Console hidden

##### Input Shortcuts
- **Test_CtrlC_ClearsInput**
  - Arrange: Input "test"
  - Act: Press Ctrl+C
  - Assert: Input cleared

- **Test_CtrlL_ClearsOutput**
  - Arrange: Output with text
  - Act: Press Ctrl+L
  - Assert: Output buffer cleared

- **Test_CtrlA_SelectsAll**
  - Arrange: Input "test"
  - Act: Press Ctrl+A
  - Assert: All text selected

- **Test_CtrlU_ClearsToStart**
  - Arrange: Input "hello", cursor at 'o'
  - Act: Press Ctrl+U
  - Assert: Input becomes "o"

##### Navigation Shortcuts
- **Test_CtrlP_PreviousHistory**
  - Arrange: History with commands
  - Act: Press Ctrl+P
  - Assert: Shows previous command (same as Up)

- **Test_CtrlN_NextHistory**
  - Arrange: History navigation active
  - Act: Press Ctrl+N
  - Assert: Shows next command (same as Down)

---

## 4. Performance Tests

### 4.1 Buffer Performance (`BufferPerformanceTests.cs`)

**Location**: `tests/Performance/Console/BufferPerformanceTests.cs`

#### Test Cases:

- **Test_AppendLine_1000Lines_UnderThreshold**
  - Measure: Time to append 1000 lines
  - Assert: Completes in <50ms

- **Test_ConcurrentAppends_Throughput**
  - Measure: Lines/second with 10 threads
  - Assert: >10,000 lines/second

- **Test_ForEach_LargeBuffer_Performance**
  - Measure: Iteration time for 10,000 entries
  - Assert: <100ms

### 4.2 Completion Performance (`CompletionPerformanceTests.cs`)

**Location**: `tests/Performance/Console/CompletionPerformanceTests.cs`

#### Test Cases:

- **Test_GetCompletions_100Commands_Fast**
  - Measure: Completion lookup time
  - Assert: <10ms for full search

- **Test_GetRichCompletions_DescriptionRetrieval**
  - Measure: Time to get rich completions
  - Assert: <20ms for 100 commands

### 4.3 Rendering Performance (`RenderingPerformanceTests.cs`)

**Location**: `tests/Performance/Console/RenderingPerformanceTests.cs`

#### Test Cases:

- **Test_RenderOutput_1000Lines_60FPS**
  - Measure: Frame time with 1000 visible lines
  - Assert: <16ms (60 FPS)

- **Test_CompletionPopup_RenderTime**
  - Measure: Popup rendering time
  - Assert: <5ms

---

## 5. Edge Cases & Stress Tests

### 5.1 Edge Cases (`EdgeCaseTests.cs`)

**Location**: `tests/EdgeCases/Console/EdgeCaseTests.cs`

#### Test Cases:

- **Test_VeryLongCommand_HandlesGracefully**
  - Input: 10,000 character command
  - Assert: Truncated appropriately, no crash

- **Test_UnicodeInput_SupportsCorrectly**
  - Input: Unicode characters (emoji, CJK)
  - Assert: Displays correctly

- **Test_RapidCommandSubmission_QueuesProperly**
  - Act: Submit 100 commands rapidly
  - Assert: All executed in order

- **Test_CompletionMidWord_CorrectPosition**
  - Input: "help", cursor at 'e'
  - Assert: Completion works correctly

### 5.2 Stress Tests (`StressTests.cs`)

**Location**: `tests/Stress/Console/StressTests.cs`

#### Test Cases:

- **Test_10000Commands_NoMemoryLeak**
  - Act: Execute 10,000 commands
  - Assert: Memory usage stable

- **Test_ConcurrentCommandExecution_ThreadSafe**
  - Act: 100 threads execute commands simultaneously
  - Assert: No exceptions, correct output

---

## 6. Test Utilities & Mocks

### 6.1 Mock Objects

**Location**: `tests/Mocks/Console/`

#### Mock Classes:

- **MockConsoleCommand**: Configurable test command
- **MockConsoleService**: Service with interceptable methods
- **MockCommandRegistry**: Registry with controlled behavior
- **MockEventBus**: Captures events for verification

### 6.2 Test Helpers

**Location**: `tests/Helpers/Console/`

#### Helper Classes:

- **ConsoleTestFixture**: Sets up common test environment
- **ImGuiTestContext**: Simulates ImGui input/rendering
- **CommandBuilder**: Fluent API for creating test commands
- **AssertionHelpers**: Custom assertions for console tests

---

## 7. Test Execution Strategy

### Test Organization
```
tests/
├── Unit/
│   ├── Console/
│   │   ├── Features/
│   │   ├── Commands/
│   │   └── Services/
├── Integration/
│   └── Console/
├── UI/
│   └── Console/
├── Performance/
│   └── Console/
├── EdgeCases/
│   └── Console/
├── Stress/
│   └── Console/
├── Mocks/
│   └── Console/
└── Helpers/
    └── Console/
```

### Test Categories
- `[Category("Unit")]`: Fast, isolated tests
- `[Category("Integration")]`: Multi-component tests
- `[Category("UI")]`: ImGui interaction tests
- `[Category("Performance")]`: Benchmarking tests
- `[Category("Stress")]`: Load/stress tests

### CI/CD Integration
- **PR Checks**: Unit + Integration tests
- **Nightly**: All tests including Performance
- **Weekly**: Stress tests

---

## 8. Coverage Goals

### Target Coverage
- **Statements**: >90%
- **Branches**: >85%
- **Functions**: >95%

### Critical Coverage Areas (100%)
- Command parsing logic
- Completion algorithms
- History navigation
- Event firing
- Thread-safe operations

---

## 9. Testing Tools & Frameworks

### Frameworks
- **xUnit**: Primary test framework
- **Moq**: Mocking framework
- **FluentAssertions**: Readable assertions
- **BenchmarkDotNet**: Performance benchmarks

### Test Helpers
- **AutoFixture**: Test data generation
- **Bogus**: Fake data generation

---

## 10. Maintenance & Best Practices

### Test Maintenance
- Update tests when features change
- Review test failures in CI regularly
- Refactor tests to reduce duplication
- Keep mocks synchronized with interfaces

### Best Practices
- **AAA Pattern**: Arrange-Act-Assert
- **One Assertion**: Test one behavior per test
- **Descriptive Names**: Test names explain scenario
- **Fast Tests**: Unit tests complete in milliseconds
- **Isolated Tests**: No dependencies between tests
- **Deterministic**: Same input = same output always

---

## 11. Known Issues & Test Gaps

### Current Gaps
1. ImGui input simulation complexity
2. Multi-threaded UI testing challenges
3. Performance benchmark baselines TBD

### Future Test Additions
1. Accessibility testing
2. Internationalization tests
3. Platform-specific keyboard handling
4. Memory profiling tests

---

## Appendix: Test Execution Commands

### Run All Tests
```bash
dotnet test
```

### Run by Category
```bash
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
dotnet test --filter "Category=UI"
```

### Run Specific Test Class
```bash
dotnet test --filter "FullyQualifiedName~ConsoleBufferTests"
```

### Coverage Report
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura
```

---

**Document Version**: 1.0
**Last Updated**: 2026-01-05
**Author**: QA Specialist Agent
**Status**: Ready for Implementation
