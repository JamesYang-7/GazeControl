using System;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Identifies one participant of the triad. The index is written verbatim to
    /// the gaze log, so it must stay stable for the whole session.
    /// </summary>
    public readonly struct ParticipantId : IEquatable<ParticipantId>
    {
        const int NoneIndex = -1;

        /// <summary>"Nobody" — e.g. the current speaker during a silent gap.</summary>
        public static readonly ParticipantId None = new(NoneIndex);

        public int Index { get; }

        public ParticipantId(int index) => Index = index;

        public bool IsValid => Index != NoneIndex;

        public bool Equals(ParticipantId other) => Index == other.Index;

        public override bool Equals(object obj) => obj is ParticipantId other && Equals(other);

        public override int GetHashCode() => Index;

        public override string ToString() => IsValid ? Index.ToString() : "none";

        public static bool operator ==(ParticipantId a, ParticipantId b) => a.Index == b.Index;

        public static bool operator !=(ParticipantId a, ParticipantId b) => a.Index != b.Index;
    }
}
