using System.Text.Json;
using Serilog;

namespace MonoBall.Core.Mods.TypeInference;

/// <summary>
/// Context object for type inference (reduces parameter count).
/// </summary>
public struct TypeInferenceContext
{
    public string FilePath { get; set; }
    public string NormalizedPath { get; set; }
    public JsonDocument? JsonDocument { get; set; }
    public ModManifest Mod { get; set; }
    public ILogger Logger { get; set; }
}
