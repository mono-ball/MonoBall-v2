namespace MonoBall.Core.Diagnostics.Console.Commands;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Events;
using Services;

/// <summary>
/// Performance threshold constants for consistent color coding.
/// </summary>
internal static class PerformanceThresholds
{
    public const float TargetFps = 60f;
    public const float MinimumAcceptableFps = 30f;
    public const float TargetFrameTimeMs = 1000f / TargetFps; // ~16.67ms
    public const float MaxFrameTimeMs = 1000f / MinimumAcceptableFps; // ~33.33ms
}

/// <summary>
/// Helper methods for debug commands.
/// </summary>
internal static class DebugCommandHelpers
{
    private static readonly Task<bool> TrueResult = Task.FromResult(true);
    private static readonly Task<bool> FalseResult = Task.FromResult(false);

    public static Task<bool> Success() => TrueResult;

    public static Task<bool> Failure() => FalseResult;

    public static ConsoleOutputLevel GetFpsOutputLevel(float fps) =>
        fps >= PerformanceThresholds.TargetFps ? ConsoleOutputLevel.Success
        : fps >= PerformanceThresholds.MinimumAcceptableFps ? ConsoleOutputLevel.Warning
        : ConsoleOutputLevel.Error;

    public static ConsoleOutputLevel GetFrameTimeOutputLevel(float frameTimeMs) =>
        frameTimeMs < PerformanceThresholds.TargetFrameTimeMs ? ConsoleOutputLevel.Success
        : frameTimeMs < PerformanceThresholds.MaxFrameTimeMs ? ConsoleOutputLevel.Warning
        : ConsoleOutputLevel.Error;

    public static bool TryGetTimeControl(IConsoleContext context, out ITimeControl? timeControl)
    {
        timeControl = context.TimeControl;
        if (timeControl == null)
        {
            context.WriteWarning("Time control not available.");
            return false;
        }
        return true;
    }
}

/// <summary>
/// Command to display performance statistics.
/// </summary>
[ConsoleCommand]
public sealed class StatsCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "stats";

    /// <inheritdoc />
    public string Description => "Display performance statistics";

    /// <inheritdoc />
    public string Usage => "stats [fps|frame|memory|gc|all]";

    /// <inheritdoc />
    public string Category => "Debug";

    /// <inheritdoc />
    public string[] Aliases => ["perf"];

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        var stats = context.PerformanceStats;
        if (stats == null)
        {
            context.WriteWarning("Performance stats not available.");
            return DebugCommandHelpers.Failure();
        }

        var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

        switch (mode)
        {
            case "fps":
                ShowFps(context, stats);
                break;

            case "frame":
                ShowFrameTime(context, stats);
                break;

            case "memory":
            case "mem":
                ShowMemory(context, stats);
                break;

            case "gc":
                ShowGc(context, stats);
                break;

            case "all":
            default:
                ShowAll(context, stats);
                break;
        }

        return DebugCommandHelpers.Success();
    }

    private static void ShowFps(IConsoleContext ctx, IPerformanceStats stats)
    {
        var fpsColor = DebugCommandHelpers.GetFpsOutputLevel(stats.Fps);

        ctx.WriteSystem("FPS Statistics:");
        ctx.WriteLine($"  Current FPS: {stats.Fps:F1}", fpsColor);
        ctx.WriteLine($"  Frame Time:  {stats.FrameTimeMs:F2} ms");
    }

    private static void ShowFrameTime(IConsoleContext ctx, IPerformanceStats stats)
    {
        var frameColor = DebugCommandHelpers.GetFrameTimeOutputLevel(stats.FrameTimeMs);

        ctx.WriteSystem("Frame Time:");
        ctx.WriteLine($"  Current: {stats.FrameTimeMs:F2} ms", frameColor);
        ctx.WriteLine($"  Target:  {PerformanceThresholds.TargetFrameTimeMs:F2} ms (60 FPS)");
    }

    private static void ShowMemory(IConsoleContext ctx, IPerformanceStats stats)
    {
        var memoryMb = stats.MemoryBytes / (1024.0 * 1024.0);

        ctx.WriteSystem("Memory Usage:");
        ctx.WriteLine($"  Managed Heap: {memoryMb:F2} MB");
        ctx.WriteLine($"  Entities:     {stats.EntityCount:N0}");
        ctx.WriteLine($"  Draw Calls:   {stats.DrawCalls:N0}");
    }

    private static void ShowGc(IConsoleContext ctx, IPerformanceStats stats)
    {
        ctx.WriteSystem("Garbage Collection:");
        ctx.WriteLine($"  Gen 0: {stats.GcGen0:N0} collections");
        ctx.WriteLine($"  Gen 1: {stats.GcGen1:N0} collections");
        ctx.WriteLine($"  Gen 2: {stats.GcGen2:N0} collections");
    }

    private static void ShowAll(IConsoleContext ctx, IPerformanceStats stats)
    {
        var fpsColor = DebugCommandHelpers.GetFpsOutputLevel(stats.Fps);
        var memoryMb = stats.MemoryBytes / (1024.0 * 1024.0);

        ctx.WriteSystem("--- Performance Statistics ---");

        ctx.WriteLine($"  FPS:          {stats.Fps:F1}", fpsColor);
        ctx.WriteLine($"  Frame Time:   {stats.FrameTimeMs:F2} ms");
        ctx.WriteLine($"  Memory:       {memoryMb:F2} MB");
        ctx.WriteLine($"  Entities:     {stats.EntityCount:N0}");
        ctx.WriteLine($"  Draw Calls:   {stats.DrawCalls:N0}");
        ctx.WriteLine($"  GC (0/1/2):   {stats.GcGen0}/{stats.GcGen1}/{stats.GcGen2}");
    }

    /// <inheritdoc />
    public IEnumerable<string> GetCompletions(string[] args, int argIndex)
    {
        if (argIndex == 0)
        {
            return ["fps", "frame", "memory", "gc", "all"];
        }
        return [];
    }
}

/// <summary>
/// Command to control game time (pause, resume, step, scale).
/// </summary>
[ConsoleCommand]
public sealed class TimeCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "time";

    /// <inheritdoc />
    public string Description => "Control game time (pause, resume, step, scale)";

    /// <inheritdoc />
    public string Usage => "time [pause|resume|toggle|step [n]|scale <value>]";

    /// <inheritdoc />
    public string Category => "Debug";

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        if (!DebugCommandHelpers.TryGetTimeControl(context, out var timeControl))
            return DebugCommandHelpers.Failure();

        if (args.Length == 0)
        {
            ShowStatus(context, timeControl!);
            return DebugCommandHelpers.Success();
        }

        var subCommand = args[0].ToLowerInvariant();

        switch (subCommand)
        {
            case "pause":
                timeControl!.Pause();
                context.WriteSuccess("Game paused.");
                break;

            case "resume":
                timeControl!.Resume();
                context.WriteSuccess("Game resumed.");
                break;

            case "toggle":
                var isPaused = timeControl!.Toggle();
                context.WriteSuccess(isPaused ? "Game paused." : "Game resumed.");
                break;

            case "step":
                var frames = 1;
                if (args.Length > 1 && int.TryParse(args[1], out var parsedFrames))
                {
                    frames = Math.Max(1, parsedFrames);
                }
                timeControl!.Step(frames);
                context.WriteSuccess($"Stepping {frames} frame(s).");
                break;

            case "scale":
                if (args.Length < 2)
                {
                    context.WriteLine($"Current time scale: {timeControl!.TimeScale:F2}x");
                }
                else if (float.TryParse(args[1], out var scale))
                {
                    timeControl!.TimeScale = scale;
                    context.WriteSuccess($"Time scale set to {timeControl.TimeScale:F2}x");
                }
                else
                {
                    context.WriteError($"Invalid scale value: {args[1]}");
                    return DebugCommandHelpers.Failure();
                }
                break;

            case "slowmo":
                if (args.Length < 2)
                {
                    var percent = (int)(timeControl!.TimeScale * 100);
                    context.WriteLine($"Current speed: {percent}%");
                }
                else if (int.TryParse(args[1], out var percentValue))
                {
                    timeControl!.TimeScale = percentValue / 100f;
                    context.WriteSuccess($"Speed set to {percentValue}%");
                }
                else
                {
                    context.WriteError($"Invalid percentage: {args[1]}");
                    return DebugCommandHelpers.Failure();
                }
                break;

            default:
                context.WriteError($"Unknown subcommand: {subCommand}");
                context.WriteSystem("Usage: " + Usage);
                return DebugCommandHelpers.Failure();
        }

        return DebugCommandHelpers.Success();
    }

    private static void ShowStatus(IConsoleContext ctx, ITimeControl timeControl)
    {
        ctx.WriteSystem("--- Time Control ---");

        var stateColor = timeControl.IsPaused
            ? ConsoleOutputLevel.Warning
            : ConsoleOutputLevel.Success;
        ctx.WriteLine($"  State:      {(timeControl.IsPaused ? "PAUSED" : "RUNNING")}", stateColor);
        ctx.WriteLine($"  Time Scale: {timeControl.TimeScale:F2}x");

        if (timeControl.PendingStepFrames > 0)
        {
            ctx.WriteLine($"  Pending:    {timeControl.PendingStepFrames} step frame(s)");
        }

        ctx.WriteSystem(
            "Commands: pause, resume, toggle, step [n], scale <value>, slowmo <percent>"
        );
    }

    /// <inheritdoc />
    public IEnumerable<string> GetCompletions(string[] args, int argIndex)
    {
        if (argIndex == 0)
        {
            return ["pause", "resume", "toggle", "step", "scale", "slowmo"];
        }
        return [];
    }
}

/// <summary>
/// Shortcut command to pause the game.
/// </summary>
[ConsoleCommand]
public sealed class PauseCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "pause";

    /// <inheritdoc />
    public string Description => "Pause the game";

    /// <inheritdoc />
    public string Usage => "pause";

    /// <inheritdoc />
    public string Category => "Debug";

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        if (!DebugCommandHelpers.TryGetTimeControl(context, out var timeControl))
            return DebugCommandHelpers.Failure();

        if (timeControl!.IsPaused)
        {
            context.WriteSystem("Game is already paused.");
        }
        else
        {
            timeControl.Pause();
            context.WriteSuccess("Game paused.");
        }

        return DebugCommandHelpers.Success();
    }
}

/// <summary>
/// Shortcut command to resume the game.
/// </summary>
[ConsoleCommand]
public sealed class ResumeCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "resume";

    /// <inheritdoc />
    public string Description => "Resume the game";

    /// <inheritdoc />
    public string Usage => "resume";

    /// <inheritdoc />
    public string Category => "Debug";

    /// <inheritdoc />
    public string[] Aliases => ["unpause"];

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        if (!DebugCommandHelpers.TryGetTimeControl(context, out var timeControl))
            return DebugCommandHelpers.Failure();

        if (!timeControl!.IsPaused)
        {
            context.WriteSystem("Game is already running.");
        }
        else
        {
            timeControl.Resume();
            context.WriteSuccess("Game resumed.");
        }

        return DebugCommandHelpers.Success();
    }
}

/// <summary>
/// Shortcut command to step frames when paused.
/// </summary>
[ConsoleCommand]
public sealed class StepCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "step";

    /// <inheritdoc />
    public string Description => "Step forward one or more frames when paused";

    /// <inheritdoc />
    public string Usage => "step [frames]";

    /// <inheritdoc />
    public string Category => "Debug";

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        if (!DebugCommandHelpers.TryGetTimeControl(context, out var timeControl))
            return DebugCommandHelpers.Failure();

        var frames = 1;
        if (args.Length > 0 && int.TryParse(args[0], out var parsedFrames))
        {
            frames = Math.Max(1, parsedFrames);
        }

        timeControl!.Step(frames);
        context.WriteSuccess($"Stepping {frames} frame(s).");

        return DebugCommandHelpers.Success();
    }
}
