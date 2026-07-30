using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// What a policy wants the agent to look at. Either a participant, or a
    /// direction away from everyone expressed as a yaw/pitch offset in degrees
    /// relative to the agent's forward direction.
    /// </summary>
    public readonly struct GazeTarget : IEquatable<GazeTarget>
    {
        public GazeTargetType Type { get; }

        /// <summary>Valid only when <see cref="Type"/> is <see cref="GazeTargetType.Person"/>.</summary>
        public ParticipantId Person { get; }

        /// <summary>Yaw/pitch in degrees; valid only when <see cref="Type"/> is <see cref="GazeTargetType.Aversion"/>.</summary>
        public Vector2 AversionOffset { get; }

        GazeTarget(GazeTargetType type, ParticipantId person, Vector2 aversionOffset)
        {
            Type = type;
            Person = person;
            AversionOffset = aversionOffset;
        }

        public static GazeTarget AtPerson(ParticipantId person) =>
            new(GazeTargetType.Person, person, Vector2.zero);

        public static GazeTarget Away(Vector2 yawPitchDegrees) =>
            new(GazeTargetType.Aversion, ParticipantId.None, yawPitchDegrees);

        /// <summary>
        /// Two targets are the same when they would produce no gaze shift. Aversion
        /// offsets compare exactly (they are sampled from a fixed discrete set, so
        /// an approximate comparison would only mask a re-sample bug).
        /// </summary>
        public bool Equals(GazeTarget other) =>
            Type == other.Type && Person == other.Person && AversionOffset == other.AversionOffset;

        public override bool Equals(object obj) => obj is GazeTarget other && Equals(other);

        public override int GetHashCode() =>
            ((int)Type * 397) ^ Person.GetHashCode() ^ AversionOffset.GetHashCode();

        public override string ToString() => Type == GazeTargetType.Person
            ? $"person {Person}"
            : $"aversion ({AversionOffset.x:0.#}, {AversionOffset.y:0.#})";

        public static bool operator ==(GazeTarget a, GazeTarget b) => a.Equals(b);

        public static bool operator !=(GazeTarget a, GazeTarget b) => !a.Equals(b);
    }
}
