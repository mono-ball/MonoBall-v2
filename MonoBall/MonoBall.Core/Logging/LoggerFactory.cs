using System;
using System.Diagnostics;
using System.IO;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Async;
using Serilog.Sinks.SystemConsole.Themes;

namespace MonoBall.Core.Logging
{
    /// <summary>
    /// Factory for creating and configuring Serilog loggers.
    /// </summary>
    public static class LoggerFactory
    {
        private static ILogger? _logger;
        private static bool _isConfigured = false;
        private static readonly object _lockObject = new object();

        /// <summary>
        /// Gets or creates the configured logger instance.
        /// </summary>
        public static ILogger Logger
        {
            get
            {
                if (!_isConfigured)
                {
                    ConfigureLogger();
                }
                return _logger ?? Log.Logger;
            }
        }

        /// <summary>
        /// Configures Serilog with appropriate sinks for the current environment.
        /// Thread-safe: uses double-checked locking pattern.
        /// </summary>
        public static void ConfigureLogger()
        {
            if (_isConfigured)
            {
                return;
            }

            lock (_lockObject)
            {
                if (_isConfigured)
                {
                    return;
                }

                var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
                Directory.CreateDirectory(logDirectory);

                var logFilePath = Path.Combine(logDirectory, "monoball-.log");

                // Detect environment (Development vs Production)
                // Check MONOBALL_ENVIRONMENT first, then fall back to ASPNETCORE_ENVIRONMENT for compatibility
                var environment =
                    Environment.GetEnvironmentVariable("MONOBALL_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    ?? (Debugger.IsAttached ? "Development" : "Production");

                var loggerConfiguration = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .MinimumLevel.Override("MonoBall.Core.ECS.Systems", LogEventLevel.Debug)
                    .MinimumLevel.Override("MonoBall.Core.Mods", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithThreadId()
                    .Enrich.WithProperty("Application", "MonoBall")
                    .Enrich.WithProperty("Environment", environment)
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                        theme: AnsiConsoleTheme.Code,
                        applyThemeToRedirectedOutput: true,
                        restrictedToMinimumLevel: environment == "Production"
                            ? LogEventLevel.Warning
                            : LogEventLevel.Debug
                    )
                    .WriteTo.Debug(
                        outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                        restrictedToMinimumLevel: environment == "Production"
                            ? LogEventLevel.Warning
                            : LogEventLevel.Debug
                    )
                    .WriteTo.Async(a =>
                        a.File(
                            logFilePath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 7,
                            fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file
                            rollOnFileSizeLimit: true,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                        )
                    );

                _logger = loggerConfiguration.CreateLogger();
                Log.Logger = _logger;
                _isConfigured = true;
            }
        }

        /// <summary>
        /// Creates a contextual logger for the specified type.
        /// Thread-safe: ensures logger is configured before creating contextual logger.
        /// </summary>
        /// <typeparam name="T">The type to create a logger for.</typeparam>
        /// <returns>A logger with source context set to the type name.</returns>
        public static ILogger CreateLogger<T>()
        {
            if (!_isConfigured)
            {
                ConfigureLogger();
            }
            return Log.ForContext<T>();
        }

        /// <summary>
        /// Closes and flushes the logger.
        /// </summary>
        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
            _logger = null;
            _isConfigured = false;
        }
    }
}
