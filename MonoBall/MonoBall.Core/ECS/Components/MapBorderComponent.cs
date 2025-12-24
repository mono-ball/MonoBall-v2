using System.Linq;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores border tile data for a map.
    /// Borders are rendered when the camera extends beyond the map bounds.
    /// Uses Pokemon Emerald's 2x2 tiling pattern for infinite border rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Pokemon Emerald Border System:</b>
    /// Borders are defined as a 2x2 pattern of tile IDs:
    /// [0]=TopLeft, [1]=TopRight, [2]=BottomLeft, [3]=BottomRight
    /// Each tile has two layers: bottom (ground) and top (overhead/foliage).
    /// </para>
    /// <para>
    /// <b>Tiling Algorithm:</b>
    /// borderIndex = (x &amp; 1) + ((y &amp; 1) &lt;&lt; 1);
    /// This creates an infinite checkerboard pattern using the 4 border tiles.
    /// </para>
    /// <para>
    /// <b>Dual Layer Rendering:</b>
    /// Bottom layer tiles are rendered at ground elevation (tree trunks, grass).
    /// Top layer tiles are rendered at overhead elevation (tree canopy, rooftops).
    /// </para>
    /// </remarks>
    public struct MapBorderComponent
    {
        /// <summary>
        /// The 4 BOTTOM layer border tile GIDs in the order: TopLeft, TopRight, BottomLeft, BottomRight.
        /// </summary>
        public int[] BottomLayerGids { get; set; }

        /// <summary>
        /// The 4 TOP layer border tile GIDs in the order: TopLeft, TopRight, BottomLeft, BottomRight.
        /// </summary>
        public int[] TopLayerGids { get; set; }

        /// <summary>
        /// The tileset ID for border rendering.
        /// </summary>
        public string TilesetId { get; set; }

        /// <summary>
        /// Pre-calculated source rectangles for bottom layer tiles (4 elements).
        /// </summary>
        public Rectangle[] BottomSourceRects { get; set; }

        /// <summary>
        /// Pre-calculated source rectangles for top layer tiles (4 elements).
        /// Empty rectangles indicate no tile for that position.
        /// </summary>
        public Rectangle[] TopSourceRects { get; set; }

        /// <summary>
        /// Gets whether this component has valid border data.
        /// </summary>
        public bool HasBorder =>
            BottomLayerGids != null
            && BottomLayerGids.Length == 4
            && !string.IsNullOrEmpty(TilesetId);

        /// <summary>
        /// Gets whether this component has valid top layer border data.
        /// </summary>
        public bool HasTopLayer =>
            TopLayerGids != null && TopLayerGids.Length == 4 && TopLayerGids.Any(gid => gid > 0);

        /// <summary>
        /// Gets the border tile index for a given tile position using the 2x2 tiling pattern.
        /// </summary>
        /// <param name="x">The X coordinate (relative to map origin).</param>
        /// <param name="y">The Y coordinate (relative to map origin).</param>
        /// <returns>The border tile index (0-3): 0=TopLeft, 1=TopRight, 2=BottomLeft, 3=BottomRight.</returns>
        public static int GetBorderTileIndex(int x, int y)
        {
            return (x & 1) + ((y & 1) << 1);
        }
    }
}
