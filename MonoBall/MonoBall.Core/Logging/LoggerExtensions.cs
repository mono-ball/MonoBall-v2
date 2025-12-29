using System;
using System.Linq;
using Serilog;
using Serilog.Events;

namespace MonoBall.Core.Logging;

/// <summary>
///     Extension methods for Serilog ILogger to enable high-performance conditional logging.
/// </summary>
/// <remarks>
///     <para>
///         IMPORTANT: Even with async sinks, Serilog evaluates all parameters synchronously BEFORE
///         checking if logging is enabled. This means expensive operations in log parameters will
///         always execute, even when logging is disabled.
///     </para>
///     <para>
///         Use these extension methods in hot paths (like Update loops) to avoid expensive
///         parameter evaluation when logging is disabled.
///     </para>
///     <para>
///         Example:
///         <code>
/// // ❌ BAD: expensiveMethod() always runs, even if Debug logging is disabled
/// _logger.Debug("Value: {Value}", expensiveMethod());
///
/// // ✅ GOOD: expensiveMethod() only runs if Debug logging is enabled
/// _logger.DebugIfEnabled("Value: {Value}", () => expensiveMethod());
/// </code>
///     </para>
/// </remarks>
public static class LoggerExtensions
{
    /// <summary>
    ///     Logs a debug message only if Debug level logging is enabled.
    ///     Parameters are evaluated lazily (only if logging is enabled).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">Lazy-evaluated arguments (functions that return the actual values).</param>
    public static void DebugIfEnabled(
        this ILogger logger,
        string messageTemplate,
        params Func<object>[] args
    )
    {
        if (logger.IsEnabled(LogEventLevel.Debug))
        {
            var evaluatedArgs = args.Select(f => f()).ToArray();
            logger.Debug(messageTemplate, evaluatedArgs);
        }
    }

    /// <summary>
    ///     Logs a debug message only if Debug level logging is enabled.
    ///     Parameters are evaluated lazily (only if logging is enabled).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="propertyValue">A single lazy-evaluated property value.</param>
    public static void DebugIfEnabled<T>(
        this ILogger logger,
        string messageTemplate,
        Func<T> propertyValue
    )
    {
        if (logger.IsEnabled(LogEventLevel.Debug))
            logger.Debug(messageTemplate, propertyValue());
    }

    /// <summary>
    ///     Logs a debug message only if Debug level logging is enabled.
    ///     Parameters are evaluated lazily (only if logging is enabled).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="propertyValue0">First lazy-evaluated property value.</param>
    /// <param name="propertyValue1">Second lazy-evaluated property value.</param>
    public static void DebugIfEnabled<T0, T1>(
        this ILogger logger,
        string messageTemplate,
        Func<T0> propertyValue0,
        Func<T1> propertyValue1
    )
    {
        if (logger.IsEnabled(LogEventLevel.Debug))
            logger.Debug(messageTemplate, propertyValue0(), propertyValue1());
    }

    /// <summary>
    ///     Logs a debug message only if Debug level logging is enabled.
    ///     Parameters are evaluated lazily (only if logging is enabled).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="propertyValue0">First lazy-evaluated property value.</param>
    /// <param name="propertyValue1">Second lazy-evaluated property value.</param>
    /// <param name="propertyValue2">Third lazy-evaluated property value.</param>
    public static void DebugIfEnabled<T0, T1, T2>(
        this ILogger logger,
        string messageTemplate,
        Func<T0> propertyValue0,
        Func<T1> propertyValue1,
        Func<T2> propertyValue2
    )
    {
        if (logger.IsEnabled(LogEventLevel.Debug))
            logger.Debug(messageTemplate, propertyValue0(), propertyValue1(), propertyValue2());
    }

    /// <summary>
    ///     Logs a debug message only if Debug level logging is enabled.
    ///     Parameters are evaluated lazily (only if logging is enabled).
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="propertyValue0">First lazy-evaluated property value.</param>
    /// <param name="propertyValue1">Second lazy-evaluated property value.</param>
    /// <param name="propertyValue2">Third lazy-evaluated property value.</param>
    /// <param name="propertyValue3">Fourth lazy-evaluated property value.</param>
    public static void DebugIfEnabled<T0, T1, T2, T3>(
        this ILogger logger,
        string messageTemplate,
        Func<T0> propertyValue0,
        Func<T1> propertyValue1,
        Func<T2> propertyValue2,
        Func<T3> propertyValue3
    )
    {
        if (logger.IsEnabled(LogEventLevel.Debug))
            logger.Debug(
                messageTemplate,
                propertyValue0(),
                propertyValue1(),
                propertyValue2(),
                propertyValue3()
            );
    }
}
