# Porycon3 Services Layer

This directory contains the core conversion services for Porycon3, responsible for orchestrating the map conversion pipeline from pokeemerald-expansion format to MonoBall format.

## Services

### MapConversionService.cs
**Main Orchestrator** - Coordinates the entire conversion pipeline.

**Key responsibilities:**
- Scans and discovers maps from pokeemerald-expansion project
- Orchestrates the 5-step conversion process
- Manages file I/O for input/output
- Tracks conversion results and metrics

**Conversion Pipeline:**
1. Read map definition (JSON)
2. Read metatiles for primary + secondary tilesets
3. Read map binary data (metatile indices)
4. Process into Tiled-compatible layers
5. Write output JSON

**Usage:**
```csharp
var service = new MapConversionService(
    inputPath: "/path/to/pokeemerald-expansion",
    outputPath: "/path/to/output",
    region: "hoenn",
    verbose: true
);

var maps = service.ScanMaps();
foreach (var map in maps)
{
    var result = service.ConvertMap(map);
    if (!result.Success)
        Console.WriteLine($"Failed: {map} - {result.Error}");
}
```

### MetatileProcessor.cs
**Layer Processing** - Converts metatile data into Tiled layers.

**Key responsibilities:**
- Processes 2x2 metatile grids into individual tiles
- Distributes tiles across 3 layers (Bg3, Bg2, Bg1) based on layer type
- Handles tile flip flags (horizontal/vertical)
- Converts tile IDs to Tiled GIDs

**Layer Distribution:**
- **Normal** (0x00): Bg2 = bottom tiles, Bg1 = top tiles
- **Covered** (0x01): Bg3 = bottom tiles, Bg2 = top tiles
- **Split** (0x02): Bg3 = bottom tiles, Bg1 = top tiles

**Output:**
- Bg3: Ground layer (rendered under player)
- Bg2: Object layer (player/NPCs walk here)
- Bg1: Overhead layer (roofs, treetops)

### TilesetBuilder.cs
**Tileset Management** - Builds optimized tilesets with GID mapping.

**Phase 1 (Current):**
- Placeholder implementation for GID tracking
- Deduplicates tiles by ID, palette, and flip flags
- Maintains GID mapping for serialization

**Phase 2 (Future):**
- Full ImageSharp integration
- PNG tileset generation
- Palette application
- Tile deduplication with image comparison

## Dependencies

These services depend on:
- **Infrastructure Layer**: MapJsonReader, MetatileBinReader, MapBinReader
- **Models Layer**: MapData, Metatile, TileData, LayerData, ConversionResult

## Architecture Notes

The services follow a **pipeline architecture** where each service has a single, focused responsibility:

1. **MapConversionService** - Orchestration
2. **MetatileProcessor** - Data transformation
3. **TilesetBuilder** - Resource optimization

This separation allows for:
- Easy unit testing
- Clear separation of concerns
- Future extensibility (e.g., adding new layer types)
- Performance optimization (e.g., parallel processing)

## Performance Considerations

**Current Status:**
- Sequential processing (one map at a time)
- In-memory processing (no streaming)
- Basic GID mapping (no image processing)

**Future Optimizations:**
- Parallel map processing with TPL
- Streaming for large maps
- Tileset caching and reuse
- Memory pooling for tile arrays

## Error Handling

All services use explicit exception handling with detailed error messages:
- File not found errors include full paths
- Invalid data errors include context (map name, position)
- All errors are propagated to ConversionResult for reporting

## Testing Strategy

**Unit Tests:**
- MetatileProcessor: Layer distribution logic
- TilesetBuilder: GID mapping and deduplication

**Integration Tests:**
- MapConversionService: Full pipeline with sample data
- Verify output format matches Tiled specification

**Performance Tests:**
- Measure conversion time per map
- Track memory usage for large maps
- Validate GC pressure
