using Porycon3.Models;
using Porycon3.Services.Extraction;

namespace Porycon3.Services.Interfaces;

/// <summary>
/// Interface for map conversion operations.
/// </summary>
public interface IMapConversionService
{
    /// <summary>
    /// Scan for available maps in the input directory.
    /// </summary>
    List<string> ScanMaps();

    /// <summary>
    /// Convert a single map by name.
    /// </summary>
    ConversionResult ConvertMap(string mapName);

    /// <summary>
    /// Finalize all shared tilesets after map conversion.
    /// </summary>
    int FinalizeSharedTilesets();

    /// <summary>
    /// Generate additional definitions (Weather, BattleScenes, Region, etc.).
    /// </summary>
    Dictionary<string, ExtractionResult> GenerateDefinitions();

    /// <summary>
    /// Get all extractors for external orchestration.
    /// </summary>
    IEnumerable<IExtractor> GetExtractors();

    /// <summary>
    /// Run the definition generator directly.
    /// </summary>
    void RunDefinitionGenerator();
}
