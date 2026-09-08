using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Xr
{
    [TestFixture]
    public class ViewRecentringTest
    {
        const float Tolerance = 1e-2f;
        const float EyeHeight = 1.6846f;

        /// <summary>The yaw half alone: a head on the tracking origin, placed at floor level.</summary>
        static ViewRecentring.Result Measure(Quaternion head) => ViewRecentring.Measure(head, Vector3.zero, 0f);

        /// <summary>Where the camera lands in the rig's frame after the play area takes a result.</summary>
        static Vector3 CameraAfter(ViewRecentring.Result result, Vector3 head) =>
            result.PositionOffset + Quaternion.Euler(0f, result.YawOffsetDegrees, 0f) * head;

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
            var result = Measure(Quaternion.identity);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(90f)]
        [TestCase(-90f)]
        [TestCase(35f)]
        [TestCase(179f)]
        public void Measure_FacingOffToOneSide_TurnsBackByThatMuch(float headYaw)
        {
            var result = Measure(Quaternion.Euler(0f, headYaw, 0f));

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(-headYaw).Within(Tolerance));
        }

        [Test]
        public void Measure_FacingBackwards_TurnsThemAround()
        {
            // The participant set up facing away from the triad entirely — the
            // case that makes automatic recentring worth having.
            var result = Measure(Quaternion.Euler(0f, 180f, 0f));

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
            var result = Measure(Quaternion.Euler(0f, 350f, 0f));

            Assert.That(result.YawOffsetDegrees, Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void Measure_WithPitchAndRoll_CorrectsTheYawAndNothingElse()
        {
            // The whole point: the room has no opinion about yaw, but pitch and
            // roll are reported against gravity and are already right. Turning
            // them would tilt the virtual horizon away from the real one.
            var head = Quaternion.Euler(20f, 40f, 15f);

            var result = Measure(head);

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
            var up = Measure(Quaternion.Euler(-90f, 30f, 0f));
            var down = Measure(Quaternion.Euler(90f, 30f, 0f));

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
            var result = Measure(Quaternion.Euler(75f, 30f, 0f));

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

            var first = Measure(head);
            var second = Measure(head);

            Assert.That(second.YawOffsetDegrees, Is.EqualTo(first.YawOffsetDegrees).Within(Tolerance));
        }

        [Test]
        public void Measure_WithNoPose_IsRefused()
        {
            var result = Measure(new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN));

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.YawOffsetDegrees, Is.EqualTo(0f));
            Assert.That(result.PositionOffset, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Measure_HeadOnTheOriginFacingForward_LiftsItToEyeHeightAndNothingElse()
        {
            var head = new Vector3(0f, 1.52f, 0f);

            var result = ViewRecentring.Measure(Quaternion.identity, head, EyeHeight);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.PositionOffset.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(result.PositionOffset.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(CameraAfter(result, head).y, Is.EqualTo(EyeHeight).Within(Tolerance),
                "a short participant starts on the agents' eye line, not their own");
        }

        [TestCase(0f, 1.2f, -0.8f)]
        [TestCase(90f, -2.5f, 3.1f)]
        [TestCase(-135f, 0.4f, 0.4f)]
        [TestCase(180f, 1f, 1f)]
        public void Measure_HeadOffTheOriginAndTurned_LandsOnTheVertexFacingForward(float headYaw, float x, float z)
        {
            // The room's tracking origin is wherever the setup left it, so the
            // participant is standing off it and facing any way at all.
            var rotation = Quaternion.Euler(0f, headYaw, 0f);
            var head = new Vector3(x, 1.7f, z);

            var result = ViewRecentring.Measure(rotation, head, EyeHeight);

            Assert.That(result.Accepted, Is.True);
            var camera = CameraAfter(result, head);
            Assert.That(camera.x, Is.EqualTo(0f).Within(Tolerance), "on the vertex");
            Assert.That(camera.z, Is.EqualTo(0f).Within(Tolerance), "on the vertex");
            Assert.That(camera.y, Is.EqualTo(EyeHeight).Within(Tolerance), "at eye height");
            Assert.That(YawOf(AfterRecentring(rotation, result.YawOffsetDegrees)), Is.EqualTo(0f).Within(Tolerance),
                "facing forward");
        }

        [Test]
        public void Measure_AfterRecentring_MovementTracksOneToOne()
        {
            // The whole point of unlocking translation: a step is a step. The
            // offset is fixed at recentring, so a later head position moves the
            // camera by the same distance, turned by the same yaw.
            var rotation = Quaternion.Euler(0f, 90f, 0f);
            var head = new Vector3(2f, 1.7f, -1f);
            var result = ViewRecentring.Measure(rotation, head, EyeHeight);

            var before = CameraAfter(result, head);
            var after = CameraAfter(result, head + new Vector3(0.3f, -0.1f, 0.2f));

            Assert.That(Vector3.Distance(before, after), Is.EqualTo(new Vector3(0.3f, -0.1f, 0.2f).magnitude).Within(Tolerance));
            Assert.That(after.y - before.y, Is.EqualTo(-0.1f).Within(Tolerance), "crouching lowers the view");
        }

        [Test]
        public void Measure_WithNoPosition_IsRefused()
        {
            var result = ViewRecentring.Measure(
                Quaternion.identity, new Vector3(float.NaN, float.NaN, float.NaN), EyeHeight);

            Assert.That(result.Accepted, Is.False);
            Assert.That(result.PositionOffset, Is.EqualTo(Vector3.zero));
        }
    }
}
