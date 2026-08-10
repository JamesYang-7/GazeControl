using System.Collections.Generic;
using GazeControl.Gaze.Policy;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>One entry of the turn schedule: who holds the floor until when, and what ends it.</summary>
    public readonly struct ScheduledTurn
    {
        public ScheduledTurn(
            ParticipantId speaker, ParticipantId addressee,
            float startTime, float endTime,
            int eventIndex, EotType eventType)
        {
            Speaker = speaker;
            Addressee = addressee;
            StartTime = startTime;
            EndTime = endTime;
            EventIndex = eventIndex;
            EventType = eventType;
        }

        public ParticipantId Speaker { get; }

        /// <summary>Who takes the floor at this turn's end — the event's second speaker.</summary>
        public ParticipantId Addressee { get; }

        public float StartTime { get; }

        /// <summary>The boundary this turn runs into: the event's transition instant.</summary>
        public float EndTime { get; }

        /// <summary>Index into the segment's event list, or -1 for a turn that no event ends.</summary>
        public int EventIndex { get; }

        public EotType EventType { get; }

        public bool HasEvent => EventIndex >= 0;
    }

    /// <summary>
    /// Publishes the turn schedule — who holds the floor, who takes it next, and
    /// when the boundary arrives — so a gaze policy can act *before* a turn
    /// boundary instead of reacting after it (§5.4).
    ///
    /// The schedule comes from the corpus rather than from a dialogue manager:
    /// the demo replays a recorded conversation whose end-of-turn events were
    /// annotated offline, so every boundary is known in advance and none of it
    /// has to be inferred from the audio. That is what lets a pre-turn pattern
    /// start a second before the turn actually ends.
    ///
    /// Roles persist through the gaps between turns. That matches the corpus the
    /// gaze models are fitted on, where a turn event labels every frame up to the
    /// next event, and it keeps the role coding defined during silences.
    /// </summary>
    public class ConversationDirector : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Triad members, used to resolve a participant id to a scene object")]
        public GazeParticipant[] Participants { get; set; }

        [field: SerializeField]
        [field: Tooltip("Half-width of the turn-boundary neighbourhood, seconds; the corpus fits its 'changing' cells on ±1 s")]
        public float TurnWindowSeconds { get; set; } = 1f;

        /// <summary>Holder of the floor, kept through the gap after the turn ends. Null before the schedule starts.</summary>
        public GazeParticipant Speaker { get; private set; }

        /// <summary>Who takes the floor at the current turn's end. Null before the schedule starts.</summary>
        public GazeParticipant Addressee { get; private set; }

        /// <summary>True once the schedule is running, i.e. once it says anything at all.</summary>
        public bool HasTurn { get; private set; }

        /// <summary>True while the current turn has not yet reached its boundary.</summary>
        public bool IsTurnActive => HasTurn && Elapsed < _current.EndTime;

        /// <summary>Seconds until the current turn's boundary; negative when no turn is running.</summary>
        public float PredictedTimeToTurnEnd => IsTurnActive ? _current.EndTime - Elapsed : -1f;

        /// <summary>
        /// Seconds since the floor last changed hands — the current turn's start,
        /// or the schedule's end once it has passed. Infinite before it starts.
        /// </summary>
        public float TimeSinceTurnInstant
        {
            get
            {
                if (!HasTurn)
                    return float.PositiveInfinity;

                var last = _turns[^1].EndTime;
                return Elapsed >= last ? Elapsed - last : Elapsed - _current.StartTime;
            }
        }

        /// <summary>The event that ends the current turn, or -1 when none does.</summary>
        public int UpcomingEventIndex => IsTurnActive && _current.HasEvent ? _current.EventIndex : -1;

        /// <summary>The class of the event that ends the current turn.</summary>
        public EotType UpcomingEventType => _current.EventType;

        /// <summary>The schedule as published, for the log's metadata sidecar.</summary>
        public IReadOnlyList<ScheduledTurn> Schedule => _turns;

        public ParticipantId SpeakerId => Speaker != null ? Speaker.ParticipantId : ParticipantId.None;

        public ParticipantId AddresseeId => Addressee != null ? Addressee.ParticipantId : ParticipantId.None;

        /// <summary>
        /// Seconds into the conversation, read from whoever published the
        /// schedule. Pulled on demand rather than cached each frame: the gaze
        /// runner and this component both run in <c>Update</c> and Unity gives no
        /// ordering between them, so a cached value would be a frame stale for
        /// one of them half the time.
        /// </summary>
        public float Elapsed => _clock != null ? _clock() : 0f;

        ScheduledTurn[] _turns = System.Array.Empty<ScheduledTurn>();
        ScheduledTurn _current;
        System.Func<float> _clock;
        int _index;

        void Update()
        {
            if (!HasTurn)
                return;

            // The schedule is monotone, so advancing is a scan rather than a
            // search. The final turn is never advanced past: its roles are what
            // the conversation is left in when the clip ends.
            while (_index < _turns.Length - 1 && Elapsed >= _turns[_index].EndTime)
                Enter(_index + 1);
        }

        /// <summary>
        /// Publish a schedule and the clock its times are measured on. The clock
        /// belongs to whoever plays the conversation — for a recorded segment it
        /// is the audio clock, so the turn boundaries stay on the words even if
        /// the player loop stalls.
        /// </summary>
        public void SetSchedule(IReadOnlyList<ScheduledTurn> turns, System.Func<float> clock)
        {
            if (turns == null || turns.Count == 0)
            {
                Debug.LogError($"{name}: an empty turn schedule was published; no policy can read a boundary from it.", this);
                return;
            }

            _turns = new ScheduledTurn[turns.Count];
            for (var i = 0; i < turns.Count; i++)
                _turns[i] = turns[i];

            _clock = clock ?? throw new System.ArgumentNullException(nameof(clock));
            HasTurn = true;
            Enter(0);
        }

        /// <summary>Where <paramref name="participant"/> stands inside its own turn.</summary>
        public TurnPhase PhaseOf(GazeParticipant participant)
        {
            if (!IsTurnActive || participant == null || participant != Speaker)
                return TurnPhase.NotSpeaking;

            if (Elapsed - _current.StartTime <= TurnWindowSeconds)
                return TurnPhase.TurnStart;

            return _current.EndTime - Elapsed <= TurnWindowSeconds ? TurnPhase.TurnEndApproaching : TurnPhase.TurnMiddle;
        }

        void Enter(int index)
        {
            _index = index;
            _current = _turns[index];
            Speaker = Find(_current.Speaker);
            Addressee = Find(_current.Addressee);
        }

        GazeParticipant Find(ParticipantId id)
        {
            if (Participants == null)
                return null;

            foreach (var participant in Participants)
            {
                if (participant != null && participant.ParticipantId == id)
                    return participant;
            }

            return null;
        }
    }
}
