namespace GazeControl.ThreeParty
{
    /// <summary>
    /// One participant's share of a 3People-2022 session: which machine they sat
    /// at, who they are, and the three files that replay them — the converted
    /// SMPL-X motion, the trimmed wav and the caption track.
    ///
    /// <para>All three are keyed by the <b>true</b> PC number. That is safe for
    /// these three streams and only for these: the audio, the captions and the
    /// end-of-turn tables carry real participant numbers, and the converter
    /// resolved the motion through <c>Notes.xlsx</c> before naming its output.
    /// The raw <c>Mocap/Separate/..._PC_&lt;N&gt;_...</c> txt does not, and is
    /// never read here (see Assets/Docs/3people-motion-sources.md).</para>
    /// </summary>
    public sealed class ThreePartyClip
    {
        public ThreePartyClip(int pc, string subject, string motionPath, string audioPath, string captionPath)
        {
            Pc = pc;
            Subject = subject;
            MotionPath = motionPath;
            AudioPath = audioPath;
            CaptionPath = captionPath;
        }

        /// <summary>True participant number, 1-3.</summary>
        public int Pc { get; }

        /// <summary>The participant's first name, as the converter read it from the mocap CSV.</summary>
        public string Subject { get; }

        /// <summary>Converted SMPL-X clip for the whole session, 60 fps.</summary>
        public string MotionPath { get; }

        /// <summary>This participant's own microphone track for the whole session.</summary>
        public string AudioPath { get; }

        /// <summary>Sentence-level ASR transcript, or null when the session has none.</summary>
        public string CaptionPath { get; }

        /// <summary>How this participant is named on screen and in logs.</summary>
        public string Label => $"PC {Pc} {Subject}";
    }
}
