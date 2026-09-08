using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GazeControl.ThreeParty;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → 3People → Session Browser: pick a three-party session and a
    /// stretch of it, read what is said in it, and watch it.
    ///
    /// <para>The counterpart to <c>Tools/find_3people_segments.py --inspect</c>,
    /// which prints a candidate window as a transcript with its events marked.
    /// The same reading, next to the button that plays it — because the two
    /// things that disqualify a window cannot be seen any other way: a caption
    /// that is microphone bleed rather than speech, and an event built on one.
    /// </para>
    ///
    /// <para>The ranking the finder writes is only a starting point here. A row
    /// loads its window; nothing stops the whole session being played instead,
    /// which is what "watch the originals first" means.</para>
    /// </summary>
    public sealed class ThreePartySessionBrowser : EditorWindow
    {
        const string k_DefaultCandidates = "output/3people_segments.csv";

        /// <summary>Context either side of a candidate window, seconds.</summary>
        float _pad = 2f;

        string _motionRoot = ThreePartySources.DefaultMotionRoot;
        string _dataRoot = ThreePartySources.DefaultDataRoot;
        string _eventRoot = ThreePartySources.DefaultEventRoot;
        string _candidatesPath = k_DefaultCandidates;

        string[] _dates = Array.Empty<string>();
        int[] _sessions = Array.Empty<int>();
        int _dateIndex;
        int _sessionIndex;
        float _start;
        float _duration = 30f;
        bool _loop = true;
        bool _fromListenerSeat;

        List<Candidate> _candidates;
        string _candidatesError;
        Transcript _transcript;

        Vector2 _candidateScroll;
        Vector2 _transcriptScroll;

        [MenuItem("GazeControl/3People/Session Browser")]
        public static void Open()
        {
            var window = GetWindow<ThreePartySessionBrowser>();
            window.titleContent = new GUIContent("3People Sessions");
            window.minSize = new Vector2(560f, 480f);
            window.Show();
        }

        void OnEnable()
        {
            RefreshDates();
        }

        void OnGUI()
        {
            DrawRoots();
            DrawSelection();
            DrawPlaybackButtons();
            EditorGUILayout.Space();
            DrawCandidates();
            EditorGUILayout.Space();
            DrawTranscript();
        }

        void DrawRoots()
        {
            EditorGUILayout.LabelField("Sources", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _motionRoot = EditorGUILayout.TextField(
                new GUIContent("Converted motion", "Written by Tools/convert_3people_smplx.py"), _motionRoot);
            if (EditorGUI.EndChangeCheck())
                RefreshDates();

            _dataRoot = EditorGUILayout.TextField(
                new GUIContent("Dataset root", "{TPC_DATA}: audio under audio_text/, transcripts under mocap/"), _dataRoot);
            _eventRoot = EditorGUILayout.TextField(
                new GUIContent("End-of-turn tables", "{TPC_EOT}"), _eventRoot);

            if (_dates.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No converted sessions under {_motionRoot}. Run Tools/convert_3people_smplx.py first.",
                    MessageType.Warning);
            }
        }

        void DrawSelection()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Session", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(_dates.Length == 0))
            {
                EditorGUI.BeginChangeCheck();
                _dateIndex = EditorGUILayout.Popup("Date", _dateIndex, _dates);
                if (EditorGUI.EndChangeCheck())
                    RefreshSessions();

                EditorGUI.BeginChangeCheck();
                _sessionIndex = EditorGUILayout.Popup("Session", _sessionIndex,
                    _sessions.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray());
                if (EditorGUI.EndChangeCheck())
                    _transcript = null;

                _start = EditorGUILayout.FloatField(
                    new GUIContent("Start (s)", "Seconds into the recording; the audio, motion and events share this clock"),
                    _start);
                _duration = EditorGUILayout.FloatField(
                    new GUIContent("Duration (s)", "0 plays to the end of the session and reads the whole track into memory"),
                    _duration);
                _loop = EditorGUILayout.Toggle("Loop the window", _loop);
                _fromListenerSeat = EditorGUILayout.Toggle(
                    new GUIContent("Watch from the listener's seat",
                        "Stand where the participant will: at the mean of the listener's eye position over the "
                        + "window, facing the two speakers' midpoint at the first frame. V toggles it in play."),
                    _fromListenerSeat);
            }
        }

        void DrawPlaybackButtons()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_dates.Length == 0 || _sessions.Length == 0))
                {
                    if (GUILayout.Button(EditorApplication.isPlaying ? "Play window" : "Play window (enters play mode)"))
                        Play(_duration);

                    if (GUILayout.Button("Play whole session"))
                        Play(0f);
                }

                using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("Stop", GUILayout.Width(70f)))
                        FindReplay()?.Stop();
                }
            }

            if (!File.Exists(ThreePartyReplaySetup.ScenePath))
            {
                EditorGUILayout.HelpBox(
                    "The replay scene does not exist yet — run GazeControl → 3People → Set Up Replay Scene.",
                    MessageType.Info);
            }
        }

        void DrawCandidates()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Candidate windows", EditorStyles.boldLabel, GUILayout.Width(150f));
                _candidatesPath = EditorGUILayout.TextField(_candidatesPath);
                _pad = EditorGUILayout.FloatField(new GUIContent("Pad", "Context either side, seconds"), _pad,
                    GUILayout.Width(90f));
                if (GUILayout.Button("Reload", GUILayout.Width(60f)))
                    _candidates = null;
            }

            _candidates ??= LoadCandidates(_candidatesPath, out _candidatesError);

            if (_candidates.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _candidatesError ?? $"No rows in {_candidatesPath}. Write one with " +
                    "Tools/find_3people_segments.py --csv output/3people_segments.csv.",
                    MessageType.Info);
                return;
            }

            using var scroll = new EditorGUILayout.ScrollViewScope(_candidateScroll, GUILayout.Height(150f));
            _candidateScroll = scroll.scrollPosition;

            foreach (var candidate in _candidates)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button($"#{candidate.Rank}", GUILayout.Width(44f)))
                        Select(candidate);

                    EditorGUILayout.LabelField(candidate.Summary);
                }
            }
        }

        void DrawTranscript()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("What is said in this window", EditorStyles.boldLabel);
                if (GUILayout.Button("Read", GUILayout.Width(60f)))
                    _transcript = ReadTranscript();
            }

            if (_transcript == null)
            {
                EditorGUILayout.HelpBox(
                    "Read the transcript before trusting a window: the captions are automatic, and microphone " +
                    "bleed puts a phantom line — and so a phantom event — on the wrong participant's track.",
                    MessageType.None);
                return;
            }

            if (_transcript.Error != null)
            {
                EditorGUILayout.HelpBox(_transcript.Error, MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(_transcript.Roles, EditorStyles.miniLabel);

            using var scroll = new EditorGUILayout.ScrollViewScope(_transcriptScroll, GUILayout.ExpandHeight(true));
            _transcriptScroll = scroll.scrollPosition;
            foreach (var line in _transcript.Lines)
                EditorGUILayout.LabelField(line, EditorStyles.label);
        }

        void Select(Candidate candidate)
        {
            _dateIndex = Math.Max(Array.IndexOf(_dates, candidate.Date), 0);
            RefreshSessions();
            _sessionIndex = Math.Max(Array.IndexOf(_sessions, candidate.Session), 0);
            _start = Mathf.Max(candidate.StartSeconds - _pad, 0f);
            _duration = candidate.EndSeconds - candidate.StartSeconds + 2f * _pad;
            _transcript = ReadTranscript();
        }

        void Play(float duration)
        {
            var date = _dates[_dateIndex];
            var session = _sessions[_sessionIndex];

            if (EditorApplication.isPlaying)
            {
                var running = FindReplay();
                if (running == null)
                {
                    Debug.LogError("3People Session Browser: no ThreePartyReplay in the running scene.");
                    return;
                }

                running.SetWindow(date, session, _start, duration);
                running.Loop = _loop;
                running.ViewFromListenerSeat = _fromListenerSeat;
                _ = running.PlayAsync(running.destroyCancellationToken);
                return;
            }

            if (!OpenReplayScene())
                return;

            var replay = FindReplay();
            if (replay == null)
            {
                Debug.LogError(
                    $"3People Session Browser: {ThreePartyReplaySetup.ScenePath} has no ThreePartyReplay — " +
                    "run GazeControl → 3People → Set Up Replay Scene.");
                return;
            }

            Undo.RecordObject(replay, "Select 3People window");
            replay.MotionRoot = _motionRoot;
            replay.DataRoot = _dataRoot;
            replay.EventRoot = _eventRoot;
            replay.SetWindow(date, session, _start, duration);
            replay.Loop = _loop;
            replay.ViewFromListenerSeat = _fromListenerSeat;
            replay.AutoplayOnStart = true;
            EditorSceneManager.MarkSceneDirty(replay.gameObject.scene);

            EditorApplication.EnterPlaymode();
        }

        static bool OpenReplayScene()
        {
            if (!File.Exists(ThreePartyReplaySetup.ScenePath))
            {
                Debug.LogError(
                    "3People Session Browser: run GazeControl → 3People → Set Up Replay Scene first.");
                return false;
            }

            var open = EditorSceneManager.GetActiveScene();
            if (open.path == ThreePartyReplaySetup.ScenePath)
                return true;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;

            EditorSceneManager.OpenScene(ThreePartyReplaySetup.ScenePath, OpenSceneMode.Single);
            return true;
        }

        static ThreePartyReplay FindReplay() =>
            FindAnyObjectByType<ThreePartyReplay>(FindObjectsInactive.Include);

        void RefreshDates()
        {
            _dates = ThreePartySources.Dates(_motionRoot);
            _dateIndex = Mathf.Clamp(_dateIndex, 0, Mathf.Max(_dates.Length - 1, 0));
            RefreshSessions();
        }

        void RefreshSessions()
        {
            _sessions = _dates.Length > 0
                ? ThreePartySources.Sessions(_motionRoot, _dates[_dateIndex])
                : Array.Empty<int>();

            _sessionIndex = Mathf.Clamp(_sessionIndex, 0, Mathf.Max(_sessions.Length - 1, 0));
            _transcript = null;
        }

        Transcript ReadTranscript()
        {
            if (_dates.Length == 0 || _sessions.Length == 0)
                return new Transcript("No session selected.");

            var date = _dates[_dateIndex];
            var session = _sessions[_sessionIndex];
            var end = _duration > 0f ? _start + _duration : float.MaxValue;

            ThreePartyClip[] clips;
            try
            {
                clips = ThreePartySources.Resolve(_motionRoot, _dataRoot, date, session);
            }
            catch (Exception e)
            {
                return new Transcript(e.Message);
            }

            var entries = new List<(float Time, string Text)>();
            var speakers = new SortedSet<int>();

            foreach (var clip in clips)
            {
                if (clip.CaptionPath == null)
                    continue;

                foreach (var utterance in ThreePartyCaptionTrack.Load(clip.CaptionPath).Utterances)
                {
                    if (utterance.EndSeconds < _start || utterance.StartSeconds > end)
                        continue;

                    speakers.Add(clip.Pc);
                    var tag = utterance.IsTag ? "  (recogniser tag)" : string.Empty;
                    entries.Add((utterance.StartSeconds,
                        $"{utterance.StartSeconds,8:F2}  {clip.Label,-16} {utterance.Text}{tag}"));
                }
            }

            var takers = new SortedSet<int>();
            var eventPath = ThreePartySources.EventPath(_eventRoot, date, session);
            if (eventPath != null)
            {
                foreach (var annotated in ThreePartyEvent.Load(eventPath))
                {
                    if (annotated.TurnSeconds < _start || annotated.TurnSeconds > end)
                        continue;

                    takers.Add(annotated.FirstSpeaker);
                    takers.Add(annotated.SecondSpeaker);
                    entries.Add((annotated.TurnSeconds,
                        $"{annotated.TurnSeconds,8:F2}  >>> {annotated.TypeName}: " +
                        $"PC {annotated.FirstSpeaker} to PC {annotated.SecondSpeaker}"));
                }
            }

            entries.Sort((a, b) => a.Time.CompareTo(b.Time));

            var bystanders = Enumerable.Range(1, ThreePartySources.ParticipantCount).Where(pc => !takers.Contains(pc));
            var roles = takers.Count == 0
                ? "No annotated end-of-turn event in this window."
                : $"Turn boundaries between PC {string.Join(", PC ", takers)} — " +
                  $"never taking a turn: {(bystanders.Any() ? "PC " + string.Join(", PC ", bystanders) : "nobody")}. " +
                  $"Speaking at all: PC {string.Join(", PC ", speakers)}.";

            return new Transcript(roles, entries.Select(entry => entry.Text).ToList());
        }

        static List<Candidate> LoadCandidates(string path, out string error)
        {
            error = null;
            var candidates = new List<Candidate>();
            var full = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
            if (!File.Exists(full))
                return candidates;

            try
            {
                foreach (var line in File.ReadLines(full).Skip(1))
                {
                    var fields = line.Split(',');
                    if (fields.Length < 20)
                        continue;

                    candidates.Add(new Candidate(fields));
                }
            }
            catch (Exception e)
            {
                error = $"{path}: {e.Message}";
            }

            return candidates;
        }

        /// <summary>One row of the finder's ranking, as much of it as is worth showing.</summary>
        readonly struct Candidate
        {
            public Candidate(IReadOnlyList<string> fields)
            {
                Rank = Field(fields[0]);
                Date = fields[1];
                Session = (int)Field(fields[2]);
                StartSeconds = Field(fields[3]);
                EndSeconds = Field(fields[4]);
                Summary =
                    $"{fields[1]} S{fields[2]}  {Field(fields[3]),7:F1}-{Field(fields[4]),7:F1} s " +
                    $"({Field(fields[5]),5:F1} s)   {fields[7]} event(s) type {fields[8]}   " +
                    $"takers PC {fields[13]}/{fields[14]}, bystander PC {fields[15]} {fields[16]}   " +
                    $"{fields[19]} glitch frames";
            }

            public float Rank { get; }
            public string Date { get; }
            public int Session { get; }
            public float StartSeconds { get; }
            public float EndSeconds { get; }
            public string Summary { get; }

            static float Field(string text) =>
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0f;
        }

        /// <summary>The window read as text: who does what, and every line in time order.</summary>
        sealed class Transcript
        {
            public Transcript(string error)
            {
                Error = error;
                Lines = new List<string>();
                Roles = string.Empty;
            }

            public Transcript(string roles, List<string> lines)
            {
                Roles = roles;
                Lines = lines;
            }

            public string Error { get; }
            public string Roles { get; }
            public List<string> Lines { get; }
        }
    }
}
