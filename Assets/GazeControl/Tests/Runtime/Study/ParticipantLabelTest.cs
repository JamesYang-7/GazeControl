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
            // Well past the study's N = 18, because the padding has to survive
            // beyond it: every label along the way stays two-wide and sortable.
            var label = "P00";
            for (var i = 0; i < 30; i++)
                label = ParticipantLabel.Next(label);

            Assert.That(label, Is.EqualTo("P30"));
        }

        [Test]
        public void ScheduleOrdinal_StartsAtZeroForTheFirstStudyParticipant()
        {
            // P05 is the first analysed participant, so it takes the schedule's
            // first slot — the pilots are outside the schedule, not ahead of it.
            Assert.That(ParticipantLabel.ScheduleOrdinal(ParticipantLabel.FirstStudyLabel, ParticipantLabel.Study1PilotCount), Is.EqualTo(0));
        }

        [Test]
        public void ScheduleOrdinal_CountsUpOneParticipantAtATime()
        {
            Assert.That(ParticipantLabel.ScheduleOrdinal("P06", ParticipantLabel.Study1PilotCount), Is.EqualTo(1));
            Assert.That(ParticipantLabel.ScheduleOrdinal("P34", ParticipantLabel.Study1PilotCount), Is.EqualTo(29));
        }

        [TestCase("P01")]
        [TestCase("P04")]
        public void ScheduleOrdinal_ForAPilot_IsRefused(string pilot)
        {
            // P01-P04 ran one common order before the schedule existed and are
            // excluded from analysis. Mapping them into a slot would make the
            // marginal balance depend on runs that are not in the sample.
            Assert.That(ParticipantLabel.ScheduleOrdinal(pilot, ParticipantLabel.Study1PilotCount), Is.EqualTo(-1));
        }

        [Test]
        public void ScheduleOrdinal_ForTheDebugLabel_IsRefused()
        {
            Assert.That(ParticipantLabel.ScheduleOrdinal(ParticipantLabel.DebugLabel, ParticipantLabel.Study1PilotCount), Is.EqualTo(-1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("P")]
        public void ScheduleOrdinal_WithNoNumberToRead_IsRefused(string label)
        {
            Assert.That(ParticipantLabel.ScheduleOrdinal(label, ParticipantLabel.Study1PilotCount), Is.EqualTo(-1));
        }

        [Test]
        public void ScheduleOrdinal_IgnoresSurroundingWhitespace()
        {
            // Same reason as Next: this is typed into an inspector field.
            Assert.That(ParticipantLabel.ScheduleOrdinal(" P07 ", ParticipantLabel.Study1PilotCount), Is.EqualTo(2));
        }

        [Test]
        public void ScheduleOrdinal_OverAWholeStudy_IsContiguousFromZero()
        {
            // The registered sample: 18 analysed participants, P05..P22, filling
            // slots 0..17 with no gap — a gap would break the multiple-of-six
            // balance silently.
            var label = ParticipantLabel.FirstStudyLabel;
            for (var expected = 0; expected < 18; expected++)
            {
                Assert.That(ParticipantLabel.ScheduleOrdinal(label, ParticipantLabel.Study1PilotCount), Is.EqualTo(expected));
                label = ParticipantLabel.Next(label);
            }
        }

        [Test]
        public void ScheduleOrdinal_WithNoPilots_StartsTheScheduleAtTheFirstLabel()
        {
            // Study 2 declares no pilots and its roster starts again at P01, so
            // P01 takes slot 0. With study 1's four pilots subtracted instead,
            // P01-P05 all collapsed onto slot 0 (found 2026-09-08).
            Assert.That(ParticipantLabel.ScheduleOrdinal("P01", 0), Is.EqualTo(0));
            Assert.That(ParticipantLabel.ScheduleOrdinal("P05", 0), Is.EqualTo(4));
            Assert.That(ParticipantLabel.ScheduleOrdinal("P06", 0), Is.EqualTo(5));
        }

        [Test]
        public void ScheduleOrdinal_WithNoPilots_StillRefusesTheDebugLabel()
        {
            Assert.That(ParticipantLabel.ScheduleOrdinal(ParticipantLabel.DebugLabel, 0), Is.EqualTo(-1));
        }
    }
}
