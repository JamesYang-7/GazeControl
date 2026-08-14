using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>How the proposed condition picks the prototype for a turn boundary.</summary>
    public enum PatternSelection
    {
        /// <summary>
        /// Draw at random from the paper's prototypes for that boundary's own
        /// class — an interruption gets an interruption prototype. The draw is
        /// seeded, so a take is reproducible.
        /// </summary>
        RandomByEventClass,

        /// <summary>Play one named prototype at every boundary, whatever its class.</summary>
        Fixed,
    }

    /// <summary>What drives gaze outside the pre-turn prototype windows.</summary>
    public enum ProposedSubstrate
    {
        /// <summary>
        /// Non-parametric replay of measured holding stretches. Declared first
        /// on purpose: the scene's serialized ProposedSettings predates this
        /// field, and a missing field deserializes to 0, so existing scenes get
        /// the new default without a scene edit.
        /// </summary>
        HoldingReplay,

        /// <summary>Baseline B underneath — the prototype-only ablation.</summary>
        SpeakerFollowing,
    }

    /// <summary>
    /// Which pre-turn prototype the proposed condition plays, and the window it
    /// plays in. Configuration rather than constants so a prototype can be
    /// swapped between takes without touching code.
    /// </summary>
    [Serializable]
    public sealed class ProposedSettings
    {
        [field: SerializeField]
        [field: Tooltip("What drives gaze outside the prototype windows; SpeakerFollowing is the prototype-only ablation")]
        public ProposedSubstrate Substrate { get; set; } = ProposedSubstrate.HoldingReplay;

        [field: SerializeField]
        [field: Tooltip("Draw a prototype per boundary from its own event class, or play one fixed prototype")]
        public PatternSelection Selection { get; set; } = PatternSelection.RandomByEventClass;

        /// <summary>The prototype used when <see cref="Selection"/> is <see cref="PatternSelection.Fixed"/>.</summary>
        [field: SerializeField]
        [field: Tooltip("Prototype played at every boundary when Selection is Fixed")]
        public PreTurnPattern Pattern { get; set; } = PreTurnPattern.Fig7e;

        /// <summary>Length of the pre-turn window the prototypes were measured in.</summary>
        [field: SerializeField]
        [field: Tooltip("Pre-turn window length, seconds; the paper measures 1 s windows at 60 fps")]
        public float PreTurnWindowSeconds { get; set; } = 1f;

        /// <summary>Stretch factor for the prototype; 1 is data-faithful.</summary>
        [field: SerializeField]
        [field: Range(1f, 8f)]
        [field: Tooltip("1 = data-faithful. Raise only to make the fastest segments visible in a demo; a recorded trial should stay at 1.")]
        public float PatternTimeScale { get; set; } = 1f;
    }
}
