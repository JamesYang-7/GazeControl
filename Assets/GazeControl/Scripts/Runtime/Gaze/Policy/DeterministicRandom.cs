using System;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Seedable random source for the gaze policies — one instance per agent, so
    /// two Baseline A agents can never correlate through a shared stream (§5.1).
    ///
    /// Implements PCG32 (XSH-RR) explicitly rather than wrapping
    /// <see cref="System.Random"/> or <c>UnityEngine.Random</c>: the former's
    /// algorithm is not contractual and has already changed once between .NET
    /// runtimes, and the latter is global mutable state shared by every caller in
    /// the process. Either would break the promise that a seed replays a trial
    /// exactly (§0.5).
    /// </summary>
    public sealed class DeterministicRandom
    {
        const ulong Multiplier = 6364136223846793005UL;

        // PCG's stream selector; must be odd. One fixed stream is enough because
        // agents are separated by their seeds, not by their streams.
        const ulong Increment = 1442695040888963407UL | 1UL;

        ulong _state;
        float _spareGaussian;
        bool _hasSpareGaussian;

        public DeterministicRandom(int seed) => Reset(seed);

        /// <summary>Restart the stream from <paramref name="seed"/>.</summary>
        public void Reset(int seed)
        {
            _hasSpareGaussian = false;
            _state = 0UL;
            NextUInt();
            _state += (ulong)(uint)seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            var previous = _state;
            _state = previous * Multiplier + Increment;

            var xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
            var rotation = (int)(previous >> 59);
            return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float NextFloat() => (NextUInt() >> 8) * (1f / (1 << 24));

        /// <summary>Standard normal, by the Marsaglia polar method.</summary>
        public float NextGaussian()
        {
            // The polar method produces two independent samples per rejection
            // loop; dropping the second would double the cost for nothing.
            if (_hasSpareGaussian)
            {
                _hasSpareGaussian = false;
                return _spareGaussian;
            }

            float x, y, radiusSquared;
            do
            {
                x = NextFloat() * 2f - 1f;
                y = NextFloat() * 2f - 1f;
                radiusSquared = x * x + y * y;
            }
            while (radiusSquared >= 1f || radiusSquared <= 0f);

            var factor = (float)Math.Sqrt(-2.0 * Math.Log(radiusSquared) / radiusSquared);
            _spareGaussian = y * factor;
            _hasSpareGaussian = true;
            return x * factor;
        }

        /// <summary>Lognormal with the given parameters of the underlying normal.</summary>
        public float NextLogNormal(float mu, float sigma) => (float)Math.Exp(mu + sigma * NextGaussian());

        /// <summary>Index drawn in proportion to <paramref name="weights"/>, which need not sum to 1.</summary>
        public int NextCategorical(ReadOnlySpan<float> weights)
        {
            var total = 0f;
            for (var i = 0; i < weights.Length; i++)
                total += weights[i];

            if (total <= 0f)
                throw new ArgumentException("Categorical weights must contain at least one positive entry.", nameof(weights));

            var threshold = NextFloat() * total;
            var cumulative = 0f;

            for (var i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (threshold < cumulative)
                    return i;
            }

            // Rounding in the running sum can leave the threshold just past the
            // last boundary; fall back to the last entry that could be drawn.
            for (var i = weights.Length - 1; i >= 0; i--)
            {
                if (weights[i] > 0f)
                    return i;
            }

            throw new ArgumentException("Categorical weights must contain at least one positive entry.", nameof(weights));
        }
    }
}
