using System.Text;
using GazeControl.Conversation;
using UnityEngine;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Preview and debugging aid: draws the segment's end-of-turn schedule over
    /// the Game view and flashes each event's class as it fires, so a take can
    /// be watched knowing where the gaze patterns are meant to land.
    ///
    /// <para><b>Never show this to a study participant.</b> It names the
    /// boundaries the questionnaire asks people to judge, which would tell them
    /// what to look at and when. Three guards keep it out of a study run, and
    /// all three are deliberate rather than belt-and-braces:</para>
    /// <list type="bullet">
    /// <item><see cref="Enabled"/> defaults to <c>false</c>, so adding the
    /// component to a scene shows nothing until someone asks for it.</item>
    /// <item>It refuses to draw outside the editor, so a build handed to a
    /// participant cannot display it whatever the serialized value says.</item>
    /// <item><c>DemoRecorder</c> turns it off before recording, so it can never
    /// be burnt into a take.</item>
    /// </list>
    /// <para>It also logs once per session when it starts drawing, so a run
    /// that was not study-clean says so in the console rather than only in
    /// whoever-was-watching's memory.</para>
    /// </summary>
    public sealed class DeveloperOverlay : MonoBehaviour
    {
        /// <summary>How long an event stays highlighted after its turn instant, seconds.</summary>
        const float k_FlashSeconds = 1.5f;

        /// <summary>
        /// The pre-turn window the proposed condition overrides, seconds. Drawn
        /// as the "arming" state so the prototype's run is visible, not just the
        /// instant it ends on.
        /// </summary>
        const float k_PreTurnSeconds = 1f;

        [field: SerializeField]
        [field: Tooltip("Draw the overlay. Off by default, and ignored outside the editor — " +
                        "this must never be visible to a study participant.")]
        public bool Enabled { get; set; }

        [field: SerializeField]
        [field: Tooltip("The conversation whose schedule is drawn; found in the scene when left empty")]
        public RecordedConversation Conversation { get; set; }

        [field: SerializeField]
        [field: Tooltip("Read only to show which gaze policy is running; found in the scene when left empty")]
        public GazeConditionRunner Runner { get; set; }

        bool _warned;

        void Awake()
        {
            // Explicit == null, not ??=: Unity overloads == so a destroyed
            // object compares equal to null, while ??= tests the reference and
            // would keep a dead one.
            if (Conversation == null)
                Conversation = FindFirstObjectByType<RecordedConversation>();
            if (Runner == null)
                Runner = FindFirstObjectByType<GazeConditionRunner>();
        }

        /// <summary>True when the overlay may draw at all, whatever the inspector says.</summary>
        bool ShouldDraw => Enabled && Application.isEditor && Conversation != null && Conversation.Segment != null;

        void OnGUI()
        {
            if (!ShouldDraw)
                return;

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning(
                    $"{name}: developer overlay is ON — this run is for preview only and must not be " +
                    "shown to a study participant.", this);
            }

            var segment = Conversation.Segment;
            var elapsed = Conversation.Elapsed;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                richText = true,
                padding = new RectOffset(10, 10, 6, 6),
            };

            var body = new StringBuilder();
            body.AppendLine($"<b>DEVELOPER MODE</b>   t = {Mathf.Max(elapsed, 0f):F2} s / {segment.durationSeconds:F2} s");
            body.AppendLine($"{segment.name}   {segment.stem}" +
                            (Runner != null
                                ? $"   condition: <b>{Runner.Condition}</b>   seed: <b>{Runner.BaseSeed}</b>   tracks: {Runner.Tracks}"
                                : string.Empty));
            body.AppendLine();

            foreach (var e in segment.events)
            {
                var untilTurn = e.turnTime - elapsed;
                var state = StateOf(untilTurn);
                body.AppendLine(
                    $"{Colour(state)}{e.turnTime,7:F2}  {e.eotTypeName,-12} " +
                    $"{Speaker(e.firstSpeaker)} → {Speaker(e.secondSpeaker)}   {state}</color>");
            }

            var size = style.CalcSize(new GUIContent(body.ToString()));
            var rect = new Rect(12f, 12f, size.x + 20f, size.y + 12f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, body.ToString(), style);

            DrawBanner(segment, elapsed);
        }

        /// <summary>Big centred callout for the seconds around a boundary, so it is unmissable while watching.</summary>
        void DrawBanner(DemoSegment segment, float elapsed)
        {
            foreach (var e in segment.events)
            {
                var since = elapsed - e.turnTime;
                if (since < 0f || since > k_FlashSeconds)
                    continue;

                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 30,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true,
                };

                // Fade out over the flash so successive events stay distinct.
                var alpha = 1f - since / k_FlashSeconds;
                var hex = ColorUtility.ToHtmlStringRGBA(new Color(1f, 0.85f, 0.2f, alpha));
                var rect = new Rect(0f, Screen.height * 0.12f, Screen.width, 60f);
                GUI.Label(rect, $"<color=#{hex}><b>{e.eotTypeName.ToUpperInvariant()}</b>  " +
                                $"{Speaker(e.firstSpeaker)} → {Speaker(e.secondSpeaker)}</color>", style);
                return;
            }
        }

        static string StateOf(float untilTurn) => untilTurn switch
        {
            > k_PreTurnSeconds => "waiting",
            > 0f => "PATTERN",
            > -k_FlashSeconds => "NOW",
            _ => "done",
        };

        static string Colour(string state) => state switch
        {
            "PATTERN" => "<color=#7fd4ff>",
            "NOW" => "<color=#ffd633>",
            "done" => "<color=#808080>",
            _ => "<color=#c8c8c8>",
        };

        static string Speaker(int code) => code switch
        {
            DemoSegment.UserSpeakerCode => "User",
            1 => "A",
            2 => "B",
            _ => code.ToString(),
        };
    }
}
