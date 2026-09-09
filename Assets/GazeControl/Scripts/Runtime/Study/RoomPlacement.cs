using UnityEngine;

namespace GazeControl.Study
{
    /// <summary>
    /// The rigid move that puts a recorded room onto the study rig: turn and
    /// slide the whole three-person triangle so that the participant's seat (the equilateral apex on the two agents)
    /// lands on the participant's fixed viewpoint and the seat's opening facing
    /// lands on the participant's initial view direction.
    ///
    /// <para>A rigid transform and nothing else (user's call, 2026-09-08): the
    /// two agents keep exactly the distance and bearing the recorded people had
    /// from the listener, so what changes between clips is the room, never the
    /// participant. That is what preserves study 1's controlled viewpoint — every
    /// participant sees every clip from the identical point — while the clips
    /// carry their own geometry.</para>
    ///
    /// <para>Yaw only, in the horizontal plane. The floor stays the floor, and
    /// the seat's height is left where the default body put it (1.61-1.72 m over
    /// the seven clips) against the rig's fixed 1.6846 m; the difference is
    /// within the agents' own sway and, as with <c>ThreePartyViewpoint</c>, the
    /// gaze is decided by target rather than by angle.</para>
    ///
    /// <para>Yaw is Unity's: degrees clockwise from +Z seen from above, i.e.
    /// <c>atan2(x, z)</c> of a horizontal direction.</para>
    /// </summary>
    public static class RoomPlacement
    {
        /// <summary>Where the room goes: a horizontal position and a yaw, in the rig's frame.</summary>
        public readonly struct Pose
        {
            public Pose(float x, float z, float yawDegrees)
            {
                X = x;
                Z = z;
                YawDegrees = yawDegrees;
            }

            public float X { get; }
            public float Z { get; }
            public float YawDegrees { get; }
        }

        /// <summary>
        /// Solve the room pose that maps the seat onto the viewpoint.
        /// </summary>
        /// <param name="seatX">The participant's seat in the recorded room, horizontal.</param>
        /// <param name="seatZ">The participant's seat in the recorded room, horizontal.</param>
        /// <param name="seatYawDegrees">The seat's opening facing in the recorded room.</param>
        /// <param name="viewX">The participant's fixed viewpoint, horizontal.</param>
        /// <param name="viewZ">The participant's fixed viewpoint, horizontal.</param>
        /// <param name="viewYawDegrees">The participant's initial view direction.</param>
        public static Pose Solve(
            float seatX, float seatZ, float seatYawDegrees,
            float viewX, float viewZ, float viewYawDegrees)
        {
            var yaw = viewYawDegrees - seatYawDegrees;
            var (turnedX, turnedZ) = Rotate(seatX, seatZ, yaw);
            return new Pose(viewX - turnedX, viewZ - turnedZ, yaw);
        }

        /// <summary>A horizontal point turned by <paramref name="yawDegrees"/> about the vertical axis.</summary>
        public static (float x, float z) Rotate(float x, float z, float yawDegrees)
        {
            var radians = yawDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);

            // Clockwise from above, as Unity's yaw is: +Z turned by +90° lands on +X.
            return (x * cos + z * sin, -x * sin + z * cos);
        }

        /// <summary>The yaw of a horizontal direction; zero along +Z.</summary>
        public static float YawOf(float directionX, float directionZ) =>
            Mathf.Atan2(directionX, directionZ) * Mathf.Rad2Deg;
    }
}
