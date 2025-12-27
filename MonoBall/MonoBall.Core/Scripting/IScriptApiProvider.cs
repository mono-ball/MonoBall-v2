using MonoBall.Core.ECS.Services;
using MonoBall.Core.Mods;
using MonoBall.Core.Scripting.Api;

namespace MonoBall.Core.Scripting
{
    /// <summary>
    /// Provider interface for script-safe game APIs.
    /// Exposes game systems through script-safe interfaces.
    /// </summary>
    public interface IScriptApiProvider
    {
        /// <summary>
        /// Gets the player API.
        /// </summary>
        IPlayerApi Player { get; }

        /// <summary>
        /// Gets the map API.
        /// </summary>
        IMapApi Map { get; }

        /// <summary>
        /// Gets the movement API.
        /// </summary>
        IMovementApi Movement { get; }

        /// <summary>
        /// Gets the camera API.
        /// </summary>
        ICameraApi Camera { get; }

        /// <summary>
        /// Gets the flags/variables service (direct access, already script-safe).
        /// </summary>
        IFlagVariableService Flags { get; }

        /// <summary>
        /// Gets the definition registry (read-only access).
        /// </summary>
        DefinitionRegistry Definitions { get; }

        /// <summary>
        /// Gets the NPC API.
        /// </summary>
        INpcApi Npc { get; }

        /// <summary>
        /// Gets the message box API.
        /// </summary>
        IMessageBoxApi MessageBox { get; }
    }
}
