using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// A stretch of a PCM wav file, read straight off disk into audio samples.
    ///
    /// <para>Unity's own loaders are not usable here. The dataset's audio lives
    /// outside the project (so there is no <c>AudioClip</c> to reference) and a
    /// session's track is ten minutes of 48 kHz stereo — 230 MB once Unity
    /// decodes it to float, times three participants. The replay only ever wants
    /// a window of it, so this reads exactly the requested range and nothing
    /// else.</para>
    ///
    /// <para>Reading and clip creation are separate on purpose: the file read is
    /// safe on a background thread, and <c>AudioClip.Create</c> is not.</para>
    /// </summary>
    public readonly struct WavSegment
    {
        WavSegment(float[] samples, int channels, int sampleRate)
        {
            Samples = samples;
            Channels = channels;
            SampleRate = sampleRate;
        }

        /// <summary>Interleaved samples in [-1, 1].</summary>
        public float[] Samples { get; }

        public int Channels { get; }
        public int SampleRate { get; }

        /// <summary>Sample frames (one frame is one sample per channel).</summary>
        public int FrameCount => Channels > 0 && Samples != null ? Samples.Length / Channels : 0;

        public float Duration => SampleRate > 0 ? (float)FrameCount / SampleRate : 0f;

        /// <summary>How long the whole file is, without reading its audio.</summary>
        public static float FileDuration(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var header = ReadHeader(reader, path);
            return (float)(header.DataLength / header.BlockAlign) / header.SampleRate;
        }

        /// <summary>
        /// Read <paramref name="durationSeconds"/> of <paramref name="path"/> from
        /// <paramref name="startSeconds"/>. A range past the end of the file is
        /// clipped rather than refused: the motion and the audio of a session are
        /// not always the same length.
        /// </summary>
        public static WavSegment Read(string path, float startSeconds, float durationSeconds)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var header = ReadHeader(reader, path);

            var totalFrames = header.DataLength / header.BlockAlign;
            var startFrame = Math.Clamp((long)(startSeconds * header.SampleRate), 0, totalFrames);
            var wanted = durationSeconds > 0f ? (long)(durationSeconds * header.SampleRate) : totalFrames;
            var frames = (int)Math.Min(wanted, totalFrames - startFrame);
            if (frames <= 0)
                return new WavSegment(Array.Empty<float>(), header.Channels, header.SampleRate);

            stream.Seek(header.DataOffset + startFrame * header.BlockAlign, SeekOrigin.Begin);
            var bytes = reader.ReadBytes(frames * header.BlockAlign);

            var samples = new float[frames * header.Channels];
            for (var i = 0; i < samples.Length; i++)
            {
                var value = (short)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
                samples[i] = value / 32768f;
            }

            return new WavSegment(samples, header.Channels, header.SampleRate);
        }

        /// <summary>Wrap the samples in a clip. Main thread only.</summary>
        public AudioClip ToAudioClip(string name)
        {
            var clip = AudioClip.Create(name, Mathf.Max(FrameCount, 1), Math.Max(Channels, 1), SampleRate, false);
            if (FrameCount > 0)
                clip.SetData(Samples, 0);

            return clip;
        }

        readonly struct Header
        {
            public Header(int channels, int sampleRate, int blockAlign, long dataOffset, long dataLength)
            {
                Channels = channels;
                SampleRate = sampleRate;
                BlockAlign = blockAlign;
                DataOffset = dataOffset;
                DataLength = dataLength;
            }

            public int Channels { get; }
            public int SampleRate { get; }
            public int BlockAlign { get; }
            public long DataOffset { get; }
            public long DataLength { get; }
        }

        static Header ReadHeader(BinaryReader reader, string path)
        {
            if (new string(reader.ReadChars(4)) != "RIFF")
                throw new InvalidDataException($"{path}: not a RIFF file");

            reader.ReadUInt32(); // riff size, unused: the chunk walk below is what finds the data
            if (new string(reader.ReadChars(4)) != "WAVE")
                throw new InvalidDataException($"{path}: not a WAVE file");

            var channels = 0;
            var sampleRate = 0;
            var blockAlign = 0;
            var stream = reader.BaseStream;

            while (stream.Position + 8 <= stream.Length)
            {
                var id = new string(reader.ReadChars(4));
                var size = reader.ReadUInt32();
                var next = stream.Position + size + (size % 2); // chunks are word-aligned

                if (id == "fmt ")
                {
                    var format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    sampleRate = (int)reader.ReadUInt32();
                    reader.ReadUInt32(); // byte rate
                    blockAlign = reader.ReadUInt16();
                    var bitsPerSample = reader.ReadUInt16();

                    // 16-bit PCM is what the corpus is, on every file. Anything
                    // else is refused rather than guessed at, because a silently
                    // misread sample format sounds like a broken recording.
                    if (format != 1 || bitsPerSample != 16)
                        throw new InvalidDataException(
                            $"{path}: expected 16-bit PCM, found format {format} at {bitsPerSample} bits");
                }
                else if (id == "data")
                {
                    if (channels == 0 || blockAlign == 0)
                        throw new InvalidDataException($"{path}: data chunk before fmt chunk");

                    return new Header(channels, sampleRate, blockAlign, stream.Position, size);
                }

                stream.Seek(next, SeekOrigin.Begin);
            }

            throw new InvalidDataException($"{path}: no data chunk");
        }
    }
}
