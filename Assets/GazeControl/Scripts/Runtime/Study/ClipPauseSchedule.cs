namespace GazeControl.Study
{
    /// <summary>Where a clip is in the play-then-answer cycle.</summary>
    public enum ClipPhase
    {
        /// <summary>The conversation is running and the participant is watching.</summary>
        Playing,

        /// <summary>The voices have stopped; the scene is holding before it is taken away.</summary>
        Holding,

        /// <summary>Stopped, waiting for the operator. This is when the participant answers.</summary>
        Paused,
    }

    /// <summary>
    /// Paces one clip of a study session: play, hold briefly once the voices
    /// stop, then stop and wait for the operator while the participant answers.
    ///
    /// <para>The hold exists because cutting to a blank scene on the last
    /// syllable reads as a crash rather than as an ending, and because the take's
    /// own <c>tailSeconds</c> is already the interval the recorder uses for
    /// exactly this. Zero is allowed and goes straight to <see cref="ClipPhase.Paused"/>.</para>
    ///
    /// <para>Pure, so the pacing can be tested without a headset, a segment or an
    /// audio device — the same split as <c>ViewRecentring</c> and
    /// <c>ParticipantEyeHeight</c>. The MonoBehaviour around it only supplies
    /// "has the conversation finished" and a delta time.</para>
    /// </summary>
    public sealed class ClipPauseSchedule
    {
        readonly float _holdSeconds;
        float _held;

        /// <param name="holdSeconds">
        /// How long the scene stays up after the voices stop. Negative values are
        /// treated as zero rather than rejected: the caller reads this off a
        /// segment file, and a malformed one should shorten the hold, not throw
        /// in the middle of a participant's session.
        /// </param>
        public ClipPauseSchedule(float holdSeconds)
        {
            _holdSeconds = holdSeconds > 0f ? holdSeconds : 0f;
        }

        /// <summary>Where the clip is now.</summary>
        public ClipPhase Phase { get; private set; } = ClipPhase.Playing;

        /// <summary>Seconds of hold still to run; zero unless <see cref="Phase"/> is Holding.</summary>
        public float HoldRemaining => Phase == ClipPhase.Holding ? _holdSeconds - _held : 0f;

        /// <summary>True once the participant may be asked to answer.</summary>
        public bool IsPaused => Phase == ClipPhase.Paused;

        /// <summary>
        /// Advance the cycle by one frame.
        /// </summary>
        /// <param name="clipFinished">Whether the conversation has run out of material.</param>
        /// <param name="deltaTime">Seconds since the last call; non-positive values do nothing.</param>
        /// <returns>The phase after this step.</returns>
        public ClipPhase Advance(bool clipFinished, float deltaTime)
        {
            switch (Phase)
            {
                case ClipPhase.Playing:
                    if (!clipFinished)
                        break;

                    // Enter the hold and immediately test it, so a zero hold
                    // pauses on the very frame the clip ends rather than one
                    // frame later.
                    Phase = ClipPhase.Holding;
                    _held = 0f;
                    if (_held >= _holdSeconds)
                        Phase = ClipPhase.Paused;

                    break;

                case ClipPhase.Holding:
                    if (deltaTime > 0f)
                        _held += deltaTime;

                    if (_held >= _holdSeconds)
                        Phase = ClipPhase.Paused;

                    break;

                case ClipPhase.Paused:
                    // Terminal until the operator advances. Deliberately ignores
                    // clipFinished going false again: a conversation that somehow
                    // restarted itself must not silently un-pause a participant
                    // who is halfway through answering.
                    break;
            }

            return Phase;
        }

        /// <summary>
        /// Jump straight to <see cref="ClipPhase.Paused"/>, skipping whatever is
        /// left of the clip and its hold.
        ///
        /// <para>For walking a whole session quickly while testing. It is the
        /// schedule's part of a skip only — the caller still has to stop the
        /// conversation itself, or the voices would play on over a paused
        /// scene.</para>
        /// </summary>
        public void SkipToPause()
        {
            Phase = ClipPhase.Paused;
            _held = _holdSeconds;
        }

        /// <summary>
        /// The operator has advanced; reset for the next clip. Safe to call in
        /// any phase, so an operator who advances early simply moves on.
        /// </summary>
        public void Resume()
        {
            Phase = ClipPhase.Playing;
            _held = 0f;
        }
    }
}
