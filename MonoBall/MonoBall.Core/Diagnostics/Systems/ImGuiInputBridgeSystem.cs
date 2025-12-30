namespace MonoBall.Core.Diagnostics.Systems;

using System;
using System.Collections.Generic;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Utilities;

/// <summary>
/// Bridges input state between MonoGame and ImGui.
/// Uses keyboard polling instead of TextInput events to avoid duplicate input issues.
/// </summary>
public sealed class ImGuiInputBridgeSystem : DebugSystemBase
{
    private readonly ImGuiLifecycleSystem _lifecycleSystem;

    // Keyboard state tracking for polling
    private KeyboardState _currentKeyboard;
    private KeyboardState _previousKeyboard;

    // Key repeat tracking for smooth text input
    private readonly Dictionary<Keys, KeyRepeatState> _keyRepeatStates = new();

    // Cached list to avoid allocation during key repeat state updates
    private readonly List<Keys> _keysToRemoveCache = new();

    /// <summary>
    /// Initial delay before key repeat starts (in seconds).
    /// </summary>
    public float InitialKeyRepeatDelay { get; set; } = 0.5f;

    /// <summary>
    /// Interval between key repeats after initial delay (in seconds).
    /// </summary>
    public float KeyRepeatInterval { get; set; } = 0.05f;

    /// <summary>
    /// Initializes the ImGui input bridge system.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="lifecycleSystem">The ImGui lifecycle system.</param>
    /// <exception cref="ArgumentNullException">Thrown when lifecycleSystem is null.</exception>
    public ImGuiInputBridgeSystem(World world, ImGuiLifecycleSystem lifecycleSystem)
        : base(world)
    {
        _lifecycleSystem =
            lifecycleSystem ?? throw new ArgumentNullException(nameof(lifecycleSystem));

        // Initialize keyboard state
        _currentKeyboard = Keyboard.GetState();
        _previousKeyboard = _currentKeyboard;
    }

    /// <summary>
    /// Hooks text input events from the game window.
    /// Note: This method is kept for API compatibility but no longer uses TextInput events.
    /// </summary>
    /// <param name="window">The game window.</param>
    public void HookTextInput(GameWindow window)
    {
        ThrowIfDisposed();

        if (window == null)
            throw new ArgumentNullException(nameof(window));

        // No longer using TextInput events - we use keyboard polling instead
        // This method is kept for API compatibility
    }

    /// <summary>
    /// Gets whether ImGui wants to capture keyboard input.
    /// When true, the game should not process keyboard input.
    /// </summary>
    public bool WantsCaptureKeyboard => _lifecycleSystem.WantsCaptureKeyboard;

    /// <summary>
    /// Gets whether ImGui wants to capture mouse input.
    /// When true, the game should not process mouse input.
    /// </summary>
    public bool WantsCaptureMouse => _lifecycleSystem.WantsCaptureMouse;

    /// <summary>
    /// Gets whether ImGui wants text input.
    /// When true, the game should not process text input.
    /// </summary>
    public bool WantsCaptureTextInput => _lifecycleSystem.WantsCaptureTextInput;

    /// <summary>
    /// Gets whether the debug overlay is currently visible.
    /// </summary>
    public bool IsDebugVisible => _lifecycleSystem.IsVisible;

    /// <inheritdoc />
    public override void Update(in float deltaTime)
    {
        // Update keyboard state
        _previousKeyboard = _currentKeyboard;
        _currentKeyboard = Keyboard.GetState();

        // Update key repeat states
        UpdateKeyRepeatStates(deltaTime);

        // Only process text input when ImGui wants it
        if (!_lifecycleSystem.IsVisible || !_lifecycleSystem.WantsCaptureTextInput)
            return;

        // Process character input via keyboard polling
        ProcessCharacterInput();
    }

    /// <inheritdoc />
    protected override void DisposeManagedResources()
    {
        _keyRepeatStates.Clear();
        base.DisposeManagedResources();
    }

    private void UpdateKeyRepeatStates(float deltaTime)
    {
        _keysToRemoveCache.Clear();

        foreach (var (key, state) in _keyRepeatStates)
        {
            // If key is no longer held, mark for removal
            if (!_currentKeyboard.IsKeyDown(key))
            {
                _keysToRemoveCache.Add(key);
                continue;
            }

            // Update hold time
            state.HoldTime += deltaTime;

            // Update repeat timer if past initial delay
            if (state.HoldTime >= InitialKeyRepeatDelay)
            {
                state.TimeSinceLastRepeat += deltaTime;
            }
        }

        // Remove keys that are no longer held
        foreach (var key in _keysToRemoveCache)
        {
            _keyRepeatStates.Remove(key);
        }
    }

    private bool IsKeyPressedWithRepeat(Keys key)
    {
        // Initial press
        if (_currentKeyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key))
        {
            // Start tracking this key for repeat
            _keyRepeatStates[key] = new KeyRepeatState { HoldTime = 0, TimeSinceLastRepeat = 0 };
            return true;
        }

        // Check for repeat
        if (_keyRepeatStates.TryGetValue(key, out var state))
        {
            // Past initial delay and time for another repeat?
            if (
                state.HoldTime >= InitialKeyRepeatDelay
                && state.TimeSinceLastRepeat >= KeyRepeatInterval
            )
            {
                state.TimeSinceLastRepeat = 0; // Reset repeat timer
                return true;
            }
        }

        return false;
    }

    private bool IsShiftDown()
    {
        return _currentKeyboard.IsKeyDown(Keys.LeftShift)
            || _currentKeyboard.IsKeyDown(Keys.RightShift);
    }

    private void ProcessCharacterInput()
    {
        var io = Hexa.NET.ImGui.ImGui.GetIO();
        bool shift = IsShiftDown();

        // Process all character-producing keys
        foreach (Keys key in Enum.GetValues<Keys>())
        {
            if (IsKeyPressedWithRepeat(key))
            {
                char? ch = KeyboardHelper.KeyToChar(key, shift);
                if (ch.HasValue)
                {
                    io.AddInputCharacterUTF16(ch.Value);
                }
            }
        }
    }

    /// <summary>
    /// Tracks the repeat state of a held key.
    /// </summary>
    private sealed class KeyRepeatState
    {
        /// <summary>
        /// How long the key has been held (in seconds).
        /// </summary>
        public float HoldTime { get; set; }

        /// <summary>
        /// Time since the last repeat fired (in seconds).
        /// </summary>
        public float TimeSinceLastRepeat { get; set; }
    }
}
