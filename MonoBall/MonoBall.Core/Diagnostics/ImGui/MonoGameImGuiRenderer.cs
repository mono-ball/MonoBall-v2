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
/// </summary>
public sealed class MonoGameImGuiRenderer : IImGuiRenderer
{
    // ImDrawVert: pos (Vector2, 8 bytes) + uv (Vector2, 8 bytes) + col (uint, 4 bytes) = 20 bytes
    private const int ImDrawVertSize = 20;

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

    private Texture2D? _fontTexture;
    private readonly ConcurrentDictionary<IntPtr, Texture2D> _boundTextures = new();
    private int _textureIdCounter = 1;
    private readonly object _textureIdLock = new();

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

    // Store pinned glyph ranges to keep them alive for ImGui
    private GCHandle _glyphRangesHandle;
    private bool _glyphRangesPinned;

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

        SetupInput(io);
        LoadFonts(io);
        CreateDeviceResources();

        ImGuiTheme.ApplyDefaultTheme();
    }

    // Store pinned font data to keep it alive for ImGui
    private GCHandle _fontDataHandle;
    private bool _fontDataPinned;

    /// <summary>
    /// Loads fonts into ImGui, including the Nerd Font from the resource manager.
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
        // Note: new ImFontConfig() creates a zeroed struct, but ImGui's C++ constructor
        // sets important defaults. We must set them explicitly.
        var fontConfig = new ImFontConfig();
        fontConfig.FontDataOwnedByAtlas = 0; // false - We manage the memory
        fontConfig.OversampleH = 2;
        fontConfig.OversampleV = 1;
        fontConfig.PixelSnapH = 1; // true
        fontConfig.GlyphMinAdvanceX = 0;
        fontConfig.GlyphMaxAdvanceX = float.MaxValue; // Default from ImGui C++
        fontConfig.RasterizerMultiply = 1.0f; // CRITICAL: 0 = invisible text!
        fontConfig.RasterizerDensity = 1.0f; // Font density multiplier
        fontConfig.EllipsisChar = unchecked((char)0xFFFF); // -1 = auto-detect

        // Build glyph ranges for ASCII + Nerd Font icons
        // Pin the glyph ranges in memory - ImGui needs them for the font atlas lifetime
        var glyphRanges = BuildNerdFontGlyphRanges();
        _glyphRangesHandle = GCHandle.Alloc(glyphRanges, GCHandleType.Pinned);
        _glyphRangesPinned = true;

        io.Fonts.AddFontFromMemoryTTF(
            (void*)_fontDataHandle.AddrOfPinnedObject(),
            fontData.Length,
            DefaultFontSize,
            &fontConfig,
            (char*)_glyphRangesHandle.AddrOfPinnedObject()
        );

        Log.Information(
            "Loaded debug font from resource manager: {ResourceId} ({Size} bytes)",
            DebugFontResourceId,
            fontData.Length
        );
    }

    /// <summary>
    /// Builds glyph ranges for ASCII characters and Nerd Font icon ranges.
    /// </summary>
    private static ushort[] BuildNerdFontGlyphRanges()
    {
        // ImGui glyph ranges are pairs of (start, end) codepoints, terminated by 0
        return new ushort[]
        {
            // Basic Latin (ASCII)
            0x0020,
            0x00FF,
            // Latin Extended-A
            0x0100,
            0x017F,
            // Box Drawing (for tree structures)
            0x2500,
            0x257F,
            // Geometric Shapes (for bullets, etc.)
            0x25A0,
            0x25FF,
            // Powerline symbols (E0A0-E0D7)
            0xE0A0,
            0xE0D7,
            // Seti-UI + Custom (E5FA-E6AC)
            0xE5FA,
            0xE6AC,
            // Devicons (E700-E7C5)
            0xE700,
            0xE7C5,
            // Font Awesome (F000-F2E0)
            0xF000,
            0xF2E0,
            // Font Awesome Extension (E200-E2A9)
            0xE200,
            0xE2A9,
            // Octicons (F400-F532)
            0xF400,
            0xF532,
            // Codicons (EA60-EBE7)
            0xEA60,
            0xEBE7,
            // Terminator
            0,
        };
    }

    /// <inheritdoc />
    public void BeginFrame(float deltaTime)
    {
        ThrowIfNotInitialized();

        // Ensure context was created (it should be, but verify for safety)
        if (!_contextCreated)
            throw new InvalidOperationException(
                "ImGui context is not created. Ensure Initialize() was called."
            );

        // Set the context as current (ensures it's valid for this frame)
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
        RenderDrawData(ImGui.GetDrawData());
    }

    /// <inheritdoc />
    public unsafe void RebuildFontAtlas()
    {
        ThrowIfNotInitialized();

        var io = ImGui.GetIO();

        // Hexa.NET.ImGui uses ref/pointer parameters
        byte* pixelData;
        int width,
            height,
            bytesPerPixel;
        io.Fonts.GetTexDataAsRGBA32(&pixelData, &width, &height, &bytesPerPixel);

        var pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy((IntPtr)pixelData, pixels, 0, pixels.Length);

        _fontTexture?.Dispose();
        _fontTexture = new Texture2D(_graphicsDevice!, width, height, false, SurfaceFormat.Color);
        _fontTexture.SetData(pixels);

        io.Fonts.SetTexID(new ImTextureID(BindTexture(_fontTexture)));
        io.Fonts.ClearTexData();
    }

    /// <inheritdoc />
    public IntPtr BindTexture(Texture2D texture)
    {
        if (texture == null)
            throw new ArgumentNullException(nameof(texture));

        IntPtr id;
        lock (_textureIdLock)
        {
            id = new IntPtr(_textureIdCounter++);
        }
        _boundTextures[id] = texture;
        return id;
    }

    /// <inheritdoc />
    public void UnbindTexture(IntPtr textureHandle)
    {
        _boundTextures.TryRemove(textureHandle, out _);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _fontTexture?.Dispose();
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect?.Dispose();
        _rasterizerState?.Dispose();

        // Dispose all bound textures (except font texture which is already disposed)
        foreach (var kvp in _boundTextures)
        {
            if (kvp.Value != _fontTexture)
            {
                kvp.Value?.Dispose();
            }
        }
        _boundTextures.Clear();

        // Free pinned font data
        if (_fontDataPinned)
        {
            _fontDataHandle.Free();
            _fontDataPinned = false;
        }

        // Free pinned glyph ranges
        if (_glyphRangesPinned)
        {
            _glyphRangesHandle.Free();
            _glyphRangesPinned = false;
        }

        // Destroy the ImGui context if it was created
        if (_contextCreated)
        {
            ImGui.DestroyContext(_context);
            _contextCreated = false;
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

        RebuildFontAtlas();
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
        io.AddMouseWheelEvent(0, scrollDelta / 120f);

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

            // Copy vertex data using pointers (Hexa.NET.ImGui uses pointer types)
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

                // UserCallback is a void* in Hexa.NET.ImGui
                if (drawCmd.UserCallback != null)
                    continue;

                // TextureId is ImTextureID struct - extract the handle
                var textureHandle = (IntPtr)drawCmd.TextureId.Handle;
                if (!_boundTextures.TryGetValue(textureHandle, out var texture))
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
                    // vtxOffset is cumulative across command lists
                    // drawCmd.VtxOffset is the per-draw-command offset within this list
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

/// <summary>
/// Vertex declaration for ImGui vertices.
/// </summary>
internal static class ImGuiVertexDeclaration
{
    // ImDrawVert layout: pos (Vector2) + uv (Vector2) + col (uint packed RGBA)
    public static readonly VertexDeclaration Declaration = new(
        20, // Total size in bytes
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0)
    );
}
