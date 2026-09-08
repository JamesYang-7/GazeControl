using System;
using System.Collections.Generic;
using System.IO;
using GazeControl.Conversation;
using GazeControl.Study;
using GazeControl.Xr;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Runs a participant's whole session: the fifteen clips in their scheduled
    /// order, stopping after each one so the questionnaire can be answered, and
    /// moving on when the operator says so.
    ///
    /// <para>The order comes from <see cref="StudySequence"/>, which
    /// counterbalances method order by participant while keeping conversation
    /// order fixed. The schedule is a pure function of the participant label, so
    /// a log that goes missing is still identifiable by the position it occupied
    /// — see that class for the balance the rotation buys.</para>
    ///
    /// <para><b>One scene, no reloads.</b> Each clip re-arms the conversation and
    /// the gaze runner in place rather than reloading the scene, which would tear
    /// the headset down between every clip and drop the participant out of the
    /// experience fifteen times. Both re-arm through the same code a fresh play
    /// takes.</para>
    /// </summary>
    /// <remarks>
    /// Ordered ahead of everything else because its <c>Awake</c> takes the
    /// inspector's leftover clip away from the other components — and Unity gives
    /// no order between two <c>Awake</c> calls, so without this the gaze runner
    /// armed first about half the time and opened a four-frame log named for
    /// whatever segment the scene was last left on.
    /// </remarks>
    [DefaultExecutionOrder(-100)]
    public sealed class StudySessionRunner : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Conversations, in the order their blocks run. Same for every participant.")]
        public string[] Conversations { get; set; } =
            { "study_c1", "study_c2", "study_c3", "study_c4", "study_c5" };

        [field: SerializeField]
        [field: Tooltip("Per-conversation baked-track seed, in the same order as Conversations")]
        public int[] Seeds { get; set; } = { 8, 4, 7, 7, 4 };

        [field: SerializeField]
        [field: Tooltip("Folder holding the exported segments")]
        public string SegmentRoot { get; set; } = "Assets/DemoSegments";

        [field: SerializeField]
        [field: Tooltip("Plays the clips; re-armed for each one")]
        public RecordedConversation Conversation { get; set; }

        [field: SerializeField]
        [field: Tooltip("Drives gaze and writes the logs; re-armed for each clip")]
        public GazeConditionRunner Runner { get; set; }

        [field: SerializeField]
        [field: Tooltip("Stops the scene at the end of each clip so the participant can answer")]
        public ClipPauseController Pause { get; set; }

        [field: SerializeField]
        [field: Tooltip("Start the first clip as soon as play begins; otherwise wait for the advance key")]
        public bool StartOnPlay { get; set; }

        [field: SerializeField]
        [field: Tooltip("Operator key that starts the session and moves to the next clip")]
        public Key AdvanceKey { get; set; } = Key.Space;

        /// <summary>
        /// Methods compared within each conversation. Fixed by the study design
        /// (§1) and read by the questionnaire, which asks for one rank per
        /// version and so has to agree with this exactly.
        /// </summary>
        public const int ConditionCount = 3;

        /// <summary>
        /// While true the start key does nothing. <see cref="QuestionnaireSession"/>
        /// holds it until the framing screen has been read, so the first clip
        /// cannot begin before the participant has been told what to watch for.
        /// </summary>
        public bool StartHeld { get; set; }

        /// <summary>
        /// True when this play is a preview (<see cref="StudyLaunchRequest.SetPreview"/>):
        /// the session's order with the baked tracks replayed, but no
        /// questionnaire, no logs and no pause — a clip's end, or the skip key
        /// during it, goes straight to the next clip. The questionnaire reads
        /// it to stay out of the way; nothing shown in a preview is a take.
        /// </summary>
        public bool Preview { get; private set; }

        IReadOnlyList<StudyTrial> _trials;
        int _scheduleOrdinal;
        int _index = -1;
        bool _busy;
        StudySessionRecord _record;

        /// <summary>
        /// This participant's own folder, once a real session has opened one;
        /// empty on a development run.
        ///
        /// <para>The session owns it rather than each writer computing it, so the
        /// session record and the questionnaire's answers cannot land in two
        /// folders. They would for the debugging label, whose folder carries a
        /// timestamp: two writers asking for it a second apart get two folders.</para>
        /// </summary>
        public string ParticipantDirectory { get; private set; } = string.Empty;

        /// <summary>
        /// This participant's place in the counterbalancing schedule, which
        /// decides the method order of every block. Recorded in the console
        /// report so a session can be reconstructed from its label alone.
        /// </summary>
        public int ScheduleOrdinal => _scheduleOrdinal;

        /// <summary>The running order, built once at Awake.</summary>
        public IReadOnlyList<StudyTrial> Trials => _trials;

        /// <summary>Index of the clip now playing or just finished; -1 before the first.</summary>
        public int CurrentIndex => _index;

        /// <summary>The clip now playing or just finished.</summary>
        public StudyTrial Current => _index >= 0 && _index < _trials.Count ? _trials[_index] : default;

        /// <summary>True once every clip has been shown.</summary>
        public bool IsFinished => _trials != null && _index >= _trials.Count - 1 && Pause != null && Pause.IsPaused;

        void Awake()
        {
#if UNITY_EDITOR
            ApplyLaunchRequest();
#endif

            // Awake runs on a disabled component too, and below this line it
            // takes the conversation's autoplay and the runner's arming away
            // from a plain Play. A bake disables this component precisely so
            // that the clip plays by itself, so a disabled session must leave
            // the scene alone. After the launch request, which is what turns a
            // disabled session on for a participant run.
            if (!enabled)
                return;

            var conditions = new[]
            {
                nameof(GazeConditionRunner.GazeCondition.SpeakerFollowing),
                nameof(GazeConditionRunner.GazeCondition.RoleConditioned),
                nameof(GazeConditionRunner.GazeCondition.Proposed),
            };

            // Safe to read the label here: NextParticipant refuses to step it
            // while playing, so it is fixed for the whole session by the time
            // Awake runs.
            _scheduleOrdinal = ParticipantLabel.ScheduleOrdinal(Runner == null ? null : Runner.StudyParticipantId);
            if (_scheduleOrdinal < 0)
            {
                // A pilot or debug label is outside the schedule. Fall back to
                // ordinal 0 so a development run still has an order to play,
                // rather than refusing here — the study guard is what stops a
                // real session on such a label, and it gives a better message.
                _scheduleOrdinal = 0;
            }

            _trials = StudySequence.Build(Conversations, conditions, _scheduleOrdinal);

            // Suppressed in Awake, which Unity guarantees runs before every
            // Start: otherwise the conversation plays whatever segment the
            // inspector was last left on, before the operator has started
            // anything, and the participant's first clip is not trial 1.
            if (Conversation != null)
                Conversation.AutoplayOnStart = false;

            // Same reason: the runner would otherwise open a log for the
            // inspector's leftover clip before the session's first take.
            if (Runner != null)
                Runner.ArmOnAwake = false;

            if (Seeds == null || Seeds.Length != Conversations.Length)
                Debug.LogError(
                    $"{name}: {Conversations.Length} conversations but " +
                    $"{(Seeds == null ? 0 : Seeds.Length)} seeds — every conversation needs the seed its " +
                    "tracks were baked at, or the wrong take will play.", this);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Turn this play into the session the operator asked for, if they asked
        /// for one from GazeControl → Study → Start Session.
        ///
        /// <para>Everything a participant run needs that is otherwise a field
        /// somebody has to remember: the label, study mode, replayed tracks,
        /// logging, this component, the headset and the developer overlay. Applied
        /// here so they are play-session state and the committed scene keeps its
        /// development defaults — see <see cref="StudyLaunchRequest"/>.</para>
        ///
        /// <para>Awake, not Start, and the whole point of this component's
        /// execution order: the gaze runner reads every one of these in its own
        /// <c>Awake</c>. The rig shares that order and so has no guaranteed
        /// position against this — but it reads <c>StartXrOnPlay</c> in
        /// <c>Start</c>, and every Awake runs before any Start, so the flag is in
        /// place whichever way the two are ordered.</para>
        /// </summary>
        void ApplyLaunchRequest()
        {
            if (StudyLaunchRequest.TryConsumePreview())
            {
                ApplyPreview();
                return;
            }

            if (!StudyLaunchRequest.TryConsume(out var participant))
                return;

            // The scene's committed state has this off — it is how a bake, a demo
            // recording and a preview each say they are not a participant run.
            enabled = true;

            if (Runner != null)
            {
                Runner.StudyParticipantId = participant;
                Runner.StudySession = true;
                Runner.Tracks = GazeConditionRunner.TrackMode.Replay;
                Runner.LoggingEnabled = true;
            }

            var rig = FindAnyObjectByType<XrParticipantRig>(FindObjectsInactive.Include);
            if (rig != null)
                rig.StartXrOnPlay = true;

            foreach (var overlay in FindObjectsByType<DeveloperOverlay>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                overlay.Enabled = false;

            Debug.Log(
                $"{name}: study launch for {participant} — study mode on, baked tracks replayed, logging " +
                "on, headset starting, developer overlay off. These are play-session settings; the scene " +
                "asset is untouched and reverts when play stops.", this);
        }

        /// <summary>
        /// Turn this play into a preview of the bakes: replayed tracks so what
        /// plays is exactly what a participant would see, the session order so
        /// each conversation's three versions come one after another, and no
        /// questionnaire, log or headset. Play-session values, like a launch.
        /// </summary>
        void ApplyPreview()
        {
            enabled = true;
            Preview = true;
            StartOnPlay = true;

            if (Runner != null)
            {
                Runner.StudySession = false;
                Runner.Tracks = GazeConditionRunner.TrackMode.Replay;
                Runner.LoggingEnabled = false;
            }

            // Desktop only. With the Varjo runtime up, Windows routes Unity's
            // output to the headset and the desktop hears nothing, and the frame
            // clock waits on a compositor nobody is wearing (found 2026-09-08:
            // a preview with no audio and a stalled clock, both sources playing).
            var rig = FindAnyObjectByType<XrParticipantRig>(FindObjectsInactive.Include);
            if (rig != null)
                rig.StartXrOnPlay = false;

            foreach (var overlay in FindObjectsByType<DeveloperOverlay>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                overlay.Enabled = true;

            Debug.Log(
                $"{name}: PREVIEW — baked tracks replayed in session order on the desktop, no questionnaire, " +
                $"no logs, no headset. {AdvanceKey} skips to the next clip. Nothing here is a take.", this);
        }
#endif

        void Start()
        {
            Report();
            BeginRecord();

            if (Pause != null)
            {
                Pause.Advanced += Pause_Advanced;
                Pause.ClipEnded += Pause_ClipEnded;
            }

            // The scene's own first clip is whatever the inspector was left on,
            // which is almost never trial 1. Take it over immediately so the
            // session is what runs, not the leftover configuration.
            if (StartOnPlay && !StartHeld)
                _ = BeginNext();
        }

        void OnDestroy()
        {
            if (Pause != null)
            {
                Pause.Advanced -= Pause_Advanced;
                Pause.ClipEnded -= Pause_ClipEnded;
            }
        }

        /// <summary>
        /// Open this participant's folder and write the session record into it,
        /// at the start of the session rather than at its end.
        ///
        /// <para>A session that stopped at clip nine otherwise looks exactly like
        /// one that finished: the take logs are named per conversation, so a
        /// missing one cannot be told from a clip nobody reached. Writing the
        /// record first also puts the folder on disk immediately, which is what
        /// keeps the next participant off this label even if this session ends
        /// before a single question is answered.</para>
        ///
        /// <para>Only for a real session. A development run would otherwise leave
        /// a timestamped folder behind on every press of Play.</para>
        /// </summary>
        void BeginRecord()
        {
            if (Runner == null || !Runner.StudySession)
                return;

            var participant = Runner.StudyParticipantId?.Trim();
            if (string.IsNullOrEmpty(participant))
                return;

            var projectRoot = Path.Combine(Application.dataPath, "..");
            ParticipantDirectory = Path.Combine(
                projectRoot, Runner.OutputDirectory, ParticipantFolder.NameFor(participant, DateTime.Now));

            _record = StudySessionRecord.Begin(
                participant, _scheduleOrdinal, _trials, Application.unityVersion,
                GitHead.Read(projectRoot), DateTime.UtcNow);

            try
            {
                _record.Save(ParticipantDirectory);
                Debug.Log($"{name}: session record opened at {StudySessionRecord.PathIn(ParticipantDirectory)}", this);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Reported, not fatal: the record is provenance, and refusing to
                // run a participant over it would cost more than it saves.
                Debug.LogError($"{name}: could not write the session record — {e.Message}", this);
                _record = null;
            }
        }

        /// <summary>Record how far the session got, as each clip finishes.</summary>
        void Pause_ClipEnded()
        {
            // A preview has nothing to ask, so the pause ends the moment it
            // begins and the next clip is armed; after the last one the scene
            // simply stops. The advance goes through the pause controller so the
            // agents are shown again by the same code that hid them.
            if (Preview)
            {
                if (_index >= _trials.Count - 1)
                    Debug.Log($"{name}: preview complete — all {_trials.Count} clips shown.", this);
                else if (Pause != null)
                    Pause.Advance();

                return;
            }

            if (_record == null)
                return;

            _record.clipsCompleted = _index + 1;

            if (_index >= _trials.Count - 1)
                _record.Complete(DateTime.UtcNow);

            try
            {
                _record.Save(ParticipantDirectory);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Debug.LogError($"{name}: could not update the session record — {e.Message}", this);
                _record = null;
            }
        }

        void Update()
        {
            // Before the first clip the pause controller is not paused yet, so it
            // will not consume the key; this is what starts the session.
            if (_index < 0 && !StartHeld && AdvanceKeyPressed())
                StartSession();
        }

        /// <summary>
        /// Start the session's first clip. Public so an operator panel can drive
        /// it without a keypress; after this the pause controller's advance is
        /// what moves the session on.
        /// </summary>
        public void StartSession()
        {
            if (_index >= 0 || _busy || StartHeld)
                return;

            _ = BeginNext();
        }

        /// <summary>The operator has finished a clip's questionnaire; run the next one.</summary>
        void Pause_Advanced()
        {
            if (_index >= _trials.Count - 1)
            {
                Debug.Log($"{name}: session complete — all {_trials.Count} clips shown.", this);
                return;
            }

            _ = BeginNext();
        }

        /// <summary>
        /// Arm and start the next clip: load its segment, re-arm the gaze side,
        /// then start the voices. See the comment inside for why that order is
        /// forced.
        /// </summary>
        async Awaitable BeginNext()
        {
            if (_busy)
                return;

            _busy = true;

            try
            {
                _index++;
                var trial = _trials[_index];

                if (!TryConditionOf(trial, out var condition))
                    return;

                Debug.Log($"{name}: starting {trial}", this);

                // Load, arm, then play — in that order, and the order is not
                // arbitrary. The runner validates its baked track against the
                // *loaded* segment's name, so the new segment has to be in place
                // before it is armed; and it has to be armed before the
                // conversation's clock starts, because that clock is what every
                // decision grid is anchored on. Doing the load and the play in one
                // call cannot satisfy both, which the track guard caught by
                // refusing study_c1's track against a still-loaded study_c5.
                var path = $"{SegmentRoot}/{trial.Conversation}/segment.json";

                if (!Conversation.LoadSegment(path))
                {
                    Debug.LogError($"{name}: could not load '{path}'; the session is stalled here.", this);
                    return;
                }

                Runner.BeginTake(condition, SeedOf(trial), trial, _scheduleOrdinal);

                if (!await Conversation.PlayLoadedAsync(destroyCancellationToken))
                    Debug.LogError($"{name}: could not start '{path}'; the session is stalled here.", this);
            }
            finally
            {
                _busy = false;
            }
        }

        int SeedOf(StudyTrial trial)
        {
            for (var i = 0; i < Conversations.Length; i++)
            {
                if (Conversations[i] == trial.Conversation && Seeds != null && i < Seeds.Length)
                    return Seeds[i];
            }

            Debug.LogError($"{name}: no seed for '{trial.Conversation}'; falling back to the runner's.", this);
            return Runner.BaseSeed;
        }

        static bool TryConditionOf(StudyTrial trial, out GazeConditionRunner.GazeCondition condition) =>
            System.Enum.TryParse(trial.Condition, out condition);

        /// <summary>
        /// Print the whole running order once, so the operator's console is the
        /// record of what this participant was shown and in what order — the
        /// thing a missing log is reconstructed against.
        /// </summary>
        void Report()
        {
            var participant = Runner == null ? "(no runner)" : Runner.StudyParticipantId;
            var report = new System.Text.StringBuilder();
            report.AppendLine(
                $"{name}: session order for {participant} — {_trials.Count} clips, " +
                $"counterbalancing schedule ordinal {_scheduleOrdinal}");
            for (var i = 0; i < _trials.Count; i++)
                report.AppendLine("  " + _trials[i]);

            report.Append($"Press {AdvanceKey} to start, and again after each clip.");
            Debug.Log(report.ToString(), this);
        }

        bool AdvanceKeyPressed() =>
            Keyboard.current != null && Keyboard.current[AdvanceKey].wasPressedThisFrame;
    }
}
