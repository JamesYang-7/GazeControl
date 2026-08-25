using System;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Replays one agent's baked <see cref="GazeTrack"/> instead of deciding
    /// anything. Every study participant sees the same take because the take is
    /// a file, not a re-derivation.
    ///
    /// <para>It reads the conversation's clock rather than counting its own
    /// ticks. That is what makes it timing-proof: the runner starts ticking when
    /// Play begins but the conversation's clock starts only once the segment has
    /// loaded, and that gap varies with load time. A tick-counting replay would
    /// consume the track during that pre-roll and reintroduce exactly the drift
    /// baking exists to remove.</para>
    ///
    /// <para>It carries no random stream and ignores <see cref="Reset"/>'s seed:
    /// there is nothing left to seed. The seed that produced the track is
    /// recorded in the track's own metadata.</para>
    /// </summary>
    public sealed class BakedGazePolicy : IGazePolicy
    {
        readonly GazeTrackSample[] _samples;
        readonly Func<float> _clock;

        int _cursor;

        /// <param name="agent">The baked sequence for this agent.</param>
        /// <param name="clock">
        /// Conversation elapsed time in seconds; negative before playback starts.
        /// </param>
        public BakedGazePolicy(GazeTrackAgent agent, Func<float> clock)
        {
            if (agent == null)
                throw new ArgumentNullException(nameof(agent));

            if (agent.samples == null || agent.samples.Length == 0)
                throw new ArgumentException($"Agent {agent.agentId} has an empty track.", nameof(agent));

            _samples = agent.samples;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>The sample currently being replayed, for logging and tests.</summary>
        public int Cursor => _cursor;

        /// <inheritdoc/>
        public void Reset(int seed) => _cursor = 0;

        /// <inheritdoc/>
        public GazeTarget Update(float deltaTime, in ConversationState state)
        {
            var now = _clock();

            // Before the conversation starts the clock reads negative; hold the
            // first decision rather than advancing, so a long load cannot eat
            // the opening of the track.
            if (now < 0f)
                return _samples[0].ToTarget();

            // Monotone scan, not a search: the clock only moves forward, so the
            // cursor walks the track once over a take.
            while (_cursor + 1 < _samples.Length && _samples[_cursor + 1].t <= now)
                _cursor++;

            // A stalled player loop can leave the clock behind the cursor. The
            // cursor is not rewound: replaying a decision the take has already
            // shown would be a visible jump backwards, and the gaze log would
            // stop being monotone in time.
            return _samples[_cursor].ToTarget();
        }
    }
}
