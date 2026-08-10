namespace GazeControl.Gaze.Policy
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
    /// End-of-turn event classes, numbered as the corpus writes them in its
    /// <c>eot_type</c> column and as the paper labels its prototype classes
    /// (p1/p2/p3). Every prototype belongs to exactly one.
    /// </summary>
    public enum EotType
    {
        Interruption = 1,
        Overlapping = 2,
        TurnTaking = 3,
    }

    /// <summary>
    /// One pre-turn gaze prototype: per-role tracks of (duration, gaze target)
    /// segments, played inside the 1 s window (60 fps) before an end-of-turn
    /// event.
    ///
    /// The listener track is carried but never rendered in this demo: the
    /// listener is the human user, who has no gaze to drive. It is transcribed
    /// anyway so the prototype is stored whole, and because a prototype can
    /// name the listener as a *target* — that is a different thing, and the
    /// agents do render it.
    /// </summary>
    public sealed class GazePattern
    {
        /// <summary>Figure reference, prototype id and class, for logs and the metadata sidecar.</summary>
        public string Name;

        /// <summary>The raw prototype archive this was transcribed from, e.g. <c>p3_ks30_17</c>.</summary>
        public string PrototypeId;

        /// <summary>The event class this prototype was measured on.</summary>
        public EotType EotType;

        /// <summary>
        /// Where the subsequence starts in the 60-frame window before the turn
        /// event: the rank-0 matched subsequence's <c>subseq_start_idx</c> from
        /// the raw data, in seconds at 60 fps.
        /// </summary>
        public float WindowOffsetSeconds;

        public (float seconds, GazeRole target)[] CurrentSpeakerTrack;
        public (float seconds, GazeRole target)[] NextSpeakerTrack;
        public (float seconds, GazeRole target)[] ListenerTrack;
    }
}
