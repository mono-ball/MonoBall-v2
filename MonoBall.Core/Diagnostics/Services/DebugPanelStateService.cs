namespace MonoBall.Core.Diagnostics.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

/// <summary>
/// Service for persisting debug panel visibility state across sessions.
/// </summary>
public sealed class DebugPanelStateService : IDisposable
{
    private const string StateFileName = "debug_panels.json";
    private const string AppDataFolderName = "MonoBall";

    private static readonly ILogger Logger = Log.ForContext<DebugPanelStateService>();

    private readonly IDebugPanelRegistry _registry;
    private readonly string _statePath;
    private bool _disposed;

    /// <summary>
    /// Gets or sets whether auto-save is enabled.
    /// </summary>
    public bool AutoSaveEnabled { get; set; } = true;

    /// <summary>
    /// Initializes the panel state service.
    /// </summary>
    /// <param name="registry">The panel registry to manage.</param>
    /// <param name="stateDirectory">Optional directory for state file. Uses app data if null.</param>
    /// <exception cref="ArgumentNullException">Thrown when registry is null.</exception>
    public DebugPanelStateService(IDebugPanelRegistry registry, string? stateDirectory = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _statePath = BuildStatePath(stateDirectory);
    }

    /// <summary>
    /// Loads panel states from disk and applies them to registered panels.
    /// </summary>
    /// <returns>True if state was loaded successfully, false otherwise.</returns>
    public bool LoadState()
    {
        ThrowIfDisposed();

        if (!File.Exists(_statePath))
        {
            Logger.Debug("No panel state file found at {Path}", _statePath);
            return false;
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            var state = JsonSerializer.Deserialize<PanelStateData>(json);

            if (state?.PanelVisibility == null)
            {
                Logger.Warning("Panel state file was empty or invalid");
                return false;
            }

            var appliedCount = 0;
            foreach (var (panelId, isVisible) in state.PanelVisibility)
            {
                if (_registry.SetPanelVisibility(panelId, isVisible))
                {
                    appliedCount++;
                }
            }

            Logger.Debug("Loaded panel state: {Count} panels restored", appliedCount);
            return true;
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to parse panel state file");
            return false;
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "Failed to read panel state file");
            return false;
        }
    }

    /// <summary>
    /// Saves current panel states to disk.
    /// </summary>
    /// <returns>True if state was saved successfully, false otherwise.</returns>
    public bool SaveState()
    {
        ThrowIfDisposed();

        try
        {
            EnsureDirectoryExists();

            var state = new PanelStateData { PanelVisibility = new Dictionary<string, bool>() };

            foreach (var panel in _registry.Panels)
            {
                state.PanelVisibility[panel.Id] = panel.IsVisible;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(state, options);
            File.WriteAllText(_statePath, json);

            Logger.Debug("Saved panel state: {Count} panels", state.PanelVisibility.Count);
            return true;
        }
        catch (JsonException ex)
        {
            Logger.Warning(ex, "Failed to serialize panel state");
            return false;
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "Failed to write panel state file");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning(ex, "No permission to write panel state file");
            return false;
        }
    }

    /// <summary>
    /// Resets all panels to their default visibility (hidden).
    /// Does not automatically save - call SaveState if persistence is needed.
    /// </summary>
    public void ResetToDefaults()
    {
        ThrowIfDisposed();

        foreach (var panel in _registry.Panels)
        {
            panel.IsVisible = false;
        }

        Logger.Debug("Reset all panels to default visibility");
    }

    /// <summary>
    /// Deletes the saved state file.
    /// </summary>
    /// <returns>True if file was deleted, false otherwise.</returns>
    public bool DeleteStateFile()
    {
        ThrowIfDisposed();

        try
        {
            if (!File.Exists(_statePath))
            {
                return false;
            }

            File.Delete(_statePath);
            Logger.Debug("Deleted panel state file");
            return true;
        }
        catch (IOException ex)
        {
            Logger.Warning(ex, "Failed to delete panel state file");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.Warning(ex, "No permission to delete panel state file");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        if (AutoSaveEnabled)
        {
            SaveState();
        }

        _disposed = true;
    }

    private static string BuildStatePath(string? stateDirectory)
    {
        if (!string.IsNullOrEmpty(stateDirectory))
        {
            return Path.Combine(stateDirectory, StateFileName);
        }

        // Use local app data for persistence
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, AppDataFolderName, StateFileName);
    }

    private void EnsureDirectoryExists()
    {
        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Serializable panel state data.
    /// </summary>
    private sealed class PanelStateData
    {
        public Dictionary<string, bool>? PanelVisibility { get; set; }
    }
}
