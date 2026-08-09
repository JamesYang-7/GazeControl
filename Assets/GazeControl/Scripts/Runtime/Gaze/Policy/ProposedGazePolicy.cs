using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// The proposed condition: a substrate policy that hands control to one of the
    /// paper's measured pre-turn prototypes for the last second of a turn, and
    /// takes it back at the boundary.
    ///
    /// The substrate is **Baseline B** (speaker-following), by the user's call of
    /// 2026-08-09. That reverses the 2026-07-27 decision to build on Baseline A,
    /// and it changes what each pairwise comparison means: against baseline B the
    /// two conditions are identical outside the window, so the contrast isolates
    /// the pattern exactly; against baseline A it is now a comparison of two whole
    /// gaze models. Report the pairings accordingly.
    ///
    /// Two things fall out of the choice. Baseline B never averts (§3) and the
    /// prototypes' "None" recentres the eyes, so this condition has no directional
    /// aversion anywhere — consistent within itself, unlike the mixed rendering
    /// the Baseline A substrate produced. And because baseline B carries no random
    /// stream, the condition is fully deterministic: one recording is the
    /// condition, not one draw from it.
    ///
    /// The substrate is ticked on every call, including while the pattern is
    /// overriding it, so its hysteresis clocks follow the same trajectory whether
    /// or not a turn boundary happens to be near — the window changes what is
    /// rendered, never what the substrate would have done.
    /// </summary>
    public sealed class ProposedGazePolicy : IGazePolicy
    {
        readonly GazePattern _pattern;
        readonly float _windowSeconds;
        readonly float _timeScale;
        readonly IGazePolicy _substrate;

        /// <param name="substrate">Drives gaze outside the pre-turn window; baseline B in the study.</param>
        /// <param name="settings">Which prototype to play, and the window it plays in.</param>
        public ProposedGazePolicy(IGazePolicy substrate, ProposedSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            _substrate = substrate ?? throw new ArgumentNullException(nameof(substrate));
            _pattern = GazePatterns.Of(settings.Pattern);
            _windowSeconds = settings.PreTurnWindowSeconds;
            _timeScale = settings.PatternTimeScale;
            Reset(0);
        }

        /// <summary>The prototype currently loaded, for the log's metadata sidecar.</summary>
        public string PatternName => _pattern.Name;

        /// <summary>True while the pattern is overriding the substrate.</summary>
        public bool IsPatternActive { get; private set; }

        /// <inheritdoc/>
        public void Reset(int seed)
        {
            _substrate.Reset(seed);
            IsPatternActive = false;
        }

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            var substrateTarget = _substrate.Update(deltaTime, in state);

            if (!TryPatternRole(in state, out var role))
            {
                IsPatternActive = false;
                return substrateTarget;
            }

            IsPatternActive = true;

            if (role != GazeRole.None)
                return GazeTarget.AtPerson(PersonInRole(role, in state));

            // The prototype's "None" recentres the eyes in the head rather than
            // choosing a direction. A zero offset is how that is expressed: the
            // animation layer aims the eyes along the head's forward direction,
            // which is the same pose the old scripted playback produced with a
            // null target.
            return GazeTarget.Away(Vector2.zero);
        }

        /// <summary>
        /// The prototype segment covering this instant, or false when the pattern
        /// has nothing to say — outside the window, between turns, or for the
        /// participant the prototype carries no track for.
        /// </summary>
        bool TryPatternRole(in ConversationState state, out GazeRole role)
        {
            role = GazeRole.None;

            var secondsToTurnEnd = state.PredictedTimeToTurnEnd;
            if (secondsToTurnEnd < 0f)
                return false;

            var track = TrackFor(in state);
            if (track == null)
                return false;

            // The window is anchored on end-of-turn, and the prototype starts
            // partway into it: the raw data's rank-0 subsequence begins at
            // WindowOffsetSeconds, not at the top of the window.
            var secondsToEndAtPatternStart = (_windowSeconds - _pattern.WindowOffsetSeconds) * _timeScale;
            if (secondsToTurnEnd > secondsToEndAtPatternStart)
                return false;

            role = SegmentAt(track, secondsToEndAtPatternStart - secondsToTurnEnd);
            return true;
        }

        (float seconds, GazeRole target)[] TrackFor(in ConversationState state)
        {
            if (state.SelfId == state.CurrentSpeaker)
                return _pattern.CurrentSpeakerTrack;

            return state.SelfId == state.CurrentAddressee ? _pattern.NextSpeakerTrack : null;
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
        /// The third party — whoever is neither speaking nor being addressed.
        /// Unreachable with the two committed prototypes, whose tracks name only
        /// the speaker, the next speaker and aversion; it exists so a prototype
        /// transcribed later cannot silently mean something else.
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
