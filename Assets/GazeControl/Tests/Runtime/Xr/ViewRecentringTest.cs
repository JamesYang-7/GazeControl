using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Xr
{
    [TestFixture]
    public class ViewRecentringTest
    {
        const float Tolerance = 1e-2f;

        /// <summary>Yaw of a rotation's forward, in (-180, 180].</summary>
        static float YawOf(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        /// <summary>The head's facing after the play area has been turned by an offset.</summary>
        static Quaternion AfterRecentring(Quaternion head, float yawOffset) =>
            Quaternion.Euler(0f, yawOffset, 0f) * head;

        [Test]
        public void Measure_AlreadyFacingForward_TurnsNothing()
        {
            var result = ViewRecentring.Measure(Quaternion.identity);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(90f)]
        [TestCase(-90f)]
        [TestCase(35f)]
        [TestCase(179f)]
        public void Measure_FacingOffToOneSide_TurnsBackByThatMuch(float headYaw)
        {
            var result = ViewRecentring.Measure(Quaternion.Euler(0f, headYaw, 0f));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(-headYaw).Within(Tolerance));
        }

        [Test]
        public void Measure_FacingBackwards_TurnsThemAround()
        {
            // The participant set up facing away from the triad entirely — the
            // case that makes automatic recentring worth having.
            var result = ViewRecentring.Measure(Quaternion.Euler(0f, 180f, 0f));

            Assert.That(result.Accepted, Is.True);
            Assert.That(Mathf.Abs(result.YawOffsetDegrees), Is.EqualTo(180f).Within(Tolerance));
            Assert.That(YawOf(AfterRecentring(Quaternion.Euler(0f, 180f, 0f), result.YawOffsetDegrees)),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Measure_IsNormalisedIntoHalfATurnEitherWay()
        {
            // 350 degrees right is 10 degrees left; the log and the operator want
            // the small number.
            var result = ViewRecentring.Measure(Quaternion.Euler(0f, 350f, 0f));

            Assert.That(result.YawOffsetDegrees, Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void Measure_WithPitchAndRoll_CorrectsTheYawAndNothingElse()
        {
            // The whole point: the room has no opinion about yaw, but pitch and
            // roll are reported against gravity and are already right. Turning
            // them would tilt the virtual horizon away from the real one.
            var head = Quaternion.Euler(20f, 40f, 15f);

            var result = ViewRecentring.Measure(head);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(-40f).Within(Tolerance),
                "roll must not leak into the measured yaw");

            var recentred = AfterRecentring(head, result.YawOffsetDegrees);
            Assert.That(YawOf(recentred), Is.EqualTo(0f).Within(Tolerance), "now facing forward");

            // Pitch survives: the forward vector's elevation is untouched.
            Assert.That((recentred * Vector3.forward).y,
                Is.EqualTo((head * Vector3.forward).y).Within(Tolerance), "pitch must be unchanged");

            // And so does roll: the head's up vector keeps its tilt off vertical.
            Assert.That(Vector3.Angle(recentred * Vector3.up, Vector3.up),
                Is.EqualTo(Vector3.Angle(head * Vector3.up, Vector3.up)).Within(Tolerance),
                "roll must be unchanged — a tilted virtual horizon is the worst outcome here");
        }

        [Test]
        public void Measure_LookingStraightUpOrDown_IsRefusedRatherThanGuessed()
        {
            // Yaw is undefined when the forward vector projects to nothing, and
            // near-vertical it swings wildly on noise. The operator is standing
            // there and can ask the participant to look level.
            var up = ViewRecentring.Measure(Quaternion.Euler(-90f, 30f, 0f));
            var down = ViewRecentring.Measure(Quaternion.Euler(90f, 30f, 0f));

            Assert.That(up.Accepted, Is.False);
            Assert.That(up.Reason, Is.Not.Empty);
            Assert.That(up.YawOffsetDegrees, Is.EqualTo(0f), "a refused sample turns nothing");
            Assert.That(down.Accepted, Is.False);
        }

        [Test]
        public void Measure_JustInsideTheLevelnessLimit_IsStillAccepted()
        {
            // sin(10 deg) is the threshold, so a head pitched 75 degrees down
            // still has enough horizontal forward to read.
            var result = ViewRecentring.Measure(Quaternion.Euler(75f, 30f, 0f));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(-30f).Within(Tolerance));
        }

        [Test]
        public void Measure_TwiceInARow_ReMeasuresRatherThanCompounding()
        {
            // The rig feeds it the raw device pose both times, so a second
            // recentring from the same head pose must ask for the same turn, not
            // twice the turn.
            var head = Quaternion.Euler(0f, 60f, 0f);

            var first = ViewRecentring.Measure(head);
            var second = ViewRecentring.Measure(head);

            Assert.That(second.YawOffsetDegrees, Is.EqualTo(first.YawOffsetDegrees).Within(Tolerance));
        }

        [Test]
        public void Measure_WithNoPose_IsRefused()
        {
            var result = ViewRecentring.Measure(new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(0f));
        }
    }
}
