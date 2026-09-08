using System;
using System.Collections.Generic;
using System.Threading;
using GazeControl.Motion;
using UnityEngine;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// Plays one 3People-2022 session — all three participants at once, on the
    /// capture's own room geometry, with their own microphone tracks and their
    /// own transcripts.
    ///
    /// <para>This is a viewing tool, not a demo. Nothing here decides who is an
    /// agent, who is hidden or who becomes the participant's seat; that decision
    /// is what watching the originals is for. So the three seats are
    /// interchangeable, no gaze policy runs, and the eyes stay where the mocap
    /// left them — the capture's eye tracking is a separate stream this does not
    /// read.</para>
    ///
    /// <para><b>The room places the bodies, not the scene.</b> Each converted
    /// clip carries the participant's real position in the capture room, and the
    /// three clips of a session share that frame, so the motion players run with
    /// <see cref="SmplxMotionPlayer.RootTranslationRelative"/> off and the three
    /// agents land in the triangle the people actually stood in. Their scene
    /// transforms must therefore be identity; the setup command builds them that
    /// way.</para>
    ///
    /// <para>One clock, as in <c>RecordedConversation</c>: the three voices are
    /// scheduled on a single DSP instant and the motion is driven from the frame
    /// clock that starts when that instant arrives. The audio, the captions, the
    /// end-of-turn tables and the converted motion all run from the same zero
    /// (verified on all 23 sessions), so a window is specified as plain seconds
    /// into the recording.</para>
    /// </summary>
    public sealed class ThreePartyReplay : MonoBehaviour
    {
        /// <summary>One place in the room: the body that stands there and the voice that comes from it.</summary>
        [Serializable]
        public sealed class Seat
        {
            [field: SerializeField]
            [field: Tooltip("Motion player on this seat's agent; its clip and frame range come from the session")]
            public SmplxMotionPlayer Motion { get; set; }

            [field: SerializeField]
            [field: Tooltip("This participant's own microphone track plays here")]
            public AudioSource Voice { get; set; }

            /// <summary>Which participant this seat is playing; null until a session loads.</summary>
            public ThreePartyClip Clip { get; set; }

            /// <summary>Their transcript, or null when the session has none.</summary>
            public ThreePartyCaptionTrack Captions { get; set; }

            /// <summary>Head bone, for the on-screen label; bound when the session loads.</summary>
            public Transform Head { get; set; }

            /// <summary>The two eye joints, bound when the session loads.</summary>
            public Transform LeftEye { get; set; }

            /// <inheritdoc cref="LeftEye"/>
            public Transform RightEye { get; set; }

            /// <summary>
            /// Midpoint of the eyes, world space — where this participant is
            /// looking from, and where a participant taking their place stands.
            /// </summary>
            public Vector3 EyeCentre => 0.5f * (LeftEye.position + RightEye.position);
        }

        [field: SerializeField]
        [field: Tooltip("Converted SMPL-X corpus written by Tools/convert_3people_smplx.py")]
        public string MotionRoot { get; set; } = ThreePartySources.DefaultMotionRoot;

        [field: SerializeField]
        [field: Tooltip("The dataset's processed tree: audio under audio_text/, transcripts under mocap/<date>/Caption/")]
        public string DataRoot { get; set; } = ThreePartySources.DefaultDataRoot;

        [field: SerializeField]
        [field: Tooltip("End-of-turn tables; empty to replay without event marks")]
        public string EventRoot { get; set; } = ThreePartySources.DefaultEventRoot;

        [field: SerializeField]
        [field: Tooltip("Recording date, e.g. 12-15-2021")]
        public string Date { get; set; } = "12-15-2021";

        [field: SerializeField]
        [field: Tooltip("Session number within the date")]
        public int Session { get; set; } = 1;

        [field: SerializeField]
        [field: Tooltip("Where to start, seconds into the recording")]
        public float StartSeconds { get; set; }

        [field: SerializeField]
        [field: Tooltip("How long to play; 0 runs to the end of the session (and reads the whole track into memory)")]
        public float DurationSeconds { get; set; } = 30f;

        [field: SerializeField]
        [field: Tooltip("Replay the window when it ends")]
        public bool Loop { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("The three seats, one per participant, in ascending PC order")]
        public Seat[] Seats { get; set; } = Array.Empty<Seat>();

        [field: SerializeField]
        [field: Tooltip("Who the study participant replaces; 0 derives it from the window's events — "
                        + "the participant no end-of-turn event names")]
        public int ListenerPc { get; set; }

        [field: SerializeField]
        [field: Tooltip("Watch from the listener's seat instead of the orbiting camera. Their body is hidden, "
                        + "because in the study it is the participant who is standing there.")]
        public bool ViewFromListenerSeat { get; set; }

        [field: SerializeField]
        [field: Tooltip("Play the inspector's window when the scene starts")]
        public bool AutoplayOnStart { get; set; } = true;

        [field: SerializeField]
        [field: Range(0.05f, 1f)]
        [field: Tooltip("Lead given to the audio engine before playback starts, seconds")]
        public float ScheduleLeadSeconds { get; set; } = 0.2f;

        /// <summary>Seconds into the window; negative before it starts.</summary>
        public float Elapsed => _started ? Time.time - _startTime : -1f;

        /// <summary>Where the playhead is in the whole recording — the clock the events and captions use.</summary>
        public float SessionSeconds => StartSeconds + Mathf.Max(Elapsed, 0f);

        /// <summary>How long the window actually is, once motion and audio are clipped to what exists.</summary>
        public float WindowSeconds { get; private set; }

        /// <summary>True once the window has played out and nothing more will happen.</summary>
        public bool HasFinished { get; private set; }

        /// <summary>True while a window is on screen and running.</summary>
        public bool IsPlaying => _started && !HasFinished;

        /// <summary>The session's end-of-turn events; empty when the session has no table.</summary>
        public IReadOnlyList<ThreePartyEvent> Events => _events;

        public string SessionLabel => $"{Date} Session {Session}";

        /// <summary>
        /// Index into <see cref="Seats"/> of the participant the study
        /// participant replaces — the one no end-of-turn event in the window
        /// names — or -1 when the window does not single anybody out.
        /// </summary>
        public int ListenerSeat { get; private set; } = -1;

        /// <summary>Where a participant taking that seat stands and what they face; only when <see cref="HasViewpoint"/>.</summary>
        public ThreePartyViewpoint Viewpoint { get; private set; }

        public bool HasViewpoint { get; private set; }

        /// <summary>Sampling stride for the seat measurement: 10 Hz over a 60 fps window.</summary>
        const int ViewpointStride = 6;

        readonly List<ThreePartyEvent> _events = new();
        readonly List<Vector3> _eyeSamples = new();
        Renderer[] _listenerRenderers = Array.Empty<Renderer>();
        bool _listenerHidden;
        float _startTime;
        bool _started;
        bool _starting;

        void Awake()
        {
            // The player loop stalls while the editor is unfocused, and this
            // scene is often driven from another window — the session browser,
            // or a script. Audio is scheduled on the DSP clock and would keep
            // running while the frame clock froze, so the window would come back
            // seconds out of step with itself. The runtime flag is the fix and
            // has to be set per play session; the PlayerSettings one does not
            // govern editor play.
            Application.runInBackground = true;
        }

        async Awaitable Start()
        {
            try
            {
                if (AutoplayOnStart)
                    await PlayAsync(destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // play mode ended mid-load; nothing to clean up
            }
        }

        void Update()
        {
            ApplyListenerVisibility();

            if (!_started || HasFinished)
                return;

            var elapsed = Elapsed;
            if (elapsed >= WindowSeconds)
            {
                if (Loop)
                    _ = LoopAsync(destroyCancellationToken);
                else
                    Finish();

                return;
            }

            foreach (var seat in Seats)
                seat.Motion.Seek(elapsed);
        }

        /// <summary>Point the replay at another window; call <see cref="PlayAsync"/> to hear it.</summary>
        public void SetWindow(string date, int session, float startSeconds, float durationSeconds)
        {
            Date = date;
            Session = session;
            StartSeconds = startSeconds;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// Load the current window and play it: resolve the three participants,
        /// arm their motion, read their audio and start all three voices on one
        /// DSP instant.
        /// </summary>
        /// <returns>False when the session could not be loaded; the reason is logged.</returns>
        public async Awaitable<bool> PlayAsync(CancellationToken cancellationToken)
        {
            Stop();

            if (!TryResolve() || !TryArmMotion())
                return false;

            ResolveListener();
            MeasureViewpoint();

            if (!await TryLoadVoicesAsync(cancellationToken))
                return false;

            await StartVoicesAsync(cancellationToken);
            Debug.Log(
                $"{name}: {SessionLabel} {StartSeconds:F2}-{StartSeconds + WindowSeconds:F2} s " +
                $"({WindowSeconds:F2} s, {EventsInWindow} of the session's {_events.Count} end-of-turn events) — " +
                $"{string.Join(", ", Describe())}", this);

            return true;
        }

        /// <summary>How many annotated boundaries fall inside the window being played.</summary>
        public int EventsInWindow
        {
            get
            {
                var end = StartSeconds + WindowSeconds;
                var inside = 0;
                foreach (var annotated in _events)
                {
                    if (annotated.TurnSeconds >= StartSeconds && annotated.TurnSeconds <= end)
                        inside++;
                }

                return inside;
            }
        }

        /// <summary>Silence the voices and leave the bodies where they are.</summary>
        public void Stop()
        {
            foreach (var seat in Seats)
            {
                if (seat.Voice != null)
                    seat.Voice.Stop();
            }

            _started = false;
            HasFinished = false;
        }

        /// <summary>What the seat's participant is saying now, or null. Indexed like <see cref="Seats"/>.</summary>
        public string CaptionOf(int seat) => Seats[seat].Captions?.TextAt(SessionSeconds);

        IEnumerable<string> Describe()
        {
            for (var i = 0; i < Seats.Length; i++)
            {
                if (Seats[i].Clip == null)
                {
                    yield return "empty seat";
                    continue;
                }

                if (i != ListenerSeat)
                {
                    yield return Seats[i].Clip.Label;
                    continue;
                }

                yield return HasViewpoint
                    ? $"{Seats[i].Clip.Label} (listener; seat at {Viewpoint.Position.x:F2}, " +
                      $"{Viewpoint.Position.y:F2}, {Viewpoint.Position.z:F2} facing {Viewpoint.Yaw:F1}°)"
                    : $"{Seats[i].Clip.Label} (listener; no seat measured)";
            }
        }

        /// <summary>
        /// Who the study participant replaces: the participant no end-of-turn
        /// event in the window names, which is the same rule
        /// <c>Tools/find_3people_segments.py</c> selects windows on. Derived
        /// rather than stored, so any window can be watched from the seat — not
        /// only one the finder ranked.
        /// </summary>
        void ResolveListener()
        {
            ListenerSeat = -1;
            _listenerRenderers = Array.Empty<Renderer>();

            if (ListenerPc > 0)
            {
                ListenerSeat = SeatOfPc(ListenerPc);
                if (ListenerSeat < 0)
                    Debug.LogWarning($"{name}: no PC {ListenerPc} in {SessionLabel}; deriving no listener.", this);
            }
            else
            {
                var end = StartSeconds + WindowSeconds;
                var takers = new HashSet<int>();
                foreach (var annotated in _events)
                {
                    if (annotated.TurnSeconds < StartSeconds || annotated.TurnSeconds > end)
                        continue;

                    takers.Add(annotated.FirstSpeaker);
                    takers.Add(annotated.SecondSpeaker);
                }

                // Exactly one, or nobody. No event names nobody; events naming
                // all three leave no listener, and that is a real answer about
                // the window rather than a reason to pick one.
                foreach (var seat in Seats)
                {
                    if (takers.Count == 0 || takers.Contains(seat.Clip.Pc))
                        continue;

                    if (ListenerSeat >= 0)
                    {
                        ListenerSeat = -1;
                        break;
                    }

                    ListenerSeat = SeatOfPc(seat.Clip.Pc);
                }
            }

            if (ListenerSeat >= 0)
                _listenerRenderers = Seats[ListenerSeat].Motion.GetComponentsInChildren<Renderer>(true);

            _listenerHidden = false;
            ApplyListenerVisibility();
        }

        /// <summary>
        /// Measure the seat: the mean of the listener's eye position over the
        /// whole window, and the facing to the two speakers' midpoint at the
        /// first frame. Both are read by posing the skeletons through the
        /// window, so this runs once while the clip is armed and before its
        /// clock starts — never per frame.
        /// </summary>
        void MeasureViewpoint()
        {
            HasViewpoint = false;
            if (ListenerSeat < 0)
                return;

            var listener = Seats[ListenerSeat];
            PoseEveryoneAtTheFirstFrame();

            var speakers = new List<Vector3>(2);
            for (var i = 0; i < Seats.Length; i++)
            {
                if (i != ListenerSeat)
                    speakers.Add(Seats[i].EyeCentre);
            }

            _eyeSamples.Clear();
            var frames = Mathf.RoundToInt(WindowSeconds * ThreePartySources.FrameRate);
            for (var frame = 0; frame < frames; frame += ViewpointStride)
            {
                listener.Motion.ApplyFrame(listener.Motion.StartFrame + frame);
                _eyeSamples.Add(listener.EyeCentre);
            }

            // Back to the first frame, which is where playback is about to start.
            PoseEveryoneAtTheFirstFrame();

            if (!ThreePartyViewpoint.TryCompute(_eyeSamples, speakers[0], speakers[1], out var viewpoint))
            {
                Debug.LogWarning(
                    $"{name}: could not measure {listener.Clip.Label}'s seat — the window has no frames, or the " +
                    "seat is level with the two speakers' midpoint, where a facing is undefined.", this);
                return;
            }

            Viewpoint = viewpoint;
            HasViewpoint = true;
        }

        void PoseEveryoneAtTheFirstFrame()
        {
            foreach (var seat in Seats)
                seat.Motion.ApplyFrame(seat.Motion.StartFrame);
        }

        void ApplyListenerVisibility()
        {
            var hide = ViewFromListenerSeat && ListenerSeat >= 0;
            if (hide == _listenerHidden)
                return;

            foreach (var listenerRenderer in _listenerRenderers)
                listenerRenderer.enabled = !hide;

            _listenerHidden = hide;
        }

        int SeatOfPc(int pc)
        {
            for (var i = 0; i < Seats.Length; i++)
            {
                if (Seats[i].Clip != null && Seats[i].Clip.Pc == pc)
                    return i;
            }

            return -1;
        }

        bool TryResolve()
        {
            if (Seats == null || Seats.Length != ThreePartySources.ParticipantCount)
            {
                Debug.LogError($"{name}: needs exactly {ThreePartySources.ParticipantCount} seats.", this);
                return false;
            }

            ThreePartyClip[] clips;
            try
            {
                clips = ThreePartySources.Resolve(MotionRoot, DataRoot, Date, Session);
            }
            catch (Exception e)
            {
                Debug.LogError($"{name}: {e.Message}", this);
                return false;
            }

            for (var i = 0; i < Seats.Length; i++)
            {
                Seats[i].Clip = clips[i];
                Seats[i].Captions = clips[i].CaptionPath != null
                    ? ThreePartyCaptionTrack.Load(clips[i].CaptionPath)
                    : null;
            }

            LoadEvents();
            return true;
        }

        void LoadEvents()
        {
            _events.Clear();
            if (string.IsNullOrEmpty(EventRoot))
                return;

            var path = ThreePartySources.EventPath(EventRoot, Date, Session);
            if (path == null)
            {
                Debug.LogWarning($"{name}: no end-of-turn table for {SessionLabel}; replaying without event marks.", this);
                return;
            }

            _events.AddRange(ThreePartyEvent.Load(path));
        }

        bool TryArmMotion()
        {
            var startFrame = Mathf.RoundToInt(StartSeconds * ThreePartySources.FrameRate);
            var frameCount = DurationSeconds > 0f
                ? Mathf.RoundToInt(DurationSeconds * ThreePartySources.FrameRate)
                : 0;

            WindowSeconds = float.MaxValue;

            foreach (var seat in Seats)
            {
                if (seat.Motion == null || seat.Voice == null)
                {
                    Debug.LogError($"{name}: a seat is missing its motion player or audio source.", this);
                    return false;
                }

                seat.Motion.ClipPath = seat.Clip.MotionPath;
                seat.Motion.StartFrame = startFrame;
                seat.Motion.FrameCount = frameCount;
                seat.Motion.Loop = false;
                seat.Motion.Autoplay = false; // this component owns the clock
                seat.Motion.ApplyRootTranslation = true;

                // The capture's own room frame, not the scene's vertices: the
                // three clips are mutually consistent, so raw positions are what
                // put the participants where they actually stood.
                seat.Motion.RootTranslationRelative = false;
                seat.Motion.enabled = true;
                seat.Motion.Initialize();

                if (seat.Motion.Clip == null)
                    return false;

                seat.Head = SmplxAnimUtils.FindChildRecursive(seat.Motion.transform, "head");
                seat.LeftEye = SmplxAnimUtils.FindChildRecursive(seat.Motion.transform, "left_eye_smplhf");
                seat.RightEye = SmplxAnimUtils.FindChildRecursive(seat.Motion.transform, "right_eye_smplhf");
                WindowSeconds = Mathf.Min(WindowSeconds, seat.Motion.SegmentDuration);
            }

            if (WindowSeconds <= 0f)
            {
                Debug.LogError(
                    $"{name}: {SessionLabel} has no motion at {StartSeconds:F2} s — the session is shorter than that.",
                    this);
                return false;
            }

            return true;
        }

        async Awaitable<bool> TryLoadVoicesAsync(CancellationToken cancellationToken)
        {
            // Paths are taken off the Unity objects while still on the main
            // thread; the read below runs on a worker and must not touch them.
            var paths = new string[Seats.Length];
            for (var i = 0; i < Seats.Length; i++)
                paths[i] = Seats[i].Clip.AudioPath;

            var segments = new WavSegment[Seats.Length];
            string failure = null;

            await Awaitable.BackgroundThreadAsync();
            try
            {
                for (var i = 0; i < paths.Length; i++)
                    segments[i] = WavSegment.Read(paths[i], StartSeconds, WindowSeconds);
            }
            catch (Exception e)
            {
                failure = e.Message;
            }

            await Awaitable.MainThreadAsync();
            cancellationToken.ThrowIfCancellationRequested();

            if (failure != null)
            {
                Debug.LogError($"{name}: could not read the session audio: {failure}", this);
                return false;
            }

            for (var i = 0; i < Seats.Length; i++)
            {
                var seat = Seats[i];
                if (segments[i].Duration < WindowSeconds - 0.5f)
                {
                    Debug.LogWarning(
                        $"{name}: {seat.Clip.Label}'s audio runs out {WindowSeconds - segments[i].Duration:F1} s " +
                        "before the motion does; the tail of the window is silent.", this);
                }

                seat.Voice.clip = segments[i].ToAudioClip($"{SessionLabel} {seat.Clip.Label}");
                seat.Voice.loop = false;
                seat.Voice.playOnAwake = false;
            }

            return true;
        }

        async Awaitable StartVoicesAsync(CancellationToken cancellationToken)
        {
            // All three on one DSP instant. Three Play() calls would start them
            // up to a frame apart, and a frame of skew between microphone tracks
            // of the same room is audible on every overlap.
            var startDsp = AudioSettings.dspTime + ScheduleLeadSeconds;
            foreach (var seat in Seats)
                seat.Voice.PlayScheduled(startDsp);

            while (AudioSettings.dspTime < startDsp)
                await Awaitable.NextFrameAsync(cancellationToken);

            _startTime = Time.time;
            _started = true;
            HasFinished = false;
        }

        /// <summary>
        /// Play the window again. The clips already hold exactly the window, so a
        /// loop is a rewind and a reschedule — nothing is re-read from disk.
        /// </summary>
        async Awaitable LoopAsync(CancellationToken cancellationToken)
        {
            if (_starting)
                return;

            _starting = true;
            try
            {
                _started = false;
                foreach (var seat in Seats)
                    seat.Voice.Stop();

                await StartVoicesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // play mode ended between two windows
            }
            finally
            {
                _starting = false;
            }
        }

        void Finish()
        {
            foreach (var seat in Seats)
                seat.Voice.Stop();

            HasFinished = true;
            Debug.Log($"{name}: window finished at {Elapsed:F2} s.", this);
        }
    }
}
