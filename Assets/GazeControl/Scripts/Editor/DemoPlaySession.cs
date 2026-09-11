using System.Globalization;
using GazeControl.Demo;
using GazeControl.Experiment;
using GazeControl.Xr;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Play one clip straight through, seen from the demo camera — the play
    /// session the framing window starts.
    ///
    /// <para><b>Two things in the scene have to be turned off first, and both of
    /// them are right for a participant and wrong for anything else.</b>
    /// <c>StudySessionRunner.Awake</c> takes autoplay away from the conversation
    /// on every plain Play, because a participant's first clip must be trial 1
    /// and not whatever the inspector was last left on — so with it enabled,
    /// pressing Play here gives a P00 session waiting on a framing screen rather
    /// than a clip. And <c>XrParticipantRig.StartXrOnPlay</c> is on in the
    /// committed scene, so the Varjo runtime would come up, take Unity's audio
    /// output with it, and stall the frame clock on a compositor nobody is
    /// wearing.</para>
    ///
    /// <para>Both are switched off in Edit Mode, which is the only place a switch
    /// read in <c>Awake</c> can be set — and therefore the only place that has to
    /// put them back, since an Edit Mode change is not one of the things Play
    /// Mode reverts on exit. Restored when play ends, however it ends.</para>
    ///
    /// <para><see cref="DemoTakeRenderer"/> does the same thing its own way and
    /// deliberately: it restores once at the end of a queue of play sessions,
    /// not after each one, and it has more to put back besides.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class DemoPlaySession
    {
        const string k_RestoreKey = "GazeControl.DemoPlaySession.Restore";

        static DemoPlaySession()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Restore();
            };
        }

        /// <summary>
        /// Enter play with the demo camera rendering and nothing else in the way.
        /// </summary>
        public static void Start()
        {
            var session = Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var rig = Object.FindFirstObjectByType<XrParticipantRig>(FindObjectsInactive.Include);

            SessionState.SetString(k_RestoreKey, string.Join("|",
                (session != null && session.enabled).ToString(CultureInfo.InvariantCulture),
                (rig != null && rig.StartXrOnPlay).ToString(CultureInfo.InvariantCulture)));

            if (session != null && session.enabled)
            {
                session.enabled = false;
                EditorUtility.SetDirty(session);
            }

            if (rig != null && rig.StartXrOnPlay)
            {
                rig.StartXrOnPlay = false;
                EditorUtility.SetDirty(rig);
            }

            DemoViewRequest.Set();
            EditorApplication.isPlaying = true;
        }

        static void Restore()
        {
            var restore = SessionState.GetString(k_RestoreKey, string.Empty);
            if (string.IsNullOrEmpty(restore))
                return;

            SessionState.EraseString(k_RestoreKey);
            DemoViewRequest.Clear(); // a request the camera never took must not outlive the play

            var parts = restore.Split('|');
            if (parts.Length != 2
                || !bool.TryParse(parts[0], out var sessionEnabled)
                || !bool.TryParse(parts[1], out var xrOnPlay))
                return;

            var session = Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            if (session != null && session.enabled != sessionEnabled)
            {
                session.enabled = sessionEnabled;
                EditorUtility.SetDirty(session);
            }

            var rig = Object.FindFirstObjectByType<XrParticipantRig>(FindObjectsInactive.Include);
            if (rig != null && rig.StartXrOnPlay != xrOnPlay)
            {
                rig.StartXrOnPlay = xrOnPlay;
                EditorUtility.SetDirty(rig);
            }
        }
    }
}
