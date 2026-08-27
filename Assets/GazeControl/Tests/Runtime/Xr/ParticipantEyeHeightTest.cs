using NUnit.Framework;

namespace GazeControl.Xr
{
    [TestFixture]
    public class ParticipantEyeHeightTest
    {
        [Test]
        public void SmplxEyeHeight_SitsOnTheAgentsOwnEyeLine()
        {
            // Measured off the scene's SMPL-X agents: left_eye_smplhf and
            // right_eye_smplhf both sit at world y = 1.6846 m in the rest pose,
            // and a 1,200-frame clip driven through the motion player puts them
            // at mean 1.6815 over the range 1.663-1.713. If the model or its
            // scale ever changes, this is the assertion that says the participant
            // is no longer level with the agents.
            Assert.That(ParticipantEyeHeight.SmplxEyeHeight, Is.EqualTo(1.6846f).Within(1e-4f));

            // And it has to stay inside the range a real participant can occupy,
            // or the rig would place the viewpoint somewhere it rejects as a
            // measurement.
            Assert.That(ParticipantEyeHeight.SmplxEyeHeight,
                Is.InRange(ParticipantEyeHeight.MinPlausibleEyeHeight, ParticipantEyeHeight.MaxPlausibleEyeHeight));
        }

        [Test]
        public void Measure_AStandingParticipant_IsAcceptedAndKeepsTheSample()
        {
            var result = ParticipantEyeHeight.Measure(1.72f);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.MeasuredEyeHeight, Is.EqualTo(1.72f).Within(1e-4f));
            Assert.That(result.Reason, Is.Empty);
        }

        [Test]
        public void Measure_AtThePlausibleBounds_IsAccepted()
        {
            Assert.That(ParticipantEyeHeight.Measure(
                ParticipantEyeHeight.MinPlausibleEyeHeight).Accepted, Is.True);
            Assert.That(ParticipantEyeHeight.Measure(
                ParticipantEyeHeight.MaxPlausibleEyeHeight).Accepted, Is.True);
        }

        [Test]
        public void Measure_BelowThePlausibleFloor_IsRejectedWithAReason()
        {
            // The headset sitting on a desk. Rejected rather than clamped: a
            // clamp would put a number that looks like a real measurement into
            // the participant's record.
            var result = ParticipantEyeHeight.Measure(0.75f);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.Not.Empty);
            Assert.That(result.MeasuredEyeHeight, Is.EqualTo(0.75f).Within(1e-4f),
                "the rejected sample is still reported, so the operator can see what was read");
        }

        [Test]
        public void Measure_AboveThePlausibleCeiling_IsRejectedWithAReason()
        {
            var result = ParticipantEyeHeight.Measure(2.40f);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.Not.Empty);
        }

        [Test]
        public void Measure_WithNoPoseAtAll_IsRejected()
        {
            Assert.That(ParticipantEyeHeight.Measure(0f).Accepted, Is.False);
            Assert.That(ParticipantEyeHeight.Measure(float.NaN).Accepted, Is.False);
            Assert.That(ParticipantEyeHeight.Measure(float.PositiveInfinity).Accepted, Is.False);
        }
    }
}
