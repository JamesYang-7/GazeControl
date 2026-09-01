using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Help: what every command on this menu does, and which of
    /// them a participant session actually needs.
    ///
    /// <para>The menu had grown to eleven commands across three submenus, some of
    /// which are one-off asset repairs and one of which starts a recording with a
    /// real person in a headset. Nothing on it said which was which, and the
    /// answers were spread across CLAUDE.md, four design documents and the
    /// scripts' own summaries. This is the index: one screen, in the place the
    /// commands are.</para>
    ///
    /// <para>Deliberately not a duplicate of the runbook. This says what each
    /// command <i>is</i>; <c>Assets/Docs/session-runbook.md</c> says what the
    /// operator <i>does</i>, in order, on the day.</para>
    /// </summary>
    public sealed class GazeControlHelp : EditorWindow
    {
        const string k_Runbook = "Assets/Docs/session-runbook.md";

        /// <summary>One entry: a command or a key, what it does, and how to open it.</summary>
        readonly struct Entry
        {
            public Entry(string name, string body, string menuPath = null)
            {
                Name = name;
                Body = body;
                MenuPath = menuPath;
            }

            public string Name { get; }
            public string Body { get; }

            /// <summary>The menu command to run from the Open button, or null for no button.</summary>
            public string MenuPath { get; }
        }

        static readonly (string Heading, string Blurb, Entry[] Entries)[] Sections =
        {
            ("Running a session", "The only part with a participant in the room. Start Session is the " +
                                  "whole procedure; everything else here is preparation.", new[]
            {
                new Entry("Study → Start Session",
                    "Starts a real recording. Proposes the next participant label by reading the folders " +
                    "under Recordings/ — it is never typed — refuses one that has already run, and shows a " +
                    "preflight of every rule the session will be held to.\n\n" +
                    "Rows marked !! block the launch and must be fixed. Rows marked → are set for you when " +
                    "the session starts: study mode, replayed tracks, logging, the session runner, the " +
                    "headset switch and the developer overlay. They are applied to the play session only, " +
                    "so the scene on disk is never modified and there is nothing to put back afterwards.\n\n" +
                    "Back / Next step the label, for a correction only. Once play starts the window shows " +
                    "the clip position and whether the headset's eye tracking is available and calibrated.",
                    "GazeControl/Study/Start Session"),

                new Entry("In play: Space",
                    "The only key the operator needs. It starts the session, and after each clip it " +
                    "releases the next one. It also skips a running clip — but a study session refuses the " +
                    "skip, because the same key advances and a mistimed press would leave a take that " +
                    "looks complete and is not."),

                new Entry("In play: the operator panel",
                    "Drawn into the desktop window, never into the headset. 1-7 answers each rating item " +
                    "in turn, Backspace undoes, Enter commits the screen and releases the next clip. The " +
                    "ranking takes 1-3 and refuses ties; the comment field commits with Ctrl+Enter. The " +
                    "panel is blinded: it shows 'Conversation 3 · Version 2 of 3' and never the method."),
            }),

            ("Preparing the clips", "Both take real time. Do them the day before, not with someone waiting.", new[]
            {
                new Entry("Study → Clip Browser",
                    "Lists every exported segment with its duration, events, measured voices and " +
                    "transcript, so the study's clips can be chosen by reading rather than by score.\n\n" +
                    "Load into scene / Load and Play preview one. Match textures to voices applies the " +
                    "clip's own voice measurement as a scene edit (a session does this itself at load). " +
                    "Scan seeds reports how much each agent averts across twelve seeds, so a clip is judged " +
                    "on the clip and not on an unlucky draw. Bake all 3 records the baked gaze track for " +
                    "each condition — one play session each, and a study session replays these files.\n\n" +
                    "Developer mode marks the end-of-turn boundaries on screen. Preview only: it names the " +
                    "very thing the questionnaire asks participants to judge, and a session refuses to " +
                    "start with it on.",
                    "GazeControl/Study/Clip Browser"),

                new Entry("Record Demo",
                    "Enters play, records the Game view with audio to Recordings/<case>/*.mp4, stops " +
                    "shortly after the segment ends and leaves play. The take's length is the segment's. " +
                    "For demo videos, not for participants."),
            }),

            ("Scene setup", "Idempotent. Each builds its part of the scene, and re-running one repairs it " +
                            "after a refactor rather than anyone hand-editing scene YAML.", new[]
            {
                new Entry("Set Up Gaze Conditions",
                    "Wires the experiment machinery: a participant component on each triad member, the " +
                    "recorded conversation, and the condition runner that drives them.",
                    "GazeControl/Set Up Gaze Conditions"),

                new Entry("Set Up XR Rig",
                    "Builds or repairs the participant rig — the triad vertex, the eye-height offset and " +
                    "the tracked camera — and the participant gaze logger on it. The camera sits at the " +
                    "agents' own eye line and tracks rotation only, so every participant sees the triad " +
                    "from the identical point. It warns if the vertex has drifted off facing the agents' " +
                    "midpoint.",
                    "GazeControl/Set Up XR Rig"),

                new Entry("Set Up Questionnaire",
                    "Builds both questionnaire surfaces: the display-only canvas on the participant's own " +
                    "vertex, and the operator's entry panel. A study session refuses to start without one.",
                    "GazeControl/Set Up Questionnaire"),
            }),

            ("Asset repair", "One-off, on an imported asset. Not part of running anything.", new[]
            {
                new Entry("Textures → Composite SMPLitex Eye Disc",
                    "SMPLitex never generates the eyeball UV island and leaves it black, and both eyes " +
                    "share it — so an untreated texture gives an agent black eyes. This copies the disc " +
                    "from a stock SMPL-X albedo."),

                new Entry("Textures → Pad SMPLitex UV Islands",
                    "Fills the gaps between islands from their edges, using the mesh's own UVs as the mask " +
                    "so nothing a triangle covers is ever written."),

                new Entry("Visemes → Open Jaw / Close Jaw",
                    "Drives the 'aa' viseme in Edit Mode, to see inside the mouth bag without entering " +
                    "play."),
            }),
        };

        Vector2 _scroll;

        [MenuItem("GazeControl/Help", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<GazeControlHelp>("GazeControl Help");
            window.minSize = new Vector2(520f, 480f);
        }

        void OnGUI()
        {
            using var scroll = new EditorGUILayout.ScrollViewScope(_scroll);
            _scroll = scroll.scrollPosition;

            EditorGUILayout.LabelField("What the GazeControl menu does", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The operator's step-by-step procedure for a participant session — including the parts no " +
                "menu command can do, like the headset's per-wearer eye-tracking calibration — is in " +
                k_Runbook + ".",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("Open the session runbook", GUILayout.Width(200f)))
                OpenRunbook();

            foreach (var (heading, blurb, entries) in Sections)
            {
                EditorGUILayout.Space(12f);
                EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(blurb, EditorStyles.wordWrappedMiniLabel);

                foreach (var entry in entries)
                    DrawEntry(entry);
            }

            EditorGUILayout.Space(12f);
        }

        static void DrawEntry(Entry entry)
        {
            EditorGUILayout.Space(6f);
            using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel);

                if (entry.MenuPath != null && GUILayout.Button("Open", GUILayout.Width(60f)))
                {
                    // Deferred: running a menu command that opens a window from
                    // inside this window's own OnGUI leaves the layout mid-group.
                    var path = entry.MenuPath;
                    EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem(path);
                }
            }

            EditorGUILayout.LabelField(entry.Body, EditorStyles.wordWrappedLabel);
        }

        static void OpenRunbook()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(k_Runbook);
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
                return;
            }

            Debug.LogWarning($"GazeControl Help: {k_Runbook} is missing.");
        }
    }
}
