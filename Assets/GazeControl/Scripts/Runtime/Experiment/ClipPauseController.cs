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
    /// <para>What fills the pause is <see cref="QuestionnaireSession"/>: the
    /// participant reads the items on a world-space canvas and answers aloud,
    /// and the operator enters the answers on the desktop panel
    /// (`questionnaire-ui-design.md` §1, restored by the user 2026-08-27 after a
    /// spell where nothing was shown at all). While a screen is unanswered this
    /// controller yields its key — see <see cref="AdvanceHeld"/> — so the
    /// questionnaire, not the keyboard, is what releases the next clip. With no
    /// questionnaire in the scene the pause still works on its own.</para>
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
        /// While true the advance key does nothing, and the pause can only be
        /// ended in code. <see cref="QuestionnaireSession"/> holds it from the
        /// moment a clip ends until that clip's screen has been answered, so one
        /// operator key cannot walk past an unanswered questionnaire screen.
        /// </summary>
        public bool AdvanceHeld { get; set; }

        /// <summary>
        /// True when the clip now paused on was skipped rather than played out.
        /// The questionnaire refuses to record ratings of it: a rating of half a
        /// clip is worse than a missing one, because nothing downstream can tell
        /// them apart. Cleared when the next clip is released.
        /// </summary>
        public bool WasCutShort { get; private set; }

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

            // The whole key is yielded while held, the skip included: during a
            // held pause the questionnaire panel owns the keyboard, and a stray
            // press must not reach either branch.
            if (AdvanceHeld || !AdvanceKeyPressed())
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

            WasCutShort = true;
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
            WasCutShort = false;
            SetParticipantsHidden(false);
            Advanced?.Invoke();
        }

        void EnterPause()
        {
            SetParticipantsHidden(true);

            // The take is over the moment the scene stops, so its logs close
            // here rather than whenever play mode happens to end. Otherwise every
            // file carries however long the participant took to answer.
            if (Runner != null)
                Runner.EndTake();

            Debug.Log($"{name}: clip ended — scene held, waiting for the operator ({AdvanceKey}).", this);
            ClipEnded?.Invoke();
        }

        /// <summary>
        /// Take the agents away, or bring them back. Public because the
        /// questionnaire shows screens outside a pause too — the framing before
        /// the first clip and the close after the last — and the participant
        /// must not read either of them past two waiting agents.
        /// </summary>
        public void SetParticipantsHidden(bool hidden)
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
