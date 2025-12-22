# Code Analysis & Refactoring Plan

## Issues Found

### SOLID Principles Violations

1. **Single Responsibility Principle (SRP)**
   - `ModLoader` has too many responsibilities: discovery, loading, JSON merging, dependency resolution
   - `MonoBallGame` contains path-finding logic that should be extracted
   - `ModValidator` duplicates some logic from `ModLoader`

2. **Open/Closed Principle (OCP)**
   - JSON serialization options are hardcoded in multiple places
   - Error handling is not extensible

3. **Dependency Inversion Principle (DIP)**
   - Direct dependency on `System.Diagnostics.Debug` instead of abstraction
   - No interface for logging

### DRY Violations

1. **JSON Serialization Options**
   - Repeated in `ModLoader.DiscoverMods()`, `ModLoader.ResolveLoadOrder()`, `ModValidator.ValidateAll()`
   - Should be centralized

2. **Path Validation**
   - Similar logic in `ModManager` constructor and `MonoBallGame.FindModsDirectory()`

3. **Definition Loading**
   - Similar JSON parsing logic in `ModLoader` and `ModValidator`

4. **Error Message Formatting**
   - Repeated formatting logic in `ModManager.Load()`

### .NET 10 Standards

1. **File-Scoped Namespaces**
   - Should use file-scoped namespaces for cleaner code

2. **Nullable Reference Types**
   - Not enabled, but nullable annotations are used inconsistently

3. **Modern C# Features**
   - Could use records for data classes
   - Could use init-only properties
   - Could use pattern matching more extensively

4. **Logging**
   - Should use `ILogger` or at least abstract logging

### Project Organization

1. **File Structure**
   - `RootModManifest` is internal class in `ModLoader.cs` - should be separate
   - `ValidationIssue` and `ValidationSeverity` in `ModValidator.cs` - should be separate
   - Path utilities should be in a separate utility class

2. **Namespace Organization**
   - All mod-related code is in `MonoBall.Core.Mods` - good
   - Could benefit from sub-namespaces for better organization

## Refactoring Plan

### Phase 1: Extract Utilities
- Create `JsonSerializerOptionsFactory` for centralized JSON options
- Create `ModsPathResolver` utility class
- Extract `RootModManifest` to separate file

### Phase 2: Improve SOLID
- Extract JSON merging to separate class
- Create logging abstraction
- Split `ModLoader` responsibilities

### Phase 3: Modernize Code
- Enable nullable reference types
- Use file-scoped namespaces
- Consider records for data classes

### Phase 4: Improve Organization
- Separate validation types to own files
- Create utility namespace


