using Microsoft.Xna.Framework;

namespace MonoBall.Core.ECS.Components
{
    /// <summary>
    /// Component that stores world position for entities in both grid and pixel coordinates.
    /// Grid coordinates are used for logical positioning, while pixel coordinates
    /// are used for smooth interpolated rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This component has been enhanced to support tile-based movement while maintaining
    /// backward compatibility with existing code that uses the Position property.
    /// </para>
    /// <para>
    /// Map identifier is stored in MapComponent, not in this component, to avoid redundancy.
    /// </para>
    /// </remarks>
    public struct PositionComponent
    {
        /// <summary>
        /// Gets or sets the X grid coordinate (tile-based, 16x16 pixels per tile).
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Gets or sets the Y grid coordinate (tile-based, 16x16 pixels per tile).
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Gets or sets the interpolated pixel X position for smooth rendering.
        /// </summary>
        public float PixelX { get; set; }

        /// <summary>
        /// Gets or sets the interpolated pixel Y position for smooth rendering.
        /// </summary>
        public float PixelY { get; set; }

        /// <summary>
        /// Gets or sets the world position in pixels (backward compatibility property).
        /// This property maintains compatibility with existing code that uses Position.
        /// Setting this property automatically syncs pixel coordinates to grid coordinates.
        /// </summary>
        public Vector2 Position
        {
            get => new Vector2(PixelX, PixelY);
            set
            {
                PixelX = value.X;
                PixelY = value.Y;
                SyncPixelsToGrid();
            }
        }

        /// <summary>
        /// Syncs grid coordinates from pixel coordinates.
        /// Does NOT snap pixel coordinates - maintains smooth interpolation during movement.
        /// Matches oldmonoball behavior.
        /// </summary>
        /// <param name="tileSize">The tile size in pixels (default: 16).</param>
        public void SyncPixelsToGrid(int tileSize = 16)
        {
            X = (int)(PixelX / tileSize);
            Y = (int)(PixelY / tileSize);
            // NOTE: Do NOT snap PixelX/PixelY - this breaks smooth movement interpolation
            // Old MonoBall didn't snap pixels in its sync method
        }
    }
}
