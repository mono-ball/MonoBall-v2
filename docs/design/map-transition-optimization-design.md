# Map Transition Optimization - Research & Design

## Problem Statement

Map transitions currently cause noticeable lag due to synchronous resource loading during the transition event. The main bottlenecks are:

1. **Font Loading**: `MapPopupSystem` loads fonts synchronously when creating popups (`base:font:game/pokemon`)
2. **Texture Loading**: `MapPopupSystem` loads popup background and outline textures synchronously
3. **Audio Loading**: `MapMusicSystem` loads audio files synchronously when playing music for new maps

All of these operations block the main thread during `MapTransitionEvent` processing, causing frame drops and stuttering.

## Design Philosophy: Pokemon-Style Instant Transitions

**Critical Requirement**: In Pokemon-style games, map transitions must be **instant and seamless**. Resources must be ready **before** the transition occurs, not during or after.

**Key Principles**:
- ✅ **Aggressive Preloading**: All resources for connected maps must be preloaded when a map loads
- ✅ **Zero Transition-Time Loading**: No loading during `MapTransitionEvent` processing
- ✅ **Predictive Preloading**: Preload based on map connections (player can only go to connected maps)
- ❌ **No Progressive Loading**: Resources cannot appear over multiple frames
- ❌ **No Frame-Time Loading**: Cannot spread loading across frames
- ❌ **No Fallback Rendering**: Everything must be ready, no placeholders

## Current Flow Analysis

### Map Transition Event Flow

```
1. Player crosses map boundary
   ↓
2. MapTransitionDetectionSystem fires MapTransitionEvent
   ↓
3. Multiple systems react synchronously:
   ├─ MapMusicSystem: Loads audio file → Creates VorbisReader → Plays music
   ├─ MapPopupSystem: Loads font → Loads textures → Creates popup entity
   └─ MapLoaderSystem: Already preloads connected maps (good!)
```

### Resource Loading Points

**MapPopupSystem** (`ShowPopupForMap` → `CreatePopup`):
- Font loading: `_resourceManager.LoadFont("base:font:game/pokemon")` - **SYNCHRONOUS**
- Texture loading: `_resourceManager.LoadTexture(backgroundId)` - **SYNCHRONOUS** (but cached)
- Texture loading: `_resourceManager.LoadTexture(outlineId)` - **SYNCHRONOUS** (but cached)

**MapMusicSystem** (`OnMapTransition` → `PlayMusicForMap`):
- Audio loading: `_resourceManager.LoadAudioReader(audioId)` - **SYNCHRONOUS**
- Creates new `VorbisReader` for each playback (not cached by design)

**MapLoaderSystem** (`LoadMap`):
- Tileset preloading: `LoadTexturesBatch()` - **PARALLEL** ✅ (already optimized)
- NPC sprite preloading: `LoadTexturesBatch()` - **PARALLEL** ✅ (already optimized)

## Design Solution: Aggressive Preloading (Pokemon-Style)

**Core Concept**: Preload **all** resources for connected maps when a map loads. When `MapTransitionEvent` fires, everything is already ready - zero loading time.

**Why This Works for Pokemon Games**:
- Player can only transition to connected maps (predictable)
- Maps are loaded proactively by `MapLoaderSystem` (connected maps already loaded)
- Resources for connected maps can be preloaded when parent map loads
- Transition is instant because resources are already in memory

**Implementation Strategy**:

### Phase 1: Resource Discovery (When Map Loads)
When `MapLoaderSystem.LoadMap()` completes and fires `MapLoadedEvent`:
1. **Analyze Connected Maps**: For each connected map, determine required resources
2. **Resource Prediction**: 
   - Fonts: `base:font:game/pokemon` (common popup font)
   - Audio: Music files from `MusicComponent` of connected maps
   - Textures: Popup backgrounds/outlines from `PopupThemeDefinition` of connected maps
   - Tilesets: Already handled ✅

### Phase 2: Background Preloading (Non-Blocking)
1. **File I/O on Background Thread**: Load raw file data (audio bytes, font bytes)
2. **Cache Raw Data**: Store in memory for fast resource creation
3. **GPU Resource Creation on Main Thread**: Create `FontSystem`, `Texture2D` from cached data
4. **Resource Ready State**: Mark resources as ready when creation completes

### Phase 3: Transition-Time Usage (Zero Loading)
When `MapTransitionEvent` fires:
1. **All Resources Ready**: Fonts, textures, audio data already in memory
2. **Instant Access**: `ResourceManager` returns cached resources immediately
3. **Zero Blocking**: No file I/O, no resource creation, just instant access
4. **Smooth Transition**: 60 FPS maintained, no frame drops

**Key Difference from Other Approaches**:
- ❌ **No async loading during transition**: Everything must be ready beforehand
- ❌ **No progressive rendering**: Resources must exist, no placeholders
- ❌ **No frame-time loading**: Cannot spread loading across frames
- ✅ **Aggressive preloading**: Preload everything for connected maps
- ✅ **Background preparation**: Load in background when map loads
- ✅ **Instant transitions**: Zero loading time when transition occurs

## Detailed Design: Aggressive Preloading System

### 1. Resource Preloader Service

**Purpose**: Preload all resources for connected maps when a map loads. Ensures resources are ready before transitions occur.

**Interface**:
```csharp
public interface IResourcePreloaderService
{
    // Preload resources for a map and all its connected maps
    void PreloadMapResources(string mapId);
    
    // Check if all resources for a map are ready
    bool AreResourcesReady(string mapId);
    
    // Get preload status for a specific resource
    bool IsResourceReady(string resourceId);
}
```

**Implementation**:
- Subscribe to `MapLoadedEvent`
- For each loaded map:
  1. Analyze connected maps from `MapDefinition.Connections`
  2. For each connected map, determine required resources:
     - **Fonts**: `base:font:game/pokemon` (popup font) - Preload full file data
     - **Audio**: Music from `MusicComponent` of connected map - Preload file paths only (streaming)
     - **Textures**: Popup backgrounds/outlines from `PopupThemeDefinition` - Preload full texture data
  3. Queue resources for preloading
- **Fonts**: Background thread reads file data → Main thread creates `FontSystem` from cached bytes
- **Audio**: Background thread resolves file paths → Cache paths (no file data loading, streaming on-demand)
- **Textures**: Already handled by `ResourceManager` cache (parallel batch loading)
- Mark resources as ready when preloading completes

**Resource Types to Preload**:
- **Fonts**: `base:font:game/pokemon` (common popup font) - **Full file data** (small files, ~100KB)
- **Audio**: Music files from `MusicComponent` of connected maps - **File paths only** (streaming, not full file data)
- **Textures**: Popup backgrounds/outlines from `PopupThemeDefinition` - **Full texture data** (already cached by ResourceManager)
- **Tilesets**: Already handled by `MapLoaderSystem.PreloadTilesets()` ✅

**Key Behavior**:
- Preloading happens **when map loads**, not during transition
- Resources are **guaranteed ready** before transition can occur
- No loading during `MapTransitionEvent` processing

### 2. Map Resource Predictor

**Purpose**: Analyze map definitions to determine which resources will be needed for transitions.

**Interface**:
```csharp
public interface IMapResourcePredictor
{
    // Get all resources needed for a map (fonts, audio, textures)
    IReadOnlyList<string> GetRequiredResources(string mapId);
    
    // Get resources for all connected maps
    IReadOnlyList<string> GetConnectedMapResources(string mapId);
}
```

**Implementation**:
- Analyze `MapDefinition` for:
  - `MapSectionId` → `MapSectionDefinition` → `PopupThemeDefinition` → fonts, textures
  - `MusicId` → audio files
  - `Connections` → recurse for connected map resources
- Cache predictions per map (avoid re-analysis)
- Return resource IDs that need preloading

**Resource Discovery Flow**:
```
MapDefinition
  ├─ MusicId → AudioDefinition → AudioPath → Audio resource ID
  ├─ MapSectionId → MapSectionDefinition
  │   └─ PopupTheme → PopupThemeDefinition
  │       ├─ Background → PopupBackgroundDefinition → Texture resource ID
  │       └─ Outline → PopupOutlineDefinition → Texture resource ID
  └─ Connections → Connected MapDefinitions (recurse)
```

### 3. Background Resource Loader

**Purpose**: Load file data on background threads, create GPU resources on main thread.

**Implementation**:
- **Background Thread**: Prepare resources for fast access
  - **Font files**: Read raw bytes (small files, ~100KB) → Cache bytes
  - **Audio files**: Resolve file paths → Cache paths (NOT full file data, streaming)
  - **Texture files**: Already handled by `ResourceManager` parallel batch loading
- **Main Thread**: Create GPU resources from cached data
  - `FontSystem`: Create from cached font bytes (fast)
  - `Texture2D`: Create from cached texture bytes (already cached by ResourceManager)
  - `VorbisReader`: Create from cached file path (instant, streams during playback)
- **Cache Strategy**:
  - **Fonts**: Cache raw file bytes (small, ~100KB per font)
  - **Audio**: Cache file paths only (streaming, ~64KB buffer per active stream)
  - **Textures**: Cache GPU resources in `ResourceManager` (already implemented)
  - Mark resources as ready when creation completes

**Thread Safety**:
- File I/O on background thread ✅
- GPU resource creation on main thread ✅
- Use `EventBus.SendOnMainThread()` for completion notifications
- Thread-safe resource state tracking

### 4. Resource Ready Validation

**Purpose**: Ensure all required resources are ready before allowing transitions.

**Implementation**:
- When `MapTransitionEvent` fires, systems check if resources are ready
- If resources not ready (edge case), log warning and use fallback
- In normal operation, resources should always be ready (preloaded)

**Validation Points**:
- `MapPopupSystem`: Check font and texture readiness before creating popup
- `MapMusicSystem`: Check audio readiness before playing music
- Log warnings if resources not ready (indicates preloading failure)

**Edge Case Handling**:
- If resource not ready (preloading failed or unexpected transition):
  - Log warning
  - Load synchronously as fallback (better than crashing)
  - This should be rare in normal operation

## Implementation Plan

### Single Phase: Aggressive Preloading System

**Goal**: Preload all resources for connected maps when a map loads. Zero loading during transitions.

**Components**:
1. `ResourcePreloaderService`: Core preloading logic
2. `MapResourcePredictor`: Resource prediction from map definitions
3. Background loading infrastructure for file I/O
4. Resource ready state tracking

**Implementation Steps**:

1. **Create ResourcePreloaderService**
   - Subscribe to `MapLoadedEvent`
   - For each loaded map, analyze connected maps
   - Queue resources for preloading
   - Track preload state per resource

2. **Create MapResourcePredictor**
   - Analyze `MapDefinition` to discover required resources
   - Cache predictions per map
   - Return resource IDs for preloading

3. **Background File I/O**
   - Load raw file bytes on background threads
   - Cache bytes in memory for fast resource creation
   - Notify main thread when file I/O completes

4. **Main Thread GPU Resource Creation**
   - Create `FontSystem` from cached font bytes
   - Create `Texture2D` from cached texture bytes (if not already cached)
   - Mark resources as ready when creation completes

5. **Resource Ready Validation**
   - Systems check resource readiness before use
   - Log warnings if resources not ready (edge case)
   - Fallback to synchronous loading if needed (should be rare)

**Benefits**:
- ✅ Resources ready before transition
- ✅ Zero blocking during `MapTransitionEvent`
- ✅ Instant transitions (Pokemon-style)
- ✅ Smooth 60 FPS maintained

**Estimated Impact**: 95-100% reduction in transition lag (eliminates all loading during transition)

**Estimated Effort**: 3-4 days

## Resource Loading Optimization Details

### Font Loading Optimization

**Current**: Synchronous `FontSystem` creation from file data

**Optimized**:
1. **Preload**: Load font file data on background thread, cache raw bytes
2. **Fast Creation**: Create `FontSystem` from cached data (fast, on main thread)
3. **Cache**: Font systems are already cached by `ResourceManager` ✅

**Implementation**:
```csharp
// Background thread: Load file data
var fontData = modManifest.ModSource.ReadFile(actualRelativePath);
_fontDataCache[resourceId] = fontData;

// Main thread: Create FontSystem (fast)
var fontSystem = new FontSystem();
fontSystem.AddFont(_fontDataCache[resourceId]);
```

**Estimated Speedup**: 50-70% faster (file I/O moved to background)

### Texture Loading Optimization

**Current**: Synchronous texture loading, but already cached ✅

**Optimized**:
- Textures are already cached by `ResourceManager`
- Preload textures for connected maps (already done for tilesets)
- Preload popup textures when map loads

**Implementation**:
- Extend `MapLoaderSystem.PreloadTilesets()` to also preload popup textures
- Use existing `LoadTexturesBatch()` for parallel loading

**Estimated Speedup**: Minimal (already optimized), but ensures ready before transition

### Audio Loading Optimization (Streaming)

**Current**: Synchronous full-file loading into memory, then `VorbisReader` creation

**Problem**: Music files can be several MB. Preloading full file data for multiple maps uses significant memory.

**Optimized Approach: Streaming**:
1. **Preload File Paths**: Cache file paths/references, not full file data
2. **Stream on Demand**: Create `VorbisReader` from file path or stream when needed
3. **Memory Efficient**: `VorbisReader` streams audio data during playback (~64KB buffer vs ~32MB full file)

**Implementation**:
```csharp
// Preload: Cache file path/reference (not full file data)
var audioDef = _modManager.GetDefinition<AudioDefinition>(resourceId);
var virtualPath = _pathResolver.ResolveResourcePath(resourceId, audioDef.AudioPath);
var (modId, actualRelativePath) = ModPathParser.ParseModPath(virtualPath);
_audioPathCache[resourceId] = (modId, actualRelativePath); // Cache path, not data

// Transition-time: Create VorbisReader from file path (streams during playback)
var modManifest = _modManager.GetModManifest(modId);
var filePath = modManifest.ModSource.GetFilePath(actualRelativePath); // Get actual file path
var reader = new VorbisReader(filePath); // Streams from disk, doesn't load full file
```

**Alternative: Stream from ModSource** (if file path not available):
```csharp
// Create stream wrapper that reads from ModSource on-demand
var stream = new ModSourceStream(modManifest.ModSource, actualRelativePath);
var reader = new VorbisReader(stream, closeOnDispose: true); // Streams during playback
```

**Benefits**:
- ✅ **Memory Efficient**: ~64KB buffer per active stream vs ~32MB per full file
- ✅ **Fast Creation**: Creating `VorbisReader` from path is instant (no file I/O)
- ✅ **Scalable**: Can preload paths for many maps without memory issues
- ✅ **VorbisReader Already Supports Streaming**: Uses NVorbis which streams by default

**Memory Comparison**:
- **Full File Loading**: 10 maps × 3MB music = 30MB memory
- **Streaming**: 1 active stream × 64KB buffer = 64KB memory
- **Savings**: ~99.8% memory reduction

**Estimated Speedup**: 
- File I/O eliminated (VorbisReader handles streaming)
- Instant `VorbisReader` creation (just opens file handle)
- No memory allocation for full file data

## Thread Safety Considerations

### MonoGame Resource Creation

**Constraint**: GPU resources (`Texture2D`, `FontSystem`, `Effect`) must be created on the main thread.

**Solution**:
- Background thread: File I/O only (read raw bytes)
- Main thread: Create GPU resources from cached bytes
- Use `EventBus.SendOnMainThread()` for completion callbacks

### Resource Manager Thread Safety

**Current**: `ResourceManager` uses locks for thread safety ✅

**Optimized**:
- Preloading can use background threads for file I/O
- Main thread creates GPU resources
- Existing locking mechanism handles synchronization

## Memory Considerations

### Preloading Memory Usage

**Risk**: Preloading resources for multiple maps increases memory usage.

**Mitigation**:
- Preload only directly connected maps (not all maps)
- Use LRU cache for preloaded resources
- Unload preloaded resources when maps unload
- Limit preload scope (e.g., only preload for 2-3 connected maps)

**Estimated Memory Increase**:
- **Fonts**: ~100KB per font (small files, acceptable)
- **Audio**: ~64KB per active stream (streaming, not full files) - **Major savings**
- **Textures**: Already cached by ResourceManager (LRU eviction)
- **Total**: ~1-2 MB per map (mostly fonts + texture cache, audio streams on-demand)

**Memory Comparison**:
- **Without Streaming**: 10 maps × 3MB music = 30MB (full file loading)
- **With Streaming**: 10 maps × 64KB buffer = 640KB (streaming)
- **Savings**: ~99% memory reduction for audio

## Performance Targets

### Current Performance
- Map transition lag: ~100-200ms (estimated)
- Frame drops: 2-5 frames during transition

### Target Performance
- Map transition lag: <16ms (1 frame at 60 FPS)
- Frame drops: 0 frames during transition
- Smooth 60 FPS maintained

### Measurement Strategy
- Profile `MapTransitionEvent` processing time
- Measure resource loading times
- Track frame time during transitions
- Use performance counters for detailed analysis

## Implementation Details

### Resource Preloading Flow

```
1. MapLoaderSystem.LoadMap() completes
   ↓
2. MapLoadedEvent fired
   ↓
3. ResourcePreloaderService receives event
   ↓
4. MapResourcePredictor analyzes connected maps
   ↓
5. Resource IDs queued for preloading:
   - Fonts: base:font:game/pokemon
   - Audio: Music from connected maps
   - Textures: Popup backgrounds/outlines
   ↓
6. Background Thread: Prepare resources
   - Read font bytes → Cache (small files, ~100KB)
   - Resolve audio file paths → Cache paths (NOT full file data)
   - Textures: Already handled by ResourceManager parallel batch loading
   ↓
7. Main Thread: Create GPU resources
   - Create FontSystem from cached font bytes
   - Create Texture2D from cached texture bytes (already cached)
   - Audio: Paths cached, VorbisReader created on-demand (streaming)
   ↓
8. Resources ready for instant access
   - Fonts: Ready in memory
   - Textures: Ready in GPU memory
   - Audio: Paths ready, streams on-demand (memory efficient)
```

### Transition-Time Flow (Zero Loading)

```
1. Player crosses map boundary
   ↓
2. MapTransitionEvent fired
   ↓
3. MapMusicSystem: Get cached audio path → Create VorbisReader from path (instant, streams)
   ↓
4. MapPopupSystem: Get cached font → Get cached textures (instant)
   ↓
5. Popup created instantly, music streams instantly
   ↓
6. Zero loading time, smooth 60 FPS, memory efficient
```

**Audio Streaming Details**:
- `VorbisReader` created from file path (instant, no file I/O)
- Audio streams from disk during playback (~64KB buffer)
- No full file loaded into memory
- Seamless playback with minimal memory footprint

### Edge Case: Resource Not Ready

If a resource is not ready (preloading failed or unexpected transition):
1. Log warning (indicates preloading issue)
2. Load synchronously as fallback (better than crashing)
3. This should be rare in normal operation
4. Investigate why preloading failed

## Testing Strategy

### Unit Tests
- Resource preloading logic
- Resource prediction accuracy
- Thread safety of async loading

### Integration Tests
- Map transition performance
- Resource loading during transitions
- Memory usage during preloading

### Performance Tests
- Measure transition lag before/after
- Profile resource loading times
- Track frame time during transitions
- Memory usage profiling

## Future Enhancements

### Advanced Preloading
- **Direction-Based Preloading**: Preload resources for maps in player's movement direction first
- **Multi-Level Preloading**: Preload resources for maps 2-3 connections away (if memory allows)
- **Dynamic Preload Priority**: Prioritize preloading based on player behavior patterns

### Resource Management
- **Memory-Aware Preloading**: Unload preloaded resources when maps unload
- **LRU Cache for Preloaded Resources**: Evict least recently used preloaded resources
- **Resource Compression**: Compress preloaded resources in memory, decompress on-demand

### Performance Monitoring
- **Preload Success Tracking**: Monitor preload success rate
- **Transition Performance Metrics**: Track transition lag to ensure zero loading
- **Resource Ready Validation**: Alert if resources not ready during transition (indicates preload failure)

## References

- MonoGame async loading best practices
- Game engine resource management patterns
- Thread safety for GPU resources
- Incremental loading strategies
