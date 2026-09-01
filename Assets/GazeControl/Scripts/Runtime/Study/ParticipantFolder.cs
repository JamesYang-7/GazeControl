using System;
using System.Globalization;

namespace GazeControl.Study
{
    /// <summary>
    /// Which folder one participant's files go in, under the recordings root.
    ///
    /// <para>Shared rather than written twice: the questionnaire's
    /// <c>responses.csv</c>, the free-text comments and the session record all
    /// belong to the same participant and have to land together, and the one
    /// place they could drift apart is here — each computing "the participant's
    /// folder" for itself.</para>
    ///
    /// <para>Pure, so the debug-label exception below is settled by a test
    /// rather than by discovering mid-session that today's second development
    /// run refused to start.</para>
    /// </summary>
    public static class ParticipantFolder
    {
        /// <summary>
        /// The folder name for <paramref name="participant"/>: their label,
        /// except for the debugging label, which takes a timestamp.
        ///
        /// <para>Every development run carries <see cref="ParticipantLabel.DebugLabel"/>,
        /// and the response writer refuses to reopen a folder — so without the
        /// timestamp the second dev run of the day would refuse to start for a
        /// reason that has nothing to do with the study. A real participant's
        /// folder is opened once and never reopened, which is the guard.</para>
        /// </summary>
        public static string NameFor(string participant, DateTime localNow)
        {
            participant = participant?.Trim();
            if (string.IsNullOrEmpty(participant))
                throw new ArgumentException("A participant folder needs a label.", nameof(participant));

            return ParticipantLabel.IsDebugLabel(participant)
                ? $"{participant}_{localNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}"
                : participant;
        }
    }
}
