using Microsoft.Xna.Framework.Input;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;

namespace MonoBall.Core.ECS.Services
{
    /// <summary>
    /// Interface for named input actions binding service.
    /// Architecture improvement over direct key mapping for better customizability.
    /// </summary>
    public interface IInputBindingService
    {
        /// <summary>
        /// Checks if an action is currently pressed.
        /// </summary>
        /// <param name="action">The input action to check.</param>
        /// <returns>True if the action is pressed; false otherwise.</returns>
        bool IsActionPressed(InputAction action);

        /// <summary>
        /// Checks if an action was just pressed this frame (was not pressed last frame).
        /// </summary>
        /// <param name="action">The input action to check.</param>
        /// <returns>True if the action was just pressed; false otherwise.</returns>
        bool IsActionJustPressed(InputAction action);

        /// <summary>
        /// Checks if an action was just released this frame (was pressed last frame).
        /// </summary>
        /// <param name="action">The input action to check.</param>
        /// <returns>True if the action was just released; false otherwise.</returns>
        bool IsActionJustReleased(InputAction action);

        /// <summary>
        /// Sets the key binding for an action.
        /// </summary>
        /// <param name="action">The input action.</param>
        /// <param name="key">The key to bind.</param>
        void SetBinding(InputAction action, Keys key);

        /// <summary>
        /// Gets the key binding for an action.
        /// </summary>
        /// <param name="action">The input action.</param>
        /// <returns>The bound key.</returns>
        Keys GetBinding(InputAction action);

        /// <summary>
        /// Converts currently pressed movement actions to a Direction.
        /// Returns the primary movement direction (prioritizes cardinal directions).
        /// </summary>
        /// <returns>The movement direction, or Direction.None if no movement action is pressed.</returns>
        Direction GetMovementDirection();

        /// <summary>
        /// Updates the input state (should be called once per frame before checking input).
        /// </summary>
        void Update();
    }
}
