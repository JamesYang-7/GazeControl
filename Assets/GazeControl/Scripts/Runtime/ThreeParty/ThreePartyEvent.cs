using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// One end-of-turn event from <c>{TPC_EOT}/&lt;date&gt;_Session_&lt;S&gt;_eot.csv</c>:
    /// who held the floor, who took it, and when. Times are seconds here; the
    /// file stores milliseconds.
    ///
    /// <para>Speaker codes are true PC numbers (unlike the raw motion export's),
    /// so they index the replay's participants directly. Marking these on screen
    /// is what makes a candidate window judgeable by eye: an event that lands on
    /// a phantom caption is a phantom event.</para>
    /// </summary>
    public readonly struct ThreePartyEvent
    {
        public ThreePartyEvent(int eotType, int firstSpeaker, int secondSpeaker,
            float turnSeconds, float startSeconds, float endSeconds)
        {
            EotType = eotType;
            FirstSpeaker = firstSpeaker;
            SecondSpeaker = secondSpeaker;
            TurnSeconds = turnSeconds;
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
        }

        /// <summary>1 interruption, 2 overlapping, 3 turn-taking.</summary>
        public int EotType { get; }

        public int FirstSpeaker { get; }
        public int SecondSpeaker { get; }

        /// <summary>The transition instant — what a pre-turn gaze pattern is anchored on.</summary>
        public float TurnSeconds { get; }

        public float StartSeconds { get; }
        public float EndSeconds { get; }

        public string TypeName => EotType switch
        {
            1 => "interruption",
            2 => "overlapping",
            3 => "turn-taking",
            _ => $"type {EotType}",
        };

        /// <summary>Read a session's table; the header row is skipped.</summary>
        public static List<ThreePartyEvent> Load(string path) => Parse(File.ReadLines(path));

        /// <summary>Parse the CSV body; the header row is recognised and skipped.</summary>
        public static List<ThreePartyEvent> Parse(IEnumerable<string> lines)
        {
            var events = new List<ThreePartyEvent>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("eot_type", StringComparison.Ordinal))
                    continue;

                var fields = line.Split(',');
                if (fields.Length < 6)
                    continue;

                if (!TryField(fields[0], out var type) || !TryField(fields[1], out var first) ||
                    !TryField(fields[2], out var second) || !TryField(fields[3], out var turn) ||
                    !TryField(fields[4], out var start) || !TryField(fields[5], out var end))
                    continue;

                events.Add(new ThreePartyEvent((int)type, (int)first, (int)second,
                    turn / 1000f, start / 1000f, end / 1000f));
            }

            return events;
        }

        static bool TryField(string text, out float value) =>
            float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
