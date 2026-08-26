using System;
using System.Linq;
using GazeControl.Conversation;
using GazeControl.Experiment;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Bakes gaze tracks by playing the scene once per condition and recording
    /// what each policy decided, so the study can replay a file rather than
    /// re-derive a take.
    ///
    /// <para>Baking runs the real thing rather than an offline simulation: the
    /// live path is what a study session replays, and baseline B's voice
    /// detection reads the audio at playback position, which an offline harness
    /// would have to reimplement and could get subtly wrong. A bake is therefore
    /// exactly what the policy did, by construction.</para>
    ///
    /// <para>A queue of conditions is carried through play sessions in
    /// <see cref="SessionState"/>, which survives the domain reload each Play
    /// causes — the same mechanism <c>DemoRecorder</c> uses.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class GazeTrackBaker
    {
        const string k_QueueKey = "GazeControl.GazeTrackBaker.Queue";
        const string k_RestoreKey = "GazeControl.GazeTrackBaker.Restore";

        // "A bake is running" is its own flag rather than "the queue is not
        // empty": the queue is emptied when the *last* condition starts, so the
        // emptiness test made the final play session look like no bake at all.
        // It then ran forever — nothing stopped play, so the track was only
        // written if someone stopped it by hand, and the scene was left in Bake
        // mode where the next Play would overwrite what it had just made.
        const string k_ActiveKey = "GazeControl.GazeTrackBaker.Active";

        /// <summary>Recorded past the last decision so the track covers any hold at the end.</summary>
        const float k_TailSeconds = 0.5f;

        /// <summary>
        /// How long a bake waits for the conversation's clock to start before
        /// giving up on the whole queue.
        ///
        /// Without it a segment that fails to load — a wav Unity has not
        /// imported is enough, and the console says so quietly — leaves the
        /// baker in Play Mode forever: it stops on <c>HasFinished</c>, which a
        /// conversation that never started never reaches. Measured once at ten
        /// minutes of silence before anyone looked.
        /// </summary>
        const float k_StartTimeoutSeconds = 30f;

        static RecordedConversation s_Conversation;
        static float s_StopAt = -1f;
        static float s_PlayStarted = -1f;

        static GazeTrackBaker()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Advance();
            };
        }

        /// <summary>Queue every condition for the scene's current clip.</summary>
        public static void BakeAllConditions()
        {
            var conditions = Enum.GetValues(typeof(GazeConditionRunner.GazeCondition))
                .Cast<GazeConditionRunner.GazeCondition>()
                .Select(c => c.ToString());

            Start(string.Join(",", conditions));
        }

        /// <summary>Queue just the condition the runner is set to.</summary>
        public static void BakeCurrentCondition()
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            if (runner == null)
            {
                Debug.LogError("Gaze track baker: no GazeConditionRunner in the open scene.");
                return;
            }

            Start(runner.Condition.ToString());
        }

        static void Start(string queue)
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            if (runner == null)
            {
                Debug.LogError("Gaze track baker: no GazeConditionRunner in the open scene.");
                return;
            }

            if (runner.StudySession)
            {
                Debug.LogError("Gaze track baker: StudySession is on, which refuses any run that is not " +
                               "a Replay. Turn it off to bake.");
                return;
            }

            // Remembered so a bake does not silently leave the scene in Bake
            // mode, where the next Play would overwrite the track just made.
            SessionState.SetString(k_RestoreKey, $"{runner.Tracks}|{runner.Condition}");
            SessionState.SetString(k_QueueKey, queue);
            SessionState.SetBool(k_ActiveKey, true);
            Advance();
        }

        /// <summary>Start the next queued condition, or finish and restore the scene.</summary>
        static void Advance()
        {
            if (!SessionState.GetBool(k_ActiveKey, false))
                return;

            var queue = SessionState.GetString(k_QueueKey, string.Empty);

            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            if (runner == null)
            {
                Finish(null);
                return;
            }

            var remaining = queue.Split(',');
            if (remaining.Length == 0 || string.IsNullOrEmpty(remaining[0]))
            {
                Finish(runner);
                return;
            }

            if (!Enum.TryParse<GazeConditionRunner.GazeCondition>(remaining[0], out var condition))
            {
                Debug.LogError($"Gaze track baker: '{remaining[0]}' is not a condition.");
                Finish(runner);
                return;
            }

            SessionState.SetString(k_QueueKey, string.Join(",", remaining.Skip(1)));

            runner.Condition = condition;
            runner.Tracks = GazeConditionRunner.TrackMode.Bake;
            EditorUtility.SetDirty(runner);

            Debug.Log($"Gaze track baker: baking {condition} — {remaining.Length - 1} condition(s) to follow.");
            s_StopAt = -1f;
            s_PlayStarted = -1f;
            s_Conversation = null;
            EditorApplication.isPlaying = true;
        }

        static void Finish(GazeConditionRunner runner)
        {
            SessionState.EraseBool(k_ActiveKey);
            SessionState.EraseString(k_QueueKey);

            var restore = SessionState.GetString(k_RestoreKey, string.Empty);
            SessionState.EraseString(k_RestoreKey);

            if (runner == null || string.IsNullOrEmpty(restore))
                return;

            var parts = restore.Split('|');
            if (parts.Length == 2
                && Enum.TryParse<GazeConditionRunner.TrackMode>(parts[0], out var mode)
                && Enum.TryParse<GazeConditionRunner.GazeCondition>(parts[1], out var condition))
            {
                runner.Tracks = mode;
                runner.Condition = condition;
                EditorUtility.SetDirty(runner);
            }

            Debug.Log("Gaze track baker: done. Set Tracks to Replay for a study session.");
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(k_ActiveKey, false))
                return;

            s_Conversation ??= UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();

            if (s_PlayStarted < 0f)
                s_PlayStarted = Time.time;

            var started = s_Conversation != null && s_Conversation.Elapsed >= 0f;
            if (!started && Time.time - s_PlayStarted > k_StartTimeoutSeconds)
            {
                Debug.LogError(
                    $"Gaze track baker: the conversation has not started after {k_StartTimeoutSeconds:0} s, " +
                    "so nothing would ever be baked. The usual cause is a segment Unity has not imported " +
                    "yet — check the console for a load error and run Assets → Refresh. Queue abandoned.");
                SessionState.EraseString(k_QueueKey);
                EditorApplication.isPlaying = false;
                return;
            }

            // The runner writes the track in OnDestroy, so leaving play mode is
            // what commits it; the tail lets the last decisions land first.
            if (s_StopAt < 0f && s_Conversation != null && s_Conversation.HasFinished)
                s_StopAt = Time.time + k_TailSeconds;

            if (s_StopAt > 0f && Time.time >= s_StopAt)
                EditorApplication.isPlaying = false;
        }
    }
}
