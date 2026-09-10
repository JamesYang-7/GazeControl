using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GazeControl.Motion;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// Where the 3People-2022 replay reads from, and what its files are called.
    ///
    /// <para>Four roots, none of them inside this project: the converted SMPL-X
    /// corpus written by <c>Tools/convert_3people_smplx.py</c>, the dataset's own
    /// processed tree (audio and captions), and the end-of-turn tables. The
    /// naming rules are here rather than spread through the replay component so
    /// that they can be read — and tested — in one place.</para>
    /// </summary>
    public static class ThreePartySources
    {
        /// <summary>Everyone in the room; the capture is three-party throughout.</summary>
        public const int ParticipantCount = 3;

        /// <summary>Motion and the mocap clock, 60 fps on every session.</summary>
        public const float FrameRate = 60f;

        /// <summary>
        /// Converted SMPL-X clips, <c>&lt;root&gt;/&lt;date&gt;/Session_&lt;S&gt;_pc&lt;N&gt;_&lt;Name&gt;.npz</c>.
        /// Machine-local, so it comes from <see cref="MotionDataRoot"/> rather
        /// than being a constant here — a clone that keeps the corpus somewhere
        /// else configures it in one place.
        /// </summary>
        public static string DefaultMotionRoot => MotionDataRoot.ThreePeopleSmplx;

        /// <summary>The dataset's processed tree, <c>{TPC_DATA}</c>.</summary>
        public const string DefaultDataRoot = @"D:\3People-2022";

        /// <summary>The end-of-turn tables, <c>{TPC_EOT}</c>.</summary>
        public const string DefaultEventRoot = @"F:\Data\GazePattern\EoT_2022";

        static readonly Regex k_MotionFile = new(@"^Session_(\d+)_pc(\d+)_(.+)$", RegexOptions.Compiled);

        /// <summary>The one motion file for a participant; the subject's name is not known in advance.</summary>
        public static string MotionSearchPattern(int session, int pc) => $"Session_{session}_pc{pc}_*.npz";

        public static string AudioFileName(int session, int pc) => $"Session_{session}_PC_{pc}_audio.wav";

        public static string CaptionFileName(int session, int pc) => $"Session_{session}_PC_{pc}_sentence.csv";

        public static string EventFileName(string date, int session) => $"{date}_Session_{session}_eot.csv";

        public static string AudioDirectory(string dataRoot, string date) =>
            Path.Combine(dataRoot, "audio_text", date);

        /// <summary>
        /// <c>Caption/</c>, not <c>Caption_SentenceBased/</c>: the two disagree
        /// substantially, and the end-of-turn tables were built from this one, so
        /// a caption shown here has to be the one an event was derived from.
        /// </summary>
        public static string CaptionDirectory(string dataRoot, string date) =>
            Path.Combine(dataRoot, "mocap", date, "Caption");

        /// <summary>The participant's name out of a converted clip's file name.</summary>
        public static string SubjectOf(string motionFileName)
        {
            var stem = Path.GetFileNameWithoutExtension(motionFileName);
            var match = k_MotionFile.Match(stem);
            return match.Success ? match.Groups[3].Value : stem;
        }

        /// <summary>Recording dates with converted motion, oldest capture first is not meaningful — sorted by name.</summary>
        public static string[] Dates(string motionRoot)
        {
            if (!Directory.Exists(motionRoot))
                return Array.Empty<string>();

            var dates = new List<string>();
            foreach (var directory in Directory.EnumerateDirectories(motionRoot))
                dates.Add(Path.GetFileName(directory));

            dates.Sort(StringComparer.Ordinal);
            return dates.ToArray();
        }

        /// <summary>Session numbers converted for one date, ascending.</summary>
        public static int[] Sessions(string motionRoot, string date)
        {
            var directory = Path.Combine(motionRoot, date);
            if (!Directory.Exists(directory))
                return Array.Empty<int>();

            var sessions = new SortedSet<int>();
            foreach (var path in Directory.EnumerateFiles(directory, "Session_*.npz"))
            {
                var match = k_MotionFile.Match(Path.GetFileNameWithoutExtension(path));
                if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var session))
                    sessions.Add(session);
            }

            var ordered = new int[sessions.Count];
            sessions.CopyTo(ordered);
            return ordered;
        }

        /// <summary>
        /// The three participants of one session, by ascending PC number.
        /// </summary>
        /// <exception cref="FileNotFoundException">A participant's motion or audio is missing.</exception>
        public static ThreePartyClip[] Resolve(string motionRoot, string dataRoot, string date, int session)
        {
            var motionDirectory = Path.Combine(motionRoot, date);
            if (!Directory.Exists(motionDirectory))
                throw new DirectoryNotFoundException($"no converted motion for {date}: {motionDirectory}");

            var audioDirectory = AudioDirectory(dataRoot, date);
            var captionDirectory = CaptionDirectory(dataRoot, date);
            var clips = new ThreePartyClip[ParticipantCount];

            for (var pc = 1; pc <= ParticipantCount; pc++)
            {
                var motion = FindSingle(motionDirectory, MotionSearchPattern(session, pc));
                if (motion == null)
                    throw new FileNotFoundException($"{date} Session {session}: no motion for PC {pc} in {motionDirectory}");

                var audio = Path.Combine(audioDirectory, AudioFileName(session, pc));
                if (!File.Exists(audio))
                    throw new FileNotFoundException($"{date} Session {session}: no audio for PC {pc}: {audio}", audio);

                // Captions are optional: they only caption the replay, and a
                // session missing one should still be watchable.
                var caption = Path.Combine(captionDirectory, CaptionFileName(session, pc));
                clips[pc - 1] = new ThreePartyClip(
                    pc, SubjectOf(motion), motion, audio, File.Exists(caption) ? caption : null);
            }

            return clips;
        }

        /// <summary>The end-of-turn table for a session, or null when it has none.</summary>
        public static string EventPath(string eventRoot, string date, int session)
        {
            var path = Path.Combine(eventRoot, EventFileName(date, session));
            return File.Exists(path) ? path : null;
        }

        static string FindSingle(string directory, string pattern)
        {
            foreach (var path in Directory.EnumerateFiles(directory, pattern))
                return path;

            return null;
        }
    }
}
