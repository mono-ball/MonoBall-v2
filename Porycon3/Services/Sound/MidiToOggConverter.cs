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
            // Parse MIDI for loop markers and resolution
            var (loopStartTick, loopEndTick, ticksPerBeat) = FindLoopMarkersAndResolution(midiPath);

            // Load SF2 if not already loaded
            _synthesizer ??= new Synthesizer(_sf2Path, _sampleRate);

            // Load and sequence the MIDI
            var midiFile = new MidiFile(midiPath);
            var sequencer = new MidiFileSequencer(_synthesizer);

            // Calculate total length - render to end of song
            var totalSeconds = midiFile.Length.TotalSeconds;

            var totalSamples = (int)(totalSeconds * _sampleRate);
            var leftBuffer = new float[totalSamples];
            var rightBuffer = new float[totalSamples];

            // Render audio (play once, render once)
            sequencer.Play(midiFile, false);
            sequencer.Render(leftBuffer, rightBuffer);

            // Convert loop ticks to sample positions
            var loopStartSample = 0;
            var loopEndSample = totalSamples;

            if (loopStartTick >= 0 && loopEndTick > loopStartTick)
            {
                loopStartSample = TicksToSamples(loopStartTick, ticksPerBeat);
                loopEndSample = TicksToSamples(loopEndTick, ticksPerBeat);
            }

            // Convert to 16-bit PCM
            var pcmData = ConvertToPcm16(leftBuffer, rightBuffer, totalSamples);

            // Write output (WAV for now, with loop metadata sidecar)
            var wavPath = Path.ChangeExtension(outputPath, ".wav");
            WriteWavWithLoopInfo(wavPath, pcmData, loopStartSample, loopEndSample);

            var loopLength = loopEndSample - loopStartSample;
            var hasLoop = loopStartTick >= 0 && loopEndTick > loopStartTick;

            // Try to convert to OGG using ffmpeg
            if (TryConvertToOgg(wavPath, outputPath, loopStartSample, loopLength))
            {
                File.Delete(wavPath); // Clean up WAV
                return new ConversionResult(
                    true,
                    hasLoop ? loopStartSample : 0,
                    hasLoop ? loopLength : 0,
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
                hasLoop ? loopLength : 0,
                _sampleRate);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MidiToOggConverter] Error converting {midiPath}: {ex.Message}");
            return new ConversionResult(false, 0, 0, _sampleRate);
        }
    }

    /// <summary>
    /// Find loop start ([) and loop end (]) markers in MIDI file.
    /// Also returns ticks per beat (resolution) for timing calculations.
    /// </summary>
    private (int loopStart, int loopEnd, int ticksPerBeat) FindLoopMarkersAndResolution(string midiPath)
    {
        var loopStart = -1;
        var loopEnd = -1;
        var ticksPerBeat = 480; // Default

        try
        {
            using var fs = File.OpenRead(midiPath);
            using var br = new BinaryReader(fs);

            // Read MIDI header
            var header = new string(br.ReadChars(4));
            if (header != "MThd") return (loopStart, loopEnd, ticksPerBeat);

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

                        if (metaType == 0x06 && metaLength == 1) // Marker with 1 char
                        {
                            var marker = (char)br.ReadByte();
                            if (marker == '[')
                                loopStart = currentTick;
                            else if (marker == ']')
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

        return (loopStart, loopEnd, ticksPerBeat);
    }

    private int TicksToSamples(int ticks, int ticksPerBeat)
    {
        // Convert MIDI ticks to samples using tempo
        var microsecondsPerBeat = 500000; // Default 120 BPM

        // This is approximate - proper implementation would track tempo changes
        var seconds = (double)ticks / ticksPerBeat * (microsecondsPerBeat / 1000000.0);
        return (int)(seconds * _sampleRate);
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
