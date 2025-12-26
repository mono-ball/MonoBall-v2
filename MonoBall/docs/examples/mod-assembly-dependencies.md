# Mod Assembly Dependencies for Scripts

This document explains how mods can provide types that other mods can use in their scripts.

## Overview

When Mod A defines a type `Foo` that Mod B wants to use in its scripts, Mod A must:
1. Compile `Foo` into a DLL assembly
2. Declare the assembly in its `mod.json`
3. Mod B declares Mod A as a dependency

When Mod B's scripts are compiled, Mod A's assemblies are automatically included as references.

## Example: Mod A Provides Types

**Mod A's `mod.json`:**
```json
{
  "id": "example:mod-a",
  "name": "Mod A",
  "version": "1.0.0",
  "assemblies": [
    "Assemblies/ModATypes.dll"
  ]
}
```

**Mod A's `Assemblies/ModATypes.dll`** contains:
```csharp
namespace ModA.Types
{
    public class Foo
    {
        public string Name { get; set; }
        public int Value { get; set; }
        
        public void DoSomething()
        {
            // ...
        }
    }
    
    public struct BarComponent
    {
        public float X { get; set; }
        public float Y { get; set; }
    }
}
```

## Example: Mod B Uses Mod A's Types

**Mod B's `mod.json`:**
```json
{
  "id": "example:mod-b",
  "name": "Mod B",
  "version": "1.0.0",
  "dependencies": [
    "example:mod-a"
  ],
  "plugins": [
    "Scripts/my_plugin.csx"
  ]
}
```

**Mod B's `Scripts/my_plugin.csx`:**
```csharp
using ModA.Types; // Can now use types from Mod A

public class MyPluginScript : ScriptBase
{
    public override void Initialize(ScriptContext context)
    {
        base.Initialize(context);
        
        // Use Mod A's Foo type
        var foo = new Foo
        {
            Name = "Test",
            Value = 42
        };
        foo.DoSomething();
        
        // Use Mod A's component type
        var bar = new BarComponent
        {
            X = 10.0f,
            Y = 20.0f
        };
        
        // Create entity with Mod A's component
        var entity = Context.CreateEntity(
            new PositionComponent { PixelX = 0, PixelY = 0 },
            bar // Using Mod A's component type
        );
        
        Context.Logger.Information("Created entity with Mod A's BarComponent");
    }
}
```

## How It Works

1. **Mod A** compiles its types into a DLL and lists it in `mod.json` under `"assemblies"`.
2. **Mod B** declares Mod A as a dependency in its `mod.json` under `"dependencies"`.
3. When **Mod B's scripts** are compiled:
   - The script compiler automatically resolves Mod A's assemblies
   - Adds them as metadata references to the Roslyn compilation
   - Mod B's scripts can now reference Mod A's public types

## Key Points

- **Assemblies must be public**: Only public types in the assemblies are accessible to other mods' scripts.
- **Dependencies are transitive**: If Mod B depends on Mod A, and Mod A depends on Mod C, Mod B's scripts automatically get access to both Mod A's and Mod C's assemblies.
- **Assembly paths are relative**: Assembly paths in `mod.json` are relative to the mod's root directory.
- **Dependency resolution**: The mod loader ensures dependencies are loaded before dependents, so assemblies are available when scripts are compiled.

## Creating Assemblies for Your Mod

To create an assembly that other mods can use:

1. **Create a separate C# project** (e.g., `ModATypes.csproj`) that compiles to a DLL
2. **Build the project** to generate the DLL
3. **Place the DLL** in your mod directory (e.g., `Mods/example-mod-a/Assemblies/ModATypes.dll`)
4. **List it in `mod.json`**:
   ```json
   {
     "assemblies": [
       "Assemblies/ModATypes.dll"
     ]
   }
   ```

## Best Practices

- **Namespace your types**: Use clear namespaces (e.g., `ModA.Types`, `ModA.Components`) to avoid conflicts
- **Version your assemblies**: Consider including version numbers in assembly names or namespaces
- **Document your types**: Provide XML documentation for types you expect others to use
- **Keep assemblies focused**: Create separate assemblies for different concerns if needed
- **Test dependencies**: Ensure your mod works when dependency mods are present

## Limitations

- **No runtime type loading**: Assemblies are only used for script compilation, not loaded at runtime
- **Public types only**: Only public types are accessible (internal/private types are not)
- **No circular dependencies**: Mod A cannot depend on Mod B if Mod B depends on Mod A
- **Assembly compatibility**: Assemblies must target compatible .NET versions (typically .NET 10.0 or compatible)

