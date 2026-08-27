namespace GazeControl.Xr
{
    /// <summary>
    /// The height the participant's viewpoint is fixed at, and whether a measured
    /// standing height is believable.
    ///
    /// <para>Why fix it at all, when the agents aim at the participant's head and
    /// so look at them correctly whatever their height: the *stimulus* geometry
    /// would otherwise differ between participants. The agents are standing mocap
    /// with their eyes at <see cref="SmplxEyeHeight"/>, so an unadjusted 1.50 m
    /// participant looks up at them and a 1.90 m one looks down, and how clearly
    /// a gaze shift reads is exactly what the study measures. A fixed viewpoint
    /// makes height a constant instead of an uncontrolled between-participant
    /// variable (`user-study-design.md` §4).</para>
    ///
    /// <para><b>This used to be a calibration</b> — measure the participant, then
    /// offset the play area by the difference — and it no longer is. Since
    /// 2026-08-27 <see cref="XrParticipantRig"/> does not track head translation
    /// at all, so there is nothing to offset and no step to run: the camera is
    /// placed at <see cref="SmplxEyeHeight"/> and stays there. What survives here
    /// is the constant and the plausibility bounds, which are still needed to
    /// decide whether a height read off the headset is a real standing
    /// participant or a headset lying on a desk.</para>
    ///
    /// <para>Pure arithmetic, deliberately: it is the one part of the XR rig that
    /// can be tested without a headset.</para>
    /// </summary>
    public static class ParticipantEyeHeight
    {
        /// <summary>
        /// Height the participant's viewpoint is fixed at, in metres: the height
        /// of the SMPL-X agents' own eye bones, so the participant is the third
        /// head on the eye line the two agents share rather than looking slightly
        /// up at both of them.
        ///
        /// <para>Measured off the scene's agents —
        /// <c>left/right_eye_smplhf</c> both sit at world y = 1.6846 m in the rest
        /// pose, with the head bone at 1.6391 m. Driving a 1,200-frame clip
        /// through the motion player puts them at mean 1.6815 m over the range
        /// 1.663-1.713 m, so the rest-pose value is 3 mm off the mocap mean —
        /// well inside the agents' own postural sway. The rig exposes it as a
        /// field in case that ever wants retuning.</para>
        ///
        /// <para>It was 1.60 m until 2026-08-27 — a generic adult eye height,
        /// inherited from where the flat camera happened to sit — which left the
        /// participant 8.5 cm below the agents' eye line.</para>
        /// </summary>
        public const float SmplxEyeHeight = 1.6846f;

        /// <summary>Shortest standing eye height accepted as a real measurement, in metres.</summary>
        public const float MinPlausibleEyeHeight = 1.2f;

        /// <summary>Tallest standing eye height accepted as a real measurement, in metres.</summary>
        public const float MaxPlausibleEyeHeight = 2.1f;

        /// <summary>What one height sample yielded.</summary>
        public readonly struct Result
        {
            public Result(float measuredEyeHeight, bool accepted, string reason)
            {
                MeasuredEyeHeight = measuredEyeHeight;
                Accepted = accepted;
                Reason = reason;
            }

            /// <summary>The sample as taken, in metres, kept whether or not it was accepted.</summary>
            public float MeasuredEyeHeight { get; }

            /// <summary>False when the sample cannot be a standing participant.</summary>
            public bool Accepted { get; }

            /// <summary>Why a sample was rejected; empty when it was accepted.</summary>
            public string Reason { get; }
        }

        /// <summary>
        /// Whether <paramref name="measuredEyeHeight"/> can be a real standing
        /// participant's eyes above the floor.
        /// </summary>
        public static Result Measure(float measuredEyeHeight)
        {
            // Rejected rather than clamped. A clamp would record a height derived
            // from a sample already known to be wrong, and it would look like a
            // real measurement in the participant's record; a rejection keeps the
            // last real one and says why.
            if (float.IsNaN(measuredEyeHeight) || float.IsInfinity(measuredEyeHeight))
                return new Result(measuredEyeHeight, false,
                    "no tracking data yet — the headset reported no pose.");

            if (measuredEyeHeight < MinPlausibleEyeHeight || measuredEyeHeight > MaxPlausibleEyeHeight)
                return new Result(measuredEyeHeight, false,
                    $"{measuredEyeHeight:F2} m is outside the plausible standing range " +
                    $"{MinPlausibleEyeHeight:F2}-{MaxPlausibleEyeHeight:F2} m — is the headset being worn, " +
                    "and is the participant standing upright?");

            return new Result(measuredEyeHeight, true, string.Empty);
        }
    }
}
