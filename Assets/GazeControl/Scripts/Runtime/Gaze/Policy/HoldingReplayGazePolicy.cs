using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Non-parametric replay of measured holding-period gaze: plays fixation
    /// stretches from a <see cref="HoldingSequenceBank"/>, conditioned on the
    /// agent's current conversational role. The proposed condition's substrate.
    ///
    /// Replay is deliberately verbatim — no duration law, no transition model.
    /// A stretch's fixations play in recorded order for their recorded
    /// durations; when it runs out (or the agent's role changes) the next
    /// stretch is drawn uniformly from the pool for the agent's current role.
    /// The draw stream is per-agent, unlike the prototype draw that both agents
    /// share: holding gaze is uncoordinated between participants in the corpus,
    /// so two agents replaying in lock-step would be an artifact.
    ///
    /// A fixation names a role, not a person, and is resolved to a person every
    /// tick, so a turn hand-over retargets it mid-fixation. On the agent's own
    /// role change the current fixation still finishes before a stretch from
    /// the new role's pool starts — with one exception: if the change moved the
    /// agent itself into the fixation's target role, finishing would mean
    /// gazing at itself, so the fixation ends on that tick instead.
    ///
    /// Aversion renders as a zero offset — eyes recentred in the head — matching
    /// how the proposed condition renders the prototypes' "None", so the whole
    /// condition expresses "looking away" one way.
    /// </summary>
    public sealed class HoldingReplayGazePolicy : IGazePolicy
    {
        readonly HoldingSequenceBank _bank;
        readonly DeterministicRandom _random = new(0);
        readonly ParticipantId _fallbackTarget;

        ParticipantId _speaker;
        ParticipantId _addressee;
        ParticipantId _sideParticipant;
        bool _hasRoleAssignment;

        ParticipantRole _stretchRole;
        int _stretchIndex;
        int _fixationIndex;
        float _remaining;
        bool _playing;

        ParticipantId _target;
        bool _averting;

        /// <param name="bank">Replay material, normally <see cref="HoldingSequenceBank.LoadDefault"/>.</param>
        /// <param name="fallbackTarget">
        /// Gazed at before the first valid role assignment — the human user,
        /// matching baselines A and B, so the conditions look alike before the
        /// conversation itself starts.
        /// </param>
        public HoldingReplayGazePolicy(HoldingSequenceBank bank, ParticipantId fallbackTarget)
        {
            _bank = bank ?? throw new ArgumentNullException(nameof(bank));

            if (!fallbackTarget.IsValid)
                throw new ArgumentException("Fallback target must be a real participant.", nameof(fallbackTarget));

            _fallbackTarget = fallbackTarget;
            Reset(0);
        }

        /// <summary>What the agent is looking at now, in the corpus's role coding.</summary>
        public GazeTargetRole State => _averting ? GazeTargetRole.Aversion : RoleOfTarget();

        /// <inheritdoc/>
        public void Reset(int seed)
        {
            _random.Reset(seed);

            _speaker = ParticipantId.None;
            _addressee = ParticipantId.None;
            _sideParticipant = ParticipantId.None;
            _hasRoleAssignment = false;

            _playing = false;
            _target = _fallbackTarget;
            _averting = false;
        }

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            UpdateRoleAssignment(in state);

            if (!_hasRoleAssignment)
                return GazeTarget.AtPerson(_target);

            var selfRole = RoleOfSelf(state.SelfId);

            if (!_playing)
            {
                BeginStretch(selfRole);
                _playing = true;
            }
            else
            {
                // Advance when less than half a tick remains, not at zero: the
                // recorded durations are exact (0.5 s is 15 ticks at 30 Hz), and
                // accumulated float error in the subtraction leaves a remainder
                // of ~1e-7 either side of zero at the boundary — a <= 0 test
                // (baseline A's, fine for *sampled* dwells) turns that into a
                // 16th tick. This rounds each fixation to its nearest tick count.
                _remaining -= deltaTime;
                if (_remaining < deltaTime * 0.5f)
                    Advance(selfRole);
            }

            return Emit(selfRole, state.SelfId);
        }

        /// <summary>
        /// Who currently plays each corpus role. The assignment is only replaced
        /// when it is a complete permutation of the triad, so it survives the
        /// silent gaps between turns — same rule as baseline A.
        /// </summary>
        void UpdateRoleAssignment(in ConversationState state)
        {
            var speaker = state.CurrentSpeaker;
            var addressee = state.CurrentAddressee;
            if (!speaker.IsValid || !addressee.IsValid || speaker == addressee)
                return;

            var side = Remaining(in state, speaker, addressee);
            if (!side.IsValid)
                return;

            _speaker = speaker;
            _addressee = addressee;
            _sideParticipant = side;
            _hasRoleAssignment = true;
        }

        static ParticipantId Remaining(in ConversationState state, ParticipantId speaker, ParticipantId addressee)
        {
            if (state.SelfId != speaker && state.SelfId != addressee)
                return state.SelfId;

            if (state.FirstPartner.IsValid && state.FirstPartner != speaker && state.FirstPartner != addressee)
                return state.FirstPartner;

            if (state.SecondPartner.IsValid && state.SecondPartner != speaker && state.SecondPartner != addressee)
                return state.SecondPartner;

            return ParticipantId.None;
        }

        /// <summary>
        /// The agent's role, read from the persistent assignment rather than
        /// from <c>state.SelfRole</c> — the assignment is what keeps replaying
        /// coherent through the gaps where the state carries no roles.
        /// </summary>
        ParticipantRole RoleOfSelf(ParticipantId self)
        {
            if (self == _speaker)
                return ParticipantRole.Speaker;

            return self == _addressee ? ParticipantRole.Addressee : ParticipantRole.SideParticipant;
        }

        void BeginStretch(ParticipantRole role)
        {
            _stretchRole = role;
            _stretchIndex = (int)(_random.NextFloat() * _bank.StretchCount(role));
            _fixationIndex = 0;
            _remaining = _bank.Fixations(role, _stretchIndex)[0].DurationSeconds;
        }

        /// <summary>
        /// The current fixation's recorded duration has elapsed. A role change
        /// that happened while it played takes effect now: the rest of the
        /// stretch is abandoned for a draw from the new role's pool.
        /// </summary>
        void Advance(ParticipantRole selfRole)
        {
            var track = _bank.Fixations(_stretchRole, _stretchIndex);
            if (selfRole != _stretchRole || _fixationIndex + 1 >= track.Length)
            {
                BeginStretch(selfRole);
                return;
            }

            _fixationIndex++;
            _remaining = track[_fixationIndex].DurationSeconds;
        }

        GazeTarget Emit(ParticipantRole selfRole, ParticipantId self)
        {
            var fixation = _bank.Fixations(_stretchRole, _stretchIndex)[_fixationIndex];
            if (fixation.Target == GazeTargetRole.Aversion)
            {
                _averting = true;
                return GazeTarget.Away(Vector2.zero);
            }

            var person = PersonInRole(fixation.Target);
            if (person == self)
            {
                // A role change moved the agent into the fixation's target role;
                // finishing it would mean gazing at itself, so it ends here. The
                // fresh stretch is drawn for the agent's own role and the bank
                // rejects own-role targets, so this recurses at most once.
                BeginStretch(selfRole);
                return Emit(selfRole, self);
            }

            _averting = false;
            _target = person;
            return GazeTarget.AtPerson(person);
        }

        /// <summary>
        /// Which corpus role the person being gazed at plays *now* — re-derived
        /// per query rather than stored, so a fixation that outlives a hand-over
        /// keeps its person and changes its label, matching the corpus coding.
        /// </summary>
        GazeTargetRole RoleOfTarget()
        {
            if (_target == _speaker)
                return GazeTargetRole.Speaker;

            return _target == _addressee ? GazeTargetRole.Addressee : GazeTargetRole.SideParticipant;
        }

        ParticipantId PersonInRole(GazeTargetRole role) => role switch
        {
            GazeTargetRole.Speaker => _speaker,
            GazeTargetRole.Addressee => _addressee,
            GazeTargetRole.SideParticipant => _sideParticipant,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Aversion is not a person."),
        };
    }
}
