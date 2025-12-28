# GBA Audio Accuracy Research: mGBA Emulation vs Hardware

## Executive Summary

**KEY FINDING**: mGBA offers **TWO audio modes**, and the user complaint likely stems from hearing mGBA with **XQ Audio enabled** (enhanced quality) versus Porycon3's **hardware-accurate** extraction. The music doesn't match because mGBA's XQ Audio mode deliberately bypasses the noisy 8-bit mixing that occurs on real GBA hardware.

## The Two Faces of mGBA Audio

### 1. **Authentic Mode (Default)**
- Replicates GBA hardware exactly
- 8-bit mixing with quantization artifacts
- Noisy, crunchy sound (especially with multiple voices)
- Matches real GBA/Game Boy Player output
- Uses Blargg's blip-buf algorithm for accurate resampling

### 2. **XQ Audio Mode (Optional Enhancement)**
- Found under Settings → Enhancements → "XQ GBA Audio"
- Only available in nightly builds
- Only works with MP2K/Sappy sound engine games
- Intercepts MP2K audio calls and replaces 8-bit mixing with:
  - **16-bit mixing** (reduces noise dramatically)
  - **Higher sample rates** (64 kHz in some emulators)
  - **Better interpolation** (cubic instead of none)
  - **Floating-point arithmetic** (precision)

## Why GBA Music Sounds "Bad" on Hardware

### The 8-Bit Mixing Problem

Most GBA games use Nintendo's MP2K (MusicPlayer2000/Sappy) sound engine, which has a fundamental design flaw:

```
GBA Hardware Limitation:
├─ DirectSound channels: 2 x 8-bit DAC outputs
├─ CPU must mix all voices in software
└─ MP2K mixes in 8-bit buffers before final DAC

Problem:
├─ Each voice adds quantization error
├─ 8 simultaneous voices = 8x accumulated noise
└─ Result: Audible hiss and crunch
```

From the research:
> "The SDK's audio mixer mixes every channel in 8-bit before also outputting at the final 8-bit DAC. This means every additional DirectSound channel will add more noise, and with enough channels it can get pretty bad."

### Technical Breakdown

**GBA Audio Hardware**:
- 2 DirectSound channels (8-bit signed PCM)
- 4 PSG channels (Game Boy compatibility)
- Final output: 10-bit (0-1023)
- PWM DAC: typically 65536 Hz, 8-bit depth
- **Nearest-neighbor resampling** (no interpolation) → aliasing

**MP2K Software Mixing**:
1. Load 8-bit sample
2. Apply volume/envelope → **8-bit buffer** ❌
3. Add reverb → **same 8-bit buffer** ❌
4. Mix multiple voices → **accumulate in 8-bit** ❌
5. Output to hardware → another 8→10→8 bit conversion ❌

Each step accumulates quantization error because there's no headroom for precision.

## What "Correct" GBA Music Actually Sounds Like

### On Real Hardware
- Noisy (especially games with 8+ simultaneous voices)
- Aliasing artifacts from nearest-neighbor resampling
- Low sample rates (often 11-13 kHz, some games use 7.8 kHz!)
- Crunchy, lo-fi character
- Example: Most Pokémon games sound notably compressed

### High-Quality Exceptions
Games that implemented **16-bit mixing** (custom engines):
- **Golden Sun series** (Camelot custom mixer)
- Much cleaner audio despite same hardware
- Proves GBA is capable, but most games didn't bother

### mGBA XQ Audio
- Sounds **significantly better** than hardware
- Cleaner, clearer, higher fidelity
- **NOT authentic** to original hardware
- Comparable to modern remaster quality

## Porycon3's Current Implementation

### What Porycon3 Does (Analysis)

**Sample Extraction** (`SoundExtractor.cs`):
```csharp
// Line 341-343: Normalizes DirectSound samples to ±64 amplitude
// This matches PSG output level (4-bit = 1/4 of 8-bit range)
data = NormalizeSampleVolume(data);
```
- ✅ Loads 8-bit WAV files from pokeemerald
- ✅ Normalizes quiet samples (±64 amplitude minimum)
- ✅ Matches PSG level for balanced mixing
- ⚠️ Converts 16-bit samples to 8-bit if present (line 287-298)

**SF2 Soundfont Building** (`Sf2Builder.cs`):
```csharp
// Line 604-614: Converts 8-bit samples to 16-bit for SF2
result[i] = (short)((data8Bit[i] - 128) * 256);
```
- ✅ Converts 8-bit to 16-bit for SF2 format
- ⚠️ **Does NOT emulate SF2 ADSR** (line 386-389, intentionally disabled)
- ⚠️ Uses basic amplitude scaling (256x multiplier)

**MIDI Synthesis** (`MidiToOggConverter.cs`):
- Uses **MeltySynth** library (software synthesizer)
- Renders at 44.1 kHz sample rate
- Uses soundfont for sample playback
- **Clean 16-bit mixing** (modern synthesizer quality)

### The Gap

**Porycon3's audio quality**:
- Input: 8-bit samples from pokeemerald
- Processing: Clean 16-bit SF2 synthesis
- Output: High-quality 44.1 kHz OGG files

**Result**: Sounds like **mGBA with XQ Audio OFF** (authentic) but rendered at high quality through a modern synthesizer. This should actually be **MORE accurate** to hardware than XQ mode, but cleaner than playing through a Game Boy Player because:
1. Original 8-bit samples preserved
2. No additional high-quality mixing applied
3. But: Clean synthesizer (no hardware noise floor)

## The User's Complaint: "Doesn't sound like mGBA"

### Most Likely Scenario

The user has been listening to **mGBA with XQ Audio ENABLED**, which:
- Bypasses 8-bit mixing entirely
- Uses 16-bit mixing with better interpolation
- Sounds dramatically cleaner than hardware
- Is **NOT authentic** but sounds much better

When comparing to Porycon3:
- Porycon3 uses **authentic 8-bit samples**
- mGBA XQ uses **enhanced mixing**
- Result: Porycon3 sounds "worse" because it's more accurate

### Alternative Scenarios

**Scenario 2**: Volume/Balance Issues
- mGBA may boost overall volume
- Porycon3's normalization (±64) might be too conservative
- PSG vs DirectSound balance could be off

**Scenario 3**: Missing Audio Enhancements
- mGBA applies some filtering/interpolation even in authentic mode
- Porycon3's MeltySynth may use different interpolation
- Reverb/echo processing differences

## Recommendations

### 1. Verify Which mGBA Mode the User Is Using
```
mGBA → Settings → Enhancements → XQ GBA Audio
```
- If ENABLED: User is comparing to enhanced audio (not authentic)
- If DISABLED: Investigate other differences

### 2. Consider Offering Two Extraction Modes

**Mode A: Hardware Authentic** (current)
- 8-bit samples
- Minimal normalization
- Matches real GBA hardware
- "Noisy but accurate"

**Mode B: Enhanced Quality** (potential)
- Boost sample resolution to 16-bit during extraction
- Apply noise reduction/filtering
- Match mGBA XQ Audio quality
- "Cleaner but not authentic"

### 3. Investigate MeltySynth Interpolation

MeltySynth may use different interpolation than mGBA:
- mGBA: Uses blip-buf (band-limited synthesis)
- MeltySynth: Unknown (may use linear/cubic)
- Could affect sound character

### 4. Compare Sample Quality Directly

Extract a single instrument and compare:
1. Original pokeemerald WAV
2. Porycon3 SF2 sample
3. mGBA recording (XQ off)
4. mGBA recording (XQ on)

Look for:
- Amplitude differences
- Frequency response changes
- Noise floor variations

### 5. Volume Normalization Review

Current normalization (±64 amplitude):
```csharp
// Minimum amplitude for normalization: ±64 from center
private const int MinAmplitude = 64;
```

This matches PSG output but may be conservative for DirectSound. Consider:
- Analyzing actual pokeemerald sample amplitudes
- Checking if mGBA applies additional gain
- Testing with ±96 or ±128 for louder samples

## Technical References

### mGBA Audio Implementation
- **Default audio**: Uses Blargg's blip-buf algorithm for accurate resampling
- **XQ Audio**: Experimental MP2K high-level emulation (nightly builds only)
- **Source**: mGBA FAQ, GitHub issues #1552, #1864, #3155

### GBA Hardware Specifications
- DirectSound: 2 channels, 8-bit signed PCM, timer-based sample rate
- PSG: 4 channels (2 square, 1 wave, 1 noise)
- Final DAC: 10-bit output, PWM modulation at ~16.77 MHz
- Typical PWM rate: 65536 Hz (8-bit depth)
- **Critical**: Nearest-neighbor resampling causes significant aliasing

### MP2K/Sappy Sound Driver
- Used in ~90% of GBA games
- 8-bit mixing buffers (design flaw)
- Quantization noise accumulates with voice count
- Games with 8+ voices sound noticeably noisy

### High-Quality Alternatives
- **ipatix's HQ mixer**: 16-bit internal mixing, single 8→16 bit conversion at final output
- **Golden Sun engine**: Custom 16-bit mixer (~63 kHz sample rate)
- **NanoBoyAdvance HLE**: 64 kHz, floating-point, cubic interpolation

## Conclusion

The discrepancy between Porycon3 and mGBA likely stems from:

1. **mGBA XQ Audio** being enabled (most likely)
2. **Volume normalization** being too conservative
3. **Synthesizer interpolation** differences (MeltySynth vs mGBA)

**Porycon3's current approach is technically correct** for hardware-accurate extraction. If the goal is to match **mGBA XQ Audio** (enhanced mode), the implementation would need significant changes to apply high-quality mixing similar to NanoBoyAdvance's HQ mixer.

**Recommendation**: First confirm which mGBA mode the user is comparing against before making changes.

---

## Sources

- [HQ sound in native GBA games is entirely possible | GBAtemp.net](https://gbatemp.net/threads/hq-sound-in-native-gba-games-is-entirely-possible.625549/)
- [mGBA FAQ](https://mgba.io/faq.html)
- [mgba-XQ-Audio README](https://github.com/Dmac9244/mgba-XQ-Audio/blob/master/README.md)
- [Game Boy Advance Audio | jsgroth's blog](https://jsgroth.dev/blog/posts/gba-audio/)
- [NanoBoyAdvance GitHub](https://github.com/nba-emu/NanoBoyAdvance)
- [ipatix's gba-hq-mixer](https://github.com/ipatix/gba-hq-mixer)
- [mGBA GitHub Issue #1552](https://github.com/mgba-emu/mgba/issues/1552)
- [Sound Quality & Palette Customizing - mGBA Forums](https://forums.mgba.io/showthread.php?tid=2071)
- [GBA sound quality discussion - nesdev.org](https://forums.nesdev.org/viewtopic.php?t=12214)
- [Why was the GBA sound so poor? - nesdev.org](https://forums.nesdev.org/viewtopic.php?t=19688)
