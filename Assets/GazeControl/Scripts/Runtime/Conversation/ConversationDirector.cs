using GazeControl.Gaze;
using GazeControl.Gaze.Policy;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Publishes the scripted turn schedule — who holds the floor, who they are
    /// addressing, and when the turn will end — so a gaze policy can act *before*
    /// a turn boundary instead of reacting after it (§5.4).
    ///
    /// Only the scripted conversation can supply this: turn end is known in
    /// advance because the utterance is synthesised up front, and the end is
    /// taken at the last non-silent sample rather than at the clip's end, since
    /// TTS clips carry trailing silence.
    ///
    /// Roles persist through the gap between turns. That matches the corpus this
    /// project's models are fitted on, where a turn event labels every frame up
    /// to the next event, and it keeps the role coding defined during silences.
    /// </summary>
    public class ConversationDirector : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Triad members, used to resolve a speaking agent's gaze controller to a participant")]
        public GazeParticipant[] Participants { get; set; }

        [field: SerializeField]
        [field: Tooltip("Half-width of the turn-boundary neighbourhood, seconds; the corpus fits its 'changing' cells on ±1 s")]
        public float TurnWindowSeconds { get; set; } = 1f;

        /// <summary>Holder of the floor, kept through the gap after the turn ends. Null before the first turn.</summary>
        public GazeParticipant Speaker { get; private set; }

        /// <summary>Who the speaker addresses, from the dialogue script. Null before the first turn.</summary>
        public GazeParticipant Addressee { get; private set; }

        /// <summary>True once a turn has been announced, i.e. once the schedule says anything at all.</summary>
        public bool HasTurn { get; private set; }

        /// <summary>True while the announced turn is still being spoken.</summary>
        public bool IsTurnActive => HasTurn && Time.time < _turnEndTime;

        /// <summary>Seconds until the current turn's last non-silent sample; negative when no turn is running.</summary>
        public float PredictedTimeToTurnEnd => IsTurnActive ? _turnEndTime - Time.time : -1f;

        /// <summary>
        /// Seconds since the floor last changed hands — the current turn's start,
        /// or its end once it has passed. Infinite before the first turn.
        /// </summary>
        public float TimeSinceTurnInstant =>
            HasTurn ? Time.time - (Time.time >= _turnEndTime ? _turnEndTime : _turnStartTime) : float.PositiveInfinity;

        public ParticipantId SpeakerId => Speaker != null ? Speaker.ParticipantId : ParticipantId.None;

        public ParticipantId AddresseeId => Addressee != null ? Addressee.ParticipantId : ParticipantId.None;

        float _turnStartTime;
        float _turnEndTime;

        /// <summary>
        /// Announce that <paramref name="speaker"/> has just started an utterance
        /// whose speech ends <paramref name="speechSeconds"/> from now.
        /// </summary>
        public void BeginTurn(GazeController speaker, float speechSeconds)
        {
            var participant = Resolve(speaker);
            if (participant == null)
            {
                Debug.LogError($"{name}: a speaking agent is not in Participants, so the turn schedule cannot be published.", this);
                return;
            }

            Speaker = participant;
            Addressee = participant.DefaultAddressee;
            HasTurn = true;
            _turnStartTime = Time.time;
            _turnEndTime = Time.time + speechSeconds;
        }

        /// <summary>
        /// Cut the current turn short. Not needed for a turn that runs to its
        /// announced length — the schedule ends it on its own.
        /// </summary>
        public void EndTurn()
        {
            if (IsTurnActive)
                _turnEndTime = Time.time;
        }

        /// <summary>Where <paramref name="participant"/> stands inside its own turn.</summary>
        public TurnPhase PhaseOf(GazeParticipant participant)
        {
            if (!IsTurnActive || participant == null || participant != Speaker)
                return TurnPhase.NotSpeaking;

            if (Time.time - _turnStartTime <= TurnWindowSeconds)
                return TurnPhase.TurnStart;

            return _turnEndTime - Time.time <= TurnWindowSeconds ? TurnPhase.TurnEndApproaching : TurnPhase.TurnMiddle;
        }

        GazeParticipant Resolve(GazeController gaze)
        {
            if (Participants == null || gaze == null)
                return null;

            foreach (var participant in Participants)
            {
                if (participant != null && participant.Gaze == gaze)
                    return participant;
            }

            return null;
        }
    }
}
