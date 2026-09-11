using System.IO;
using GazeControl.Conversation;
using GazeControl.Demo;
using GazeControl.Experiment;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// <c>GazeControl → Demo Video → Camera Framing</c>: the five numbers that
    /// decide the shot, with a readout of what they actually cover.
    ///
    /// <para><b>Tuned in Play Mode, against a clip that is running.</b> In Edit
    /// Mode the agents stand in the bind pose at the room's origin, which is
    /// enough to rough a shot in and no use at all for judging one; pick a clip
    /// below and play it instead. That is why the numbers live in an asset — a
    /// scene field dragged in Play Mode is discarded the moment Play Mode
    /// ends.</para>
    ///
    /// <para>It loads the clip itself rather than sending anyone to a browser:
    /// study 1's Clip Browser came off the menu with the rest of study 1, and
    /// study 2's clips are read out of the 3People corpus by a different window
    /// entirely. The list here is the session runner's own clip order, which is
    /// also what gets rendered.</para>
    ///
    /// <para>The readout is in metres rather than in pixels, because that is what
    /// can be checked against the world: how far the agents are, what heights the
    /// frame's top and bottom edges cross at that distance, and how much room is
    /// left above their heads and beyond their shoulders. A picture in a window
    /// would show the same thing less precisely, and the Game view already shows
    /// the picture.</para>
    /// </summary>
    public sealed class DemoCameraFramingWindow : EditorWindow
    {
        /// <summary>The aspect a demo take is rendered at, and the one the readout assumes.</summary>
        const float OutputAspect = 16f / 9f;

        [SerializeField] Vector2 _scroll;
        [SerializeField] int _clip;

        SerializedObject _framing;
        DemoCamera _camera;

        [MenuItem("GazeControl/Demo Video/Camera Framing")]
        public static void Open()
        {
            var window = GetWindow<DemoCameraFramingWindow>("Demo Framing");
            window.minSize = new Vector2(430f, 340f);
            window.Rebind();
        }

        void OnEnable()
        {
            Rebind();
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        void OnDisable() => EditorApplication.playModeStateChanged -= PlayModeChanged;

        // Entering and leaving play mode replaces every scene object, so the
        // camera this window holds is a stale reference from then on.
        void PlayModeChanged(PlayModeStateChange _) => Rebind();

        void OnInspectorUpdate() => Repaint(); // the readout tracks a clip that is playing

        void Rebind()
        {
            _camera = DemoCameraSetup.Find();
            var asset = _camera != null && _camera.Framing != null
                ? _camera.Framing
                : AssetDatabase.LoadAssetAtPath<DemoCameraFraming>(DemoCameraSetup.FramingAssetPath);

            _framing = asset != null ? new SerializedObject(asset) : null;
        }

        void OnGUI()
        {
            if (_camera == null || _framing == null)
            {
                DrawMissing();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawFraming();
            EditorGUILayout.Space();
            DrawCoverage();
            EditorGUILayout.Space();
            DrawActions();
            EditorGUILayout.EndScrollView();
        }

        void DrawMissing()
        {
            EditorGUILayout.HelpBox(
                "This scene has no demo camera yet. It is a second camera beside the participant's, " +
                "pulled back and tilted down so that a flat 16:9 frame holds both agents' bodies; " +
                "the participant's own camera is left exactly where it is, because the agents aim at it.",
                MessageType.Info);

            if (GUILayout.Button("Set Up Demo Camera"))
            {
                DemoCameraSetup.SetUp();
                Rebind();
            }
        }

        void DrawFraming()
        {
            EditorGUILayout.LabelField("The shot", EditorStyles.boldLabel);

            _framing.Update();
            EditorGUI.BeginChangeCheck();

            Field(nameof(DemoCameraFraming.PullBackMetres), "Pull back (m)");
            Field(nameof(DemoCameraFraming.HeightMetres), "Height (m)");
            Field(nameof(DemoCameraFraming.PitchDegrees), "Tilt down (deg)");
            Field(nameof(DemoCameraFraming.VerticalFieldOfView), "Vertical FOV (deg)");
            Field(nameof(DemoCameraFraming.LateralOffsetMetres), "Sideways (m)");

            if (EditorGUI.EndChangeCheck())
            {
                _framing.ApplyModifiedProperties();
                _camera.Apply();
                SceneView.RepaintAll();
            }

            // Play Mode edits an asset rather than a scene object, so they survive
            // leaving Play Mode — but only once they are on disk. Written when the
            // slider is let go rather than on every frame of a drag.
            if (Event.current.type is EventType.MouseUp or EventType.KeyUp &&
                EditorUtility.IsDirty(_framing.targetObject))
            {
                AssetDatabase.SaveAssets();
            }

            var framing = (DemoCameraFraming)_framing.targetObject;
            EditorGUILayout.LabelField(
                " ",
                $"{DemoCameraFrame.HorizontalFieldOfView(framing.VerticalFieldOfView, OutputAspect):F1}° across a 16:9 frame",
                EditorStyles.miniLabel);
        }

        /// <summary>Draw one of the asset's fields under a shorter label than its own name.</summary>
        void Field(string propertyName, string label) =>
            EditorGUILayout.PropertyField(
                _framing.FindProperty($"<{propertyName}>k__BackingField"), new GUIContent(label));

        void DrawCoverage()
        {
            EditorGUILayout.LabelField("What it covers", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "The agents are in the bind pose until a clip runs. Pick one below and play it, " +
                    "then tune against the real thing.",
                    MessageType.None);
            }

            var agents = Object.FindObjectsByType<GazeParticipant>(FindObjectsSortMode.None);
            var reported = 0;

            foreach (var agent in agents)
            {
                if (agent.IsHuman || agent.LookAtAnchor == null)
                    continue;

                DrawAgentCoverage(agent);
                reported++;
            }

            if (reported == 0)
                EditorGUILayout.LabelField("No agents in this scene to measure against.", EditorStyles.miniLabel);

            WarnAboutGameViewAspect();
        }

        void DrawAgentCoverage(GazeParticipant agent)
        {
            var head = agent.LookAtAnchor.position;
            var distance = _camera.HorizontalDistanceTo(head);
            var span = _camera.SpanAt(head);

            var lens = _camera.transform;
            var local = lens.InverseTransformPoint(head);
            var halfWidth = local.z > 0f
                ? local.z * Mathf.Tan(
                    DemoCameraFrame.HorizontalFieldOfView(((DemoCameraFraming)_framing.targetObject).VerticalFieldOfView,
                        OutputAspect) * 0.5f * Mathf.Deg2Rad)
                : 0f;
            var sideMargin = halfWidth - Mathf.Abs(local.x);

            EditorGUILayout.LabelField(
                agent.DisplayName,
                $"{distance:F2} m away · frame covers {span.Bottom:F2}–{span.Top:F2} m");
            EditorGUILayout.LabelField(
                " ",
                $"head at {head.y:F2} m · {span.Top - head.y:F2} m of headroom · " +
                $"{sideMargin:F2} m to the side edge",
                span.Contains(head.y) && sideMargin > 0f ? EditorStyles.miniLabel : Warning);
        }

        /// <summary>
        /// A demo take is rendered at 16:9 whatever shape the Game view happens to
        /// be, so a Game view at some other aspect shows a framing that will not
        /// be the one recorded. Only checkable while playing, where the camera
        /// carries the view's real aspect.
        /// </summary>
        void WarnAboutGameViewAspect()
        {
            if (!Application.isPlaying || _camera.Camera == null)
                return;

            var aspect = _camera.Camera.aspect;
            if (Mathf.Abs(aspect - OutputAspect) < 0.02f)
                return;

            EditorGUILayout.HelpBox(
                $"The Game view is {aspect:F2}:1, and a take is rendered at 16:9 ({OutputAspect:F2}:1). " +
                "Set the Game view's aspect to 16:9 or this framing is not the one you will get.",
                MessageType.Warning);
        }

        void DrawActions()
        {
            EditorGUILayout.LabelField("Watch it", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                var showing = _camera.Camera != null && _camera.Camera.enabled;
                var wanted = EditorGUILayout.ToggleLeft(
                    "Show the demo view in the Game view (Edit Mode)", showing);

                if (wanted != showing && _camera.Camera != null)
                {
                    Undo.RecordObject(_camera.Camera, "Show demo view");
                    _camera.Camera.enabled = wanted;
                    EditorUtility.SetDirty(_camera.Camera);
                }
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawClipRow();
            }

            if (GUILayout.Button("Save the framing"))
                AssetDatabase.SaveAssets();

            if (GUILayout.Button("Save a preview frame"))
                CapturePreviewFrame();

            EditorGUILayout.LabelField(
                "Playing from here suspends the session runner and the headset for that play session and " +
                "puts both back afterwards — with the session runner live, a plain Play is a participant " +
                "session waiting on a framing screen, not a clip. Pressing Play by hand is an ordinary run: " +
                "the demo camera takes its request once and never renders a session it was not asked to.",
                EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// Pick one of the session's clips and play it through the demo camera.
        ///
        /// <para>The clip is loaded the way a bake loads one — the conversation's
        /// segment path and the runner's case name — rather than left to whatever
        /// the scene was last saved on, which is how a framing session comes to
        /// be tuned against a clip nobody is looking at.</para>
        /// </summary>
        void DrawClipRow()
        {
            var session = FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var conversation = FindFirstObjectByType<RecordedConversation>();

            if (session?.Conversations == null || session.Conversations.Length == 0 || conversation == null)
            {
                EditorGUILayout.LabelField(
                    "No session runner with clips in this scene, so there is nothing to play.",
                    EditorStyles.miniLabel);
                return;
            }

            var clips = session.Conversations;
            _clip = Mathf.Clamp(_clip, 0, clips.Length - 1);

            using (new EditorGUILayout.HorizontalScope())
            {
                _clip = EditorGUILayout.Popup(_clip, clips, GUILayout.Width(160f));

                if (GUILayout.Button("Play it through the demo camera"))
                    Play(session, conversation, clips[_clip]);
            }
        }

        void Play(StudySessionRunner session, RecordedConversation conversation, string clip)
        {
            var segment = $"{session.SegmentRoot}/{clip}/segment.json";
            if (!File.Exists(segment))
            {
                Debug.LogError($"Demo framing: {segment} does not exist; export the segment first.");
                return;
            }

            Undo.RecordObject(conversation, "Load clip for framing");
            conversation.SegmentPath = segment;
            EditorUtility.SetDirty(conversation);

            var runner = FindFirstObjectByType<GazeConditionRunner>();
            if (runner != null && runner.CaseName != clip)
            {
                Undo.RecordObject(runner, "Load clip for framing");
                runner.CaseName = clip;
                EditorUtility.SetDirty(runner);
            }

            AssetDatabase.SaveAssets();
            DemoPlaySession.Start();
        }

        /// <summary>Where a captured frame goes, for the caption preview to read.</summary>
        const string PreviewFramePath = "output/demo_preview_frame.png";

        /// <summary>
        /// Write one frame of exactly what a take will be, so the titles and the
        /// subtitles can be checked before anything is rendered.
        ///
        /// <para>Rendered into a 1920x1080 texture rather than grabbed from the
        /// Game view, which can be any shape at all — the whole point is to see
        /// the frame the recorder will write. Press it while a clip is playing;
        /// pressed in Edit Mode it captures the bind pose, which says nothing
        /// about the shot.</para>
        /// </summary>
        void CapturePreviewFrame()
        {
            var camera = _camera.Camera;
            var texture = new RenderTexture(
                DemoTakeRenderer.OutputWidth, DemoTakeRenderer.OutputHeight, 24, RenderTextureFormat.ARGB32);

            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D image = null;

            try
            {
                // Render() draws on demand whether or not the camera is enabled,
                // which is what lets this work with the demo camera switched off.
                camera.targetTexture = texture;
                camera.Render();

                RenderTexture.active = texture;
                image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, mipChain: false);
                image.ReadPixels(new Rect(0f, 0f, texture.width, texture.height), 0, 0);
                image.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(PreviewFramePath)!);
                File.WriteAllBytes(PreviewFramePath, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (image != null)
                    DestroyImmediate(image);
                texture.Release();
                DestroyImmediate(texture);
            }

            var clip = FindFirstObjectByType<RecordedConversation>();
            var clipName = clip != null && clip.Segment != null ? clip.Segment.name : "<clip>";
            var at = clip != null && clip.Elapsed >= 0f ? clip.Elapsed : 0f;

            Debug.Log(
                $"Demo framing: wrote {PreviewFramePath} ({DemoTakeRenderer.OutputWidth}x" +
                $"{DemoTakeRenderer.OutputHeight}). Caption it with\n" +
                $"  uv run python Tools/compose_demo_video.py --preview --clip {clipName} " +
                $"--method Proposed --at {at:F2}");
        }

        static GUIStyle s_warning;

        /// <summary>The miniature label in the colour a number out of frame is reported in.</summary>
        static GUIStyle Warning
        {
            get
            {
                if (s_warning != null)
                    return s_warning;

                s_warning = new GUIStyle(EditorStyles.miniLabel);
                s_warning.normal.textColor = new Color(0.85f, 0.45f, 0.2f);
                return s_warning;
            }
        }
    }
}
