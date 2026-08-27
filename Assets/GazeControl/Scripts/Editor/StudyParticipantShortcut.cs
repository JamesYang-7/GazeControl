using GazeControl.Experiment;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Keyboard shortcut for stepping to the next study participant between
    /// sessions.
    ///
    /// <para>It finds the scene's condition runner rather than acting on the
    /// selection: there is exactly one, and nobody would think to select it for
    /// this. An editor shortcut rather than a key read in <c>Update</c> because
    /// the change belongs strictly between sessions — the runner refuses it
    /// during play — and because this way it is rebindable in Edit →
    /// Shortcuts.</para>
    /// </summary>
    public static class StudyParticipantShortcut
    {
        /// <summary>
        /// Bound to <b>Alt+N</b> rather than a bare key on purpose. A stray press
        /// would relabel the next participant's whole session, and that is the
        /// kind of mistake only noticed during analysis; a modifier makes it
        /// deliberate without costing a second gesture.
        /// </summary>
        [Shortcut("GazeControl/Next Study Participant", KeyCode.N, ShortcutModifiers.Alt)]
        public static void NextStudyParticipant()
        {
            var runner = Object.FindAnyObjectByType<GazeConditionRunner>(FindObjectsInactive.Include);
            if (runner == null)
            {
                Debug.Log("Next Study Participant: no GazeConditionRunner in the open scene.");
                return;
            }

            runner.NextParticipant();

            // Selected and pinged so the change is visible where it happened,
            // rather than only in the console: this edits an object the operator
            // may not have open in the inspector.
            Selection.activeObject = runner.gameObject;
            EditorGUIUtility.PingObject(runner);
        }
    }
}
