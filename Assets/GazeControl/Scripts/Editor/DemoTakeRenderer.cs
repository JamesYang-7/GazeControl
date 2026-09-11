using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GazeControl.Conversation;
using GazeControl.Demo;
using GazeControl.Experiment;
using GazeControl.Gaze.Policy;
using GazeControl.Xr;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Stage one of a demo video: play every chosen clip under every chosen
    /// method and write out the frames, the sound, and enough about the take for
    /// stage two to caption it.
    ///
    /// <para><b>Frames and sound, not a finished video.</b> The titles, the
    /// method's name and the subtitles are all text, and text is what gets
    /// rewritten — so the expensive half is done once and kept, and
    /// <c>Tools/compose_demo_video.py</c> cuts the video from it as often as the
    /// wording changes. A PNG per frame costs a gigabyte or two per take and
    /// loses nothing; the alternative is re-rendering fifteen takes to fix a
    /// caption.</para>
    ///
    /// <para><b>What it plays is what a participant saw.</b> The baked track is
    /// replayed, at the clip's chosen seed, so the gaze in the video is the gaze
    /// in the study rather than a fresh draw that happens to look better. A clip
    /// whose track has not been baked is refused before anything starts.</para>
    ///
    /// <para>The queue is carried between play sessions in
    /// <see cref="SessionState"/> exactly as <see cref="GazeTrackBaker"/> carries
    /// its bakes, and the scene is put back afterwards for the same reason: a
    /// render left the scene on some clip in Replay with the demo camera live,
    /// and the next thing someone pressed Play for inherited it.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class DemoTakeRenderer
    {
        const string k_QueueKey = "GazeControl.DemoRender.Queue";
        const string k_RestoreKey = "GazeControl.DemoRender.Restore";
        const string k_CurrentKey = "GazeControl.DemoRender.Current";
        const string k_ReportKey = "GazeControl.DemoRender.Report";
        const string k_StartedKey = "GazeControl.DemoRender.Started";
        const string k_ActiveKey = "GazeControl.DemoRender.Active";

        /// <summary>Where takes are written, relative to the project root.</summary>
        public const string OutputRoot = "Recordings/Demos";

        /// <summary>The frame the video runs at, and the rate the game clock is locked to.</summary>
        public const int FrameRate = 30;

        /// <summary>Frame size of every rendered take.</summary>
        public const int OutputWidth = 1920;

        /// <summary>Frame size of every rendered take.</summary>
        public const int OutputHeight = 1080;

        /// <summary>Held after the voices stop when the segment asks for no particular tail.</summary>
        const float k_DefaultTailSeconds = 1.5f;

        /// <summary>How long a take waits for the conversation's clock before the queue is abandoned.</summary>
        const float k_StartTimeoutSeconds = 30f;

        static RecorderController s_Controller;
        static RecordedConversation s_Conversation;
        static float s_StopAt = -1f;
        static float s_PlayStarted = -1f;

        // Its own flag rather than "s_Controller is null", because the controller
        // is cleared again as play mode exits — and play mode is still reported
        // as running for a tick after that, which was enough to start a second
        // recording over the take that had just finished.
        static bool s_Started;

        /// <summary>One clip under one method: a single play session, and a single folder of frames.</summary>
        public readonly struct Shot
        {
            public Shot(string segmentPath, GazeConditionRunner.GazeCondition condition, int seed)
            {
                SegmentPath = segmentPath;
                Condition = condition;
                Seed = seed;
            }

            public string SegmentPath { get; }
            public GazeConditionRunner.GazeCondition Condition { get; }
            public int Seed { get; }

            /// <summary>The clip's folder name — <c>study2_c1</c> and the like.</summary>
            public string Clip => Path.GetFileName(Path.GetDirectoryName(SegmentPath));

            public string Label => $"{Clip} / {Condition} / seed {Seed}";

            /// <summary>Where this take's frames, sound and manifest go.</summary>
            public string Directory => $"{OutputRoot}/{Clip}/{Condition}";

            // Segment paths carry no '|' or ';', so the two are safe separators.
            public string Serialize() =>
                $"{SegmentPath}|{Condition}|{Seed.ToString(CultureInfo.InvariantCulture)}";

            public static bool TryParse(string text, out Shot shot)
            {
                shot = default;
                var parts = text.Split('|');
                if (parts.Length != 3
                    || !Enum.TryParse<GazeConditionRunner.GazeCondition>(parts[1], out var condition)
                    || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                    return false;

                shot = new Shot(parts[0], condition, seed);
                return true;
            }
        }

        static DemoTakeRenderer()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                    StopRecording(); // safety: finalise the files if play ends early
                else if (state == PlayModeStateChange.EnteredEditMode)
                    OnPlaySessionEnded();
            };
        }

        /// <summary>True while a render is working through its queue.</summary>
        public static bool IsRunning => SessionState.GetBool(k_ActiveKey, false);

        /// <summary>
        /// Everything that would stop <paramref name="shots"/> from rendering, or
        /// null when nothing would. Checked before the first play session rather
        /// than discovered at the eleventh, because a queue of fifteen takes is
        /// half an hour that nobody watches.
        /// </summary>
        public static string Problem(IReadOnlyList<Shot> shots)
        {
            if (shots == null || shots.Count == 0)
                return "nothing is selected";

            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            if (runner == null || UnityEngine.Object.FindFirstObjectByType<RecordedConversation>() == null)
                return "this scene has no GazeConditionRunner and RecordedConversation";

            if (DemoCameraSetup.Find() == null)
                return "this scene has no demo camera — run Demo Video → Set Up Demo Camera first";

            if (runner.StudySession)
                return "StudySession is on, which refuses any run that is not a participant's";

            if (IsRunning)
                return "a render is already working through its queue";

            foreach (var shot in shots)
            {
                if (!File.Exists(shot.SegmentPath))
                    return $"{shot.Clip}: {shot.SegmentPath} does not exist; export the segment first";

                var track = GazeTrack.PathFor(
                    Path.GetDirectoryName(shot.SegmentPath), shot.Condition.ToString(), shot.Seed);
                if (!File.Exists(track))
                    return $"{shot.Label}: no baked track at {track}; bake it before rendering";
            }

            return null;
        }

        /// <summary>Queue the shots and start working through them.</summary>
        public static void Render(IReadOnlyList<Shot> shots)
        {
            var problem = Problem(shots);
            if (problem != null)
            {
                Debug.LogError($"Demo renderer: {problem}. Nothing queued.");
                return;
            }

            var conversation = UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            var session = UnityEngine.Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var rig = UnityEngine.Object.FindFirstObjectByType<XrParticipantRig>(FindObjectsInactive.Include);

            SessionState.SetString(k_RestoreKey,
                $"{runner.Tracks}|{runner.Condition}|{runner.BaseSeed.ToString(CultureInfo.InvariantCulture)}|" +
                $"{conversation.SegmentPath}|{runner.LoggingEnabled}|" +
                $"{(session != null && session.enabled)}|{(rig != null && rig.StartXrOnPlay)}");

            // A render reads no headset, and starting the Varjo runtime would
            // hand it Unity's audio output — which is the sound being recorded.
            if (rig != null && rig.StartXrOnPlay)
            {
                rig.StartXrOnPlay = false;
                EditorUtility.SetDirty(rig);
            }

            // The session runner's Awake takes autoplay away from every plain
            // Play, which is right for a participant run and leaves a render
            // waiting on a framing screen nobody will dismiss.
            if (session != null && session.enabled)
            {
                session.enabled = false;
                EditorUtility.SetDirty(session);
            }

            DemoRecorder.SilenceDeveloperOverlay();

            SessionState.SetString(k_QueueKey, string.Join(";", shots.Select(s => s.Serialize())));
            SessionState.SetString(k_ReportKey, string.Empty);
            SessionState.SetString(k_StartedKey, DateTime.Now.ToString("O"));
            SessionState.SetBool(k_ActiveKey, true);

            Debug.Log($"Demo renderer: {shots.Count} take(s) queued, writing under {OutputRoot}/.");
            Advance();
        }

        /// <summary>Start the next queued shot, or finish and put the scene back.</summary>
        static void Advance()
        {
            if (!IsRunning)
                return;

            var conversation = UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            if (runner == null || conversation == null)
            {
                Finish(null, null);
                return;
            }

            var queue = SessionState.GetString(k_QueueKey, string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (queue.Length == 0)
            {
                Finish(conversation, runner);
                return;
            }

            if (!Shot.TryParse(queue[0], out var shot))
            {
                Debug.LogError($"Demo renderer: '{queue[0]}' is not a queued take.");
                Finish(conversation, runner);
                return;
            }

            SessionState.SetString(k_QueueKey, string.Join(";", queue.Skip(1)));
            SessionState.SetString(k_CurrentKey, shot.Serialize());

            conversation.SegmentPath = shot.SegmentPath;
            EditorUtility.SetDirty(conversation);
            runner.Condition = shot.Condition;
            runner.BaseSeed = shot.Seed;
            runner.Tracks = GazeConditionRunner.TrackMode.Replay;

            // The baked track is the record of what this take's gaze was; a
            // second copy written per render would only be the same numbers in
            // another folder.
            runner.LoggingEnabled = false;
            EditorUtility.SetDirty(runner);

            Debug.Log($"Demo renderer: rendering {shot.Label} — {queue.Length - 1} take(s) to follow.");
            s_StopAt = -1f;
            s_PlayStarted = -1f;
            s_Started = false;
            s_Conversation = null;
            DemoViewRequest.Set();
            RecordingHold.Hold(); // the conversation waits until the encoder is up
            EditorApplication.isPlaying = true;
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying || !IsRunning)
                return;

            if (!s_Started && Shot.TryParse(SessionState.GetString(k_CurrentKey, string.Empty), out var shot))
            {
                s_Started = true;

                // Released in a finally so an encoder that fails to start cannot
                // leave the conversation waiting for it for ever.
                try
                {
                    StartRecording(shot);
                }
                finally
                {
                    RecordingHold.Release();
                }
            }

            s_Conversation ??= UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();

            if (s_PlayStarted < 0f)
                s_PlayStarted = Time.time;

            var started = s_Conversation != null && s_Conversation.Elapsed >= 0f;
            if (!started && Time.time - s_PlayStarted > k_StartTimeoutSeconds)
            {
                Debug.LogError($"Demo renderer: the conversation has not started after {k_StartTimeoutSeconds:0} s, " +
                               "so nothing would be recorded. The usual causes are a segment Unity has not imported " +
                               "yet and an audio output device that is not running. Queue abandoned.");
                Report("ABANDONED: the conversation never started");
                SessionState.EraseString(k_QueueKey);
                EditorApplication.isPlaying = false;
                return;
            }

            if (s_StopAt < 0f && s_Conversation != null && s_Conversation.HasFinished)
                s_StopAt = Time.time + TailSeconds(s_Conversation);

            if (s_StopAt > 0f && Time.time >= s_StopAt)
                EditorApplication.isPlaying = false; // ExitingPlayMode stops the recorder
        }

        /// <summary>
        /// How long the take holds after the voices stop. The segment's own
        /// number, because the hold is a property of what was played: read
        /// literally, a take ends on the last syllable.
        /// </summary>
        static float TailSeconds(RecordedConversation conversation) =>
            conversation.HoldSeconds > 0f ? conversation.HoldSeconds : k_DefaultTailSeconds;

        static void StartRecording(Shot shot)
        {
            Application.runInBackground = true; // keep rendering while the editor is unfocused

            var directory = shot.Directory;
            System.IO.Directory.CreateDirectory(
                Path.Combine(Application.dataPath, "..", directory));

            var frames = ScriptableObject.CreateInstance<ImageRecorderSettings>();
            frames.name = "DemoFrames";
            frames.Enabled = true;
            frames.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.PNG;
            frames.CaptureAlpha = false;

            // The demo camera by tag, not the Game view: the participant's camera
            // is still in the scene, the Game view can be any shape at all, and a
            // take has to be 1920x1080 whatever shape it is.
            frames.imageInputSettings = new CameraInputSettings
            {
                Source = ImageSource.TaggedCamera,
                CameraTag = DemoCameraSetup.CameraTag,
                OutputWidth = OutputWidth,
                OutputHeight = OutputHeight,
            };
            frames.OutputFile = $"{directory}/frame_{DefaultWildcard.Frame}";

            var audio = ScriptableObject.CreateInstance<AudioRecorderSettings>();
            audio.name = "DemoAudio";
            audio.Enabled = true;
            audio.OutputFile = $"{directory}/audio";

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(frames);
            settings.AddRecorderSettings(audio);
            settings.SetRecordModeToManual();
            settings.FrameRate = FrameRate;

            // Locked game clock: game time advances exactly 1/FrameRate per
            // recorded frame, so an 18.55 s segment is 556 frames however fast
            // the machine actually renders, and the sound stays on the picture.
            settings.CapFrameRate = true;

            s_Controller = new RecorderController(settings);
            s_Controller.PrepareRecording();
            s_Controller.StartRecording();
            Debug.Log($"Demo renderer: recording {shot.Label} into {directory}/.");
        }

        static void StopRecording()
        {
            if (s_Controller == null)
                return;

            s_Controller.StopRecording();
            s_Controller = null;
            s_Conversation = null;
            s_StopAt = -1f;
        }

        /// <summary>
        /// Check the take that just played, write its manifest, and move on.
        /// </summary>
        static void OnPlaySessionEnded()
        {
            if (!IsRunning)
                return;

            var current = SessionState.GetString(k_CurrentKey, string.Empty);
            SessionState.EraseString(k_CurrentKey);

            if (Shot.TryParse(current, out var shot))
            {
                var frames = CountFrames(shot);
                if (frames == 0)
                {
                    Debug.LogError($"Demo renderer: {shot.Label}: no frames were written.");
                    Report($"FAIL  {shot.Label}: no frames written");
                }
                else
                {
                    WriteManifest(shot, frames);
                    Report($"ok    {shot.Label}: {frames} frames");
                }
            }

            Advance();
        }

        static int CountFrames(Shot shot)
        {
            var directory = Path.Combine(Application.dataPath, "..", shot.Directory);
            return System.IO.Directory.Exists(directory)
                ? System.IO.Directory.GetFiles(directory, "frame_*.png").Length
                : 0;
        }

        /// <summary>
        /// What stage two needs to know about a take that the frames themselves
        /// cannot say: which clip and method it is, which seed it was drawn at,
        /// how fast it runs, and where the segment with the transcript lives.
        /// </summary>
        static void WriteManifest(Shot shot, int frames)
        {
            var framing = AssetDatabase.LoadAssetAtPath<DemoCameraFraming>(DemoCameraSetup.FramingAssetPath);
            var manifest = new TakeManifest
            {
                schema = "gazecontrol.demo-take/1",
                clip = shot.Clip,
                condition = shot.Condition.ToString(),
                seed = shot.Seed,
                segmentPath = shot.SegmentPath.Replace('\\', '/'),
                frameRate = FrameRate,
                frameCount = frames,
                width = OutputWidth,
                height = OutputHeight,
                renderedUtc = DateTime.UtcNow.ToString("O"),
                pullBackMetres = framing != null ? framing.PullBackMetres : 0f,
                heightMetres = framing != null ? framing.HeightMetres : 0f,
                pitchDegrees = framing != null ? framing.PitchDegrees : 0f,
                verticalFieldOfView = framing != null ? framing.VerticalFieldOfView : 0f,
            };

            var path = Path.Combine(Application.dataPath, "..", shot.Directory, "take.json");
            File.WriteAllText(path, JsonUtility.ToJson(manifest, prettyPrint: true));
        }

        static void Report(string line)
        {
            var report = SessionState.GetString(k_ReportKey, string.Empty);
            SessionState.SetString(k_ReportKey, string.IsNullOrEmpty(report) ? line : $"{report}\n{line}");
        }

        static void Finish(RecordedConversation conversation, GazeConditionRunner runner)
        {
            SessionState.EraseBool(k_ActiveKey);
            SessionState.EraseString(k_QueueKey);
            DemoViewRequest.Clear();
            RecordingHold.Release(); // an abandoned queue must not leave the next Play waiting

            var restore = SessionState.GetString(k_RestoreKey, string.Empty);
            SessionState.EraseString(k_RestoreKey);

            var report = SessionState.GetString(k_ReportKey, string.Empty);
            SessionState.EraseString(k_ReportKey);
            var started = SessionState.GetString(k_StartedKey, string.Empty);
            SessionState.EraseString(k_StartedKey);
            WriteReport(report, started);

            RestoreScene(conversation, runner, restore);

            var failures = report.Split('\n').Count(l => l.StartsWith("FAIL", StringComparison.Ordinal));
            if (failures > 0)
                Debug.LogError($"Demo renderer: done with {failures} take(s) that wrote nothing; see the report.");
            else
                Debug.Log($"Demo renderer: done. Compose with " +
                          $"uv run python Tools/compose_demo_video.py --takes {OutputRoot}");
        }

        static void RestoreScene(RecordedConversation conversation, GazeConditionRunner runner, string restore)
        {
            if (runner == null || string.IsNullOrEmpty(restore))
                return;

            var parts = restore.Split('|');
            if (parts.Length != 7
                || !Enum.TryParse<GazeConditionRunner.TrackMode>(parts[0], out var mode)
                || !Enum.TryParse<GazeConditionRunner.GazeCondition>(parts[1], out var condition)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
                || !bool.TryParse(parts[4], out var logging)
                || !bool.TryParse(parts[5], out var sessionEnabled)
                || !bool.TryParse(parts[6], out var xrOnPlay))
                return;

            runner.Tracks = mode;
            runner.Condition = condition;
            runner.BaseSeed = seed;
            runner.LoggingEnabled = logging;
            EditorUtility.SetDirty(runner);

            if (conversation != null)
            {
                conversation.SegmentPath = parts[3];
                EditorUtility.SetDirty(conversation);
            }

            var session = UnityEngine.Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            if (session != null && session.enabled != sessionEnabled)
            {
                session.enabled = sessionEnabled;
                EditorUtility.SetDirty(session);
            }

            var rig = UnityEngine.Object.FindFirstObjectByType<XrParticipantRig>(FindObjectsInactive.Include);
            if (rig != null && rig.StartXrOnPlay != xrOnPlay)
            {
                rig.StartXrOnPlay = xrOnPlay;
                EditorUtility.SetDirty(rig);
            }
        }

        /// <summary>
        /// One line per take under <c>output/</c>, so a render left running can be
        /// read afterwards without scrolling a console a domain reload may have
        /// cleared.
        /// </summary>
        static void WriteReport(string report, string started)
        {
            if (string.IsNullOrEmpty(report))
                return;

            try
            {
                System.IO.Directory.CreateDirectory("output");
                var path = Path.Combine("output", $"demo_render_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, new StringBuilder()
                    .AppendLine($"started {started}")
                    .AppendLine($"finished {DateTime.Now:O}")
                    .AppendLine(report)
                    .ToString());
                Debug.Log($"Demo renderer: report written to {path}.");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"Demo renderer: could not write the report: {e.Message}\n{report}");
            }
        }

        /// <summary>What stage two reads beside each take's frames.</summary>
        [Serializable]
        class TakeManifest
        {
            public string schema;
            public string clip;
            public string condition;
            public int seed;
            public string segmentPath;
            public int frameRate;
            public int frameCount;
            public int width;
            public int height;
            public string renderedUtc;
            public float pullBackMetres;
            public float heightMetres;
            public float pitchDegrees;
            public float verticalFieldOfView;
        }
    }
}
