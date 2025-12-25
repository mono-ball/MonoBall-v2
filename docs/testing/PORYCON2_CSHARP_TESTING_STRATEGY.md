# Porycon2 C# Rewrite - Comprehensive Testing Strategy

## Executive Summary

This document outlines a comprehensive testing strategy for the C# rewrite of porycon2, a Pokemon Emerald asset conversion tool. The strategy ensures the C# version maintains feature parity with the Python implementation while achieving superior performance, reliability, and maintainability.

### Key Goals
- **Correctness**: Output must match Python version byte-for-byte where applicable
- **Performance**: 2-3x faster execution than Python implementation
- **Reliability**: 90%+ code coverage, zero regression tolerance
- **Maintainability**: Clean, testable architecture with clear separation of concerns

---

## 1. Unit Testing Strategy

### 1.1 Core Components Requiring Unit Tests

#### A. Metatile Processing (`MetatileProcessor`)
**Critical functionality tested:**
- `DetermineTilesetForMetatile()` - Primary/secondary tileset assignment
- `DetermineTilesetForTile()` - Tile ID resolution (0-511 primary, 512+ secondary)
- `ValidateMetatileBounds()` - Bounds checking logic
- `ProcessSingleMetatile()` - Rendering, GID assignment, deduplication

**Test cases:**
```csharp
[Theory]
[InlineData(0, "primary", 0)]           // First primary metatile
[InlineData(511, "primary", 511)]       // Last primary metatile
[InlineData(512, "secondary", 0)]       // First secondary metatile
[InlineData(1023, "secondary", 511)]    // Last secondary metatile
public void DetermineTilesetForMetatile_ReturnsCorrectTileset(
    int metatileId, string expectedTileset, int expectedActualId)
{
    // Test primary/secondary boundary logic
}

[Theory]
[InlineData(0, 511, true)]              // Valid primary range
[InlineData(512, 1023, true)]           // Valid secondary range
[InlineData(-1, 0, false)]              // Negative ID
[InlineData(1024, 1535, false)]         // Out of bounds
public void ValidateMetatileBounds_ValidatesCorrectly(
    int metatileId, int maxTiles, bool expected)
{
    // Test bounds validation
}
```

#### B. Sprite Extraction (`SpriteExtractor`)
**Critical functionality tested:**
- `AnalyzeSpriteSheet()` - Frame layout detection (16x16, 16x32, 32x32)
- `DetectMaskColor()` - Transparency color detection
- `ApplyTransparency()` - Mask color replacement
- `GenerateAnimations()` - Animation metadata creation

**Test cases:**
```csharp
[Theory]
[InlineData(64, 64, 32, 32, 2)]         // 32x32 sprites, 2 frames
[InlineData(144, 32, 16, 32, 9)]        // 16x32 sprites, 9 frames
[InlineData(256, 16, 16, 16, 16)]       // 16x16 sprites, 16 frames
public void AnalyzeSpriteSheet_DetectsFrameLayout(
    int width, int height, int expectedFrameW, int expectedFrameH, int expectedCount)
{
    // Test frame detection logic
}

[Fact]
public void DetectMaskColor_IdentifiesBackgroundColor()
{
    // Create image with 60% background color
    var image = CreateTestImage(100, 100, Color.FromArgb(255, 0, 255));
    var result = _extractor.DetectMaskColor(image);

    Assert.Equal("#FF00FF", result);
}
```

#### C. Animation Parsing (`AnimationParser`)
**Critical functionality tested:**
- `ParseAnimationDefinitions()` - C header file parsing
- `ExtractPhysicalFrameMapping()` - Frame index extraction
- `ParseLoopPoints()` - Audio loop point detection

**Test cases:**
```csharp
[Fact]
public void ParseAnimationDefinitions_ParsesCHeaderCorrectly()
{
    var cCode = @"
        const struct SpriteFrameImage gObjectEventPic_Brendan[] = {
            {gObjectEventPic_BrendanNormal, 0x800, 0x800},
            {gObjectEventPic_BrendanMachBike, 0x800, 0x800},
        };
    ";

    var result = _parser.ParseAnimationDefinitions(cCode);

    Assert.Equal(2, result.Count);
    Assert.Equal("BrendanNormal", result[0].Name);
}
```

#### D. Map Conversion (`MapConverter`)
**Critical functionality tested:**
- `LoadMapData()` - JSON parsing and validation
- `ProcessTilesets()` - Primary/secondary tileset loading
- `GenerateTMXLayers()` - Layer creation (bottom, top, collision)
- `WriteMapOutput()` - TMX/JSON serialization

**Test cases:**
```csharp
[Fact]
public void LoadMapData_ParsesJsonCorrectly()
{
    var json = File.ReadText("TestData/Maps/route101.json");
    var map = _converter.LoadMapData(json);

    Assert.Equal("Route 101", map.Name);
    Assert.Equal(20, map.Width);
    Assert.Equal(20, map.Height);
    Assert.NotNull(map.Layout);
}

[Fact]
public async Task ProcessTilesets_LoadsPrimaryAndSecondary()
{
    var result = await _converter.ProcessTilesets("general", "route101");

    Assert.NotNull(result.Primary);
    Assert.NotNull(result.Secondary);
    Assert.Equal(512, result.Primary.MetatileCount);
}
```

#### E. Palette Loading (`PaletteLoader`)
**Critical functionality tested:**
- `LoadPalette()` - JASC/GPL/ACT palette parsing
- `ConvertTo32BitColor()` - Color depth conversion
- `ApplyGammaCorrection()` - Gamma adjustment

**Test cases:**
```csharp
[Fact]
public void LoadPalette_ParsesJASCFormat()
{
    var jasc = @"JASC-PAL
0100
16
255 0 255
0 0 0
...";

    var palette = _loader.LoadPalette(jasc, PaletteFormat.JASC);

    Assert.Equal(16, palette.Colors.Count);
    Assert.Equal(Color.FromArgb(255, 0, 255), palette.Colors[0]);
}
```

### 1.2 Mock Strategies

#### File System Mocking
Use `System.IO.Abstractions` for testable file operations:

```csharp
public interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllText(string path, string contents);
    IEnumerable<string> EnumerateFiles(string path, string pattern);
}

// In tests:
var mockFileSystem = new Mock<IFileSystem>();
mockFileSystem
    .Setup(f => f.ReadAllText("test.json"))
    .Returns("{\"width\": 20, \"height\": 20}");
```

#### External Tool Mocking (Poryscript, etc.)
```csharp
public interface IProcessRunner
{
    ProcessResult Run(string command, string arguments);
}

// In tests:
var mockRunner = new Mock<IProcessRunner>();
mockRunner
    .Setup(r => r.Run("poryscript", It.IsAny<string>()))
    .Returns(new ProcessResult { ExitCode = 0, Output = "Success" });
```

#### Image Processing Mocking
```csharp
public interface IImageProcessor
{
    Image LoadImage(string path);
    void SaveImage(Image image, string path, ImageFormat format);
    Color[,] GetPixels(Image image);
}

// Use in-memory images for testing:
var testImage = new Bitmap(16, 16);
for (int y = 0; y < 16; y++)
    for (int x = 0; x < 16; x++)
        testImage.SetPixel(x, y, Color.Red);
```

### 1.3 Test Data Fixtures

Create reusable test data in `Tests/Fixtures/`:

```
Tests/
└── Fixtures/
    ├── Maps/
    │   ├── route101.json          # Sample map
    │   ├── rustboro_city.json     # City map
    │   └── battle_frontier.json   # Complex map
    ├── Tilesets/
    │   ├── general.bin            # Primary tileset
    │   ├── route101.bin           # Secondary tileset
    │   └── palettes/
    │       └── general.pal        # JASC palette
    ├── Sprites/
    │   ├── brendan_normal.png     # 16x32 sprite
    │   ├── may_running.png        # 16x32 sprite
    │   └── animations/
    │       └── brendan.h          # Animation data
    └── Expected/
        ├── route101.tmx           # Expected TMX output
        ├── route101_bottom.png    # Expected layer image
        └── brendan_normal.json    # Expected manifest
```

**Fixture loading helper:**
```csharp
public static class TestFixtures
{
    private static readonly string BasePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures");

    public static string GetMapJson(string name) =>
        File.ReadAllText(Path.Combine(BasePath, "Maps", $"{name}.json"));

    public static byte[] GetTilesetData(string name) =>
        File.ReadAllBytes(Path.Combine(BasePath, "Tilesets", $"{name}.bin"));

    public static Image GetSpriteImage(string name) =>
        Image.FromFile(Path.Combine(BasePath, "Sprites", $"{name}.png"));
}
```

---

## 2. Integration Testing Strategy

### 2.1 End-to-End Conversion Tests

#### Test Structure
```csharp
[Collection("Integration")]
public class EndToEndConversionTests : IClassFixture<PoryconTestEnvironment>
{
    private readonly PoryconTestEnvironment _env;

    public EndToEndConversionTests(PoryconTestEnvironment env)
    {
        _env = env;
    }

    [Fact]
    public async Task ConvertMap_Route101_ProducesCorrectOutput()
    {
        // Arrange
        var inputPath = _env.GetTestMap("route101");
        var outputPath = _env.CreateTempOutputDir();

        // Act
        var converter = new MapConverter(_env.FileSystem, _env.ProcessRunner);
        await converter.ConvertMapAsync(inputPath, outputPath);

        // Assert
        var tmxPath = Path.Combine(outputPath, "route101.tmx");
        Assert.True(File.Exists(tmxPath));

        var tmxContent = File.ReadAllText(tmxPath);
        var expectedTmx = TestFixtures.GetExpected("route101.tmx");

        // Normalize line endings and compare
        Assert.Equal(
            NormalizeXml(expectedTmx),
            NormalizeXml(tmxContent)
        );
    }
}
```

#### Test Environment Setup
```csharp
public class PoryconTestEnvironment : IDisposable
{
    public IFileSystem FileSystem { get; }
    public IProcessRunner ProcessRunner { get; }
    private readonly string _tempDir;

    public PoryconTestEnvironment()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);

        // Copy test fixtures to temp directory
        CopyFixturesToTemp();

        FileSystem = new PhysicalFileSystem();
        ProcessRunner = new RealProcessRunner();
    }

    public string GetTestMap(string name) =>
        Path.Combine(_tempDir, "Maps", $"{name}.json");

    public string CreateTempOutputDir()
    {
        var dir = Path.Combine(_tempDir, "Output", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
```

### 2.2 Python Output Comparison Tests

**Critical requirement:** C# output must match Python output exactly.

```csharp
[Theory]
[InlineData("route101")]
[InlineData("rustboro_city")]
[InlineData("battle_frontier_outside_east")]
public async Task ConvertMap_MatchesPythonOutput(string mapName)
{
    // Arrange
    var inputPath = _env.GetTestMap(mapName);
    var csharpOutput = _env.CreateTempOutputDir();

    // Get Python reference output
    var pythonOutput = TestFixtures.GetExpected($"{mapName}_python");

    // Act
    var converter = new MapConverter(_env.FileSystem, _env.ProcessRunner);
    await converter.ConvertMapAsync(inputPath, csharpOutput);

    // Assert - Compare TMX structure
    var csharpTmx = XDocument.Load(Path.Combine(csharpOutput, $"{mapName}.tmx"));
    var pythonTmx = XDocument.Load(Path.Combine(pythonOutput, $"{mapName}.tmx"));

    AssertTmxEquivalent(pythonTmx, csharpTmx);

    // Assert - Compare layer images (pixel-by-pixel)
    var csharpBottom = new Bitmap(Path.Combine(csharpOutput, $"{mapName}_bottom.png"));
    var pythonBottom = new Bitmap(Path.Combine(pythonOutput, $"{mapName}_bottom.png"));

    AssertImagesEqual(pythonBottom, csharpBottom);
}

private void AssertImagesEqual(Bitmap expected, Bitmap actual)
{
    Assert.Equal(expected.Width, actual.Width);
    Assert.Equal(expected.Height, actual.Height);

    int differences = 0;
    for (int y = 0; y < expected.Height; y++)
    {
        for (int x = 0; x < expected.Width; x++)
        {
            var expectedPixel = expected.GetPixel(x, y);
            var actualPixel = actual.GetPixel(x, y);

            if (expectedPixel.ToArgb() != actualPixel.ToArgb())
            {
                differences++;
                _output.WriteLine(
                    $"Pixel difference at ({x},{y}): " +
                    $"Expected {expectedPixel}, Actual {actualPixel}"
                );
            }
        }
    }

    // Allow up to 0.1% pixel difference for rounding errors
    var maxAllowedDiff = (expected.Width * expected.Height) * 0.001;
    Assert.True(differences <= maxAllowedDiff,
        $"Too many pixel differences: {differences} > {maxAllowedDiff}");
}
```

### 2.3 Performance Benchmarks

Use BenchmarkDotNet for performance regression detection:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class ConversionBenchmarks
{
    private MapConverter _converter;
    private string _inputPath;
    private string _outputPath;

    [GlobalSetup]
    public void Setup()
    {
        _converter = new MapConverter(
            new PhysicalFileSystem(),
            new RealProcessRunner()
        );
        _inputPath = TestFixtures.GetMapJson("route101");
        _outputPath = Path.GetTempPath();
    }

    [Benchmark(Baseline = true)]
    public async Task ConvertRoute101()
    {
        await _converter.ConvertMapAsync(_inputPath, _outputPath);
    }

    [Benchmark]
    public async Task ConvertRustboroCity()
    {
        var input = TestFixtures.GetMapJson("rustboro_city");
        await _converter.ConvertMapAsync(input, _outputPath);
    }

    [Benchmark]
    public async Task ConvertBattleFrontier()
    {
        var input = TestFixtures.GetMapJson("battle_frontier_outside_east");
        await _converter.ConvertMapAsync(input, _outputPath);
    }
}

// Expected results (baseline: Python version)
// | Method                  | Mean       | Allocated   | vs Python |
// |------------------------ |-----------:|------------:|----------:|
// | ConvertRoute101         | 150.2 ms   | 12.5 MB     | 2.8x      |
// | ConvertRustboroCity     | 320.8 ms   | 28.3 MB     | 3.1x      |
// | ConvertBattleFrontier   | 450.1 ms   | 35.7 MB     | 2.9x      |
```

---

## 3. Testing Frameworks & Libraries

### 3.1 Primary Framework: **xUnit**

**Rationale:**
- Modern, extensible architecture
- Excellent async/await support (critical for I/O operations)
- Built-in parallel test execution
- Theory/InlineData for parameterized tests
- Strong community support and ecosystem

**Alternatives considered:**
- NUnit: Older, less modern async support
- MSTest: Limited extensibility, slower innovation

### 3.2 Mocking Framework: **NSubstitute**

**Rationale:**
- Clean, readable syntax (`Substitute.For<IInterface>()`)
- Simpler than Moq for most use cases
- Excellent async support
- Better error messages

**Example:**
```csharp
var fileSystem = Substitute.For<IFileSystem>();
fileSystem.FileExists(Arg.Any<string>()).Returns(true);
fileSystem.ReadAllText("test.json").Returns("{\"width\": 20}");

// Verify calls
fileSystem.Received(1).ReadAllText("test.json");
```

**Alternative:** Moq (more features, steeper learning curve)

### 3.3 Assertion Library: **FluentAssertions**

**Rationale:**
- Natural language assertions
- Better error messages
- Extensive collection/object comparison
- Image comparison utilities

**Example:**
```csharp
// Instead of:
Assert.Equal(expected, actual);
Assert.True(value > 0);

// Use:
actual.Should().Be(expected);
value.Should().BePositive();

// Complex object comparison:
actualMap.Should().BeEquivalentTo(expectedMap, options => options
    .Excluding(m => m.Timestamp)
    .WithStrictOrdering()
);

// Collection assertions:
actualPixels.Should().HaveCount(256)
    .And.OnlyContain(p => p.A == 255);
```

### 3.4 Additional Libraries

**BenchmarkDotNet** - Performance testing
```xml
<PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
```

**System.IO.Abstractions** - File system mocking
```xml
<PackageReference Include="System.IO.Abstractions" Version="21.0.2" />
<PackageReference Include="System.IO.Abstractions.TestingHelpers" Version="21.0.2" />
```

**Verify** - Snapshot testing
```xml
<PackageReference Include="Verify.Xunit" Version="24.2.0" />
```

**FakeItEasy** - Alternative mocking (lightweight)
```xml
<PackageReference Include="FakeItEasy" Version="8.3.0" />
```

---

## 4. Test Organization & Project Structure

### 4.1 Solution Structure

```
Porycon2.sln
├── src/
│   ├── Porycon2.Core/                 # Core library
│   │   ├── Converters/
│   │   ├── Parsers/
│   │   ├── Processors/
│   │   └── Utilities/
│   ├── Porycon2.CLI/                  # Command-line interface
│   └── Porycon2.Abstractions/         # Interfaces & contracts
│
└── tests/
    ├── Porycon2.Core.Tests/           # Unit tests
    │   ├── Converters/
    │   ├── Parsers/
    │   ├── Processors/
    │   └── Utilities/
    ├── Porycon2.Integration.Tests/    # Integration tests
    │   ├── EndToEnd/
    │   ├── PythonComparison/
    │   └── Performance/
    ├── Porycon2.Benchmarks/           # BenchmarkDotNet tests
    └── Fixtures/                       # Shared test data
        ├── Maps/
        ├── Tilesets/
        ├── Sprites/
        └── Expected/
```

### 4.2 Test Project Configuration

**Porycon2.Core.Tests.csproj:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="NSubstitute" Version="5.1.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="System.IO.Abstractions" Version="21.0.2" />
    <PackageReference Include="System.IO.Abstractions.TestingHelpers" Version="21.0.2" />
    <PackageReference Include="Verify.Xunit" Version="24.2.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Porycon2.Core\Porycon2.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="Fixtures\**\*">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

### 4.3 Naming Conventions

**Test Classes:**
```csharp
// Unit tests: {ClassName}Tests
public class MetatileProcessorTests { }
public class SpriteExtractorTests { }

// Integration tests: {Feature}IntegrationTests
public class MapConversionIntegrationTests { }
public class SpriteExtractionIntegrationTests { }
```

**Test Methods:**
```csharp
// Pattern: {MethodName}_{Scenario}_{ExpectedBehavior}
[Fact]
public void DetermineTilesetForMetatile_PrimaryRange_ReturnsPrimaryTileset() { }

[Fact]
public void ValidateMetatileBounds_NegativeId_ReturnsFalse() { }

[Theory]
[InlineData(0, true)]
[InlineData(512, true)]
[InlineData(-1, false)]
public void ValidateMetatileBounds_VariousIds_ReturnsExpected(int id, bool expected) { }
```

### 4.4 Test Categories

Use traits to organize tests:

```csharp
// Fast unit tests (run on every build)
[Trait("Category", "Unit")]
[Trait("Speed", "Fast")]
public class MetatileProcessorTests { }

// Slow integration tests (run before commit)
[Trait("Category", "Integration")]
[Trait("Speed", "Slow")]
public class MapConversionIntegrationTests { }

// Performance tests (run nightly)
[Trait("Category", "Performance")]
[Trait("Speed", "Slow")]
public class ConversionBenchmarks { }

// Python comparison tests (run before release)
[Trait("Category", "Comparison")]
[Trait("Speed", "Slow")]
public class PythonOutputComparisonTests { }
```

**Filter tests in CI:**
```bash
# Fast tests only (PR validation)
dotnet test --filter "Speed=Fast"

# All except performance (pre-commit)
dotnet test --filter "Category!=Performance"

# Full suite (nightly)
dotnet test
```

---

## 5. CI/CD Integration

### 5.1 GitHub Actions Workflow

**.github/workflows/test.yml:**
```yaml
name: Test Suite

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  unit-tests:
    name: Unit Tests
    runs-on: ubuntu-latest
    strategy:
      matrix:
        dotnet-version: ['9.0.x']

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: ${{ matrix.dotnet-version }}

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run Unit Tests
      run: |
        dotnet test \
          --no-build \
          --configuration Release \
          --filter "Category=Unit" \
          --logger "trx;LogFileName=test-results.trx" \
          --collect:"XPlat Code Coverage" \
          -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

    - name: Upload Test Results
      uses: actions/upload-artifact@v4
      if: always()
      with:
        name: test-results
        path: '**/test-results.trx'

    - name: Upload Coverage
      uses: codecov/codecov-action@v4
      with:
        files: '**/coverage.opencover.xml'
        flags: unittests

  integration-tests:
    name: Integration Tests
    runs-on: ubuntu-latest
    needs: unit-tests

    steps:
    - uses: actions/checkout@v4
      with:
        submodules: true  # Clone pokeemerald for test fixtures

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'

    - name: Setup Python (for comparison tests)
      uses: actions/setup-python@v5
      with:
        python-version: '3.14'

    - name: Install Python Dependencies
      run: |
        cd porycon2
        python -m venv venv
        source venv/bin/activate
        pip install -r requirements.txt

    - name: Generate Python Reference Output
      run: |
        cd porycon2
        source venv/bin/activate
        python -m porycon.converter \
          --input tests/fixtures/maps/route101.json \
          --output tests/fixtures/expected/python/

    - name: Run Integration Tests
      run: |
        dotnet test \
          --configuration Release \
          --filter "Category=Integration" \
          --logger "trx;LogFileName=integration-results.trx"

    - name: Upload Integration Results
      uses: actions/upload-artifact@v4
      if: always()
      with:
        name: integration-results
        path: '**/integration-results.trx'

  performance-tests:
    name: Performance Benchmarks
    runs-on: ubuntu-latest
    needs: integration-tests
    if: github.event_name == 'push' && github.ref == 'refs/heads/main'

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'

    - name: Run Benchmarks
      run: |
        cd tests/Porycon2.Benchmarks
        dotnet run -c Release --exporters json

    - name: Upload Benchmark Results
      uses: actions/upload-artifact@v4
      with:
        name: benchmark-results
        path: '**/BenchmarkDotNet.Artifacts/results/*.json'

    - name: Compare with Baseline
      run: |
        # Compare current benchmarks with previous run
        # Fail if performance regressed by >10%
        dotnet run --project tools/BenchmarkComparator \
          --baseline benchmarks/baseline.json \
          --current BenchmarkDotNet.Artifacts/results/Porycon2.Benchmarks.ConversionBenchmarks-report.json \
          --threshold 0.10

  code-quality:
    name: Code Quality & Coverage
    runs-on: ubuntu-latest
    needs: unit-tests

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'

    - name: Run All Tests with Coverage
      run: |
        dotnet test \
          --configuration Release \
          --collect:"XPlat Code Coverage" \
          -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover

    - name: Generate Coverage Report
      run: |
        dotnet tool install -g dotnet-reportgenerator-globaltool
        reportgenerator \
          -reports:**/coverage.opencover.xml \
          -targetdir:coverage-report \
          -reporttypes:"Html;Badges"

    - name: Check Coverage Threshold
      run: |
        # Extract coverage percentage
        COVERAGE=$(grep -oP 'Line coverage: \K[0-9.]+' coverage-report/index.html)

        # Fail if coverage < 90%
        if (( $(echo "$COVERAGE < 90.0" | bc -l) )); then
          echo "Coverage $COVERAGE% is below 90% threshold"
          exit 1
        fi

        echo "Coverage: $COVERAGE%"

    - name: Upload Coverage Report
      uses: actions/upload-artifact@v4
      with:
        name: coverage-report
        path: coverage-report/
```

### 5.2 Code Coverage Requirements

**Minimum coverage thresholds:**
- **Overall**: 90%
- **Critical paths** (conversion, rendering): 95%
- **Edge cases** (error handling): 85%
- **Utilities**: 80%

**Excluded from coverage:**
- Auto-generated code
- Program.cs (entry point)
- DTOs/POCOs without logic

**Coverage configuration (.coverletrc.json):**
```json
{
  "exclude": [
    "[*.Tests]*",
    "[*]*.Program",
    "[*]*Dto",
    "[*]*Model"
  ],
  "include": [
    "[Porycon2.Core]*"
  ],
  "threshold": 90,
  "thresholdType": "line",
  "thresholdStat": "total"
}
```

### 5.3 Performance Regression Detection

**Baseline configuration (benchmarks/baseline.json):**
```json
{
  "ConvertRoute101": {
    "mean_ms": 150.2,
    "allocated_mb": 12.5,
    "max_regression": 0.10
  },
  "ConvertRustboroCity": {
    "mean_ms": 320.8,
    "allocated_mb": 28.3,
    "max_regression": 0.10
  }
}
```

**Regression detection script (tools/BenchmarkComparator/Program.cs):**
```csharp
public class BenchmarkComparator
{
    public static int Main(string[] args)
    {
        var baseline = JsonSerializer.Deserialize<Baseline>(
            File.ReadAllText(args[0])
        );
        var current = ParseBenchmarkResults(args[1]);
        var threshold = double.Parse(args[2]);

        bool failed = false;
        foreach (var (name, baselineData) in baseline)
        {
            if (!current.TryGetValue(name, out var currentData))
                continue;

            var regression = (currentData.MeanMs - baselineData.MeanMs)
                           / baselineData.MeanMs;

            if (regression > threshold)
            {
                Console.WriteLine(
                    $"FAIL: {name} regressed by {regression:P2} " +
                    $"({baselineData.MeanMs}ms -> {currentData.MeanMs}ms)"
                );
                failed = true;
            }
        }

        return failed ? 1 : 0;
    }
}
```

---

## 6. Test Execution Strategy

### 6.1 Development Workflow

**Local development (pre-commit):**
```bash
# Fast feedback loop
dotnet watch test --filter "Speed=Fast"

# Before committing
dotnet test --filter "Category=Unit|Category=Integration"

# Full validation
dotnet test
```

**Pre-push validation:**
```bash
# Run full test suite with coverage
dotnet test --collect:"XPlat Code Coverage"

# Check coverage threshold
dotnet tool run reportgenerator \
  -reports:**/coverage.opencover.xml \
  -targetdir:coverage \
  -reporttypes:Html

# Open coverage report
start coverage/index.html
```

### 6.2 CI Pipeline Stages

**Pull Request:**
1. Unit tests (fast feedback)
2. Integration tests (correctness)
3. Code coverage check (>90%)
4. Python comparison tests (compatibility)

**Main Branch:**
1. All PR checks
2. Performance benchmarks
3. Regression detection
4. Coverage report upload

**Nightly:**
1. Full test suite (including slow tests)
2. Extended performance benchmarks
3. Memory leak detection
4. Cross-platform validation (Windows/Linux/macOS)

### 6.3 Test Parallelization

**xUnit configuration (xunit.runner.json):**
```json
{
  "parallelizeAssembly": true,
  "parallelizeTestCollections": true,
  "maxParallelThreads": -1,
  "diagnosticMessages": false,
  "methodDisplay": "method",
  "methodDisplayOptions": "all"
}
```

**Collection fixtures for shared state:**
```csharp
[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    // This class has no code, serves only as marker
}

[Collection("Database")]
public class MapConverterTests
{
    private readonly DatabaseFixture _fixture;

    public MapConverterTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }
}
```

---

## 7. Specialized Testing Scenarios

### 7.1 Image Comparison Testing

**Pixel-perfect comparison:**
```csharp
public class ImageComparer
{
    public static ImageComparisonResult Compare(
        Bitmap expected,
        Bitmap actual,
        double tolerance = 0.001)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new ImageComparisonResult
            {
                Passed = false,
                Message = "Image dimensions differ"
            };
        }

        int totalPixels = expected.Width * expected.Height;
        int differences = 0;
        var diffImage = new Bitmap(expected.Width, expected.Height);

        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                var expectedPixel = expected.GetPixel(x, y);
                var actualPixel = actual.GetPixel(x, y);

                if (expectedPixel.ToArgb() != actualPixel.ToArgb())
                {
                    differences++;
                    // Highlight difference in red
                    diffImage.SetPixel(x, y, Color.Red);
                }
                else
                {
                    diffImage.SetPixel(x, y, Color.Transparent);
                }
            }
        }

        double diffPercentage = (double)differences / totalPixels;

        return new ImageComparisonResult
        {
            Passed = diffPercentage <= tolerance,
            DifferencePercentage = diffPercentage,
            DifferenceCount = differences,
            DiffImage = diffImage,
            Message = diffPercentage <= tolerance
                ? "Images match"
                : $"{diffPercentage:P2} pixels differ ({differences}/{totalPixels})"
        };
    }
}

// Usage in tests:
[Fact]
public void ConvertMap_Route101_ProducesCorrectBottomLayer()
{
    var expected = TestFixtures.GetExpectedImage("route101_bottom.png");
    var actual = _converter.ConvertMap("route101").BottomLayer;

    var result = ImageComparer.Compare(expected, actual);

    result.Passed.Should().BeTrue(result.Message);

    if (!result.Passed)
    {
        // Save diff image for debugging
        result.DiffImage.Save("route101_diff.png");
        _output.WriteLine($"Diff image saved to route101_diff.png");
    }
}
```

### 7.2 TMX XML Comparison

**Normalize and compare TMX files:**
```csharp
public class TmxComparer
{
    public static void AssertTmxEquivalent(
        XDocument expected,
        XDocument actual)
    {
        // Normalize XML (sort attributes, remove whitespace)
        var normalizedExpected = Normalize(expected);
        var normalizedActual = Normalize(actual);

        // Compare structure
        XNode.DeepEquals(normalizedExpected, normalizedActual)
            .Should().BeTrue("TMX structure should match");

        // Compare layer data (base64-encoded tile indices)
        var expectedLayers = normalizedExpected.Descendants("layer");
        var actualLayers = normalizedActual.Descendants("layer");

        expectedLayers.Should().HaveSameCount(actualLayers);

        foreach (var (expectedLayer, actualLayer) in
            expectedLayers.Zip(actualLayers))
        {
            var expectedData = expectedLayer.Element("data")?.Value;
            var actualData = actualLayer.Element("data")?.Value;

            // Decode base64 and compare tile indices
            var expectedTiles = DecodeTileData(expectedData);
            var actualTiles = DecodeTileData(actualData);

            actualTiles.Should().Equal(expectedTiles);
        }
    }

    private static uint[] DecodeTileData(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var tiles = new uint[bytes.Length / 4];

        for (int i = 0; i < tiles.Length; i++)
        {
            tiles[i] = BitConverter.ToUInt32(bytes, i * 4);
        }

        return tiles;
    }
}
```

### 7.3 Animation Metadata Validation

```csharp
[Fact]
public void ExtractSprite_Brendan_HasCorrectAnimations()
{
    var manifest = _extractor.ExtractSprite("brendan_normal");

    manifest.Animations.Should().HaveCount(4);
    manifest.Animations.Should().Contain(a => a.Name == "walk_down");
    manifest.Animations.Should().Contain(a => a.Name == "walk_up");
    manifest.Animations.Should().Contain(a => a.Name == "walk_left");
    manifest.Animations.Should().Contain(a => a.Name == "walk_right");

    var walkDown = manifest.Animations.First(a => a.Name == "walk_down");
    walkDown.FrameIndices.Should().Equal(new[] { 0, 1, 2, 1 });
    walkDown.FrameDurations.Should().AllSatisfy(d => d.Should().BeApproximately(0.167, 0.01));
    walkDown.Loop.Should().BeTrue();
}
```

---

## 8. Test Data Management

### 8.1 Fixture Organization

**Fixture directory structure:**
```
tests/Fixtures/
├── README.md                    # Fixture documentation
├── Maps/
│   ├── simple/
│   │   └── route101.json        # Small, simple map
│   ├── complex/
│   │   ├── rustboro_city.json   # Medium complexity
│   │   └── battle_frontier.json # High complexity
│   └── edge_cases/
│       ├── empty_map.json       # Edge case: empty
│       └── max_size.json        # Edge case: maximum size
├── Tilesets/
│   ├── primary/
│   │   ├── general.bin
│   │   └── general.pal
│   └── secondary/
│       ├── route101.bin
│       └── route101.pal
├── Sprites/
│   ├── player/
│   │   ├── brendan_normal.png
│   │   └── may_running.png
│   └── npc/
│       ├── gym_leader_roxanne.png
│       └── item_ball.png
└── Expected/
    ├── python/                  # Python reference output
    │   ├── route101.tmx
    │   └── route101_bottom.png
    └── csharp/                  # C# expected output
        ├── route101.tmx
        └── route101_bottom.png
```

### 8.2 Fixture Generation Script

**scripts/generate-test-fixtures.sh:**
```bash
#!/bin/bash

set -e

POKEEMERALD_PATH="${1:-../pokeemerald}"
FIXTURES_PATH="tests/Fixtures"

echo "Generating test fixtures from $POKEEMERALD_PATH"

# Extract sample maps
echo "Extracting maps..."
python porycon2/porycon/converter.py \
  --pokeemerald "$POKEEMERALD_PATH" \
  --map-filter "Route101,RustboroCity_Gym" \
  --output "$FIXTURES_PATH/Maps/simple"

# Extract tilesets
echo "Extracting tilesets..."
cp "$POKEEMERALD_PATH/data/tilesets/primary/general/metatiles.bin" \
   "$FIXTURES_PATH/Tilesets/primary/general.bin"

cp "$POKEEMERALD_PATH/data/tilesets/primary/general/palettes/00.pal" \
   "$FIXTURES_PATH/Tilesets/primary/general.pal"

# Extract sprites
echo "Extracting sprites..."
cp "$POKEEMERALD_PATH/graphics/object_events/pics/people/brendan_normal.png" \
   "$FIXTURES_PATH/Sprites/player/"

# Generate Python reference output
echo "Generating Python reference output..."
cd porycon2
source venv/bin/activate
python -m porycon.converter \
  --input "../$FIXTURES_PATH/Maps/simple/route101.json" \
  --output "../$FIXTURES_PATH/Expected/python/"
deactivate
cd ..

echo "✓ Fixtures generated successfully"
```

### 8.3 Fixture Validation

**Ensure fixtures remain valid:**
```csharp
public class FixtureValidationTests
{
    [Fact]
    public void AllMapFixtures_AreValid()
    {
        var mapFiles = Directory.GetFiles(
            TestFixtures.MapsPath,
            "*.json",
            SearchOption.AllDirectories
        );

        foreach (var mapFile in mapFiles)
        {
            var json = File.ReadAllText(mapFile);
            var map = JsonSerializer.Deserialize<MapData>(json);

            map.Should().NotBeNull($"{mapFile} should deserialize");
            map.Width.Should().BeGreaterThan(0);
            map.Height.Should().BeGreaterThan(0);
            map.Layout.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void AllExpectedOutputs_Exist()
    {
        var requiredFiles = new[]
        {
            "Expected/python/route101.tmx",
            "Expected/python/route101_bottom.png",
            "Expected/python/route101_top.png",
        };

        foreach (var file in requiredFiles)
        {
            var fullPath = Path.Combine(TestFixtures.BasePath, file);
            File.Exists(fullPath).Should().BeTrue(
                $"{file} should exist in fixtures"
            );
        }
    }
}
```

---

## 9. Test Maintenance Guidelines

### 9.1 Test Review Checklist

Before merging test code, verify:

- [ ] **Clear intent**: Test name describes what it validates
- [ ] **Single responsibility**: Each test validates one behavior
- [ ] **Arrange-Act-Assert**: Clear three-phase structure
- [ ] **No magic numbers**: Use named constants or theory data
- [ ] **No test interdependence**: Tests can run in any order
- [ ] **Proper cleanup**: Resources disposed in `Dispose()` or `using`
- [ ] **Fast execution**: Unit tests complete in <100ms
- [ ] **Good error messages**: Failures explain what went wrong
- [ ] **Coverage**: Critical paths covered by tests

### 9.2 Test Code Quality Standards

**Good test example:**
```csharp
[Fact]
public void ProcessMetatile_WithValidData_AssignsCorrectGids()
{
    // Arrange
    const int metatileId = 42;
    const int expectedBottomGid = 100;
    const int expectedTopGid = 101;

    var processor = new MetatileProcessor(_mockRenderer.Object);
    var metatiles = CreateValidMetatileData(count: 512);
    var attributes = new Dictionary<int, int> { [metatileId] = 0 };

    // Act
    var result = processor.ProcessSingleMetatile(
        metatileId,
        "primary",
        metatiles,
        attributes,
        "primary",
        "secondary",
        new Dictionary<ValueTuple<int, string, int>, (Image, Image)>(),
        new Dictionary<byte[], int>(),
        nextGid: expectedBottomGid
    );

    // Assert
    result.MetatileToGid.Should().ContainKey((metatileId, "primary", 0, false))
        .WhoseValue.Should().Be(expectedBottomGid);
    result.MetatileToGid.Should().ContainKey((metatileId, "primary", 0, true))
        .WhoseValue.Should().Be(expectedTopGid);
}
```

**Bad test example (avoid):**
```csharp
[Fact]
public void Test1()  // ❌ Non-descriptive name
{
    var p = new MetatileProcessor(null);  // ❌ Null dependency
    var r = p.ProcessSingleMetatile(42, "a", null, null, "a", "b", null, null, 1);  // ❌ Magic numbers, nulls
    Assert.NotNull(r);  // ❌ Vague assertion
}
```

### 9.3 Refactoring Tests

**When to refactor:**
- Test is hard to understand
- Test is brittle (breaks on minor changes)
- Multiple tests have duplicate setup
- Test takes >5 seconds to run

**Refactoring techniques:**
```csharp
// Extract fixture creation
private MetatileTestData CreateValidMetatileData(int count = 512)
{
    var tiles = new List<(int, int, int)>();
    for (int i = 0; i < count * 8; i++)
    {
        tiles.Add((i % 256, 0, 0));  // tile_id, flip_flags, palette
    }
    return new MetatileTestData { Tiles = tiles };
}

// Use builder pattern for complex objects
var map = new MapBuilder()
    .WithSize(20, 20)
    .WithTileset("general", "route101")
    .WithLayer("bottom", new[] { 1, 2, 3, 4 })
    .Build();

// Extract assertion helpers
private void AssertImageMatches(Bitmap expected, Bitmap actual)
{
    var comparison = ImageComparer.Compare(expected, actual);
    comparison.Passed.Should().BeTrue(comparison.Message);
}
```

---

## 10. Success Metrics

### 10.1 Test Suite Health Indicators

**Target metrics:**
- **Code coverage**: >90% (>95% for critical paths)
- **Test execution time**: <5 minutes for full suite
- **Test reliability**: <1% flaky test rate
- **Test-to-code ratio**: ~1:1 (lines of test code to production code)
- **Bug escape rate**: <5% (bugs found in production vs. caught by tests)

**Monitoring:**
```bash
# Generate test metrics report
dotnet test \
  --logger "trx;LogFileName=test-results.trx" \
  --logger "html;LogFileName=test-report.html"

# Analyze test trends
dotnet tool run test-analyzer \
  --results test-results.trx \
  --baseline previous-results.trx
```

### 10.2 Quality Gates

**PR merge requirements:**
1. All tests pass
2. Code coverage >90%
3. No new warnings
4. Performance regression <10%
5. Integration tests pass with Python comparison

**Release requirements:**
1. All PR requirements
2. Performance benchmarks meet targets (2-3x faster than Python)
3. Memory usage within acceptable range (<50MB for typical maps)
4. No known critical bugs
5. Documentation updated

---

## 11. Appendix

### 11.1 Quick Reference Commands

```bash
# Run all tests
dotnet test

# Run specific category
dotnet test --filter "Category=Unit"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Run benchmarks
cd tests/Porycon2.Benchmarks
dotnet run -c Release

# Watch tests during development
dotnet watch test --filter "Speed=Fast"

# Generate coverage report
reportgenerator \
  -reports:**/coverage.opencover.xml \
  -targetdir:coverage \
  -reporttypes:"Html;Badges"
```

### 11.2 Common Test Patterns

**Testing async methods:**
```csharp
[Fact]
public async Task ConvertMapAsync_ValidInput_Succeeds()
{
    var result = await _converter.ConvertMapAsync(
        TestFixtures.GetMapJson("route101"),
        _tempOutputDir
    );

    result.Should().NotBeNull();
}
```

**Testing exceptions:**
```csharp
[Fact]
public void LoadMap_MissingFile_ThrowsFileNotFoundException()
{
    var act = () => _converter.LoadMap("nonexistent.json");

    act.Should().Throw<FileNotFoundException>()
        .WithMessage("*nonexistent.json*");
}
```

**Testing with theory data:**
```csharp
public static IEnumerable<object[]> MetatileBoundaryTestData =>
    new List<object[]>
    {
        new object[] { 0, "primary", 0 },
        new object[] { 511, "primary", 511 },
        new object[] { 512, "secondary", 0 },
        new object[] { 1023, "secondary", 511 },
    };

[Theory]
[MemberData(nameof(MetatileBoundaryTestData))]
public void DetermineTileset_BoundaryValues_ReturnsExpected(
    int metatileId, string expectedTileset, int expectedActualId)
{
    var (tileset, actualId) = _processor.DetermineTilesetForMetatile(
        metatileId, "primary", "secondary"
    );

    tileset.Should().Be(expectedTileset);
    actualId.Should().Be(expectedActualId);
}
```

### 11.3 Troubleshooting Test Failures

**Common issues and solutions:**

1. **Flaky tests**: Add retry logic with Polly
```csharp
[Fact]
[Retry(3)]
public async Task FlakeyNetworkTest()
{
    // Test code
}
```

2. **Test isolation**: Use `IClassFixture` or `ICollectionFixture`
```csharp
public class MyTests : IClassFixture<DatabaseFixture>
{
    // Tests share database fixture
}
```

3. **Slow tests**: Profile with BenchmarkDotNet
```csharp
[MemoryDiagnoser]
public class SlowTestBenchmark
{
    [Benchmark]
    public void SlowTest() => _tests.MySlowTest();
}
```

---

## Summary

This comprehensive testing strategy ensures the C# rewrite of porycon2:

1. **Maintains correctness** through extensive unit and integration tests
2. **Validates compatibility** with Python version through output comparison
3. **Ensures performance** through automated benchmarks and regression detection
4. **Supports maintainability** through clean test organization and documentation
5. **Enables confidence** through CI/CD integration and quality gates

The strategy prioritizes:
- **Test-first development** (write tests before implementation)
- **Fast feedback** (unit tests <100ms, full suite <5 minutes)
- **Comprehensive coverage** (90%+ with focus on critical paths)
- **Automated validation** (CI/CD integration with quality gates)

By following this strategy, the C# rewrite will achieve superior quality, performance, and reliability compared to the Python implementation.
