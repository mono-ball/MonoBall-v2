namespace MonoBall.Core.Diagnostics.Console.Commands;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Features;
using Serilog;
using Services;

/// <summary>
/// Registry that discovers and manages console commands.
/// </summary>
public sealed class ConsoleCommandRegistry : IConsoleCommandRegistry
{
    private static readonly ILogger Logger = Log.ForContext<ConsoleCommandRegistry>();

    private readonly Dictionary<string, IConsoleCommand> _commands = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets all registered commands.
    /// </summary>
    public IReadOnlyDictionary<string, IConsoleCommand> Commands => _commands;

    /// <summary>
    /// Initializes a new command registry and discovers commands.
    /// </summary>
    /// <param name="autoDiscover">Whether to auto-discover commands with [ConsoleCommand] attribute.</param>
    public ConsoleCommandRegistry(bool autoDiscover = true)
    {
        if (autoDiscover)
        {
            DiscoverCommands();
        }
    }

    /// <summary>
    /// Registers a command instance.
    /// </summary>
    /// <param name="command">The command to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when command is null.</exception>
    public void RegisterCommand(IConsoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (_commands.ContainsKey(command.Name))
        {
            Logger.Warning("Command '{Name}' is already registered, overwriting", command.Name);
        }

        _commands[command.Name] = command;

        // Register aliases
        foreach (var alias in command.Aliases)
        {
            if (_aliases.ContainsKey(alias))
            {
                Logger.Warning("Alias '{Alias}' is already registered, overwriting", alias);
            }
            _aliases[alias] = command.Name;
        }

        Logger.Debug(
            "Registered command: {Name} (aliases: {Aliases})",
            command.Name,
            string.Join(", ", command.Aliases)
        );
    }

    /// <summary>
    /// Unregisters a command by name.
    /// </summary>
    public bool UnregisterCommand(string name)
    {
        if (!_commands.TryGetValue(name, out var command))
            return false;

        _commands.Remove(name);

        // Remove aliases
        foreach (var alias in command.Aliases)
        {
            _aliases.Remove(alias);
        }

        return true;
    }

    /// <summary>
    /// Tries to get a command by name or alias.
    /// </summary>
    public bool TryGetCommand(string nameOrAlias, out IConsoleCommand? command)
    {
        // Try direct name first
        if (_commands.TryGetValue(nameOrAlias, out command))
            return true;

        // Try alias
        if (_aliases.TryGetValue(nameOrAlias, out var canonicalName))
        {
            return _commands.TryGetValue(canonicalName, out command);
        }

        command = null;
        return false;
    }

    /// <summary>
    /// Gets commands grouped by category.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IConsoleCommand>> GetCommandsByCategory()
    {
        return _commands
            .Values.GroupBy(cmd => cmd.Category)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IConsoleCommand>)g.OrderBy(c => c.Name).ToList()
            );
    }

    /// <summary>
    /// Gets completion suggestions for partial command input.
    /// </summary>
    public IEnumerable<string> GetCompletions(string partial)
    {
        return GetRichCompletions(partial).Select(c => c.Text);
    }

    /// <summary>
    /// Gets rich completion suggestions with descriptions for partial command input.
    /// </summary>
    public IEnumerable<CompletionItem> GetRichCompletions(string partial)
    {
        var results = new List<CompletionItem>();

        if (string.IsNullOrEmpty(partial))
        {
            // Return all commands with descriptions
            foreach (var cmd in _commands.Values.OrderBy(c => c.Name))
            {
                results.Add(CompletionItem.Command(cmd.Name, cmd.Description));
            }
            return results;
        }

        // Match command names
        foreach (
            var cmd in _commands
                .Values.Where(c => c.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name)
        )
        {
            results.Add(CompletionItem.Command(cmd.Name, cmd.Description));
        }

        // Match aliases (show as the alias but include command description)
        foreach (
            var kvp in _aliases
                .Where(a => a.Key.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                .OrderBy(a => a.Key)
        )
        {
            if (_commands.TryGetValue(kvp.Value, out var cmd))
            {
                // Don't add if we already have this command by its main name
                if (!results.Any(r => r.Text.Equals(kvp.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(
                        new CompletionItem(kvp.Key, $"→ {cmd.Name}: {cmd.Description}", "alias")
                    );
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Automatically discovers and registers commands with [ConsoleCommand] attribute.
    /// </summary>
    private void DiscoverCommands()
    {
        var commandTypes = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.GetCustomAttribute<ConsoleCommandAttribute>() != null)
            .Where(t => typeof(IConsoleCommand).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(t => t.GetCustomAttribute<ConsoleCommandAttribute>()!.Enabled);

        foreach (var type in commandTypes)
        {
            try
            {
                var command = (IConsoleCommand)Activator.CreateInstance(type)!;
                RegisterCommand(command);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to register command type: {TypeName}", type.Name);
            }
        }

        Logger.Information("Discovered and registered {Count} console commands", _commands.Count);
    }
}
