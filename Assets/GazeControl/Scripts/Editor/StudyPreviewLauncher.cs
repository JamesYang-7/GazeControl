using GazeControl.Experiment;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Study → Preview Session: play the session's clip order
    /// with the baked tracks replayed, for checking the bakes by eye.
    ///
    /// <para>A request consumed at play start, exactly like a participant
    /// launch (<see cref="StudyLaunchRequest"/>), so the scene on disk is never
    /// touched: the session runner turns itself on, replays tracks, keeps the
    /// questionnaire and the logs off, shows the developer overlay, and runs
    /// each clip straight into the next. Space skips the clip that is playing.
    /// Nothing a preview shows is a take.</para>
    /// </summary>
    public static class StudyPreviewLauncher
    {
        [MenuItem("GazeControl/Study/Preview Session")]
        public static void Preview()
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

            StudyLaunchRequest.SetPreview();
            EditorApplication.isPlaying = true;
        }
    }
}
