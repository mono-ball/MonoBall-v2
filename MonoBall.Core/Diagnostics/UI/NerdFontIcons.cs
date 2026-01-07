namespace MonoBall.Core.Diagnostics.UI;

/// <summary>
///     Nerd Font icon constants for the debug console UI.
///     These icons require a Nerd Font (e.g., 0xProto Nerd Font) to render properly.
///     Reference: https://www.nerdfonts.com/cheat-sheet
///     Codepoints: https://github.com/ryanoasis/nerd-fonts/wiki/Glyph-Sets-and-Code-Points
/// </summary>
public static class NerdFontIcons
{
    // ═══════════════════════════════════════════════════════════════
    // TREE/HIERARCHY INDICATORS (Codicons: EA60-EBE7)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Expanded indicator (chevron down): nf-cod-chevron_down</summary>
    public const string Expanded = "\uEAB4";

    /// <summary>Collapsed indicator (chevron right): nf-cod-chevron_right</summary>
    public const string Collapsed = "\uEAB6";

    /// <summary>Expanded indicator alternative (folder open): nf-fa-folder_open</summary>
    public const string FolderOpen = "\uF07C";

    /// <summary>Collapsed indicator alternative (folder): nf-fa-folder</summary>
    public const string FolderClosed = "\uF07B";

    /// <summary>Tree branch: nf-cod-dash</summary>
    public const string TreeDash = "\uEAD4";

    // ═══════════════════════════════════════════════════════════════
    // SELECTION & CURSOR (Codicons + Powerline)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Selection pointer (triangle right): nf-cod-triangle_right</summary>
    public const string SelectionPointer = "\uEA9C";

    /// <summary>Cursor/caret: block cursor</summary>
    public const string Cursor = "▌";

    /// <summary>Prompt chevron: nf-pl-left_hard_divider</summary>
    public const string PromptChevron = "\uE0B0";

    /// <summary>Prompt arrow: nf-cod-arrow_right</summary>
    public const string PromptArrow = "\uEA9C";

    /// <summary>Command prompt lambda: λ</summary>
    public const string PromptLambda = "λ";

    /// <summary>Prompt symbol: nf-oct-chevron_right</summary>
    public const string PromptSymbol = "\uF460";

    // ═══════════════════════════════════════════════════════════════
    // STATUS INDICATORS (Font Awesome + Codicons)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Success/OK checkmark: nf-fa-check</summary>
    public const string Success = "\uF00C";

    /// <summary>Success circle: nf-fa-check_circle</summary>
    public const string SuccessCircle = "\uF058";

    /// <summary>Error/fail X: nf-fa-times</summary>
    public const string Error = "\uF00D";

    /// <summary>Error circle: nf-fa-times_circle</summary>
    public const string ErrorCircle = "\uF057";

    /// <summary>Warning triangle: nf-fa-warning (exclamation_triangle)</summary>
    public const string Warning = "\uF071";

    /// <summary>Alert circle: nf-fa-exclamation_circle</summary>
    public const string AlertCircle = "\uF06A";

    /// <summary>Info circle: nf-fa-info_circle</summary>
    public const string Info = "\uF05A";

    /// <summary>Question circle: nf-fa-question_circle</summary>
    public const string Question = "\uF059";

    /// <summary>Pin/pinned: nf-oct-pin</summary>
    public const string Pinned = "\uF435";

    /// <summary>Thumbtack: nf-fa-thumb_tack</summary>
    public const string Thumbtack = "\uF08D";

    /// <summary>Star/favorite: nf-fa-star</summary>
    public const string Star = "\uF005";

    /// <summary>Star outline: nf-fa-star_o</summary>
    public const string StarOutline = "\uF006";

    /// <summary>Flame/fire/hot: nf-fa-fire</summary>
    public const string Flame = "\uF06D";

    /// <summary>Lightning bolt: nf-oct-zap</summary>
    public const string Bolt = "\uF0E7";

    // ═══════════════════════════════════════════════════════════════
    // ARROWS & NAVIGATION (Font Awesome + Codicons)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Arrow up: nf-fa-arrow_up</summary>
    public const string ArrowUp = "\uF062";

    /// <summary>Arrow down: nf-fa-arrow_down</summary>
    public const string ArrowDown = "\uF063";

    /// <summary>Arrow left: nf-fa-arrow_left</summary>
    public const string ArrowLeft = "\uF060";

    /// <summary>Arrow right: nf-fa-arrow_right</summary>
    public const string ArrowRight = "\uF061";

    /// <summary>Caret up: nf-fa-caret_up</summary>
    public const string CaretUp = "\uF0D8";

    /// <summary>Caret down: nf-fa-caret_down</summary>
    public const string CaretDown = "\uF0D7";

    /// <summary>Caret left: nf-fa-caret_left</summary>
    public const string CaretLeft = "\uF0D9";

    /// <summary>Caret right: nf-fa-caret_right</summary>
    public const string CaretRight = "\uF0DA";

    /// <summary>Angle up: nf-fa-angle_up</summary>
    public const string AngleUp = "\uF106";

    /// <summary>Angle down: nf-fa-angle_down</summary>
    public const string AngleDown = "\uF107";

    /// <summary>Double angle up: nf-fa-angle_double_up</summary>
    public const string ScrollUp = "\uF102";

    /// <summary>Double angle down: nf-fa-angle_double_down</summary>
    public const string ScrollDown = "\uF103";

    /// <summary>Dropdown/menu arrow (same as caret down)</summary>
    public const string DropdownArrow = "\uF0D7";

    // ═══════════════════════════════════════════════════════════════
    // ACTIONS & COMMANDS (Font Awesome + Codicons)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Play/run: nf-fa-play</summary>
    public const string Play = "\uF04B";

    /// <summary>Pause: nf-fa-pause</summary>
    public const string Pause = "\uF04C";

    /// <summary>Stop: nf-fa-stop</summary>
    public const string Stop = "\uF04D";

    /// <summary>Step forward: nf-fa-step_forward</summary>
    public const string StepForward = "\uF051";

    /// <summary>Refresh/reload: nf-fa-refresh</summary>
    public const string Refresh = "\uF021";

    /// <summary>Sync: nf-fa-sync (arrows circle)</summary>
    public const string Sync = "\uF021";

    /// <summary>Spinner/loading: nf-fa-spinner</summary>
    public const string Spinner = "\uF110";

    /// <summary>Search/magnifying glass: nf-fa-search</summary>
    public const string Search = "\uF002";

    /// <summary>Settings/gear/cog: nf-fa-cog</summary>
    public const string Settings = "\uF013";

    /// <summary>Gears: nf-fa-cogs</summary>
    public const string Gears = "\uF085";

    /// <summary>Close/X: nf-fa-close (same as times)</summary>
    public const string Close = "\uF00D";

    /// <summary>Plus: nf-fa-plus</summary>
    public const string Add = "\uF067";

    /// <summary>Minus: nf-fa-minus</summary>
    public const string Remove = "\uF068";

    /// <summary>Edit/pencil: nf-fa-pencil</summary>
    public const string Edit = "\uF040";

    /// <summary>Copy/clone: nf-fa-clone</summary>
    public const string Copy = "\uF24D";

    /// <summary>Clipboard/paste: nf-fa-clipboard</summary>
    public const string Paste = "\uF0EA";

    /// <summary>Trash/delete: nf-fa-trash</summary>
    public const string Trash = "\uF1F8";

    /// <summary>Save/floppy: nf-fa-floppy_o</summary>
    public const string Save = "\uF0C7";

    /// <summary>Undo: nf-fa-undo</summary>
    public const string Undo = "\uF0E2";

    /// <summary>Redo: nf-fa-repeat</summary>
    public const string Redo = "\uF01E";

    // ═══════════════════════════════════════════════════════════════
    // DATA & OBJECTS (Codicons + Seti-UI + Custom)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Variable/symbol: nf-cod-symbol_variable</summary>
    public const string Variable = "\uEA88";

    /// <summary>Array/list: nf-cod-symbol_array</summary>
    public const string Array = "\uEA8A";

    /// <summary>Object/class: nf-cod-symbol_class</summary>
    public const string Object = "\uEB5B";

    /// <summary>Function/method: nf-cod-symbol_method</summary>
    public const string Function = "\uEA8C";

    /// <summary>Property: nf-cod-symbol_property</summary>
    public const string Property = "\uEB65";

    /// <summary>Field: nf-cod-symbol_field</summary>
    public const string Field = "\uEB5F";

    /// <summary>Keyword: nf-cod-symbol_keyword</summary>
    public const string Keyword = "\uEB62";

    /// <summary>Constant: nf-cod-symbol_constant</summary>
    public const string Constant = "\uEB5D";

    /// <summary>String type: nf-cod-symbol_string</summary>
    public const string StringType = "\uEB8D";

    /// <summary>Number type: nf-cod-symbol_numeric</summary>
    public const string NumberType = "\uEB64";

    /// <summary>Boolean type: nf-cod-symbol_boolean</summary>
    public const string BooleanType = "\uEB5C";

    /// <summary>Null/empty: nf-cod-symbol_null</summary>
    public const string Null = "\uEB63";

    /// <summary>Enum: nf-cod-symbol_enum</summary>
    public const string Enum = "\uEA95";

    /// <summary>Interface: nf-cod-symbol_interface</summary>
    public const string Interface = "\uEB61";

    /// <summary>Namespace: nf-cod-symbol_namespace</summary>
    public const string Namespace = "\uEA8B";

    // ═══════════════════════════════════════════════════════════════
    // ENTITIES & GAME (Font Awesome + Custom)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Entity/cube: nf-fa-cube</summary>
    public const string Entity = "\uF1B2";

    /// <summary>Cubes: nf-fa-cubes</summary>
    public const string Entities = "\uF1B3";

    /// <summary>Component/puzzle piece: nf-fa-puzzle_piece</summary>
    public const string Component = "\uF12E";

    /// <summary>Relationship/link: nf-fa-link</summary>
    public const string Relationship = "\uF0C1";

    /// <summary>Player/user: nf-fa-user</summary>
    public const string Player = "\uF007";

    /// <summary>NPC/user outline: nf-fa-user_o</summary>
    public const string NPC = "\uF2C0";

    /// <summary>Users/group: nf-fa-users</summary>
    public const string Users = "\uF0C0";

    /// <summary>World/globe: nf-fa-globe</summary>
    public const string World = "\uF0AC";

    /// <summary>Map/location: nf-fa-map_marker</summary>
    public const string Map = "\uF041";

    /// <summary>Map outline: nf-fa-map_o</summary>
    public const string MapOutline = "\uF278";

    /// <summary>Gamepad: nf-fa-gamepad</summary>
    public const string Gamepad = "\uF11B";

    // ═══════════════════════════════════════════════════════════════
    // DEBUGGING & PROFILING (Codicons + Font Awesome)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Bug/debug: nf-fa-bug</summary>
    public const string Bug = "\uF188";

    /// <summary>Debug: nf-cod-debug</summary>
    public const string Debug = "\uEA87";

    /// <summary>Breakpoint: nf-cod-debug_breakpoint</summary>
    public const string Breakpoint = "\uEAB1";

    /// <summary>Watch/eye: nf-fa-eye</summary>
    public const string Watch = "\uF06E";

    /// <summary>Eye slash/hidden: nf-fa-eye_slash</summary>
    public const string Hidden = "\uF070";

    /// <summary>Timer/clock: nf-fa-clock_o</summary>
    public const string Timer = "\uF017";

    /// <summary>Stopwatch: nf-oct-stopwatch</summary>
    public const string Stopwatch = "\uF439";

    /// <summary>Performance/dashboard: nf-fa-dashboard (tachometer)</summary>
    public const string Performance = "\uF0E4";

    /// <summary>Memory/microchip: nf-fa-microchip</summary>
    public const string Memory = "\uF2DB";

    /// <summary>Server: nf-fa-server</summary>
    public const string Server = "\uF233";

    /// <summary>Database: nf-fa-database</summary>
    public const string Database = "\uF1C0";

    // ═══════════════════════════════════════════════════════════════
    // LOGS & OUTPUT (Font Awesome + Codicons)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Log/file text: nf-fa-file_text_o</summary>
    public const string Log = "\uF0F6";

    /// <summary>File alert/warning: nf-fa-file_text_o (with alert context)</summary>
    public const string FileAlert = "\uF0F6";

    /// <summary>Console/terminal: nf-fa-terminal</summary>
    public const string Console = "\uF120";

    /// <summary>Output/export: nf-cod-output</summary>
    public const string Output = "\uEB9D";

    /// <summary>History/clock: nf-fa-history</summary>
    public const string History = "\uF1DA";

    /// <summary>List: nf-fa-list</summary>
    public const string List = "\uF03A";

    /// <summary>List alt: nf-fa-list_alt</summary>
    public const string ListAlt = "\uF022";

    /// <summary>Filter: nf-fa-filter</summary>
    public const string Filter = "\uF0B0";

    // ═══════════════════════════════════════════════════════════════
    // FILE TYPES & EXTENSIONS (Seti-UI: E5FA-E6AC)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>C# file: nf-seti-csharp</summary>
    public const string CSharp = "\uE648";

    /// <summary>JSON file: nf-seti-json</summary>
    public const string Json = "\uE60B";

    /// <summary>Config file: nf-seti-config</summary>
    public const string Config = "\uE615";

    /// <summary>Script file: nf-custom-c</summary>
    public const string Script = "\uE614";

    /// <summary>Image file: nf-fa-file_image_o</summary>
    public const string Image = "\uF1C5";

    /// <summary>Code file: nf-fa-file_code_o</summary>
    public const string Code = "\uF1C9";

    // ═══════════════════════════════════════════════════════════════
    // MISC SYMBOLS (Unicode + Font Awesome)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Ellipsis: horizontal ellipsis</summary>
    public const string Ellipsis = "\u2026";

    /// <summary>Bullet point: nf-fa-circle</summary>
    public const string Bullet = "\uF111";

    /// <summary>Dot/filled circle: nf-cod-circle_filled</summary>
    public const string Dot = "\uEAAB";

    /// <summary>Empty dot/circle outline: nf-fa-circle_o</summary>
    public const string DotOutline = "\uF10C";

    /// <summary>Square: nf-fa-square</summary>
    public const string Square = "\uF0C8";

    /// <summary>Square outline: nf-fa-square_o</summary>
    public const string SquareOutline = "\uF096";

    /// <summary>Check square: nf-fa-check_square</summary>
    public const string CheckSquare = "\uF14A";

    /// <summary>Check square outline: nf-fa-check_square_o</summary>
    public const string CheckSquareOutline = "\uF046";

    /// <summary>Box drawing: vertical line</summary>
    public const string Separator = "│";

    /// <summary>Box drawing: branch</summary>
    public const string TreeBranch = "├";

    /// <summary>Box drawing: last item</summary>
    public const string TreeLast = "└";

    /// <summary>Box drawing: horizontal</summary>
    public const string TreeHorizontal = "─";

    /// <summary>Box drawing: vertical</summary>
    public const string TreeVertical = "│";

    /// <summary>Powerline left: nf-pl-left_hard_divider</summary>
    public const string PowerlineLeft = "\uE0B0";

    /// <summary>Powerline right: nf-pl-right_hard_divider</summary>
    public const string PowerlineRight = "\uE0B2";

    // ═══════════════════════════════════════════════════════════════
    // COMPOSITE STRINGS (Pre-built common combinations)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Prompt with space using chevron</summary>
    public const string Prompt = "\uF460 ";

    /// <summary>Expanded with space</summary>
    public const string ExpandedWithSpace = "\uEAB4 ";

    /// <summary>Collapsed with space</summary>
    public const string CollapsedWithSpace = "\uEAB6 ";

    /// <summary>Selected pointer with space</summary>
    public const string SelectedWithSpace = "\uEA9C ";

    /// <summary>Unselected (blank space to match selection width)</summary>
    public const string UnselectedSpace = "  ";

    /// <summary>Pinned section header</summary>
    public const string PinnedHeader = "\uF435 PINNED";

    /// <summary>Status healthy (checkmark)</summary>
    public const string StatusHealthy = "\uF00C";

    /// <summary>Status warning (triangle)</summary>
    public const string StatusWarning = "\uF071";

    /// <summary>Status error (X)</summary>
    public const string StatusError = "\uF00D";
}
