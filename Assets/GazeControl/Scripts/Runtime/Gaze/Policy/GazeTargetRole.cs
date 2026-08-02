namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// What a gaze lands on, in the corpus's role-relative coding: the two other
    /// participants named by their conversational role, or no one.
    ///
    /// The values index the corpus tables directly (<c>[sp, ad, sd, aversion]</c>),
    /// so the order must not change without regenerating
    /// <c>BaselineAParameters.json</c>. The first three values line up with
    /// <see cref="ParticipantRole"/> by design.
    /// </summary>
    public enum GazeTargetRole
    {
        Speaker = 0,
        Addressee = 1,
        SideParticipant = 2,
        Aversion = 3,
    }
}
