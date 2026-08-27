using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class ParticipantLabelTest
    {
        [TestCase("P00", "P01")]
        [TestCase("P01", "P02")]
        [TestCase("P07", "P08")]
        public void Next_StepsTheTrailingNumber(string current, string expected)
        {
            Assert.That(ParticipantLabel.Next(current), Is.EqualTo(expected));
        }

        [Test]
        public void Next_FromTheDebugLabel_ReachesTheFirstParticipant()
        {
            // The whole operating procedure in one assertion: the scene stays on
            // the debug label, and one press before the first participant is all
            // that separates a development run from a real one.
            Assert.That(ParticipantLabel.Next(ParticipantLabel.DebugLabel),
                Is.EqualTo(ParticipantLabel.First));
        }

        [Test]
        public void TheDebugLabelIsNotAParticipant()
        {
            Assert.That(ParticipantLabel.DebugLabel, Is.Not.EqualTo(ParticipantLabel.First));
        }

        [Test]
        public void Next_CarriesWithoutLosingThePadding()
        {
            // The case that catches a naive int round-trip: 9 -> 10 must stay two
            // wide, or P9 and P10 sort out of run order beside P01..P08.
            Assert.That(ParticipantLabel.Next("P09"), Is.EqualTo("P10"));
            Assert.That(ParticipantLabel.Next("P29"), Is.EqualTo("P30"));
        }

        [Test]
        public void Next_WidensOnlyWhenItMust()
        {
            Assert.That(ParticipantLabel.Next("P99"), Is.EqualTo("P100"));
            Assert.That(ParticipantLabel.Next("P009"), Is.EqualTo("P010"), "three-wide stays three-wide");
        }

        [Test]
        public void Next_KeepsWhateverPrefixIsThere()
        {
            Assert.That(ParticipantLabel.Next("pilot_3"), Is.EqualTo("pilot_4"));
            Assert.That(ParticipantLabel.Next("S01"), Is.EqualTo("S02"));
        }

        [Test]
        public void Next_WithNoTrailingDigits_StartsCounting()
        {
            // Refusing would help nobody: the operator wants the next label, and
            // "P" plainly means they have not started numbering yet.
            Assert.That(ParticipantLabel.Next("P"), Is.EqualTo("P1"));
            Assert.That(ParticipantLabel.Next("pilot"), Is.EqualTo("pilot1"));
        }

        [Test]
        public void Next_FromNothing_GivesTheFirstLabel()
        {
            Assert.That(ParticipantLabel.Next(null), Is.EqualTo(ParticipantLabel.First));
            Assert.That(ParticipantLabel.Next(""), Is.EqualTo(ParticipantLabel.First));
            Assert.That(ParticipantLabel.Next("   "), Is.EqualTo(ParticipantLabel.First));
        }

        [Test]
        public void Next_TrimsSurroundingSpace()
        {
            // A label typed with a stray space would otherwise reach a filename.
            Assert.That(ParticipantLabel.Next(" P04 "), Is.EqualTo("P05"));
        }

        [Test]
        public void Next_WithAnAbsurdNumber_DoesNotThrow()
        {
            // Past long.MaxValue there is nothing sensible to do, but a session
            // must not die on it.
            Assert.That(() => ParticipantLabel.Next("P99999999999999999999"), Throws.Nothing);
        }

        [Test]
        public void Next_ThirtyTimes_WalksAWholeStudy()
        {
            // The real usage: N = 30, and every label along the way stays sortable.
            var label = "P00";
            for (var i = 0; i < 30; i++)
                label = ParticipantLabel.Next(label);

            Assert.That(label, Is.EqualTo("P30"));
        }
    }
}
