using Porycon3.Services.Progress;

namespace Porycon3.Services.Extraction;

/// <summary>
/// Orchestrator that reports progress to external ConversionProgress instead of managing its own display.
/// Used for unified UI with a single Live context.
/// </summary>
public class ProgressAwareOrchestrator
{
    private readonly List<(IExtractor Extractor, string DisplayName)> _extractors = new();
    private readonly ConversionProgress _progress;
    private readonly bool _verbose;

    public ProgressAwareOrchestrator(ConversionProgress progress, bool verbose = false)
    {
        _progress = progress;
        _verbose = verbose;
    }

    /// <summary>
    /// Add an extractor to be orchestrated.
    /// </summary>
    public ProgressAwareOrchestrator Add(IExtractor extractor, string? displayName = null)
    {
        var name = displayName ?? extractor.Name;
        _extractors.Add((extractor, name));
        _progress.RegisterExtractor(name);
        return this;
    }

    /// <summary>
    /// Run all extractors sequentially, reporting progress externally.
    /// No internal Spectre context - caller manages the Live display.
    /// </summary>
    public Dictionary<string, ExtractionResult> RunAll(CancellationToken ct = default)
    {
        var results = new Dictionary<string, ExtractionResult>();

        foreach (var (extractor, name) in _extractors)
        {
            if (ct.IsCancellationRequested)
            {
                _progress.UpdateExtractor(name, ExtractorState.Skipped);
                continue;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            _progress.UpdateExtractor(name, ExtractorState.Running);

            try
            {
                // Run the extractor in quiet mode (no internal display)
                extractor.QuietMode = true;
                var result = extractor.ExtractAll();
                results[name] = result;

                sw.Stop();
                var state = result.Success ? ExtractorState.Complete : ExtractorState.Failed;
                _progress.UpdateExtractor(name, state, result.ItemCount, sw.Elapsed);
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                _progress.UpdateExtractor(name, ExtractorState.Skipped, elapsed: sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _progress.UpdateExtractor(name, ExtractorState.Failed, elapsed: sw.Elapsed);

                results[name] = new ExtractionResult
                {
                    ExtractorName = name,
                    Success = false,
                    ItemCount = 0,
                    Duration = sw.Elapsed,
                    Errors = new List<ExtractionError>
                    {
                        new(name, ex.Message, ex)
                    }
                };
            }
        }

        return results;
    }

    /// <summary>
    /// Get summary of results for final display.
    /// </summary>
    public static (int TotalItems, int SuccessCount, int FailCount, TimeSpan TotalDuration)
        GetSummary(Dictionary<string, ExtractionResult> results)
    {
        var totalItems = results.Values.Sum(r => r.ItemCount);
        var totalDuration = TimeSpan.FromTicks(results.Values.Sum(r => r.Duration.Ticks));
        var successCount = results.Values.Count(r => r.Success);
        var failCount = results.Values.Count(r => !r.Success);

        return (totalItems, successCount, failCount, totalDuration);
    }
}
