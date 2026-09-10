using System.Text;
using GazeControl.Study;
using GazeControl.Xr;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeControl.Experiment
{
    /// <summary>
    /// The participant's half of the questionnaire: a world-space panel showing
    /// the screen they are being asked about, and — under it — the row of
    /// buttons they may answer with themselves.
    ///
    /// <para><b>It was display-only until 2026-09-09</b>
    /// (<c>questionnaire-ui-design.md</c> §0). The participant answered aloud and
    /// the operator typed it, which bought the design out of the XR Interaction
    /// Toolkit, ray interactors and a controller path never run under the Varjo
    /// loader. The button row is the user's call to add a second way in, and it
    /// keeps that saving: the row is flat rectangles on this same canvas, aimed
    /// at by intersecting a ray with the panel plane
    /// (<see cref="PanelRaycast"/>), so there is still no interaction toolkit,
    /// no event system and no collider in the scene. The spoken path is
    /// untouched — <see cref="QuestionnaireSession"/> takes both through the same
    /// state machine.</para>
    ///
    /// <para><b>A rating screen mirrors the operator's own list</b> (user's
    /// call, 2026-09-02, replacing the marker-only list of 2026-08-27). Every
    /// item is numbered and carries a box, the <c>&gt;</c> marks the one being
    /// asked, and the number the operator types — or the participant presses —
    /// appears in that item's box, so the participant can see their answer land
    /// and say so when it was heard wrong, which is the failure mode of
    /// answering aloud.</para>
    ///
    /// <para>The panel is built in code rather than kept as a prefab so that the
    /// wording has exactly one source — <c>Resources/Questionnaire.json</c>, the
    /// same file the operator panel reads. A prefab would be a second copy of
    /// the instrument, free to drift.</para>
    /// </summary>
    public sealed class QuestionnaireDisplay : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Question page size in metres, as the participant sees it at its distance. The button " +
                        "row hangs below this and is not counted in it.")]
        public Vector2 PanelSize { get; set; } = new(1.2f, 0.8f);

        [field: SerializeField]
        [field: Tooltip("Height of the question page's centre above the floor. The participant's eye line, so " +
                        "reading it is the same gaze direction as watching the agents.")]
        public float EyeHeight { get; set; } = ParticipantEyeHeight.SmplxEyeHeight;

        [field: SerializeField]
        [field: Tooltip("How far in front of the participant the panel hangs, metres. 1.5 m is where the agents " +
                        "stand, so the eyes do not have to re-converge to read it.")]
        public float Distance { get; set; } = 1.5f;

        [field: SerializeField]
        [field: Range(20f, 80f)]
        [field: Tooltip("Body type size, in the panel's own units — 1000 of them to the metre")]
        public float BodyFontSize { get; set; } = 34f;

        [field: SerializeField]
        [field: Tooltip("How tall the participant's buttons are, metres. Nine of them span the panel on a rating " +
                        "screen, so this is the smaller of the two dimensions a controller has to hit.")]
        public float ButtonHeight { get; set; } = 0.14f;

        [field: SerializeField]
        [field: Tooltip("Clear space between the bottom of the question page and the top of the button row, metres")]
        public float ButtonRowGap { get; set; } = 0.05f;

        [field: SerializeField]
        [field: Tooltip("Dead space between neighbouring buttons, metres. A press that lands in it hits neither, " +
                        "which is the right answer when the aim was between two.")]
        public float ButtonGap { get; set; } = 0.012f;

        [field: SerializeField]
        [field: Tooltip("Panel background. Dark and opaque: a bright field at 1.5 m is tiring to read in a headset, " +
                        "and anything showing through it is one more thing to look at instead of the question.")]
        public Color Background { get; set; } = new(0.06f, 0.06f, 0.07f, 1f);

        [field: SerializeField]
        [field: Tooltip("Text colour")]
        public Color Foreground { get; set; } = new(0.94f, 0.94f, 0.96f, 1f);

        [field: SerializeField]
        [field: Tooltip("A button that may be pressed")]
        public Color ButtonFace { get; set; } = new(0.20f, 0.21f, 0.24f, 1f);

        [field: SerializeField]
        [field: Tooltip("The button the controller is pointing at")]
        public Color ButtonFaceAimed { get; set; } = new(0.42f, 0.50f, 0.62f, 1f);

        [field: SerializeField]
        [field: Tooltip("A button that would be refused — nothing to undo, a rank already used, a rating not yet " +
                        "complete. Dimmed rather than hidden, so the row keeps its shape from screen to screen.")]
        public Color ButtonFaceDisabled { get; set; } = new(0.10f, 0.10f, 0.12f, 1f);

        [field: SerializeField]
        [field: Tooltip("Where the controller's ray meets the panel, in metres across")]
        public float CursorSize { get; set; } = 0.02f;

        /// <summary>
        /// Canvas units to the metre. A world-space canvas is laid out in these
        /// and scaled down; 1000 keeps the numbers in the range UI text is
        /// designed for, so type sizes read like type sizes.
        /// </summary>
        const float k_UnitsPerMetre = 1000f;

        const float k_Margin = 70f;

        RectTransform _panel;
        RectTransform _row;
        RectTransform _cursor;
        TextMeshProUGUI _title;
        TextMeshProUGUI _body;
        TextMeshProUGUI _footer;
        Image[] _buttonFaces;
        TextMeshProUGUI[] _buttonLabels;
        QuestionnaireButton[] _buttons;
        ButtonRowLayout _layout;
        int _buttonCount;
        int _aimedAt = -1;

        /// <summary>
        /// The most buttons a row will ever hold. Built once and shown or hidden
        /// per screen rather than rebuilt: nine widgets created and destroyed
        /// twenty times a session is twenty chances for a frame with half a row
        /// on it, for no gain.
        /// </summary>
        const int k_MaximumButtons = 12;

        /// <summary>
        /// How many buttons a lone <c>Next</c> is sized as one of: the
        /// short-answer row's five, so the button the participant meets first is
        /// the size of the ones they will meet later.
        /// </summary>
        const int k_SoloButtonSlots = 5;

        void Awake()
        {
            Build();
            SetVisible(false);
        }

        /// <summary>
        /// Put the panel where the participant is facing: on their own eye line,
        /// at the agents' distance, square to the vertex it hangs from.
        ///
        /// <para>The component places itself rather than trusting the scene's
        /// transform, because a <c>RectTransform</c> is not a plain transform —
        /// its position is expressed through anchors, and an offset written into
        /// the scene by hand or by a setup script does not reliably survive.
        /// Owning the geometry here also puts the three numbers a headset
        /// legibility pass needs (height, distance, type size) on one
        /// component.</para>
        ///
        /// <para>The canvas is taller than the question page by the button row
        /// beneath it, and is dropped by half that so the <i>page</i> stays
        /// centred on the eye line. The row is what moved down, not the
        /// reading.</para>
        /// </summary>
        public void ApplyPlacement()
        {
            transform.localPosition = new Vector3(0f, EyeHeight - BelowPanel * 0.5f, Distance);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        /// <summary>How much canvas hangs below the question page: the gap and the buttons, in metres.</summary>
        float BelowPanel => ButtonRowGap + ButtonHeight;

        /// <summary>Show whatever the session is on, or hide the panel when it is not asking anything.</summary>
        public void Render(QuestionnaireSession session)
        {
            if (_panel == null)
                Build();

            var definition = session.Definition;
            if (definition == null)
            {
                SetVisible(false);
                return;
            }

            switch (session.CurrentPhase)
            {
                case QuestionnaireSession.Phase.Framing:
                    Passage(definition.framing.title,
                        definition.FramingBody(session.GroupCount, StudySessionRunner.ConditionCount));
                    break;

                case QuestionnaireSession.Phase.RatingPreview:
                case QuestionnaireSession.Phase.Rating:
                    Rating(session, definition);
                    break;

                case QuestionnaireSession.Phase.ShortAnswerPreview:
                case QuestionnaireSession.Phase.Ranking:
                case QuestionnaireSession.Phase.Comment:
                    ShortAnswer(session, definition);
                    break;

                case QuestionnaireSession.Phase.Closing:
                    Passage(definition.closing);
                    break;

                default:
                    // Playing, Done, Inactive and Refused all show the
                    // participant nothing: a clip is running, the session is
                    // over, or something has gone wrong that is the operator's
                    // to read and not theirs.
                    SetVisible(false);
                    return;
            }

            RenderButtons(session.Buttons);
            SetVisible(true);
        }

        /// <summary>
        /// Where a ray lands on the panel, and which button — if any — it is on.
        ///
        /// <para>The ray is intersected with the canvas plane rather than
        /// raycast against colliders: the panel is flat, the buttons are
        /// rectangles laid out on it by <see cref="ButtonRowLayout"/>, and the
        /// same layout answers both where a button is drawn and what a point
        /// falls in. A hit test computed apart from the rectangles is one that
        /// can be millimetres out for a whole study without anyone seeing it —
        /// the participant would press one thing and get its neighbour.</para>
        /// </summary>
        /// <param name="worldRay">The controller's aim, in world space.</param>
        /// <param name="worldPoint">Where it meets the panel.</param>
        /// <param name="buttonIndex">The button it is on, or -1 elsewhere on the panel.</param>
        /// <returns>
        /// False when the panel is not showing, or the ray misses it. <b>Missing
        /// the panel, not missing its plane</b> (fixed 2026-09-09, on hardware):
        /// the plane is infinite, so a controller hanging at the participant's
        /// side and pointing vaguely forward crosses it metres off to one side
        /// and counted as a hit. That put the cursor at the end of the idle
        /// controller's ray and, because the first hit wins, left the controller
        /// actually aimed at a button with no cursor and no highlight at all —
        /// which from inside the headset is a dead controller.
        /// </returns>
        public bool TryAim(Ray worldRay, out Vector3 worldPoint, out int buttonIndex)
        {
            worldPoint = default;
            buttonIndex = -1;

            if (_panel == null || !_panel.gameObject.activeSelf)
                return false;

            var canvas = (RectTransform)transform;
            var origin = canvas.InverseTransformPoint(worldRay.origin);

            // Unaffected by scale, unlike InverseTransformPoint, so it stays a
            // unit vector in a space scaled by 1/1000.
            var direction = canvas.InverseTransformDirection(worldRay.direction);

            if (!PanelRaycast.TryHit(origin, direction, out var point, out _))
                return false;

            // The canvas rect, which is the page and the button row together, so
            // the dot can be watched travelling down the question to the answer.
            if (!canvas.rect.Contains(point))
                return false;

            worldPoint = canvas.TransformPoint(new Vector3(point.x, point.y, 0f));

            if (_buttonCount > 0 && _row != null && _row.gameObject.activeSelf)
            {
                var inRow = _row.InverseTransformPoint(worldPoint);
                buttonIndex = _layout.IndexAt(new Vector2(inRow.x, inRow.y));
            }

            return true;
        }

        /// <summary>
        /// Put the pointer's dot on the panel and light the button under it.
        /// Called every frame by <see cref="QuestionnaireControllerPointer"/>,
        /// and with <paramref name="visible"/> false whenever nothing is aiming
        /// at the panel.
        /// </summary>
        public void ShowPointer(bool visible, Vector3 worldPoint, int buttonIndex)
        {
            if (_cursor == null)
                return;

            // Never over a hidden panel. A press can commit the screen and start
            // the next clip in the middle of the pointer's own update, and the
            // aim it is holding at that moment is a frame old — without this the
            // dot hangs in the air for a frame after the page it was on has
            // gone.
            visible &= _panel != null && _panel.gameObject.activeSelf;

            if (_cursor.gameObject.activeSelf != visible)
                _cursor.gameObject.SetActive(visible);

            if (visible)
                _cursor.position = worldPoint;

            var aimed = visible ? buttonIndex : -1;
            if (aimed == _aimedAt)
                return;

            _aimedAt = aimed;
            RepaintButtons();
        }

        void Passage(QuestionnaireDefinition.Passage passage) => Passage(passage.title, passage.body);

        void Passage(string title, string body)
        {
            // Vertically centred, unlike the item lists: a passage is one block
            // of prose and reads better in the middle of the panel than pinned
            // to the line the items happen to start on.
            _body.alignment = TextAlignmentOptions.Left;
            _title.text = title;
            _body.text = body;
            _footer.text = string.Empty;
        }

        /// <summary>
        /// The four items with their boxes. It doubles as the rating preview
        /// shown before the first clip: there is no entry then, so every box is
        /// blank and nothing carries the <c>&gt;</c> — which is exactly what a
        /// page nobody is being asked to answer yet should look like.
        /// </summary>
        void Rating(QuestionnaireSession session, QuestionnaireDefinition definition)
        {
            var items = definition.perClipItems;
            var entry = session.Entry;
            var cursor = entry != null ? entry.Cursor : -1;

            var body = new StringBuilder();
            for (var i = 0; i < items.Length; i++)
            {
                // A plain '>' rather than a glyph like U+25B6: the panel runs on
                // the LiberationSans SDF font that ships with TMP, and a
                // character it has no glyph for renders as an empty box.
                body.Append(i == cursor ? "> " : "  ");
                body.Append($"[{AnswerBox(entry, i)}] {i + 1}. ");
                body.AppendLine(items[i].text);
                body.AppendLine();
            }

            _body.alignment = TextAlignmentOptions.TopLeft;
            _title.text = definition.ratingTitle ?? string.Empty;
            _body.text = body.ToString().TrimEnd();
            _footer.text = ScaleLine(definition.scale);
        }

        /// <summary>
        /// What goes in one item's box: the answer recorded for it, or a blank
        /// slot. An underscore rather than a space, both because it is the
        /// operator panel's own mark for a slot still waiting on an answer, and
        /// because it is the width of a digit in this font — a space is not, and
        /// the item text would step sideways as the boxes filled.
        /// </summary>
        static string AnswerBox(QuestionnaireEntry entry, int slot)
        {
            if (entry == null || slot >= entry.SlotCount)
                return "_";

            var value = entry[slot];
            return value == QuestionnaireEntry.Unanswered ? "_" : value.ToString();
        }

        /// <summary>
        /// The ranking and the free-text probe on one page, numbered on from the
        /// rating items, so a participant reads six questions about a
        /// conversation rather than four and then two unnumbered ones (user's
        /// call, 2026-09-02). The operator still enters them as two screens — the
        /// ranks first, then the comment — because they are two records and the
        /// ranking refuses a tie.
        ///
        /// <para><b>It is also shown once before the first clip</b> (user's call,
        /// 2026-09-02): a participant who knows a group ends by ranking its three
        /// versions watches the first one for what they will be asked about,
        /// instead of meeting the question when it is too late to look. That
        /// showing answers nothing — one press of the operator's key moves it
        /// on.</para>
        ///
        /// <para>The foot names the group about to be watched, which is the only
        /// sense the participant is given of where they are in a session of five.
        /// After the last group there is none, and it says nothing.</para>
        ///
        /// <para><b>The prompts, the order given so far, and a marker on the
        /// one being asked.</b> "You may skip this question" is the operator's
        /// script, not the participant's reading: a rule the participant cannot
        /// break by speaking is one more line to read before answering. The
        /// ranking used to list nothing at all for the same reason (2026-08-31)
        /// — until the participant could enter it themselves, at which point a
        /// press that showed nothing back was a press they could not check or
        /// correct (user's call, 2026-09-09). It is the order they gave, in the
        /// buttons' own labels, and it appears only once something has been
        /// entered. The <c>&gt;</c> against the current question arrived with it
        /// and for the same reason: two questions on a page, and the buttons
        /// change under them, so which one the row is answering has to be
        /// visible.</para>
        /// </summary>
        void ShortAnswer(QuestionnaireSession session, QuestionnaireDefinition definition)
        {
            // The first free number after the rating items, so an instrument that
            // gains or loses one keeps the sequence the participant is reading.
            var number = definition.perClipItems.Length + 1;

            // Which of the two is being asked. The preview asks neither, so it
            // marks neither — exactly as the rating preview shows no cursor.
            var asking = session.CurrentPhase;

            var body = new StringBuilder();
            body.Append(Marker(asking == QuestionnaireSession.Phase.Ranking));
            body.Append(number).Append(". ").AppendLine(definition.ranking.prompt);

            var ranking = RankingLine(session.RankingEntered);
            if (ranking.Length > 0)
            {
                body.AppendLine();
                body.Append(k_ShortAnswerIndent).AppendLine(ranking);
            }

            body.AppendLine();
            body.Append(Marker(asking == QuestionnaireSession.Phase.Comment));
            body.Append(number + 1).Append(". ").AppendLine(definition.comment.prompt);

            _body.alignment = TextAlignmentOptions.TopLeft;
            _title.text = definition.shortAnswerTitle ?? string.Empty;
            _body.text = body.ToString().TrimEnd();
            _footer.text = definition.NextGroupText(session.NextGroupNumber, session.GroupCount);
        }

        /// <summary>
        /// The <c>&gt;</c> against the question being asked, or the blank that
        /// holds its place. The same two columns the rating page uses, so a
        /// participant reads one convention across both, and a plain '&gt;' for
        /// the same reason: the panel's font has no glyph for a nicer arrow and
        /// would draw an empty box.
        /// </summary>
        static string Marker(bool current) => current ? "> " : "  ";

        /// <summary>
        /// Indent that puts the order given inside the prompt it belongs to:
        /// two columns for the marker, three for "5. ", and two more so it reads
        /// as subordinate to the question rather than as another one.
        /// </summary>
        const string k_ShortAnswerIndent = "       ";

        /// <summary>
        /// The order given so far — "Your order:   1st: V2,   2nd: V1,   3rd: _"
        /// — or empty before anything is entered.
        ///
        /// <para><b>Ranks named, not chained</b> (user's call, 2026-09-09,
        /// replacing "V2 &gt; V1 &gt; _"). A chain of "&gt;" leaves the reader
        /// to work out which end is best from the shape of the line, and a
        /// participant pressing the buttons alone has nobody to ask; an ordinal
        /// against each slot says it outright and names the empty one they are
        /// filling next.</para>
        ///
        /// <para>The versions carry the buttons' own labels rather than the
        /// instrument's "Version 2", so what is on the line is what was pressed
        /// to put it there.</para>
        /// </summary>
        static string RankingLine(int[] versionsBestFirst)
        {
            if (versionsBestFirst == null || versionsBestFirst.Length == 0)
                return string.Empty;

            var line = new StringBuilder("Your order:   ");
            for (var i = 0; i < versionsBestFirst.Length; i++)
            {
                if (i > 0)
                    line.Append(",   ");

                var version = versionsBestFirst[i];
                line.Append(RankingOrder.Ordinal(i + 1)).Append(": ");
                line.Append(version == QuestionnaireEntry.Unanswered ? "_" : $"V{version}");
            }

            return line.ToString();
        }

        /// <summary>
        /// The range sentence over the three anchors. The anchors are led rather
        /// than left to speak for themselves because three numbers each followed
        /// by an "=" read as a three-option menu, and participants asked whether
        /// they were choosing from 1, 4 and 7 or from the whole range.
        /// </summary>
        static string ScaleLine(QuestionnaireDefinition.Scale scale)
        {
            var anchors =
                $"{scale.min} = {scale.minLabel}     {(scale.min + scale.max) / 2} = {scale.midLabel}     " +
                $"{scale.max} = {scale.maxLabel}";

            var range = scale.RangeHintText;
            return range.Length == 0 ? anchors : $"{range}\n{anchors}";
        }

        /// <summary>Lay the screen's buttons out and label them, or hide the row when it asks for none.</summary>
        void RenderButtons(QuestionnaireButton[] buttons)
        {
            _buttons = buttons;
            _buttonCount = buttons == null ? 0 : Mathf.Min(buttons.Length, _buttonFaces.Length);

            if (buttons != null && buttons.Length > _buttonFaces.Length)
            {
                Debug.LogError(
                    $"{name}: this screen asks for {buttons.Length} buttons and the row holds {_buttonFaces.Length}. " +
                    "Raise the row's maximum rather than letting a participant answer with part of a scale.", this);
            }

            _row.gameObject.SetActive(_buttonCount > 0);
            if (_buttonCount == 0)
                return;

            var gap = ButtonGap * k_UnitsPerMetre;
            var rowWidth = PanelSize.x * k_UnitsPerMetre - 2f * k_Margin;

            // A row of one is the framing passage's lone Next, and spreading one
            // button over the whole row would give it a button a metre wide.
            // It takes the width a short-answer row's button gets instead —
            // narrowing the layout rather than the button, so it stays centred
            // and the hit test still comes from the same rectangles.
            if (_buttonCount == 1)
                rowWidth = (rowWidth - gap * (k_SoloButtonSlots - 1)) / k_SoloButtonSlots;

            _layout = new ButtonRowLayout(rowWidth, ButtonHeight * k_UnitsPerMetre, _buttonCount, gap);

            for (var i = 0; i < _buttonFaces.Length; i++)
            {
                var face = _buttonFaces[i];
                var used = i < _buttonCount;
                face.gameObject.SetActive(used);
                if (!used)
                    continue;

                var rect = _layout.RectOf(i);
                var transformOfFace = (RectTransform)face.transform;
                transformOfFace.sizeDelta = new Vector2(rect.width, rect.height);
                transformOfFace.anchoredPosition = rect.center;

                _buttonLabels[i].text = buttons[i].Label ?? string.Empty;
            }

            RepaintButtons();
        }

        /// <summary>
        /// Colour the row for what it now allows and what the controller is on.
        /// Split from the layout because aiming changes many times a second and
        /// nothing about the rectangles changes with it.
        /// </summary>
        void RepaintButtons()
        {
            if (_buttons == null)
                return;

            for (var i = 0; i < _buttonCount; i++)
            {
                var button = _buttons[i];
                _buttonFaces[i].color = !button.Enabled ? ButtonFaceDisabled
                    : i == _aimedAt ? ButtonFaceAimed
                    : ButtonFace;

                // Dimmed rather than greyed to a fixed colour, so a panel
                // recoloured in the inspector keeps one text colour family.
                _buttonLabels[i].color = button.Enabled ? Foreground : Foreground * 0.45f;
            }
        }

        void SetVisible(bool visible)
        {
            if (_panel != null)
                _panel.gameObject.SetActive(visible);

            if (_row != null)
                _row.gameObject.SetActive(visible && _buttonCount > 0);

            if (!visible)
                ShowPointer(false, Vector3.zero, -1);
        }

        void Build()
        {
            ApplyPlacement();

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();

            canvas.renderMode = RenderMode.WorldSpace;

            // Canvas requires a RectTransform and Unity swaps the plain one out
            // when the component is added; the guard is for the case where that
            // has not happened, which would otherwise throw mid-build and leave
            // half a panel.
            if (canvas.transform is not RectTransform canvasRect)
            {
                Debug.LogError($"{name}: the questionnaire panel needs a RectTransform.", this);
                return;
            }

            var total = PanelSize.y + BelowPanel;
            canvasRect.sizeDelta = new Vector2(PanelSize.x, total) * k_UnitsPerMetre;
            canvasRect.localScale = Vector3.one / k_UnitsPerMetre;

            // Fractions of the canvas rather than offsets, because everything
            // below is anchored and an offset would have to be recomputed
            // wherever the row's height is retuned.
            var pageBottom = 1f - PanelSize.y / total;
            var rowTop = ButtonHeight / total;

            _panel = NewChild("Panel", canvasRect);
            _panel.anchorMin = new Vector2(0f, pageBottom);
            _panel.anchorMax = Vector2.one;
            _panel.offsetMin = Vector2.zero;
            _panel.offsetMax = Vector2.zero;

            var background = _panel.gameObject.AddComponent<Image>();
            background.color = Background;

            // Stretched top, middle and bottom rather than a layout group: three
            // fixed regions read the same on every screen, and a screen whose
            // text moves between blocks is one more thing for a participant to
            // process before answering.
            _title = NewText("Title", _panel, new Vector2(0f, 0.74f), new Vector2(1f, 1f),
                BodyFontSize * 1.25f, TextAlignmentOptions.TopLeft);
            _body = NewText("Body", _panel, new Vector2(0f, 0.24f), new Vector2(1f, 0.74f),
                BodyFontSize, TextAlignmentOptions.TopLeft);
            _footer = NewText("Footer", _panel, new Vector2(0f, 0.02f), new Vector2(1f, 0.22f),
                BodyFontSize * 0.8f, TextAlignmentOptions.Left);

            BuildButtonRow(canvasRect, rowTop);
            BuildCursor(canvasRect);
        }

        /// <summary>
        /// The row of buttons, below the page and outside its background: a
        /// separate strip reads as something to press, where the same buttons
        /// inside the page's field read as more of the question.
        /// </summary>
        void BuildButtonRow(RectTransform canvasRect, float rowTop)
        {
            _row = NewChild("Buttons", canvasRect);
            _row.anchorMin = Vector2.zero;
            _row.anchorMax = new Vector2(1f, rowTop);
            _row.offsetMin = Vector2.zero;
            _row.offsetMax = Vector2.zero;

            _buttonFaces = new Image[k_MaximumButtons];
            _buttonLabels = new TextMeshProUGUI[k_MaximumButtons];

            for (var i = 0; i < k_MaximumButtons; i++)
            {
                // Anchored at the row's centre and positioned by the layout, so
                // one ButtonRowLayout describes both where this is drawn and
                // what a ray falling on it hits.
                var button = NewChild($"Button {i}", _row);
                button.anchorMin = new Vector2(0.5f, 0.5f);
                button.anchorMax = new Vector2(0.5f, 0.5f);
                button.pivot = new Vector2(0.5f, 0.5f);

                var face = button.gameObject.AddComponent<Image>();
                face.color = ButtonFace;
                _buttonFaces[i] = face;

                // Stretched to the button with no inset: NewText's margin is the
                // page's, and on a button an eighth its width it would invert
                // the rect.
                var label = NewText("Label", button, Vector2.zero, Vector2.one,
                    BodyFontSize, TextAlignmentOptions.Center, inset: 0f);
                _buttonLabels[i] = label;

                button.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// The dot where the controller's ray meets the panel. On the canvas
        /// rather than in the world so that it needs no material and no shader —
        /// and so it can never end up behind the panel it is pointing at.
        /// </summary>
        void BuildCursor(RectTransform canvasRect)
        {
            _cursor = NewChild("Cursor", canvasRect);
            _cursor.anchorMin = new Vector2(0.5f, 0.5f);
            _cursor.anchorMax = new Vector2(0.5f, 0.5f);
            _cursor.pivot = new Vector2(0.5f, 0.5f);
            _cursor.sizeDelta = Vector2.one * (CursorSize * k_UnitsPerMetre);

            var dot = _cursor.gameObject.AddComponent<Image>();
            dot.color = Foreground;
            dot.raycastTarget = false;

            _cursor.gameObject.SetActive(false);
        }

        TextMeshProUGUI NewText(
            string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            float fontSize, TextAlignmentOptions alignment, float inset = k_Margin)
        {
            var rect = NewChild(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(inset, inset * 0.5f);
            rect.offsetMax = new Vector2(-inset, -inset * 0.5f);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Foreground;
            text.alignment = alignment;
            text.richText = false;
            return text;
        }

        static RectTransform NewChild(string name, RectTransform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            return rect;
        }
    }
}
