using UnityEngine;

namespace GazeControl.Xr
{
    /// <summary>
    /// One decision-tick record of where the human participant was looking
    /// (<c>user-study-design.md</c> §7). Field order here is the CSV column
    /// order; changing it changes the analysis contract.
    ///
    /// <para>This is a separate file from the twenty-column agent log rather
    /// than more columns on it, because the two are on different grids: the
    /// agent log records what was <i>rendered</i>, once per frame, and this
    /// records what was <i>decided against</i>, once per decision tick. Merging
    /// them would force one of the two off its own clock.</para>
    /// </summary>
    public struct ParticipantGazeRow
    {
        /// <summary>Conversation time of this tick — the same clock the agent tracks are stamped in.</summary>
        public float Time { get; set; }

        /// <summary>Index on the decision grid, so <c>Time == Tick / DecisionHz</c> exactly.</summary>
        public int Tick { get; set; }

        public string ParticipantId { get; set; }

        public string Condition { get; set; }

        /// <summary>The take this row belongs to, so a log found alone still names its recording.</summary>
        public string Case { get; set; }

        public ParticipantGazeStatus TrackerStatus { get; set; }

        public ParticipantEyeStatus LeftEyeStatus { get; set; }

        public ParticipantEyeStatus RightEyeStatus { get; set; }

        public bool Calibrated { get; set; }

        /// <summary>The tracker's frame counter; unchanged between rows means the sample repeated.</summary>
        public long DeviceFrame { get; set; }

        public long CaptureTimeNanoseconds { get; set; }

        /// <summary>Head position in world space, metres.</summary>
        public Vector3 HeadPosition { get; set; }

        /// <summary>Head orientation in world space, degrees, signed to (-180, 180].</summary>
        public float HeadYaw { get; set; }

        public float HeadPitch { get; set; }

        public float HeadRoll { get; set; }

        /// <summary>
        /// Combined gaze ray, world space. The tracker reports it head-relative;
        /// it is converted once here so the analysis never has to reconstruct the
        /// head pose to find out what was being looked at. The head pose above
        /// makes the head-relative form recoverable.
        /// </summary>
        public Vector3 GazeOrigin { get; set; }

        public Vector3 GazeDirection { get; set; }

        public Vector3 LeftGazeOrigin { get; set; }

        public Vector3 LeftGazeDirection { get; set; }

        public Vector3 RightGazeOrigin { get; set; }

        public Vector3 RightGazeDirection { get; set; }

        public float FocusDistance { get; set; }

        public float FocusStability { get; set; }

        public float InterPupillaryDistanceMm { get; set; }

        public float LeftPupilDiameterMm { get; set; }

        public float RightPupilDiameterMm { get; set; }

        public float LeftEyeOpenness { get; set; }

        public float RightEyeOpenness { get; set; }

        /// <summary><c>person</c>, <c>elsewhere</c>, or <c>invalid</c> when the tracker had no gaze.</summary>
        public string TargetType { get; set; }

        /// <summary>Participant id looked at, or the nearest one on a miss; -1 when neither applies.</summary>
        public int TargetId { get; set; }

        /// <summary>Angle from the gaze ray to <see cref="TargetId"/>'s head centre, degrees.</summary>
        public float TargetAngleDegrees { get; set; }

        /// <summary>Distance from the participant's eyes to that head centre, metres.</summary>
        public float TargetDistance { get; set; }

        /// <summary>
        /// The participant is looking at an agent that is looking back. This is
        /// the column <c>baseline-spec.md</c> §6 reserved for
        /// <c>mutual_gaze_with_human</c> and could never fill, now that the human
        /// half is observable.
        /// </summary>
        public bool MutualGaze { get; set; }

        /// <summary>
        /// Bit set over agent participant ids (<c>1 &lt;&lt; id</c>) of the agents
        /// currently looking at the participant, whether or not they are looked
        /// back at. Gaze-return latency (§7.4) is measured from this column's
        /// rising edges, so it needs no join against the agent log.
        /// </summary>
        public int AgentsLookingAtUser { get; set; }
    }
}
