using System;
using System.Globalization;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Xr
{
    [TestFixture]
    public class ParticipantGazeLogWriterTest
    {
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"participant_gaze_{Path.GetRandomFileName()}");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        static ParticipantGazeRow SampleRow() => new()
        {
            Time = 1.25f,
            Tick = 75,
            ParticipantId = "P07",
            Condition = "Proposed",
            Case = "study_c1",
            TrackerStatus = ParticipantGazeStatus.Valid,
            LeftEyeStatus = ParticipantEyeStatus.Tracked,
            RightEyeStatus = ParticipantEyeStatus.Visible,
            Calibrated = true,
            DeviceFrame = 4242,
            CaptureTimeNanoseconds = 123456789012345L,
            HeadPosition = new Vector3(0.01f, 1.6f, -0.02f),
            HeadYaw = -12.5f,
            HeadPitch = 3.25f,
            HeadRoll = 0.5f,
            GazeOrigin = new Vector3(0f, 1.6f, 0f),
            GazeDirection = new Vector3(0f, 0f, 1f),
            LeftGazeOrigin = new Vector3(-0.03f, 1.6f, 0f),
            LeftGazeDirection = new Vector3(0.01f, 0f, 0.99f),
            RightGazeOrigin = new Vector3(0.03f, 1.6f, 0f),
            RightGazeDirection = new Vector3(-0.01f, 0f, 0.99f),
            FocusDistance = 0.98f,
            FocusStability = 0.75f,
            InterPupillaryDistanceMm = 63.5f,
            LeftPupilDiameterMm = 3.1f,
            RightPupilDiameterMm = 3.2f,
            LeftEyeOpenness = 0.9f,
            RightEyeOpenness = 0.85f,
            TargetType = "person",
            TargetId = 1,
            TargetAngleDegrees = 2.5f,
            TargetDistance = 1.02f,
            MutualGaze = true,
            AgentsLookingAtUser = 0b10,
        };

        string[] WriteOne(ParticipantGazeRow row)
        {
            using (var writer = new ParticipantGazeLogWriter(_directory, "take"))
            {
                writer.Append(in row);
            }

            return File.ReadAllLines(Path.Combine(_directory, "take.csv"));
        }

        [Test]
        public void Append_WritesAsManyValuesAsTheHeaderNames()
        {
            // The analysis reads these files by column position, so a row that
            // does not line up with the header is the one failure that silently
            // shifts every measure after it.
            var lines = WriteOne(SampleRow());

            Assert.That(lines.Length, Is.EqualTo(2));
            Assert.That(lines[1].Split(',').Length, Is.EqualTo(lines[0].Split(',').Length));
        }

        [Test]
        public void Append_UnderACommaDecimalCulture_StillWritesDots()
        {
            // A decimal comma would split every float into two columns on a
            // machine set to a European locale, and the file would still look
            // plausible until it was parsed.
            var previous = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                var lines = WriteOne(SampleRow());

                Assert.That(lines[1].Split(',').Length, Is.EqualTo(lines[0].Split(',').Length));
                Assert.That(lines[1], Does.StartWith("1.25,75,P07,Proposed,study_c1,"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void Append_WritesTheStatusesAndFlagsInTheirLoggedForm()
        {
            var lines = WriteOne(SampleRow());
            var header = lines[0].Split(',');
            var fields = lines[1].Split(',');

            Assert.That(fields[Array.IndexOf(header, "tracker")], Is.EqualTo("Valid"));
            Assert.That(fields[Array.IndexOf(header, "left_eye")], Is.EqualTo("Tracked"));
            Assert.That(fields[Array.IndexOf(header, "calibrated")], Is.EqualTo("1"));
            Assert.That(fields[Array.IndexOf(header, "mutual_gaze")], Is.EqualTo("1"));
            Assert.That(fields[Array.IndexOf(header, "agents_looking_at_user")], Is.EqualTo("2"));
            Assert.That(fields[Array.IndexOf(header, "capture_time_ns")], Is.EqualTo("123456789012345"));
        }

        [Test]
        public void ValidRowCount_CountsOnlyRowsThatCarryGaze()
        {
            var valid = SampleRow();
            var blind = SampleRow();
            blind.TrackerStatus = ParticipantGazeStatus.Invalid;

            using var writer = new ParticipantGazeLogWriter(_directory, "take");
            writer.Append(in valid);
            writer.Append(in blind);
            writer.Append(in blind);

            Assert.That(writer.RowCount, Is.EqualTo(3));
            Assert.That(writer.ValidRowCount, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_FlushesRowsThatNeverFilledABatch()
        {
            // A take is ~1,800 ticks and the buffer holds 120, so the tail of
            // every single log depends on this.
            var lines = WriteOne(SampleRow());

            Assert.That(lines.Length, Is.EqualTo(2));
        }
    }
}
