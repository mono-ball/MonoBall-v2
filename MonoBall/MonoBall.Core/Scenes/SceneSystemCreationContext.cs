using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.ECS.Services;
using MonoBall.Core.ECS.Systems;
using MonoBall.Core.ECS.Systems.Animation;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.Scenes;

/// <summary>
///     Context for scene system creation containing all dependencies needed by scene factories.
///     Extends beyond basic SystemCreationContext with scene-specific dependencies.
/// </summary>
public sealed class SceneSystemCreationContext
{
    /// <summary>
    ///     Initializes a new instance of the SceneSystemCreationContext.
    /// </summary>
    public SceneSystemCreationContext(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        Game game,
        IModManager modManager,
        IResourceManager resourceManager,
        IConstantsService constantsService,
        IInputBindingService inputBindingService,
        ICameraService cameraService,
        IFlagVariableService flagVariableService,
        IShaderSystems? shaderSystems,
        IRenderingSystems? renderingSystems,
        IAnimationSystems? animationSystems,
        PlayerSystem? playerSystem,
        IShaderService shaderService,
        ILogger logger
    )
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        GraphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        SpriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        Game = game ?? throw new ArgumentNullException(nameof(game));
        ModManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        ResourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        ConstantsService =
            constantsService ?? throw new ArgumentNullException(nameof(constantsService));
        InputBindingService =
            inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
        CameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
        FlagVariableService =
            flagVariableService ?? throw new ArgumentNullException(nameof(flagVariableService));
        ShaderSystems = shaderSystems; // Nullable - may not be available
        RenderingSystems = renderingSystems; // Nullable - may not be available
        AnimationSystems = animationSystems; // Nullable - may not be available
        PlayerSystem = playerSystem; // Nullable - may not be available
        ShaderService = shaderService ?? throw new ArgumentNullException(nameof(shaderService));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    ///     Gets the ECS world.
    /// </summary>
    public World World { get; }

    /// <summary>
    ///     Gets the graphics device.
    /// </summary>
    public GraphicsDevice GraphicsDevice { get; }

    /// <summary>
    ///     Gets the sprite batch for rendering.
    /// </summary>
    public SpriteBatch SpriteBatch { get; }

    /// <summary>
    ///     Gets the game instance.
    /// </summary>
    public Game Game { get; }

    /// <summary>
    ///     Gets the mod manager.
    /// </summary>
    public IModManager ModManager { get; }

    /// <summary>
    ///     Gets the resource manager.
    /// </summary>
    public IResourceManager ResourceManager { get; }

    /// <summary>
    ///     Gets the constants service.
    /// </summary>
    public IConstantsService ConstantsService { get; }

    /// <summary>
    ///     Gets the input binding service for input handling.
    /// </summary>
    public IInputBindingService InputBindingService { get; }

    /// <summary>
    ///     Gets the camera service for camera queries.
    /// </summary>
    public ICameraService CameraService { get; }

    /// <summary>
    ///     Gets the flag/variable service for game state.
    /// </summary>
    public IFlagVariableService FlagVariableService { get; }

    /// <summary>
    ///     Gets the shader systems bundle (nullable - may not be available).
    /// </summary>
    public IShaderSystems? ShaderSystems { get; }

    /// <summary>
    ///     Gets the rendering systems bundle (nullable - may not be available).
    /// </summary>
    public IRenderingSystems? RenderingSystems { get; }

    /// <summary>
    ///     Gets the animation systems bundle (nullable - may not be available).
    /// </summary>
    public IAnimationSystems? AnimationSystems { get; }

    /// <summary>
    ///     Gets the player system (nullable - may not be available).
    /// </summary>
    public PlayerSystem? PlayerSystem { get; }

    /// <summary>
    ///     Gets the shader service for shader operations.
    /// </summary>
    public IShaderService ShaderService { get; }

    /// <summary>
    ///     Gets the logger.
    /// </summary>
    public ILogger Logger { get; }
}
