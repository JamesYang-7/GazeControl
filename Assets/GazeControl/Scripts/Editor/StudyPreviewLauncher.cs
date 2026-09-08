using GazeControl.Experiment;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Study 1 / Study 2 → Preview Session: play the session's clip order
    /// with the baked tracks replayed, for checking the bakes by eye.
    ///
    /// <para>A request consumed at play start, exactly like a participant
    /// launch (<see cref="StudyLaunchRequest"/>), so the scene on disk is never
    /// touched: the session runner turns itself on, replays tracks, keeps the
    /// questionnaire and the logs off, shows the developer overlay, and runs
    /// each clip straight into the next, on the desktop with the headset off.
    /// Space skips the clip that is playing. Nothing a preview shows is a take.</para>
    /// </summary>
    public static class StudyPreviewLauncher
    {
        [MenuItem("GazeControl/Study 1/Preview Session")]
        public static void PreviewStudy1()
        {
            if (StudyScenes.EnsureOpen(StudyScenes.Study1ScenePath, "Study 1 → Preview Session"))
                Preview(inHeadset: false);
        }

        [MenuItem("GazeControl/Study 1/Preview Session in Headset")]
        public static void PreviewStudy1InHeadset()
        {
            if (StudyScenes.EnsureOpen(StudyScenes.Study1ScenePath, "Study 1 → Preview Session in Headset"))
                Preview(inHeadset: true);
        }

        [MenuItem("GazeControl/Study 2/Preview Session")]
        public static void PreviewStudy2()
        {
            if (StudyScenes.EnsureOpen(StudyScenes.Study2ScenePath, "Study 2 → Preview Session"))
                Preview(inHeadset: false);
        }

        /// <summary>
        /// The same preview with the headset started, to watch the bakes from
        /// the participant's seat. Put the headset on before pressing it: the
        /// frame clock waits on the compositor while nobody is wearing it.
        /// </summary>
        [MenuItem("GazeControl/Study 2/Preview Session in Headset")]
        public static void PreviewStudy2InHeadset()
        {
            if (StudyScenes.EnsureOpen(StudyScenes.Study2ScenePath, "Study 2 → Preview Session in Headset"))
                Preview(inHeadset: true);
        }

        static void Preview(bool inHeadset)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Preview Session: already playing — stop first.");
                return;
            }

            var session = Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            if (session == null)
            {
                Debug.LogError("Preview Session: no StudySessionRunner in the open scene — open a study scene.");
                return;
            }

            if (!string.IsNullOrEmpty(StudyLaunchRequest.Pending))
            {
                Debug.LogError(
                    $"Preview Session: a participant launch for {StudyLaunchRequest.Pending} is pending; " +
                    "start or cancel that first.");
                return;
            }

            StudyLaunchRequest.SetPreview(inHeadset);
            EditorApplication.isPlaying = true;
        }
    }
}
