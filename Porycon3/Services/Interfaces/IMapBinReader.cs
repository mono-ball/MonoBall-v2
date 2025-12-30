namespace Porycon3.Services.Interfaces;

/// <summary>
/// Interface for reading map binary data (metatile indices).
/// </summary>
public interface IMapBinReader
{
    /// <summary>
    /// Read map binary containing metatile indices.
    /// </summary>
    ushort[] ReadMapBin(string layoutId, int width, int height, string? blockdataPath = null);
}
