using System;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Rendering;

/// <summary>
///     Minimal renderer for loading screen - no ECS, no events, no SystemManager.
///     Used during game initialization before the full SystemManager is created.
/// </summary>
public interface ILoadingRenderer : IDisposable
{
    /// <summary>
    ///     Updates the loading renderer (minimal, typically empty).
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    void Update(GameTime gameTime);

    /// <summary>
    ///     Renders the loading screen with progress bar and message.
    /// </summary>
    /// <param name="gameTime">The game time.</param>
    void Render(GameTime gameTime);

    /// <summary>
    ///     Sets the current loading progress and message.
    /// </summary>
    /// <param name="progress">Progress value from 0.0 to 1.0.</param>
    /// <param name="message">Status message to display.</param>
    void SetProgress(float progress, string message);
}
