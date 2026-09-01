using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class ParticipantRosterTest
    {
        /// <summary>
        /// The recordings root as a real one looks: participant folders beside the
        /// per-conversation take folders and a development run's timestamped one.
        /// </summary>
        static readonly string[] Root =
        {
            "P01", "P02", "P03",
            "study_c1", "study_c3", "case1_01", "cand_v008",
            "P00_20260901_143000",
        };

        [Test]
        public void NextFree_IsOnePastTheHighestThatHasRun()
        {
            Assert.That(ParticipantRoster.NextFree(Root), Is.EqualTo("P04"));
        }

        [Test]
        public void NextFree_OnAnEmptyRoot_IsTheFirstParticipant()
        {
            Assert.That(ParticipantRoster.NextFree(new string[0]), Is.EqualTo(ParticipantLabel.First));
        }

        [Test]
        public void NextFree_IgnoresTheTakeFolders()
        {
            // The failure this rules out: "anything ending in digits" reads
            // case1_01 and cand_v008 as participants and hands the next real one
            // a label from the far end of the study.
            Assert.That(ParticipantRoster.NextFree(new[] { "case1_01", "cand_v120", "study_c5" }),
                Is.EqualTo(ParticipantLabel.First));
        }

        [Test]
        public void NextFree_IgnoresTheDebugFolders()
        {
            // P00 is the debugging namespace and its folders are timestamped, so
            // neither the label nor a day's development runs may advance the study.
            Assert.That(ParticipantRoster.NextFree(new[] { "P00", "P00_20260901_143000" }),
                Is.EqualTo(ParticipantLabel.First));
        }

        [Test]
        public void NextFree_StepsPastAGapRatherThanFillingIt()
        {
            // P04 aborted and their folder was deleted. Reusing the number would
            // put two people under one label in the take logs, which are named per
            // conversation and so survive deleting the folder; skipping one costs
            // nothing.
            Assert.That(ParticipantRoster.NextFree(new[] { "P01", "P02", "P03", "P05" }),
                Is.EqualTo("P06"));
        }

        [Test]
        public void NextFree_KeepsThePadding()
        {
            Assert.That(ParticipantRoster.NextFree(new[] { "P09" }), Is.EqualTo("P10"));
            Assert.That(ParticipantRoster.NextFree(new[] { "P99" }), Is.EqualTo("P100"));
        }

        [TestCase("P01", true)]
        [TestCase("P100", true)]
        [TestCase("p07", true, Description = "an operator's folder, typed by hand")]
        [TestCase("P00_20260901_143000", false)]
        [TestCase("case1_01", false)]
        [TestCase("cand_v008", false)]
        [TestCase("study_c1", false)]
        [TestCase("P", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsParticipantFolder(string name, bool expected)
        {
            Assert.That(ParticipantRoster.IsParticipantFolder(name), Is.EqualTo(expected));
        }

        [Test]
        public void HasRun_ComparesByNumberNotByText()
        {
            // A hand-typed P7 has to collide with the recorded P07, or the guard
            // that stops two participants sharing a label is one keystroke wide.
            Assert.That(ParticipantRoster.HasRun("P7", new[] { "P07" }), Is.True);
            Assert.That(ParticipantRoster.HasRun("P07", new[] { "P07" }), Is.True);
            Assert.That(ParticipantRoster.HasRun("P08", new[] { "P07" }), Is.False);
        }

        [Test]
        public void HasRun_IsNeverTrueForTheDebugLabel()
        {
            Assert.That(ParticipantRoster.HasRun(ParticipantLabel.DebugLabel, new[] { "P00" }), Is.False);
        }

        [Test]
        public void NumbersIn_IsAscendingAndDeduplicated()
        {
            Assert.That(ParticipantRoster.NumbersIn(new[] { "P03", "P01", "p01", "P02" }),
                Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void HighestIn_IsTheFolderAsWritten()
        {
            Assert.That(ParticipantRoster.HighestIn(Root), Is.EqualTo("P03"));
            Assert.That(ParticipantRoster.HighestIn(new[] { "study_c1" }), Is.Null);
        }

        [Test]
        public void LabelFor_PadsLikeTheFirstLabel()
        {
            Assert.That(ParticipantRoster.LabelFor(4), Is.EqualTo("P04"));
            Assert.That(ParticipantRoster.LabelFor(120), Is.EqualTo("P120"));
        }
    }
}
