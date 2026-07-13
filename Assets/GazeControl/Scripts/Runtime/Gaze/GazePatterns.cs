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
    /// Pre-turn gaze patterns transcribed from the ICMI paper's raw prototype data
    /// (F:\aF\My_Papers\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking\raw_prototypes).
    /// Patterns run inside the 1 s window (60 fps) before a turn boundary.
    /// </summary>
    public static class GazePatterns
    {
        const float Frame = 1f / 60f;

        /// <summary>
        /// Turn-yielding double glance — prototype p3_ks40_4 (Fig. 8d, class turn-taking):
        /// the current speaker's gaze track, verbatim from kernel.npy. Two micro-glances
        /// at the next speaker early in the window, then sustained aversion (None) up to
        /// the turn boundary. Next speaker and listener tracks are None throughout.
        /// </summary>
        public static readonly (float seconds, GazeRole target)[] TurnYieldingDoubleGlance =
        {
            (1 * Frame, GazeRole.None),
            (2 * Frame, GazeRole.NextSpeaker), // glance 1, frames 1-2
            (1 * Frame, GazeRole.None),
            (3 * Frame, GazeRole.NextSpeaker), // glance 2, frames 4-6
            (33 * Frame, GazeRole.None),       // sustained aversion until the boundary
        };

        /// <summary>
        /// Where the 40-frame subsequence sits inside the 60-frame pre-turn window:
        /// median start of the matched subsequences (frames {1, 4, 16, 18} → 10).
        /// </summary>
        public const float TurnYieldingWindowOffsetSeconds = 10 * Frame;
    }
}
