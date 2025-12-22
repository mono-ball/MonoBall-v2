using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;
using Serilog;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Implementation of IInputBindingService that maps keyboard and gamepad input to named actions.
    /// Provides default key bindings matching MonoBall behavior.
    /// </summary>
    public class InputBindingService : IInputBindingService
    {
        private readonly Dictionary<InputAction, Keys> _keyBindings;
        private readonly Dictionary<InputAction, HashSet<Keys>> _keyBindingsMultiple;
        private KeyboardState _currentKeyboardState;
        private KeyboardState _previousKeyboardState;
        private GamePadState _currentGamePadState;
        private GamePadState _previousGamePadState;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the InputBindingService with default key bindings.
        /// </summary>
        /// <param name="logger">The logger for logging operations.</param>
        public InputBindingService(ILogger logger)
        {
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
            _keyBindings = new Dictionary<InputAction, Keys>();
            _keyBindingsMultiple = new Dictionary<InputAction, HashSet<Keys>>();

            // Set default key bindings (matches MonoBall behavior)
            SetDefaultBindings();
            _logger.Debug("InputBindingService initialized with default key bindings");
        }

        /// <summary>
        /// Sets the default key bindings matching MonoBall behavior.
        /// </summary>
        private void SetDefaultBindings()
        {
            // Movement actions can be bound to multiple keys
            _keyBindingsMultiple[InputAction.MoveNorth] = new HashSet<Keys> { Keys.Up, Keys.W };
            _keyBindingsMultiple[InputAction.MoveSouth] = new HashSet<Keys> { Keys.Down, Keys.S };
            _keyBindingsMultiple[InputAction.MoveEast] = new HashSet<Keys> { Keys.Right, Keys.D };
            _keyBindingsMultiple[InputAction.MoveWest] = new HashSet<Keys> { Keys.Left, Keys.A };

            // Action buttons
            _keyBindings[InputAction.Interact] = Keys.Space; // Also supports Enter, Z
            _keyBindings[InputAction.Pause] = Keys.Escape;
            _keyBindings[InputAction.Menu] = Keys.Tab;
        }

        /// <summary>
        /// Updates the input state (should be called once per frame before checking input).
        /// </summary>
        public void Update()
        {
            _previousKeyboardState = _currentKeyboardState;
            _previousGamePadState = _currentGamePadState;

            _currentKeyboardState = Keyboard.GetState();
            _currentGamePadState = GamePad.GetState(0); // Player index 0
        }

        /// <summary>
        /// Checks if an action is currently pressed.
        /// </summary>
        /// <param name="action">The input action to check.</param>
        /// <returns>True if the action is pressed; false otherwise.</returns>
        public bool IsActionPressed(InputAction action)
        {
            // Check keyboard bindings
            if (_keyBindingsMultiple.TryGetValue(action, out var keys))
            {
                foreach (var key in keys)
                {
                    if (_currentKeyboardState.IsKeyDown(key))
                    {
                        return true;
                    }
                }
            }
            else if (_keyBindings.TryGetValue(action, out var singleKey))
            {
                if (_currentKeyboardState.IsKeyDown(singleKey))
                {
                    return true;
                }
            }

            // Check gamepad (for movement actions)
            if (IsGamePadActionPressed(action))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if an action was just pressed this frame.
        /// </summary>
        /// <param name="action">The input action to check.</param>
        /// <returns>True if the action was just pressed; false otherwise.</returns>
        public bool IsActionJustPressed(InputAction action)
        {
            // Check keyboard bindings
            if (_keyBindingsMultiple.TryGetValue(action, out var keys))
            {
                foreach (var key in keys)
                {
                    if (_currentKeyboardState.IsKeyDown(key) && _previousKeyboardState.IsKeyUp(key))
                    {
                        return true;
                    }
                }
            }
            else if (_keyBindings.TryGetValue(action, out var singleKey))
            {
                if (
                    _currentKeyboardState.IsKeyDown(singleKey)
                    && _previousKeyboardState.IsKeyUp(singleKey)
                )
                {
                    return true;
                }
            }

            // Check gamepad
            return IsGamePadActionJustPressed(action);
        }

        /// <summary>
        /// Checks if an action was just released this frame.
        /// </summary>
        /// <param name="action">The input action to check.</param>
        /// <returns>True if the action was just released; false otherwise.</returns>
        public bool IsActionJustReleased(InputAction action)
        {
            // Check keyboard bindings
            if (_keyBindingsMultiple.TryGetValue(action, out var keys))
            {
                foreach (var key in keys)
                {
                    if (_currentKeyboardState.IsKeyUp(key) && _previousKeyboardState.IsKeyDown(key))
                    {
                        return true;
                    }
                }
            }
            else if (_keyBindings.TryGetValue(action, out var singleKey))
            {
                if (
                    _currentKeyboardState.IsKeyUp(singleKey)
                    && _previousKeyboardState.IsKeyDown(singleKey)
                )
                {
                    return true;
                }
            }

            // Check gamepad
            return IsGamePadActionJustReleased(action);
        }

        /// <summary>
        /// Sets the key binding for an action.
        /// </summary>
        /// <param name="action">The input action.</param>
        /// <param name="key">The key to bind.</param>
        public void SetBinding(InputAction action, Keys key)
        {
            // Remove from multiple bindings if exists
            if (_keyBindingsMultiple.ContainsKey(action))
            {
                _keyBindingsMultiple.Remove(action);
            }

            _keyBindings[action] = key;
            _logger.Debug("Key binding changed: {Action} -> {Key}", action, key);
        }

        /// <summary>
        /// Gets the key binding for an action.
        /// </summary>
        /// <param name="action">The input action.</param>
        /// <returns>The bound key, or Keys.None if not bound or has multiple bindings.</returns>
        public Keys GetBinding(InputAction action)
        {
            if (_keyBindings.TryGetValue(action, out var key))
            {
                return key;
            }

            return Keys.None;
        }

        /// <summary>
        /// Converts currently pressed movement actions to a Direction.
        /// Returns the primary movement direction (prioritizes cardinal directions).
        /// </summary>
        /// <returns>The movement direction, or Direction.None if no movement action is pressed.</returns>
        public Direction GetMovementDirection()
        {
            // Check cardinal directions in priority order
            if (IsActionPressed(InputAction.MoveNorth))
            {
                return Direction.North;
            }

            if (IsActionPressed(InputAction.MoveSouth))
            {
                return Direction.South;
            }

            if (IsActionPressed(InputAction.MoveEast))
            {
                return Direction.East;
            }

            if (IsActionPressed(InputAction.MoveWest))
            {
                return Direction.West;
            }

            return Direction.None;
        }

        /// <summary>
        /// Checks if a gamepad action is currently pressed.
        /// </summary>
        private bool IsGamePadActionPressed(InputAction action)
        {
            if (!_currentGamePadState.IsConnected)
            {
                return false;
            }

            return action switch
            {
                InputAction.MoveNorth => _currentGamePadState.DPad.Up == ButtonState.Pressed
                    || _currentGamePadState.ThumbSticks.Left.Y > 0.5f,
                InputAction.MoveSouth => _currentGamePadState.DPad.Down == ButtonState.Pressed
                    || _currentGamePadState.ThumbSticks.Left.Y < -0.5f,
                InputAction.MoveEast => _currentGamePadState.DPad.Right == ButtonState.Pressed
                    || _currentGamePadState.ThumbSticks.Left.X > 0.5f,
                InputAction.MoveWest => _currentGamePadState.DPad.Left == ButtonState.Pressed
                    || _currentGamePadState.ThumbSticks.Left.X < -0.5f,
                InputAction.Interact => _currentGamePadState.Buttons.A == ButtonState.Pressed,
                InputAction.Pause => _currentGamePadState.Buttons.Start == ButtonState.Pressed,
                InputAction.Menu => _currentGamePadState.Buttons.Back == ButtonState.Pressed,
                _ => false,
            };
        }

        /// <summary>
        /// Checks if a gamepad action was just pressed this frame.
        /// </summary>
        private bool IsGamePadActionJustPressed(InputAction action)
        {
            if (!_currentGamePadState.IsConnected || !_previousGamePadState.IsConnected)
            {
                return false;
            }

            return action switch
            {
                InputAction.MoveNorth => (
                    _currentGamePadState.DPad.Up == ButtonState.Pressed
                    && _previousGamePadState.DPad.Up == ButtonState.Released
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.Y > 0.5f
                        && _previousGamePadState.ThumbSticks.Left.Y <= 0.5f
                    ),
                InputAction.MoveSouth => (
                    _currentGamePadState.DPad.Down == ButtonState.Pressed
                    && _previousGamePadState.DPad.Down == ButtonState.Released
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.Y < -0.5f
                        && _previousGamePadState.ThumbSticks.Left.Y >= -0.5f
                    ),
                InputAction.MoveEast => (
                    _currentGamePadState.DPad.Right == ButtonState.Pressed
                    && _previousGamePadState.DPad.Right == ButtonState.Released
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.X > 0.5f
                        && _previousGamePadState.ThumbSticks.Left.X <= 0.5f
                    ),
                InputAction.MoveWest => (
                    _currentGamePadState.DPad.Left == ButtonState.Pressed
                    && _previousGamePadState.DPad.Left == ButtonState.Released
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.X < -0.5f
                        && _previousGamePadState.ThumbSticks.Left.X >= -0.5f
                    ),
                InputAction.Interact => _currentGamePadState.Buttons.A == ButtonState.Pressed
                    && _previousGamePadState.Buttons.A == ButtonState.Released,
                InputAction.Pause => _currentGamePadState.Buttons.Start == ButtonState.Pressed
                    && _previousGamePadState.Buttons.Start == ButtonState.Released,
                InputAction.Menu => _currentGamePadState.Buttons.Back == ButtonState.Pressed
                    && _previousGamePadState.Buttons.Back == ButtonState.Released,
                _ => false,
            };
        }

        /// <summary>
        /// Checks if a gamepad action was just released this frame.
        /// </summary>
        private bool IsGamePadActionJustReleased(InputAction action)
        {
            if (!_currentGamePadState.IsConnected || !_previousGamePadState.IsConnected)
            {
                return false;
            }

            return action switch
            {
                InputAction.MoveNorth => (
                    _currentGamePadState.DPad.Up == ButtonState.Released
                    && _previousGamePadState.DPad.Up == ButtonState.Pressed
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.Y <= 0.5f
                        && _previousGamePadState.ThumbSticks.Left.Y > 0.5f
                    ),
                InputAction.MoveSouth => (
                    _currentGamePadState.DPad.Down == ButtonState.Released
                    && _previousGamePadState.DPad.Down == ButtonState.Pressed
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.Y >= -0.5f
                        && _previousGamePadState.ThumbSticks.Left.Y < -0.5f
                    ),
                InputAction.MoveEast => (
                    _currentGamePadState.DPad.Right == ButtonState.Released
                    && _previousGamePadState.DPad.Right == ButtonState.Pressed
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.X <= 0.5f
                        && _previousGamePadState.ThumbSticks.Left.X > 0.5f
                    ),
                InputAction.MoveWest => (
                    _currentGamePadState.DPad.Left == ButtonState.Released
                    && _previousGamePadState.DPad.Left == ButtonState.Pressed
                )
                    || (
                        _currentGamePadState.ThumbSticks.Left.X >= -0.5f
                        && _previousGamePadState.ThumbSticks.Left.X < -0.5f
                    ),
                InputAction.Interact => _currentGamePadState.Buttons.A == ButtonState.Released
                    && _previousGamePadState.Buttons.A == ButtonState.Pressed,
                InputAction.Pause => _currentGamePadState.Buttons.Start == ButtonState.Released
                    && _previousGamePadState.Buttons.Start == ButtonState.Pressed,
                InputAction.Menu => _currentGamePadState.Buttons.Back == ButtonState.Released
                    && _previousGamePadState.Buttons.Back == ButtonState.Pressed,
                _ => false,
            };
        }
    }
}
