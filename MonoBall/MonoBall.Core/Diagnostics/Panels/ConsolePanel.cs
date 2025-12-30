namespace MonoBall.Core.Diagnostics.Panels;

using System;
using System.Collections.Generic;
using System.Numerics;
using Hexa.NET.ImGui;
using MonoBall.Core.Diagnostics.Console.Features;
using MonoBall.Core.Diagnostics.Console.Services;
using MonoBall.Core.Diagnostics.UI;

/// <summary>
/// ImGui-based debug console panel.
/// Provides command input, output display, and auto-completion.
/// </summary>
public sealed class ConsolePanel : IDebugPanel, IDebugPanelLifecycle
{
    private const int MaxInputLength = 4096;
    private const float InputHeight = 24f;
    private const float CompletionItemHeight = 36f; // Height per completion item (text + description)
    private const float CompletionMaxHeight = 250f; // Maximum popup height
    private const float CompletionMinWidth = 400f; // Minimum popup width
    private const int MaxVisibleCompletions = 8; // Max items before scrolling

    private readonly IConsoleService _consoleService;
    private readonly byte[] _inputBuffer = new byte[MaxInputLength];
    private readonly List<CompletionItem> _completions = [];

    private bool _isVisible = true;
    private bool _scrollToBottom = true;
    private bool _focusInput;
    private bool _showCompletions;
    private int _selectedCompletion;
    private bool _completionScrollNeeded;
    private int _pendingCursorPos = -1; // -1 means no pending cursor change
    private bool _skipNextSubmit; // Prevents double-Enter bug (completion + submit)
    private bool _disposed;

    /// <summary>
    /// Gets the panel ID.
    /// </summary>
    public string Id => "Console";

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName => $"{NerdFontIcons.Console} Console";

    /// <summary>
    /// Gets or sets whether the panel is visible.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => _isVisible = value;
    }

    /// <summary>
    /// Gets the panel category.
    /// </summary>
    public string Category => "Tools";

    /// <summary>
    /// Gets the sort order.
    /// </summary>
    public int SortOrder => 100;

    /// <summary>
    /// Gets the default panel size.
    /// </summary>
    public Vector2? DefaultSize => new(600, 400);

    /// <summary>
    /// Initializes a new console panel.
    /// </summary>
    /// <param name="consoleService">The console service.</param>
    public ConsolePanel(IConsoleService consoleService)
    {
        _consoleService = consoleService ?? throw new ArgumentNullException(nameof(consoleService));
    }

    /// <summary>
    /// Called when the panel is first registered.
    /// </summary>
    public void Initialize()
    {
        _consoleService.WriteWelcome();
    }

    /// <summary>
    /// Called every frame before Draw.
    /// </summary>
    public void Update(float deltaTime)
    {
        // Nothing to update
    }

    /// <summary>
    /// Renders the console panel.
    /// </summary>
    public void Draw(float deltaTime)
    {
        // Handle completion popup keyboard navigation
        // (History is handled by ImGui callback in HandleHistory)
        if (_showCompletions && _completions.Count > 0)
        {
            HandleCompletionsKeyboard();
        }

        RenderOutput();
        RenderInputArea();

        if (_showCompletions && _completions.Count > 0)
        {
            RenderCompletions();
        }

        // Reset skip flag at end of frame - ensures it only affects the current frame
        // This prevents the flag from persisting if InputText didn't return true
        _skipNextSubmit = false;
    }

    /// <summary>
    /// Handles keyboard navigation for the completions popup.
    /// </summary>
    private void HandleCompletionsKeyboard()
    {
        // Tab - cycle to next (handled here to prevent ImGui's tab navigation)
        if (ImGui.IsKeyPressed(ImGuiKey.Tab))
        {
            _selectedCompletion = (_selectedCompletion + 1) % _completions.Count;
            _completionScrollNeeded = true;
        }
        // Up arrow - select previous
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
        {
            _selectedCompletion--;
            if (_selectedCompletion < 0)
                _selectedCompletion = _completions.Count - 1;
            _completionScrollNeeded = true;
        }
        // Down arrow - select next
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
        {
            _selectedCompletion++;
            if (_selectedCompletion >= _completions.Count)
                _selectedCompletion = 0;
            _completionScrollNeeded = true;
        }
        // Enter - accept selection
        else if (
            ImGui.IsKeyPressed(ImGuiKey.Enter)
            && _selectedCompletion >= 0
            && _selectedCompletion < _completions.Count
        )
        {
            ApplyCompletion(_completions[_selectedCompletion].Text);
            // Prevent the InputText from also submitting the command
            _skipNextSubmit = true;
        }
        // Escape - close completions
        else if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _showCompletions = false;
        }
    }

    /// <summary>
    /// Renders the output area.
    /// </summary>
    private void RenderOutput()
    {
        var footerHeight = ImGui.GetStyle().ItemSpacing.Y + InputHeight;
        var outputSize = new Vector2(0, -footerHeight);

        ImGui.BeginChild(
            "ConsoleOutput",
            outputSize,
            ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar
        );

        _consoleService.OutputBuffer.ForEach(entry =>
        {
            ImGui.PushStyleColor(ImGuiCol.Text, entry.Color);
            ImGui.TextUnformatted(entry.Text);
            ImGui.PopStyleColor();
        });

        // Auto-scroll to bottom
        if (_scrollToBottom && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// Renders the input area.
    /// </summary>
    private void RenderInputArea()
    {
        ImGui.Separator();

        // Input prompt
        ImGui.TextColored(ConsoleColors.System, ">");
        ImGui.SameLine();

        // Set focus if requested
        if (_focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInput = false;
        }

        // Input text field
        ImGui.PushItemWidth(-1);
        var flags =
            ImGuiInputTextFlags.EnterReturnsTrue
            | ImGuiInputTextFlags.CallbackCompletion
            | ImGuiInputTextFlags.CallbackHistory
            | ImGuiInputTextFlags.CallbackEdit
            | ImGuiInputTextFlags.CallbackAlways;

        unsafe
        {
            fixed (byte* buf = _inputBuffer)
            {
                if (ImGui.InputText("##ConsoleInput", buf, MaxInputLength, flags, InputCallback))
                {
                    // Check if we should skip submission (Enter was used for completion this frame)
                    if (!_skipNextSubmit)
                    {
                        SubmitCommand();
                    }
                }
            }
        }
        ImGui.PopItemWidth();

        // Handle focus
        if (ImGui.IsItemActive())
        {
            _scrollToBottom = true;
        }
    }

    /// <summary>
    /// Renders the completions popup.
    /// </summary>
    private void RenderCompletions()
    {
        // Get the position of the input area for popup positioning
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        // Calculate popup dimensions
        var itemCount = Math.Min(_completions.Count, MaxVisibleCompletions);
        var popupHeight = Math.Min(itemCount * CompletionItemHeight + 8, CompletionMaxHeight);
        var popupWidth = Math.Max(CompletionMinWidth, windowSize.X - 16);

        // Position popup above the input area at the left edge of the window
        var popupPos = new Vector2(
            windowPos.X + 8,
            windowPos.Y + windowSize.Y - popupHeight - InputHeight - 30
        );

        ImGui.SetNextWindowPos(popupPos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(popupWidth, popupHeight));

        // Style the popup with a subtle border
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.Border, DebugColors.TextDim);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, DebugColors.BackgroundElevated);
        // Disable nav highlight to avoid conflicting selection visuals
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, new Vector4(0, 0, 0, 0));

        var flags =
            ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoNav; // Disable ImGui navigation to prevent Tab conflicts

        if (ImGui.Begin("##Completions", flags))
        {
            for (var i = 0; i < _completions.Count; i++)
            {
                var completion = _completions[i];
                var isSelected = i == _selectedCompletion;

                // Draw custom completion item with two lines
                DrawCompletionItem(completion, isSelected, i);
            }
        }
        ImGui.End();

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(3);
    }

    /// <summary>
    /// Draws a single completion item with name and description.
    /// </summary>
    private void DrawCompletionItem(CompletionItem item, bool isSelected, int index)
    {
        var startPos = ImGui.GetCursorPos();
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(index);

        // Override ALL selection colors to ensure consistent appearance
        // Use Info (blue) for selected, slightly darker for hover/active
        var selectColor = isSelected ? DebugColors.Info : new System.Numerics.Vector4(0, 0, 0, 0);
        var hoverColor = isSelected
            ? DebugColors.Info
            : new System.Numerics.Vector4(0.3f, 0.3f, 0.35f, 0.5f);

        ImGui.PushStyleColor(ImGuiCol.Header, selectColor);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, selectColor);

        // Selectable covers the full item height
        if (
            ImGui.Selectable(
                "##item",
                isSelected,
                ImGuiSelectableFlags.None,
                new Vector2(availWidth, CompletionItemHeight - 4)
            )
        )
        {
            ApplyCompletion(item.Text);
        }

        ImGui.PopStyleColor(3);

        if (isSelected)
        {
            ImGui.SetItemDefaultFocus();

            if (ImGui.IsWindowAppearing() || _completionScrollNeeded)
            {
                ImGui.SetScrollHereY();
                _completionScrollNeeded = false;
            }
        }

        // Draw text over the selectable
        ImGui.SetCursorPos(new Vector2(startPos.X + 8, startPos.Y + 2));

        // Category badge (if present)
        if (!string.IsNullOrEmpty(item.Category))
        {
            var categoryColor = item.Category switch
            {
                "command" => DebugColors.Success,
                "alias" => DebugColors.Warning,
                "arg" => DebugColors.Info,
                _ => DebugColors.TextDim,
            };
            ImGui.TextColored(categoryColor, $"[{item.Category}]");
            ImGui.SameLine();
        }

        // Command name (primary text)
        ImGui.TextColored(DebugColors.TextPrimary, item.Text);

        // Description on second line (if present)
        if (!string.IsNullOrEmpty(item.Description))
        {
            ImGui.SetCursorPos(new Vector2(startPos.X + 8, startPos.Y + 16));
            ImGui.TextColored(DebugColors.TextDim, item.Description);
        }

        // Move cursor to end of item
        ImGui.SetCursorPos(new Vector2(startPos.X, startPos.Y + CompletionItemHeight - 4));

        ImGui.PopID();
    }

    /// <summary>
    /// Input callback for handling special keys.
    /// </summary>
    private unsafe int InputCallback(ImGuiInputTextCallbackData* data)
    {
        switch (data->EventFlag)
        {
            case ImGuiInputTextFlags.CallbackCompletion:
                HandleTabCompletion(data);
                break;

            case ImGuiInputTextFlags.CallbackHistory:
                HandleHistory(data);
                break;

            case ImGuiInputTextFlags.CallbackEdit:
                UpdateCompletions(data);
                break;

            case ImGuiInputTextFlags.CallbackAlways:
                // Apply pending cursor position from completion
                if (_pendingCursorPos >= 0)
                {
                    data->CursorPos = _pendingCursorPos;
                    data->SelectionStart = _pendingCursorPos;
                    data->SelectionEnd = _pendingCursorPos;
                    _pendingCursorPos = -1;
                }
                break;
        }

        return 0;
    }

    /// <summary>
    /// Handles tab completion callback - triggers popup on first Tab press.
    /// Subsequent Tab presses are handled in HandleCompletionsKeyboard().
    /// </summary>
    private unsafe void HandleTabCompletion(ImGuiInputTextCallbackData* data)
    {
        // If popup is already visible, let HandleCompletionsKeyboard() handle Tab cycling
        if (_showCompletions)
        {
            return;
        }

        // First Tab press - refresh completions and show popup
        var input = GetStringFromBuffer(data->Buf, data->BufTextLen);
        _completions.Clear();
        _completions.AddRange(_consoleService.GetRichCompletions(input));
        _selectedCompletion = 0;

        if (_completions.Count == 0)
        {
            // No completions available
            return;
        }

        if (_completions.Count == 1)
        {
            // Single match - apply immediately
            ApplyCompletionToBuffer(data, _completions[0].Text);
        }
        else
        {
            // Multiple matches - show popup
            _showCompletions = true;
            _completionScrollNeeded = true;
        }
    }

    /// <summary>
    /// Handles history navigation via ImGui callback.
    /// Only fires when completions popup is NOT visible.
    /// </summary>
    private unsafe void HandleHistory(ImGuiInputTextCallbackData* data)
    {
        // If completions popup is visible, don't handle history - let HandleCompletionsKeyboard() do it
        if (_showCompletions && _completions.Count > 0)
        {
            return;
        }

        var currentInput = GetInputString();
        string? newText = null;

        if (data->EventKey == ImGuiKey.UpArrow)
        {
            newText = _consoleService.History.NavigatePrevious(currentInput);
        }
        else if (data->EventKey == ImGuiKey.DownArrow)
        {
            newText = _consoleService.History.NavigateNext();
        }

        if (newText != null)
        {
            SetInputBuffer(data, newText);
        }
    }

    /// <summary>
    /// Updates the completions list without changing visibility.
    /// Called on edit to keep completions in sync if popup is already visible.
    /// </summary>
    private unsafe void UpdateCompletions(ImGuiInputTextCallbackData* data)
    {
        var input = GetStringFromBuffer(data->Buf, data->BufTextLen);

        // Check if user typed a character that should dismiss completions
        if (_showCompletions && data->BufTextLen > 0)
        {
            var lastChar = (char)data->Buf[data->BufTextLen - 1];
            // Dismiss on space, semicolon, braces (like oldmonoball)
            if (lastChar is ' ' or ';' or '{' or '}')
            {
                _showCompletions = false;
                return;
            }
        }

        // Update completions list (but don't auto-show - that's Tab's job)
        _completions.Clear();
        _completions.AddRange(_consoleService.GetRichCompletions(input));
        _selectedCompletion = 0;

        // If popup was visible but now we have no matches, hide it
        if (_showCompletions && _completions.Count == 0)
        {
            _showCompletions = false;
        }
    }

    /// <summary>
    /// Applies the selected completion.
    /// </summary>
    private void ApplyCompletion(string completion)
    {
        var input = GetInputString();
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string newInput;
        if (parts.Length <= 1)
        {
            // Completing command name
            newInput = completion + " ";
        }
        else
        {
            // Completing argument
            parts[^1] = completion;
            newInput = string.Join(" ", parts) + " ";
        }

        SetInputString(newInput);
        _showCompletions = false;
        _focusInput = true;

        // Set pending cursor position to end of text (will be applied in callback)
        _pendingCursorPos = System.Text.Encoding.UTF8.GetByteCount(newInput);
    }

    /// <summary>
    /// Applies completion directly to the input buffer.
    /// </summary>
    private unsafe void ApplyCompletionToBuffer(ImGuiInputTextCallbackData* data, string completion)
    {
        var input = GetStringFromBuffer(data->Buf, data->BufTextLen);
        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string newInput;
        if (parts.Length <= 1)
        {
            newInput = completion + " ";
        }
        else
        {
            parts[^1] = completion;
            newInput = string.Join(" ", parts) + " ";
        }

        SetInputBuffer(data, newInput);
    }

    /// <summary>
    /// Submits the current input as a command.
    /// </summary>
    private void SubmitCommand()
    {
        var command = GetInputString();
        if (!string.IsNullOrWhiteSpace(command))
        {
            _ = _consoleService.ExecuteCommandAsync(command);
        }

        ClearInput();
        _showCompletions = false;
        _focusInput = true;
        _scrollToBottom = true;
    }

    /// <summary>
    /// Gets the current input string.
    /// </summary>
    private string GetInputString()
    {
        var nullIndex = Array.IndexOf(_inputBuffer, (byte)0);
        var length = nullIndex >= 0 ? nullIndex : _inputBuffer.Length;
        return System.Text.Encoding.UTF8.GetString(_inputBuffer, 0, length);
    }

    /// <summary>
    /// Sets the input string.
    /// </summary>
    private void SetInputString(string text)
    {
        Array.Clear(_inputBuffer);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Array.Copy(bytes, _inputBuffer, Math.Min(bytes.Length, _inputBuffer.Length - 1));
    }

    /// <summary>
    /// Clears the input buffer.
    /// </summary>
    private void ClearInput()
    {
        Array.Clear(_inputBuffer);
    }

    /// <summary>
    /// Gets a string from a buffer pointer.
    /// </summary>
    private static unsafe string GetStringFromBuffer(byte* buf, int length)
    {
        return System.Text.Encoding.UTF8.GetString(buf, length);
    }

    /// <summary>
    /// Sets the buffer content from a string.
    /// </summary>
    private static unsafe void SetInputBuffer(ImGuiInputTextCallbackData* data, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var length = Math.Min(bytes.Length, data->BufSize - 1);

        for (var i = 0; i < length; i++)
        {
            data->Buf[i] = bytes[i];
        }
        data->Buf[length] = 0;

        data->BufTextLen = length;
        data->CursorPos = length;
        data->SelectionStart = length;
        data->SelectionEnd = length;
        data->BufDirty = 1;
    }

    /// <summary>
    /// Disposes the console panel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}
