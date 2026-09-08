namespace GazeControl.ThreeParty
{
    /// <summary>
    /// One line of a participant's sentence-level transcript, in seconds from
    /// the start of the recording — the same clock as the motion and the
    /// end-of-turn events.
    /// </summary>
    public readonly struct ThreePartyUtterance
    {
        public ThreePartyUtterance(float startSeconds, float endSeconds, string text)
        {
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            Text = text;
        }

        public float StartSeconds { get; }
        public float EndSeconds { get; }
        public string Text { get; }

        /// <summary>
        /// A recogniser tag rather than speech — <c>[Music]</c>, <c>[Laughter]</c>,
        /// <c>[Applause]</c>. Worth marking on screen, because a window whose only
        /// content is a tag is not a window of conversation.
        /// </summary>
        public bool IsTag => !string.IsNullOrEmpty(Text) && Text[0] == '[' && Text[^1] == ']';
    }
}
