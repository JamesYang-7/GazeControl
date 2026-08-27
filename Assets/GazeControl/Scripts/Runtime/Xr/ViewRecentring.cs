using UnityEngine;

namespace GazeControl.Xr
{
    /// <summary>
    /// Which way to turn the play area so that the participant's "straight
    /// ahead" points between the two agents.
    ///
    /// <para>The headset's yaw origin comes from the room's tracking setup, not
    /// from where the participant happens to be standing, so without this a
    /// session can start with the triad off to one side or behind them. The
    /// operator says "look straight ahead" and recentres.</para>
    ///
    /// <para><b>Yaw only, deliberately.</b> Pitch and roll are reported against
    /// gravity and are therefore already correct; rotating them would tilt the
    /// virtual horizon away from the real one, which is both wrong and the most
    /// reliable way to make someone ill. Only the axis the room has no opinion
    /// about is adjusted.</para>
    ///
    /// <para>Pure arithmetic, like <see cref="ParticipantEyeHeight"/>, so the one
    /// part of this that can be tested without a headset is.</para>
    /// </summary>
    public static class ViewRecentring
    {
        /// <summary>
        /// How much of the head's forward has to lie in the horizontal plane for
        /// its yaw to mean anything: <c>sin(10°)</c>, so a head pitched more than
        /// about 80° from level is refused.
        ///
        /// <para>Looking straight up or down leaves yaw undefined — the forward
        /// vector projects to nothing — and the nearer it gets, the more a
        /// fraction of a degree of noise swings the answer. Refused rather than
        /// guessed from the head's up vector, because the operator is standing
        /// there and can simply ask again.</para>
        /// </summary>
        public const float MinHorizontalForward = 0.17f;

        /// <summary>What one recentring sample yielded.</summary>
        public readonly struct Result
        {
            public Result(float yawOffsetDegrees, bool accepted, string reason)
            {
                YawOffsetDegrees = yawOffsetDegrees;
                Accepted = accepted;
                Reason = reason;
            }

            /// <summary>
            /// Degrees to turn the play area about its vertical axis, in
            /// (-180, 180]. Zero when the sample was rejected.
            /// </summary>
            public float YawOffsetDegrees { get; }

            /// <summary>False when the head pose cannot give a meaningful yaw.</summary>
            public bool Accepted { get; }

            /// <summary>Why a sample was rejected; empty when it was accepted.</summary>
            public string Reason { get; }
        }

        /// <summary>
        /// The play-area yaw that puts <paramref name="headRotation"/>'s forward
        /// on the rig's own forward axis.
        /// </summary>
        /// <param name="headRotation">
        /// The headset's rotation as the runtime reports it, in tracking space —
        /// <b>not</b> the camera's world rotation. Reading the raw device pose is
        /// what makes a second recentring re-measure the participant rather than
        /// compound the first one.
        /// </param>
        public static Result Measure(Quaternion headRotation)
        {
            var forward = headRotation * Vector3.forward;

            // Yaw from the horizontal projection rather than from
            // eulerAngles.y: Euler decomposition flips at the poles and mixes
            // roll into yaw well before it gets there, which would show up as a
            // recentring that is wrong by tens of degrees for a participant who
            // happened to tilt their head.
            var horizontal = new Vector2(forward.x, forward.z);
            if (float.IsNaN(horizontal.x) || float.IsNaN(horizontal.y))
                return new Result(0f, false, "no tracking data yet — the headset reported no pose.");

            if (horizontal.magnitude < MinHorizontalForward)
                return new Result(0f, false,
                    "the head is pointing too near straight up or down for its facing to be read — " +
                    "ask the participant to look level at the room and recentre again.");

            var headYaw = Mathf.Atan2(horizontal.x, horizontal.y) * Mathf.Rad2Deg;

            // DeltaAngle against zero normalises into (-180, 180]: turning the
            // play area -350 degrees and +10 land in the same place, and the
            // small number is the one worth logging and reading.
            return new Result(Mathf.DeltaAngle(headYaw, 0f), true, string.Empty);
        }
    }
}
