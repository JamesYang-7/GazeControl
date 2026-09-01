using System.Collections.Generic;

namespace GazeControl.Study
{
    /// <summary>
    /// Everything <see cref="StudySessionRules"/> needs to decide whether a run
    /// may be shown to a participant, gathered from the scene and from disk by
    /// whoever is asking.
    ///
    /// <para>A plain record rather than the rules reaching into the scene
    /// themselves: the two callers see different amounts of it. The runner knows
    /// its own clip and asks at <c>Awake</c>; the operator's window knows the
    /// whole session and asks in Edit Mode, before anyone has put the headset
    /// on. Splitting it this way is what lets one rule set answer both, so the
    /// window can never report ready for a run the guard will then refuse.</para>
    /// </summary>
    public sealed class StudySessionState
    {
        /// <summary>The human subject's label, as it will be written to every file.</summary>
        public string ParticipantLabel { get; set; }

        /// <summary>Whether a folder for this participant already exists under the recordings root.</summary>
        public bool ParticipantHasRun { get; set; }

        /// <summary>Where that folder is, for the message; may be null.</summary>
        public string ParticipantFolder { get; set; }

        /// <summary>The runner's track mode, named, for the message.</summary>
        public string TrackMode { get; set; }

        /// <summary>Whether that mode is Replay — the only one a participant may see.</summary>
        public bool ReplayingBakedTrack { get; set; }

        /// <summary>
        /// Whether a baked track is actually loaded for the current clip, or null
        /// when the caller cannot know — nothing is loaded in Edit Mode, and the
        /// window answers the stronger question instead: whether all fifteen
        /// takes have a track on disk.
        /// </summary>
        public bool? TrackLoaded { get; set; }

        /// <summary>Where it was expected, for the message; may be null.</summary>
        public string TrackPath { get; set; }

        public bool LoggingEnabled { get; set; }

        public bool ParticipantGazeWired { get; set; }

        public bool ParticipantGazeRigWired { get; set; }

        public bool QuestionnairePresent { get; set; }

        /// <summary>Name of the object carrying a switched-on developer overlay, or null when none is.</summary>
        public string DeveloperOverlayOn { get; set; }

        /// <summary>Whether a session runner exists on an active object at all.</summary>
        public bool SessionRunnerPresent { get; set; }

        /// <summary>Whether that component is enabled, so its key actually runs the session.</summary>
        public bool SessionRunnerEnabled { get; set; }

        /// <summary>Whether the participant rig will bring the headset up when play starts.</summary>
        public bool XrStartsOnPlay { get; set; }

        /// <summary>The XR loader configured for this build target, or null when none is.</summary>
        public string XrLoader { get; set; }

        /// <summary>
        /// Clips whose exported segment is missing, or null when the caller did
        /// not look. Null rather than empty, because "checked and all present" and
        /// "not my business" have to be told apart — the runner sees one clip and
        /// cannot answer for the session's other fourteen.
        /// </summary>
        public IReadOnlyList<string> MissingSegments { get; set; }

        /// <summary>Takes with no baked gaze track, in <c>clip/condition</c> form, or null when unchecked.</summary>
        public IReadOnlyList<string> MissingTracks { get; set; }
    }
}
