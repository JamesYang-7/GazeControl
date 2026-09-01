using System.Collections.Generic;

namespace GazeControl.Study
{
    /// <summary>
    /// Every rule a run has to satisfy before a participant may see it, in one
    /// place, evaluated over a <see cref="StudySessionState"/>.
    ///
    /// <para>One rule set with two readers. <c>GazeConditionRunner</c> takes the
    /// first failure and refuses to start; the operator's Start Session window
    /// draws all of them in Edit Mode, before the headset goes on. They have to
    /// be the same rules, or the window reports ready and <c>Awake</c> then
    /// refuses — with the participant already wearing the headset, which is
    /// exactly the moment this exists to avoid.</para>
    ///
    /// <para>Each of these silently produces a normal-looking session that is
    /// quietly worthless: a live take is a different stimulus from every other
    /// participant's, a missing log means the trial was not recorded, a reused
    /// label means two people's data under one name, and the developer overlay
    /// names the very boundaries the questionnaire asks people to judge.</para>
    /// </summary>
    public static class StudySessionRules
    {
        /// <summary>
        /// Every rule, in the order the operator should read them: who the run is
        /// for, then whether it is a session at all, then what it will record,
        /// then the hardware, then the assets it replays.
        /// </summary>
        public static IReadOnlyList<StudyCheck> Evaluate(StudySessionState state)
        {
            var checks = new List<StudyCheck>();
            var label = state.ParticipantLabel?.Trim();

            checks.Add(new StudyCheck("Participant label",
                !string.IsNullOrEmpty(label) && !ParticipantLabel.IsDebugLabel(label),
                string.IsNullOrEmpty(label)
                    ? "the participant label is empty, so this participant's takes would be unattributable."
                    : $"the participant label is still {ParticipantLabel.DebugLabel}, which is reserved for " +
                      "debugging. Start the session from GazeControl → Study → Start Session, which takes " +
                      "the next free label from the recordings folder."));

            checks.Add(new StudyCheck("Label is free",
                !state.ParticipantHasRun,
                $"{label} has already run — {state.ParticipantFolder ?? "their folder"} exists. Two " +
                "participants under one label cannot be told apart afterwards, and the collision would " +
                "otherwise surface only when this one's first questionnaire screen was committed."));

            checks.Add(new StudyCheck("Session runner in scene",
                state.SessionRunnerPresent,
                "there is no StudySessionRunner on an active object, so this would show one clip and stop " +
                "— a session that looks complete after a fifteenth of it."));

            checks.Add(new StudyCheck("Session runner enabled",
                state.SessionRunnerEnabled,
                "the StudySessionRunner is disabled, so the start key would play the inspector's leftover " +
                "clip instead of the participant's first trial.",
                setAtLaunch: true));

            checks.Add(new StudyCheck("Questionnaire in scene",
                state.QuestionnairePresent,
                "there is no QuestionnaireSession in the scene, so this participant would watch all " +
                "fifteen clips and never be asked a question. Run GazeControl → Set Up Questionnaire."));

            checks.Add(new StudyCheck("Replaying baked gaze",
                state.ReplayingBakedTrack,
                $"Tracks is {state.TrackMode}, so gaze would be decided live and this participant would " +
                "see a different take from everyone else.",
                setAtLaunch: true));

            if (state.TrackLoaded.HasValue)
            {
                checks.Add(new StudyCheck("Track loaded",
                    state.TrackLoaded.Value,
                    $"no baked track is loaded — expected one at {state.TrackPath}."));
            }

            checks.Add(new StudyCheck("Logging on",
                state.LoggingEnabled,
                "logging is off, so the trial would leave no record.",
                setAtLaunch: true));

            checks.Add(new StudyCheck("Participant gaze logger",
                state.ParticipantGazeWired,
                "no ParticipantGazeLogger is wired, so the study's objective gaze measures would not be " +
                "recorded for this participant."));

            checks.Add(new StudyCheck("Rig and head camera",
                state.ParticipantGazeRigWired,
                "the participant gaze logger has no rig or head camera, so it would record neither head " +
                "pose nor gaze."));

            checks.Add(new StudyCheck("XR loader configured",
                !string.IsNullOrEmpty(state.XrLoader),
                "no XR loader is configured for this build target, so the headset cannot come up. " +
                "Check Project Settings → XR Plug-in Management."));

            checks.Add(new StudyCheck("Headset starts on play",
                state.XrStartsOnPlay,
                "the participant rig will not start the headset (XrParticipantRig.StartXrOnPlay is off), " +
                "so the session would run on the desktop and every participant-gaze row would say the " +
                "tracker was unavailable.",
                setAtLaunch: true));

            checks.Add(new StudyCheck("Developer overlay off",
                state.DeveloperOverlayOn == null,
                $"the developer overlay on '{state.DeveloperOverlayOn}' is on, and it names the " +
                "end-of-turn boundaries the questionnaire asks about.",
                setAtLaunch: true));

            // Null means the caller did not look — the runner sees one clip and
            // cannot answer for the session's other fourteen — so the row is
            // omitted rather than reported as passing.
            if (state.MissingSegments != null)
            {
                checks.Add(new StudyCheck("Segments exported",
                    state.MissingSegments.Count == 0,
                    $"{state.MissingSegments.Count} of the session's clips have no exported segment: " +
                    $"{string.Join(", ", state.MissingSegments)}. The session would stall at the first one."));
            }

            if (state.MissingTracks != null)
            {
                checks.Add(new StudyCheck("Tracks baked",
                    state.MissingTracks.Count == 0,
                    $"{state.MissingTracks.Count} of the session's takes have no baked gaze track: " +
                    $"{string.Join(", ", state.MissingTracks)}. Bake them from the Clip Browser before " +
                    "the participant arrives."));
            }

            return checks;
        }

        /// <summary>
        /// Why this run must not be shown to a participant, or null when it may.
        /// The first failing rule's sentence, in the order above.
        /// </summary>
        public static string FirstProblem(StudySessionState state)
        {
            foreach (var check in Evaluate(state))
            {
                if (!check.Ok)
                    return check.Problem;
            }

            return null;
        }

        /// <summary>
        /// Whether the operator's window may launch: every rule holds except the
        /// ones the launch itself puts right.
        /// </summary>
        public static bool ReadyToLaunch(StudySessionState state)
        {
            foreach (var check in Evaluate(state))
            {
                if (!check.Ok && !check.SetAtLaunch)
                    return false;
            }

            return true;
        }
    }
}
