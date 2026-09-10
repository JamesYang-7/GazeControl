using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Gaze.Policy;
using GazeControl.Xr;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Bakes gaze tracks by playing the scene once per take and recording what
    /// each policy decided, so the study can replay a file rather than
    /// re-derive a take.
    ///
    /// <para>Baking runs the real thing rather than an offline simulation: the
    /// live path is what a study session replays, and baseline B's voice
    /// detection reads the audio at playback position, which an offline harness
    /// would have to reimplement and could get subtly wrong. A bake is therefore
    /// exactly what the policy did, by construction.</para>
    ///
    /// <para>A queue of takes — segment, seed, condition — is carried through
    /// play sessions in <see cref="SessionState"/>, which survives the domain
    /// reload each Play causes (the same mechanism <c>DemoRecorder</c> uses).
    /// Every take is <b>validated when its play session ends</b>: the track must
    /// exist and carry one decision per 60 Hz tick of the segment for every
    /// agent, or the take is queued again, up to <see cref="k_MaxAttempts"/>
    /// times. An unfocused editor can stall the frame clock the take is measured
    /// on, and a short track replayed in a session would end the agents' gaze
    /// before the clip does.</para>
    ///
    /// <para>The session runner is disabled for the duration and restored after,
    /// because its <c>Awake</c> otherwise takes the conversation's autoplay
    /// away from every plain Play — which is right for a participant run and
    /// leaves a bake waiting on a framing screen nobody will dismiss.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class GazeTrackBaker
    {
        const string k_QueueKey = "GazeControl.GazeTrackBaker.Queue";
        const string k_RestoreKey = "GazeControl.GazeTrackBaker.Restore";
        const string k_CurrentKey = "GazeControl.GazeTrackBaker.Current";
        const string k_ReportKey = "GazeControl.GazeTrackBaker.Report";
        const string k_StartedKey = "GazeControl.GazeTrackBaker.Started";

        // "A bake is running" is its own flag rather than "the queue is not
        // empty": the queue is emptied when the *last* take starts, so the
        // emptiness test made the final play session look like no bake at all.
        // It then ran forever — nothing stopped play, so the track was only
        // written if someone stopped it by hand, and the scene was left in Bake
        // mode where the next Play would overwrite what it had just made.
        const string k_ActiveKey = "GazeControl.GazeTrackBaker.Active";

        /// <summary>Recorded past the last decision so the track covers any hold at the end.</summary>
        const float k_TailSeconds = 0.5f;

        /// <summary>A take is given this many play sessions to produce a full-length track.</summary>
        const int k_MaxAttempts = 3;

        /// <summary>The seeds a full study-2 matrix is baked at; one is chosen and recorded per clip.</summary>
        public static readonly int[] StudySeeds = Enumerable.Range(1, 12).ToArray();

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
        static double s_DspAtPlayStart;

        /// <summary>One queued play session.</summary>
        readonly struct Take
        {
            public Take(string segmentPath, int seed, GazeConditionRunner.GazeCondition condition, int attempt)
            {
                SegmentPath = segmentPath;
                Seed = seed;
                Condition = condition;
                Attempt = attempt;
            }

            public string SegmentPath { get; }
            public int Seed { get; }
            public GazeConditionRunner.GazeCondition Condition { get; }
            public int Attempt { get; }

            public string Label =>
                $"{Path.GetFileName(Path.GetDirectoryName(SegmentPath))} / {Condition} / seed {Seed}";

            public Take Retried() => new(SegmentPath, Seed, Condition, Attempt + 1);

            // Segment paths carry no '|' or ';', so the two are safe separators.
            public string Serialize() =>
                $"{SegmentPath}|{Seed.ToString(CultureInfo.InvariantCulture)}|{Condition}|{Attempt.ToString(CultureInfo.InvariantCulture)}";

            public static bool TryParse(string text, out Take take)
            {
                take = default;
                var parts = text.Split('|');
                if (parts.Length != 4
                    || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
                    || !Enum.TryParse<GazeConditionRunner.GazeCondition>(parts[2], out var condition)
                    || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var attempt))
                    return false;

                take = new Take(parts[0], seed, condition, attempt);
                return true;
            }
        }

        static GazeTrackBaker()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    OnPlaySessionEnded();
            };
        }

        static IEnumerable<GazeConditionRunner.GazeCondition> AllConditions =>
            Enum.GetValues(typeof(GazeConditionRunner.GazeCondition)).Cast<GazeConditionRunner.GazeCondition>();

        /// <summary>Queue every condition for the scene's current clip and seed.</summary>
        public static void BakeAllConditions()
        {
            var (conversation, runner) = FindScene();
            if (runner == null)
                return;

            Start(AllConditions.Select(c => new Take(conversation.SegmentPath, runner.BaseSeed, c, 1)).ToList());
        }

        /// <summary>Queue just the condition the runner is set to, at the scene's clip and seed.</summary>
        public static void BakeCurrentCondition()
        {
            var (conversation, runner) = FindScene();
            if (runner == null)
                return;

            Start(new List<Take> { new(conversation.SegmentPath, runner.BaseSeed, runner.Condition, 1) });
        }

        /// <summary>
        /// GazeControl → Study 2 → Bake Seeds 1-12: every condition at every
        /// study seed for every conversation the session runner lists. Seven
        /// clips give 252 play sessions, a couple of hours; the operator then
        /// chooses one seed per clip from what exists rather than waiting for
        /// a bake on the morning of the study.
        /// </summary>
        [MenuItem("GazeControl/Study 2/Bake Seeds 1-12", priority = 24)]
        public static void BakeStudySeeds()
        {
            if (!StudyScenes.EnsureOpen(StudyScenes.Study2ScenePath, "Study 2 → Bake Seeds 1-12"))
                return;

            var (_, runner) = FindScene();
            if (runner == null)
                return;

            var session = UnityEngine.Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            if (session == null || session.Conversations == null || session.Conversations.Length == 0)
            {
                Debug.LogError("Gaze track baker: no StudySessionRunner with conversations in the open scene.");
                return;
            }

            var takes = new List<Take>();
            foreach (var conversation in session.Conversations)
            {
                var segmentPath = $"{session.SegmentRoot}/{conversation}/segment.json";
                if (!File.Exists(segmentPath))
                {
                    Debug.LogError($"Gaze track baker: {segmentPath} does not exist; export it first. Nothing queued.");
                    return;
                }

                foreach (var seed in StudySeeds)
                {
                    foreach (var condition in AllConditions)
                        takes.Add(new Take(segmentPath, seed, condition, 1));
                }
            }

            Start(takes);
        }

        static (RecordedConversation conversation, GazeConditionRunner runner) FindScene()
        {
            var runner = UnityEngine.Object.FindFirstObjectByType<GazeConditionRunner>();
            var conversation = UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();
            if (runner == null || conversation == null)
            {
                Debug.LogError("Gaze track baker: no GazeConditionRunner and RecordedConversation in the open scene.");
                return (null, null);
            }

            return (conversation, runner);
        }

        static void Start(List<Take> takes)
        {
            var (conversation, runner) = FindScene();
            if (runner == null)
                return;

            if (runner.StudySession)
            {
                Debug.LogError("Gaze track baker: StudySession is on, which refuses any run that is not " +
                               "a Replay. Turn it off to bake.");
                return;
            }

            if (SessionState.GetBool(k_ActiveKey, false))
            {
                Debug.LogError("Gaze track baker: a bake is already running; let it finish first.");
                return;
            }

            // Remembered so a bake does not silently leave the scene in Bake
            // mode, where the next Play would overwrite the track just made —
            // nor on another clip or seed than the one it was left on.
            var session = UnityEngine.Object.FindFirstObjectByType<StudySessionRunner>(FindObjectsInactive.Include);
            var sessionEnabled = session != null && session.enabled;
            var rig = UnityEngine.Object.FindFirstObjectByType<XrParticipantRig>(FindObjectsInactive.Include);
            var xrOnPlay = rig != null && rig.StartXrOnPlay;
            SessionState.SetString(k_RestoreKey,
                $"{runner.Tracks}|{runner.Condition}|{runner.BaseSeed.ToString(CultureInfo.InvariantCulture)}|" +
                $"{conversation.SegmentPath}|{sessionEnabled}|{xrOnPlay}");

            // A bake reads no headset, and bringing the loader up on every one
            // of hundreds of play sessions costs seconds each and recentres a
            // view nobody is looking through.
            if (rig != null && rig.StartXrOnPlay)
            {
                rig.StartXrOnPlay = false;
                EditorUtility.SetDirty(rig);
            }
            SessionState.SetString(k_QueueKey, string.Join(";", takes.Select(t => t.Serialize())));
            SessionState.SetString(k_ReportKey, string.Empty);
            SessionState.SetString(k_StartedKey, DateTime.Now.ToString("O"));
            SessionState.SetBool(k_ActiveKey, true);

            if (session != null && session.enabled)
            {
                session.enabled = false;
                EditorUtility.SetDirty(session);
            }

            ResetAudio();
            Debug.Log($"Gaze track baker: {takes.Count} take(s) queued.");
            Advance();
        }

        /// <summary>
        /// Re-initialise the audio system before a run. The conversation starts
        /// its clock on a scheduled DSP instant, and an editor whose output
        /// device failed to initialise (seen 2026-09-08 after a headset was
        /// unplugged: "FMOD failed to switch back to normal output") has a DSP
        /// clock that never advances, so every take waited forever and the
        /// baker abandoned the queue with "the conversation never started".
        /// A reset brought the clock back; done unconditionally because it is
        /// cheap and the failure is silent.
        /// </summary>
        static void ResetAudio()
        {
            var before = AudioSettings.dspTime;
            if (!AudioSettings.Reset(AudioSettings.GetConfiguration()))
                Debug.LogWarning("Gaze track baker: the audio system refused to reset; if no take starts, check the output device.");
            else if (before > 0 && Mathf.Approximately((float)before, (float)AudioSettings.dspTime))
                Debug.Log("Gaze track baker: audio system reset.");
        }

        /// <summary>Start the next queued take, or finish and restore the scene.</summary>
        static void Advance()
        {
            if (!SessionState.GetBool(k_ActiveKey, false))
                return;

            var (conversation, runner) = FindScene();
            if (runner == null)
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

            if (!Take.TryParse(queue[0], out var take))
            {
                Debug.LogError($"Gaze track baker: '{queue[0]}' is not a queued take.");
                Finish(conversation, runner);
                return;
            }

            SessionState.SetString(k_QueueKey, string.Join(";", queue.Skip(1)));
            SessionState.SetString(k_CurrentKey, take.Serialize());

            conversation.SegmentPath = take.SegmentPath;
            EditorUtility.SetDirty(conversation);
            runner.BaseSeed = take.Seed;
            runner.Condition = take.Condition;
            runner.Tracks = GazeConditionRunner.TrackMode.Bake;
            EditorUtility.SetDirty(runner);

            var attempt = take.Attempt > 1 ? $" (attempt {take.Attempt})" : string.Empty;
            Debug.Log($"Gaze track baker: baking {take.Label}{attempt} — {queue.Length - 1} take(s) to follow.");
            s_StopAt = -1f;
            s_PlayStarted = -1f;
            s_Conversation = null;
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Check the take that just played and either move on or queue it again.
        /// </summary>
        static void OnPlaySessionEnded()
        {
            if (!SessionState.GetBool(k_ActiveKey, false))
                return;

            var current = SessionState.GetString(k_CurrentKey, string.Empty);
            SessionState.EraseString(k_CurrentKey);

            if (Take.TryParse(current, out var take))
            {
                var problem = Validate(take);
                if (problem == null)
                {
                    Report($"ok    {take.Label}" + (take.Attempt > 1 ? $" (attempt {take.Attempt})" : string.Empty));
                }
                else if (take.Attempt < k_MaxAttempts)
                {
                    Debug.LogWarning($"Gaze track baker: {take.Label}: {problem}; queued again.");
                    Report($"retry {take.Label}: {problem}");
                    var queue = SessionState.GetString(k_QueueKey, string.Empty);
                    SessionState.SetString(k_QueueKey,
                        string.IsNullOrEmpty(queue) ? take.Retried().Serialize() : $"{take.Retried().Serialize()};{queue}");
                }
                else
                {
                    Debug.LogError($"Gaze track baker: {take.Label}: {problem} after {k_MaxAttempts} attempts; giving up on it.");
                    Report($"FAIL  {take.Label}: {problem} after {k_MaxAttempts} attempts");
                }
            }

            Advance();
        }

        /// <summary>
        /// Null when the take's track is complete: it exists, was baked against
        /// this segment at this seed, and every agent carries at least one
        /// decision per tick of the segment's length.
        /// </summary>
        static string Validate(Take take)
        {
            var directory = Path.GetDirectoryName(take.SegmentPath);
            var trackPath = GazeTrack.PathFor(directory, take.Condition.ToString(), take.Seed);
            if (!File.Exists(trackPath))
                return "no track was written";

            GazeTrack track;
            DemoSegment segment;
            try
            {
                track = GazeTrack.Load(trackPath);
                segment = DemoSegment.Load(take.SegmentPath);
            }
            catch (Exception e)
            {
                return $"unreadable: {e.Message}";
            }

            if (track.baseSeed != take.Seed)
                return $"track carries seed {track.baseSeed}";

            if (track.segment != segment.name)
                return $"track was baked against '{track.segment}'";

            // The runner decides once per 60 Hz tick from 0 to the segment's end,
            // so a complete track has duration × 60 samples (study 1's all did,
            // exactly). Fewer means the clock stalled or the take was cut.
            var expected = Mathf.FloorToInt(segment.durationSeconds * track.decisionHz) - 1;
            foreach (var agent in track.agents)
            {
                if (agent.samples.Length < expected)
                    return $"agent {agent.agentId} has {agent.samples.Length} decisions, expected at least {expected}";
            }

            return null;
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

            var restore = SessionState.GetString(k_RestoreKey, string.Empty);
            SessionState.EraseString(k_RestoreKey);

            var report = SessionState.GetString(k_ReportKey, string.Empty);
            SessionState.EraseString(k_ReportKey);
            var started = SessionState.GetString(k_StartedKey, string.Empty);
            SessionState.EraseString(k_StartedKey);
            WriteReport(report, started);

            if (runner == null || string.IsNullOrEmpty(restore))
                return;

            var parts = restore.Split('|');
            if (parts.Length == 6
                && Enum.TryParse<GazeConditionRunner.TrackMode>(parts[0], out var mode)
                && Enum.TryParse<GazeConditionRunner.GazeCondition>(parts[1], out var condition)
                && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
                && bool.TryParse(parts[4], out var sessionEnabled)
                && bool.TryParse(parts[5], out var xrOnPlay))
            {
                runner.Tracks = mode;
                runner.Condition = condition;
                runner.BaseSeed = seed;
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

            var failures = report.Split('\n').Count(l => l.StartsWith("FAIL", StringComparison.Ordinal));
            if (failures > 0)
                Debug.LogError($"Gaze track baker: done with {failures} take(s) that never produced a full track; see the report.");
            else
                Debug.Log("Gaze track baker: done. Set Tracks to Replay for a study session.");
        }

        /// <summary>
        /// One line per take under <c>output/</c>, so a bake run overnight can
        /// be read the next morning without scrolling a console that a domain
        /// reload may have cleared.
        /// </summary>
        static void WriteReport(string report, string started)
        {
            if (string.IsNullOrEmpty(report))
                return;

            try
            {
                Directory.CreateDirectory("output");
                var path = Path.Combine("output", $"bake_report_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                var text = new StringBuilder()
                    .AppendLine($"started {started}")
                    .AppendLine($"finished {DateTime.Now:O}")
                    .AppendLine(report)
                    .ToString();
                File.WriteAllText(path, text);
                Debug.Log($"Gaze track baker: report written to {path}.");
            }
            catch (IOException e)
            {
                Debug.LogWarning($"Gaze track baker: could not write the report: {e.Message}\n{report}");
            }
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(k_ActiveKey, false))
                return;

            s_Conversation ??= UnityEngine.Object.FindFirstObjectByType<RecordedConversation>();

            if (s_PlayStarted < 0f)
            {
                s_PlayStarted = Time.time;
                s_DspAtPlayStart = AudioSettings.dspTime;
            }

            var started = s_Conversation != null && s_Conversation.Elapsed >= 0f;
            if (!started && Time.time - s_PlayStarted > k_StartTimeoutSeconds)
            {
                var dspFrozen = Mathf.Approximately((float)s_DspAtPlayStart, (float)AudioSettings.dspTime);
                Debug.LogError(
                    $"Gaze track baker: the conversation has not started after {k_StartTimeoutSeconds:0} s, " +
                    "so nothing would ever be baked. " +
                    (dspFrozen
                        ? "The DSP clock has not advanced since play began: the audio output device is not " +
                          "running, so the scheduled start never arrives. Fix the output device (Edit → Project " +
                          "Settings → Audio, or replug it) and start the bake again."
                        : "The usual cause is a segment Unity has not imported yet — check the console for a " +
                          "load error and run Assets → Refresh.") +
                    " Queue abandoned.");
                Report(dspFrozen
                    ? "ABANDONED: the conversation never started (DSP clock frozen — audio output device down)"
                    : "ABANDONED: the conversation never started");
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
