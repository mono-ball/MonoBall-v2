using Arch.Core;
using Microsoft.Xna.Framework;
using MonoBall.Core.Rendering;

namespace MonoBall.Core.ECS.Components;

/// <summary>
///     Camera component for viewport and world-to-screen transformations.
///     This is a pure data component following ECS best practices.
///     All camera logic (following, zooming, etc.) is handled by dedicated systems.
/// </summary>
/// <remarks>
///     <para>
///         <b>Architecture:</b>
///         - Camera: Pure data component (this struct)
///         - CameraSystem: Handles camera updates, following, and bounds enforcement
///         - MapRendererSystem: Uses camera for culling and transform matrix
///     </para>
///     <para>
///         This component operates in TILE coordinates, not pixel coordinates.
///         Tile coordinates are converted to pixel coordinates using TileWidth/TileHeight (typically 16x16).
///     </para>
/// </remarks>
public struct CameraComponent
{
    /// <summary>
    ///     Minimum zoom level allowed (prevents excessive zoom out).
    /// </summary>
    public const float MinZoom = 0.1f;

    /// <summary>
    ///     Maximum zoom level allowed (prevents excessive zoom in).
    /// </summary>
    public const float MaxZoom = 10.0f;

    /// <summary>
    ///     Default tile width in pixels (used for coordinate conversion).
    /// </summary>
    public const int DefaultTileWidth = 16;

    /// <summary>
    ///     Default tile height in pixels (used for coordinate conversion).
    /// </summary>
    public const int DefaultTileHeight = 16;

    /// <summary>
    ///     Gets or sets the camera position in TILE coordinates (center point).
    /// </summary>
    public Vector2 Position { get; set; }

    /// <summary>
    ///     Gets or sets the camera zoom level (1.0 = normal, 2.0 = 2x zoom).
    ///     Automatically clamped between MinZoom and MaxZoom.
    /// </summary>
    public float Zoom
    {
        get => _zoom;
        set => _zoom = MathHelper.Clamp(value, MinZoom, MaxZoom);
    }

    private float _zoom;

    /// <summary>
    ///     Gets or sets the camera rotation in radians (clockwise).
    ///     Use for camera shake effects, isometric views, or cinematic angles.
    /// </summary>
    public float Rotation { get; set; }

    /// <summary>
    ///     Gets or sets the viewport rectangle for rendering bounds (in pixels).
    ///     This is the actual rendering area within the window.
    /// </summary>
    public Rectangle Viewport { get; set; }

    /// <summary>
    ///     Gets or sets the virtual viewport rectangle (viewport with borders applied).
    ///     This is the actual rendering area within the window, accounting for letterboxing/pillarboxing.
    /// </summary>
    public Rectangle VirtualViewport { get; set; }

    /// <summary>
    ///     Gets or sets the reference (target) width for aspect ratio calculation (in pixels).
    ///     This is the initial/desired width that the camera should maintain when resizing.
    /// </summary>
    public int ReferenceWidth { get; set; }

    /// <summary>
    ///     Gets or sets the reference (target) height for aspect ratio calculation (in pixels).
    ///     This is the initial/desired height that the camera should maintain when resizing.
    /// </summary>
    public int ReferenceHeight { get; set; }

    /// <summary>
    ///     Gets or sets the tile width in pixels (for coordinate conversion).
    /// </summary>
    public int TileWidth { get; set; }

    /// <summary>
    ///     Gets or sets the tile height in pixels (for coordinate conversion).
    /// </summary>
    public int TileHeight { get; set; }

    /// <summary>
    ///     Gets or sets the map bounds for camera clamping (in TILE coordinates).
    ///     Prevents the camera from showing areas outside the map.
    ///     Set to Rectangle.Empty to disable bounds checking.
    /// </summary>
    public Rectangle MapBounds { get; set; }

    /// <summary>
    ///     Gets or sets the target position for camera following (in TILE coordinates).
    ///     When set, the camera will smoothly follow this position.
    ///     Set to null to disable following.
    /// </summary>
    public Vector2? FollowTarget { get; set; }

    /// <summary>
    ///     Gets or sets the entity to follow. When set, the camera will follow this entity's position each frame.
    ///     Entity-based following takes precedence over position-based following (FollowTarget).
    ///     Set to null to disable entity following.
    /// </summary>
    /// <remarks>
    ///     The entity must have a PositionComponent. The camera system validates this each frame.
    ///     If the entity is destroyed or missing PositionComponent, following is automatically stopped.
    /// </remarks>
    public Entity? FollowEntity { get; set; }

    /// <summary>
    ///     Gets or sets whether camera following is locked (disabled).
    ///     When true, the camera will not follow FollowEntity, allowing manual camera control.
    ///     Useful for cutscenes, map transitions, or other scenarios requiring camera override.
    /// </summary>
    public bool IsFollowingLocked { get; set; }

    /// <summary>
    ///     Gets or sets the camera smoothing speed (0 = instant, 1 = very smooth).
    ///     Lower values = faster response, higher values = smoother motion.
    ///     Recommended: 0.1-0.3 for responsive feel, 0.5-0.8 for cinematic feel.
    /// </summary>
    public float SmoothingSpeed { get; set; }

    /// <summary>
    ///     Whether this camera is active (only active cameras render).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    ///     Indicates whether the camera transform needs to be recalculated.
    ///     Set to true when Position, Zoom, or Rotation changes.
    ///     Reset to false after transform is calculated.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>
    ///     Initializes a new instance of the CameraComponent struct with default values.
    /// </summary>
    public CameraComponent()
    {
        Position = Vector2.Zero;
        _zoom = 1.0f;
        Rotation = 0f;
        Viewport = Rectangle.Empty;
        VirtualViewport = Rectangle.Empty;
        ReferenceWidth = 0;
        ReferenceHeight = 0;
        TileWidth = DefaultTileWidth;
        TileHeight = DefaultTileHeight;
        MapBounds = Rectangle.Empty;
        FollowTarget = null;
        FollowEntity = null;
        IsFollowingLocked = false;
        SmoothingSpeed = 0.1f; // Default smoothing speed (matches DefaultCameraSmoothingSpeed constant)
        IsActive = true;
        IsDirty = true;
    }

    /// <summary>
    ///     Gets the camera's bounding rectangle in TILE coordinates.
    ///     Useful for culling and intersection tests.
    /// </summary>
    /// <remarks>
    ///     The viewport shows a fixed number of tiles based on the reference resolution.
    ///     With GBA resolution (240x160) and 16x16 tiles, this is 15x10 tiles.
    ///     Zoom = scale means: Viewport pixels / Zoom = reference pixels (240x160)
    /// </remarks>
    public RectangleF BoundingRectangle => CameraTransformUtility.GetBoundingRectangle(this);

    /// <summary>
    ///     Gets the transformation matrix for this camera.
    ///     Includes position, rotation, zoom, and viewport centering.
    ///     Rounds the camera position to prevent sub-pixel rendering artifacts (texture bleeding between tiles).
    /// </summary>
    public Matrix GetTransformMatrix()
    {
        return CameraTransformUtility.GetTransformMatrix(this);
    }

    /// <summary>
    ///     Converts screen coordinates to tile coordinates.
    ///     Useful for mouse/touch input handling and click detection.
    /// </summary>
    /// <param name="screenPosition">Position in screen space (pixels).</param>
    /// <returns>Position in tile coordinates.</returns>
    public Vector2 ScreenToTile(Vector2 screenPosition)
    {
        return CameraTransformUtility.ScreenToTile(this, screenPosition);
    }

    /// <summary>
    ///     Converts tile coordinates to screen coordinates.
    ///     Useful for UI positioning and debug rendering.
    /// </summary>
    /// <param name="tilePosition">Position in tile coordinates.</param>
    /// <returns>Position in screen space (pixels).</returns>
    public Vector2 TileToScreen(Vector2 tilePosition)
    {
        return CameraTransformUtility.TileToScreen(this, tilePosition);
    }

    /// <summary>
    ///     Gets the camera's world view bounds as an integer Rectangle in TILE coordinates.
    ///     Useful for tile-based culling and rendering optimization.
    /// </summary>
    /// <returns>A rectangle representing the visible tile area.</returns>
    public Rectangle GetTileViewBounds()
    {
        return CameraTransformUtility.GetTileViewBounds(this);
    }

    /// <summary>
    ///     Clamps the camera position to prevent showing areas outside the map.
    /// </summary>
    /// <param name="position">The desired camera position in tile coordinates.</param>
    /// <returns>The clamped camera position in tile coordinates.</returns>
    public Vector2 ClampPositionToMapBounds(Vector2 position)
    {
        return CameraTransformUtility.ClampPositionToMapBounds(this, position);
    }
}

/// <summary>
///     Helper struct for float-based rectangles (used for camera bounds).
/// </summary>
public struct RectangleF
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public RectangleF(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public Rectangle ToRectangle()
    {
        return new Rectangle((int)X, (int)Y, (int)Width, (int)Height);
    }
}
