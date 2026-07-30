using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Raw (unsmoothed) voice activity for one <see cref="AudioSource"/>, measured
    /// as the RMS of a short window of the clip at the current playback position.
    /// <see cref="Gaze.Policy.VoiceActivityTracker"/> applies the hysteresis.
    ///
    /// One instance per participant: it carries a playback clock.
    ///
    /// Reads the clip rather than <c>AudioSource.GetOutputData</c> on purpose: the
    /// agents are spatialised, so output data is attenuated by distance and would
    /// make a far agent's speech fall under the threshold — a detection that
    /// depends on where the listener stands is not voice activity.
    /// </summary>
    public sealed class AudioClipVoiceDetector
    {
        const int WindowSamples = 512;

        readonly float[] _window = new float[WindowSamples];

        AudioClip _playingClip;
        float _elapsedSeconds;

        /// <param name="source">Voice to measure; may be null (the human user has none).</param>
        /// <param name="rmsThreshold">RMS above which the window counts as voiced.</param>
        /// <param name="deltaTime">Seconds since the previous call, for the fallback playback clock.</param>
        public bool IsVoiced(AudioSource source, float rmsThreshold, float deltaTime)
        {
            if (source == null || source.clip == null || !source.isPlaying)
            {
                _playingClip = null;
                _elapsedSeconds = 0f;
                return false;
            }

            if (!ReferenceEquals(source.clip, _playingClip))
            {
                _playingClip = source.clip;
                _elapsedSeconds = 0f;
            }
            else
            {
                _elapsedSeconds += deltaTime;
            }

            var clip = source.clip;
            var offset = PlaybackOffset(source, clip);
            var channels = Mathf.Max(1, clip.channels);

            // GetData needs the whole buffer to be available; within one window of
            // the end we stop reporting voiced. Kokoro clips end in silence, so
            // this costs nothing but avoids a per-frame length dance.
            var availableSamples = (clip.samples - offset) * channels;
            if (offset < 0 || availableSamples < WindowSamples)
                return false;

            if (!clip.GetData(_window, offset))
                return false;

            var sumOfSquares = 0f;
            for (var i = 0; i < WindowSamples; i++)
                sumOfSquares += _window[i] * _window[i];

            return Mathf.Sqrt(sumOfSquares / WindowSamples) > rmsThreshold;
        }

        /// <summary>
        /// Where in the clip playback has reached. Prefers the audio engine's own
        /// position, but falls back to an internal clock when it is not advancing:
        /// if the output device fails to initialise, <c>isPlaying</c> stays true
        /// while <c>timeSamples</c> is pinned at 0, and reading there would sample
        /// the leading silence forever — a silent failure that yields a complete,
        /// plausible-looking gaze log in which nobody ever speaks.
        /// </summary>
        int PlaybackOffset(AudioSource source, AudioClip clip)
        {
            var enginePosition = source.timeSamples;
            return enginePosition > 0
                ? enginePosition
                : Mathf.FloorToInt(_elapsedSeconds * clip.frequency);
        }
    }
}
