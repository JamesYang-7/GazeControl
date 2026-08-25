using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// The proposed condition: a substrate policy that hands control to one of the
    /// paper's measured pre-turn prototypes for the last second of a turn, and
    /// takes it back at the boundary.
    ///
    /// Each boundary gets its own prototype, drawn from the pool the paper
    /// measured on boundaries of that class — an interruption is preceded by an
    /// interruption prototype, never by a turn-taking one. The recorded
    /// conversation supplies both the boundaries and their classes, so this is
    /// the corpus's own annotation deciding which pattern plays.
    ///
    /// The draw is a hash of the run's pattern seed and the event's index, not a
    /// running random stream. Two things follow, and both are needed: the two
    /// agents pick the *same* prototype for a boundary even though they hold
    /// separate policy instances with separate seeds — otherwise one would play
    /// the current-speaker track of one prototype against the next-speaker track
    /// of another — and the choice does not depend on how many ticks have gone
    /// by, so it survives a change of decision rate.
    ///
    /// The substrate is **holding replay** as of 2026-08-14 (user's call): outside
    /// the windows the agent replays measured holding-period fixation stretches
    /// from our own corpus, so the method is one full data-driven model — corpus
    /// sequences between turns, corpus prototypes before them. That supersedes
    /// the 2026-08-09 baseline-B substrate, which survives as the
    /// ProposedSettings.Substrate = SpeakerFollowing ablation: bit-for-bit
    /// baseline B outside the window, isolating the pattern exactly. Report the
    /// pairwise comparisons accordingly.
    ///
    /// The prototypes' "None" averts directionally, through the same
    /// <see cref="AversionSampler"/> baselines A and the holding substrate use
    /// (2026-08-24). It used to recentre the eyes; with the head on pure mocap
    /// that is indistinguishable from an agent with no gaze control, so a
    /// condition rendering "looking away" that way would lose a naturalness
    /// comparison on the rendering rather than on the policy. Every condition
    /// now averts the same way and only the choice of when differs (spec §0.1).
    ///
    /// The substrate is ticked on every call, including while the pattern is
    /// overriding it, so its hysteresis clocks follow the same trajectory whether
    /// or not a turn boundary happens to be near — the window changes what is
    /// rendered, never what the substrate would have done.
    /// </summary>
    public sealed class ProposedGazePolicy : IGazePolicy
    {
        readonly ProposedSettings _settings;
        readonly int _patternSeed;
        readonly IGazePolicy _substrate;
        readonly DeterministicRandom _random = new(0);
        readonly AversionSampler _aversion;

        /// <summary>
        /// Whether the previous tick was already on the prototype's "None".
        /// Edge detection: the sampler needs Begin on the tick the aversion
        /// starts and Tick on every one after.
        /// </summary>
        bool _averting;

        /// <param name="substrate">Drives gaze outside the pre-turn window; baseline B in the study.</param>
        /// <param name="settings">How prototypes are chosen, and the window they play in.</param>
        /// <param name="patternSeed">
        /// Seeds the per-boundary draw. Shared by both agents on purpose — it is
        /// the run's seed, not the agent's.
        /// </param>
        /// <param name="parameters">
        /// Baseline A's fitted parameters, for the aversion direction
        /// distributions only. Unlike <paramref name="patternSeed"/> the
        /// aversion stream is the agent's own, seeded through Reset: which way
        /// an agent happens to look away is not something the prototypes
        /// coordinate.
        /// </param>
        public ProposedGazePolicy(IGazePolicy substrate, ProposedSettings settings, int patternSeed,
            ShintaniGazeParameters parameters)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _substrate = substrate ?? throw new ArgumentNullException(nameof(substrate));
            _patternSeed = patternSeed;
            _aversion = new AversionSampler(parameters, _random);
            Reset(0);
        }

        /// <summary>True while a prototype is overriding the substrate.</summary>
        public bool IsPatternActive { get; private set; }

        /// <summary>The prototype currently playing, or null when the substrate has control.</summary>
        public GazePattern ActivePattern { get; private set; }

        /// <inheritdoc/>
        public void Reset(int seed)
        {
            _substrate.Reset(seed);
            _random.Reset(seed);
            _aversion.Reset();
            _averting = false;
            IsPatternActive = false;
            ActivePattern = null;
        }

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            var substrateTarget = _substrate.Update(deltaTime, in state);

            if (!TryPatternRole(in state, out var role))
            {
                IsPatternActive = false;
                ActivePattern = null;
                _averting = false;
                return substrateTarget;
            }

            IsPatternActive = true;

            if (role != GazeRole.None)
            {
                _averting = false;
                return GazeTarget.AtPerson(PersonInRole(role, in state));
            }

            // The prototype's "None" is an aversion, rendered with the shared
            // sampler so it looks like every other aversion in the study.
            if (_averting)
            {
                _aversion.Tick(deltaTime);
            }
            else
            {
                _averting = true;
                _aversion.Begin(state.SelfRole);
            }

            return GazeTarget.Away(_aversion.Offset);
        }

        /// <summary>
        /// The prototype that plays before a given boundary. Public and static so
        /// the run's metadata can record the whole mapping up front — the draw is
        /// a pure function of the seed, the index and the class, so the log can
        /// say which prototype every boundary will get before any of them does.
        /// </summary>
        public static GazePattern SelectPattern(ProposedSettings settings, int patternSeed, int eventIndex, EotType eotType)
        {
            if (settings.Selection == PatternSelection.Fixed)
                return GazePatterns.Of(settings.Pattern);

            var pool = GazePatterns.PoolFor(eotType);
            return pool[(int)(Mix(patternSeed, eventIndex) % (uint)pool.Count)];
        }

        /// <summary>
        /// FNV-1a over the two ints. Any decent mixer would do; this one is used
        /// because the runner already seeds with FNV-1a and because
        /// <c>GetHashCode</c> is randomised per process on modern .NET, which
        /// would silently break replay.
        /// </summary>
        static uint Mix(int seed, int eventIndex)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            var hash = offsetBasis;
            for (var shift = 0; shift < 32; shift += 8)
            {
                hash = (hash ^ (uint)((seed >> shift) & 0xFF)) * prime;
                hash = (hash ^ (uint)((eventIndex >> shift) & 0xFF)) * prime;
            }

            return hash;
        }

        /// <summary>
        /// The prototype segment covering this instant, or false when no pattern
        /// has anything to say — outside the window, between turns, or at a turn
        /// no annotated event ends.
        /// </summary>
        bool TryPatternRole(in ConversationState state, out GazeRole role)
        {
            role = GazeRole.None;

            var secondsToTurnEnd = state.PredictedTimeToTurnEnd;
            if (secondsToTurnEnd < 0f || state.UpcomingEventIndex < 0)
                return false;

            var pattern = SelectPattern(_settings, _patternSeed, state.UpcomingEventIndex, state.UpcomingEventType);
            var track = TrackFor(pattern, in state);

            // The window is anchored on the boundary, and the prototype starts
            // partway into it: the raw data's rank-0 subsequence begins at
            // WindowOffsetSeconds, not at the top of the window.
            var secondsToEndAtPatternStart =
                (_settings.PreTurnWindowSeconds - pattern.WindowOffsetSeconds) * _settings.PatternTimeScale;
            if (secondsToTurnEnd > secondsToEndAtPatternStart)
                return false;

            ActivePattern = pattern;
            role = SegmentAt(track, secondsToEndAtPatternStart - secondsToTurnEnd);
            return true;
        }

        /// <summary>
        /// The track this participant plays: the role it holds at the boundary
        /// it is heading into. A participant in neither named role is the
        /// boundary's listener and plays the listener track — in scene 2 the
        /// user takes the final turn, which puts the *other agent* in that
        /// seat. Safe against self-gaze by the data itself: no prototype's
        /// listener track contains <see cref="GazeRole.Listener"/>, so this
        /// track can never resolve a target back to its own player.
        /// </summary>
        static (float seconds, GazeRole target)[] TrackFor(GazePattern pattern, in ConversationState state)
        {
            if (state.SelfId == state.CurrentSpeaker)
                return pattern.CurrentSpeakerTrack;

            return state.SelfId == state.CurrentAddressee ? pattern.NextSpeakerTrack : pattern.ListenerTrack;
        }

        /// <summary>
        /// The track's segment at <paramref name="elapsed"/>, holding the last one
        /// past the end of the track — a prototype shorter than its window keeps
        /// its final gaze through to the boundary rather than releasing it.
        /// </summary>
        static GazeRole SegmentAt((float seconds, GazeRole target)[] track, float elapsed)
        {
            var remaining = elapsed;
            for (var i = 0; i < track.Length; i++)
            {
                remaining -= track[i].seconds;
                if (remaining < 0f)
                    return track[i].target;
            }

            return track[^1].target;
        }

        static ParticipantId PersonInRole(GazeRole role, in ConversationState state) => role switch
        {
            GazeRole.CurrentSpeaker => state.CurrentSpeaker,
            GazeRole.NextSpeaker => state.CurrentAddressee,
            GazeRole.Listener => Listener(in state),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Aversion is not a person."),
        };

        /// <summary>
        /// The third party — whoever is neither speaking nor being addressed. In
        /// scene 1 that is the human user; in scene 2's final turn the user is
        /// the addressee, so the listener is the other agent.
        /// </summary>
        static ParticipantId Listener(in ConversationState state)
        {
            if (state.FirstPartner != state.CurrentSpeaker && state.FirstPartner != state.CurrentAddressee)
                return state.FirstPartner;

            return state.SecondPartner != state.CurrentSpeaker && state.SecondPartner != state.CurrentAddressee
                ? state.SecondPartner
                : ParticipantId.None;
        }
    }
}
