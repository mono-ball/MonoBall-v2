using Porycon3.Models;

namespace Porycon3.Services.Interfaces;

/// <summary>
/// Interface for reading metatile binary data.
/// </summary>
public interface IMetatileReader
{
    /// <summary>
    /// Read metatiles from a tileset.
    /// </summary>
    List<Metatile> ReadMetatiles(string tilesetName);
}
