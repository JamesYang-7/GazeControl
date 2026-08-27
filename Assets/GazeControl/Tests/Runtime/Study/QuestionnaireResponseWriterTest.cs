using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Study
{
    [TestFixture]
    public class QuestionnaireResponseWriterTest
    {
        static readonly DateTime FixedUtc = new(2026, 8, 27, 14, 5, 30, 250, DateTimeKind.Utc);

        string _directory;
        QuestionnaireScript _script;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"questionnaire_{Path.GetRandomFileName()}");
            _script = QuestionnaireScript.Build(QuestionnaireFixture.Definition(), blockCount: 2, versionsPerBlock: 3);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }

        QuestionnaireResponseWriter NewWriter(string participantId = "P07") =>
            new(_script, _directory, participantId, () => FixedUtc);

        string[] ResponseLines() => LinesOf("responses.csv");

        string[] CommentLines() => LinesOf("comments.jsonl");

        /// <summary>
        /// Read a file the writer may still have open.
        ///
        /// <para>Not File.ReadAllLines: it opens with FileShare.Read, which
        /// refuses a file that already has a live write handle on it, and the
        /// writer holds one for the whole session by design (it AutoFlushes so a
        /// session that dies in block 4 leaves blocks 1-3 readable). Asking for
        /// FileShare.ReadWrite is what lets the test observe exactly that
        /// property instead of only being able to check the file after Dispose.
        /// </para>
        /// </summary>
        string[] LinesOf(string fileName)
        {
            using var stream = new FileStream(
                Path.Combine(_directory, fileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
                lines.Add(line);

            return lines.ToArray();
        }

        [Test]
        public void AppendRating_WritesTheRowInTheHeadersOrder()
        {
            using (var sut = NewWriter())
            {
                sut.AppendRating(1, "study_c3", 2, "Proposed", "N1", 6);
            }

            Assert.That(ResponseLines()[1],
                Is.EqualTo("P07,1,study_c3,2,Proposed,N1,6,2026-08-27T14:05:30.250Z"));
        }

        [Test]
        public void AppendRating_WritesAsManyValuesAsTheHeaderNames()
        {
            using (var sut = NewWriter())
            {
                sut.AppendRating(1, "study_c3", 2, "Proposed", "N1", 6);
            }

            var lines = ResponseLines();

            Assert.That(lines[1].Split(',').Length, Is.EqualTo(lines[0].Split(',').Length));
        }

        [Test]
        public void AppendRating_IsOnDiskBeforeTheWriterIsClosed()
        {
            using var sut = NewWriter();

            sut.AppendRating(1, "study_c3", 1, "BaselineA", "N1", 4);

            // The point of flushing per answer: a session that dies in block 4
            // must leave blocks 1-3 readable.
            Assert.That(ResponseLines(), Has.Length.EqualTo(2));
        }

        [Test]
        public void AppendRating_OffTheScale_ThrowsArgumentOutOfRangeException()
        {
            using var sut = NewWriter();

            Assert.That(() => sut.AppendRating(1, "study_c3", 1, "Proposed", "N1", 8),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void AppendRating_OffTheScale_WritesNothing()
        {
            using var sut = NewWriter();

            Assert.That(() => sut.AppendRating(1, "study_c3", 1, "Proposed", "N1", 0),
                Throws.TypeOf<ArgumentOutOfRangeException>(), "refused");
            Assert.That(ResponseLines(), Has.Length.EqualTo(1), "header only, no row written");
        }

        [Test]
        public void AppendRating_ForAnItemTheInstrumentDoesNotCarry_ThrowsArgumentException()
        {
            using var sut = NewWriter();

            Assert.That(() => sut.AppendRating(1, "study_c3", 1, "Proposed", "Z9", 4),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void AppendRating_BeyondTheRunsBlocks_ThrowsArgumentOutOfRangeException()
        {
            using var sut = NewWriter();

            Assert.That(() => sut.AppendRating(3, "study_c3", 1, "Proposed", "N1", 4),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void AppendRating_BeyondTheBlocksVersions_ThrowsArgumentOutOfRangeException()
        {
            using var sut = NewWriter();

            Assert.That(() => sut.AppendRating(1, "study_c3", 4, "Proposed", "N1", 4),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void AppendRating_ForAQuestionAlreadyAnswered_ThrowsInvalidOperationException()
        {
            using var sut = NewWriter();
            sut.AppendRating(1, "study_c3", 1, "Proposed", "N1", 4);

            Assert.That(() => sut.AppendRating(1, "study_c3", 1, "Proposed", "N1", 5),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AppendRating_ForTheSameItemOnAnotherVersion_IsRecorded()
        {
            using var sut = NewWriter();
            sut.AppendRating(1, "study_c3", 1, "Proposed", "N1", 4);

            sut.AppendRating(1, "study_c3", 2, "BaselineB", "N1", 5);

            Assert.That(sut.ResponseCount, Is.EqualTo(2));
        }

        [Test]
        public void AppendRank_WritesTheRankUnderTheRankingsCode()
        {
            using (var sut = NewWriter())
            {
                sut.AppendRank(2, "study_c1", 3, "BaselineB", 1);
            }

            Assert.That(ResponseLines()[1], Does.Contain(",R1,1,"));
        }

        [Test]
        public void AppendRank_BeyondTheNumberOfVersions_ThrowsArgumentOutOfRangeException()
        {
            using var sut = NewWriter();

            Assert.That(() => sut.AppendRank(1, "study_c1", 1, "Proposed", 4),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void IsComplete_AfterEveryItemAndRankOfEveryClip_IsTrue()
        {
            using var sut = NewWriter();

            for (var block = 1; block <= _script.BlockCount; block++)
            {
                for (var position = 1; position <= _script.VersionsPerBlock; position++)
                {
                    foreach (var item in _script.Definition.perClipItems)
                        sut.AppendRating(block, "study_c1", position, "Proposed", item.code, 4);

                    sut.AppendRank(block, "study_c1", position, "Proposed", position);
                }
            }

            Assert.That(sut.IsComplete, Is.True);
        }

        [Test]
        public void IsComplete_WithOneAnswerMissing_IsFalse()
        {
            using var sut = NewWriter();
            sut.AppendRating(1, "study_c1", 1, "Proposed", "N1", 4);

            Assert.That(sut.IsComplete, Is.False);
        }

        [Test]
        public void AppendComment_WritesOneJsonRecordPerBlock()
        {
            using (var sut = NewWriter())
            {
                sut.AppendComment(1, "study_c1", "The second one stared at me.");
            }

            var actual = JsonUtility.FromJson<CommentProbe>(CommentLines()[0]);

            Assert.That(actual.text, Is.EqualTo("The second one stared at me."), "text");
            Assert.That(actual.skipped, Is.False, "not skipped");
            Assert.That(actual.block, Is.EqualTo(1), "block");
            Assert.That(actual.item, Is.EqualTo("D1"), "item code");
        }

        [Test]
        public void AppendComment_WithTextCarryingCommasAndQuotes_SurvivesARoundTrip()
        {
            const string spoken = "The \"second\" one, mostly, felt off\nand cold.";

            using (var sut = NewWriter())
            {
                sut.AppendComment(1, "study_c1", spoken);
            }

            var actual = JsonUtility.FromJson<CommentProbe>(CommentLines()[0]);

            Assert.That(actual.text, Is.EqualTo(spoken));
        }

        [Test]
        public void AppendComment_WithNothingSaid_RecordsTheSkipRatherThanOmittingIt()
        {
            using (var sut = NewWriter())
            {
                sut.AppendComment(1, "study_c1", "   ");
            }

            var actual = JsonUtility.FromJson<CommentProbe>(CommentLines()[0]);

            Assert.That(actual.skipped, Is.True);
        }

        [Test]
        public void AppendComment_IsNotCountedAsAResponse()
        {
            using var sut = NewWriter();

            sut.AppendComment(1, "study_c1", "nothing odd");

            Assert.That(sut.ResponseCount, Is.Zero);
        }

        [Test]
        public void Csv_WithAParticipantIdCarryingAComma_QuotesTheField()
        {
            using (var sut = NewWriter("P07, pilot"))
            {
                sut.AppendRating(1, "study_c1", 1, "Proposed", "N1", 4);
            }

            Assert.That(ResponseLines()[1], Does.StartWith("\"P07, pilot\","));
        }

        [Test]
        public void Constructor_OverAFolderThatAlreadyHasResponses_ThrowsIOException()
        {
            using (var first = NewWriter())
            {
                first.AppendRating(1, "study_c1", 1, "Proposed", "N1", 4);
            }

            Assert.That(() => NewWriter(), Throws.TypeOf<IOException>());
        }

        [Test]
        public void Constructor_WithNoParticipantId_ThrowsArgumentException()
        {
            Assert.That(() => new QuestionnaireResponseWriter(_script, _directory, "  "),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Dispose_LeavesEveryAnswerOnDisk()
        {
            var sut = NewWriter();
            sut.AppendRating(1, "study_c1", 1, "Proposed", "N1", 4);
            sut.AppendRating(1, "study_c1", 1, "Proposed", "T2", 5);

            sut.Dispose();

            Assert.That(ResponseLines().Skip(1).Count(), Is.EqualTo(2));
        }

        /// <summary>Mirror of the writer's private record, for reading a line back.</summary>
        [Serializable]
        sealed class CommentProbe
        {
            public string participant;
            public int block;
            public string conversation;
            public string item;
            public string text;
            public bool skipped;
            public string utc;
        }
    }
}
