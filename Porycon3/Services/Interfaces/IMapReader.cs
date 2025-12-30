using Porycon3.Models;

namespace Porycon3.Services.Interfaces;

/// <summary>
/// Interface for reading map JSON data.
/// </summary>
public interface IMapReader
{
    /// <summary>
    /// Read a map by name from the input directory.
    /// </summary>
    MapData ReadMap(string mapName);
}
