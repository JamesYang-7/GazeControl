using GazeControl.Study;
using UnityEngine;

namespace GazeControl.Experiment
{
    /// <summary>
    /// The experimenter's half of the questionnaire: the item being asked, and
    /// the keys that record what the participant said.
    ///
    /// <para><b>Drawn with IMGUI, which is why it is invisible to the
    /// participant</b> (<c>questionnaire-ui-design.md</c> §1). Unity renders
    /// screen-space overlays and <c>OnGUI</c> into the desktop window only, not
    /// into the eye textures, so one scene gives two independent surfaces with
    /// no second camera and no culling mask to get wrong. <b>Confirm that on the
    /// Varjo before the first participant</b> — it is the one assumption this
    /// design rests on that has not been checked on hardware.</para>
    ///
    /// <para><b>The panel is blinded</b> (§2). It shows "Conversation 3 ·
    /// Version 2 of 3" and never the method behind a version. The experimenter
    /// reads the items aloud and knows the hypothesis; naming the condition
    /// would tell them which answer is the interesting one while they are the
    /// person speaking the question. The condition reaches the response file
    /// straight from the study harness and is displayed nowhere.</para>
    ///
    /// <para>Every key is read from <see cref="Event"/> inside <c>OnGUI</c>
    /// rather than through the Input System, so typing a comment cannot also
    /// enter a rating: IMGUI's own focus rules decide where a keystroke goes,
    /// and the text field eats what it is given.</para>
    /// </summary>
    public sealed class QuestionnaireOperatorPanel : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("The run this panel enters answers for; found in the scene when left empty")]
        public QuestionnaireSession Session { get; set; }

        [field: SerializeField]
        [field: Tooltip("Panel width in pixels; it sits against the left edge of the game view")]
        public float Width { get; set; } = 560f;

        const string k_CommentControl = "questionnaire.comment";

        Vector2 _scroll;
        GUIStyle _label;
        GUIStyle _heading;
        bool _focusComment;

        void Awake()
        {
            if (Session == null)
                Session = FindFirstObjectByType<QuestionnaireSession>();
        }

        void OnGUI()
        {
            if (Session == null || Session.CurrentPhase == QuestionnaireSession.Phase.Inactive)
                return;

            EnsureStyles();
            HandleKeys();

            var rect = new Rect(10f, 10f, Width, Screen.height - 20f);
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 20f));
            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawHeader();
            GUILayout.Space(8f);
            DrawScreen();
            GUILayout.Space(8f);
            DrawNotice();
            GUILayout.Space(8f);
            DrawKeys();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawHeader()
        {
            var screen = Session.Screen;
            GUILayout.Label("QUESTIONNAIRE — OPERATOR", _heading);

            var where = screen.BlockNumber > 0
                ? $"Conversation {screen.BlockNumber}" +
                  (screen.VersionPosition > 0 ? $" · Version {screen.VersionPosition} of 3" : " · all three versions")
                : "before the first conversation";

            GUILayout.Label($"Screen {screen.Number} of {Session.ScreenCount} — {where}", _label);
            GUILayout.Label($"Phase: {Session.CurrentPhase}", _label);
        }

        void DrawScreen()
        {
            var definition = Session.Definition;
            if (definition == null)
                return;

            switch (Session.CurrentPhase)
            {
                case QuestionnaireSession.Phase.Framing:
                    GUILayout.Label("Read to the participant:", _heading);
                    GUILayout.Label(definition.FramingBody(Session.GroupCount, StudySessionRunner.ConditionCount), _label);
                    break;

                case QuestionnaireSession.Phase.RatingPreview:
                    DrawRatingPreview(definition);
                    break;

                case QuestionnaireSession.Phase.ShortAnswerPreview:
                    DrawShortAnswerPreview(definition);
                    break;

                case QuestionnaireSession.Phase.Playing:
                    GUILayout.Label("Clip playing — nothing to enter.", _label);
                    break;

                case QuestionnaireSession.Phase.Rating:
                    DrawRating(definition);
                    break;

                case QuestionnaireSession.Phase.Ranking:
                    DrawRanking(definition);
                    break;

                case QuestionnaireSession.Phase.Comment:
                    DrawComment(definition);
                    break;

                case QuestionnaireSession.Phase.Closing:
                    GUILayout.Label("Read to the participant:", _heading);
                    GUILayout.Label(definition.closing.body, _label);
                    break;

                case QuestionnaireSession.Phase.Done:
                    GUILayout.Label("Session complete. Every answer is written.", _heading);
                    GUILayout.Label(Session.ResponsesPath ?? string.Empty, _label);
                    break;

                case QuestionnaireSession.Phase.Refused:
                    GUILayout.Label("STOPPED", _heading);
                    break;
            }
        }

        /// <summary>
        /// The four rating items before the first clip: the participant is shown
        /// them so they know what follows every version, and nothing is entered.
        /// Read them aloud and move on.
        /// </summary>
        void DrawRatingPreview(QuestionnaireDefinition definition)
        {
            GUILayout.Label("Read to the participant — these come after every version:", _heading);

            foreach (var item in definition.perClipItems)
                GUILayout.Label(item.text, _label);

            GUILayout.Space(6f);
            GUILayout.Label(
                $"Scale {definition.scale.min}-{definition.scale.max}: " +
                $"{definition.scale.min} {definition.scale.minLabel}, {definition.scale.max} {definition.scale.maxLabel}",
                _label);
            GUILayout.Label("Nothing is answered here.", _label);
        }

        /// <summary>
        /// The two short-answer questions before the first clip: the participant
        /// is shown them so they know what a group ends with, and nothing is
        /// entered. Read them aloud and move on.
        /// </summary>
        void DrawShortAnswerPreview(QuestionnaireDefinition definition)
        {
            GUILayout.Label("Read to the participant — these come after every conversation:", _heading);
            GUILayout.Label(definition.ranking.prompt, _label);
            GUILayout.Label(definition.ranking.instruction ?? string.Empty, _label);
            GUILayout.Label(definition.comment.prompt, _label);
            GUILayout.Space(6f);
            GUILayout.Label("Nothing is answered here.", _label);
        }

        void DrawRating(QuestionnaireDefinition definition)
        {
            var entry = Session.Entry;
            var items = definition.perClipItems;

            GUILayout.Label(
                $"Scale {definition.scale.min}-{definition.scale.max}: " +
                $"{definition.scale.min} {definition.scale.minLabel}, {definition.scale.max} {definition.scale.maxLabel}",
                _label);
            GUILayout.Space(6f);

            for (var i = 0; i < items.Length; i++)
            {
                var current = entry != null && entry.Cursor == i;
                var value = entry != null ? entry[i] : QuestionnaireEntry.Unanswered;
                var answer = value == QuestionnaireEntry.Unanswered ? "_" : value.ToString();

                // The item code (N1, T2 …) is analysis shorthand and is not read
                // to the participant, so it is deliberately not in this line.
                GUILayout.Label($"{(current ? ">" : " ")} [{answer}]  {items[i].text}", _label);
            }
        }

        void DrawRanking(QuestionnaireDefinition definition)
        {
            var entry = Session.Entry;

            GUILayout.Label("Read aloud:", _heading);
            GUILayout.Label(definition.ranking.prompt, _label);
            GUILayout.Label(definition.ranking.instruction, _label);
            GUILayout.Space(6f);

            for (var position = 1; entry != null && position <= entry.SlotCount; position++)
            {
                var current = entry.Cursor == position - 1;
                var value = entry[position - 1];
                var rank = value == QuestionnaireEntry.Unanswered ? "_" : value.ToString();
                GUILayout.Label($"{(current ? ">" : " ")} [{rank}]  {definition.ranking.LabelFor(position)}", _label);
            }
        }

        void DrawComment(QuestionnaireDefinition definition)
        {
            GUILayout.Label("Read aloud:", _heading);
            GUILayout.Label(definition.comment.prompt, _label);
            GUILayout.Space(6f);

            GUI.SetNextControlName(k_CommentControl);
            Session.Comment = GUILayout.TextArea(Session.Comment ?? string.Empty, GUILayout.MinHeight(90f));

            if (_focusComment)
            {
                _focusComment = false;
                GUI.FocusControl(k_CommentControl);
            }

            if (GUILayout.Button("Record this answer  (Ctrl+Enter)"))
                Session.Commit();
        }

        void DrawNotice()
        {
            if (!string.IsNullOrEmpty(Session.Notice))
                GUILayout.Label(Session.Notice, _label);
        }

        void DrawKeys()
        {
            var keys = Session.CurrentPhase switch
            {
                QuestionnaireSession.Phase.Rating =>
                    Session.ClipWasCutShort
                        ? "[Enter] move on without recording"
                        : "[1-7] answer   [Backspace] undo   [Enter] record and play the next clip",
                QuestionnaireSession.Phase.Ranking =>
                    "[1-3] rank each version in turn   [Backspace] undo   [Enter] record",
                QuestionnaireSession.Phase.Comment =>
                    "type the answer, or leave it empty   [Ctrl+Enter] record",
                QuestionnaireSession.Phase.Framing => "[Enter] show the four rating items",
                QuestionnaireSession.Phase.RatingPreview => "[Enter] show the two end-of-group questions",
                QuestionnaireSession.Phase.ShortAnswerPreview => "[Enter] start the first clip",
                QuestionnaireSession.Phase.Closing => "[Enter] finish the session",
                _ => string.Empty,
            };

            if (!string.IsNullOrEmpty(keys))
                GUILayout.Label(keys, _label);
        }

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            var phase = Session.CurrentPhase;

            // The comment field owns the keyboard while it is up: a digit there
            // is part of an answer, not a rating. Only the modifier commits.
            if (phase == QuestionnaireSession.Phase.Comment)
            {
                if (IsReturn(e.keyCode) && (e.control || e.command))
                {
                    Session.Commit();
                    e.Use();
                }

                return;
            }

            if (IsReturn(e.keyCode))
            {
                Session.Commit();
                _focusComment = Session.CurrentPhase == QuestionnaireSession.Phase.Comment;
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Backspace)
            {
                Session.Back();
                e.Use();
                return;
            }

            var digit = DigitOf(e.keyCode);
            if (digit > 0)
            {
                Session.Enter(digit);
                e.Use();
            }
        }

        static bool IsReturn(KeyCode key) => key is KeyCode.Return or KeyCode.KeypadEnter;

        /// <summary>The number typed, top row or keypad, or 0 for any other key.</summary>
        static int DigitOf(KeyCode key)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha9)
                return key - KeyCode.Alpha1 + 1;

            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad9)
                return key - KeyCode.Keypad1 + 1;

            return 0;
        }

        void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            _heading = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, wordWrap = true };
        }
    }
}
