using System.Collections.Generic;
using System.IO;
using System.Linq;
using GazeControl.Experiment;
using GazeControl.Gaze.Policy;
using GazeControl.Study;
using GazeControl.Xr;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Study → Start Session: the one gesture that starts a real
    /// participant recording.
    ///
    /// <para><b>What it replaces.</b> Starting a session used to mean setting six
    /// fields by hand across three components — the participant label,
    /// <c>StudySession</c>, <c>Tracks</c>, logging, the session runner's enabled
    /// box and the rig's headset switch — and then pressing Play and hoping. Two
    /// of those had no guard at all, and the guards that did exist fired in
    /// <c>Awake</c>, which is after the participant has put the headset on. The
    /// label was worst: a keyboard shortcut nobody was obliged to press was the
    /// only thing standing between two participants and one filename, and
    /// forgetting it surfaced as an exception during the first questionnaire
    /// screen, a clip too late.</para>
    ///
    /// <para><b>What it does instead.</b> The label is taken from the recordings
    /// folder rather than typed. Every rule the runner will enforce is drawn here,
    /// in Edit Mode, alongside the things only a whole-session view can check —
    /// that all five segments are exported and all fifteen gaze tracks are baked.
    /// Start writes a <see cref="StudyLaunchRequest"/> and enters play; the
    /// session runner applies the settings itself as the scene wakes, so the
    /// committed scene keeps its development defaults and there is nothing to put
    /// back afterwards.</para>
    /// </summary>
    public sealed class StudySessionWindow : EditorWindow
    {
        const string k_SegmentRoot = "Assets/DemoSegments";

        string _label;
        StudySessionState _state;
        IReadOnlyList<StudyCheck> _checks;
        StudySessionRecord _previous;
        string _previousLabel;
        Vector2 _scroll;

        /// <summary>
        /// Study 1's session, kept but off the menu (user's call, 2026-09-09):
        /// its eighteen participants are recorded and its paper written, so the
        /// only thing the entry could still do is open the wrong scene by
        /// mistake. Restore its <c>[MenuItem("GazeControl/Study 1/Start
        /// Session")]</c> if study 1 is ever run again.
        /// </summary>
        public static void OpenForStudy1()
        {
            if (StudyScenes.EnsureOpen(StudyScenes.Study1ScenePath, "Study 1 → Start Session"))
                Open();
        }

        /// <summary>
        /// First in the Study 2 menu and separated from the rest (user's call,
        /// 2026-09-09), because it is the one command a session needs and the
        /// others are all preparation.
        ///
        /// <para><b>It is also what puts Study 2 at the top of the GazeControl
        /// menu</b>, which is the second half of the same call: a submenu takes
        /// its place in the parent from its lowest-priority item, so this 0 is
        /// read twice over — first in the row of the Study 2 menu, then in where
        /// Study 2 itself sits. The rest of the menu is left at Unity's default
        /// 1000 and falls in below, with Help sent to 2000 at the
        /// bottom.</para>
        /// </summary>
        [MenuItem("GazeControl/Study 2/Start Session", priority = 0)]
        public static void OpenForStudy2()
        {
            if (StudyScenes.EnsureOpen(StudyScenes.Study2ScenePath, "Study 2 → Start Session"))
                Open();
        }

        public static void Open()
        {
            var window = GetWindow<StudySessionWindow>("Start Session");
            window.minSize = new Vector2(520f, 460f);
        }

        void OnEnable() => Refresh();

        void OnFocus() => Refresh();

        /// <summary>
        /// Repainted every editor tick so that the live rows — the session's
        /// progress, and whether the tracker can see the wearer's eyes — are worth
        /// looking at during a session rather than only before one.
        /// </summary>
        void OnInspectorUpdate() => Repaint();

        void Refresh()
        {
            var folders = ParticipantFolders();

            // Derived, not remembered: whatever the operator did last time, the
            // answer to "who is next" is what is on disk now.
            _label = ParticipantRoster.NextFree(folders);

            _previousLabel = ParticipantRoster.HighestIn(folders);
            _previous = _previousLabel == null ? null : StudySessionRecord.Load(FolderOf(_previousLabel));

            Rebuild();
        }

        void Rebuild()
        {
            _state = BuildState(_label);
            _checks = StudySessionRules.Evaluate(_state);
        }

        // ---------------------------------------------------------------- state

        static string RecordingsRoot
        {
            get
            {
                var runner = FindFirstObjectByType<GazeConditionRunner>(FindObjectsInactive.Include);
                var folder = runner != null ? runner.OutputDirectory : "Recordings";
                return Path.GetFullPath(Path.Combine(Application.dataPath, "..", folder));
            }
        }

        static IReadOnlyList<string> ParticipantFolders() =>
            Directory.Exists(RecordingsRoot)
                ? Directory.GetDirectories(RecordingsRoot).Select(Path.GetFileName).ToList()
                : new List<string>();

        static string FolderOf(string label) => Path.Combine(RecordingsRoot, label);

        StudySessionState BuildState(string label)
        {
            var runner = FindFirstObjectByType<GazeConditionRunner>(FindObjectsInactive.Include);
            var session = FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var questionnaire = FindFirstObjectByType<QuestionnaireSession>(FindObjectsInactive.Include);
            var rig = runner != null && runner.ParticipantGaze != null ? runner.ParticipantGaze.Rig : null;

            string overlayOn = null;
            foreach (var overlay in FindObjectsByType<DeveloperOverlay>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (overlay.Enabled)
                {
                    overlayOn = overlay.name;
                    break;
                }
            }

            var (missingSegments, missingTracks) = SessionAssets(session);

            return new StudySessionState
            {
                ParticipantLabel = label,
                ParticipantHasRun = Directory.Exists(FolderOf(label)),
                ParticipantFolder = FolderOf(label),
                TrackMode = runner != null ? runner.Tracks.ToString() : "no runner",
                ReplayingBakedTrack = runner != null &&
                                      runner.Tracks == GazeConditionRunner.TrackMode.Replay,

                // Nothing is loaded in Edit Mode. The stronger question — whether
                // every one of the fifteen takes has a track on disk — is asked
                // below instead, and answering it here as well would only report
                // the same thing twice, once misleadingly.
                TrackLoaded = null,
                LoggingEnabled = runner != null && runner.LoggingEnabled,
                ParticipantGazeWired = runner != null && runner.ParticipantGaze != null,
                ParticipantGazeRigWired = rig != null && rig.HeadCamera != null,
                QuestionnairePresent = questionnaire != null,
                DeveloperOverlayOn = overlayOn,
                SessionRunnerPresent = session != null && session.gameObject.activeInHierarchy,
                SessionRunnerEnabled = session != null && session.enabled,
                XrStartsOnPlay = rig != null && rig.StartXrOnPlay,
                XrLoader = XrLoaderStatus.LoaderName,
                MissingSegments = missingSegments,
                MissingTracks = missingTracks,
            };
        }

        /// <summary>
        /// Which of the session's clips have no exported segment, and which of its
        /// fifteen takes have no baked gaze track.
        ///
        /// <para>The whole-session questions, and the reason this window knows more
        /// than the runner's own guard does: the runner is armed one clip at a time
        /// and finds a missing track at clip eleven, mid-session, with nothing to be
        /// done about it. Baking takes a play session per condition, so it has to be
        /// answered before the participant arrives.</para>
        /// </summary>
        static (IReadOnlyList<string> segments, IReadOnlyList<string> tracks) SessionAssets(
            StudySessionRunner session)
        {
            if (session == null || session.Conversations == null)
                return (null, null);

            var conditions = new[]
            {
                GazeConditionRunner.GazeCondition.SpeakerFollowing,
                GazeConditionRunner.GazeCondition.RoleConditioned,
                GazeConditionRunner.GazeCondition.Proposed,
            };

            var segments = new List<string>();
            var tracks = new List<string>();

            for (var i = 0; i < session.Conversations.Length; i++)
            {
                var conversation = session.Conversations[i];
                var directory = $"{k_SegmentRoot}/{conversation}";

                if (!File.Exists($"{directory}/segment.json"))
                {
                    segments.Add(conversation);

                    // No segment means no track directory to look in; reporting
                    // three missing tracks as well would only repeat the same fact.
                    continue;
                }

                var seed = session.Seeds != null && i < session.Seeds.Length ? session.Seeds[i] : 0;
                foreach (var condition in conditions)
                {
                    if (!File.Exists(GazeTrack.PathFor(directory, condition.ToString(), seed)))
                        tracks.Add($"{conversation}/{condition}");
                }
            }

            return (segments, tracks);
        }

        // ------------------------------------------------------------------ GUI

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    Refresh();

                GUILayout.Label(RecordingsRoot, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }

            using var scroll = new EditorGUILayout.ScrollViewScope(_scroll);
            _scroll = scroll.scrollPosition;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                DrawRunning();
                return;
            }

            DrawParticipant();
            EditorGUILayout.Space();
            DrawChecks();
            EditorGUILayout.Space();
            DrawStart();
        }

        void DrawParticipant()
        {
            EditorGUILayout.LabelField("Participant", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(_label, EditorStyles.largeLabel, GUILayout.Width(90f));

                if (GUILayout.Button("◂ Back", GUILayout.Width(70f)))
                {
                    _label = Step(_label, -1);
                    Rebuild();
                }

                if (GUILayout.Button("Next ▸", GUILayout.Width(70f)))
                {
                    _label = ParticipantLabel.Next(_label);
                    Rebuild();
                }

                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.LabelField(
                _previousLabel == null
                    ? "No participant has run yet."
                    : $"Last run: {_previousLabel} — {(_previous == null ? "no session record" : _previous.Progress)}",
                EditorStyles.miniLabel);

            EditorGUILayout.LabelField(
                "Taken from the folders under the recordings root, so it is never typed and never repeats. " +
                "The step buttons are for a correction, not for the normal path.",
                EditorStyles.wordWrappedMiniLabel);

            if (_previous != null && !_previous.IsComplete)
            {
                EditorGUILayout.HelpBox(
                    $"{_previousLabel} did not finish — {_previous.clipsCompleted} of {_previous.clipCount} " +
                    "clips. To re-run that participant, move their folder aside first: a session refuses to " +
                    "reopen one, because appending would mix two runs in files nothing downstream can " +
                    "separate.",
                    MessageType.Warning);
            }
        }

        void DrawChecks()
        {
            EditorGUILayout.LabelField("Preflight", EditorStyles.boldLabel);

            foreach (var check in _checks)
            {
                var mark = check.Ok ? "ok  " : check.SetAtLaunch ? "→   " : "!!  ";
                var suffix = check.Ok || !check.SetAtLaunch ? string.Empty : "  (set at launch)";
                EditorGUILayout.LabelField($"{mark}{check.Name}{suffix}");

                if (!check.Ok && !check.SetAtLaunch)
                {
                    EditorGUILayout.HelpBox(check.Problem, MessageType.Error);
                }
            }

            EditorGUILayout.LabelField(
                "Rows marked → are set for you when the session starts; the scene keeps its development " +
                "defaults on disk. The headset itself — connected, permitted in Varjo Base, calibrated for " +
                "this wearer — cannot be checked from Edit Mode and is reported here once play starts.",
                EditorStyles.wordWrappedMiniLabel);
        }

        void DrawStart()
        {
            var ready = StudySessionRules.ReadyToLaunch(_state);

            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button($"Start session for {_label}", GUILayout.Height(32f)))
                    Launch();
            }

            if (!ready)
            {
                EditorGUILayout.HelpBox(
                    "Fix the rows marked !! above. Every one of them produces a session that looks normal " +
                    "and is not usable.",
                    MessageType.Info);
            }
        }

        void DrawRunning()
        {
            var session = FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var runner = FindFirstObjectByType<GazeConditionRunner>(FindObjectsInactive.Include);

            EditorGUILayout.LabelField("Session running", EditorStyles.boldLabel);

            if (runner != null)
            {
                EditorGUILayout.LabelField(
                    $"Participant {runner.StudyParticipantId}" +
                    (runner.StudySession ? "  ·  study mode" : "  ·  NOT a study session"));
            }

            if (session != null && session.Trials != null)
            {
                // Position only, never the method: the operator reads the items
                // aloud and knows the hypothesis, so naming the condition would
                // tell them which answer is the interesting one.
                var index = session.CurrentIndex;
                EditorGUILayout.LabelField(index < 0
                    ? $"Not started — press {session.AdvanceKey} in the Game view to begin. {session.Trials.Count} clips."
                    : $"Clip {index + 1} of {session.Trials.Count}");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Headset", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(XrLoaderStatus.IsRunning
                ? $"XR running on {XrLoaderStatus.LoaderName}"
                : "XR is not running.");

            if (runner != null && runner.ParticipantGaze != null)
            {
                var reason = runner.ParticipantGaze.UnusableReason();
                if (reason == null)
                    EditorGUILayout.LabelField("Eye tracking is available and calibrated.");
                else
                    EditorGUILayout.HelpBox(reason, MessageType.Warning);
            }
        }

        // -------------------------------------------------------------- actions

        void Launch()
        {
            StudyLaunchRequest.Set(_label);
            Debug.Log(
                $"Start Session: launching {_label}. The session runner will set study mode, replayed " +
                "tracks, logging and the headset as the scene wakes; the scene asset is not modified.");

            EditorApplication.EnterPlaymode();
        }

        /// <summary>
        /// Step the label back, for the case where the operator has stepped past
        /// the one they meant. Refuses to go below the first label rather than
        /// producing P00, which is the debugging namespace.
        /// </summary>
        static string Step(string label, int by)
        {
            var numbers = ParticipantRoster.NumbersIn(new[] { label });
            var number = numbers.Count == 0 ? 1 : numbers[0] + by;
            return number < 1 ? ParticipantLabel.First : ParticipantRoster.LabelFor(number);
        }
    }
}
