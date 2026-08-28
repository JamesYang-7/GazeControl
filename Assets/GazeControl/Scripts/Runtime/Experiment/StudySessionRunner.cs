using System.Collections.Generic;
using GazeControl.Conversation;
using GazeControl.Study;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Runs a participant's whole session: the fifteen clips in their fixed
    /// order, stopping after each one so the questionnaire can be answered, and
    /// moving on when the operator says so.
    ///
    /// <para>The order comes from <see cref="StudySequence"/> and is the same for
    /// every participant, so a log that goes missing is still identifiable by the
    /// position it occupied. Method order varies between blocks, balanced so no
    /// method is systematically first — see that class for what that buys and
    /// what it gives up.</para>
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

        IReadOnlyList<StudyTrial> _trials;
        int _index = -1;
        bool _busy;

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
            var conditions = new[]
            {
                nameof(GazeConditionRunner.GazeCondition.SpeakerFollowing),
                nameof(GazeConditionRunner.GazeCondition.RoleConditioned),
                nameof(GazeConditionRunner.GazeCondition.Proposed),
            };

            _trials = StudySequence.Build(Conversations, conditions);

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

        void Start()
        {
            Report();

            if (Pause != null)
                Pause.Advanced += Pause_Advanced;

            // The scene's own first clip is whatever the inspector was left on,
            // which is almost never trial 1. Take it over immediately so the
            // session is what runs, not the leftover configuration.
            if (StartOnPlay && !StartHeld)
                _ = BeginNext();
        }

        void OnDestroy()
        {
            if (Pause != null)
                Pause.Advanced -= Pause_Advanced;
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

                Runner.BeginTake(condition, SeedOf(trial), trial.Conversation);

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
            var report = new System.Text.StringBuilder();
            report.AppendLine($"{name}: session order ({_trials.Count} clips, identical for every participant)");
            for (var i = 0; i < _trials.Count; i++)
                report.AppendLine("  " + _trials[i]);

            report.Append($"Press {AdvanceKey} to start, and again after each clip.");
            Debug.Log(report.ToString(), this);
        }

        bool AdvanceKeyPressed() =>
            Keyboard.current != null && Keyboard.current[AdvanceKey].wasPressedThisFrame;
    }
}
