using System;
using GazeControl.Conversation;
using GazeControl.Study;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Stops the scene when a clip ends so the participant can answer, and waits
    /// for the operator to move on.
    ///
    /// <para>The questionnaire is not shown in the headset (user's call,
    /// 2026-08-27, superseding the world-space canvas of
    /// `questionnaire-ui-design.md` §3): the participant watches a clip, the
    /// scene stops, they answer outside VR, and the operator advances. This is
    /// the piece that makes that cycle exist; sequencing the fifteen runs sits on
    /// top of it and is not here yet.</para>
    ///
    /// <para><b>The agents are taken away while they answer</b>, which is a
    /// correctness point rather than presentation. The runner keeps ticking past
    /// the end of the conversation, so without this the participant would rate
    /// "the character seemed aware that I was there" while two agents stood
    /// gazing at them in a state no condition was ever baked to produce — the
    /// answer would be partly about the pause, not the take.</para>
    /// </summary>
    public sealed class ClipPauseController : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("The clip being watched; its finish drives the pause")]
        public RecordedConversation Conversation { get; set; }

        [field: SerializeField]
        [field: Tooltip("Closed when the clip ends, so the take's logs stop with the take")]
        public GazeConditionRunner Runner { get; set; }

        [field: SerializeField]
        [field: Tooltip("Hidden while the participant answers — normally the two agents")]
        public GameObject[] HideWhilePaused { get; set; } = Array.Empty<GameObject>();

        /// <summary>
        /// Hold used when the segment does not name one. It must stay equal to
        /// <c>DemoRecorder.k_DefaultTailSeconds</c>: a live session and a recorded
        /// video of the same clip should end the same way, and the segment files
        /// encode "use the default" as a zero rather than by omitting the field.
        /// </summary>
        const float DefaultHoldSeconds = 1.5f;

        [field: SerializeField]
        [field: Tooltip("Seconds the scene stays up after the voices stop. Negative takes it from the " +
                        "segment's own tailSeconds, which itself falls back to 1.5 s when zero.")]
        public float HoldSeconds { get; set; } = -1f;

        [field: SerializeField]
        [field: Tooltip("Operator key that ends the pause and moves on")]
        public Key AdvanceKey { get; set; } = Key.Space;

        ClipPauseSchedule _schedule;

        /// <summary>Where the current clip is in the play-then-answer cycle.</summary>
        public ClipPhase Phase => _schedule?.Phase ?? ClipPhase.Playing;

        /// <summary>True while the participant should be answering.</summary>
        public bool IsPaused => _schedule != null && _schedule.IsPaused;

        /// <summary>
        /// Raised once, when the clip has ended and the scene has stopped. The
        /// session sequencer will hang off this.
        /// </summary>
        public event Action ClipEnded;

        /// <summary>Raised when the operator advances past the pause.</summary>
        public event Action Advanced;

        void Start()
        {
            // Started here rather than in Awake so the segment is loaded and can
            // be asked for its own tail; RecordedConversation reads it in Awake.
            _schedule = new ClipPauseSchedule(ResolveHold());
        }

        void Update()
        {
            if (_schedule == null || Conversation == null)
                return;

            var before = _schedule.Phase;
            var after = _schedule.Advance(Conversation.HasFinished, Time.deltaTime);

            if (before != ClipPhase.Paused && after == ClipPhase.Paused)
                EnterPause();

            if (!AdvanceKeyPressed())
                return;

            // One key for the whole session: it advances past a pause, and while
            // a clip is running it ends the clip. Pressing it repeatedly walks
            // the fifteen clips as fast as they can be armed.
            if (after == ClipPhase.Paused)
                Advance();
            else
                Skip();
        }

        /// <summary>
        /// End the clip now and go straight to the pause, skipping the rest of
        /// the conversation and its hold.
        ///
        /// <para><b>Refused during a real participant session.</b> The same key
        /// advances and skips, so a mistimed press would otherwise cut a
        /// participant's clip short and leave a take that looks complete but is
        /// not — exactly the kind of quietly-worthless trial
        /// <c>GazeConditionRunner.StudySession</c> exists to prevent. Turn that
        /// switch off to walk a session while testing.</para>
        /// </summary>
        public void Skip()
        {
            if (_schedule == null || _schedule.IsPaused || Conversation == null)
                return;

            // A clip that has not started cannot be skipped: before the first one
            // the session runner owns this key, and between clips there is a
            // short window while the voices are being scheduled.
            if (Conversation.Elapsed < 0f)
                return;

            if (Runner != null && Runner.StudySession)
            {
                Debug.LogWarning(
                    $"{name}: skip refused — this is a study session, and cutting a participant's clip " +
                    "short would leave a take that looks complete but is not.", this);
                return;
            }

            if (!Conversation.SkipToEnd())
                return;

            _schedule.SkipToPause();
            Debug.Log($"{name}: clip skipped at {Conversation.Elapsed:F1} s — not a usable take.", this);
            EnterPause();
        }

        /// <summary>
        /// End the pause and ready the next clip. Public so the operator panel
        /// can drive it without a keypress once that exists.
        /// </summary>
        public void Advance()
        {
            if (_schedule == null || !_schedule.IsPaused)
                return;

            _schedule.Resume();
            SetHidden(false);
            Advanced?.Invoke();
        }

        void EnterPause()
        {
            SetHidden(true);

            // The take is over the moment the scene stops, so its logs close
            // here rather than whenever play mode happens to end. Otherwise every
            // file carries however long the participant took to answer.
            if (Runner != null)
                Runner.EndTake();

            Debug.Log($"{name}: clip ended — scene held, waiting for the operator ({AdvanceKey}).", this);
            ClipEnded?.Invoke();
        }

        void SetHidden(bool hidden)
        {
            for (var i = 0; i < HideWhilePaused.Length; i++)
            {
                if (HideWhilePaused[i] != null)
                    HideWhilePaused[i].SetActive(!hidden);
            }
        }

        /// <summary>
        /// The segment's own <c>tailSeconds</c> unless overridden, with the same
        /// fallback the recorder applies. Sharing the rule means a live session
        /// holds for exactly as long as the recorded video of the same clip does.
        ///
        /// <para>A zero tail means "use the default", <b>not</b> "no hold" — the
        /// scene-1 segments all carry zero, so reading it literally would cut the
        /// agents away on the participant's last syllable.</para>
        /// </summary>
        float ResolveHold()
        {
            if (HoldSeconds >= 0f)
                return HoldSeconds;

            var tail = Conversation != null ? Conversation.HoldSeconds : 0f;
            return tail > 0f ? tail : DefaultHoldSeconds;
        }

        /// <summary>
        /// Read through the Input System's keyboard directly rather than through
        /// an action asset: this is an operator control on the desktop, not a
        /// participant binding, and it must keep working in a scene opened on its
        /// own — the same argument the XR rig's pose actions are built on.
        /// </summary>
        bool AdvanceKeyPressed() =>
            Keyboard.current != null && Keyboard.current[AdvanceKey].wasPressedThisFrame;
    }
}
