namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Whether the conversation is near a turn instant. Baseline A conditions
    /// both its target ratios and its dwell durations on this, which is where all
    /// of its turn-boundary behaviour comes from — the model has no hand-written
    /// turn-onset or turn-yielding override.
    ///
    /// The corpus labels a fixation <see cref="Changing"/> when it starts within
    /// ±1 s of the nearest annotated turn instant, in either direction.
    /// </summary>
    public enum TurnState
    {
        Holding = 0,
        Changing = 1,
    }
}
