using System.Collections.Generic;
using Arch.System;
using MonoBall.Core.ECS;
using MonoBall.Core.Scenes;

namespace MonoBall.Core.Audio;

/// <summary>
///     Factory interface for creating audio-related systems.
/// </summary>
public interface IAudioSystemFactory
{
    /// <summary>
    ///     Creates the audio systems bundle.
    /// </summary>
    /// <param name="context">The system creation context with shared dependencies.</param>
    /// <param name="sceneManager">The scene manager for map music system.</param>
    /// <param name="audioEngine">The audio engine for playback.</param>
    /// <param name="updateSystems">List to add update systems to for registration.</param>
    /// <returns>The created audio systems bundle.</returns>
    IAudioSystems Create(
        SystemCreationContext context,
        ISceneManager sceneManager,
        IAudioEngine audioEngine,
        IList<BaseSystem<Arch.Core.World, float>> updateSystems
    );
}
