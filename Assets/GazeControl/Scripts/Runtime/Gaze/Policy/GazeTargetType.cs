namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Kind of gaze target. Aversion is a first-class target, not an idle state:
    /// modelling it as noise would weaken Baseline A and make the comparison unfair.
    /// </summary>
    public enum GazeTargetType
    {
        Person,
        Aversion,
    }
}
