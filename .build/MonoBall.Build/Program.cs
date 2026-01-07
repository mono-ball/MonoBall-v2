using Cake.Frosting;

namespace MonoBall.Build
{
    /// <summary>
    /// Entry point for the Cake Frosting build system.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main entry point for the build system.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>Exit code (0 for success, non-zero for failure).</returns>
        public static int Main(string[] args)
        {
            return new CakeHost()
                .UseContext<BuildContext>()
                .Run(args);
        }
    }
}
