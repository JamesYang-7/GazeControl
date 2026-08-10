using System;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Baseline B (§3) — speaker-following, driven purely by voice activity:
    /// look at the addressee while speaking, at whoever else holds the floor
    /// otherwise, and hold the last target through silences.
    ///
    /// Deliberately simple, but it is what most deployed multi-agent systems do,
    /// and because both agents run identical logic they align on the speaker for
    /// free — that joint attention is the baseline's strength, not an accident.
    ///
    /// This policy never averts: §3 says to add aversion only if the proposed
    /// method's aversion is added here too, and otherwise to report that the
    /// condition has no aversion behaviour.
    ///
    /// It also ignores the scripted turn fields of <see cref="ConversationState"/>
    /// on purpose — reacting to real voice activity, with no privileged knowledge
    /// of when a turn will end, is what makes it a baseline.
    /// </summary>
    public sealed class SpeakerFollowingGazePolicy : IGazePolicy
    {
        readonly SpeakerFollowingSettings _settings;
        readonly VoiceActivityTracker _voiceActivity;
        readonly ParticipantId _fallbackAddressee;

        ParticipantId _target;
        float _timeSinceSwitch;

        /// <param name="settings">Hysteresis thresholds.</param>
        /// <param name="voiceActivity">Shared tracker; ticked by the caller once per decision tick, not by this policy.</param>
        /// <param name="fallbackAddressee">Looked at when the dialogue manager names no addressee — the human user (§3).</param>
        public SpeakerFollowingGazePolicy(
            SpeakerFollowingSettings settings,
            VoiceActivityTracker voiceActivity,
            ParticipantId fallbackAddressee)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _voiceActivity = voiceActivity ?? throw new ArgumentNullException(nameof(voiceActivity));

            if (!fallbackAddressee.IsValid)
                throw new ArgumentException("Fallback addressee must be a real participant.", nameof(fallbackAddressee));

            _fallbackAddressee = fallbackAddressee;
            Reset(0);
        }

        /// <summary>Who the agent is currently looking at.</summary>
        public ParticipantId Target => _target;

        /// <inheritdoc/>
        public void Reset(int seed)
        {
            // The seed is unused: this policy is deterministic. It stays in the
            // signature so every condition is seeded and logged the same way.
            _target = _fallbackAddressee;

            // Start with the dwell already satisfied, so the first real speaker is
            // not ignored for 700 ms at the top of the conversation.
            _timeSinceSwitch = _settings.MinimumDwellSeconds;
        }

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            _timeSinceSwitch += deltaTime;

            // Minimum dwell — hold regardless of voice activity.
            if (_timeSinceSwitch < _settings.MinimumDwellSeconds)
                return GazeTarget.AtPerson(_target);

            // Self speaking -> look at the addressee. The backchannel threshold
            // applies here as much as it does to a partner: §3 suppresses gaze
            // switches driven by utterances under 600 ms, and with real
            // conversation audio most of an agent's own short utterances are
            // backchannels thrown in while the *other* agent holds the floor.
            // Without this, every "yeah" re-aims the agent.
            if (IsEstablishedSpeaker(state.SelfId))
                return SwitchTo(AddresseeOf(in state));

            // Overlap — while the current target still holds the floor, stay with
            // them until they drop out past the offset threshold.
            if (_target != state.SelfId && _voiceActivity.IsVoiced(_target))
                return GazeTarget.AtPerson(_target);

            // Someone else speaking -> the most established utterance.
            var candidate = LongestEstablishedSpeaker(state);
            if (candidate.IsValid)
                return SwitchTo(candidate);

            // Silence or gap -> hold the last target.
            return GazeTarget.AtPerson(_target);
        }

        /// <summary>
        /// Who this agent looks at while speaking.
        ///
        /// Normally the schedule's addressee, but that field belongs to whoever
        /// holds the floor, so when voice activity and the schedule disagree it
        /// names this agent itself — which happens constantly, because talking
        /// while someone else holds the floor is what a backchannel is. An agent
        /// is not its own addressee; the person it is addressing is then the
        /// floor-holder it is responding to. Only with neither available does
        /// §3's human fallback apply.
        ///
        /// Sending it to the human instead was tried and is visibly wrong: in a
        /// segment where one agent backchannels through the other's long turn,
        /// it left that agent staring at the user for 66% of the take rather
        /// than at the speaker it was agreeing with.
        /// </summary>
        ParticipantId AddresseeOf(in ConversationState state)
        {
            if (state.CurrentAddressee.IsValid && state.CurrentAddressee != state.SelfId)
                return state.CurrentAddressee;

            if (state.CurrentSpeaker.IsValid && state.CurrentSpeaker != state.SelfId)
                return state.CurrentSpeaker;

            return _fallbackAddressee;
        }

        /// <summary>Voiced, and voiced for longer than a backchannel.</summary>
        bool IsEstablishedSpeaker(ParticipantId id) =>
            _voiceActivity.IsVoiced(id) && _voiceActivity.UtteranceSeconds(id) >= _settings.BackchannelSeconds;

        /// <summary>
        /// The partner whose utterance has outlasted the backchannel threshold and
        /// has run longest. Applying the threshold as "wait before switching to
        /// them" is what makes backchannel suppression implementable without
        /// lookahead: we cannot know an utterance was short until it has ended.
        /// </summary>
        ParticipantId LongestEstablishedSpeaker(in ConversationState state)
        {
            var best = ParticipantId.None;
            var bestSeconds = 0f;

            Consider(state.FirstPartner, ref best, ref bestSeconds);
            Consider(state.SecondPartner, ref best, ref bestSeconds);

            return best;
        }

        void Consider(ParticipantId candidate, ref ParticipantId best, ref float bestSeconds)
        {
            if (!IsEstablishedSpeaker(candidate))
                return;

            var seconds = _voiceActivity.UtteranceSeconds(candidate);
            if (seconds <= bestSeconds)
                return;

            best = candidate;
            bestSeconds = seconds;
        }

        GazeTarget SwitchTo(ParticipantId id)
        {
            if (id != _target)
            {
                _target = id;
                _timeSinceSwitch = 0f;
            }

            return GazeTarget.AtPerson(_target);
        }
    }
}
