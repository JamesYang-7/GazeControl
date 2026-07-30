using System;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Turns a raw per-tick voiced/unvoiced signal into a stable "who holds the
    /// floor" view, by applying the onset/offset hysteresis of baseline B (§3).
    /// Naive VAD-following jitters badly, which would unfairly weaken that
    /// baseline, so the smoothing lives here rather than in the policy.
    ///
    /// One shared instance observes every participant: this is perception (anyone
    /// can hear who is talking), not shared policy state, so it does not
    /// compromise the per-agent independence Baseline A requires.
    /// </summary>
    public sealed class VoiceActivityTracker
    {
        readonly float _onsetSeconds;
        readonly float _offsetSeconds;
        readonly bool[] _confirmed;
        readonly float[] _voicedRun;
        readonly float[] _silentRun;
        readonly float[] _voicedTotal;

        /// <param name="participantCount">Number of participants; ids index into this range.</param>
        /// <param name="onsetSeconds">Continuous voicing required before a participant counts as speaking.</param>
        /// <param name="offsetSeconds">Continuous silence required before their turn counts as ended.</param>
        public VoiceActivityTracker(int participantCount, float onsetSeconds, float offsetSeconds)
        {
            if (participantCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(participantCount));

            _onsetSeconds = onsetSeconds;
            _offsetSeconds = offsetSeconds;
            _confirmed = new bool[participantCount];
            _voicedRun = new float[participantCount];
            _silentRun = new float[participantCount];
            _voicedTotal = new float[participantCount];
        }

        public int ParticipantCount => _confirmed.Length;

        /// <summary>True once <paramref name="id"/> has held the floor past the onset threshold, until they pass the offset threshold.</summary>
        public bool IsVoiced(ParticipantId id) => id.IsValid && _confirmed[id.Index];

        /// <summary>
        /// Voiced seconds accumulated in the current utterance, used to reject
        /// backchannels. Only voiced time counts: an amplitude VAD drops out
        /// between words, and counting the gaps would let a short "mm-hm" followed
        /// by silence cross the backchannel threshold and steal a gaze shift.
        /// </summary>
        public float UtteranceSeconds(ParticipantId id) => id.IsValid ? _voicedTotal[id.Index] : 0f;

        public void Reset()
        {
            Array.Clear(_confirmed, 0, _confirmed.Length);
            Array.Clear(_voicedRun, 0, _voicedRun.Length);
            Array.Clear(_silentRun, 0, _silentRun.Length);
            Array.Clear(_voicedTotal, 0, _voicedTotal.Length);
        }

        /// <param name="deltaTime">Seconds since the previous tick.</param>
        /// <param name="rawVoiced">Unsmoothed voiced flag per participant, indexed by <see cref="ParticipantId.Index"/>.</param>
        public void Tick(float deltaTime, ReadOnlySpan<bool> rawVoiced)
        {
            if (rawVoiced.Length != _confirmed.Length)
                throw new ArgumentException($"Expected {_confirmed.Length} flags, got {rawVoiced.Length}.", nameof(rawVoiced));

            for (var i = 0; i < rawVoiced.Length; i++)
            {
                if (rawVoiced[i])
                {
                    _voicedRun[i] += deltaTime;
                    _voicedTotal[i] += deltaTime;
                    _silentRun[i] = 0f;

                    if (_voicedRun[i] >= _onsetSeconds)
                        _confirmed[i] = true;
                }
                else
                {
                    _silentRun[i] += deltaTime;
                    _voicedRun[i] = 0f;

                    if (_confirmed[i] && _silentRun[i] >= _offsetSeconds)
                        _confirmed[i] = false;

                    if (!_confirmed[i])
                        _voicedTotal[i] = 0f;
                }
            }
        }
    }
}
