using UnityEngine;

namespace GazeControl.Demo
{
    /// <summary>
    /// Where the demo camera stands, and what its frame covers — the arithmetic
    /// behind <see cref="DemoCamera"/>, with no scene in it.
    ///
    /// <para>The study's own camera cannot be used for a flat video. It is the
    /// participant's eye, the agents aim at it, and it sits on the triad's eye
    /// line looking straight ahead: in a headset that is exactly right, and in a
    /// 16:9 frame it puts two heads across the middle with a third of the picture
    /// empty above them and both bodies cut off at the hip. The demo camera is a
    /// second, locked-off camera that frames the pair as a shot — pulled back
    /// along the same sight line and tilted down — while the participant's camera
    /// stays where it is so that nothing about the rendered gaze changes.</para>
    ///
    /// <para>Pure arithmetic, like <c>ViewRecentring</c>: the framing is
    /// judged by eye, but a transposed sign in the placement would be judged by
    /// eye too, and much later.</para>
    /// </summary>
    public static class DemoCameraFrame
    {
        /// <summary>
        /// How far from level either frame edge is allowed to get before the
        /// vertical span stops meaning anything, degrees. An edge at 90° is
        /// parallel to the ground and meets a vertical plane nowhere, and the
        /// tangent runs away long before that.
        /// </summary>
        public const float MaxEdgeAngleDegrees = 89f;

        /// <summary>What the frame covers vertically at one distance, in metres above the floor.</summary>
        public readonly struct Span
        {
            public Span(float bottom, float top)
            {
                Bottom = bottom;
                Top = top;
            }

            /// <summary>Height of the frame's bottom edge, metres.</summary>
            public float Bottom { get; }

            /// <summary>Height of the frame's top edge, metres.</summary>
            public float Top { get; }

            /// <summary>How much of the world is in frame vertically, metres.</summary>
            public float Height => Top - Bottom;

            /// <summary>True when <paramref name="height"/> is inside the frame.</summary>
            public bool Contains(float height) => height >= Bottom && height <= Top;
        }

        /// <summary>
        /// Where to put the demo camera, given the triad vertex the participant
        /// stands on.
        ///
        /// <para>Anchored on the vertex rather than on the participant's camera
        /// because the vertex is the one pose that never moves: recentring turns
        /// and slides the play area under it, and the head tracks freely inside
        /// that, so a camera that followed the eye would drift with whoever last
        /// wore the headset. The height is absolute, not an offset from the eye,
        /// for the same reason.</para>
        /// </summary>
        /// <param name="vertexPosition">The triad vertex, world space.</param>
        /// <param name="vertexRotation">The vertex's facing; only its yaw is used.</param>
        /// <param name="pullBackMetres">Metres to step back along the sight line.</param>
        /// <param name="lateralOffsetMetres">Metres to step to the participant's right.</param>
        /// <param name="heightMetres">Camera height above the floor plane at y = 0.</param>
        /// <param name="pitchDegrees">Downward tilt; positive tips the lens towards the floor.</param>
        public static Pose Place(
            Vector3 vertexPosition,
            Quaternion vertexRotation,
            float pullBackMetres,
            float lateralOffsetMetres,
            float heightMetres,
            float pitchDegrees)
        {
            var forward = SightLine(vertexRotation);
            var right = Vector3.Cross(Vector3.up, forward);

            var position = vertexPosition
                           - forward * pullBackMetres
                           + right * lateralOffsetMetres;
            position.y = heightMetres;

            var rotation = Quaternion.LookRotation(forward, Vector3.up)
                           * Quaternion.Euler(pitchDegrees, 0f, 0f);

            return new Pose(position, rotation);
        }

        /// <summary>
        /// The vertex's facing flattened onto the floor, normalised.
        ///
        /// <para>Taken from the forward vector's horizontal projection rather than
        /// from <c>eulerAngles.y</c>, which mixes roll into yaw — the same reason
        /// <c>ViewRecentring</c> does it this way. A vertex pointing
        /// straight up has no sight line to flatten and falls back to world
        /// forward; it cannot happen in either study scene, where the vertex is
        /// level by construction.</para>
        /// </summary>
        public static Vector3 SightLine(Quaternion vertexRotation)
        {
            var forward = vertexRotation * Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-6f ? Vector3.forward : forward.normalized;
        }

        /// <summary>
        /// What the frame covers, in metres above the floor, on a vertical plane
        /// <paramref name="horizontalDistance"/> ahead of the camera — the numbers
        /// that say whether the agents' feet and the tops of their heads are in
        /// the picture.
        ///
        /// <para>Exact rather than approximate: the frustum's top and bottom edges
        /// are planes through the lens at fixed angles to level, so where each one
        /// crosses a vertical plane is one tangent. The frame is not perpendicular
        /// to the ground once the camera is tilted, which is why this is expressed
        /// against horizontal distance and not against distance along the lens
        /// axis.</para>
        /// </summary>
        public static Span VerticalSpanAt(
            float horizontalDistance,
            float heightMetres,
            float pitchDegrees,
            float verticalFieldOfView)
        {
            var half = verticalFieldOfView * 0.5f;
            var top = heightMetres + horizontalDistance * TangentOfDrop(half - pitchDegrees);
            var bottom = heightMetres - horizontalDistance * TangentOfDrop(pitchDegrees + half);
            return new Span(bottom, top);
        }

        /// <summary>
        /// The horizontal field of view a vertical one gives at an aspect ratio,
        /// degrees. Unity's <c>Camera.fieldOfView</c> is the vertical one, and
        /// whether both agents fit across the frame is the horizontal question.
        /// </summary>
        public static float HorizontalFieldOfView(float verticalFieldOfView, float aspect)
        {
            var halfVertical = Mathf.Tan(verticalFieldOfView * 0.5f * Mathf.Deg2Rad);
            return 2f * Mathf.Atan(halfVertical * aspect) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// How far a ray at <paramref name="degreesBelowLevel"/> drops per metre
        /// travelled horizontally, clamped so that an edge approaching vertical
        /// gives a large number instead of an infinite one.
        /// </summary>
        static float TangentOfDrop(float degreesBelowLevel) =>
            Mathf.Tan(Mathf.Clamp(degreesBelowLevel, -MaxEdgeAngleDegrees, MaxEdgeAngleDegrees) * Mathf.Deg2Rad);
    }
}
