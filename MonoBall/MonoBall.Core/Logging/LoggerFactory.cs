using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace MonoBall.Core.Logging
{
    /// <summary>
    /// Factory for creating and configuring Serilog loggers.
    /// </summary>
    public static class LoggerFactory
    {
        private static ILogger? _logger;
        private static bool _isConfigured = false;

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
        /// </summary>
        public static void ConfigureLogger()
        {
            if (_isConfigured)
            {
                return;
            }

            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);

            var logFilePath = Path.Combine(logDirectory, "monoball-.log");

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "MonoBall")
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.Debug(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
                );

            _logger = loggerConfiguration.CreateLogger();
            Log.Logger = _logger;
            _isConfigured = true;
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
