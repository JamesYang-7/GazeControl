using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class QuestionnaireButtonRowTest
    {
        static QuestionnaireEntry Rating() => new(4, 1, 7, requireDistinct: false);

        static QuestionnaireEntry Ranking() => new(3, 1, 3, requireDistinct: true);

        static QuestionnaireButton[] RatingRow(QuestionnaireEntry entry, bool commitAllowed) =>
            QuestionnaireButtonRow.Build(entry, 1, QuestionnaireButtonRow.ScaleLabels(1, 7), commitAllowed);

        static QuestionnaireButton[] RankingRow(QuestionnaireEntry entry, bool commitAllowed) =>
            QuestionnaireButtonRow.Build(entry, 1, QuestionnaireButtonRow.VersionLabels(3), commitAllowed);

        [Test]
        public void Build_ARatingRow_IsUndoThenTheScaleThenNext()
        {
            var row = RatingRow(Rating(), commitAllowed: false);

            Assert.That(row.Length, Is.EqualTo(9));
            Assert.That(row[0].Kind, Is.EqualTo(QuestionnaireButtonKind.Back));
            Assert.That(row[^1].Kind, Is.EqualTo(QuestionnaireButtonKind.Next));

            for (var i = 1; i <= 7; i++)
            {
                Assert.That(row[i].Kind, Is.EqualTo(QuestionnaireButtonKind.Value));
                Assert.That(row[i].Value, Is.EqualTo(i));
                Assert.That(row[i].Label, Is.EqualTo(i.ToString()));
            }
        }

        [Test]
        public void Build_WithNothingEnteredYet_HasNothingToUndo()
        {
            var row = RatingRow(Rating(), commitAllowed: false);

            Assert.That(row[0].Enabled, Is.False);
            Assert.That(row[1].Enabled, Is.True);
        }

        [Test]
        public void Build_AfterAnAnswer_CanUndoIt()
        {
            var entry = Rating();
            entry.TryEnter(5, out _);

            Assert.That(RatingRow(entry, commitAllowed: false)[0].Enabled, Is.True);
        }

        [Test]
        public void Build_WithEverySlotFilled_OffersNoMoreValues()
        {
            var entry = Rating();
            for (var i = 0; i < entry.SlotCount; i++)
                entry.TryEnter(4, out _);

            var row = RatingRow(entry, entry.IsComplete);

            Assert.That(row[1].Enabled, Is.False, "the scale is spent");
            Assert.That(row[0].Enabled, Is.True, "the last answer can still be corrected");
            Assert.That(row[^1].Enabled, Is.True, "and the screen can be committed");
        }

        [Test]
        public void Build_AnIncompleteRating_WillNotCommit()
        {
            var entry = Rating();
            entry.TryEnter(3, out _);

            Assert.That(RatingRow(entry, entry.IsComplete)[^1].Enabled, Is.False);
        }

        /// <summary>
        /// The two preview screens and the free-text probe record nothing, so
        /// they carry no entry — and the row is shown all the same, because
        /// seeing the buttons is half of what a preview is for. On a preview it
        /// is the Next that is live, so the participant can read on alone.
        /// </summary>
        [Test]
        public void Build_WithNoEntry_ShowsTheRowWithOnlyNextPressable()
        {
            var row = RatingRow(null, commitAllowed: true);

            Assert.That(row.Length, Is.EqualTo(9));
            Assert.That(row[0].Enabled, Is.False);
            Assert.That(row[4].Enabled, Is.False);
            Assert.That(row[^1].Enabled, Is.True);
        }

        /// <summary>
        /// The framing passage asks nothing and still has to be passable by the
        /// participant, or a session they could otherwise finish alone stops on
        /// its first screen.
        /// </summary>
        [Test]
        public void NextOnly_IsOneLiveNext()
        {
            var row = QuestionnaireButtonRow.NextOnly(commitAllowed: true);

            Assert.That(row.Length, Is.EqualTo(1));
            Assert.That(row[0].Kind, Is.EqualTo(QuestionnaireButtonKind.Next));
            Assert.That(row[0].Label, Is.EqualTo(QuestionnaireButtonRow.NextLabel));
            Assert.That(row[0].Enabled, Is.True);
        }

        [Test]
        public void Build_TheFreeTextScreen_OffersOnlyNext()
        {
            var row = RankingRow(null, commitAllowed: true);

            Assert.That(row[0].Enabled, Is.False);
            Assert.That(row[1].Enabled, Is.False);
            Assert.That(row[^1].Enabled, Is.True, "an empty answer is an answer");
        }

        [Test]
        public void Build_ARankingRow_IsUndoThenTheVersionsThenNext()
        {
            var row = RankingRow(Ranking(), commitAllowed: false);

            Assert.That(row.Length, Is.EqualTo(5));
            Assert.That(row[1].Label, Is.EqualTo("V1"));
            Assert.That(row[2].Label, Is.EqualTo("V2"));
            Assert.That(row[3].Label, Is.EqualTo("V3"));
        }

        /// <summary>
        /// The ranking may not tie, and the participant's panel carries no notice
        /// for them to read — so a version already placed goes dim rather than
        /// being refused after the press.
        /// </summary>
        [Test]
        public void Build_ARankingWithAVersionAlreadyPlaced_GreysItOut()
        {
            var entry = Ranking();
            entry.TryEnter(2, out _);

            var row = RankingRow(entry, entry.IsComplete);

            Assert.That(row[2].Enabled, Is.False, "version 2 is placed");
            Assert.That(row[1].Enabled, Is.True);
            Assert.That(row[3].Enabled, Is.True);
        }

        [Test]
        public void Build_ACompleteRanking_MayCommit()
        {
            var entry = Ranking();
            entry.TryEnter(2, out _);
            entry.TryEnter(1, out _);
            entry.TryEnter(3, out _);

            Assert.That(RankingRow(entry, entry.IsComplete)[^1].Enabled, Is.True);
        }

        [Test]
        public void Build_WithNoValueButtons_Throws()
        {
            Assert.That(
                () => QuestionnaireButtonRow.Build(null, 1, new string[0], commitAllowed: true),
                Throws.ArgumentException);
        }

        [Test]
        public void ScaleLabels_CoverTheWholeScale()
        {
            Assert.That(QuestionnaireButtonRow.ScaleLabels(1, 7),
                Is.EqualTo(new[] { "1", "2", "3", "4", "5", "6", "7" }));
        }

        [Test]
        public void VersionLabels_AreShort()
        {
            Assert.That(QuestionnaireButtonRow.VersionLabels(3), Is.EqualTo(new[] { "V1", "V2", "V3" }));
        }
    }
}
