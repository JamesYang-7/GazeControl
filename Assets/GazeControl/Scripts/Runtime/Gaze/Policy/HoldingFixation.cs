namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// One replayed fixation from the holding-sequence bank: what the recorded
    /// gazer looked at, in the corpus's role coding, and for how long.
    /// </summary>
    public readonly struct HoldingFixation
    {
        public HoldingFixation(GazeTargetRole target, float durationSeconds)
        {
            Target = target;
            DurationSeconds = durationSeconds;
        }

        public GazeTargetRole Target { get; }

        public float DurationSeconds { get; }
    }
}
