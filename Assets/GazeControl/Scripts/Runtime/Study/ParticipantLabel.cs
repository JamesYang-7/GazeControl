using System.Globalization;

namespace GazeControl.Study
{
    /// <summary>
    /// The study participant's label — <c>P01</c>, <c>P02</c> — and how to step
    /// to the next one.
    ///
    /// <para>This is the <b>human subject's</b> identifier, not a triad member's.
    /// <c>GazeParticipant.Id</c> is the other thing called an id: it names
    /// AgentA/AgentB/User inside one session, indexes the runner's arrays, and
    /// never changes. This one changes exactly once per participant and appears
    /// in every filename, the <c>participant_id</c> column and the sidecar.</para>
    ///
    /// <para>Pure, so the awkward cases — carrying 09 to 10, widening 99 to 100,
    /// a label with no digits at all — are settled by tests rather than by
    /// discovering them while a participant waits.</para>
    /// </summary>
    public static class ParticipantLabel
    {
        /// <summary>
        /// Reserved for development runs, and the scene's committed default. A
        /// real participant never carries it: a session labelled P00 is a
        /// debugging take, so the study guard refuses to start on it.
        /// </summary>
        public const string DebugLabel = "P00";

        /// <summary>
        /// The first real participant. <see cref="Next"/> reaches it from
        /// <see cref="DebugLabel"/> in one step, which is the whole workflow —
        /// leave the scene on P00, press the shortcut once before the first
        /// participant.
        /// </summary>
        public const string First = "P01";

        /// <summary>
        /// The label after <paramref name="current"/>: its trailing digits
        /// incremented, its prefix and zero-padding kept.
        ///
        /// <para>Padding is preserved rather than fixed, so a study numbered
        /// <c>P01</c> stays two-wide through <c>P30</c> and its files keep
        /// sorting in run order. It widens only when it has to, at <c>P99</c>.</para>
        /// </summary>
        public static string Next(string current)
        {
            if (string.IsNullOrWhiteSpace(current))
                return First;

            current = current.Trim();

            // Walk back over the trailing digits; everything before them is a
            // prefix to keep, so "P01" and "pilot_3" both work without the caller
            // having to declare a format.
            var end = current.Length;
            var start = end;
            while (start > 0 && char.IsDigit(current[start - 1]))
                start--;

            // No trailing number to step: "P" becomes "P1" rather than being
            // refused, because refusing mid-session helps nobody.
            if (start == end)
                return current + "1";

            var digits = current.Substring(start);
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                return current + "1";

            var next = (number + 1).ToString(CultureInfo.InvariantCulture);
            if (next.Length < digits.Length)
                next = next.PadLeft(digits.Length, '0');

            return current.Substring(0, start) + next;
        }
    }
}
