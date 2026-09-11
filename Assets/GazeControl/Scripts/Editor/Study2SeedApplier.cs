using System;
using System.IO;
using System.Linq;
using GazeControl.Experiment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Study 2 → Apply Chosen Seeds: writes the clip order and
    /// the seed per clip that <c>Tools/choose_study2_seeds.py</c> chose onto
    /// the session runner in Study2Scene, and saves the scene.
    ///
    /// <para>The choice is made by a rule in that script and recorded in
    /// <c>Config/study2_seeds.json</c>; this command only transcribes it, so
    /// the scene never carries a seed whose reason is not on disk. Typing the
    /// seven numbers into the inspector by hand is what this replaces — the
    /// order matters, and a transposition there would play the wrong bake
    /// under the right clip name with nothing to say so.</para>
    /// </summary>
    public static class Study2SeedApplier
    {
        public const string ChoicePath = "Config/study2_seeds.json";
        const string RunnerObjectName = "GazeCondition";

        [Serializable]
        class Choice
        {
            public string clip;
            public int seed;
            public string prototype;
        }

        [Serializable]
        class Record
        {
            public Choice[] chosen;
        }

        [MenuItem("GazeControl/Study 2/Apply Chosen Seeds", priority = 23)]
        public static void Apply()
        {
            const string command = "Study 2 → Apply Chosen Seeds";
            if (!File.Exists(ChoicePath))
            {
                Debug.LogError($"{command}: {ChoicePath} not found — run uv run python Tools/choose_study2_seeds.py first.");
                return;
            }

            var record = JsonUtility.FromJson<Record>(File.ReadAllText(ChoicePath));
            if (record?.chosen == null || record.chosen.Length == 0)
            {
                Debug.LogError($"{command}: {ChoicePath} lists no chosen seeds.");
                return;
            }

            if (!StudyScenes.EnsureOpen(StudyScenes.Study2ScenePath, command))
                return;

            var scene = EditorSceneManager.GetActiveScene();
            var runnerObject = scene.GetRootGameObjects().FirstOrDefault(o => o.name == RunnerObjectName);
            var session = runnerObject != null ? runnerObject.GetComponent<StudySessionRunner>() : null;
            if (session == null)
            {
                Debug.LogError($"{command}: no '{RunnerObjectName}' object with a StudySessionRunner in {scene.path}.");
                return;
            }

            foreach (var choice in record.chosen)
            {
                if (!File.Exists($"{session.SegmentRoot}/{choice.clip}/segment.json"))
                {
                    Debug.LogError($"{command}: {choice.clip} has no segment under {session.SegmentRoot}; nothing applied.");
                    return;
                }
            }

            Undo.RecordObject(session, "Apply chosen seeds");
            session.Conversations = record.chosen.Select(c => c.clip).ToArray();
            session.Seeds = record.chosen.Select(c => c.seed).ToArray();
            EditorUtility.SetDirty(session);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, scene.path))
            {
                Debug.LogError($"{command}: could not save {scene.path}.");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"{command}: {record.chosen.Length} conversations on the session runner — " +
                string.Join(", ", record.chosen.Select(c => $"{c.clip} seed {c.seed} ({c.prototype})")) +
                ". Check them by eye with Study 2 → Preview Session.", session);
        }
    }
}
