using System;
using Microsoft.Xna.Framework;
using MonoBall.Core.ECS.Components;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Utility class for camera transform calculations and coordinate conversions.
    /// Extracted from CameraComponent to follow ECS principles (components = data, utilities = logic).
    /// </summary>
    public static class CameraTransformUtility
    {
        /// <summary>
        /// Gets the transformation matrix for a camera.
        /// Includes position, rotation, zoom, and viewport centering.
        /// Rounds the camera position to prevent sub-pixel rendering artifacts (texture bleeding between tiles).
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <returns>The transformation matrix.</returns>
        public static Matrix GetTransformMatrix(CameraComponent camera)
        {
            // Defensive check: If viewport is not initialized, return identity matrix
            if (camera.Viewport.Width == 0 || camera.Viewport.Height == 0)
            {
                return Matrix.Identity;
            }

            // Convert tile position to pixel position
            float pixelX = camera.Position.X * camera.TileWidth;
            float pixelY = camera.Position.Y * camera.TileHeight;

            // Round camera position to nearest pixel after zoom to prevent texture bleeding/seams
            // This ensures tiles always render at integer screen coordinates
            float roundedX = MathF.Round(pixelX * camera.Zoom) / camera.Zoom;
            float roundedY = MathF.Round(pixelY * camera.Zoom) / camera.Zoom;

            // Use Viewport width/height for centering (VirtualViewport is same size, just offset)
            float centerX = camera.Viewport.Width / 2f;
            float centerY = camera.Viewport.Height / 2f;

            return Matrix.CreateTranslation(-roundedX, -roundedY, 0)
                * Matrix.CreateRotationZ(camera.Rotation)
                * Matrix.CreateScale(camera.Zoom, camera.Zoom, 1)
                * Matrix.CreateTranslation(centerX, centerY, 0);
        }

        /// <summary>
        /// Calculates the viewport scale factor from a camera component.
        /// The scale represents how many times larger the viewport is compared to the reference resolution.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <param name="referenceWidth">The reference width (e.g., GameConstants.GbaReferenceWidth).</param>
        /// <returns>The scale factor (1 = 1x, 2 = 2x, etc.). Returns 1 if viewport is not initialized.</returns>
        public static int GetViewportScale(CameraComponent camera, int referenceWidth)
        {
            if (referenceWidth <= 0)
            {
                return 1; // Invalid reference width, return default scale
            }

            // Use VirtualViewport if available (accounts for letterboxing), otherwise fall back to Viewport
            int viewportWidth =
                camera.VirtualViewport != Rectangle.Empty
                    ? camera.VirtualViewport.Width
                    : camera.Viewport.Width;

            if (viewportWidth <= 0)
            {
                return 1; // Viewport not initialized, return default scale
            }

            return viewportWidth / referenceWidth;
        }

        /// <summary>
        /// Converts screen coordinates to tile coordinates.
        /// Useful for mouse/touch input handling and click detection.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <param name="screenPosition">Position in screen space (pixels).</param>
        /// <returns>Position in tile coordinates.</returns>
        public static Vector2 ScreenToTile(CameraComponent camera, Vector2 screenPosition)
        {
            Matrix matrix = GetTransformMatrix(camera);
            Matrix.Invert(ref matrix, out Matrix invertedMatrix);
            Vector2 pixelPos = Vector2.Transform(screenPosition, invertedMatrix);

            // Convert pixels to tiles
            return new Vector2(pixelPos.X / camera.TileWidth, pixelPos.Y / camera.TileHeight);
        }

        /// <summary>
        /// Converts tile coordinates to screen coordinates.
        /// Useful for UI positioning and debug rendering.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <param name="tilePosition">Position in tile coordinates.</param>
        /// <returns>Position in screen space (pixels).</returns>
        public static Vector2 TileToScreen(CameraComponent camera, Vector2 tilePosition)
        {
            // Convert tiles to pixels
            Vector2 pixelPos = new Vector2(
                tilePosition.X * camera.TileWidth,
                tilePosition.Y * camera.TileHeight
            );
            return Vector2.Transform(pixelPos, GetTransformMatrix(camera));
        }

        /// <summary>
        /// Gets the camera's bounding rectangle in TILE coordinates.
        /// Useful for culling and intersection tests.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <returns>The bounding rectangle in tile coordinates.</returns>
        /// <remarks>
        /// The viewport shows a fixed number of tiles based on the reference resolution.
        /// With GBA resolution (240x160) and 16x16 tiles, this is 15x10 tiles.
        /// Zoom = scale means: Viewport pixels / Zoom = reference pixels (240x160)
        /// </remarks>
        public static RectangleF GetBoundingRectangle(CameraComponent camera)
        {
            // Viewport shows reference resolution in world space (e.g., 240x160 pixels = 15x10 tiles)
            // When zoom = scale, viewport pixels / zoom = reference pixels
            float worldViewportWidth = camera.Viewport.Width / camera.Zoom;
            float worldViewportHeight = camera.Viewport.Height / camera.Zoom;

            // Convert to tile coordinates
            float viewportWidthTiles = worldViewportWidth / camera.TileWidth;
            float viewportHeightTiles = worldViewportHeight / camera.TileHeight;

            float halfWidth = viewportWidthTiles / 2f;
            float halfHeight = viewportHeightTiles / 2f;

            return new RectangleF(
                camera.Position.X - halfWidth,
                camera.Position.Y - halfHeight,
                halfWidth * 2,
                halfHeight * 2
            );
        }

        /// <summary>
        /// Gets the camera's world view bounds as an integer Rectangle in TILE coordinates.
        /// Useful for tile-based culling and rendering optimization.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <returns>A rectangle representing the visible tile area.</returns>
        public static Rectangle GetTileViewBounds(CameraComponent camera)
        {
            RectangleF bounds = GetBoundingRectangle(camera);
            return new Rectangle(
                (int)Math.Floor(bounds.X),
                (int)Math.Floor(bounds.Y),
                (int)Math.Ceiling(bounds.Width),
                (int)Math.Ceiling(bounds.Height)
            );
        }

        /// <summary>
        /// Clamps the camera position to prevent showing areas outside the map.
        /// </summary>
        /// <param name="camera">The camera component.</param>
        /// <param name="position">The desired camera position in tile coordinates.</param>
        /// <returns>The clamped camera position in tile coordinates.</returns>
        public static Vector2 ClampPositionToMapBounds(CameraComponent camera, Vector2 position)
        {
            if (camera.MapBounds == Rectangle.Empty)
            {
                return position;
            }

            // Calculate half viewport dimensions in tile coordinates
            // Viewport shows reference resolution in world space (e.g., 240x160 pixels = 15x10 tiles)
            // When zoom = scale, viewport pixels / zoom = reference pixels
            float worldViewportWidth = camera.Viewport.Width / camera.Zoom;
            float worldViewportHeight = camera.Viewport.Height / camera.Zoom;

            float viewportWidthTiles = worldViewportWidth / camera.TileWidth;
            float viewportHeightTiles = worldViewportHeight / camera.TileHeight;

            float halfViewportWidth = viewportWidthTiles / 2f;
            float halfViewportHeight = viewportHeightTiles / 2f;

            // Calculate the bounds where the camera should stop
            float minX = camera.MapBounds.Left + halfViewportWidth;
            float maxX = camera.MapBounds.Right - halfViewportWidth;
            float minY = camera.MapBounds.Top + halfViewportHeight;
            float maxY = camera.MapBounds.Bottom - halfViewportHeight;

            // Handle cases where viewport is larger than map (center the camera)
            if (maxX < minX)
            {
                minX = maxX = (camera.MapBounds.Left + camera.MapBounds.Right) / 2f;
            }

            if (maxY < minY)
            {
                minY = maxY = (camera.MapBounds.Top + camera.MapBounds.Bottom) / 2f;
            }

            // Clamp position
            return new Vector2(
                MathHelper.Clamp(position.X, minX, maxX),
                MathHelper.Clamp(position.Y, minY, maxY)
            );
        }
    }
}
