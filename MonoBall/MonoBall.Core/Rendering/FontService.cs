using System;
using System.Collections.Generic;
using System.IO;
using FontStashSharp;
using Microsoft.Xna.Framework.Graphics;
using MonoBall.Core.Mods;
using Serilog;

namespace MonoBall.Core.Rendering
{
    /// <summary>
    /// Service for loading and caching fonts from font definitions using FontStashSharp.
    /// </summary>
    public class FontService
    {
        private readonly IModManager _modManager;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ILogger _logger;
        private readonly Dictionary<string, FontSystem> _fontCache =
            new Dictionary<string, FontSystem>();

        /// <summary>
        /// Initializes a new instance of the FontService.
        /// </summary>
        /// <param name="modManager">The mod manager for accessing font definitions.</param>
        /// <param name="graphicsDevice">The graphics device for font rendering.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public FontService(IModManager modManager, GraphicsDevice graphicsDevice, ILogger logger)
        {
            _modManager = modManager ?? throw new ArgumentNullException(nameof(modManager));
            _graphicsDevice =
                graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets a FontSystem for the specified font definition ID, loading it if not already cached.
        /// </summary>
        /// <param name="fontId">The font definition ID (e.g., "base:font:debug/mono").</param>
        /// <returns>The FontSystem instance, or null if the font could not be loaded.</returns>
        public FontSystem? GetFontSystem(string fontId)
        {
            if (string.IsNullOrEmpty(fontId))
            {
                _logger.Warning("Attempted to load font with null or empty ID");
                return null;
            }

            // Check cache first
            if (_fontCache.TryGetValue(fontId, out var cachedFont))
            {
                _logger.Debug("Using cached font for {FontId}", fontId);
                return cachedFont;
            }

            _logger.Debug("Loading font {FontId}", fontId);

            // Get font definition
            var definition = _modManager.GetDefinition<FontDefinition>(fontId);
            if (definition == null)
            {
                _logger.Warning("Font definition not found: {FontId}", fontId);
                return null;
            }

            _logger.Debug(
                "Found font definition for {FontId} (fontPath: {FontPath})",
                fontId,
                definition.FontPath
            );

            // Get mod directory
            var metadata = _modManager.GetDefinitionMetadata(fontId);
            if (metadata == null)
            {
                _logger.Warning("Font metadata not found: {FontId}", fontId);
                return null;
            }

            _logger.Debug(
                "Found metadata for {FontId} (originalModId: {ModId})",
                fontId,
                metadata.OriginalModId
            );

            // Get mod manifest
            var modManifest = _modManager.GetModManifest(metadata.OriginalModId);
            if (modManifest == null)
            {
                _logger.Warning(
                    "Mod manifest not found for font {FontId} (mod: {ModId})",
                    fontId,
                    metadata.OriginalModId
                );
                return null;
            }

            _logger.Debug(
                "Found mod manifest for {ModId} (modDirectory: {ModDirectory})",
                modManifest.Id,
                modManifest.ModDirectory
            );

            // Resolve font path
            // FontPath in definition is relative to mod root (like TexturePath in sprite definitions)
            if (string.IsNullOrEmpty(definition.FontPath))
            {
                _logger.Warning("Font definition {FontId} has no FontPath", fontId);
                return null;
            }
            string fontPath = Path.Combine(modManifest.ModDirectory, definition.FontPath);

            fontPath = Path.GetFullPath(fontPath);

            _logger.Debug("Resolved font path: {FontPath}", fontPath);

            if (!File.Exists(fontPath))
            {
                _logger.Warning(
                    "Font file not found: {FontPath} (font: {FontId})",
                    fontPath,
                    fontId
                );
                return null;
            }

            try
            {
                // Create FontSystem (GraphicsDevice is not needed in constructor, it's used during rendering)
                var fontSystem = new FontSystem();

                // Read font file bytes
                byte[] fontData = File.ReadAllBytes(fontPath);
                if (fontData == null || fontData.Length == 0)
                {
                    _logger.Warning(
                        "Font file is empty or could not be read: {FontPath} (font: {FontId})",
                        fontPath,
                        fontId
                    );
                    return null;
                }

                // Add font to FontSystem
                fontSystem.AddFont(fontData);

                // Verify font was added by attempting to get a font (this will throw if no fonts were added)
                // This ensures we don't cache an invalid FontSystem
                try
                {
                    // Try to get a font to verify it was added successfully
                    // Use a small test size to verify the font is valid
                    var testFont = fontSystem.GetFont(12);
                    if (testFont == null)
                    {
                        _logger.Error(
                            "Font was added but GetFont(12) returned null: {FontId}. Font may be corrupted.",
                            fontId
                        );
                        return null;
                    }
                }
                catch (Exception verifyEx)
                {
                    _logger.Error(
                        verifyEx,
                        "Font was added but GetFont failed (font may be invalid or corrupted): {FontId}. Error: {Error}",
                        fontId,
                        verifyEx.Message
                    );
                    return null;
                }

                // Cache the font system only after successful verification
                _fontCache[fontId] = fontSystem;

                _logger.Information(
                    "Successfully loaded and verified font {FontId} from {FontPath}",
                    fontId,
                    fontPath
                );

                return fontSystem;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load font {FontId} from {FontPath}", fontId, fontPath);
                return null;
            }
        }
    }
}
