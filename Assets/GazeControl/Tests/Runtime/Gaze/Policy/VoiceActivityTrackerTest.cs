using NUnit.Framework;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class VoiceActivityTrackerTest
    {
        const float OnsetSeconds = 0.25f;
        const float OffsetSeconds = 0.5f;

        static readonly ParticipantId Talker = new(0);

        // Ticks are coarse on purpose: the tracker only accumulates elapsed time,
        // so one large tick is equivalent to many small ones and keeps the tests
        // free of loops. Production ticks at 30 Hz.
        static VoiceActivityTracker CreateSystemUnderTest() =>
            new(participantCount: 1, OnsetSeconds, OffsetSeconds);

        [Test]
        public void IsVoiced_VoicedForLessThanOnsetThreshold_ReturnsFalse()
        {
            var sut = CreateSystemUnderTest();

            sut.Tick(0.2f, new[] { true });

            Assert.That(sut.IsVoiced(Talker), Is.False);
        }

        [Test]
        public void IsVoiced_VoicedBeyondOnsetThreshold_ReturnsTrue()
        {
            var sut = CreateSystemUnderTest();

            sut.Tick(0.3f, new[] { true });

            Assert.That(sut.IsVoiced(Talker), Is.True);
        }

        [Test]
        public void IsVoiced_SilentForLessThanOffsetThreshold_RemainsTrue()
        {
            var sut = CreateSystemUnderTest();
            sut.Tick(0.3f, new[] { true });

            sut.Tick(0.4f, new[] { false });

            Assert.That(sut.IsVoiced(Talker), Is.True);
        }

        [Test]
        public void IsVoiced_SilentBeyondOffsetThreshold_ReturnsFalse()
        {
            var sut = CreateSystemUnderTest();
            sut.Tick(0.3f, new[] { true });

            sut.Tick(0.6f, new[] { false });

            Assert.That(sut.IsVoiced(Talker), Is.False);
        }

        [Test]
        public void UtteranceSeconds_WithSilentGapBetweenWords_CountsOnlyVoicedTime()
        {
            var sut = CreateSystemUnderTest();

            sut.Tick(0.3f, new[] { true });
            sut.Tick(0.2f, new[] { false });
            sut.Tick(0.3f, new[] { true });

            Assert.That(sut.UtteranceSeconds(Talker), Is.EqualTo(0.6f).Within(0.001f));
        }

        [Test]
        public void UtteranceSeconds_AfterTurnEnds_ResetsToZero()
        {
            var sut = CreateSystemUnderTest();
            sut.Tick(0.8f, new[] { true });

            sut.Tick(0.6f, new[] { false });

            Assert.That(sut.UtteranceSeconds(Talker), Is.Zero);
        }

        [Test]
        public void UtteranceSeconds_BlipsTooShortToConfirm_DoNotAccumulate()
        {
            var sut = CreateSystemUnderTest();

            sut.Tick(0.2f, new[] { true });
            sut.Tick(0.6f, new[] { false });
            sut.Tick(0.2f, new[] { true });

            Assert.That(sut.UtteranceSeconds(Talker), Is.EqualTo(0.2f).Within(0.001f));
        }
    }
}
