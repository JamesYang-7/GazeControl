using NUnit.Framework;

namespace GazeControl.Xr
{
    [TestFixture]
    public class EyeHeightCalibrationTest
    {
        const float Reference = EyeHeightCalibration.DefaultReferenceEyeHeight;
        const float Tolerance = 1e-4f;

        [Test]
        public void Measure_AtTheReferenceHeight_NeedsNoOffset()
        {
            var result = EyeHeightCalibration.Measure(Reference, Reference);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Offset, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Measure_ForATallParticipant_LowersTheRig()
        {
            // Their eyes sit 20 cm above the reference, so the play area drops by
            // 20 cm and they see the triad from the geometry every other
            // participant sees it from.
            var result = EyeHeightCalibration.Measure(1.80f, Reference);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Offset, Is.EqualTo(-0.20f).Within(Tolerance));
        }

        [Test]
        public void Measure_ForAShortParticipant_RaisesTheRig()
        {
            var result = EyeHeightCalibration.Measure(1.45f, Reference);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Offset, Is.EqualTo(0.15f).Within(Tolerance));
        }

        [Test]
        public void Measure_AgainstANonDefaultReference_TargetsThatReference()
        {
            var result = EyeHeightCalibration.Measure(1.60f, 1.70f);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Offset, Is.EqualTo(0.10f).Within(Tolerance));
        }

        [Test]
        public void Measure_AtThePlausibleBounds_IsAccepted()
        {
            Assert.That(EyeHeightCalibration.Measure(
                EyeHeightCalibration.MinPlausibleEyeHeight, Reference).Accepted, Is.True);
            Assert.That(EyeHeightCalibration.Measure(
                EyeHeightCalibration.MaxPlausibleEyeHeight, Reference).Accepted, Is.True);
        }

        [Test]
        public void Measure_BelowThePlausibleFloor_IsRejectedAndMovesNothing()
        {
            // The sample a headset resting on the desk produces. Accepting it
            // would raise the play area by a metre and leave the participant
            // looking at the agents' knees, with nothing downstream able to tell.
            var result = EyeHeightCalibration.Measure(0.75f, Reference);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Offset, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(result.Reason, Is.Not.Empty);
        }

        [Test]
        public void Measure_AboveThePlausibleCeiling_IsRejected()
        {
            var result = EyeHeightCalibration.Measure(2.40f, Reference);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Offset, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Measure_WithNoTrackingData_IsRejected()
        {
            // Before the runtime delivers a pose the camera sits at the origin,
            // and a NaN can arrive from a dropped frame of tracking.
            Assert.That(EyeHeightCalibration.Measure(0f, Reference).Accepted, Is.False);
            Assert.That(EyeHeightCalibration.Measure(float.NaN, Reference).Accepted, Is.False);
        }

        [Test]
        public void Measure_KeepsTheRawMeasurement_ForTheParticipantRecord()
        {
            // The applied offset is normalisation; the raw height is the datum
            // the analysis needs if height ever has to be checked as a covariate.
            var result = EyeHeightCalibration.Measure(1.72f, Reference);

            Assert.That(result.MeasuredEyeHeight, Is.EqualTo(1.72f).Within(Tolerance));
        }
    }
}
