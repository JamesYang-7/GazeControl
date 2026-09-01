using System;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class ParticipantFolderTest
    {
        static readonly DateTime Now = new(2026, 9, 1, 14, 30, 0, DateTimeKind.Local);

        [Test]
        public void ARealParticipantsFolderIsTheirLabel()
        {
            Assert.That(ParticipantFolder.NameFor("P07", Now), Is.EqualTo("P07"));
        }

        [Test]
        public void TheDebugLabelTakesATimestamp()
        {
            // Every development run carries P00 and the response writer refuses to
            // reopen a folder, so without this the second run of the day would
            // refuse to start for a reason that has nothing to do with the study.
            Assert.That(ParticipantFolder.NameFor(ParticipantLabel.DebugLabel, Now),
                Is.EqualTo("P00_20260901_143000"));
        }

        [Test]
        public void ALabelIsTrimmed()
        {
            Assert.That(ParticipantFolder.NameFor("  P07 ", Now), Is.EqualTo("P07"));
        }

        [Test]
        public void AnEmptyLabelIsRefused()
        {
            Assert.That(() => ParticipantFolder.NameFor(" ", Now), Throws.ArgumentException);
        }
    }
}
