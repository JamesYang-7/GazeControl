using System;

namespace GazeControl.Study
{
    /// <summary>
    /// The row of buttons under a question page: undo, one per answer the screen
    /// takes, and next.
    ///
    /// <para><b>One shape for both answer screens</b> — the rating screen's row
    /// is the seven scale points and the short-answer screen's is the three
    /// versions, and they differ only in how many value buttons there are and
    /// what is written on them. Building both here, purely, is what lets the
    /// enabling rules be tested without a headset, a controller or a
    /// scene.</para>
    ///
    /// <para><b>Enabling is asked of the entry, never restated.</b>
    /// <see cref="QuestionnaireEntry.Accepts"/> is the same predicate that
    /// refuses the operator's keystroke, so a rank already used greys out for
    /// the participant for exactly the reason it is refused for the operator. A
    /// second copy of that rule here would be free to drift.</para>
    /// </summary>
    public static class QuestionnaireButtonRow
    {
        /// <summary>A screen that asks nothing shows no row at all.</summary>
        public static readonly QuestionnaireButton[] None = Array.Empty<QuestionnaireButton>();

        /// <summary>
        /// Words rather than arrow glyphs: the panel runs on the LiberationSans
        /// SDF font that ships with TMP, and a character it has no glyph for
        /// renders as an empty box — which on a button is indistinguishable from
        /// a button with no label.
        /// </summary>
        public const string BackLabel = "Undo";

        public const string NextLabel = "Next";

        /// <summary>
        /// Build the row for a screen.
        /// </summary>
        /// <param name="entry">
        /// What the screen has taken so far, or null on a screen that records
        /// nothing — the two previews, and the free-text probe the participant
        /// cannot type into. Undo and every value are disabled without one.
        /// </param>
        /// <param name="firstValue">The number the first value button enters; the rest follow it.</param>
        /// <param name="valueLabels">What each value button says, in row order.</param>
        /// <param name="commitAllowed">
        /// Whether <see cref="QuestionnaireButtonKind.Next"/> may be pressed. The
        /// caller decides, because "answered enough to move on" is the session's
        /// rule and differs per screen: every rating item answered, no rank
        /// missing, but a free-text answer may be left empty.
        /// </param>
        public static QuestionnaireButton[] Build(
            QuestionnaireEntry entry, int firstValue, string[] valueLabels, bool commitAllowed)
        {
            if (valueLabels == null || valueLabels.Length == 0)
                throw new ArgumentException("a row needs at least one value button", nameof(valueLabels));

            var row = new QuestionnaireButton[valueLabels.Length + 2];
            row[0] = new QuestionnaireButton(
                QuestionnaireButtonKind.Back, 0, BackLabel, entry != null && entry.Cursor > 0);

            for (var i = 0; i < valueLabels.Length; i++)
            {
                var value = firstValue + i;
                row[i + 1] = new QuestionnaireButton(
                    QuestionnaireButtonKind.Value, value, valueLabels[i], entry != null && entry.Accepts(value));
            }

            row[^1] = new QuestionnaireButton(QuestionnaireButtonKind.Next, 0, NextLabel, commitAllowed);
            return row;
        }

        /// <summary>
        /// A row of one button: the framing passage's <c>Next</c>. A passage
        /// asks nothing, so there is nothing to undo and no value to enter — but
        /// it still needs the participant's own way on, or a session they can
        /// otherwise finish alone would stop on its first screen.
        /// </summary>
        public static QuestionnaireButton[] NextOnly(bool commitAllowed) =>
            new[] { new QuestionnaireButton(QuestionnaireButtonKind.Next, 0, NextLabel, commitAllowed) };

        /// <summary>The scale's points as labels: "1" … "7".</summary>
        public static string[] ScaleLabels(int min, int max)
        {
            if (min > max)
                throw new ArgumentOutOfRangeException(nameof(min), min, $"min {min} is above max {max}");

            var labels = new string[max - min + 1];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = (min + i).ToString();

            return labels;
        }

        /// <summary>
        /// The versions as labels: "V1" … "V3". Short rather than the
        /// instrument's own "Version 1", because these sit side by side on one
        /// row a metre and a half away and the number is the whole of what
        /// distinguishes them; the page above still asks the question in the
        /// instrument's words.
        /// </summary>
        public static string[] VersionLabels(int versionCount)
        {
            if (versionCount < 1)
                throw new ArgumentOutOfRangeException(nameof(versionCount), versionCount, "a block has versions to rank");

            var labels = new string[versionCount];
            for (var i = 0; i < labels.Length; i++)
                labels[i] = $"V{i + 1}";

            return labels;
        }
    }
}
