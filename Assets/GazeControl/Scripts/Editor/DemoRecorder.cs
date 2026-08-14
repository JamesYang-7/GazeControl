using GazeControl.Conversation;
using GazeControl.Experiment;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// One-click demo recording: GazeControl → Record Demo enters Play Mode,
    /// records the Game view with audio to Recordings/&lt;case&gt;/*.mp4
    /// (git-ignored), stops automatically shortly after the recorded segment
    /// finishes and freezes the motion players, and exits Play Mode.
    ///
    /// The take's length is therefore the segment's, not a fixed number: a
    /// 24 s segment gives a 24 s video plus the tail below.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoRecorder
    {
        // SessionState survives the domain reload that entering play mode triggers.
        const string k_PendingKey = "GazeControl.DemoRecorder.Pending";
        const float k_DefaultTailSeconds = 1.5f;

        static RecorderController s_Controller;
        static RecordedConversation s_Conversation;
        static float s_StopAt = -1f;

        static DemoRecorder()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                    StopRecording(); // safety: finalize the file if play mode ends early
            };
        }

        [MenuItem("GazeControl/Record Demo")]
        public static void RecordDemo()
        {
            SessionState.SetBool(k_PendingKey, true);
            EditorApplication.isPlaying = true;
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying)
                return;

            if (SessionState.GetBool(k_PendingKey, false))
            {
                // The flag is cleared only once the encoder is up, because the
                // conversation waits on it: starting the segment while Recorder
                // is still preparing cost the first two seconds of the video.
                // Cleared in a finally so a failed start cannot hang the scene.
                try
                {
                    StartRecording();
                }
                finally
                {
                    SessionState.SetBool(k_PendingKey, false);
                }
            }

            if (s_Controller == null)
                return;

            if (s_Conversation == null)
                s_Conversation = Object.FindFirstObjectByType<RecordedConversation>();

            if (s_StopAt < 0f && s_Conversation != null && s_Conversation.HasFinished)
                s_StopAt = Time.time + TailSeconds(s_Conversation);

            if (s_StopAt > 0f && Time.time >= s_StopAt)
                EditorApplication.isPlaying = false; // ExitingPlayMode handler stops the recorder
        }

        /// <summary>
        /// How long the take holds after the voices stop. Segment data, because
        /// the hold is a property of what was played: a scene-2 yield needs a
        /// few watchable seconds of gaze at the user, a scene-1 take does not.
        /// </summary>
        static float TailSeconds(RecordedConversation conversation) =>
            conversation.Segment != null && conversation.Segment.tailSeconds > 0f
                ? conversation.Segment.tailSeconds
                : k_DefaultTailSeconds;

        static void StartRecording()
        {
            Application.runInBackground = true; // keep recording while the editor is unfocused

            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "GazeDemo";
            movie.Enabled = true;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
            };
            movie.CaptureAudio = true;
            movie.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080,
            };
            // Video and gaze log share one folder per take, so a recording is never
            // separated from the log that explains it. The condition also goes in
            // the file name: three takes shot minutes apart are otherwise told
            // apart only by their timestamps.
            var runner = Object.FindFirstObjectByType<GazeConditionRunner>();
            var condition = runner != null ? runner.Condition.ToString() : "NoCondition";
            var directory = runner != null
                ? $"{runner.OutputDirectory}/{runner.CaseName}"
                : "Recordings";

            // Unity Recorder does not create missing intermediate folders, and a
            // failed write only shows up as a silently absent file at the end.
            System.IO.Directory.CreateDirectory(
                System.IO.Path.Combine(Application.dataPath, "..", directory));

            movie.OutputFile = $"{directory}/GazeDemo_{condition}_{System.DateTime.Now:yyyyMMdd_HHmmss}";

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = 30;

            // Locked game clock: game time advances exactly 1/30 s per recorded
            // frame, so a 24.33 s segment is 730 frames of video however fast the
            // machine actually renders. The conversation is driven by the same
            // frame clock, which is what keeps the take's length equal to the
            // segment's.
            settings.CapFrameRate = true;

            s_Controller = new RecorderController(settings);
            s_Controller.PrepareRecording();
            s_Controller.StartRecording();
            Debug.Log($"[DemoRecorder] Recording to {movie.OutputFile}.mp4");
        }

        static void StopRecording()
        {
            if (s_Controller == null)
                return;
            s_Controller.StopRecording();
            s_Controller = null;
            s_Conversation = null;
            s_StopAt = -1f;
            Debug.Log("[DemoRecorder] Recording stopped.");
        }
    }
}
