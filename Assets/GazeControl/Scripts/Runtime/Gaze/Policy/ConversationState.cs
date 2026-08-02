namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Everything a gaze policy is allowed to know at one decision tick, rebuilt
    /// per agent per tick and passed by <c>in</c>.
    ///
    /// The turn fields carry the scripted ground truth (speaker, addressee, phase,
    /// predicted end): Baseline A and the proposed method read them, Baseline B
    /// deliberately ignores them and works only from voice activity.
    /// </summary>
    public struct ConversationState
    {
        /// <summary>Seconds since the conversation started.</summary>
        public float Time { get; set; }

        /// <summary>May be <see cref="ParticipantId.None"/> during a gap.</summary>
        public ParticipantId CurrentSpeaker { get; set; }

        /// <summary>From the dialogue manager; may be <see cref="ParticipantId.None"/>.</summary>
        public ParticipantId CurrentAddressee { get; set; }

        public ParticipantId SelfId { get; set; }

        public ParticipantRole SelfRole { get; set; }

        public TurnPhase TurnPhase { get; set; }

        /// <summary>
        /// Seconds since the most recent turn instant — a speaker taking or
        /// releasing the floor, whichever happened last. Measured from the
        /// instant rather than from the turn's start because that is what the
        /// corpus's "changing" window is centred on, and a gap between turns is
        /// as close to a turn instant as the last second of a turn is.
        /// </summary>
        public float TimeSinceTurnInstant { get; set; }

        /// <summary>
        /// Seconds until the current speaker stops. Negative when unknown. Known in
        /// advance for a TTS agent (clip duration), which is what lets the yielding
        /// gaze start *before* the utterance ends rather than reacting to its end.
        /// </summary>
        public float PredictedTimeToTurnEnd { get; set; }

        /// <summary>
        /// The other two participants. Stored as two fields rather than an array so
        /// that building the state every tick allocates nothing.
        /// </summary>
        public ParticipantId FirstPartner { get; set; }

        public ParticipantId SecondPartner { get; set; }

        /// <summary>Is a partner currently looking at me (measured geometrically, not read from their policy).</summary>
        public bool MutualGazeActive { get; set; }

        public float MutualGazeDuration { get; set; }
    }
}
