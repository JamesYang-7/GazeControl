using System.Text;
using GazeControl.Study;
using GazeControl.Xr;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeControl.Experiment
{
    /// <summary>
    /// The participant's half of the questionnaire: a world-space panel that
    /// shows the screen they are being asked about, and takes no input at all.
    ///
    /// <para><b>Display-only, on purpose</b> (<c>questionnaire-ui-design.md</c>
    /// §0). The participant answers aloud and the operator types it, which
    /// removes the XR Interaction Toolkit, ray interactors and a controller path
    /// that has never been run under the Varjo loader — on hardware that has
    /// already produced one enumeration surprise. There is nothing here to point
    /// at, so there is nothing to go wrong in the headset.</para>
    ///
    /// <para><b>It never shows an answer back.</b> The panel marks which item is
    /// being asked, and stops there: reflecting the number the operator typed
    /// would put the participant's own last answer in front of them while they
    /// give the next one.</para>
    ///
    /// <para>The panel is built in code rather than kept as a prefab so that the
    /// wording has exactly one source — <c>Resources/Questionnaire.json</c>, the
    /// same file the operator panel reads. A prefab would be a second copy of
    /// the instrument, free to drift.</para>
    /// </summary>
    public sealed class QuestionnaireDisplay : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Panel size in metres, as the participant sees it at its distance")]
        public Vector2 PanelSize { get; set; } = new(1.2f, 0.8f);

        [field: SerializeField]
        [field: Tooltip("Height of the panel's centre above the floor. The participant's eye line, so reading it " +
                        "is the same gaze direction as watching the agents.")]
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
        [field: Tooltip("Panel background. Dark and opaque: a bright field at 1.5 m is tiring to read in a headset, " +
                        "and anything showing through it is one more thing to look at instead of the question.")]
        public Color Background { get; set; } = new(0.06f, 0.06f, 0.07f, 1f);

        [field: SerializeField]
        [field: Tooltip("Text colour")]
        public Color Foreground { get; set; } = new(0.94f, 0.94f, 0.96f, 1f);

        /// <summary>
        /// Canvas units to the metre. A world-space canvas is laid out in these
        /// and scaled down; 1000 keeps the numbers in the range UI text is
        /// designed for, so type sizes read like type sizes.
        /// </summary>
        const float k_UnitsPerMetre = 1000f;

        const float k_Margin = 70f;

        RectTransform _panel;
        TextMeshProUGUI _title;
        TextMeshProUGUI _body;
        TextMeshProUGUI _footer;

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
        /// </summary>
        public void ApplyPlacement()
        {
            transform.localPosition = new Vector3(0f, EyeHeight, Distance);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

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
                    Passage(definition.framing);
                    break;

                case QuestionnaireSession.Phase.Rating:
                    Rating(session, definition);
                    break;

                case QuestionnaireSession.Phase.Ranking:
                    Ranking(session, definition);
                    break;

                case QuestionnaireSession.Phase.Comment:
                    Comment(definition);
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

            SetVisible(true);
        }

        void Passage(QuestionnaireDefinition.Passage passage)
        {
            // Vertically centred, unlike the item lists: a passage is one block
            // of prose and reads better in the middle of the panel than pinned
            // to the line the items happen to start on.
            _body.alignment = TextAlignmentOptions.Left;
            _title.text = passage.title;
            _body.text = passage.body;
            _footer.text = string.Empty;
        }

        void Rating(QuestionnaireSession session, QuestionnaireDefinition definition)
        {
            var items = definition.perClipItems;
            var cursor = session.Entry != null ? session.Entry.Cursor : -1;

            var body = new StringBuilder();
            for (var i = 0; i < items.Length; i++)
            {
                // A plain '>' rather than a glyph like U+25B6: the panel runs on
                // the LiberationSans SDF font that ships with TMP, and a
                // character it has no glyph for renders as an empty box.
                body.Append(i == cursor ? ">   " : "    ");
                body.AppendLine(items[i].text);
                body.AppendLine();
            }

            _body.alignment = TextAlignmentOptions.TopLeft;
            _title.text = string.Empty;
            _body.text = body.ToString().TrimEnd();
            _footer.text = $"{ScaleLine(definition.scale)}\n{definition.spokenAnswerInstruction}";
        }

        void Ranking(QuestionnaireSession session, QuestionnaireDefinition definition)
        {
            var ranking = definition.ranking;
            var cursor = session.Entry != null ? session.Entry.Cursor : -1;
            var versions = session.Entry != null ? session.Entry.SlotCount : 0;

            var body = new StringBuilder();
            for (var position = 1; position <= versions; position++)
            {
                body.Append(position - 1 == cursor ? ">   " : "    ");
                body.AppendLine(ranking.LabelFor(position));
            }

            _body.alignment = TextAlignmentOptions.TopLeft;
            _title.text = ranking.prompt;
            _body.text = body.ToString().TrimEnd();
            _footer.text = $"{ranking.instruction}\n{definition.spokenAnswerInstruction}";
        }

        void Comment(QuestionnaireDefinition definition)
        {
            _body.alignment = TextAlignmentOptions.Left;
            _title.text = string.Empty;
            _body.text = definition.comment.prompt;
            _footer.text = $"{definition.comment.instruction}\n{definition.spokenAnswerInstruction}";
        }

        static string ScaleLine(QuestionnaireDefinition.Scale scale) =>
            $"{scale.min} = {scale.minLabel}     {(scale.min + scale.max) / 2} = {scale.midLabel}     " +
            $"{scale.max} = {scale.maxLabel}";

        void SetVisible(bool visible)
        {
            if (_panel != null)
                _panel.gameObject.SetActive(visible);
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

            canvasRect.sizeDelta = PanelSize * k_UnitsPerMetre;
            canvasRect.localScale = Vector3.one / k_UnitsPerMetre;

            _panel = NewChild("Panel", canvasRect);
            _panel.anchorMin = Vector2.zero;
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
        }

        TextMeshProUGUI NewText(
            string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax,
            float fontSize, TextAlignmentOptions alignment)
        {
            var rect = NewChild(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(k_Margin, k_Margin * 0.5f);
            rect.offsetMax = new Vector2(-k_Margin, -k_Margin * 0.5f);

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
