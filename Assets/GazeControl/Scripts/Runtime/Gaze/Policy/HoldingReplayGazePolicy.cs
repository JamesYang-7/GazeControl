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
    ///
    /// <para><b>Do not re-do this.</b> On 2026-08-25 aversion here was changed to
    /// sample a direction from baseline A's fitted table, because live previews
    /// looked like "the eyes are just centered". That observation was confounded:
    /// <c>GazeController</c> was frame-rate dependent — the mocap re-poses the eye
    /// bones every frame and the corpus carries no eye animation, so the rendered
    /// angle was capped at <c>EyeDegreesPerSecond / fps</c> and *every* target,
    /// person fixations included, rendered near centre in a live editor preview.
    /// Recorded takes were locked to 30 fps by the recorder and looked right,
    /// which is why the two disagreed. With that fixed, recentred aversion reads
    /// as intended, and the condition is back to what the signed-off 2026-08-14
    /// take played. Reverted the same day at the user's call.</para>
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

        /// <summary>
        /// What the agent is looking at now, in the corpus's role coding. The
        /// current fixation stores its target as a role and is resolved to a
        /// person from the same assignment every tick, so the fixation's own
        /// target *is* the gazed-at person's current role — nothing to re-derive.
        /// Before the first stretch the agent holds the fallback human, who by
        /// definition is in neither known role.
        /// </summary>
        public GazeTargetRole State => _playing ? Current().Target : GazeTargetRole.SideParticipant;

        /// <inheritdoc/>
        public void Reset(int seed)
        {
            _random.Reset(seed);

            _speaker = ParticipantId.None;
            _addressee = ParticipantId.None;
            _sideParticipant = ParticipantId.None;
            _hasRoleAssignment = false;

            _playing = false;
        }

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            UpdateRoleAssignment(in state);

            if (!_hasRoleAssignment)
                return GazeTarget.AtPerson(_fallbackTarget);

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

            // A role change can move the agent itself into the current fixation's
            // target role; finishing it would mean gazing at itself, so it ends
            // now. Once is enough: the fresh stretch is drawn for the agent's own
            // role and the bank rejects own-role targets.
            if (WouldGazeAtSelf(state.SelfId))
                BeginStretch(selfRole);

            return Emit();
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
            _remaining = Current().DurationSeconds;
        }

        /// <summary>
        /// The current fixation's recorded duration has elapsed. A role change
        /// that happened while it played takes effect now: the rest of the
        /// stretch is abandoned for a draw from the new role's pool.
        /// </summary>
        void Advance(ParticipantRole selfRole)
        {
            if (selfRole != _stretchRole || _fixationIndex + 1 >= _bank.Fixations(_stretchRole, _stretchIndex).Length)
            {
                BeginStretch(selfRole);
                return;
            }

            _fixationIndex++;
            _remaining = Current().DurationSeconds;
        }

        HoldingFixation Current() => _bank.Fixations(_stretchRole, _stretchIndex)[_fixationIndex];

        bool WouldGazeAtSelf(ParticipantId self)
        {
            var fixation = Current();
            return fixation.Target != GazeTargetRole.Aversion && PersonInRole(fixation.Target) == self;
        }

        GazeTarget Emit()
        {
            var fixation = Current();
            return fixation.Target == GazeTargetRole.Aversion
                ? GazeTarget.Away(Vector2.zero)
                : GazeTarget.AtPerson(PersonInRole(fixation.Target));
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
