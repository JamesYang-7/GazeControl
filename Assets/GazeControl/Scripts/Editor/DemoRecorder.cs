using GazeControl.Conversation;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// One-click demo recording: GazeControl → Record Demo enters Play Mode,
    /// records the Game view with audio to Recordings/*.mp4 (git-ignored), stops
    /// automatically shortly after the conversation freezes the motion players
    /// (the demo's end), and exits Play Mode.
    /// </summary>
    [InitializeOnLoad]
    public static class DemoRecorder
    {
        // SessionState survives the domain reload that entering play mode triggers.
        const string k_PendingKey = "GazeControl.DemoRecorder.Pending";
        const float k_TailSeconds = 1.5f;

        static RecorderController s_Controller;
        static TriadConversation s_Conversation;
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
                SessionState.SetBool(k_PendingKey, false);
                StartRecording();
            }

            if (s_Controller == null)
                return;

            if (s_Conversation == null)
                s_Conversation = Object.FindFirstObjectByType<TriadConversation>();

            var demoEnded = s_Conversation != null
                            && s_Conversation.MotionPlayers is { Length: > 0 }
                            && !s_Conversation.MotionPlayers[0].enabled;
            if (s_StopAt < 0f && demoEnded)
                s_StopAt = Time.time + k_TailSeconds;

            if (s_StopAt > 0f && Time.time >= s_StopAt)
                EditorApplication.isPlaying = false; // ExitingPlayMode handler stops the recorder
        }

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
            movie.OutputFile = $"Recordings/GazeDemo_{System.DateTime.Now:yyyyMMdd_HHmmss}";

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRate = 30;
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
