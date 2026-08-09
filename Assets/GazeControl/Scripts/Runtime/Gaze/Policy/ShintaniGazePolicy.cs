using System;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Baseline A — the multi-party gaze model of Shintani et al. (2024),
    /// re-estimated on our own three-party end-of-turn corpus.
    ///
    /// A semi-Markov dwell-and-shift sampler: while a dwell runs the gaze holds,
    /// and when it expires the next target is drawn from a jump chain conditioned
    /// on the agent's conversational role, then given a fresh dwell drawn from
    /// that (role × turn state × target) cell. Aversion is a target like any
    /// other, with its own eyeball direction re-targeting inside it.
    ///
    /// Three deliberate departures from the published model, all forced by the
    /// corpus (the reasoning is in <c>Tools/build_baseline_a_params.py</c>):
    /// lognormal dwells instead of chi-square, a jump chain instead of i.i.d.
    /// draws from the target ratios, and an empirical re-target interval instead
    /// of their 0.7 s constant. Their extraversion axis is dropped — we have no
    /// personality ratings to interpolate between, so there is one parameter set.
    ///
    /// There are no hand-written turn-boundary overrides. The turn-onset and
    /// turn-yielding gaze that the literature describes is already in the
    /// <see cref="TurnState.Changing"/> half of the fitted parameters, and adding
    /// a scripted yielding gaze here would pre-empt exactly what the proposed
    /// method is being compared for.
    ///
    /// Two agents running this policy share nothing — no RNG, no view of each
    /// other's target (§5.1). Uncoordinated gaze is the point of the baseline.
    /// </summary>
    public sealed class ShintaniGazePolicy : IGazePolicy
    {
        // A truncated draw is a rejection loop; bail out to a clamp rather than
        // spin if a cell is ever parametrised so extremely that it cannot land in
        // range. With the fitted parameters roughly 6% of draws are rejected.
        const int MaxDwellDraws = 32;

        readonly ShintaniGazeParameters _parameters;
        readonly DeterministicRandom _random = new(0);
        readonly AversionSampler _aversion;
        readonly ParticipantId _fallbackTarget;

        ParticipantId _speaker;
        ParticipantId _addressee;
        ParticipantId _sideParticipant;
        bool _hasRoleAssignment;

        ParticipantId _target;
        bool _averting;
        float _dwellRemaining;
        bool _sampling;

        /// <param name="parameters">Fitted model, normally <see cref="ShintaniGazeParameters.LoadDefault"/>.</param>
        /// <param name="fallbackTarget">
        /// Held before the first turn, when no one has spoken yet and the corpus's
        /// role coding is undefined. The human user, matching baseline B, so the
        /// conditions look alike outside the conversation itself.
        /// </param>
        public ShintaniGazePolicy(ShintaniGazeParameters parameters, ParticipantId fallbackTarget)
        {
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));

            if (!fallbackTarget.IsValid)
                throw new ArgumentException("Fallback target must be a real participant.", nameof(fallbackTarget));

            _aversion = new AversionSampler(parameters, _random);
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

            _target = _fallbackTarget;
            _averting = false;
            _dwellRemaining = 0f;
            _sampling = false;
            _aversion.Reset();
        }

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            UpdateRoleAssignment(in state);

            if (!_hasRoleAssignment)
                return GazeTarget.AtPerson(_target);

            var role = state.SelfRole;
            var turnState = TurnStateOf(in state);

            if (!_sampling)
            {
                Start(role, turnState);
                _sampling = true;
                return CurrentTarget();
            }

            _dwellRemaining -= deltaTime;
            if (_dwellRemaining <= 0f)
            {
                Shift(role, turnState);
                return CurrentTarget();
            }

            if (_averting)
                _aversion.Tick(deltaTime);

            return CurrentTarget();
        }

        /// <summary>
        /// Who currently plays each corpus role. The assignment is only replaced
        /// when it is a complete permutation of the triad, so it survives the
        /// silent gaps between turns — matching the corpus, where the governing
        /// turn event labels every frame up to the next event, gaps included.
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
        /// The corpus calls a gaze "changing" when it starts within ±1 s of the
        /// nearest turn instant, so both the time since the last one and the time
        /// to the next one count. The time to the next one is known in advance for
        /// a scripted agent, which is what makes the pre-turn half observable at
        /// all (§5.4).
        /// </summary>
        TurnState TurnStateOf(in ConversationState state)
        {
            var toNearest = state.TimeSinceTurnInstant;
            if (state.PredictedTimeToTurnEnd >= 0f && state.PredictedTimeToTurnEnd < toNearest)
                toNearest = state.PredictedTimeToTurnEnd;

            return toNearest <= _parameters.TurnWindowSeconds ? TurnState.Changing : TurnState.Holding;
        }

        void Start(ParticipantRole role, TurnState turnState)
        {
            // The first state is drawn from the occupancy ratios rather than from
            // the jump chain: there is no previous target to jump away from, and S
            // is exactly the distribution a run would settle into.
            var initial = (GazeTargetRole)_random.NextCategorical(_parameters.TargetRatios(role, turnState));
            EnterState(initial, role, turnState);
        }

        void Shift(ParticipantRole role, TurnState turnState)
        {
            var next = (GazeTargetRole)_random.NextCategorical(_parameters.JumpProbabilities(role, State));
            EnterState(next, role, turnState);
        }

        void EnterState(GazeTargetRole next, ParticipantRole role, TurnState turnState)
        {
            _dwellRemaining = SampleDwell(role, turnState, next);

            if (next == GazeTargetRole.Aversion)
            {
                _averting = true;
                _aversion.Begin(role);
                return;
            }

            _averting = false;
            _target = PersonInRole(next);
        }

        float SampleDwell(ParticipantRole role, TurnState turnState, GazeTargetRole target)
        {
            var (mu, sigma) = _parameters.Dwell(role, turnState, target);

            for (var attempt = 0; attempt < MaxDwellDraws; attempt++)
            {
                var seconds = _random.NextLogNormal(mu, sigma);
                if (seconds >= _parameters.MinDwellSeconds && seconds <= _parameters.MaxDwellSeconds)
                    return seconds;
            }

            return _parameters.MinDwellSeconds;
        }

        /// <summary>
        /// Which corpus role the person being gazed at plays *now*. Re-deriving it
        /// every tick rather than storing the label is what keeps gaze on the same
        /// person across a turn hand-over: the corpus segments fixations on
        /// participant identity and only then role-codes them, so a fixation that
        /// outlives its turn keeps its target and changes its label.
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

        GazeTarget CurrentTarget() => _averting
            ? GazeTarget.Away(_aversion.Offset)
            : GazeTarget.AtPerson(_target);
    }
}
