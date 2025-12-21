using Arch.Core;

namespace MonoBall.Core.ECS
{
    /// <summary>
    /// Provides access to the ECS world instance.
    /// </summary>
    public static class EcsWorld
    {
        private static World? _instance;

        /// <summary>
        /// Gets or creates the ECS world instance.
        /// </summary>
        public static World Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = World.Create();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Resets the world instance. Used for cleanup or testing.
        /// </summary>
        public static void Reset()
        {
            if (_instance != null)
            {
                World.Destroy(_instance);
                _instance = null;
            }
        }
    }
}
