namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Where the eyeballs point while gaze is averted — Shintani's nine-way
    /// direction category (their <c>L</c>, Eq 7), recovered from the corpus's
    /// head-mounted eye tracker.
    ///
    /// The values index the corpus tables (<c>[lu, u, ru, l, f, r, ld, d, rd]</c>),
    /// so the order must not change without regenerating
    /// <c>BaselineAParameters.json</c>. <see cref="Forward"/> is "eyes centred in
    /// the head while looking at no one", not a fourth person.
    /// </summary>
    public enum AversionDirection
    {
        UpLeft = 0,
        Up = 1,
        UpRight = 2,
        Left = 3,
        Forward = 4,
        Right = 5,
        DownLeft = 6,
        Down = 7,
        DownRight = 8,
    }
}
