using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Gaze.Policy;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Lists every segment exported under Assets/DemoSegments/, shows what it
    /// contains, and loads one into the open scene so it can be played.
    ///
    /// Built for choosing the study's five scene-1 clips by eye. The candidate
    /// set is far larger than the study needs, and the ranking in
    /// Tools/find_demo_segments.py scores timing only — nothing in it reads the
    /// words — so the final pick has to be made by watching and reading. The
    /// transcript is shown beside the metadata for exactly that reason.
    ///
    /// <para><b>Study 1's, and off the menu with the rest of study 1</b> (user's
    /// call, 2026-09-10). It was at the menu root as a shared command, and it is
    /// not shared: study 2's clips are chosen from the 3People corpus through
    /// <c>GazeControl → 3People → Session Browser</c>, which reads a window out
    /// as a transcript before anything plays. Restore its
    /// <c>[MenuItem("GazeControl/Study 1/Clip Browser")]</c> if study 1's clips
    /// are ever picked over again.</para>
    /// </summary>
    public sealed class StudyClipBrowser : EditorWindow
    {
        const string k_SegmentRoot = "Assets/DemoSegments";
        const float k_ListWidth = 260f;

        /// <summary>Seeds reported by the scan; enough to see the spread without a long wait.</summary>
        const int k_SeedScanCount = 12;

        /// <summary>
        /// What the scan sweeps, in report order. Baseline B is absent because it
        /// carries no random stream — one recording of it really is the condition.
        /// </summary>
        static readonly GazeConditionRunner.GazeCondition[] k_StochasticConditions =
        {
            GazeConditionRunner.GazeCondition.Proposed,
            GazeConditionRunner.GazeCondition.RoleConditioned,
        };

        /// <summary>One row: the parsed segment plus what the list needs to draw it.</summary>
        sealed class Entry
        {
            public string Name;
            public string Path;
            public DemoSegment Segment;
            public string Summary;
            public string Voices;
            public string LoadError;
        }

        readonly List<Entry> _entries = new();
        Entry _selected;
        Vector2 _listScroll;
        Vector2 _detailScroll;

        /// <inheritdoc cref="StudyClipBrowser"/>
        public static void Open()
        {
            var window = GetWindow<StudyClipBrowser>("Clip Browser");
            window.minSize = new Vector2(760f, 420f);
        }

        void OnEnable() => Rescan();

        void Rescan()
        {
            _entries.Clear();
            var selectedName = _selected?.Name;
            _selected = null;

            if (!Directory.Exists(k_SegmentRoot))
                return;

            foreach (var directory in Directory.GetDirectories(k_SegmentRoot).OrderBy(d => d))
            {
                var path = Path.Combine(directory, "segment.json").Replace('\\', '/');
                if (!File.Exists(path))
                    continue;

                var entry = new Entry { Name = Path.GetFileName(directory), Path = path };
                try
                {
                    entry.Segment = DemoSegment.Load(path);
                    entry.Summary = Describe(entry.Segment);
                    entry.Voices = DescribeVoices(entry.Segment);
                }
                catch (Exception e)
                {
                    entry.LoadError = e.Message;
                }

                _entries.Add(entry);
            }

            _selected = _entries.FirstOrDefault(e => e.Name == selectedName) ?? _entries.FirstOrDefault();
        }

        static string Describe(DemoSegment segment)
        {
            // Same one-letter class codes the Python ranking prints, so a row
            // here and a line of --top output are read the same way.
            var types = segment.events.Length == 0
                ? "none"
                : string.Join(",", segment.events.Select(e => e.eotTypeName[..1] + e.firstSpeaker));
            return $"{segment.durationSeconds:F1}s  [{types}]";
        }

        static string DescribeVoices(DemoSegment segment)
        {
            var agents = segment.agents.OrderBy(a => a.speaker);
            return string.Join(" / ", agents.Select(a =>
                string.IsNullOrEmpty(a.voice) ? "?" : $"{a.voice[..1].ToUpperInvariant()}{a.voicePitchHz:F0}"));
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                    Rescan();
                GUILayout.Label($"{_entries.Count} segments in {k_SegmentRoot}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }

            if (_entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No segments under {k_SegmentRoot}. Export some with Tools/find_demo_segments.py --export.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawDetail();
            }
        }

        void DrawList()
        {
            using var scope = new EditorGUILayout.VerticalScope(GUILayout.Width(k_ListWidth));
            using var scroll = new EditorGUILayout.ScrollViewScope(_listScroll, GUI.skin.box);
            _listScroll = scroll.scrollPosition;

            foreach (var entry in _entries)
            {
                var label = entry.LoadError == null
                    ? $"{entry.Name}\n    {entry.Summary}  {entry.Voices}"
                    : $"{entry.Name}\n    (unreadable)";

                var style = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fixedHeight = 32f,
                    richText = false,
                };

                var wasSelected = entry == _selected;
                if (GUILayout.Toggle(wasSelected, label, style) && !wasSelected)
                {
                    _selected = entry;
                    _detailScroll = Vector2.zero;
                }
            }
        }

        void DrawDetail()
        {
            using var scope = new EditorGUILayout.VerticalScope();

            if (_selected == null)
                return;

            if (_selected.LoadError != null)
            {
                EditorGUILayout.HelpBox($"{_selected.Path}\n\n{_selected.LoadError}", MessageType.Error);
                return;
            }

            var segment = _selected.Segment;
            EditorGUILayout.LabelField(_selected.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{segment.stem}   {segment.sourceStartSeconds:F2}–{segment.sourceEndSeconds:F2} s " +
                $"({segment.durationSeconds:F2} s)   voices {_selected.Voices}");
            EditorGUILayout.LabelField(
                $"{segment.events.Length} events   {segment.turns.Length} turns   " +
                (segment.yieldsToUser ? $"yields to user, {segment.tailSeconds:F1} s hold" : "scene 1"));

            DrawActions();
            EditorGUILayout.Space();

            using var scroll = new EditorGUILayout.ScrollViewScope(_detailScroll, GUI.skin.box);
            _detailScroll = scroll.scrollPosition;
            DrawTranscript(segment);
        }

        void DrawActions()
        {
            var conversation = UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();

            DrawMethodRow(runner);
            DrawDeveloperRow(conversation);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(conversation == null))
            {
                if (GUILayout.Button("Load into scene"))
                    LoadIntoScene(conversation);

                if (GUILayout.Button("Load and Play"))
                {
                    LoadIntoScene(conversation);
                    EditorApplication.isPlaying = true;
                }

                if (GUILayout.Button("Match textures to voices"))
                    ApplyTextures(conversation);

                using (new EditorGUI.DisabledScope(runner == null))
                {
                    if (GUILayout.Button("Scan seeds"))
                        ScanSeeds(conversation, runner);
                }
            }

            DrawTrackRow(conversation, runner);

            if (conversation == null)
            {
                EditorGUILayout.HelpBox(
                    "No RecordedConversation in the open scene, so there is nothing to load into.",
                    MessageType.Warning);
                return;
            }

            if (conversation.SegmentPath == _selected.Path)
                EditorGUILayout.LabelField("Loaded in the scene.", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Which gaze policy plays the clip. The seed sits beside it because in
        /// the Proposed condition it chooses which prototype each boundary
        /// draws, so two takes of one clip differ by it alone.
        /// </summary>
        void DrawMethodRow(GazeConditionRunner runner)
        {
            if (runner == null)
            {
                EditorGUILayout.HelpBox(
                    "No GazeConditionRunner in the open scene — run GazeControl → Set Up Gaze Conditions to wire one.",
                    MessageType.Warning);
                return;
            }

            using var row = new EditorGUILayout.HorizontalScope();

            EditorGUI.BeginChangeCheck();
            var condition = (GazeConditionRunner.GazeCondition)EditorGUILayout.EnumPopup(
                "Method", runner.Condition);
            var seed = EditorGUILayout.IntField("Seed", runner.BaseSeed, GUILayout.Width(140f));
            if (!EditorGUI.EndChangeCheck())
                return;

            Undo.RecordObject(runner, "Set gaze condition");
            runner.Condition = condition;
            runner.BaseSeed = seed;
            EditorUtility.SetDirty(runner);
            EditorSceneManager.MarkSceneDirty(runner.gameObject.scene);
        }

        /// <summary>
        /// Developer mode is preview-only: it names the end-of-turn boundaries
        /// the study asks participants to judge. The toggle adds the component
        /// on demand rather than the scene carrying it permanently, so a scene
        /// saved for the study has nothing to leave switched on by accident.
        /// </summary>
        void DrawDeveloperRow(RecordedConversation conversation)
        {
            if (conversation == null)
                return;

            var overlay = UnityEngine.Object.FindFirstObjectByType<DeveloperOverlay>(FindObjectsInactive.Include);
            var on = overlay != null && overlay.Enabled;

            using (new EditorGUILayout.HorizontalScope())
            {
                var wanted = EditorGUILayout.ToggleLeft(
                    "Developer mode (shows EoT events — preview only, never for participants)", on);
                if (wanted == on)
                    return;

                if (wanted && overlay == null)
                {
                    overlay = Undo.AddComponent<DeveloperOverlay>(conversation.gameObject);
                    overlay.Conversation = conversation;
                }

                if (overlay == null)
                    return;

                Undo.RecordObject(overlay, "Toggle developer mode");
                overlay.Enabled = wanted;
                EditorUtility.SetDirty(overlay);
                EditorSceneManager.MarkSceneDirty(overlay.gameObject.scene);
            }
        }

        void LoadIntoScene(RecordedConversation conversation)
        {
            Undo.RecordObject(conversation, "Load study clip");
            conversation.SegmentPath = _selected.Path;
            EditorUtility.SetDirty(conversation);
            EditorSceneManager.MarkSceneDirty(conversation.gameObject.scene);

            // The runner names its output folder from CaseName, so without this
            // every preview writes its gaze log into whichever take folder the
            // scene was last left on — five runs of three different clips piled
            // into one scene-2 folder before this was added.
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            if (runner != null && runner.CaseName != _selected.Name)
            {
                Undo.RecordObject(runner, "Load study clip");
                runner.CaseName = _selected.Name;
                EditorUtility.SetDirty(runner);
            }

            Debug.Log($"Clip Browser: {_selected.Name} loaded — {_selected.Segment.stem} " +
                      $"{_selected.Segment.durationSeconds:F2} s, {_selected.Segment.events.Length} events.", conversation);
        }

        /// <summary>
        /// Give each agent the albedo matching its own speaker's measured voice,
        /// as a persistent edit to the open scene.
        ///
        /// The rule and the applying are <see cref="AgentAppearance"/>, the same
        /// code a loading segment runs, so a preview here cannot come to differ
        /// from what a take actually shows. Only the edit-mode bookkeeping —
        /// Undo, and marking the scene dirty — is this button's own.
        /// </summary>
        void ApplyTextures(RecordedConversation conversation)
        {
            var applied = AgentAppearance.MatchToVoices(
                _selected.Segment, conversation.Speakers, conversation,
                renderer => Undo.RecordObject(renderer, "Match textures to voices"));

            if (applied > 0)
                EditorSceneManager.MarkSceneDirty(conversation.gameObject.scene);
        }

        /// <summary>
        /// Report how much each agent averts under the first
        /// <see cref="k_SeedScanCount"/> base seeds, so a clip is judged on the
        /// clip rather than on whichever draw its current seed happens to be.
        ///
        /// Both stochastic conditions are swept in one press, whatever the
        /// runner's current Method: an unlucky baseline A draw misrepresents the
        /// comparison exactly as much as an unlucky Proposed one, and scanning
        /// only the method under test would look for it in one half of the
        /// comparison. Each condition reports separately — the seeds are shared
        /// but the draws are not comparable across conditions.
        /// </summary>
        void ScanSeeds(RecordedConversation conversation, GazeConditionRunner runner)
        {
            foreach (var condition in k_StochasticConditions)
                ReportSeedScan(conversation, runner, condition);
        }

        /// <summary>One condition's sweep, logged as a table sorted by distance from the mean.</summary>
        void ReportSeedScan(RecordedConversation conversation, GazeConditionRunner runner,
            GazeConditionRunner.GazeCondition condition)
        {
            var results = SeedScan.Run(conversation, runner, _selected.Segment, condition,
                firstSeed: 1, seedCount: k_SeedScanCount);

            // Null means the scan refused this condition and said why.
            if (results == null)
                return;

            var mean = results.Average(r => r.Mean);
            var ordered = results.OrderBy(r => Mathf.Abs(r.Mean - mean)).ToList();
            var label = condition == GazeConditionRunner.GazeCondition.Proposed
                ? $"Proposed / {runner.Proposed.Substrate}"
                : "Baseline A / RoleConditioned";

            var report = new StringBuilder();
            report.AppendLine($"Seed scan — {_selected.Name} ({_selected.Segment.durationSeconds:F1} s), " +
                              $"{label}, {results.Count} seeds");
            report.AppendLine($"  aversion fraction per agent, mean over seeds {mean:P0}");

            foreach (var result in ordered)
            {
                var per = string.Join("  ", result.AversionFractions.Select(f => $"{f,6:P0}"));
                var marker = result.BaseSeed == ordered[0].BaseSeed ? "  <- closest to the mean" : string.Empty;
                report.AppendLine($"  seed {result.BaseSeed,3}:  {per}   (mean {result.Mean:P0}){marker}");
            }

            report.AppendLine("Sorted by distance from the mean. The spread across seeds is the condition's " +
                              "own variance — the extremes are draws, not the clip.");
            Debug.Log(report.ToString(), runner);
        }


        /// <summary>
        /// Baked-track state for this clip: which conditions have a track, and
        /// the actions that make or use them. A study session replays these
        /// files, so whether they exist is the single most important thing to
        /// see next to a clip.
        /// </summary>
        void DrawTrackRow(RecordedConversation conversation, GazeConditionRunner runner)
        {
            if (conversation == null || runner == null)
                return;

            var directory = Path.GetDirectoryName(_selected.Path);
            var baked = new List<string>();
            var missing = new List<string>();

            foreach (GazeConditionRunner.GazeCondition condition in
                     Enum.GetValues(typeof(GazeConditionRunner.GazeCondition)))
            {
                var path = GazeTrack.PathFor(directory, condition.ToString(), runner.BaseSeed);
                (File.Exists(path) ? baked : missing).Add(condition.ToString());
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    missing.Count == 0
                        ? $"Tracks (seed {runner.BaseSeed}): all three baked"
                        : $"Tracks (seed {runner.BaseSeed}): missing {string.Join(", ", missing)}",
                    EditorStyles.miniLabel);

                if (GUILayout.Button("Bake all 3", GUILayout.Width(90f)))
                {
                    LoadIntoScene(conversation);
                    GazeTrackBaker.BakeAllConditions();
                }
            }

            if (missing.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "A study session replays a baked track, so every condition needs one before this " +
                    "clip can be used. Baking plays the scene once per condition.",
                    MessageType.Info);
            }
        }

        static void DrawTranscript(DemoSegment segment)
        {
            var marks = segment.events
                .Select(e => (time: e.turnTime, text: $"--- EoT {e.eotTypeName}: {Label(e.firstSpeaker)} → {Label(e.secondSpeaker)} ---"))
                .ToList();

            var lines = segment.utterances
                .Select(u => (time: u.startTime, text: $"{Label(u.speaker)}  {u.text}"))
                .Concat(marks)
                .OrderBy(l => l.time);

            foreach (var (time, text) in lines)
                EditorGUILayout.LabelField($"{time,7:F2}  {text}", EditorStyles.wordWrappedMiniLabel);
        }

        static string Label(int speaker) => speaker switch
        {
            DemoSegment.UserSpeakerCode => "User",
            1 => "A",
            2 => "B",
            _ => speaker.ToString(),
        };
    }
}
