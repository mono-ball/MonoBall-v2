using Arch.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.Mods;
using MonoBall.Core.Scripting.Services;
using Serilog;

namespace MonoBall.Core.ECS.Services;

/// <summary>
///     Dispatcher for tile interaction scripts.
///     Provides O(1) direct dispatch to script handlers.
/// </summary>
/// <remarks>
///     This is currently a placeholder implementation. When tile scripts with collision
///     checking are implemented, this dispatcher will resolve interactionId to the script
///     instance and call its CheckCollision method directly.
/// </remarks>
public sealed class TileInteractionDispatcher : ITileInteractionDispatcher
{
    private readonly DefinitionRegistry _definitionRegistry;
    private readonly ILogger _logger;
    private readonly ScriptLoaderService? _scriptLoaderService;

    /// <summary>
    ///     Initializes a new instance of TileInteractionDispatcher.
    /// </summary>
    /// <param name="definitionRegistry">The definition registry for looking up script definitions.</param>
    /// <param name="scriptLoaderService">The script loader service for resolving scripts (optional, can be null for placeholder mode).</param>
    /// <param name="logger">The logger.</param>
    public TileInteractionDispatcher(
        DefinitionRegistry definitionRegistry,
        ScriptLoaderService? scriptLoaderService,
        ILogger logger
    )
    {
        _definitionRegistry =
            definitionRegistry
            ?? throw new System.ArgumentNullException(nameof(definitionRegistry));
        _scriptLoaderService = scriptLoaderService; // Can be null for placeholder mode
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool CheckCollision(
        string interactionId,
        Entity entity,
        (int X, int Y) currentPosition,
        (int X, int Y) targetPosition,
        Direction fromDirection,
        byte elevation
    )
    {
        // TODO: When tile collision scripts are implemented, resolve the script and call its
        // CheckCollision method directly. For now, log and allow movement.
        //
        // Future implementation:
        // 1. Look up ScriptDefinition by interactionId
        // 2. Get or create script instance
        // 3. Call script.CheckTileCollision(entity, currentPos, targetPos, fromDir, elevation)
        // 4. Return script's blocking decision

        _logger.Verbose(
            "TileInteractionDispatcher.CheckCollision: {InteractionId} entity={EntityId} "
                + "from=({FromX},{FromY}) to=({ToX},{ToY}) dir={Direction} elev={Elevation}",
            interactionId,
            entity.Id,
            currentPosition.X,
            currentPosition.Y,
            targetPosition.X,
            targetPosition.Y,
            fromDirection,
            elevation
        );

        // Currently no tile scripts implement collision blocking
        return false;
    }

    /// <inheritdoc />
    public void NotifyMovementCompleted(
        string interactionId,
        Entity entity,
        (int X, int Y) newPosition,
        Direction direction,
        byte elevation
    )
    {
        // TODO: When tile movement scripts are implemented, resolve the script and call its
        // OnEntityEnter method directly.
        //
        // Future implementation:
        // 1. Look up ScriptDefinition by interactionId
        // 2. Get or create script instance
        // 3. Call script.OnTileEnter(entity, newPos, direction, elevation)

        _logger.Verbose(
            "TileInteractionDispatcher.NotifyMovementCompleted: {InteractionId} entity={EntityId} "
                + "pos=({X},{Y}) dir={Direction} elev={Elevation}",
            interactionId,
            entity.Id,
            newPosition.X,
            newPosition.Y,
            direction,
            elevation
        );
    }
}
