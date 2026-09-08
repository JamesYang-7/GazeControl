using System.IO;
using System.Text;
using NUnit.Framework;

namespace GazeControl.ThreeParty
{
    [TestFixture]
    public class WavSegmentTest
    {
        const int SampleRate = 1000;
        const int Channels = 2;
        const int Frames = 4000; // four seconds

        string _path;

        [SetUp]
        public void WriteAFakeRecording()
        {
            _path = Path.Combine(Path.GetTempPath(), $"wav-segment-{Path.GetRandomFileName()}.wav");
            File.WriteAllBytes(_path, RampedStereoWav());
        }

        [TearDown]
        public void DeleteTheFakeRecording()
        {
            if (_path != null && File.Exists(_path))
                File.Delete(_path);
        }

        [Test]
        public void FileDuration_ReadsTheLengthWithoutTheAudio()
        {
            Assert.That(WavSegment.FileDuration(_path), Is.EqualTo(4f).Within(1e-4f));
        }

        [Test]
        public void Read_ReturnsOnlyTheRequestedRange()
        {
            var segment = WavSegment.Read(_path, 1f, 2f);

            Assert.That(segment.SampleRate, Is.EqualTo(SampleRate));
            Assert.That(segment.Channels, Is.EqualTo(Channels));
            Assert.That(segment.FrameCount, Is.EqualTo(2 * SampleRate));
        }

        [Test]
        public void Read_StartsAtTheRequestedOffset()
        {
            // Each frame's left sample is its own index, so the first sample back
            // says exactly which frame the read started on.
            var segment = WavSegment.Read(_path, 1f, 1f);

            Assert.That(segment.Samples[0] * 32768f, Is.EqualTo(SampleRate).Within(0.5f));
        }

        [Test]
        public void Read_ClipsARangeThatRunsPastTheEnd()
        {
            // A session's motion and audio are not always the same length, so
            // asking for more than exists is ordinary rather than an error.
            var segment = WavSegment.Read(_path, 3f, 5f);

            Assert.That(segment.Duration, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Read_ReturnsNothingWhenTheRangeStartsPastTheEnd()
        {
            var segment = WavSegment.Read(_path, 10f, 1f);

            Assert.That(segment.FrameCount, Is.Zero);
            Assert.That(segment.Duration, Is.Zero);
        }

        [Test]
        public void Read_RefusesASampleFormatItWouldMisread()
        {
            var float32 = Path.Combine(Path.GetTempPath(), $"wav-segment-{Path.GetRandomFileName()}.wav");
            File.WriteAllBytes(float32, RampedStereoWav(format: 3, bitsPerSample: 32));

            try
            {
                Assert.Throws<InvalidDataException>(() => WavSegment.Read(float32, 0f, 1f));
            }
            finally
            {
                File.Delete(float32);
            }
        }

        /// <summary>
        /// A minimal PCM wav whose left channel counts frames, so a read can be
        /// checked for where it started as well as how much it returned. The
        /// declared format is a parameter so the refusal path can be exercised
        /// without writing a genuinely different encoding.
        /// </summary>
        static byte[] RampedStereoWav(int format = 1, int bitsPerSample = 16)
        {
            var data = new byte[Frames * Channels * 2];
            for (var frame = 0; frame < Frames; frame++)
            {
                var value = (short)frame;
                data[4 * frame] = (byte)(value & 0xFF);
                data[4 * frame + 1] = (byte)((value >> 8) & 0xFF);
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.ASCII);

            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + data.Length);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((ushort)format);
            writer.Write((ushort)Channels);
            writer.Write(SampleRate);
            writer.Write(SampleRate * Channels * 2);
            writer.Write((ushort)(Channels * 2));
            writer.Write((ushort)bitsPerSample);
            writer.Write("data".ToCharArray());
            writer.Write(data.Length);
            writer.Write(data);
            writer.Flush();

            return stream.ToArray();
        }
    }
}
