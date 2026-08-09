using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Which pre-turn prototype the proposed condition plays, and the window it
    /// plays in. Configuration rather than constants so a prototype can be swapped
    /// between takes without touching code.
    /// </summary>
    [Serializable]
    public sealed class ProposedSettings
    {
        /// <summary>Which turn-taking prototype from the paper drives the pre-turn window.</summary>
        [field: SerializeField]
        [field: Tooltip("Which turn-taking prototype plays before a turn boundary")]
        public PreTurnPattern Pattern { get; set; } = PreTurnPattern.CheckAvertReengage7e;

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
