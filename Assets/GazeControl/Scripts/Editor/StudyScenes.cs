using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// The two study scenes, and the one rule the study menus share: a command
    /// under <c>GazeControl → Study 1</c> acts on TriadScene and one under
    /// <c>Study 2</c> on Study2Scene, opening it first if some other scene is up.
    ///
    /// <para>Two menus rather than one "Study" menu that acts on whatever scene
    /// happens to be open (user, 2026-09-08): the two studies share every
    /// component and differ only in the scene, so a Start Session pressed with
    /// the wrong scene open would run the wrong study under a label from the
    /// wrong roster, and nothing in the window would say so until the first
    /// clip.</para>
    /// </summary>
    public static class StudyScenes
    {
        public const string Study1ScenePath = "Assets/Scenes/TriadScene.unity";
        public const string Study2ScenePath = Study2SceneSetup.ScenePath;

        /// <summary>
        /// Make <paramref name="scenePath"/> the open scene. Refused in play
        /// mode, and the ordinary "save changes?" prompt guards the scene being
        /// left.
        /// </summary>
        /// <returns>False when the scene is not open afterwards; the reason is logged.</returns>
        public static bool EnsureOpen(string scenePath, string command)
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path == scenePath)
                return true;

            if (EditorApplication.isPlaying)
            {
                Debug.LogError($"{command}: {System.IO.Path.GetFileNameWithoutExtension(scenePath)} is not the open " +
                               "scene, and play mode is running — stop it, then run this again.");
                return false;
            }

            if (!System.IO.File.Exists(scenePath))
            {
                Debug.LogError($"{command}: {scenePath} does not exist. For study 2, run GazeControl → Study 2 → Set Up Scene first.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            var opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (!opened.IsValid())
            {
                Debug.LogError($"{command}: could not open {scenePath}.");
                return false;
            }

            Debug.Log($"{command}: opened {opened.name}.");
            return true;
        }
    }
}
