namespace MonoBall.Core.Diagnostics.Console.Commands;

using System;
using System.Linq;
using System.Threading.Tasks;
using Services;

/// <summary>
/// Built-in help command that lists available commands.
/// </summary>
[ConsoleCommand]
public sealed class HelpCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "help";

    /// <inheritdoc />
    public string Description => "Shows available commands or help for a specific command";

    /// <inheritdoc />
    public string Usage => "help [command]";

    /// <inheritdoc />
    public string Category => "General";

    /// <inheritdoc />
    public string[] Aliases => ["?", "h"];

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        if (args.Length > 0)
        {
            ShowCommandHelp(context, args[0]);
        }
        else
        {
            ShowAllCommands(context);
        }

        return Task.FromResult(true);
    }

    private void ShowCommandHelp(IConsoleContext ctx, string commandName)
    {
        if (ctx.CommandRegistry.TryGetCommand(commandName, out var command))
        {
            ctx.WriteSystem($"Command: {command!.Name}");
            ctx.WriteLine($"  {command.Description}");
            ctx.WriteLine($"  Usage: {command.Usage}");
            ctx.WriteLine($"  Category: {command.Category}");

            if (command.Aliases.Length > 0)
            {
                ctx.WriteLine($"  Aliases: {string.Join(", ", command.Aliases)}");
            }
        }
        else
        {
            ctx.WriteError($"Unknown command: {commandName}");
        }
    }

    private void ShowAllCommands(IConsoleContext ctx)
    {
        ctx.WriteSystem("Available Commands:");
        ctx.WriteLine(string.Empty);

        var categories = ctx.CommandRegistry.GetCommandsByCategory();

        foreach (var (category, commands) in categories)
        {
            ctx.WriteSystem($"── {category} ──");

            foreach (var cmd in commands)
            {
                var aliases =
                    cmd.Aliases.Length > 0 ? $" ({string.Join(", ", cmd.Aliases)})" : string.Empty;
                ctx.WriteLine($"  {cmd.Name, -15}{aliases, -12} {cmd.Description}");
            }

            ctx.WriteLine(string.Empty);
        }

        ctx.WriteSystem("Type 'help <command>' for detailed help.");
    }
}

/// <summary>
/// Built-in clear command that clears the console output.
/// </summary>
[ConsoleCommand]
public sealed class ClearCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "clear";

    /// <inheritdoc />
    public string Description => "Clears the console output";

    /// <inheritdoc />
    public string Usage => "clear";

    /// <inheritdoc />
    public string Category => "General";

    /// <inheritdoc />
    public string[] Aliases => ["cls"];

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        context.Clear();
        return Task.FromResult(true);
    }
}

/// <summary>
/// Built-in echo command that prints text to the console.
/// </summary>
[ConsoleCommand]
public sealed class EchoCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "echo";

    /// <inheritdoc />
    public string Description => "Prints text to the console";

    /// <inheritdoc />
    public string Usage => "echo <text>";

    /// <inheritdoc />
    public string Category => "General";

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        if (args.Length == 0)
        {
            context.WriteLine(string.Empty);
        }
        else
        {
            context.WriteLine(string.Join(" ", args));
        }

        return Task.FromResult(true);
    }
}

/// <summary>
/// Built-in history command that shows command history.
/// </summary>
[ConsoleCommand]
public sealed class HistoryCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "history";

    /// <inheritdoc />
    public string Description => "Shows command history";

    /// <inheritdoc />
    public string Usage => "history [search]";

    /// <inheritdoc />
    public string Category => "General";

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        // Get history from service - need to cast to access history
        if (context is IConsoleService service)
        {
            var query = args.Length > 0 ? args[0] : null;
            var history =
                query != null
                    ? service.History.Search(query).ToList()
                    : service.History.GetAll().Reverse().ToList();

            if (history.Count == 0)
            {
                context.WriteSystem(
                    query != null ? $"No commands matching '{query}'" : "No command history"
                );
            }
            else
            {
                context.WriteSystem($"Command History ({history.Count} entries):");
                var index = 1;
                foreach (var cmd in history.Take(20))
                {
                    context.WriteLine($"  {index++, 3}. {cmd}");
                }

                if (history.Count > 20)
                {
                    context.WriteSystem($"  ... and {history.Count - 20} more");
                }
            }
        }
        else
        {
            context.WriteWarning("History not available");
        }

        return Task.FromResult(true);
    }
}

/// <summary>
/// Built-in version command that shows version information.
/// </summary>
[ConsoleCommand]
public sealed class VersionCommand : IConsoleCommand
{
    /// <inheritdoc />
    public string Name => "version";

    /// <inheritdoc />
    public string Description => "Shows version information";

    /// <inheritdoc />
    public string Usage => "version";

    /// <inheritdoc />
    public string Category => "General";

    /// <inheritdoc />
    public string[] Aliases => ["ver"];

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(IConsoleContext context, string[] args)
    {
        context.WriteSystem("MonoBall Debug Console v1.0");
        context.WriteLine($"  Runtime: {Environment.Version}");
        context.WriteLine($"  OS: {Environment.OSVersion}");
        context.WriteLine($"  64-bit: {Environment.Is64BitProcess}");
        return Task.FromResult(true);
    }
}
