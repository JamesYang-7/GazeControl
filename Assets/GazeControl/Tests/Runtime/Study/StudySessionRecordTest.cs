using System;
using System.IO;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class StudySessionRecordTest
    {
        static readonly DateTime Started = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        static readonly DateTime Finished = new(2026, 9, 1, 10, 42, 0, DateTimeKind.Utc);

        string _directory;

        [SetUp]
        public void SetUp() =>
            _directory = Path.Combine(Path.GetTempPath(), $"session_{Path.GetRandomFileName()}");

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        static StudySessionRecord NewRecord() => StudySessionRecord.Begin(
            "P07", scheduleOrdinal: 2,
            StudySequence.Build(new[] { "study_c1", "study_c2" },
                new[] { "SpeakerFollowing", "RoleConditioned", "Proposed" }, participantOrdinal: 2),
            "6000.5.3f1", "0123456789abcdef0123456789abcdef01234567", Started);

        [Test]
        public void ANewRecordIsNotComplete()
        {
            var record = NewRecord();

            Assert.That(record.IsComplete, Is.False);
            Assert.That(record.clipsCompleted, Is.Zero);
            Assert.That(record.clipCount, Is.EqualTo(6));
        }

        [Test]
        public void TheRunningOrderIsRecorded()
        {
            // What a missing take log is reconstructed against: the record says
            // what this participant was shown, in what order, and from which
            // counterbalancing slot.
            var record = NewRecord();

            Assert.That(record.scheduleOrdinal, Is.EqualTo(2));
            Assert.That(record.trials[0].conversation, Is.EqualTo("study_c1"));
            Assert.That(record.trials.Length, Is.EqualTo(record.clipCount));
        }

        [Test]
        public void ASavedRecordReadsBack()
        {
            var record = NewRecord();
            record.clipsCompleted = 4;
            record.Save(_directory);

            var read = StudySessionRecord.Load(_directory);

            Assert.That(read, Is.Not.Null);
            Assert.That(read.participant, Is.EqualTo("P07"));
            Assert.That(read.clipsCompleted, Is.EqualTo(4));
            Assert.That(read.gitCommit, Does.StartWith("0123456789"));
            Assert.That(read.trials.Length, Is.EqualTo(6));
        }

        [Test]
        public void AnUnfinishedSessionSaysSo()
        {
            // The reason the record is written at the start: nine clips of fifteen
            // otherwise looks exactly like fifteen, because the take logs are named
            // per conversation and a missing one cannot be told from a clip nobody
            // reached.
            var record = NewRecord();
            record.clipsCompleted = 4;
            record.Save(_directory);

            Assert.That(StudySessionRecord.Load(_directory).Progress, Does.Contain("INCOMPLETE"));
        }

        [Test]
        public void CompleteStampsTheEndOnce()
        {
            var record = NewRecord();
            record.Complete(Finished);
            var first = record.completedUtc;
            record.Complete(Finished.AddHours(1));

            Assert.That(record.IsComplete, Is.True);
            Assert.That(record.completedUtc, Is.EqualTo(first), "the first end time is the one kept");
        }

        [Test]
        public void LoadingFromAFolderWithNoRecordIsNull()
        {
            Assert.That(StudySessionRecord.Load(_directory), Is.Null);
        }

        [Test]
        public void SavingCreatesTheParticipantFolder()
        {
            // The folder has to exist from the first moment, or the roster hands
            // the next participant this same label when a session ends before
            // anyone answers a question.
            NewRecord().Save(_directory);

            Assert.That(File.Exists(StudySessionRecord.PathIn(_directory)), Is.True);
        }
    }
}
