using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Where the eyes go while gaze is averted, and when they move again — Shintani
    /// et al.'s <c>L</c> (Eq 7) over nine direction categories, plus the measured
    /// re-target cadence within a single aversion.
    ///
    /// Its own class rather than private state inside one policy, so that any
    /// condition wanting corpus-shaped aversion gets exactly the same behaviour.
    ///
    /// It is currently reached only through <see cref="ShintaniGazePolicy"/> —
    /// which means Baseline A, and the proposed condition's substrate outside its
    /// pre-turn window. Inside that window the proposed condition recentres the
    /// eyes instead (user's call, 2026-08-02), so aversion is deliberately *not*
    /// uniform across conditions any more; <see cref="ProposedGazePolicy"/>
    /// records what that costs.
    /// </summary>
    public sealed class AversionSampler
    {
        readonly ShintaniGazeParameters _parameters;
        readonly DeterministicRandom _random;

        AversionDirection _direction;
        float _secondsToRetarget;

        /// <param name="random">
        /// The owning policy's stream, not a private one: an agent draws from a
        /// single seeded sequence so a trial replays exactly (§0.5).
        /// </param>
        public AversionSampler(ShintaniGazeParameters parameters, DeterministicRandom random)
        {
            _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        /// <summary>Current direction category.</summary>
        public AversionDirection Direction => _direction;

        /// <summary>Measured eye-in-head yaw/pitch for <see cref="Direction"/>, in degrees.</summary>
        public Vector2 Offset => _parameters.AversionAngles(_direction);

        /// <summary>Start a new aversion: draw the first direction from the role's marginals.</summary>
        public void Begin(ParticipantRole role)
        {
            _direction = (AversionDirection)_random.NextCategorical(_parameters.AversionDirectionWeights(role));
            _secondsToRetarget = _parameters.RetargetSeconds;
        }

        /// <summary>
        /// Advance an aversion already in progress, moving the eyes to another
        /// direction once <see cref="ShintaniGazeParameters.RetargetSeconds"/> has
        /// passed. Subsequent directions come from the first→second conditional,
        /// not the marginals: the corpus shows the eyes drifting between
        /// neighbouring categories rather than jumping anywhere at random.
        /// </summary>
        public void Tick(float deltaTime)
        {
            _secondsToRetarget -= deltaTime;
            if (_secondsToRetarget > 0f)
                return;

            _direction = (AversionDirection)_random.NextCategorical(_parameters.AversionDirectionWeights(_direction));
            _secondsToRetarget = _parameters.RetargetSeconds;
        }

        public void Reset()
        {
            _direction = AversionDirection.Forward;
            _secondsToRetarget = 0f;
        }
    }
}
