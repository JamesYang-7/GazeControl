namespace GazeControl.Xr
{
    /// <summary>
    /// Per-participant eye-height calibration: how far the play area has to move
    /// vertically so that a participant of any height stands eye to eye with the
    /// agents.
    ///
    /// <para>Why normalise at all, when the agents aim at the participant's real
    /// head and so look at them correctly whatever their height: the *stimulus*
    /// geometry would otherwise differ between participants. The agents are
    /// standing mocap with their heads fixed at about 1.64 m, so an unadjusted
    /// 1.50 m participant looks up at them and a 1.90 m one looks down, and how
    /// clearly a gaze shift reads is exactly what the study measures. Normalising
    /// makes height a constant instead of an uncontrolled between-participant
    /// variable (`user-study-design.md` §4).</para>
    ///
    /// <para>Pure arithmetic, deliberately: it is the one part of the XR rig that
    /// can be tested without a headset, and getting it wrong silently ruins every
    /// take a participant sees.</para>
    /// </summary>
    public static class EyeHeightCalibration
    {
        /// <summary>
        /// Eye height every participant is normalised to, in metres. 1.60 m is
        /// where the flat-mode camera has always sat, so a calibrated take and a
        /// desktop preview of the same clip show the same geometry.
        /// </summary>
        public const float DefaultReferenceEyeHeight = 1.6f;

        /// <summary>Shortest standing eye height accepted as a real measurement, in metres.</summary>
        public const float MinPlausibleEyeHeight = 1.2f;

        /// <summary>Tallest standing eye height accepted as a real measurement, in metres.</summary>
        public const float MaxPlausibleEyeHeight = 2.1f;

        /// <summary>What one calibration sample yielded.</summary>
        public readonly struct Result
        {
            public Result(float measuredEyeHeight, float offset, bool accepted, string reason)
            {
                MeasuredEyeHeight = measuredEyeHeight;
                Offset = offset;
                Accepted = accepted;
                Reason = reason;
            }

            /// <summary>The sample as taken, in metres, kept whether or not it was accepted.</summary>
            public float MeasuredEyeHeight { get; }

            /// <summary>Metres to add to the play area's height. Zero when the sample was rejected.</summary>
            public float Offset { get; }

            /// <summary>False when the sample cannot be a standing participant.</summary>
            public bool Accepted { get; }

            /// <summary>Why a sample was rejected; empty when it was accepted.</summary>
            public string Reason { get; }
        }

        /// <summary>
        /// Offset the play area needs so that a participant whose eyes are
        /// <paramref name="measuredEyeHeight"/> above the floor sees the triad
        /// from <paramref name="referenceEyeHeight"/>.
        /// </summary>
        public static Result Measure(float measuredEyeHeight, float referenceEyeHeight)
        {
            // Rejected rather than clamped. A clamp would apply a large offset
            // from a sample already known to be wrong, and the take would look
            // plausible on the recording; a rejection leaves the rig where it was
            // and the operator re-runs the calibration.
            if (float.IsNaN(measuredEyeHeight) || float.IsInfinity(measuredEyeHeight))
                return new Result(measuredEyeHeight, 0f, false,
                    "no tracking data yet — the headset reported no pose.");

            if (measuredEyeHeight < MinPlausibleEyeHeight || measuredEyeHeight > MaxPlausibleEyeHeight)
                return new Result(measuredEyeHeight, 0f, false,
                    $"{measuredEyeHeight:F2} m is outside the plausible standing range " +
                    $"{MinPlausibleEyeHeight:F2}-{MaxPlausibleEyeHeight:F2} m — is the headset being worn, " +
                    "and is the participant standing upright?");

            return new Result(measuredEyeHeight, referenceEyeHeight - measuredEyeHeight, true, string.Empty);
        }
    }
}
