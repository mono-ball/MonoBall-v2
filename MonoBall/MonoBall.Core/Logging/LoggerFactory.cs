using System;
using System.Diagnostics;
using System.IO;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace MonoBall.Core.Logging;

/// <summary>
///     Factory for creating and configuring Serilog loggers.
/// </summary>
/// <remarks>
///     <para>
///         <b>Performance Considerations:</b>
///     </para>
///     <para>
///         Even with async sinks, Serilog evaluates all log parameters synchronously BEFORE checking
///         if logging is enabled. This means expensive operations in log parameters will always execute,
///         even when logging is disabled.
///     </para>
///     <para>
///         For high-frequency code paths (like Update loops), use conditional logging:
///         <code>
/// // ❌ BAD: expensiveMethod() always runs
/// _logger.Debug("Value: {Value}", expensiveMethod());
///
/// // ✅ GOOD: Check IsEnabled first
/// if (_logger.IsEnabled(LogEventLevel.Debug))
/// {
///     _logger.Debug("Value: {Value}", expensiveMethod());
/// }
///
/// // ✅ ALSO GOOD: Use LoggerExtensions.DebugIfEnabled for lazy evaluation
/// _logger.DebugIfEnabled("Value: {Value}", () => expensiveMethod());
/// </code>
///     </para>
///     <para>
///         The logger is configured with:
///         - Single async wrapper for ALL sinks (one background thread, maximum efficiency)
///         - Very large buffer (100k events) to handle high-volume logging
///         - blockWhenFull: false (drops logs rather than blocking game thread)
///         - Minimal enrichers (reduced overhead)
///         - Console restricted to Information+ only (Debug logs go to file only, console I/O is expensive)
///         - Optional console/debug sink disabling via MONOBALL_DISABLE_CONSOLE_LOGGING=1
///     </para>
///     <para>
///         <b>High-Volume Logging:</b>
///         If logging volume exceeds the buffer capacity, logs will be dropped rather than
///         blocking the game thread. To maximize performance, set MONOBALL_DISABLE_CONSOLE_LOGGING=1
///         to disable console/debug sinks and only log to file.
///     </para>
/// </remarks>
public static class LoggerFactory
{
    private static ILogger? _logger;
    private static bool _isConfigured;
    private static readonly object _lockObject = new();

    /// <summary>
    ///     Gets or creates the configured logger instance.
    /// </summary>
    public static ILogger Logger
    {
        get
        {
            if (!_isConfigured)
                ConfigureLogger();
            return _logger ?? Log.Logger;
        }
    }

    /// <summary>
    ///     Configures Serilog with appropriate sinks for the current environment.
    ///     Thread-safe: uses double-checked locking pattern.
    /// </summary>
    public static void ConfigureLogger()
    {
        if (_isConfigured)
            return;

        lock (_lockObject)
        {
            if (_isConfigured)
                return;

            var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);

            var logFilePath = Path.Combine(logDirectory, "monoball-.log");

            // Detect environment (Development vs Production)
            // Check MONOBALL_ENVIRONMENT first, then fall back to ASPNETCORE_ENVIRONMENT for compatibility
            var environment =
                Environment.GetEnvironmentVariable("MONOBALL_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? (Debugger.IsAttached ? "Development" : "Production");

            // Console logging is expensive - restrict to Information level or higher
            // Debug logs will only go to file, not console
            const LogEventLevel consoleMinLevel = LogEventLevel.Information;

            // Check if console/debug sinks should be disabled for maximum performance
            // Set MONOBALL_DISABLE_CONSOLE_LOGGING=1 to disable console/debug sinks (file only)
            var disableConsoleLogging =
                Environment.GetEnvironmentVariable("MONOBALL_DISABLE_CONSOLE_LOGGING") == "1";

            var loggerConfiguration = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("MonoBall.Core.ECS.Systems", LogEventLevel.Debug)
                .MinimumLevel.Override("MonoBall.Core.Mods", LogEventLevel.Information)
                // Performance optimization: Minimize enrichers for high-frequency logging
                // Enrichers run synchronously BEFORE sink filtering, adding overhead even for filtered logs
                // Removed MachineName and ThreadId enrichers - they add overhead on every log call
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "MonoBall")
                .Enrich.WithProperty("Environment", environment)
                // Performance optimization: Wrap ALL sinks in a SINGLE async wrapper
                // This is more efficient than wrapping each sink individually - uses one background thread
                // instead of multiple threads, reducing overhead and improving throughput
                .WriteTo.Async(
                    sinkConfiguration =>
                    {
                        // Console sink: Only add if not disabled for performance
                        if (!disableConsoleLogging)
                        {
                            sinkConfiguration.Console(
                                outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                                theme: AnsiConsoleTheme.Code,
                                applyThemeToRedirectedOutput: true,
                                restrictedToMinimumLevel: consoleMinLevel
                            );
                            sinkConfiguration.Debug(
                                outputTemplate: "[{Timestamp:HH:mm:ss.fff}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                                restrictedToMinimumLevel: consoleMinLevel
                            );
                        }

                        // File sink: Always enabled for persistent logs
                        sinkConfiguration.File(
                            logFilePath,
                            rollingInterval: RollingInterval.Day,
                            retainedFileCountLimit: 30, // Keep 30 days of logs
                            fileSizeLimitBytes: 100 * 1024 * 1024, // 100 MB per file
                            rollOnFileSizeLimit: true,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
                            buffered: true // Enable file buffering for better I/O performance
                        );
                    },
                    100000 // Drop logs if buffer is full rather than blocking game thread
                )
                // ImGui log sink for debug panel (always enabled, lightweight when no panel connected)
                .WriteTo.Sink(new ImGuiLogSink());

            _logger = loggerConfiguration.CreateLogger();
            Log.Logger = _logger;
            _isConfigured = true;
        }
    }

    /// <summary>
    ///     Creates a contextual logger for the specified type.
    ///     Thread-safe: ensures logger is configured before creating contextual logger.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for.</typeparam>
    /// <returns>A logger with source context set to the type name.</returns>
    public static ILogger CreateLogger<T>()
    {
        if (!_isConfigured)
            ConfigureLogger();
        return Log.ForContext<T>();
    }

    /// <summary>
    ///     Closes and flushes the logger.
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.CloseAndFlush();
        _logger = null;
        _isConfigured = false;
    }
}
