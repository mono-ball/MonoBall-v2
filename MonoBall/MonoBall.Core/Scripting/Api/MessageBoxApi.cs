using System;
using Arch.Core;
using MonoBall.Core.ECS;
using MonoBall.Core.ECS.Events;
using MonoBall.Core.Scenes.Components;

namespace MonoBall.Core.Scripting.Api;

/// <summary>
///     Implementation of IMessageBoxApi that sends events via EventBus.
/// </summary>
public class MessageBoxApi : IMessageBoxApi
{
    // Cached QueryDescription (created once, never in hot paths)
    private static readonly QueryDescription _messageBoxQuery =
        new QueryDescription().WithAll<MessageBoxComponent>();

    private readonly World _world;

    /// <summary>
    ///     Initializes a new instance of the MessageBoxApi.
    /// </summary>
    /// <param name="world">The ECS world for querying message box state.</param>
    public MessageBoxApi(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    /// <summary>
    ///     Shows a message box with the specified text.
    ///     The message box is queued to be shown on the next frame to avoid blocking during heavy operations
    ///     (e.g., map transitions). This prevents lag when showing message boxes during map transitions.
    /// </summary>
    /// <param name="text">The text to display.</param>
    /// <param name="textSpeedOverride">Optional text speed override in seconds per character (null = use player preference).</param>
    /// <exception cref="System.ArgumentException">Thrown if text is null or whitespace.</exception>
    public void ShowMessage(string text, float? textSpeedOverride = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be null or whitespace.", nameof(text));

        var evt = new MessageBoxShowEvent
        {
            Text = text,
            TextSpeedOverride = textSpeedOverride,
            CanSpeedUpWithButton = true,
            AutoScroll = false,
        };

        // Always queue the event to defer message box creation until the next frame.
        // This prevents lag when showing message boxes during heavy operations like map transitions.
        // Even if called from the main thread, queuing ensures the message box is created
        // after the current frame's heavy work (map loading, entity creation, etc.) completes.
        EventBus.SendNextFrame(evt);
    }

    /// <summary>
    ///     Hides the current message box.
    /// </summary>
    public void HideMessage()
    {
        var evt = new MessageBoxHideEvent { WindowId = 0 };
        EventBus.Send(ref evt);
    }

    /// <summary>
    ///     Checks if a message box is currently visible.
    /// </summary>
    /// <returns>True if a message box is visible, false otherwise.</returns>
    public bool IsMessageBoxVisible()
    {
        // Query for message box components using cached query
        // Include Entity in query to validate entity is alive
        var found = false;
        _world.Query(
            in _messageBoxQuery,
            (Entity entity, ref MessageBoxComponent msgBox) =>
            {
                // Validate entity is still alive (defensive check)
                if (!_world.IsAlive(entity))
                    return;

                if (msgBox.IsVisible && msgBox.State != MessageBoxRenderState.Hidden)
                    found = true;
            }
        );
        return found;
    }
}
