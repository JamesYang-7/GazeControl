using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Hysteresis parameters for <see cref="SpeakerFollowingGazePolicy"/> (baseline B, §3).
    /// Exposed as configuration rather than constants so the condition can be
    /// retuned without touching code.
    /// </summary>
    [Serializable]
    public sealed class SpeakerFollowingSettings
    {
        /// <summary>Continuous voicing required before a new speaker is switched to.</summary>
        [field: SerializeField]
        [field: Tooltip("Onset threshold: continuous voicing before a new speaker counts, seconds")]
        public float OnsetSeconds { get; set; } = 0.25f;

        /// <summary>Continuous silence required before a turn counts as ended.</summary>
        [field: SerializeField]
        [field: Tooltip("Offset threshold: silence before a turn counts as ended, seconds")]
        public float OffsetSeconds { get; set; } = 0.5f;

        /// <summary>Utterances with less voiced time than this never trigger a gaze switch (e.g. "mm-hm").</summary>
        [field: SerializeField]
        [field: Tooltip("Backchannel suppression: shorter utterances never trigger a switch, seconds")]
        public float BackchannelSeconds { get; set; } = 0.6f;

        /// <summary>Once switched, the target is held at least this long regardless of voice activity.</summary>
        [field: SerializeField]
        [field: Tooltip("Minimum dwell after a switch, seconds")]
        public float MinimumDwellSeconds { get; set; } = 0.7f;
    }
}
