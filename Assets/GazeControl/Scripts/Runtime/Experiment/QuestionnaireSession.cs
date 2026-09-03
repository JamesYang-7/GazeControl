using System;
using System.IO;
using GazeControl.Study;
using UnityEngine;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Runs the questionnaire alongside the clips: shows the participant the
    /// screen they are being asked about, takes the operator's entry of what
    /// they answered aloud, and writes it as it is entered.
    ///
    /// <para><b>Two surfaces, one state machine</b>
    /// (<c>questionnaire-ui-design.md</c> §1). The participant sees
    /// <see cref="QuestionnaireDisplay"/>, a world-space canvas with no input at
    /// all; the operator sees <see cref="QuestionnaireOperatorPanel"/>, an IMGUI
    /// panel that Unity draws into the desktop window and not into the eye
    /// textures. This component owns the state both of them render, so the two
    /// cannot disagree about which screen is up.</para>
    ///
    /// <para><b>It sequences screens, not clips.</b>
    /// <see cref="StudySessionRunner"/> owns the fifteen takes and their order;
    /// this holds that runner between clips and releases it when the screen for
    /// the clip just watched has been answered. The seam is deliberately that
    /// narrow: the questionnaire never chooses a conversation, a condition or a
    /// seed, and the runner never knows what was asked.</para>
    ///
    /// <para><b>It runs only when the session runner does.</b> A bake, a demo
    /// recording and a plain preview all press Play in this same scene, and none
    /// of them is a participant — so with the session runner off, this writes
    /// nothing, shows nothing and holds nothing.</para>
    /// </summary>
    public sealed class QuestionnaireSession : MonoBehaviour
    {
        /// <summary>Where the run is: which surface is showing what, and what the operator may type.</summary>
        public enum Phase
        {
            /// <summary>Not running — the session runner is off, or this is a preview.</summary>
            Inactive,

            /// <summary>The framing passage, before the first clip.</summary>
            Framing,

            /// <summary>
            /// The four rating items, shown once before the first clip so the
            /// participant knows what follows every version. Nothing is entered
            /// on it; one press of the operator's key moves it on.
            /// </summary>
            RatingPreview,

            /// <summary>
            /// The two short-answer questions, shown once after
            /// <see cref="RatingPreview"/> so the participant knows what a group
            /// ends with. Nothing is entered on it either.
            /// </summary>
            ShortAnswerPreview,

            /// <summary>A clip is playing; neither surface asks for anything.</summary>
            Playing,

            /// <summary>The four Likert items for the clip just watched.</summary>
            Rating,

            /// <summary>The block's ranking of its three versions.</summary>
            Ranking,

            /// <summary>The block's free-text probe.</summary>
            Comment,

            /// <summary>The closing passage, after the last block.</summary>
            Closing,

            /// <summary>Every screen answered.</summary>
            Done,

            /// <summary>Refused to run; <see cref="Notice"/> says why, and the session stays held.</summary>
            Refused,
        }

        [field: SerializeField]
        [field: Tooltip("Sequences the clips; held between them until the screen is answered")]
        public StudySessionRunner Session { get; set; }

        [field: SerializeField]
        [field: Tooltip("Stops the scene at the end of each clip; its pause is when a screen is shown")]
        public ClipPauseController Pause { get; set; }

        [field: SerializeField]
        [field: Tooltip("Supplies the participant label and the output root; never read for the condition")]
        public GazeConditionRunner Runner { get; set; }

        [field: SerializeField]
        [field: Tooltip("The participant's world-space canvas. Optional: without one the operator reads the items aloud unaided.")]
        public QuestionnaireDisplay Display { get; set; }

        [field: SerializeField]
        [field: Tooltip("Response folder root, relative to the project root. One folder per participant inside it.")]
        public string OutputDirectory { get; set; } = "Recordings";

        QuestionnaireScript _script;
        QuestionnaireResponseWriter _writer;
        StudyTrial _trial;
        int _screenIndex;
        bool _clipWasCutShort;

        /// <summary>Where the run is.</summary>
        public Phase CurrentPhase { get; private set; } = Phase.Inactive;

        /// <summary>The instrument being administered; null before it loads.</summary>
        public QuestionnaireDefinition Definition => _script?.Definition;

        /// <summary>The screen now showing.</summary>
        public QuestionnaireScreen Screen =>
            _script != null && _screenIndex < _script.Screens.Count
                ? _script.Screens[_screenIndex]
                : default;

        /// <summary>Screens in the whole run, for "screen 7 of 24".</summary>
        public int ScreenCount => _script?.Screens.Count ?? 0;

        /// <summary>Groups (blocks) in the whole run.</summary>
        public int GroupCount => _script?.BlockCount ?? 0;

        /// <summary>
        /// The 1-based group the participant is about to watch, or 0 when no
        /// group follows this screen. The short-answer page shows it, and it is
        /// their only sense of where they are in a session of five; the last
        /// group is followed by the closing passage, so it says nothing there.
        /// </summary>
        public int NextGroupNumber => CurrentPhase switch
        {
            Phase.ShortAnswerPreview => 1,
            Phase.Ranking or Phase.Comment => Screen.BlockNumber < GroupCount ? Screen.BlockNumber + 1 : 0,
            _ => 0,
        };

        /// <summary>What the operator is typing into, or null on a screen with no answer.</summary>
        public QuestionnaireEntry Entry { get; private set; }

        /// <summary>The block's free-text answer, as typed. Empty is a legitimate answer — the probe is optional.</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>Last refusal or note for the operator; never shown to the participant.</summary>
        public string Notice { get; private set; } = string.Empty;

        /// <summary>
        /// True when the clip just watched was cut short, so its ratings must not
        /// be recorded (<c>questionnaire-ui-design.md</c> §7): a rating of half a
        /// clip is worse than a missing one, because nothing downstream can tell
        /// them apart.
        /// </summary>
        public bool ClipWasCutShort => _clipWasCutShort;

        /// <summary>Where this participant's answers are being written; null before the run starts.</summary>
        public string ResponsesPath => _writer?.ResponsesPath;

        void Awake()
        {
            // In Awake, because StudySessionRunner reads the hold in its own
            // Start: Unity runs every Awake before any Start, and the first clip
            // must not begin before the framing screen has been dismissed.
            if (!IsRunnable())
                return;

            Session.StartHeld = true;
        }

        void Start()
        {
            if (!IsRunnable())
            {
                CurrentPhase = Phase.Inactive;
                return;
            }

            Pause.ClipEnded += Pause_ClipEnded;

            if (!TryOpen())
                return;

            _screenIndex = 0;
            CurrentPhase = Phase.Framing;
            Pause.SetParticipantsHidden(true);
            Show();

            Debug.Log(
                $"{name}: questionnaire ready — {ScreenCount} screens, writing to {_writer.ResponsesPath}. " +
                "The operator panel takes every answer; the participant's canvas takes none.", this);
        }

        void OnDestroy()
        {
            if (Pause != null)
                Pause.ClipEnded -= Pause_ClipEnded;

            // Answers are flushed as they are entered, so this only closes the
            // handles — an interrupted session keeps everything already answered.
            _writer?.Dispose();
            _writer = null;
        }

        /// <summary>Type one number into the screen's current slot.</summary>
        public void Enter(int value)
        {
            if (Entry == null)
                return;

            if (!Entry.TryEnter(value, out var refusal))
            {
                Notice = refusal;
                return;
            }

            Notice = string.Empty;
            Show();
        }

        /// <summary>Undo the last number typed, so it can be retyped before the screen is committed.</summary>
        public void Back()
        {
            if (Entry == null)
                return;

            Entry.Back();
            Notice = string.Empty;
            Show();
        }

        /// <summary>Move the entry cursor, to correct one answer out of several.</summary>
        public void MoveTo(int slot)
        {
            if (Entry == null || slot < 0 || slot > Entry.SlotCount)
                return;

            Entry.MoveTo(slot);
            Show();
        }

        /// <summary>
        /// The operator says this screen is done. Writes whatever the screen
        /// asked for and moves the run on — which, for a rating screen, is what
        /// releases the next clip.
        /// </summary>
        public void Commit()
        {
            switch (CurrentPhase)
            {
                case Phase.Framing:
                    CommitFraming();
                    break;

                case Phase.RatingPreview:
                    CommitRatingPreview();
                    break;

                case Phase.ShortAnswerPreview:
                    CommitShortAnswerPreview();
                    break;

                case Phase.Rating:
                    CommitRating();
                    break;

                case Phase.Ranking:
                    CommitRanking();
                    break;

                case Phase.Comment:
                    CommitComment();
                    break;

                case Phase.Closing:
                    CommitClosing();
                    break;
            }
        }

        void CommitFraming()
        {
            _screenIndex++;
            CurrentPhase = Phase.RatingPreview;
            Show();
        }

        /// <summary>
        /// The rating items have been read; the short-answer questions follow.
        /// Both previews record nothing — they are the pages the participant will
        /// answer later, shown once so they know what they are watching for.
        /// </summary>
        void CommitRatingPreview()
        {
            _screenIndex++;
            CurrentPhase = Phase.ShortAnswerPreview;
            Show();
        }

        /// <summary>
        /// Both previews are read and the first clip may start, so committing
        /// this one is the release the framing screen used to do.
        /// </summary>
        void CommitShortAnswerPreview()
        {
            CurrentPhase = Phase.Playing;
            _screenIndex++;
            Pause.SetParticipantsHidden(false);
            Show();

            Session.StartHeld = false;
            Session.StartSession();
        }

        void CommitRating()
        {
            if (!_clipWasCutShort && !Entry.IsComplete)
            {
                Notice = "every item needs an answer before the clip can move on.";
                return;
            }

            if (_clipWasCutShort)
            {
                Debug.LogWarning(
                    $"{name}: block {Screen.BlockNumber} version {Screen.VersionPosition} was cut short, " +
                    "so nothing was recorded for it.", this);
            }
            else if (!TryWrite(() =>
            {
                var items = Definition.perClipItems;
                for (var i = 0; i < items.Length; i++)
                {
                    _writer.AppendRating(
                        _trial.BlockNumber, _trial.Conversation, _trial.VersionPosition, _trial.Condition,
                        items[i].code, Entry[i]);
                }
            }))
            {
                return;
            }

            var wasLastOfBlock = _trial.IsLastOfBlock;
            _screenIndex++;

            if (wasLastOfBlock)
            {
                BeginRanking();
                return;
            }

            Release();
        }

        void CommitRanking()
        {
            if (!Entry.IsComplete)
            {
                Notice = "every version needs a rank, and no two may be the same.";
                return;
            }

            var block = Screen.BlockNumber;
            if (!TryWrite(() =>
            {
                for (var position = 1; position <= _script.VersionsPerBlock; position++)
                {
                    var trial = TrialOf(block, position);
                    _writer.AppendRank(block, trial.Conversation, position, trial.Condition, Entry[position - 1]);
                }
            }))
            {
                return;
            }

            // The comment shares the ranking screen rather than getting its own
            // (§3): it is asked about the same three versions, and a screen the
            // participant can skip is not worth a page turn.
            Entry = null;
            Comment = string.Empty;
            CurrentPhase = Phase.Comment;
            Show();
        }

        void CommitComment()
        {
            var block = Screen.BlockNumber;
            if (!TryWrite(() => _writer.AppendComment(block, TrialOf(block, 1).Conversation, Comment)))
                return;

            _screenIndex++;

            if (Screen.Kind == QuestionnaireScreenKind.Closing)
            {
                BeginClosing();
                return;
            }

            Release();
        }

        void CommitClosing()
        {
            CurrentPhase = Phase.Done;
            Show();

            var complete = _writer.IsComplete;
            _writer.Dispose();
            _writer = null;

            if (complete)
                Debug.Log($"{name}: questionnaire complete — every answer recorded.", this);
            else
                Debug.LogWarning($"{name}: questionnaire finished with answers missing; check the response file.", this);
        }

        /// <summary>The clip is over and the scene has stopped: put its rating screen up.</summary>
        void Pause_ClipEnded()
        {
            if (CurrentPhase != Phase.Playing)
                return;

            _trial = Session.Current;
            _clipWasCutShort = Pause.WasCutShort;

            var screen = Screen;
            if (screen.Kind != QuestionnaireScreenKind.Rating ||
                screen.BlockNumber != _trial.BlockNumber ||
                screen.VersionPosition != _trial.VersionPosition)
            {
                Refuse(
                    $"the questionnaire is on '{screen}' but the clip that just played was '{_trial}'. " +
                    "The two sequencers have diverged, and every answer from here would be filed against " +
                    "the wrong take.");
                return;
            }

            // Held so the operator's advance key cannot skip past an unanswered
            // screen: from here the panel is what moves the session on.
            Pause.AdvanceHeld = true;

            var scale = Definition.scale;
            Entry = new QuestionnaireEntry(Definition.perClipItems.Length, scale.min, scale.max, requireDistinct: false);
            Notice = _clipWasCutShort
                ? "this clip was cut short — it is not a usable take, and nothing will be recorded for it."
                : string.Empty;

            CurrentPhase = Phase.Rating;
            Show();
        }

        void BeginRanking()
        {
            Entry = new QuestionnaireEntry(_script.VersionsPerBlock, 1, _script.VersionsPerBlock, requireDistinct: true);
            Notice = string.Empty;
            CurrentPhase = Phase.Ranking;
            Show();
        }

        void BeginClosing()
        {
            // The agents are left hidden and the pause is never released: there
            // is no clip after this, and two agents reappearing behind the
            // closing passage would read as another take starting.
            Entry = null;
            CurrentPhase = Phase.Closing;
            Show();
        }

        /// <summary>Let the next clip start: unhold the pause and advance it.</summary>
        void Release()
        {
            Entry = null;
            Notice = string.Empty;
            CurrentPhase = Phase.Playing;
            Show();

            Pause.AdvanceHeld = false;
            Pause.Advance();
        }

        /// <summary>Push the current state to the participant's canvas, if there is one.</summary>
        void Show()
        {
            if (Display != null)
                Display.Render(this);
        }

        /// <summary>
        /// Run a write, and stop the run if it is refused. The writer enforces
        /// the recording rules — scale, item codes, one answer per question — and
        /// a refusal means the run has drifted from the instrument, which is not
        /// something to carry on through.
        /// </summary>
        bool TryWrite(Action write)
        {
            try
            {
                write();
                return true;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException)
            {
                Refuse($"the response writer refused this answer: {e.Message}");
                return false;
            }
        }

        bool TryOpen()
        {
            QuestionnaireDefinition definition;
            try
            {
                definition = QuestionnaireDefinition.LoadDefault();
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
            {
                Refuse($"the instrument could not be loaded: {e.Message}");
                return false;
            }

            var participant = Runner.StudyParticipantId?.Trim();
            if (string.IsNullOrEmpty(participant))
            {
                Refuse("the participant label is empty, so the answers would be unattributable.");
                return false;
            }

            _script = QuestionnaireScript.Build(
                definition, Session.Conversations.Length, StudySessionRunner.ConditionCount);

            try
            {
                _writer = new QuestionnaireResponseWriter(_script, ResponseDirectory(participant), participant);
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                Refuse($"the response files could not be opened: {e.Message}");
                return false;
            }

            return true;
        }

        /// <summary>
        /// This participant's own folder. A real participant gets exactly one,
        /// and the writer refuses to reopen it — writing over a recorded session
        /// is the failure that guard exists for.
        ///
        /// <para>The debugging label is the exception: every development run
        /// carries it, so it takes a timestamped folder instead. Otherwise the
        /// second run of the day would refuse to start for a reason that has
        /// nothing to do with the study.</para>
        /// </summary>
        string ResponseDirectory(string participant)
        {
            // The session opens the participant's folder first and the answers
            // belong beside its record, so its answer wins. Computing it again
            // here would put them in two folders whenever the timestamped debug
            // label was in play and the two calls fell in different seconds.
            if (Session != null && !string.IsNullOrEmpty(Session.ParticipantDirectory))
                return Session.ParticipantDirectory;

            var root = Path.Combine(Application.dataPath, "..", OutputDirectory);
            return Path.Combine(root, ParticipantFolder.NameFor(participant, DateTime.Now));
        }

        /// <summary>The trial that filled one slot of a block, for the conversation and condition a row needs.</summary>
        StudyTrial TrialOf(int block, int versionPosition)
        {
            foreach (var trial in Session.Trials)
            {
                if (trial.BlockNumber == block && trial.VersionPosition == versionPosition)
                    return trial;
            }

            return default;
        }

        /// <summary>
        /// Whether this is a run the questionnaire belongs in. Tied to the
        /// session runner rather than to a switch of its own: a bake, a demo
        /// recording and a preview all press Play in this scene, and turning the
        /// session runner off is already how each of them says it is not a
        /// participant run.
        /// </summary>
        bool IsRunnable() =>
            isActiveAndEnabled && Session != null && Session.isActiveAndEnabled &&
            Pause != null && Runner != null;

        void Refuse(string reason)
        {
            Notice = reason;
            CurrentPhase = Phase.Refused;
            Entry = null;

            // The holds stay on. Whatever went wrong, the safe direction is a
            // session that will not run rather than one that runs unrecorded.
            Debug.LogError($"{name}: questionnaire stopped — {reason}", this);
            Show();
        }
    }
}
