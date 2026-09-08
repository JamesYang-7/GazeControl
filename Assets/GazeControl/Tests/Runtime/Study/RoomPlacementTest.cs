using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Study
{
    [TestFixture]
    public class RoomPlacementTest
    {
        // A seat measured off a real clip (study2_c1) and the study rig's User
        // vertex: at (0, -0.866) facing +Z.
        const float SeatX = 0.14f;
        const float SeatZ = 0.41f;
        const float SeatYaw = 158.3f;
        const float ViewX = 0f;
        const float ViewZ = -0.866f;
        const float ViewYaw = 0f;

        [Test]
        public void Solve_PutsTheSeatOnTheViewpoint()
        {
            var pose = RoomPlacement.Solve(SeatX, SeatZ, SeatYaw, ViewX, ViewZ, ViewYaw);

            // Checked through Unity's own transform maths rather than the
            // solver's, so a wrong-handed rotation cannot cancel itself out.
            var moved = Quaternion.Euler(0f, pose.YawDegrees, 0f) * new Vector3(SeatX, 0f, SeatZ)
                        + new Vector3(pose.X, 0f, pose.Z);

            Assert.That(moved.x, Is.EqualTo(ViewX).Within(1e-4f), "x");
            Assert.That(moved.z, Is.EqualTo(ViewZ).Within(1e-4f), "z");
        }

        [Test]
        public void Solve_TurnsTheSeatFacingOntoTheViewDirection()
        {
            var pose = RoomPlacement.Solve(SeatX, SeatZ, SeatYaw, ViewX, ViewZ, ViewYaw);

            var seatForward = Quaternion.Euler(0f, SeatYaw, 0f) * Vector3.forward;
            var turned = Quaternion.Euler(0f, pose.YawDegrees, 0f) * seatForward;

            Assert.That(Vector3.Angle(turned, Vector3.forward), Is.LessThan(1e-3f));
        }

        [Test]
        public void Solve_WithAViewpointFacingAwayFromZ_TurnsByTheDifference()
        {
            var pose = RoomPlacement.Solve(0f, 0f, 30f, 0f, 0f, -45f);

            Assert.That(pose.YawDegrees, Is.EqualTo(-75f).Within(1e-4f));
        }

        [Test]
        public void Rotate_ByNinetyDegrees_TurnsForwardOntoRight()
        {
            var (x, z) = RoomPlacement.Rotate(0f, 1f, 90f);

            Assert.That(x, Is.EqualTo(1f).Within(1e-5f), "x");
            Assert.That(z, Is.EqualTo(0f).Within(1e-5f), "z");
        }

        [Test]
        public void YawOf_Right_IsNinety()
        {
            Assert.That(RoomPlacement.YawOf(1f, 0f), Is.EqualTo(90f).Within(1e-4f));
        }
    }
}
