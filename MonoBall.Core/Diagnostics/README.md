# MonoBall Debug System

An ImGui.NET-based debug overlay system for MonoGame, built with Arch ECS patterns.

## Quick Start

```csharp
// In your Game class:

private DebugOverlayService _debugOverlay;

protected override void Initialize()
{
    base.Initialize();

    // Initialize the debug overlay after base initialization
    _debugOverlay = new DebugOverlayService(world);
    _debugOverlay.Initialize(this);
}

protected override void Update(GameTime gameTime)
{
    // Start ImGui frame at the beginning of Update
    _debugOverlay.BeginUpdate(gameTime);

    // Your game update logic here...
    // Check if ImGui wants input:
    if (!_debugOverlay.WantsCaptureKeyboard)
    {
        // Process keyboard input
    }
    if (!_debugOverlay.WantsCaptureMouse)
    {
        // Process mouse input
    }

    // End ImGui frame (renders panels)
    _debugOverlay.EndUpdate(gameTime);

    base.Update(gameTime);
}

protected override void Draw(GameTime gameTime)
{
    GraphicsDevice.Clear(Color.CornflowerBlue);

    // Your game rendering here...

    // Render debug overlay last
    _debugOverlay.Draw();

    base.Draw(gameTime);
}

protected override void UnloadContent()
{
    _debugOverlay?.Dispose();
    base.UnloadContent();
}
```

## Toggle Debug Overlay

```csharp
// Toggle visibility with F3 key
if (Keyboard.GetState().IsKeyDown(Keys.F3) && !_f3WasDown)
{
    _debugOverlay.Toggle();
}
_f3WasDown = Keyboard.GetState().IsKeyDown(Keys.F3);
```

## Creating Custom Panels

```csharp
public class MyCustomPanel : IDebugPanel
{
    public string Id => "my-custom-panel";
    public string DisplayName => "My Panel";
    public bool IsVisible { get; set; }
    public string Category => "Game";
    public int SortOrder => 10;

    public void Draw(float deltaTime)
    {
        ImGui.Text("Hello from custom panel!");

        if (ImGui.Button("Click Me"))
        {
            // Handle button click
        }
    }
}

// Register the panel:
_debugOverlay.RegisterPanel(new MyCustomPanel());
```

## Architecture

```
Debug/
├── Events/                    # Event types
│   ├── DebugToggleEvent.cs
│   └── DebugPanelToggleEvent.cs
├── ImGui/                     # ImGui integration
│   ├── IImGuiRenderer.cs
│   ├── MonoGameImGuiRenderer.cs
│   └── ImGuiTheme.cs
├── Panels/                    # Debug panels
│   ├── IDebugPanel.cs
│   ├── PerformancePanel.cs
│   └── EntityInspectorPanel.cs
├── Services/                  # Service layer
│   ├── IDebugPanelRegistry.cs
│   ├── DebugPanelRegistry.cs
│   └── DebugOverlayService.cs
└── Systems/                   # ECS systems
    ├── DebugSystemBase.cs
    ├── ImGuiLifecycleSystem.cs
    ├── ImGuiInputBridgeSystem.cs
    └── DebugPanelRenderSystem.cs
```

## Built-in Panels

- **Performance Panel**: FPS, frame time graph, memory usage, GC stats
- **Entity Inspector**: Browse and inspect ECS entities and components

## Event Integration

Use the EventBus to toggle panels:

```csharp
// Toggle a specific panel
var evt = new DebugPanelToggleEvent
{
    PanelId = "performance",
    Show = null // null = toggle, true = show, false = hide
};
EventBus.Send(ref evt);
```
