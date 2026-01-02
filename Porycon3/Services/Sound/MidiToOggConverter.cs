using MeltySynth;

namespace Porycon3.Services.Sound;

/// <summary>
/// Converts MIDI files to OGG audio using an SF2 soundfont.
/// Detects loop markers ([/]) and embeds loop metadata in the output.
/// </summary>
public class MidiToOggConverter
{
    private readonly string _sf2Path;
    private readonly int _sampleRate;
    private Synthesizer? _synthesizer;

    public MidiToOggConverter(string sf2Path, int sampleRate = 44100)
    {
        _sf2Path = sf2Path;
        _sampleRate = sampleRate;
    }

    /// <summary>
    /// Result of MIDI to OGG conversion including loop information.
    /// </summary>
    public record ConversionResult(
        bool Success,
        int LoopStartSamples,
        int LoopLengthSamples,
        int SampleRate);

    /// <summary>
    /// Convert a MIDI file to OGG with loop support.
    /// </summary>
    /// <param name="midiPath">Path to input MIDI file</param>
    /// <param name="outputPath">Path to output OGG file</param>
    /// <returns>Conversion result with loop information</returns>
    public ConversionResult Convert(string midiPath, string outputPath)
    {
        try
        {
            // Parse MIDI for loop markers, resolution, and tempo map
            var parseResult = FindLoopMarkersAndResolution(midiPath);

            // Preprocess MIDI to filter out GBA m4a modulation-triggered volume drops
            var cleanedMidiPath = PreprocessMidi(midiPath);
            var midiPathToUse = cleanedMidiPath ?? midiPath;

            // Load SF2 if not already loaded
            _synthesizer ??= new Synthesizer(_sf2Path, _sampleRate);

            // Load and sequence the MIDI
            var midiFile = new MidiFile(midiPathToUse);
            var sequencer = new MidiFileSequencer(_synthesizer);

            // Calculate total length - render to end of song
            var totalSeconds = midiFile.Length.TotalSeconds;

            // Ensure we have at least 1 second of audio (some MIDI files may report 0 length)
            if (totalSeconds < 0.1)
            {
                // Very short duration, use minimum
                totalSeconds = 1.0; // Default to 1 second minimum
            }

            var totalSamples = (int)(totalSeconds * _sampleRate);
            if (totalSamples <= 0)
            {
                // Invalid sample count, conversion failed
                return new ConversionResult(false, 0, 0, _sampleRate);
            }

            var leftBuffer = new float[totalSamples];
            var rightBuffer = new float[totalSamples];

            // Render audio (play once, render once)
            sequencer.Play(midiFile, false);
            sequencer.Render(leftBuffer, rightBuffer);

            // Clean up preprocessed MIDI if created
            if (cleanedMidiPath != null && File.Exists(cleanedMidiPath))
            {
                try { File.Delete(cleanedMidiPath); } catch { }
            }

            // Convert loop ticks to sample positions using tempo-accurate conversion
            var loopStartSample = -1; // Use -1 to indicate no loop, not 0
            var loopEndSample = totalSamples;

            if (parseResult.LoopStartTick >= 0 && parseResult.LoopEndTick > parseResult.LoopStartTick)
            {
                loopStartSample = TicksToSamples(parseResult.LoopStartTick, parseResult.TicksPerBeat, parseResult.TempoChanges);
                loopEndSample = TicksToSamples(parseResult.LoopEndTick, parseResult.TicksPerBeat, parseResult.TempoChanges);
            }

            // Convert to 16-bit PCM
            var pcmData = ConvertToPcm16(leftBuffer, rightBuffer, totalSamples);

            // Check if we have a valid loop
            var hasLoop = loopStartSample >= 0 && loopEndSample > loopStartSample;
            var loopLength = hasLoop ? loopEndSample - loopStartSample : 0;

            // Write output (WAV for now, with loop metadata sidecar)
            var wavPath = Path.ChangeExtension(outputPath, ".wav");
            WriteWavWithLoopInfo(wavPath, pcmData, hasLoop ? loopStartSample : -1, loopEndSample);

            // Try to convert to OGG using ffmpeg
            if (TryConvertToOgg(wavPath, outputPath, hasLoop ? loopStartSample : -1, loopLength))
            {
                File.Delete(wavPath); // Clean up WAV
                return new ConversionResult(
                    true,
                    hasLoop ? loopStartSample : 0,
                    loopLength,
                    _sampleRate);
            }

            // If ffmpeg failed, keep the WAV and rename
            if (File.Exists(wavPath))
            {
                var finalWavPath = Path.ChangeExtension(outputPath, ".wav");
                if (wavPath != finalWavPath)
                    File.Move(wavPath, finalWavPath, overwrite: true);
            }

            return new ConversionResult(
                true,
                hasLoop ? loopStartSample : 0,
                loopLength,
                _sampleRate);
        }
        catch
        {
            // Conversion error - result.Success will be false
            return new ConversionResult(false, 0, 0, _sampleRate);
        }
    }

    /// <summary>
    /// Preprocess MIDI to remove ALL CC7 (Volume) and CC1 (Modulation) changes.
    /// Since we now have proper SF2 ADSR envelopes, the GBA m4a volume automation
    /// in the MIDI files is no longer needed and causes conflicts (double envelope effect).
    /// We set all channels to full volume (127) and let the SF2 envelopes handle dynamics.
    /// </summary>
    /// <param name="midiPath">Path to input MIDI file</param>
    /// <returns>Path to cleaned MIDI file, or null if no changes needed</returns>
    private string? PreprocessMidi(string midiPath)
    {
        try
        {
            using var fs = File.OpenRead(midiPath);
            var data = new byte[fs.Length];
            fs.Read(data, 0, data.Length);

            var modified = false;

            // Read header
            if (data.Length < 14) return null;
            var headerLen = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];
            var pos = 8 + headerLen;

            while (pos < data.Length - 8)
            {
                // Read track header
                if (pos + 8 > data.Length) break;
                var chunkType = System.Text.Encoding.ASCII.GetString(data, pos, 4);
                var chunkLen = (data[pos + 4] << 24) | (data[pos + 5] << 16) | (data[pos + 6] << 8) | data[pos + 7];

                if (chunkType != "MTrk")
                {
                    pos += 8 + chunkLen;
                    continue;
                }

                var trackEnd = pos + 8 + chunkLen;
                var p = pos + 8;
                byte runningStatus = 0;

                while (p < trackEnd)
                {
                    // Skip delta time
                    byte b;
                    do
                    {
                        if (p >= trackEnd) break;
                        b = data[p++];
                    } while ((b & 0x80) != 0);

                    if (p >= trackEnd) break;

                    var status = data[p];

                    // Handle running status
                    if (status < 0x80)
                    {
                        status = runningStatus;
                    }
                    else
                    {
                        p++;
                        if (status < 0xF0)
                            runningStatus = status;
                    }

                    if (status == 0xFF) // Meta event
                    {
                        if (p >= trackEnd) break;
                        p++; // meta type
                        var len = 0;
                        do
                        {
                            if (p >= trackEnd) break;
                            b = data[p++];
                            len = (len << 7) | (b & 0x7F);
                        } while ((b & 0x80) != 0);
                        p += len;
                    }
                    else if (status == 0xF0 || status == 0xF7) // SysEx
                    {
                        var len = 0;
                        do
                        {
                            if (p >= trackEnd) break;
                            b = data[p++];
                            len = (len << 7) | (b & 0x7F);
                        } while ((b & 0x80) != 0);
                        p += len;
                    }
                    else if ((status & 0xF0) == 0xB0) // Control Change
                    {
                        var ccNum = data[p];

                        if (ccNum == 7) // Volume - set to max
                        {
                            data[p + 1] = 127;
                            modified = true;
                        }
                        else if (ccNum == 11) // Expression - set to max
                        {
                            data[p + 1] = 127;
                            modified = true;
                        }
                        else if (ccNum == 1) // Modulation - set to 0 (disable)
                        {
                            data[p + 1] = 0;
                            modified = true;
                        }
                        p += 2;
                    }
                    else if ((status & 0xF0) >= 0x80)
                    {
                        var msgLen = GetMidiMessageLength(status) - 1;
                        p += msgLen;
                    }
                }

                pos = trackEnd;
            }

            if (!modified)
                return null;

            // Write to temp file
            var tempPath = Path.Combine(Path.GetTempPath(), $"porycon3_midi_{Guid.NewGuid():N}.mid");
            File.WriteAllBytes(tempPath, data);
            return tempPath;
        }
        catch
        {
            return null; // On error, just use original file
        }
    }

    /// <summary>
    /// MIDI parsing result with loop markers, resolution, and tempo map.
    /// </summary>
    private record MidiParseResult(
        int LoopStartTick,
        int LoopEndTick,
        int TicksPerBeat,
        List<(int tick, int microsecondsPerBeat)> TempoChanges);

    /// <summary>
    /// Find loop start ([) and loop end (]) markers in MIDI file.
    /// Also returns ticks per beat (resolution) and tempo map for accurate timing.
    /// </summary>
    private MidiParseResult FindLoopMarkersAndResolution(string midiPath)
    {
        var loopStart = -1;
        var loopEnd = -1;
        var ticksPerBeat = 480; // Default
        var tempoChanges = new List<(int tick, int microsecondsPerBeat)>();

        try
        {
            using var fs = File.OpenRead(midiPath);
            using var br = new BinaryReader(fs);

            // Read MIDI header
            var header = new string(br.ReadChars(4));
            if (header != "MThd")
                return new MidiParseResult(loopStart, loopEnd, ticksPerBeat, tempoChanges);

            var headerLength = ReadBigEndianInt32(br);
            if (headerLength >= 6)
            {
                br.ReadInt16(); // Format
                br.ReadInt16(); // Number of tracks
                ticksPerBeat = ReadBigEndianInt16(br); // Division (ticks per beat)
                if (headerLength > 6)
                    br.ReadBytes(headerLength - 6);
            }
            else
            {
                br.ReadBytes(headerLength);
            }

            // Read tracks
            while (fs.Position < fs.Length)
            {
                var chunkType = new string(br.ReadChars(4));
                var chunkLength = ReadBigEndianInt32(br);

                if (chunkType != "MTrk")
                {
                    br.ReadBytes(chunkLength);
                    continue;
                }

                var trackEnd = fs.Position + chunkLength;
                var currentTick = 0;

                while (fs.Position < trackEnd)
                {
                    // Read delta time (variable length)
                    currentTick += ReadVariableLength(br);

                    // Read event
                    var status = br.ReadByte();

                    if (status == 0xFF) // Meta event
                    {
                        var metaType = br.ReadByte();
                        var metaLength = ReadVariableLength(br);

                        if (metaType == 0x51 && metaLength == 3) // Set Tempo
                        {
                            // Tempo is 3 bytes, big-endian microseconds per beat
                            var b1 = br.ReadByte();
                            var b2 = br.ReadByte();
                            var b3 = br.ReadByte();
                            var usPerBeat = (b1 << 16) | (b2 << 8) | b3;
                            tempoChanges.Add((currentTick, usPerBeat));
                        }
                        else if (metaType == 0x06) // Marker
                        {
                            var markerBytes = br.ReadBytes(metaLength);
                            var marker = System.Text.Encoding.ASCII.GetString(markerBytes);

                            // Check for loop markers - handle single char or text containing [ ]
                            if (marker.Contains('[') || marker.Equals("loopStart", StringComparison.OrdinalIgnoreCase))
                                loopStart = currentTick;
                            else if (marker.Contains(']') || marker.Equals("loopEnd", StringComparison.OrdinalIgnoreCase))
                                loopEnd = currentTick;
                        }
                        else
                        {
                            br.ReadBytes(metaLength);
                        }
                    }
                    else if (status == 0xF0 || status == 0xF7) // SysEx
                    {
                        var sysexLength = ReadVariableLength(br);
                        br.ReadBytes(sysexLength);
                    }
                    else if ((status & 0xF0) >= 0x80) // Channel message
                    {
                        var dataBytes = GetMidiMessageLength(status) - 1;
                        br.ReadBytes(dataBytes);
                    }
                }
            }
        }
        catch
        {
            // Ignore parsing errors, return what we found
        }

        // Default tempo if none found
        if (tempoChanges.Count == 0)
            tempoChanges.Add((0, 500000)); // 120 BPM

        return new MidiParseResult(loopStart, loopEnd, ticksPerBeat, tempoChanges);
    }

    /// <summary>
    /// Convert MIDI ticks to sample position using tempo map for accurate timing.
    /// </summary>
    private int TicksToSamples(int targetTicks, int ticksPerBeat, List<(int tick, int microsecondsPerBeat)> tempoChanges)
    {
        if (targetTicks <= 0) return 0;

        double totalSeconds = 0;
        var currentTick = 0;
        var currentUsPerBeat = tempoChanges.Count > 0 ? tempoChanges[0].microsecondsPerBeat : 500000;
        var tempoIndex = 0;

        // Walk through tempo changes accumulating time
        while (currentTick < targetTicks)
        {
            // Find next tempo change or target tick
            var nextTick = targetTicks;
            if (tempoIndex + 1 < tempoChanges.Count && tempoChanges[tempoIndex + 1].tick < targetTicks)
            {
                nextTick = tempoChanges[tempoIndex + 1].tick;
            }

            // Calculate time for this segment
            var ticksInSegment = nextTick - currentTick;
            var secondsInSegment = (double)ticksInSegment / ticksPerBeat * (currentUsPerBeat / 1_000_000.0);
            totalSeconds += secondsInSegment;

            currentTick = nextTick;

            // Move to next tempo if we hit a tempo change
            if (tempoIndex + 1 < tempoChanges.Count && currentTick >= tempoChanges[tempoIndex + 1].tick)
            {
                tempoIndex++;
                currentUsPerBeat = tempoChanges[tempoIndex].microsecondsPerBeat;
            }
        }

        return (int)(totalSeconds * _sampleRate);
    }

    private static short[] ConvertToPcm16(float[] left, float[] right, int samples)
    {
        var pcm = new short[samples * 2]; // Stereo interleaved

        for (var i = 0; i < samples; i++)
        {
            // Clamp and convert to 16-bit
            var l = Math.Clamp(left[i], -1f, 1f);
            var r = Math.Clamp(right[i], -1f, 1f);

            pcm[i * 2] = (short)(l * 32767);
            pcm[i * 2 + 1] = (short)(r * 32767);
        }

        return pcm;
    }

    private void WriteWavWithLoopInfo(string path, short[] pcmData, int loopStart, int loopEnd)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        var dataSize = pcmData.Length * 2;
        var hasLoop = loopStart >= 0 && loopEnd > loopStart;

        // RIFF header
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize + (hasLoop ? 68 : 0)); // File size - 8
        bw.Write("WAVE"u8);

        // Format chunk
        bw.Write("fmt "u8);
        bw.Write(16);           // Chunk size
        bw.Write((short)1);     // PCM format
        bw.Write((short)2);     // Stereo
        bw.Write(_sampleRate);  // Sample rate
        bw.Write(_sampleRate * 4); // Byte rate (stereo 16-bit)
        bw.Write((short)4);     // Block align
        bw.Write((short)16);    // Bits per sample

        // smpl chunk for loop points
        if (hasLoop)
        {
            bw.Write("smpl"u8);
            bw.Write(60);       // Chunk size
            bw.Write(0);        // Manufacturer
            bw.Write(0);        // Product
            bw.Write((int)(1000000000.0 / _sampleRate)); // Sample period in nanoseconds
            bw.Write(60);       // MIDI unity note
            bw.Write(0);        // MIDI pitch fraction
            bw.Write(0);        // SMPTE format
            bw.Write(0);        // SMPTE offset
            bw.Write(1);        // Number of loops
            bw.Write(0);        // Sampler data size

            // Loop data
            bw.Write(0);        // Cue point ID
            bw.Write(0);        // Type (forward loop)
            bw.Write(loopStart); // Start sample
            bw.Write(loopEnd);   // End sample
            bw.Write(0);        // Fraction
            bw.Write(0);        // Play count (infinite)
        }

        // Data chunk
        bw.Write("data"u8);
        bw.Write(dataSize);
        foreach (var sample in pcmData)
        {
            bw.Write(sample);
        }
    }

    private bool TryConvertToOgg(string wavPath, string oggPath, int loopStart, int loopLength)
    {
        try
        {
            // Build ffmpeg command with loop metadata
            var loopMetadata = loopStart >= 0
                ? $"-metadata LOOPSTART={loopStart} -metadata LOOPLENGTH={loopLength}"
                : "";

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-y -i \"{wavPath}\" -c:a libvorbis -q:a 6 {loopMetadata} \"{oggPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(30000); // 30 second timeout

            return process.ExitCode == 0 && File.Exists(oggPath);
        }
        catch
        {
            return false;
        }
    }

    private static int ReadBigEndianInt32(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    private static int ReadBigEndianInt16(BinaryReader br)
    {
        var bytes = br.ReadBytes(2);
        return (bytes[0] << 8) | bytes[1];
    }

    private static int ReadVariableLength(BinaryReader br)
    {
        var result = 0;
        byte b;
        do
        {
            b = br.ReadByte();
            result = (result << 7) | (b & 0x7F);
        } while ((b & 0x80) != 0);

        return result;
    }

    private static int GetMidiMessageLength(byte status)
    {
        return (status & 0xF0) switch
        {
            0x80 => 3, // Note Off
            0x90 => 3, // Note On
            0xA0 => 3, // Aftertouch
            0xB0 => 3, // Control Change
            0xC0 => 2, // Program Change
            0xD0 => 2, // Channel Pressure
            0xE0 => 3, // Pitch Bend
            _ => 1
        };
    }
}
