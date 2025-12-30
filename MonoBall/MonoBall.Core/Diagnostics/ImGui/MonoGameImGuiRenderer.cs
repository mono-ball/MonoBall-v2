namespace MonoBall.Core.Diagnostics.ImGui;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Serilog;

/// <summary>
/// ImGui renderer implementation for MonoGame.
/// Handles texture management, vertex buffer rendering, and input bridging.
/// </summary>
public sealed class MonoGameImGuiRenderer : IImGuiRenderer
{
    // ImDrawVert: pos (Vector2, 8 bytes) + uv (Vector2, 8 bytes) + col (uint, 4 bytes) = 20 bytes
    private const int ImDrawVertSize = 20;

    private Game? _game;
    private GraphicsDevice? _graphicsDevice;
    private BasicEffect? _effect;
    private RasterizerState? _rasterizerState;

    private Texture2D? _fontTexture;
    private readonly Dictionary<IntPtr, Texture2D> _boundTextures = new();
    private int _textureIdCounter = 1;

    private byte[]? _vertexData;
    private byte[]? _indexData;
    private VertexBuffer? _vertexBuffer;
    private IndexBuffer? _indexBuffer;
    private int _vertexBufferSize;
    private int _indexBufferSize;

    private int _scrollWheelValue;
    private readonly List<int> _keys = new();
    private bool _disposed;

    /// <inheritdoc />
    public bool IsInitialized => _game != null && _graphicsDevice != null;

    /// <inheritdoc />
    public void Initialize(Game game)
    {
        if (game == null)
            throw new ArgumentNullException(nameof(game));

        if (IsInitialized)
            throw new InvalidOperationException("ImGui renderer is already initialized.");

        _game = game;
        _graphicsDevice = game.GraphicsDevice;

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;

        SetupInput(io);
        CreateDeviceResources();

        ImGuiTheme.ApplyDefaultTheme();
    }

    /// <inheritdoc />
    public void BeginFrame(float deltaTime)
    {
        ThrowIfNotInitialized();

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

        var id = new IntPtr(_textureIdCounter++);
        _boundTextures[id] = texture;
        return id;
    }

    /// <inheritdoc />
    public void UnbindTexture(IntPtr textureHandle)
    {
        _boundTextures.Remove(textureHandle);
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

        _boundTextures.Clear();

        ImGui.DestroyContext();

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

        io.AddKeyEvent(
            ImGuiKey.ModCtrl,
            keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl)
        );
        io.AddKeyEvent(
            ImGuiKey.ModShift,
            keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift)
        );
        io.AddKeyEvent(
            ImGuiKey.ModAlt,
            keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt)
        );

        foreach (var key in _keys)
        {
            var xnaKey = (Keys)key;
            io.AddKeyEvent(TranslateKey(xnaKey), keyboard.IsKeyDown(xnaKey));
        }
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
                    _graphicsDevice.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        vtxOffset,
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
