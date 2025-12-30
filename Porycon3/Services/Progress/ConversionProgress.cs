using System.Diagnostics;

namespace Porycon3.Services.Progress;

/// <summary>
/// Thread-safe progress state container for the entire conversion pipeline.
/// Designed for Spectre.Console Live rendering with minimal lock contention.
/// </summary>
public sealed class ConversionProgress
{
    private readonly object _lock = new();
    private readonly Stopwatch _totalTimer = Stopwatch.StartNew();

    // Phase tracking
    public ConversionPhase CurrentPhase { get; private set; } = ConversionPhase.Initializing;

    // Map conversion progress
    public int MapTotal { get; private set; }
    public int MapCompleted { get; private set; }
    public int MapFailed { get; private set; }
    private readonly List<string> _recentMaps = new(capacity: 8);

    // Tileset progress
    public int TilesetTotal { get; private set; }
    public int TilesetCompleted { get; private set; }

    // Extractor progress (keyed by extractor name)
    private readonly Dictionary<string, ExtractorProgressData> _extractors = new();

    // Thread-safe update methods
    public void SetPhase(ConversionPhase phase)
    {
        lock (_lock) CurrentPhase = phase;
    }

    public void SetMapTotal(int total)
    {
        lock (_lock) MapTotal = total;
    }

    public void IncrementMapCompleted(string mapName, bool success)
    {
        lock (_lock)
        {
            if (success) MapCompleted++;
            else MapFailed++;

            _recentMaps.Add(mapName);
            if (_recentMaps.Count > 8) _recentMaps.RemoveAt(0);
        }
    }

    public void SetTilesetTotal(int total)
    {
        lock (_lock) TilesetTotal = total;
    }

    public void IncrementTilesetCompleted()
    {
        lock (_lock) TilesetCompleted++;
    }

    public void SetTilesetCompleted(int completed)
    {
        lock (_lock) TilesetCompleted = completed;
    }

    public void RegisterExtractor(string name)
    {
        lock (_lock)
        {
            if (!_extractors.ContainsKey(name))
            {
                _extractors[name] = new ExtractorProgressData { Name = name, State = ExtractorState.Pending };
            }
        }
    }

    public void UpdateExtractor(string name, ExtractorState state, int? items = null, TimeSpan? elapsed = null)
    {
        lock (_lock)
        {
            if (!_extractors.TryGetValue(name, out var progress))
            {
                progress = new ExtractorProgressData { Name = name };
                _extractors[name] = progress;
            }

            progress.State = state;
            if (items.HasValue) progress.ItemCount = items.Value;
            if (elapsed.HasValue) progress.Elapsed = elapsed.Value;
        }
    }

    /// <summary>
    /// Get immutable snapshot for rendering (called from UI refresh loop).
    /// </summary>
    public ProgressSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return new ProgressSnapshot
            {
                Phase = CurrentPhase,
                TotalElapsed = _totalTimer.Elapsed,
                MapTotal = MapTotal,
                MapCompleted = MapCompleted,
                MapFailed = MapFailed,
                RecentMaps = _recentMaps.ToArray(),
                TilesetTotal = TilesetTotal,
                TilesetCompleted = TilesetCompleted,
                Extractors = _extractors.Values.Select(e => e.Clone()).ToArray()
            };
        }
    }
}

public enum ConversionPhase
{
    Initializing,
    ScanningMaps,
    ConvertingMaps,
    FinalizingTilesets,
    ExtractingAssets,
    Complete,
    Failed
}

public enum ExtractorState
{
    Pending,
    Running,
    Complete,
    Failed,
    Skipped
}

/// <summary>
/// Immutable snapshot of progress state for safe rendering.
/// </summary>
public record ProgressSnapshot
{
    public ConversionPhase Phase { get; init; }
    public TimeSpan TotalElapsed { get; init; }
    public int MapTotal { get; init; }
    public int MapCompleted { get; init; }
    public int MapFailed { get; init; }
    public string[] RecentMaps { get; init; } = Array.Empty<string>();
    public int TilesetTotal { get; init; }
    public int TilesetCompleted { get; init; }
    public ExtractorProgressData[] Extractors { get; init; } = Array.Empty<ExtractorProgressData>();
}

/// <summary>
/// Progress data for a single extractor.
/// </summary>
public class ExtractorProgressData
{
    public string Name { get; init; } = "";
    public ExtractorState State { get; set; }
    public int ItemCount { get; set; }
    public TimeSpan Elapsed { get; set; }

    public ExtractorProgressData Clone() => new()
    {
        Name = Name,
        State = State,
        ItemCount = ItemCount,
        Elapsed = Elapsed
    };
}
