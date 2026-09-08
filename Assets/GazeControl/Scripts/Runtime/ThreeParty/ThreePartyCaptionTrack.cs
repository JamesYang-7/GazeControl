using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// One participant's sentence-level transcript, read from
    /// <c>{TPC_DATA}/mocap/&lt;date&gt;/Caption/Session_&lt;S&gt;_PC_&lt;N&gt;_sentence.csv</c>.
    ///
    /// <para>These are the transcripts the end-of-turn events were built from, so
    /// what is shown on screen is what a candidate window was selected on. They
    /// are automatic: expect <c>[Music]</c> rows and microphone bleed putting a
    /// phantom line on the wrong track. Reading them beside the bodies is the
    /// only way to catch either.</para>
    /// </summary>
    public sealed class ThreePartyCaptionTrack
    {
        readonly List<ThreePartyUtterance> _utterances;

        ThreePartyCaptionTrack(List<ThreePartyUtterance> utterances)
        {
            _utterances = utterances;
        }

        /// <summary>Every line, in the file's order (which is chronological).</summary>
        public IReadOnlyList<ThreePartyUtterance> Utterances => _utterances;

        public static ThreePartyCaptionTrack Load(string path) => Parse(File.ReadLines(path));

        /// <summary>Parse the CSV body; the header row is recognised and skipped.</summary>
        public static ThreePartyCaptionTrack Parse(IEnumerable<string> lines)
        {
            var utterances = new List<ThreePartyUtterance>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("start,", StringComparison.Ordinal))
                    continue;

                // Split on the first two and the last comma rather than on every
                // one: the sentence field is free ASR text and may contain commas,
                // while the two clocks and the trailing SID never do.
                var firstComma = line.IndexOf(',');
                if (firstComma < 0) continue;
                var secondComma = line.IndexOf(',', firstComma + 1);
                if (secondComma < 0) continue;
                var lastComma = line.LastIndexOf(',');
                if (lastComma <= secondComma) continue;

                var start = ParseClock(line[..firstComma]);
                var end = ParseClock(line[(firstComma + 1)..secondComma]);
                var text = line[(secondComma + 1)..lastComma].Trim();
                if (float.IsNaN(start) || float.IsNaN(end))
                    continue;

                utterances.Add(new ThreePartyUtterance(start, end, text));
            }

            return new ThreePartyCaptionTrack(utterances);
        }

        /// <summary><c>hh:mm:ss.fff</c> to seconds; NaN when the field is not a clock.</summary>
        public static float ParseClock(string text)
        {
            var parts = text.Trim().Split(':');
            if (parts.Length != 3)
                return float.NaN;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                return float.NaN;

            return hours * 3600f + minutes * 60f + seconds;
        }

        /// <summary>What this participant is saying at <paramref name="seconds"/>, or null when nothing.</summary>
        public string TextAt(float seconds)
        {
            foreach (var utterance in _utterances)
            {
                if (utterance.StartSeconds > seconds)
                    break;

                if (seconds <= utterance.EndSeconds)
                    return utterance.Text;
            }

            return null;
        }
    }
}
