# MonoBall.DesktopVK

Vulkan backend for MonoBall using MonoGame 3.8.5-preview.2.

## Known Issues

### macOS Crash (Exit Code 138)

DesktopVK on macOS uses MoltenVK (Vulkan over Metal) which is still in preview and has known stability issues. The application may crash during Vulkan device initialization with `EXC_BAD_ACCESS` errors.

**Workaround:** Use `MonoBall.DesktopGL` for macOS development, which uses OpenGL and is stable.

### Platform Support

- **Windows:** Should work (uses native Vulkan)
- **Linux:** Should work (uses native Vulkan)
- **macOS:** Unstable (uses MoltenVK) - Use DesktopGL instead

## Building

```bash
dotnet build MonoBall.DesktopVK/MonoBall.DesktopVK.csproj --configuration Debug
```

## Running

```bash
dotnet run --project MonoBall.DesktopVK/MonoBall.DesktopVK.csproj --configuration Debug
```
