using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Help: what every command on this menu does, and which of
    /// them a participant session actually needs.
    ///
    /// <para>The menu had grown to thirteen commands across four submenus, some of
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
                                  "whole procedure; everything else here is preparation. The Study 1 and " +
                                  "Study 2 menus carry the same commands and differ only in the scene they " +
                                  "open first — TriadScene or Study2Scene — so a session cannot be started " +
                                  "in the wrong one.", new[]
            {
                new Entry("Study 1 → Start Session  (Study 2 → Start Session)",
                    "Starts a real recording. Proposes the next participant label by reading the folders " +
                    "under Recordings/ — it is never typed — refuses one that has already run, and shows a " +
                    "preflight of every rule the session will be held to.\n\n" +
                    "Rows marked !! block the launch and must be fixed. Rows marked → are set for you when " +
                    "the session starts: study mode, replayed tracks, logging, the session runner, the " +
                    "headset switch and the developer overlay. They are applied to the play session only, " +
                    "so the scene on disk is never modified and there is nothing to put back afterwards.\n\n" +
                    "Back / Next step the label, for a correction only. Once play starts the window shows " +
                    "the clip position and whether the headset's eye tracking is available and calibrated.",
                    "GazeControl/Study 1/Start Session"),

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
                new Entry("Clip Browser",
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
                    "GazeControl/Clip Browser"),

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

            ("Looking at source material", "A different corpus and a different purpose: nothing here is " +
                                           "measured, recorded or shown to a participant.", new[]
            {
                new Entry("3People → Set Up Replay Scene",
                    "Builds Assets/Scenes/ThreePartyReplay.unity, where a whole 3People-2022 session plays " +
                    "with all three participants at once — their converted SMPL-X motion on the capture's " +
                    "own room frame, their own microphone tracks and their own transcripts.\n\n" +
                    "Drag to turn the camera, scroll to close in, middle-drag to slide the pivot. The " +
                    "overlay names each body, shows what they are saying and marks the annotated " +
                    "end-of-turn events.\n\n" +
                    "Its own scene on purpose: this camera orbits, and the study's is fixed because it is " +
                    "a controlled variable.",
                    "GazeControl/3People/Set Up Replay Scene"),

                new Entry("3People → Session Browser",
                    "Picks the session and the stretch of it to watch, and reads it out as a transcript " +
                    "with the end-of-turn events marked before anything is played — which is what makes a " +
                    "candidate window judgeable rather than merely watchable, and the only way to catch " +
                    "the microphone bleed that puts a phantom line, and so a phantom event, on the wrong " +
                    "participant's track.\n\n" +
                    "The ranking written by Tools/find_3people_segments.py is listed as a starting point; " +
                    "Play whole session ignores it, which is what watching the originals means.",
                    "GazeControl/3People/Session Browser"),
            }),

            ("Study 2 (3People clips)", "The seven 3People-2022 windows played through study 1's pipeline. " +
                                        "The scene moves the recorded room onto the participant's starting viewpoint " +
                                        "per clip; everything else is study 1's.", new[]
            {
                new Entry("Study 2 → Set Up Scene",
                    "Builds (or repairs) Assets/Scenes/Study2Scene.unity as a copy of TriadScene whose two " +
                    "agents sit under a Room object placed by each clip: the listener's measured seat lands on " +
                    "the User vertex facing its forward axis. Lists study2_c1-c7 on the session runner.\n\n" +
                    "Export the clips first: uv run python Tools/export_3people_segments.py.",
                    "GazeControl/Study 2/Set Up Scene"),

                new Entry("Study 2 → Bake Seeds 1-12",
                    "Every condition at seeds 1-12 for every conversation the session runner lists — 252 " +
                    "play sessions for seven clips, about two hours. Each track is checked as its play ends " +
                    "and re-baked if short; a report lands in output/. Then choose one seed per clip: " +
                    "uv run python Tools/choose_study2_seeds.py applies the rule (each turn-taking prototype " +
                    "once, non-degenerate bakes) and writes output/study2_seeds.json.",
                    "GazeControl/Study 2/Bake Seeds 1-12"),

                new Entry("Study 2 → Apply Chosen Seeds",
                    "Writes the clip order and seeds from output/study2_seeds.json onto the session runner " +
                    "and saves Study2Scene. Transcribes the script's choice rather than typing seven numbers " +
                    "into the inspector, so the scene never carries a seed whose reason is not on disk.",
                    "GazeControl/Study 2/Apply Chosen Seeds"),

                new Entry("Study 2 → Start Session",
                    "The Start Session window with Study2Scene open. Same window, same rules; the roster " +
                    "reads Recordings/Study_02, so labels start again at P01.",
                    "GazeControl/Study 2/Start Session"),

                new Entry("Study 2 → Preview Session  (Study 1 → Preview Session)",
                    "Plays the session's clip order with the baked tracks replayed and nothing else: no " +
                    "questionnaire, no logs, headset off, developer overlay on with the condition and seed. " +
                    "Each clip runs straight into the next, and Space skips the current one. For checking " +
                    "bakes by eye on the desktop; nothing it shows is a take.",
                    "GazeControl/Study 2/Preview Session"),

                new Entry("Study 2 → Preview Session in Headset  (Study 1 → Preview Session in Headset)",
                    "The same preview with the headset started, to watch the bakes from the participant's " +
                    "seat. Put the headset on before pressing it: with the runtime up and nobody wearing it, " +
                    "the frame clock waits on the compositor. Audio stays on the desktop output.",
                    "GazeControl/Study 2/Preview Session in Headset"),
            }),

            ("When something is wrong", "", new[]
            {
                new Entry("Reset Audio",
                    "Re-initialises the editor's audio engine on Windows' current default output. Use it " +
                    "when the agents move and nothing is heard, or a clip never starts: after a headset is " +
                    "unplugged the engine stays bound to the device that has gone, even though Windows shows " +
                    "Unity playing on the monitor. Then start the clip again.",
                    "GazeControl/Reset Audio"),
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
