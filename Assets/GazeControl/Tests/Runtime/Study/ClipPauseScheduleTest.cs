using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class ClipPauseScheduleTest
    {
        const float Hold = 1.5f;

        static ClipPauseSchedule Sut(float holdSeconds = Hold) => new(holdSeconds);

        [Test]
        public void WhileTheClipRuns_ItStaysPlaying()
        {
            var sut = Sut();

            for (var i = 0; i < 100; i++)
                Assert.That(sut.Advance(clipFinished: false, 1f / 60f), Is.EqualTo(ClipPhase.Playing));

            Assert.That(sut.IsPaused, Is.False);
        }

        [Test]
        public void WhenTheVoicesStop_ItHoldsBeforePausing()
        {
            var sut = Sut();

            Assert.That(sut.Advance(clipFinished: true, 1f / 60f), Is.EqualTo(ClipPhase.Holding),
                "cutting to a blank scene on the last syllable reads as a crash, not an ending");
            Assert.That(sut.HoldRemaining, Is.EqualTo(Hold).Within(1e-4f));
        }

        [Test]
        public void OnceTheHoldElapses_ItPauses()
        {
            var sut = Sut();
            sut.Advance(clipFinished: true, 0f);

            var phase = ClipPhase.Holding;
            for (var i = 0; i < 200 && phase != ClipPhase.Paused; i++)
                phase = sut.Advance(clipFinished: true, 1f / 60f);

            Assert.That(phase, Is.EqualTo(ClipPhase.Paused));
            Assert.That(sut.IsPaused, Is.True);
            Assert.That(sut.HoldRemaining, Is.EqualTo(0f));
        }

        [Test]
        public void TheHoldLastsTheRequestedTime()
        {
            var sut = Sut(1f);
            sut.Advance(clipFinished: true, 0f);

            // 0.9 s in, still holding; a step past 1.0 s and it pauses.
            for (var i = 0; i < 9; i++)
                Assert.That(sut.Advance(true, 0.1f), Is.EqualTo(ClipPhase.Holding));

            Assert.That(sut.Advance(true, 0.2f), Is.EqualTo(ClipPhase.Paused));
        }

        [Test]
        public void WithNoHold_ItPausesOnTheFrameTheClipEnds()
        {
            // Not one frame later: a zero hold means the caller wants the scene
            // taken away immediately, and an off-by-one frame here would be
            // invisible in review and wrong in the headset.
            var sut = Sut(0f);

            Assert.That(sut.Advance(clipFinished: true, 0f), Is.EqualTo(ClipPhase.Paused));
        }

        [Test]
        public void ANegativeHold_IsTreatedAsNone()
        {
            // The hold is read off a segment file; a malformed one should shorten
            // the hold, not throw in the middle of a participant's session.
            var sut = Sut(-3f);

            Assert.That(sut.Advance(clipFinished: true, 0f), Is.EqualTo(ClipPhase.Paused));
        }

        [Test]
        public void OncePaused_ItStaysPausedHoweverLongTheOperatorTakes()
        {
            var sut = Sut(0f);
            sut.Advance(true, 0f);

            for (var i = 0; i < 600; i++)
                Assert.That(sut.Advance(true, 0.1f), Is.EqualTo(ClipPhase.Paused));
        }

        [Test]
        public void OncePaused_AClipThatSomehowRestartsDoesNotUnpauseIt()
        {
            // The participant is halfway through answering. Nothing the scene
            // does may take the question away from them.
            var sut = Sut(0f);
            sut.Advance(true, 0f);

            Assert.That(sut.Advance(clipFinished: false, 1f / 60f), Is.EqualTo(ClipPhase.Paused));
        }

        [Test]
        public void SkipToPause_FromPlaying_GoesStraightToPaused()
        {
            var sut = Sut();

            sut.SkipToPause();

            Assert.That(sut.Phase, Is.EqualTo(ClipPhase.Paused));
            Assert.That(sut.IsPaused, Is.True);
            Assert.That(sut.HoldRemaining, Is.EqualTo(0f), "the hold is skipped too");
        }

        [Test]
        public void SkipToPause_MidHold_SkipsTheRemainder()
        {
            var sut = Sut();
            sut.Advance(clipFinished: true, 0.2f);

            sut.SkipToPause();

            Assert.That(sut.Phase, Is.EqualTo(ClipPhase.Paused));
        }

        [Test]
        public void SkipToPause_ThenResume_GivesTheNextClipItsFullHold()
        {
            // The skipped clip's shortened hold must not carry into the next one.
            var sut = Sut();
            sut.SkipToPause();

            sut.Resume();
            sut.Advance(clipFinished: true, 0f);

            Assert.That(sut.Phase, Is.EqualTo(ClipPhase.Holding));
            Assert.That(sut.HoldRemaining, Is.EqualTo(Hold).Within(1e-4f));
        }

        [Test]
        public void Resume_StartsTheNextClipCleanly()
        {
            var sut = Sut();
            sut.Advance(true, 0f);
            sut.Advance(true, 10f);
            Assert.That(sut.IsPaused, Is.True);

            sut.Resume();

            Assert.That(sut.Phase, Is.EqualTo(ClipPhase.Playing));
            Assert.That(sut.HoldRemaining, Is.EqualTo(0f));

            // And the next clip gets the full hold, not the remainder of the last.
            sut.Advance(clipFinished: true, 0f);
            Assert.That(sut.HoldRemaining, Is.EqualTo(Hold).Within(1e-4f));
        }

        [Test]
        public void Resume_MidHold_IsAllowed()
        {
            // An operator who advances early just moves on; nothing should throw
            // or wedge because the hold had not finished.
            var sut = Sut();
            sut.Advance(true, 0.5f);

            sut.Resume();

            Assert.That(sut.Phase, Is.EqualTo(ClipPhase.Playing));
        }

        [Test]
        public void NonPositiveDeltaTime_DoesNotAdvanceTheHold()
        {
            // A paused editor or a stalled frame must not consume the hold.
            var sut = Sut();
            sut.Advance(true, 0f);

            for (var i = 0; i < 50; i++)
                sut.Advance(true, 0f);

            Assert.That(sut.Phase, Is.EqualTo(ClipPhase.Holding));
            Assert.That(sut.HoldRemaining, Is.EqualTo(Hold).Within(1e-4f));
        }
    }
}
