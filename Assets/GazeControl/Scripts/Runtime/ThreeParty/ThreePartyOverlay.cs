using UnityEngine;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// What the replay cannot show by itself: who each body is, what they are
    /// saying, and where the annotated turn boundaries fall.
    ///
    /// <para>All three are the point of watching an original session. The bodies
    /// alone cannot say which of the three is PC 2, a caption is the only way to
    /// catch the microphone bleed that puts a phantom line — and so a phantom
    /// event — on the wrong track, and an event mark is what makes a candidate
    /// window judgeable rather than merely watchable.</para>
    ///
    /// <para>IMGUI on purpose. This never goes near a participant or a headset —
    /// it is an operator surface on the desktop window, and a canvas would be
    /// three prefabs and a font asset to say the same thing.</para>
    /// </summary>
    [RequireComponent(typeof(ThreePartyReplay))]
    public sealed class ThreePartyOverlay : MonoBehaviour
    {
        /// <summary>One colour per seat, so a label, a caption row and an event mark agree.</summary>
        static readonly Color[] k_SeatColours =
        {
            new(0.42f, 0.75f, 1f),
            new(1f, 0.72f, 0.36f),
            new(0.55f, 0.9f, 0.5f),
        };

        [field: SerializeField]
        [field: Tooltip("Draw the name plates, captions and event strip")]
        public bool Enabled { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("How far above the head bone the name plate floats, metres")]
        public float LabelHeight { get; set; } = 0.28f;

        [field: SerializeField]
        [field: Tooltip("An event is called out on screen for this long either side of its turn instant, seconds")]
        public float EventFlashSeconds { get; set; } = 0.75f;

        ThreePartyReplay _replay;
        GUIStyle _plate;
        GUIStyle _caption;
        GUIStyle _heading;
        Texture2D _pixel;

        void Awake()
        {
            _replay = GetComponent<ThreePartyReplay>();
            _pixel = new Texture2D(1, 1);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
        }

        void OnDestroy()
        {
            if (_pixel != null)
                Destroy(_pixel);
        }

        void OnGUI()
        {
            if (!Enabled || _replay.Seats == null)
                return;

            EnsureStyles();
            DrawHeading();
            DrawNamePlates();
            DrawCaptions();
            DrawEventStrip();
        }

        void EnsureStyles()
        {
            _plate ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold,
            };

            _caption ??= new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = false };
            _heading ??= new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        }

        void DrawHeading()
        {
            var elapsed = Mathf.Max(_replay.Elapsed, 0f);
            var view = _replay.ViewFromListenerSeat && _replay.HasViewpoint
                ? "   [listener's seat — V for the orbit camera]"
                : "   [V to stand in the listener's seat]";

            var text = _replay.WindowSeconds > 0f
                ? $"{_replay.SessionLabel}   {_replay.SessionSeconds:F1} s   " +
                  $"(window {_replay.StartSeconds:F1}-{_replay.StartSeconds + _replay.WindowSeconds:F1} s, " +
                  $"{elapsed:F1}/{_replay.WindowSeconds:F1}){view}"
                : $"{_replay.SessionLabel}   loading";

            Fill(new Rect(0f, 0f, Screen.width, 26f), new Color(0f, 0f, 0f, 0.55f));
            GUI.color = Color.white;
            GUI.Label(new Rect(12f, 4f, Screen.width - 24f, 20f), text, _heading);
        }

        void DrawNamePlates()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            for (var i = 0; i < _replay.Seats.Length; i++)
            {
                var seat = _replay.Seats[i];
                if (seat.Head == null || seat.Clip == null)
                    continue;

                // No plate over a body that is not being drawn, and none over
                // the viewer's own seat.
                if (i == _replay.ListenerSeat && _replay.ViewFromListenerSeat)
                    continue;

                var world = seat.Head.position + Vector3.up * LabelHeight;
                var screen = camera.WorldToScreenPoint(world);
                if (screen.z <= 0f)
                    continue; // behind the camera: WorldToScreenPoint mirrors it to the front

                var rect = new Rect(screen.x - 90f, Screen.height - screen.y - 12f, 180f, 22f);
                Fill(rect, new Color(0f, 0f, 0f, 0.5f));
                GUI.color = k_SeatColours[i % k_SeatColours.Length];
                GUI.Label(rect, seat.Clip.Label, _plate);
                GUI.color = Color.white;
            }
        }

        void DrawCaptions()
        {
            const float rowHeight = 24f;
            var height = rowHeight * _replay.Seats.Length + 10f;
            var top = Screen.height - height - 46f;
            Fill(new Rect(0f, top, Screen.width, height), new Color(0f, 0f, 0f, 0.55f));

            for (var i = 0; i < _replay.Seats.Length; i++)
            {
                var seat = _replay.Seats[i];
                if (seat.Clip == null)
                    continue;

                var text = _replay.CaptionOf(i);
                var colour = k_SeatColours[i % k_SeatColours.Length];

                // Dimmed rather than hidden while a participant is silent: an
                // empty row still says which of the three is not talking, and a
                // row appearing and vanishing is harder to read than one fading.
                GUI.color = string.IsNullOrEmpty(text) ? colour * 0.45f : colour;
                var who = i == _replay.ListenerSeat ? $"{seat.Clip.Label} (listener)" : seat.Clip.Label;
                GUI.Label(
                    new Rect(12f, top + 5f + i * rowHeight, Screen.width - 24f, rowHeight),
                    $"{who}:  {text}", _caption);
            }

            GUI.color = Color.white;
        }

        void DrawEventStrip()
        {
            var window = _replay.WindowSeconds;
            if (window <= 0f)
                return;

            var strip = new Rect(12f, Screen.height - 34f, Screen.width - 24f, 22f);
            Fill(strip, new Color(0f, 0f, 0f, 0.55f));

            var start = _replay.StartSeconds;
            foreach (var annotated in _replay.Events)
            {
                var offset = (annotated.TurnSeconds - start) / window;
                if (offset is < 0f or > 1f)
                    continue;

                Fill(new Rect(strip.x + offset * strip.width - 1f, strip.y, 3f, strip.height),
                    EventColour(annotated.EotType));
            }

            var head = Mathf.Clamp01(Mathf.Max(_replay.Elapsed, 0f) / window);
            Fill(new Rect(strip.x + head * strip.width - 1f, strip.y - 3f, 2f, strip.height + 6f), Color.white);

            DrawEventCallout();
        }

        void DrawEventCallout()
        {
            var now = _replay.SessionSeconds;
            foreach (var annotated in _replay.Events)
            {
                if (Mathf.Abs(annotated.TurnSeconds - now) > EventFlashSeconds)
                    continue;

                var rect = new Rect(Screen.width * 0.5f - 170f, 34f, 340f, 24f);
                Fill(rect, new Color(0f, 0f, 0f, 0.6f));
                GUI.color = EventColour(annotated.EotType);
                GUI.Label(rect,
                    $"{annotated.TypeName}: PC {annotated.FirstSpeaker} → PC {annotated.SecondSpeaker}",
                    _plate);
                GUI.color = Color.white;
                return;
            }
        }

        static Color EventColour(int eotType) => eotType switch
        {
            1 => new Color(1f, 0.45f, 0.4f),   // interruption
            2 => new Color(1f, 0.85f, 0.35f),  // overlapping
            _ => new Color(0.5f, 1f, 0.6f),    // turn-taking
        };

        void Fill(Rect rect, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(rect, _pixel);
            GUI.color = Color.white;
        }
    }
}
