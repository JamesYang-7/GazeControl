using System;
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
        /// How many participants ran before the counterbalanced schedule existed.
        /// P01-P04 ran a single fixed method order — every one of them the same
        /// one — and are declared pilots, excluded from analysis
        /// (`user-study-design.md` §0.1, 2026-08-31).
        /// </summary>
        public const int PilotCount = 4;

        /// <summary>
        /// The first participant of the analysed study, and so the first one the
        /// counterbalancing schedule covers.
        /// </summary>
        public const string FirstStudyLabel = "P05";

        /// <summary>
        /// This participant's 0-based place in the counterbalancing schedule, or
        /// <c>-1</c> for a pilot, the debugging label, or anything unparseable.
        ///
        /// <para>The schedule covers the analysed participants only, so the
        /// pilots are genuinely outside it rather than mapped into it — a pilot
        /// that silently took a schedule slot would make the marginal balance
        /// claim depend on runs that are not in the sample.</para>
        /// </summary>
        public static int ScheduleOrdinal(string label)
        {
            if (!TryTrailingNumber(label, out var number) || number <= PilotCount)
                return -1;

            return (int)(number - (PilotCount + 1));
        }

        /// <summary>
        /// Whether a label is the reserved debugging one. Case- and
        /// whitespace-insensitive, because it is typed into an inspector field.
        /// </summary>
        public static bool IsDebugLabel(string label) =>
            string.Equals(label?.Trim(), DebugLabel, StringComparison.OrdinalIgnoreCase);

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

            // No trailing number to step: "P" becomes "P1" rather than being
            // refused, because refusing mid-session helps nobody.
            if (!TryTrailingNumber(current, out var number, out var start))
                return current + "1";

            var digits = current.Substring(start);
            var next = (number + 1).ToString(CultureInfo.InvariantCulture);
            if (next.Length < digits.Length)
                next = next.PadLeft(digits.Length, '0');

            return current.Substring(0, start) + next;
        }

        static bool TryTrailingNumber(string label, out long number) =>
            TryTrailingNumber(label, out number, out _);

        /// <summary>
        /// The label's trailing digits as a number, and where they start.
        ///
        /// <para>Walks back over the digits rather than matching a format, so
        /// "P01" and "pilot_3" both work without the caller having to declare
        /// one.</para>
        /// </summary>
        static bool TryTrailingNumber(string label, out long number, out int start)
        {
            number = 0;
            start = 0;

            if (string.IsNullOrWhiteSpace(label))
                return false;

            label = label.Trim();

            var end = label.Length;
            start = end;
            while (start > 0 && char.IsDigit(label[start - 1]))
                start--;

            return start != end &&
                   long.TryParse(label.Substring(start), NumberStyles.None,
                       CultureInfo.InvariantCulture, out number);
        }
    }
}
