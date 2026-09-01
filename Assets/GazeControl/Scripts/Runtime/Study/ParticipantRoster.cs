using System;
using System.Collections.Generic;
using System.Globalization;

namespace GazeControl.Study
{
    /// <summary>
    /// Which participants have already run — read off the folders under the
    /// recordings root — and therefore which label the next one takes.
    ///
    /// <para>The label used to be typed into the inspector and stepped by hand.
    /// Forgetting the step is silent: the guard catches
    /// <see cref="ParticipantLabel.DebugLabel"/> and an empty label, but nothing
    /// caught running <c>P05</c> twice, and the collision surfaced only when the
    /// first questionnaire screen committed — after the participant had already
    /// watched a clip in the headset. Deriving the label from what is on disk
    /// removes the step, and with it both the repeat and the skipped number.</para>
    ///
    /// <para>Pure — it is given folder names, not a directory — so the awkward
    /// cases (a gap where an aborted participant's folder was deleted, the take
    /// folders that share the root, a hand-made folder) are settled by tests.</para>
    /// </summary>
    public static class ParticipantRoster
    {
        /// <summary>
        /// Whether <paramref name="folderName"/> is one participant's folder.
        ///
        /// <para>Deliberately strict: the label's own prefix followed by digits
        /// and nothing else. The recordings root also holds the per-conversation
        /// take folders (<c>study_c1</c>, <c>case1_01</c>, <c>cand_v008</c>), and
        /// a looser rule — anything ending in digits — would read every one of
        /// them as a participant and hand the next real one a label far past the
        /// end of the study.</para>
        ///
        /// <para>The timestamped debug folders (<c>P00_20260901_143000</c>) fail
        /// it too, which is what keeps development runs out of the roster.</para>
        /// </summary>
        public static bool IsParticipantFolder(string folderName) =>
            TryNumberOf(folderName, out _);

        /// <summary>
        /// The participant numbers among <paramref name="folderNames"/>, ascending,
        /// with the debugging label's 0 excluded — it is not a participant.
        /// </summary>
        public static IReadOnlyList<int> NumbersIn(IEnumerable<string> folderNames)
        {
            var numbers = new List<int>();
            if (folderNames == null)
                return numbers;

            foreach (var name in folderNames)
            {
                if (TryNumberOf(name, out var number) && number > 0 && !numbers.Contains(number))
                    numbers.Add(number);
            }

            numbers.Sort();
            return numbers;
        }

        /// <summary>
        /// Whether this label has already run — compared by number, so a
        /// hand-typed <c>P7</c> still collides with the recorded <c>P07</c>.
        /// </summary>
        public static bool HasRun(string label, IEnumerable<string> folderNames)
        {
            if (!TryNumberOf(label, out var number) || number <= 0)
                return false;

            foreach (var existing in NumbersIn(folderNames))
            {
                if (existing == number)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The label the next participant should take: one past the highest that
        /// has run, or <see cref="ParticipantLabel.First"/> when none has.
        ///
        /// <para>One past the <i>highest</i>, not the first gap. A gap is an
        /// aborted participant whose folder was deleted, and reusing their number
        /// would put two people's data under one label in whatever partial files
        /// survive elsewhere — the take logs are named per conversation, not per
        /// participant folder, so deleting the folder does not delete the
        /// evidence. Skipping a number costs nothing; reusing one is
        /// unrecoverable.</para>
        /// </summary>
        public static string NextFree(IEnumerable<string> folderNames)
        {
            var numbers = NumbersIn(folderNames);
            if (numbers.Count == 0)
                return ParticipantLabel.First;

            // Stepped through ParticipantLabel rather than formatted here, so the
            // zero-padding rule lives in exactly one place and P99 widens the
            // same way from both.
            return ParticipantLabel.Next(LabelFor(numbers[^1]));
        }

        /// <summary>The label a participant number is written as, padded like <see cref="ParticipantLabel.First"/>.</summary>
        public static string LabelFor(int number) =>
            Prefix + number.ToString(PaddingFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// The highest-numbered participant folder, as it is written on disk, or
        /// null when there is none. The folder name rather than the label, so an
        /// operator reading the window sees the folder they would go and look in.
        /// </summary>
        public static string HighestIn(IEnumerable<string> folderNames)
        {
            string highestName = null;
            var highest = 0;

            if (folderNames == null)
                return null;

            foreach (var name in folderNames)
            {
                if (TryNumberOf(name, out var number) && number > highest)
                {
                    highest = number;
                    highestName = name.Trim();
                }
            }

            return highestName;
        }

        /// <summary>
        /// The label prefix and its width, taken from <see cref="ParticipantLabel.First"/>
        /// so that renaming the scheme there renames it here too.
        /// </summary>
        static string Prefix { get; } = PrefixOf(ParticipantLabel.First);

        static string PaddingFormat { get; } =
            new('0', ParticipantLabel.First.Length - PrefixOf(ParticipantLabel.First).Length);

        static string PrefixOf(string label)
        {
            var start = label.Length;
            while (start > 0 && char.IsDigit(label[start - 1]))
                start--;

            return label[..start];
        }

        /// <summary>The folder's participant number, when it is a participant folder at all.</summary>
        static bool TryNumberOf(string folderName, out int number)
        {
            number = 0;
            folderName = folderName?.Trim();

            if (string.IsNullOrEmpty(folderName) ||
                !folderName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var digits = folderName[Prefix.Length..];
            if (digits.Length == 0)
                return false;

            foreach (var c in digits)
            {
                if (!char.IsDigit(c))
                    return false;
            }

            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }
    }
}
