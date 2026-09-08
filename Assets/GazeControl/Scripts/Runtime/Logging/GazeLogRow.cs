namespace GazeControl.Logging
{
    /// <summary>
    /// One per-frame, per-agent gaze log record (baseline spec §6). Field order
    /// here is the CSV column order; changing it changes the analysis contract.
    /// </summary>
    public struct GazeLogRow
    {
        /// <summary>
        /// Conversation time in seconds — the segment's clock, shared with the
        /// participant log's <c>t</c>. Rows written before the clock starts
        /// (the audio lead) carry -1; only a scene with no conversation falls
        /// back to seconds since the session started.
        /// </summary>
        public float Time { get; set; }

        /// <summary>Study participant (the human subject), not a triad member.</summary>
        public string ParticipantId { get; set; }

        public string Condition { get; set; }

        public string AgentId { get; set; }

        public string SelfRole { get; set; }

        public string TurnPhase { get; set; }

        public int CurrentSpeaker { get; set; }

        public int CurrentAddressee { get; set; }

        public string TargetType { get; set; }

        public int TargetId { get; set; }

        public float AversionYaw { get; set; }

        public float AversionPitch { get; set; }

        public bool IsShifting { get; set; }

        /// <summary>
        /// Agent-side only: whether this agent is looking at the human. The human's
        /// own gaze is unobservable without eye tracking, so this is not symmetric
        /// with <see cref="MutualGazeWithOtherAgent"/>.
        /// </summary>
        public bool MutualGazeWithHuman { get; set; }

        /// <summary>Both agents looking at each other, measured geometrically.</summary>
        public bool MutualGazeWithOtherAgent { get; set; }

        /// <summary>Head direction in the agent's body frame, degrees.</summary>
        public float HeadYaw { get; set; }

        public float HeadPitch { get; set; }

        /// <summary>Eye direction in the agent's head frame, degrees.</summary>
        public float EyeYaw { get; set; }

        public float EyePitch { get; set; }

        public int Seed { get; set; }
    }
}
