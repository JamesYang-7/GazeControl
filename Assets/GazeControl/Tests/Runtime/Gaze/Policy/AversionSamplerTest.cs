using NUnit.Framework;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class AversionSamplerTest
    {
        static ShintaniGazeParameters Parameters => ShintaniGazeParameters.LoadDefault();

        static AversionSampler CreateSystemUnderTest() => new(Parameters, new DeterministicRandom(1));

        static int DirectionChangesOver(AversionSampler sut, float seconds, float tickSeconds)
        {
            sut.Begin(ParticipantRole.Speaker);

            var changes = 0;
            var previous = sut.Direction;
            for (var elapsed = 0f; elapsed < seconds; elapsed += tickSeconds)
            {
                sut.Tick(tickSeconds);
                changes += sut.Direction != previous ? 1 : 0;
                previous = sut.Direction;
            }

            return changes;
        }

        [Test]
        public void Tick_WithinTheRetargetPeriod_HoldsTheDirection()
        {
            var sut = CreateSystemUnderTest();

            var actual = DirectionChangesOver(sut, seconds: 0.6f, tickSeconds: 1f / 30f);

            Assert.That(actual, Is.Zero);
        }

        [Test]
        public void Tick_OverAFourSecondAversion_ReTargetsAboutFiveTimes()
        {
            var sut = CreateSystemUnderTest();

            var actual = DirectionChangesOver(sut, seconds: 4f, tickSeconds: 1f / 30f);

            // 4 s at Shintani's 0.7 s period. Sampling the corpus's 0.217 s segment
            // median instead produced thirteen changes here, which reads as darting.
            Assert.That(actual, Is.InRange(5, 6));
        }

        [Test]
        public void Begin_ForAnyRole_StartsOnAMeasuredDirectionAngle()
        {
            var sut = CreateSystemUnderTest();

            sut.Begin(ParticipantRole.Addressee);

            Assert.That(sut.Offset, Is.EqualTo(Parameters.AversionAngles(sut.Direction)));
        }
    }
}
