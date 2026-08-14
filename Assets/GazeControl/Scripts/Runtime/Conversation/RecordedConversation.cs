using System;
using System.Collections.Generic;
using GazeControl.Gaze.Policy;
using GazeControl.Motion;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Plays a recorded TalkingWithHands segment on the two agents: each speaks
    /// the corpus's own audio and moves on its own mocap, and the corpus's
    /// end-of-turn annotation becomes the turn schedule the gaze policies read.
    ///
    /// Nothing here is synthesised. The turn boundaries are the ones a human
    /// annotator's pipeline found in the real conversation, which is the whole
    /// point: a pre-turn gaze pattern is only meaningful if the turn it precedes
    /// is real. It also means the demo shows overlaps, backchannels and
    /// interruptions that a scripted two-utterance exchange cannot.
    ///
    /// One clock drives everything. Audio for both agents is scheduled on the
    /// same DSP instant, and motion and the turn schedule start when that instant
    /// arrives, so a gaze pattern lands on the word it was measured against.
    /// </summary>
    public class RecordedConversation : MonoBehaviour
    {
        /// <summary>One agent's share of the segment: which corpus speaker it plays, and what plays it.</summary>
        [Serializable]
        public sealed class Speaker
        {
            [field: SerializeField]
            [field: Tooltip("Corpus speaker code: 1 is main-agent, 2 is interloctr")]
            public int SpeakerCode { get; set; }

            [field: SerializeField]
            [field: Tooltip("The triad member playing this speaker; its AudioSource carries the voice")]
            public GazeParticipant Participant { get; set; }

            [field: SerializeField]
            [field: Tooltip("Motion player for this agent; its clip and frame range come from the segment")]
            public SmplxMotionPlayer Motion { get; set; }

            [field: SerializeField]
            [field: Tooltip("Trimmed segment audio. Left empty in the editor, it is loaded from the path in the segment file.")]
            public AudioClip Clip { get; set; }
        }

        [field: SerializeField]
        [field: Tooltip("Segment written by Tools/find_demo_segments.py, relative to the project root")]
        public string SegmentPath { get; set; } = "Assets/DemoSegments/case1_seg01/segment.json";

        [field: SerializeField]
        [field: Tooltip("The two agents, one per corpus speaker")]
        public Speaker[] Speakers { get; set; }

        [field: SerializeField]
        [field: Tooltip("The human participant; turns addressed to speaker code 0 (the user) resolve to them")]
        public GazeParticipant User { get; set; }

        [field: SerializeField]
        [field: Tooltip("Publishes the segment's turn schedule to the gaze policies")]
        public ConversationDirector Director { get; set; }

        [field: SerializeField]
        [field: Range(0.05f, 1f)]
        [field: Tooltip("Lead given to the audio engine before playback starts, seconds")]
        public float ScheduleLeadSeconds { get; set; } = 0.2f;

        /// <summary>True once the segment has played out and the motion players are frozen.</summary>
        public bool HasFinished { get; private set; }

        /// <summary>
        /// How long the take should hold after the voices stop; 0 when the
        /// segment does not say. Exposed here so the recorder never reads the
        /// segment's schema itself.
        /// </summary>
        public float HoldSeconds => Segment != null ? Segment.tailSeconds : 0f;

        /// <summary>The segment being played; null until it has loaded.</summary>
        public DemoSegment Segment { get; private set; }

        /// <summary>
        /// Seconds into the segment; negative before playback starts. One clock
        /// for motion, the turn schedule and every gaze policy, so a prototype
        /// lands on the word it was measured against.
        ///
        /// It is the frame clock, not the audio clock, because the frame clock is
        /// what a recorded take is measured in: Unity Recorder writes one output
        /// frame per rendered frame, so the video's timeline *is* the frame count,
        /// and it captures the agents' audio frame by frame along with it. An
        /// audio-clock version of this property was tried and produced a take
        /// 0.7 s shorter than the segment, with the tail of the last utterance
        /// cut off.
        ///
        /// The cost is that a stalled player loop — an editor hitch, the editor
        /// losing focus — slides this clock behind the voices you hear live.
        /// <c>Application.runInBackground</c> is set for that reason, and a take
        /// should be checked for length against the segment.
        /// </summary>
        public float Elapsed => _started ? Time.time - _startTime : -1f;

        float _startTime;
        bool _started;

        // Loaded in Awake, not Start: the condition runner writes the segment's
        // boundaries into its log metadata during its own Start, and Unity gives
        // no ordering guarantee between two components' Start methods.
        void Awake()
        {
            if (!TryLoadSegment())
                enabled = false;
        }

        async Awaitable Start()
        {
            try
            {
                if (Segment == null)
                    return;

                await PlayAsync(destroyCancellationToken);
            }
            catch (OperationCanceledException)
            {
                // play mode ended / object destroyed mid-segment; nothing to clean up
            }
        }

        void Update()
        {
            if (!_started || HasFinished)
                return;

            var elapsed = Elapsed;
            if (elapsed >= Segment.durationSeconds)
            {
                Finish();
                return;
            }

            foreach (var speaker in Speakers)
                speaker.Motion.Seek(elapsed);
        }

        bool TryLoadSegment()
        {
            if (Speakers == null || Speakers.Length == 0)
            {
                Debug.LogError($"{name}: no speakers assigned; the segment has nobody to play it.", this);
                return false;
            }

            try
            {
                Segment = DemoSegment.Load(SegmentPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"{name}: could not load '{SegmentPath}': {e.Message}", this);
                return false;
            }

            // Refused rather than degraded: with no User wired, the yield turn's
            // addressee would silently resolve to nobody and every policy would
            // play a plausible-looking take with no yield in it.
            if (Segment.yieldsToUser && User == null)
            {
                Debug.LogError($"{name}: '{SegmentPath}' yields to the user, but no User participant is assigned.", this);
                return false;
            }

            foreach (var speaker in Speakers)
            {
                if (!TryPrepare(speaker))
                    return false;
            }

            Debug.Log(
                $"{name}: {Segment.name} — {Segment.stem} {Segment.sourceStartSeconds:F2}-{Segment.sourceEndSeconds:F2} s " +
                $"({Segment.durationSeconds:F2} s, {Segment.events.Length} end-of-turn events)", this);

            return true;
        }

        bool TryPrepare(Speaker speaker)
        {
            if (speaker.Participant == null || speaker.Motion == null)
            {
                Debug.LogError($"{name}: speaker {speaker.SpeakerCode} is missing its participant or motion player.", this);
                return false;
            }

            var agent = Segment.AgentOf(speaker.SpeakerCode);
            if (agent == null)
            {
                Debug.LogError($"{name}: the segment has no speaker {speaker.SpeakerCode}.", this);
                return false;
            }

            var voice = speaker.Participant.Voice;
            if (voice == null)
            {
                Debug.LogError($"{name}: {speaker.Participant.DisplayName} has no AudioSource to speak through.", this);
                return false;
            }

            var clip = speaker.Clip != null ? speaker.Clip : LoadClip(agent.audio);
            if (clip == null)
            {
                Debug.LogError($"{name}: no audio clip for speaker {speaker.SpeakerCode} ('{agent.audio}').", this);
                return false;
            }

            voice.clip = clip;
            voice.loop = false;
            voice.playOnAwake = false;

            speaker.Motion.ClipPath = agent.motion;
            speaker.Motion.StartFrame = Segment.motionStartFrame;
            speaker.Motion.FrameCount = Segment.motionFrameCount;
            speaker.Motion.Loop = false;
            speaker.Motion.Autoplay = false; // this component owns the clock
            speaker.Motion.enabled = true;
            speaker.Motion.Initialize();

            return true;
        }

        /// <summary>
        /// Editor convenience: pull the trimmed wav straight out of the segment
        /// folder, so switching segments is one path field rather than a
        /// re-drag of both clips. A build needs the clips wired in the scene.
        /// </summary>
        static AudioClip LoadClip(string assetPath)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
#else
            return null;
#endif
        }

        /// <summary>
        /// Hold the segment until the demo recorder's encoder is running. The
        /// flag is the recorder's own "a recording was asked for" session key, so
        /// a plain Play Mode session never waits on anything.
        /// </summary>
        async Awaitable WaitForRecorderAsync(System.Threading.CancellationToken cancellationToken)
        {
#if UNITY_EDITOR
            while (UnityEditor.SessionState.GetBool("GazeControl.DemoRecorder.Pending", false))
                await Awaitable.NextFrameAsync(cancellationToken);
#else
            await Awaitable.NextFrameAsync(cancellationToken);
#endif
        }

        async Awaitable PlayAsync(System.Threading.CancellationToken cancellationToken)
        {
            await WaitForRecorderAsync(cancellationToken);

            // Both voices start on one DSP instant. Calling Play() twice would
            // start them a frame apart, and the two sides of a conversation
            // drifting apart by a frame is audible on the overlaps.
            var startDsp = AudioSettings.dspTime + ScheduleLeadSeconds;
            foreach (var speaker in Speakers)
                speaker.Participant.Voice.PlayScheduled(startDsp);

            while (AudioSettings.dspTime < startDsp)
                await Awaitable.NextFrameAsync(cancellationToken);

            _startTime = Time.time;
            _started = true;

            if (Director != null)
                Director.SetSchedule(BuildSchedule(), () => Elapsed);
            else
                Debug.LogWarning($"{name}: no director wired, so no policy can see a turn boundary coming.", this);
        }

        /// <summary>
        /// The corpus turn list, resolved to participants. Speaker codes are the
        /// corpus's; participant ids are the scene's, and only this component
        /// knows both.
        /// </summary>
        List<ScheduledTurn> BuildSchedule()
        {
            var schedule = new List<ScheduledTurn>(Segment.turns.Length);

            foreach (var turn in Segment.turns)
            {
                var eventType = turn.eventIndex >= 0 && turn.eventIndex < Segment.events.Length
                    ? (EotType)Segment.events[turn.eventIndex].eotType
                    : EotType.TurnTaking;

                schedule.Add(new ScheduledTurn(
                    IdOf(turn.speaker), IdOf(turn.addressee),
                    turn.startTime, turn.endTime,
                    turn.eventIndex, eventType));
            }

            return schedule;
        }

        ParticipantId IdOf(int speakerCode)
        {
            if (speakerCode == DemoSegment.UserSpeakerCode)
                return User != null ? User.ParticipantId : ParticipantId.None;

            foreach (var speaker in Speakers)
            {
                if (speaker.SpeakerCode == speakerCode)
                    return speaker.Participant.ParticipantId;
            }

            return ParticipantId.None;
        }

        void Finish()
        {
            foreach (var speaker in Speakers)
            {
                speaker.Participant.Voice.Stop();
                speaker.Motion.enabled = false; // freezes the skeleton at the last pose
            }

            HasFinished = true;
            Debug.Log($"{name}: segment finished at {Elapsed:F2} s.", this);
        }
    }
}
