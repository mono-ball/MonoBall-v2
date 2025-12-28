# Code Review: GBA Music Extraction - Voicegroup Parsing and SF2 Building

**Reviewer**: Code Review Agent
**Date**: 2025-12-27
**Files Analyzed**:
- `/Porycon3/Services/Sound/VoicegroupParser.cs`
- `/Porycon3/Services/Sound/VoiceDefinition.cs`
- `/Porycon3/Services/Sound/Sf2Builder.cs`
- `/Porycon3/Services/Sound/SoundExtractor.cs`
- `/Porycon3/Services/Sound/PsgSampleGenerator.cs`

---

## Code Review Summary

### ✅ Strengths
- Comprehensive parsing of all GBA voice types (DirectSound, PSG, keysplit)
- Well-structured separation of concerns with dedicated parsers
- Proper handling of SF2 format specification (RIFF chunks, generators, zones)
- Good documentation and comments explaining GBA-specific behavior
- PSG samples use correct amplitude range (±64 from center)

### 🔴 Critical Issues

#### 1. **MASTER VOICEGROUP LOSES PER-SONG ASSIGNMENTS**
**Location**: `SoundExtractor.cs:100-137`
**Impact**: HIGH - Songs will have incorrect instruments

The "master voicegroup" approach merges all voicegroups by taking the most common voice for each program slot. This fundamentally breaks the GBA music system design:

```csharp
// PROBLEM: BuildMasterVoicegroup() creates a single merged voicegroup
private Voicegroup BuildMasterVoicegroup(IReadOnlyDictionary<string, Voicegroup> allVoicegroups)
{
    var master = new Voicegroup { Name = "master" };

    // Excludes keysplit/drumset voicegroups
    var songVoicegroups = allVoicegroups.Values
        .Where(vg => !vg.Name.Contains("keysplit") && !vg.Name.Contains("drumset"))
        .ToList();

    for (var program = 0; program < 128; program++)
    {
        // Takes the MOST COMMON voice across all songs
        var mostCommon = voiceCounts.MaxBy(kv => kv.Value.count);
        master.Voices[program] = mostCommon.Value.voice;
    }
}
```

**Why This Is Wrong**:
- Each song in pokeemerald has its own voicegroup (e.g., `voicegroup_mus_route101`, `voicegroup_mus_littleroot`)
- Songs intentionally use different samples/instruments in the same program slots
- Example: Route 101 might use `piano_1` in program 0, but Littleroot uses `organ_2` in program 0
- The "master" approach will pick whichever is more common, making half the songs sound wrong

**Correct Approach**:
Need to preserve per-song voicegroup assignments and create separate SF2 banks or use MIDI Bank Select (CC0) messages.

---

#### 2. **KEYSPLIT/KEYSPLIT_ALL VOICE RESOLUTION IS INCOMPLETE**
**Location**: `VoicegroupParser.cs:314-340`
**Impact**: HIGH - Multi-sample instruments won't work correctly

The parser stores keysplit voice references but doesn't fully resolve the chain:

```csharp
// PROBLEM: Stores the reference but doesn't resolve nested keysplits
case KeysplitVoice ks:
    var keysplitVg = _voicegroupParser.GetVoicegroup(ks.VoicegroupName);
    var keysplitTable = _voicegroupParser.GetKeysplitTable(ks.KeysplitTableName);
    if (keysplitVg != null && keysplitTable != null)
    {
        return BuildKeysplitInstrument(sf2, keysplitVg, keysplitTable, name);
    }
    break;
```

**Issues**:
1. Keysplit voicegroups can contain OTHER keysplit voices (nested references)
2. The code doesn't recursively resolve these chains
3. Missing validation that the keysplit table entries point to valid voice indices

**Example Failure Scenario**:
```
voicegroup_piano_keysplit:
  voice 0: keysplit voicegroup_piano_samples, keysplit_piano

keysplit_piano:
  split 48, 0  -> maps to voice 0 in voicegroup_piano_samples
  split 60, 1
  split 72, 2

But voice 0 might ITSELF be a keysplit! Chain not resolved.
```

---

#### 3. **SF2 ADSR ENVELOPES COMPLETELY DISABLED**
**Location**: `Sf2Builder.cs:383-390`
**Impact**: MEDIUM - All instruments have instant attack/release

The code intentionally disables SF2 ADSR generators:

```csharp
/// <summary>
/// Number of generators per instrument zone.
/// keyRange, velRange, overridingRootKey, sampleModes, sampleID = 5
///
/// Note: We intentionally DON'T use SF2 ADSR generators because:
/// 1. PSG envelopes (0-15 scale) vs DirectSound (0-255) have completely different semantics
/// 2. SF2 envelope model doesn't match GBA m4a hardware envelope behavior
/// 3. Incorrect conversion causes "wave oscillation" artifacts
/// The samples themselves already contain the correct amplitude characteristics.
/// </summary>
private const int GeneratorsPerZone = 5;
```

**Why This Is Wrong**:
- DirectSound samples are RAW RECORDINGS, they don't contain envelope information
- The GBA m4a engine applies envelopes in real-time during playback
- Disabling SF2 envelopes means every note plays with instant attack/release
- This causes clicks and unnatural sound

**Evidence**:
The code has commented-out ADSR conversion functions (lines 441-481) but doesn't use them. The conversion logic exists but is disabled.

**Impact on Sound**:
- Sustained instruments (strings, pads) will have clicking attacks
- No natural fade-out on release
- Percussion might sound acceptable (short attack/release)
- Melodic instruments will sound robotic

---

#### 4. **PSG AMPLITUDE CALCULATION MAY BE INCORRECT**
**Location**: `PsgSampleGenerator.cs:22-26`
**Impact**: MEDIUM - Volume imbalance between PSG and DirectSound

```csharp
// PSG amplitude: ±64 from center (64-192 range)
// This matches the natural GBA PSG output level (4-bit volume = ~1/4 of 8-bit range)
private const byte PsgHigh = 192;
private const byte PsgLow = 64;
```

**Concerns**:
1. **GBA PSG Volume Scale**: The GBA's PSG channels have a 4-bit volume control (0-15), but the comment assumes this directly maps to 1/4 of 8-bit range
2. **Hardware Mixing**: The GBA's hardware mixer may not linearly blend PSG and DirectSound at the same levels
3. **No Verification**: The ±64 value appears to be an educated guess without hardware measurement

**Need to Verify**:
- Actual GBA hardware output levels for PSG vs DirectSound
- Whether the 4-bit volume (0-15) is pre-mixer gain or post-mixer output
- If the mixer applies different scaling to PSG vs DirectSound channels

---

#### 5. **SAMPLE NORMALIZATION AFFECTS ALL DIRECTSOUND SAMPLES**
**Location**: `SoundExtractor.cs:341-343, 851-899`
**Impact**: MEDIUM - May distort carefully mastered samples

```csharp
// Normalize DirectSound samples to target amplitude (±48 to match PSG)
// This ensures balanced volume between sampled instruments and PSG tones
data = NormalizeSampleVolume(data);
```

The normalization function:
```csharp
private static byte[] NormalizeSampleVolume(byte[] data)
{
    // Find the peak deviation from center (128)
    var maxDeviation = 0;
    // ...

    // Only boost quiet samples - don't reduce loud ones
    if (maxDeviation >= MinAmplitude) // MinAmplitude = 64
        return data;

    // Calculate boost factor to reach minimum amplitude (±64)
    var boostFactor = (double)MinAmplitude / maxDeviation;

    // Cap boost to avoid extreme amplification (max 4x)
    boostFactor = Math.Min(boostFactor, 4.0);
}
```

**Issues**:
1. **Target Changed in Comment**: The function comment says "±48" but `MinAmplitude = 64`
2. **Boosts All Quiet Samples**: Many samples might be intentionally quiet (soft instruments, distant sounds)
3. **Lost Dynamic Range**: Samples carefully mastered with dynamics will be normalized
4. **No Per-Instrument Analysis**: Some instruments (piano) might be quieter by design

**Better Approach**:
- Preserve original sample dynamics
- Handle volume differences through SF2 attenuation generators or MIDI velocity
- Only normalize if sample is below a noise threshold (~4, which the code does check)

---

### 🟡 Major Issues

#### 6. **LOOP POINT HANDLING MAY BE OFF-BY-ONE**
**Location**: `Sf2Builder.cs:530-538`
**Impact**: MEDIUM - Potential clicks or gaps in looping samples

```csharp
// Loop positions: SF2 spec says loopEnd points to first sample AFTER the loop
// So for a loop from 0-99, loopEnd should be 100, not 99
var loopStart = Math.Clamp(sample.LoopStart, 0, sample.Data.Length - 1);
// loopEnd in SF2 is exclusive (one past the last sample), clamp to data length
var loopEnd = Math.Clamp(sample.LoopEnd, loopStart + 1, sample.Data.Length);

bw.Write(sampleStart);                      // Start
bw.Write(sampleEnd);                        // End (points to first terminator)
bw.Write(sampleStart + loopStart);          // Loop start (first sample of loop)
bw.Write(sampleStart + loopEnd);            // Loop end (first sample AFTER loop)
```

**Concerns**:
1. **WAV smpl Chunk Format**: The `LoadWavFile` function reads loop points from the smpl chunk (lines 306-328)
2. **WAV vs SF2 Conventions**: Need to verify if WAV loop points are inclusive/exclusive
3. **No Validation**: The smpl chunk might have corrupted or invalid loop points
4. **Edge Case**: What if `loopEnd == loopStart`? This creates a 1-sample loop

**Need to Verify**:
- pokeemerald WAV files' actual smpl chunk loop point format
- Whether any samples have `loopStart == 0` and `loopEnd == sampleLength` (full loop)

---

#### 7. **ROOT KEY IS HARDCODED TO 60 FOR ALL SAMPLES**
**Location**: `SoundExtractor.cs:357`
**Impact**: MEDIUM - All samples pitch-shifted from same reference

```csharp
// Default root key is 60 (middle C)
return (data, sampleRate, 60, loopStart, loopEnd);
```

**Issues**:
1. **No Root Key Detection**: GBA samples might be recorded at different root pitches
2. **BaseMidiKey Ignored**: The `VoiceDefinition.BaseMidiKey` field exists but isn't used when adding samples
3. **Pitch Accuracy**: If a sample was recorded at C5 but marked as C4, it will play a full octave wrong

**Evidence**:
```csharp
// DirectSoundVoice has BaseMidiKey field
public record DirectSoundVoice : VoiceDefinition
{
    public required int BaseMidiKey { get; init; } // Root pitch (usually 60 = C3)
    public required string SampleName { get; init; }
}
```

**Correct Approach**:
Pass `voice.BaseMidiKey` to `sf2.AddSample()` instead of hardcoded 60.

---

#### 8. **PROGRAMMABLE WAVE ALWAYS USES SINE WAVE**
**Location**: `SoundExtractor.cs:441-447`
**Impact**: MEDIUM - Wrong timbre for wave channel instruments

```csharp
case ProgrammableWaveVoice pw:
    // Use sine wave as default for now
    if (_sampleIndexCache.TryGetValue("psg_wave_sine", out var pwIndex))
    {
        return sf2.AddSimpleInstrument(name, pwIndex, pw.Envelope);
    }
    break;
```

**Issues**:
1. **WaveName Ignored**: The voice has `pw.WaveName` (e.g., "ProgrammableWaveData_3") but it's not used
2. **Only Sine Used**: All programmable wave voices will sound identical
3. **No Wave Data Loading**: The code generates standard patterns but doesn't load actual wave data from pokeemerald

**Correct Approach**:
1. Parse wave data from pokeemerald source (wave patterns are in the sound data)
2. Match `pw.WaveName` to loaded wave patterns
3. Generate sample from the actual 32-byte wave definition

---

#### 9. **NOISE LFSR IMPLEMENTATION MAY NOT MATCH GBA HARDWARE**
**Location**: `PsgSampleGenerator.cs:79-112`
**Impact**: LOW-MEDIUM - Noise channel sounds different from hardware

```csharp
// GBA uses a 15-bit or 7-bit LFSR for noise generation
ushort lfsr = 0x7FFF;
var lfsrMask = shortPeriod ? 0x7F : 0x7FFF;
var xorBit = shortPeriod ? 6 : 14;

// Step LFSR at approximately 262kHz / divider
// For simplicity, we step every few samples
var stepInterval = shortPeriod ? 2 : 4;

// LFSR feedback: XOR bits 0 and 1 (or bit at xorBit for short)
var feedback = ((lfsr >> 0) ^ (lfsr >> 1)) & 1;
lfsr = (ushort)((lfsr >> 1) | (feedback << xorBit));
```

**Concerns**:
1. **Feedback Polynomial**: The GBA's LFSR uses a specific polynomial (needs verification)
2. **Step Rate Approximation**: "step every few samples" is imprecise - should calculate exact frequency divider
3. **No Frequency Control**: GBA noise channel has frequency/period control that affects the LFSR step rate
4. **XOR Bits**: Need to verify the exact tap positions (currently XORing bits 0 and 1)

**GBA Noise Hardware**:
- The GBA noise channel is based on Game Boy hardware
- Uses a 15-bit LFSR with taps at positions 0 and 1
- Short mode uses 7-bit LFSR
- Clock frequency is divided by selected divisor

---

### 🟡 Suggestions

#### 10. **Keysplit Voicegroup Filtering is Fragile**
**Location**: `SoundExtractor.cs:104-107`

```csharp
var songVoicegroups = allVoicegroups.Values
    .Where(vg => !vg.Name.Contains("keysplit") && !vg.Name.Contains("drumset"))
    .ToList();
```

**Issues**:
- String-based filtering is error-prone
- A song named "keysplit_intro" would be incorrectly excluded
- No clear distinction between "utility" and "song" voicegroups

**Better Approach**:
- Add a `VoicegroupType` enum (Song, Keysplit, Drumset)
- Parse from file location or explicit markers
- Use type-based filtering instead of name matching

---

#### 11. **No Validation of Sample Data**
**Location**: `SoundExtractor.cs:206-210`

```csharp
// Sample not found - add a placeholder silently (reduce noise)
var placeholderData = new byte[1000];
Array.Fill(placeholderData, (byte)128); // Silence
var placeholderIndex = sf2.AddSample(sampleName, placeholderData, 22050, 60);
_sampleIndexCache[sampleName] = placeholderIndex;
```

**Issues**:
- Missing samples are silently replaced with silence
- No way to detect which instruments will be broken
- Should at least log warnings or create a report

**Better Approach**:
- Keep track of missing samples
- Generate a report after processing
- Consider using a beep/test tone instead of silence for easier debugging

---

#### 12. **8-bit to 16-bit Conversion May Introduce DC Offset**
**Location**: `Sf2Builder.cs:604-615`

```csharp
private static short[] ConvertTo16Bit(byte[] data8Bit)
{
    var result = new short[data8Bit.Length];
    for (var i = 0; i < data8Bit.Length; i++)
    {
        // Convert unsigned 8-bit (0-255) to signed 16-bit (-32768 to 32767)
        // Use 256 to avoid overflow (max becomes 32512, close enough)
        result[i] = (short)((data8Bit[i] - 128) * 256);
    }
    return result;
}
```

**Concerns**:
1. **"Close Enough"**: Max value is 32512 instead of 32767 (0.8% quieter)
2. **Asymmetric Range**: -32768 to +32512 is asymmetric
3. **No DC Offset Correction**: If 8-bit sample has DC offset, it's preserved and amplified

**Better Approach**:
```csharp
result[i] = (short)((data8Bit[i] - 128) * 257); // 257 gives full range
// Or analyze for DC offset and subtract mean before conversion
```

---

### 📊 Metrics Analysis

**File Complexity**:
- `SoundExtractor.cs`: 900 lines - Consider splitting into:
  - `SoundfontBuilder.cs` (SF2 construction)
  - `SampleLoader.cs` (WAV/sample loading)
  - `MidiConverter.cs` (MIDI to OGG)

**Test Coverage**:
- No unit tests found for critical functions
- Should add tests for:
  - Voicegroup parsing (all voice types)
  - SF2 generation (validate format)
  - Sample normalization edge cases
  - Loop point handling

---

## 🎯 Priority Action Items

1. **CRITICAL**: Fix master voicegroup approach - implement per-song SF2 banks
2. **CRITICAL**: Resolve keysplit voice chains correctly (recursive lookup)
3. **HIGH**: Use BaseMidiKey from voice definitions instead of hardcoded 60
4. **HIGH**: Implement SF2 ADSR envelopes with proper GBA-to-SF2 conversion
5. **MEDIUM**: Load actual programmable wave data instead of always using sine
6. **MEDIUM**: Validate and fix loop point off-by-one errors
7. **MEDIUM**: Review PSG amplitude (±64) against actual GBA hardware measurements
8. **MEDIUM**: Reconsider sample normalization - preserve original dynamics
9. **LOW**: Verify LFSR noise implementation against GBA hardware specs
10. **LOW**: Add logging/reporting for missing samples

---

## 🔍 Detailed Analysis: Critical Bug #1

### Master Voicegroup Problem - Deep Dive

**The Core Issue**:
```
pokeemerald song structure:
  mus_route101.mid references voicegroup_mus_route101
    program 0: voice_directsound 60, 0, DirectSoundWaveData_sc88pro_fretless_bass, ...
    program 1: voice_square_1 60, 0, 0, 2, ...

  mus_littleroot.mid references voicegroup_mus_littleroot
    program 0: voice_directsound 60, 0, DirectSoundWaveData_sc88pro_jazz_guitar, ...
    program 1: voice_noise 60, 0, 1, ...

Current code creates MASTER voicegroup:
  program 0: whichever voice appears more often across all songs
  program 1: whichever voice appears more often across all songs

Result: Both songs use the SAME voices, losing per-song customization
```

**Example Impact**:
If 60% of songs use bass in program 0 and 40% use guitar, ALL songs will get bass, making the 40% sound wrong.

**Correct Solution Options**:

**Option A: Multiple SF2 Banks (Recommended)**
```csharp
// Create one SF2 bank per song
foreach (var song in _midiConfigParser.GetAllSongs())
{
    var voicegroup = _voicegroupParser.GetVoicegroup(song.VoicegroupName);
    if (voicegroup != null)
    {
        BuildVoicegroupInstruments(sf2, voicegroup, bankNumber);
        song.BankNumber = bankNumber;
        bankNumber++;
    }
}
```

**Option B: One SF2 with Bank Select Messages**
- Modify MIDI files to insert Bank Select (CC0) messages
- Each song switches to its assigned bank before playing
- More complex but single SF2 file

**Option C: Separate SF2 per Song** (Not recommended)
- Generate `route101.sf2`, `littleroot.sf2`, etc.
- Wastes space (duplicate samples)
- MIDI converter needs per-song SF2 lookup

---

## Files Reviewed

| File | Lines | Complexity | Issues Found |
|------|-------|-----------|--------------|
| VoicegroupParser.cs | 395 | Medium | 2 (keysplit resolution) |
| VoiceDefinition.cs | 143 | Low | 0 |
| Sf2Builder.cs | 664 | High | 4 (ADSR, loops, conversion) |
| SoundExtractor.cs | 901 | Very High | 6 (master VG, normalization, root key) |
| PsgSampleGenerator.cs | 250 | Medium | 2 (amplitude, LFSR) |

---

## Conclusion

The codebase demonstrates a solid understanding of the SF2 format and GBA sound architecture, but has several critical bugs that will cause incorrect music playback:

1. **Master voicegroup merging loses per-song instrument assignments** (CRITICAL)
2. **ADSR envelopes disabled causing unnatural sound** (HIGH)
3. **Root key hardcoded causing pitch errors** (MEDIUM-HIGH)

These issues explain why the extracted music doesn't match the original game sound. The architecture is sound, but the implementation needs targeted fixes in the areas identified above.

**Estimated Fix Effort**:
- Master voicegroup fix: 4-6 hours (requires MIDI bank architecture)
- ADSR implementation: 2-3 hours (conversion functions exist, need integration)
- Root key fix: 1 hour (simple parameter passing)
- Other issues: 4-6 hours combined

**Total**: ~12-16 hours to address all critical and high-priority issues.
