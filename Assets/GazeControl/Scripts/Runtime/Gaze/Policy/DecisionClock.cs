using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// The fixed-rate clock the gaze policies are ticked on, anchored on the
    /// conversation's clock rather than on the moment Play started.
    ///
    /// <para>The anchor is the whole point. The runner wakes several frames
    /// before the segment has loaded and the voices are scheduled, and a
    /// free-running accumulator spends that pre-roll ticking the policies: a
    /// stochastic policy consumes draws, a replay policy advances its fixation
    /// clock, all before the conversation exists. Load time varies, so the
    /// number of pre-roll ticks varies with it — which is why two runs of one
    /// clip with identical seeds diverged (measured 2026-08-25). Reporting no
    /// tick while the clock reads negative removes the variable.</para>
    ///
    /// <para>Ticks are counted from the clock reading rather than accumulated
    /// from delta times: an accumulator drifts against the clock every other
    /// component reads, and that drift is what lands a decision on the wrong
    /// side of a turn boundary. Tick <c>k</c> fires on the first frame at or
    /// after <c>k · Step</c> seconds of conversation, so the grid is a function
    /// of the conversation clock alone.</para>
    /// </summary>
    public sealed class DecisionClock
    {
        readonly float _step;

        int _ticks;

        /// <param name="hz">Decision rate, ticks per second.</param>
        public DecisionClock(float hz)
        {
            if (hz <= 0f)
                throw new ArgumentOutOfRangeException(nameof(hz), hz, "The decision rate must be positive.");

            _step = 1f / hz;
        }

        /// <summary>Seconds between two decisions — the delta time a tick is given.</summary>
        public float Step => _step;

        /// <summary>How many decisions have fallen due so far.</summary>
        public int Ticks => _ticks;

        /// <summary>
        /// The conversation time tick <paramref name="tick"/> belongs to, which
        /// is the grid time and not the clock reading it fired on.
        ///
        /// <para>They differ when a frame is longer than a step — the frame the
        /// conversation starts on regularly is, because Unity clamps a long
        /// frame to <c>maximumDeltaTime</c> (a third of a second) and the whole
        /// catch-up burst then reads one clock value. Stamping a bake with the
        /// reading gave a dozen decisions the same timestamp and a track that
        /// was not strictly monotone; stamping it with the grid time gives each
        /// decision the instant it is actually replayed at.</para>
        /// </summary>
        public float TimeOfTick(int tick) => tick * _step;

        /// <summary>
        /// How many decision ticks are due at <paramref name="now"/>, consuming
        /// them: the caller runs exactly that many and no more.
        ///
        /// <para>Zero while the clock reads negative — the conversation has not
        /// started — and zero when the clock has stalled or gone backwards. A
        /// tick is never un-run, for the same reason the baked policy never
        /// rewinds its cursor: gaze would jump backwards and the log would stop
        /// being monotone in time.</para>
        /// </summary>
        /// <param name="now">Conversation elapsed time in seconds; negative before playback starts.</param>
        public int Advance(float now)
        {
            if (now < 0f)
                return 0;

            var due = Mathf.FloorToInt(now / _step) + 1 - _ticks;
            if (due <= 0)
                return 0;

            _ticks += due;
            return due;
        }
    }
}
