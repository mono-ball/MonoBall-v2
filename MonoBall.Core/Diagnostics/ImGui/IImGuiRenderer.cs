namespace MonoBall.Core.Diagnostics.ImGui;

using System;
using Microsoft.Xna.Framework;
using MonoBall.Core.Resources;

/// <summary>
/// Interface for ImGui rendering backend integration with MonoGame.
/// </summary>
public interface IImGuiRenderer : IDisposable
{
    /// <summary>
    /// Gets whether the renderer has been initialized.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Initializes the ImGui renderer with the game instance and optional resource manager.
    /// </summary>
    /// <param name="game">The MonoGame Game instance.</param>
    /// <param name="resourceManager">Optional resource manager for loading fonts from the mod system.</param>
    /// <exception cref="ArgumentNullException">Thrown when game is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when already initialized.</exception>
    void Initialize(Game game, IResourceManager? resourceManager = null);

    /// <summary>
    /// Begins a new ImGui frame. Call at start of Update.
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds.</param>
    void BeginFrame(float deltaTime);

    /// <summary>
    /// Ends the ImGui frame. Call at end of Update after all ImGui drawing.
    /// </summary>
    void EndFrame();

    /// <summary>
    /// Renders the ImGui draw data. Call in Draw.
    /// </summary>
    void Render();

    /// <summary>
    /// Rebuilds the font texture atlas. Call after adding fonts.
    /// </summary>
    void RebuildFontAtlas();

    /// <summary>
    /// Binds a MonoGame texture for use in ImGui.
    /// </summary>
    /// <param name="texture">The texture to bind.</param>
    /// <returns>An IntPtr handle for use with ImGui.Image().</returns>
    IntPtr BindTexture(Microsoft.Xna.Framework.Graphics.Texture2D texture);

    /// <summary>
    /// Unbinds a previously bound texture.
    /// </summary>
    /// <param name="textureHandle">The texture handle to unbind.</param>
    void UnbindTexture(IntPtr textureHandle);
}
