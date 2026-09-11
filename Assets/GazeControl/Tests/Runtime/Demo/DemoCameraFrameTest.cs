using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Demo
{
    [TestFixture]
    public class DemoCameraFrameTest
    {
        const float Tolerance = 1e-3f;
        const float EyeHeight = 1.6846f;

        /// <summary>The study's own vertex: level, facing +Z, on the floor plane.</summary>
        static readonly Vector3 Vertex = new(0f, 0f, -0.866f);

        /// <summary>Yaw of a rotation's forward, degrees in (-180, 180].</summary>
        static float YawOf(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            return Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        /// <summary>How far below level a rotation's forward points, degrees.</summary>
        static float PitchOf(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            return -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        [Test]
        public void Place_NoPullBackOrTilt_SitsOnTheVertexAtTheGivenHeight()
        {
            var pose = DemoCameraFrame.Place(Vertex, Quaternion.identity, 0f, 0f, EyeHeight, 0f);

            Assert.That(pose.position.x, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(pose.position.z, Is.EqualTo(-0.866f).Within(Tolerance));
            Assert.That(pose.position.y, Is.EqualTo(EyeHeight).Within(Tolerance));
            Assert.That(YawOf(pose.rotation), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(PitchOf(pose.rotation), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Place_PullingBack_StepsAwayAlongTheSightLineAndNotThroughIt()
        {
            var pose = DemoCameraFrame.Place(Vertex, Quaternion.identity, 1.2f, 0f, EyeHeight, 0f);

            // Facing +Z, so back is -Z: the agents get further away, never nearer.
            Assert.That(pose.position.z, Is.EqualTo(-0.866f - 1.2f).Within(Tolerance));
            Assert.That(pose.position.x, Is.EqualTo(0f).Within(Tolerance));
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(-135f)]
        [TestCase(180f)]
        public void Place_VertexTurned_PullsBackAlongWhicheverWayItFaces(float vertexYaw)
        {
            var rotation = Quaternion.Euler(0f, vertexYaw, 0f);

            var pose = DemoCameraFrame.Place(Vertex, rotation, 1.2f, 0f, EyeHeight, 0f);

            var stepped = pose.position - Vertex;
            stepped.y = 0f;
            Assert.That(stepped.magnitude, Is.EqualTo(1.2f).Within(Tolerance));
            Assert.That(Vector3.Dot(stepped.normalized, rotation * Vector3.forward),
                Is.EqualTo(-1f).Within(Tolerance), "the step must be straight backwards");

            // As a difference, not a value: a yaw of 180 comes back as -180, which
            // is the same direction and would otherwise fail on the sign alone.
            Assert.That(Mathf.DeltaAngle(YawOf(pose.rotation), vertexYaw),
                Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Place_LateralOffset_StepsToTheParticipantsRight()
        {
            var pose = DemoCameraFrame.Place(Vertex, Quaternion.identity, 0f, 0.5f, EyeHeight, 0f);

            // Facing +Z, the participant's right is +X.
            Assert.That(pose.position.x, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(pose.position.z, Is.EqualTo(-0.866f).Within(Tolerance));
        }

        [TestCase(0f)]
        [TestCase(10f)]
        [TestCase(-5f)]
        public void Place_Pitch_TiltsTheLensDownByThatMuchAndLeavesTheYawAlone(float pitch)
        {
            var pose = DemoCameraFrame.Place(Vertex, Quaternion.Euler(0f, 40f, 0f), 1f, 0f, EyeHeight, pitch);

            Assert.That(PitchOf(pose.rotation), Is.EqualTo(pitch).Within(Tolerance));
            Assert.That(YawOf(pose.rotation), Is.EqualTo(40f).Within(Tolerance));
        }

        [Test]
        public void Place_HeightIsAbsolute_AndIgnoresWhereTheVertexSits()
        {
            // A working scene can have the vertex nudged off the floor plane; the
            // shot's height must not quietly inherit that.
            var sunken = DemoCameraFrame.Place(
                Vertex + new Vector3(0f, -0.1f, 0f), Quaternion.identity, 1f, 0f, EyeHeight, 0f);

            Assert.That(sunken.position.y, Is.EqualTo(EyeHeight).Within(Tolerance));
        }

        [Test]
        public void SightLine_VertexPitchedAndRolled_StaysFlatAndUnit()
        {
            var sightLine = DemoCameraFrame.SightLine(Quaternion.Euler(25f, 90f, 30f));

            Assert.That(sightLine.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(sightLine.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(sightLine.x, Is.EqualTo(1f).Within(1e-2f), "yaw 90 faces +X");
        }

        [Test]
        public void SightLine_LookingStraightUp_FallsBackToWorldForwardRatherThanZero()
        {
            var sightLine = DemoCameraFrame.SightLine(Quaternion.Euler(-90f, 0f, 0f));

            Assert.That(sightLine, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void VerticalSpanAt_LevelCamera_IsSymmetricAboutTheLensHeight()
        {
            var span = DemoCameraFrame.VerticalSpanAt(2f, EyeHeight, 0f, 60f);

            // Half-angle 30 degrees, tan 30 = 0.5774, so 1.1547 m either way at 2 m.
            Assert.That(span.Top, Is.EqualTo(EyeHeight + 1.1547f).Within(Tolerance));
            Assert.That(span.Bottom, Is.EqualTo(EyeHeight - 1.1547f).Within(Tolerance));
            Assert.That(span.Height, Is.EqualTo(2.3094f).Within(Tolerance));
        }

        [Test]
        public void VerticalSpanAt_TiltingDown_TradesHeadroomForBody()
        {
            const float distance = 2.2f;
            var level = DemoCameraFrame.VerticalSpanAt(distance, EyeHeight, 0f, 42f);
            var tilted = DemoCameraFrame.VerticalSpanAt(distance, EyeHeight, 10f, 42f);

            Assert.That(tilted.Top, Is.LessThan(level.Top));
            Assert.That(tilted.Bottom, Is.LessThan(level.Bottom));
        }

        [Test]
        public void VerticalSpanAt_TheDefaultFraming_HoldsAnAgentFromTheKneeToOverTheHead()
        {
            // The narrowest session room: an agent 1.143 m from the seat, so
            // 0.866 * 1.143 + 1.0 = 1.990 m ahead of a camera pulled back 1 m.
            var span = DemoCameraFrame.VerticalSpanAt(1.99f, EyeHeight, 10f, 42f);

            Assert.That(span.Contains(1.75f), Is.True, "the top of the agent's head");
            Assert.That(span.Contains(0.5f), Is.True, "the knee");
            Assert.That(span.Contains(0f), Is.False, "the feet stay out of frame, as intended");
        }

        [Test]
        public void VerticalSpanAt_FurtherAway_SeesMoreOfTheWorld()
        {
            var near = DemoCameraFrame.VerticalSpanAt(2f, EyeHeight, 10f, 42f);
            var far = DemoCameraFrame.VerticalSpanAt(3f, EyeHeight, 10f, 42f);

            Assert.That(far.Height, Is.GreaterThan(near.Height));
        }

        [Test]
        public void VerticalSpanAt_EdgeApproachingVertical_StaysFiniteRatherThanBlowingUp()
        {
            var span = DemoCameraFrame.VerticalSpanAt(2f, EyeHeight, 80f, 60f);

            Assert.That(float.IsFinite(span.Bottom), Is.True);
            Assert.That(float.IsFinite(span.Top), Is.True);
        }

        [Test]
        public void HorizontalFieldOfView_SixteenByNine_IsWiderThanTheVerticalOne()
        {
            var horizontal = DemoCameraFrame.HorizontalFieldOfView(42f, 16f / 9f);

            Assert.That(horizontal, Is.EqualTo(68.5f).Within(0.5f));
        }

        [Test]
        public void HorizontalFieldOfView_SquareFrame_MatchesTheVerticalOne()
        {
            Assert.That(DemoCameraFrame.HorizontalFieldOfView(42f, 1f), Is.EqualTo(42f).Within(Tolerance));
        }

        [Test]
        public void HorizontalFieldOfView_TheDefaultFraming_HoldsBothAgentsOfTheWidestRoom()
        {
            // The widest session room: an agent 1.622 m from the seat at 30 degrees
            // off the sight line, seen from 1 m behind it, shoulder included.
            const float side = 1.622f;
            const float shoulderHalfWidth = 0.25f;
            var lateral = side * 0.5f;
            var ahead = side * Mathf.Cos(30f * Mathf.Deg2Rad) + 1f;

            var halfFrame = DemoCameraFrame.HorizontalFieldOfView(42f, 16f / 9f) * 0.5f;
            var shoulderAngle = Mathf.Atan2(lateral + shoulderHalfWidth, ahead) * Mathf.Rad2Deg;

            Assert.That(shoulderAngle, Is.LessThan(halfFrame));
        }
    }
}
