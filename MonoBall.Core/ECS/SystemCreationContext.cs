using System;
using Arch.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Constants;
using MonoBall.Core.Mods;
using MonoBall.Core.Rendering;
using MonoBall.Core.Resources;
using Serilog;

namespace MonoBall.Core.ECS;

/// <summary>
///     Shared context for system creation - eliminates repeated parameter passing across factories.
///     Contains all common dependencies needed by system factories.
/// </summary>
public sealed class SystemCreationContext
{
    /// <summary>
    ///     Initializes a new instance of the SystemCreationContext.
    /// </summary>
    /// <param name="world">The ECS world.</param>
    /// <param name="graphicsDevice">The graphics device.</param>
    /// <param name="spriteBatch">The sprite batch for rendering.</param>
    /// <param name="modManager">The mod manager.</param>
    /// <param name="resourceManager">The resource manager.</param>
    /// <param name="shaderService">The shader service.</param>
    /// <param name="shaderParameterValidator">The shader parameter validator.</param>
    /// <param name="constantsService">The constants service.</param>
    /// <param name="game">The game instance.</param>
    /// <param name="logger">The logger for logging operations.</param>
    public SystemCreationContext(
        World world,
        GraphicsDevice graphicsDevice,
        SpriteBatch spriteBatch,
        IModManager modManager,
        IResourceManager resourceManager,
        IShaderService shaderService,
        IShaderParameterValidator shaderParameterValidator,
        IConstantsService constantsService,
        Game game,
        ILogger logger
    )
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        GraphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
        SpriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        ModManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
        ResourceManager =
            resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        ShaderService = shaderService ?? throw new ArgumentNullException(nameof(shaderService));
        ShaderParameterValidator =
            shaderParameterValidator
            ?? throw new ArgumentNullException(nameof(shaderParameterValidator));
        ConstantsService =
            constantsService ?? throw new ArgumentNullException(nameof(constantsService));
        Game = game ?? throw new ArgumentNullException(nameof(game));
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
    ///     Gets the mod manager.
    /// </summary>
    public IModManager ModManager { get; }

    /// <summary>
    ///     Gets the resource manager.
    /// </summary>
    public IResourceManager ResourceManager { get; }

    /// <summary>
    ///     Gets the shader service.
    /// </summary>
    public IShaderService ShaderService { get; }

    /// <summary>
    ///     Gets the shader parameter validator.
    /// </summary>
    public IShaderParameterValidator ShaderParameterValidator { get; }

    /// <summary>
    ///     Gets the constants service.
    /// </summary>
    public IConstantsService ConstantsService { get; }

    /// <summary>
    ///     Gets the game instance.
    /// </summary>
    public Game Game { get; }

    /// <summary>
    ///     Gets the logger.
    /// </summary>
    public ILogger Logger { get; }
}
