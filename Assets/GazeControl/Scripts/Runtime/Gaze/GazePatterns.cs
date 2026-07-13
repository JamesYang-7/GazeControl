namespace GazeControl.Gaze
{
    /// <summary>Gaze target categories used by the collected patterns (conversational roles).</summary>
    public enum GazeRole
    {
        None,
        CurrentSpeaker,
        NextSpeaker,
        Listener,
    }

    /// <summary>
    /// One pre-turn gaze prototype: per-role tracks of (duration, gaze target)
    /// segments, played inside the 1 s window (60 fps) before a turn boundary.
    /// The listener track is omitted — the listener is the human user.
    /// </summary>
    public sealed class GazePattern
    {
        public string Name;
        /// <summary>
        /// Where the subsequence starts in the 60-frame window before end-of-turn
        /// (the moment the speaker stops talking): the rank-0 matched subsequence's
        /// subseq_start_idx from the raw data, in seconds at 60 fps.
        /// </summary>
        public float WindowOffsetSeconds;
        public (float seconds, GazeRole target)[] CurrentSpeakerTrack;
        public (float seconds, GazeRole target)[] NextSpeakerTrack;
    }

    /// <summary>
    /// Pre-turn gaze patterns transcribed verbatim from the ICMI paper's raw
    /// prototype data (F:\aF\My_Papers\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking\raw_prototypes;
    /// figure↔file mapping via the tex labels, e.g. Fig. 8d → p3_ks40_4.npz).
    /// </summary>
    public static class GazePatterns
    {
        const float Frame = 1f / 60f;

        /// <summary>
        /// Fig. 8d (p3_ks40_4, turn-taking): the current speaker micro-glances at the
        /// next speaker twice early in the window, then sustains aversion up to the
        /// boundary. Other roles gaze None throughout.
        /// Rank-0 matched subsequence starts at window frame 4 (subseq_start_idx).
        /// </summary>
        public static readonly GazePattern TurnYieldingDoubleGlance = new()
        {
            Name = "Fig. 8d double glance (p3_ks40_4)",
            WindowOffsetSeconds = 4 * Frame,
            CurrentSpeakerTrack = new[]
            {
                (1 * Frame, GazeRole.None),
                (2 * Frame, GazeRole.NextSpeaker), // glance 1
                (1 * Frame, GazeRole.None),
                (3 * Frame, GazeRole.NextSpeaker), // glance 2
                (33 * Frame, GazeRole.None),       // sustained aversion to the boundary
            },
            NextSpeakerTrack = new[]
            {
                (40 * Frame, GazeRole.None),
            },
        };

        /// <summary>
        /// Fig. 7e (p3_ks30_17, turn-taking): the current speaker checks the next
        /// speaker, averts while finishing the utterance, then re-engages them to hand
        /// over the floor; the next speaker watches the current speaker and averts as
        /// they take the turn (turn onset with averted gaze).
        /// Rank-0 matched subsequence starts at window frame 29 (subseq_start_idx),
        /// so the pattern runs to one frame before end-of-turn: the current speaker's
        /// re-engage lands exactly at the hand-over.
        /// </summary>
        public static readonly GazePattern CheckAvertReengage = new()
        {
            Name = "Fig. 7e check-avert-reengage (p3_ks30_17)",
            WindowOffsetSeconds = 29 * Frame,
            CurrentSpeakerTrack = new[]
            {
                (5 * Frame, GazeRole.NextSpeaker),  // check, frames 0-4
                (17 * Frame, GazeRole.None),        // avert, frames 5-21
                (8 * Frame, GazeRole.NextSpeaker),  // re-engage into the boundary, frames 22-29
            },
            NextSpeakerTrack = new[]
            {
                (20 * Frame, GazeRole.CurrentSpeaker), // watch the speaker, frames 0-19
                (10 * Frame, GazeRole.None),           // avert going into their own turn, frames 20-29
            },
        };
    }
}
