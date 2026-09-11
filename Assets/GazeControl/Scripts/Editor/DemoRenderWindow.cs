using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GazeControl.Experiment;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// <c>GazeControl → Demo Video → Render Takes</c>: choose which clips and
    /// which methods go into the video, and queue them.
    ///
    /// <para>The clips and their seeds are read from the session runner rather
    /// than typed, so what the video shows is what the participants were shown —
    /// a seed picked here by hand would be a different draw of the prototypes
    /// from the one the study ran on, and nothing in the finished video would
    /// say so.</para>
    /// </summary>
    public sealed class DemoRenderWindow : EditorWindow
    {
        [SerializeField] Vector2 _scroll;
        [SerializeField] List<string> _skippedClips = new();
        [SerializeField] List<string> _skippedConditions = new();

        StudySessionRunner _session;

        static IReadOnlyList<GazeConditionRunner.GazeCondition> AllConditions { get; } =
            ((GazeConditionRunner.GazeCondition[])Enum.GetValues(typeof(GazeConditionRunner.GazeCondition)))
            .ToArray();

        [MenuItem("GazeControl/Demo Video/Render Takes")]
        public static void Open()
        {
            var window = GetWindow<DemoRenderWindow>("Render Takes");
            window.minSize = new Vector2(460f, 380f);
            window.Rebind();
        }

        void OnEnable() => Rebind();

        void Rebind() =>
            _session = FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);

        void OnGUI()
        {
            if (_session == null || _session.Conversations == null || _session.Conversations.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "This scene has no StudySessionRunner with conversations, so there is no clip order to render. " +
                    "Open the study scene first.", MessageType.Info);

                if (GUILayout.Button("Look again"))
                    Rebind();

                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            for (var i = 0; i < _session.Conversations.Length; i++)
                DrawClipRow(_session.Conversations[i], SeedFor(i));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Methods", EditorStyles.boldLabel);
            foreach (var condition in AllConditions)
                DrawConditionRow(condition);

            EditorGUILayout.Space();
            DrawRenderRow();

            EditorGUILayout.EndScrollView();
        }

        int SeedFor(int index) =>
            _session.Seeds != null && index < _session.Seeds.Length ? _session.Seeds[index] : 0;

        void DrawClipRow(string clip, int seed)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var wanted = EditorGUILayout.ToggleLeft(clip, !_skippedClips.Contains(clip), GUILayout.Width(160f));
                Skip(_skippedClips, clip, !wanted);

                var segment = SegmentPath(clip);
                EditorGUILayout.LabelField(
                    File.Exists(segment) ? $"seed {seed}" : $"seed {seed} · segment missing",
                    EditorStyles.miniLabel);
            }
        }

        void DrawConditionRow(GazeConditionRunner.GazeCondition condition)
        {
            var name = condition.ToString();
            var wanted = EditorGUILayout.ToggleLeft(name, !_skippedConditions.Contains(name));
            Skip(_skippedConditions, name, !wanted);
        }

        static void Skip(ICollection<string> skipped, string key, bool skip)
        {
            if (skip && !skipped.Contains(key))
                skipped.Add(key);
            else if (!skip)
                skipped.Remove(key);
        }

        void DrawRenderRow()
        {
            var shots = Shots();
            var problem = DemoTakeRenderer.Problem(shots);

            EditorGUILayout.LabelField(
                $"{shots.Count} take(s), 1920x1080 at {DemoTakeRenderer.FrameRate} fps, " +
                $"into {DemoTakeRenderer.OutputRoot}/<clip>/<method>/.",
                EditorStyles.wordWrappedMiniLabel);

            if (problem != null)
                EditorGUILayout.HelpBox(problem, MessageType.Warning);

            using (new EditorGUI.DisabledScope(problem != null))
            {
                if (GUILayout.Button($"Render {shots.Count} take(s)"))
                    DemoTakeRenderer.Render(shots);
            }

            EditorGUILayout.LabelField(
                "One play session per take, unattended. The scene's clip, seed, method and logging are put back " +
                "afterwards, and the headset is kept off for the duration.",
                EditorStyles.wordWrappedMiniLabel);
        }

        string SegmentPath(string clip) => $"{_session.SegmentRoot}/{clip}/segment.json";

        /// <summary>Every ticked clip under every ticked method, in the session's own order.</summary>
        List<DemoTakeRenderer.Shot> Shots()
        {
            var shots = new List<DemoTakeRenderer.Shot>();
            if (_session?.Conversations == null)
                return shots;

            for (var i = 0; i < _session.Conversations.Length; i++)
            {
                var clip = _session.Conversations[i];
                if (_skippedClips.Contains(clip))
                    continue;

                foreach (var condition in AllConditions)
                {
                    if (_skippedConditions.Contains(condition.ToString()))
                        continue;

                    shots.Add(new DemoTakeRenderer.Shot(SegmentPath(clip), condition, SeedFor(i)));
                }
            }

            return shots;
        }
    }
}
