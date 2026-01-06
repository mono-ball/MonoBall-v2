namespace MonoBall.Core.Diagnostics.ImGui;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoBall.Core.Resources;
using Serilog;

/// <summary>
/// ImGui renderer implementation for MonoGame.
/// Handles texture management, vertex buffer rendering, and input bridging.
/// Supports the new ImGui 1.92+ dynamic texture protocol.
/// </summary>
public sealed class MonoGameImGuiRenderer : IImGuiRenderer
{
    // ImDrawVert: pos (Vector2, 8 bytes) + uv (Vector2, 8 bytes) + col (uint, 4 bytes) = 20 bytes
    private const int ImDrawVertSize = 20;

    /// <summary>
    /// Standard mouse wheel delta value per notch (Windows standard).
    /// </summary>
    private const float MouseWheelDelta = 120f;

    /// <summary>
    /// Resource ID for the debug font in the mod system.
    /// </summary>
    private const string DebugFontResourceId = "base:font:debug/mono";

    /// <summary>
    /// Default font size for ImGui.
    /// </summary>
    private const float DefaultFontSize = 14.0f;

    private Game? _game;
    private GraphicsDevice? _graphicsDevice;
    private IResourceManager? _resourceManager;
    private BasicEffect? _effect;
    private RasterizerState? _rasterizerState;

    // Texture management for the new ImGui 1.92+ protocol
    private readonly ConcurrentDictionary<int, Texture2D> _textures = new();

    // Atomic counter for user-bound texture IDs to avoid hash collisions
    private int _userTextureIdCounter = int.MinValue;

    // Reusable pixel buffer to avoid allocations in hot paths
    private byte[]? _pixelBuffer;

    private byte[]? _vertexData;
    private byte[]? _indexData;
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;
    private int _vertexBufferSize;
    private int _indexBufferSize;

    private int _scrollWheelValue;
    private readonly List<int> _keys = new();
    private bool _disposed;

    // Store the ImGui context to prevent garbage collection
    private ImGuiContextPtr _context;
    private bool _contextCreated;

    // Store pinned font data to keep it alive for ImGui
    private GCHandle _fontDataHandle;
    private bool _fontDataPinned;

    /// <inheritdoc />
    public bool IsInitialized => _game != null && _graphicsDevice != null;

    /// <inheritdoc />
    public void Initialize(Game game, IResourceManager? resourceManager = null)
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));

        if (IsInitialized)
            throw new InvalidOperationException("ImGui renderer is already initialized.");

        _game = game;
        _graphicsDevice = game.GraphicsDevice;
        _resourceManager = resourceManager;

        _context = ImGui.CreateContext();
        _contextCreated = true;
        ImGui.SetCurrentContext(_context);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
        // Enable the new texture protocol for dynamic font updates (ImGui 1.92+)
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;

        SetupInput(io);
        LoadFonts(io);
        CreateDeviceResources();

        ImGuiTheme.ApplyDefaultTheme();
    }

    /// <summary>
    /// Loads fonts into ImGui using the new dynamic font system.
    /// With ImGui 1.92+, glyph ranges are optional - glyphs are loaded on demand.
    /// </summary>
    private unsafe void LoadFonts(ImGuiIOPtr io)
    {
        if (_resourceManager == null)
        {
            throw new InvalidOperationException(
                "ResourceManager is null - cannot load debug font. "
                    + "Ensure ResourceManager is passed to DebugOverlayService.Initialize()."
            );
        }

        // Load the debug font from the mod system
        byte[] fontData;
        try
        {
            fontData = _resourceManager.LoadFontData(DebugFontResourceId);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load font data for '{DebugFontResourceId}': {ex.Message}",
                ex
            );
        }

        if (fontData == null || fontData.Length == 0)
        {
            throw new InvalidOperationException(
                $"Font data was null or empty for '{DebugFontResourceId}'. "
                    + "Ensure the font definition exists and fontPath is correct."
            );
        }

        // Pin the font data in memory for ImGui - keep it alive!
        _fontDataHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
        _fontDataPinned = true;

        // Create font config with proper defaults
        var fontConfig = new ImFontConfig();
        fontConfig.FontDataOwnedByAtlas = 0; // false - We manage the memory
        fontConfig.OversampleH = 2;
        fontConfig.OversampleV = 1;
        fontConfig.PixelSnapH = 1; // true
        fontConfig.GlyphMinAdvanceX = 0;
        fontConfig.GlyphMaxAdvanceX = float.MaxValue;
        fontConfig.RasterizerMultiply = 1.0f;
        fontConfig.RasterizerDensity = 1.0f;
        fontConfig.EllipsisChar = unchecked((uint)0xFFFFFFFF); // -1 = auto-detect

        // With ImGui 1.92+, we don't need to specify glyph ranges!
        // Glyphs are loaded dynamically on demand, which is much more efficient
        // for fonts like Nerd Fonts that have many icon ranges.
        io.Fonts.AddFontFromMemoryTTF(
            (void*)_fontDataHandle.AddrOfPinnedObject(),
            fontData.Length,
            DefaultFontSize,
            &fontConfig
        );

        Log.Information(
            "Loaded debug font from resource manager: {ResourceId} ({Size} bytes) - using dynamic glyph loading",
            DebugFontResourceId,
            fontData.Length
        );
    }

    /// <inheritdoc />
    public void BeginFrame(float deltaTime)
    {
        ThrowIfNotInitialized();

        if (!_contextCreated)
            throw new InvalidOperationException(
                "ImGui context is not created. Ensure Initialize() was called."
            );

        ImGui.SetCurrentContext(_context);

        var io = ImGui.GetIO();

        io.DisplaySize = new System.Numerics.Vector2(
            _graphicsDevice!.PresentationParameters.BackBufferWidth,
            _graphicsDevice.PresentationParameters.BackBufferHeight
        );
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
        io.DeltaTime = deltaTime;

        UpdateInput(io);

        ImGui.NewFrame();
    }

    /// <inheritdoc />
    public void EndFrame()
    {
        ThrowIfNotInitialized();
        ImGui.EndFrame();
    }

    /// <inheritdoc />
    public void Render()
    {
        ThrowIfNotInitialized();

        ImGui.Render();
        var drawData = ImGui.GetDrawData();

        // Process texture requests (create/update/destroy) - new ImGui 1.92+ protocol
        ProcessTextureRequests(drawData);

        RenderDrawData(drawData);
    }

    /// <summary>
    /// Processes texture create/update/destroy requests from ImGui.
    /// This is the new ImGui 1.92+ texture protocol that enables dynamic font updates.
    /// </summary>
    private unsafe void ProcessTextureRequests(ImDrawDataPtr drawData)
    {
        var textures = drawData.Textures;
        for (var i = 0; i < textures.Size; i++)
        {
            var texData = textures[i];
            var status = texData.Status;

            switch (status)
            {
                case ImTextureStatus.WantCreate:
                    CreateTexture(texData);
                    break;

                case ImTextureStatus.WantUpdates:
                    UpdateTexture(texData);
                    break;

                case ImTextureStatus.WantDestroy:
                    DestroyTexture(texData);
                    break;
            }
        }
    }

    /// <summary>
    /// Creates a new texture from ImTextureData and assigns a texture ID.
    /// </summary>
    private unsafe void CreateTexture(ImTextureDataPtr texData)
    {
        var width = texData.Width;
        var height = texData.Height;
        var format = texData.Format;
        var srcPixels = (byte*)texData.GetPixels();

        // Ensure pixel buffer is large enough (reuse to avoid allocations)
        var requiredSize = width * height * 4;
        EnsurePixelBufferCapacity(requiredSize);

        // Convert pixels to RGBA format
        if (format == ImTextureFormat.Alpha8)
        {
            ConvertAlpha8ToRgba(srcPixels, _pixelBuffer!, width, height, width);
        }
        else // ImTextureFormat.Rgba32
        {
            var sizeInBytes = (int)texData.GetSizeInBytes();
            Marshal.Copy((IntPtr)srcPixels, _pixelBuffer!, 0, sizeInBytes);
        }

        // Create texture and upload data
        var texture = new Texture2D(_graphicsDevice!, width, height, false, SurfaceFormat.Color);
        texture.SetData(_pixelBuffer, 0, requiredSize);

        // Use the texture's unique ID from ImGui
        var uniqueId = texData.UniqueID;
        _textures[uniqueId] = texture;

        // Set the texture ID back to ImGui
        texData.SetTexID(new ImTextureID((nint)uniqueId));
        texData.SetStatus(ImTextureStatus.Ok);

        Log.Debug("Created ImGui texture: ID={UniqueId}, Size={Width}x{Height}, Format={Format}",
            uniqueId, width, height, format);
    }

    /// <summary>
    /// Updates a texture with partial data from ImTextureData.
    /// </summary>
    private unsafe void UpdateTexture(ImTextureDataPtr texData)
    {
        var uniqueId = texData.UniqueID;
        if (!_textures.TryGetValue(uniqueId, out var texture))
        {
            Log.Warning("Attempted to update non-existent texture: ID={UniqueId}", uniqueId);
            return;
        }

        var format = texData.Format;
        var textureWidth = texData.Width;
        var pitch = (int)texData.GetPitch();

        // Process update rectangles
        var updates = texData.Updates;
        for (var i = 0; i < updates.Size; i++)
        {
            var rect = updates[i];
            var rectWidth = rect.W;
            var rectHeight = rect.H;

            // Ensure pixel buffer is large enough
            var requiredSize = rectWidth * rectHeight * 4;
            EnsurePixelBufferCapacity(requiredSize);

            // Get pixel data at the rectangle position
            var srcPixels = (byte*)texData.GetPixelsAt(rect.X, rect.Y);

            // Convert pixels to RGBA format
            if (format == ImTextureFormat.Alpha8)
            {
                ConvertAlpha8ToRgba(srcPixels, _pixelBuffer!, rectWidth, rectHeight, textureWidth);
            }
            else
            {
                // RGBA32 format - copy row by row due to pitch
                for (var y = 0; y < rectHeight; y++)
                {
                    var srcOffset = y * pitch;
                    var dstOffset = y * rectWidth * 4;
                    Marshal.Copy((IntPtr)(srcPixels + srcOffset), _pixelBuffer!, dstOffset, rectWidth * 4);
                }
            }

            texture.SetData(
                0,
                new Rectangle(rect.X, rect.Y, rectWidth, rectHeight),
                _pixelBuffer,
                0,
                requiredSize
            );
        }

        texData.SetStatus(ImTextureStatus.Ok);
    }

    /// <summary>
    /// Converts Alpha8 format pixels to RGBA format.
    /// </summary>
    /// <param name="src">Source Alpha8 pixels.</param>
    /// <param name="dst">Destination RGBA buffer.</param>
    /// <param name="width">Width of the region to convert.</param>
    /// <param name="height">Height of the region to convert.</param>
    /// <param name="srcPitch">Source pitch (stride) in pixels.</param>
    private static unsafe void ConvertAlpha8ToRgba(byte* src, byte[] dst, int width, int height, int srcPitch)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var srcIdx = y * srcPitch + x;
                var dstIdx = (y * width + x) * 4;
                var alpha = src[srcIdx];
                dst[dstIdx + 0] = 255; // R
                dst[dstIdx + 1] = 255; // G
                dst[dstIdx + 2] = 255; // B
                dst[dstIdx + 3] = alpha; // A
            }
        }
    }

    /// <summary>
    /// Ensures the reusable pixel buffer has at least the specified capacity.
    /// </summary>
    private void EnsurePixelBufferCapacity(int requiredSize)
    {
        if (_pixelBuffer == null || _pixelBuffer.Length < requiredSize)
        {
            // Allocate with some extra capacity to reduce reallocations
            _pixelBuffer = new byte[(int)(requiredSize * 1.5f)];
        }
    }

    /// <summary>
    /// Destroys a texture and removes it from the cache.
    /// </summary>
    private void DestroyTexture(ImTextureDataPtr texData)
    {
        var uniqueId = texData.UniqueID;
        if (_textures.TryRemove(uniqueId, out var texture))
        {
            texture.Dispose();
            Log.Debug("Destroyed ImGui texture: ID={UniqueId}", uniqueId);
        }

        texData.SetStatus(ImTextureStatus.Destroyed);
    }

    /// <inheritdoc />
    public void RebuildFontAtlas()
    {
        // With ImGui 1.92+ and RendererHasTextures, the font atlas is managed
        // dynamically through the texture protocol. Manual rebuilding is no longer
        // necessary as textures are created/updated on demand.
        Log.Debug("RebuildFontAtlas called - using dynamic texture protocol, no manual rebuild needed");
    }

    /// <inheritdoc />
    public IntPtr BindTexture(Texture2D texture)
    {
        if (texture == null)
            throw new ArgumentNullException(nameof(texture));

        // Use atomic counter for user texture IDs to avoid hash collisions
        // User IDs start from int.MinValue and increment, while ImGui uses positive IDs
        var id = System.Threading.Interlocked.Increment(ref _userTextureIdCounter);
        _textures[id] = texture;
        return new IntPtr(id);
    }

    /// <inheritdoc />
    public void UnbindTexture(IntPtr textureHandle)
    {
        var id = (int)textureHandle;
        _textures.TryRemove(id, out _);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases resources used by the renderer.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Dispose all textures
            foreach (var kvp in _textures)
            {
                kvp.Value?.Dispose();
            }
            _textures.Clear();

            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            _effect?.Dispose();
            _rasterizerState?.Dispose();

            // Free pinned font data
            if (_fontDataPinned)
            {
                _fontDataHandle.Free();
                _fontDataPinned = false;
            }

            // Destroy the ImGui context if it was created
            if (_contextCreated)
            {
                ImGui.DestroyContext(_context);
                _contextCreated = false;
            }
        }

        _disposed = true;
    }

    private void CreateDeviceResources()
    {
        _effect = new BasicEffect(_graphicsDevice!)
        {
            TextureEnabled = true,
            VertexColorEnabled = true,
        };

        _rasterizerState = new RasterizerState
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0,
        };
    }

    private void SetupInput(ImGuiIOPtr io)
    {
        _keys.Clear();
        _keys.Add((int)Keys.Tab);
        _keys.Add((int)Keys.Left);
        _keys.Add((int)Keys.Right);
        _keys.Add((int)Keys.Up);
        _keys.Add((int)Keys.Down);
        _keys.Add((int)Keys.PageUp);
        _keys.Add((int)Keys.PageDown);
        _keys.Add((int)Keys.Home);
        _keys.Add((int)Keys.End);
        _keys.Add((int)Keys.Delete);
        _keys.Add((int)Keys.Back);
        _keys.Add((int)Keys.Enter);
        _keys.Add((int)Keys.Escape);
        _keys.Add((int)Keys.Space);
        _keys.Add((int)Keys.A);
        _keys.Add((int)Keys.C);
        _keys.Add((int)Keys.V);
        _keys.Add((int)Keys.X);
        _keys.Add((int)Keys.Y);
        _keys.Add((int)Keys.Z);
    }

    private void UpdateInput(ImGuiIOPtr io)
    {
        if (!_game!.IsActive)
            return;

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);

        var scrollDelta = mouse.ScrollWheelValue - _scrollWheelValue;
        _scrollWheelValue = mouse.ScrollWheelValue;
        io.AddMouseWheelEvent(0, scrollDelta / MouseWheelDelta);

        var isCtrl = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);
        var isShift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
        var isAlt = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);

        io.AddKeyEvent(ImGuiKey.ModCtrl, isCtrl);
        io.AddKeyEvent(ImGuiKey.ModShift, isShift);
        io.AddKeyEvent(ImGuiKey.ModAlt, isAlt);

        foreach (var key in _keys)
        {
            var xnaKey = (Keys)key;
            io.AddKeyEvent(TranslateKey(xnaKey), keyboard.IsKeyDown(xnaKey));
        }

        // NOTE: Text input (AddInputCharacter) is handled by ImGuiInputBridgeSystem
        // which provides keyboard polling with key repeat support.
        // Do NOT add character input here - it would cause duplicate characters.
    }

    private static ImGuiKey TranslateKey(Keys key)
    {
        return key switch
        {
            Keys.Tab => ImGuiKey.Tab,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.Home => ImGuiKey.Home,
            Keys.End => ImGuiKey.End,
            Keys.Delete => ImGuiKey.Delete,
            Keys.Back => ImGuiKey.Backspace,
            Keys.Enter => ImGuiKey.Enter,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.A => ImGuiKey.A,
            Keys.C => ImGuiKey.C,
            Keys.V => ImGuiKey.V,
            Keys.X => ImGuiKey.X,
            Keys.Y => ImGuiKey.Y,
            Keys.Z => ImGuiKey.Z,
            _ => ImGuiKey.None,
        };
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.CmdListsCount == 0)
            return;

        var lastViewport = _graphicsDevice!.Viewport;
        var lastScissorBox = _graphicsDevice.ScissorRectangle;
        var lastBlendState = _graphicsDevice.BlendState;
        var lastDepthStencilState = _graphicsDevice.DepthStencilState;
        var lastRasterizerState = _graphicsDevice.RasterizerState;
        var lastSamplerState = _graphicsDevice.SamplerStates[0];

        _graphicsDevice.BlendState = BlendState.NonPremultiplied;
        _graphicsDevice.DepthStencilState = DepthStencilState.None;
        _graphicsDevice.RasterizerState = _rasterizerState;
        _graphicsDevice.SamplerStates[0] = SamplerState.PointClamp;

        drawData.ScaleClipRects(ImGui.GetIO().DisplayFramebufferScale);

        _effect!.Projection = Matrix.CreateOrthographicOffCenter(
            0f,
            drawData.DisplaySize.X,
            drawData.DisplaySize.Y,
            0f,
            -1f,
            1f
        );
        _effect.View = Matrix.Identity;
        _effect.World = Matrix.Identity;

        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        _graphicsDevice.Viewport = lastViewport;
        _graphicsDevice.ScissorRectangle = lastScissorBox;
        _graphicsDevice.BlendState = lastBlendState;
        _graphicsDevice.DepthStencilState = lastDepthStencilState;
        _graphicsDevice.RasterizerState = lastRasterizerState;
        _graphicsDevice.SamplerStates[0] = lastSamplerState;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        var totalVbSize = drawData.TotalVtxCount * ImDrawVertSize;
        if (totalVbSize > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(totalVbSize * 1.5f);
            _vertexBuffer = new VertexBuffer(
                _graphicsDevice!,
                ImGuiVertexDeclaration.Declaration,
                _vertexBufferSize / ImDrawVertSize,
                BufferUsage.None
            );
            _vertexData = new byte[_vertexBufferSize];
        }

        var totalIbSize = drawData.TotalIdxCount * sizeof(ushort);
        if (totalIbSize > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(totalIbSize * 1.5f);
            _indexBuffer = new IndexBuffer(
                _graphicsDevice!,
                IndexElementSize.SixteenBits,
                _indexBufferSize / sizeof(ushort),
                BufferUsage.None
            );
            _indexData = new byte[_indexBufferSize];
        }

        var vtxOffset = 0;
        var idxOffset = 0;

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            var vtxSize = cmdList.VtxBuffer.Size * ImDrawVertSize;
            var idxSize = cmdList.IdxBuffer.Size * sizeof(ushort);

            // Copy vertex data using pointers
            var vtxPtr = (IntPtr)cmdList.VtxBuffer.Data;
            var idxPtr = (IntPtr)cmdList.IdxBuffer.Data;
            Marshal.Copy(vtxPtr, _vertexData!, vtxOffset, vtxSize);
            Marshal.Copy(idxPtr, _indexData!, idxOffset, idxSize);

            vtxOffset += vtxSize;
            idxOffset += idxSize;
        }

        _vertexBuffer!.SetData(_vertexData!, 0, vtxOffset);
        _indexBuffer!.SetData(_indexData!, 0, idxOffset);
    }

    private unsafe void RenderCommandLists(ImDrawDataPtr drawData)
    {
        _graphicsDevice!.SetVertexBuffer(_vertexBuffer);
        _graphicsDevice.Indices = _indexBuffer;

        var vtxOffset = 0;
        var idxOffset = 0;

        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];

            for (var cmdI = 0; cmdI < cmdList.CmdBuffer.Size; cmdI++)
            {
                var drawCmd = cmdList.CmdBuffer[cmdI];

                // UserCallback is a void* - skip if present
                if (drawCmd.UserCallback != null)
                    continue;

                // Get texture ID using the new API (ImGui 1.92+)
                var textureId = (int)drawCmd.GetTexID().Handle;
                if (!_textures.TryGetValue(textureId, out var texture))
                    continue;

                _graphicsDevice.ScissorRectangle = new Rectangle(
                    (int)drawCmd.ClipRect.X,
                    (int)drawCmd.ClipRect.Y,
                    (int)(drawCmd.ClipRect.Z - drawCmd.ClipRect.X),
                    (int)(drawCmd.ClipRect.W - drawCmd.ClipRect.Y)
                );

                _effect!.Texture = texture;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        vtxOffset + (int)drawCmd.VtxOffset,
                        (int)drawCmd.IdxOffset + idxOffset,
                        (int)drawCmd.ElemCount / 3
                    );
                }
            }

            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ImGui renderer has not been initialized.");
    }
}
