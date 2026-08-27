namespace GazeControl.Study
{
    /// <summary>What a questionnaire screen asks for.</summary>
    public enum QuestionnaireScreenKind
    {
        /// <summary>The framing passage, once, before the first clip.</summary>
        Framing,

        /// <summary>The four Likert items, after one clip.</summary>
        Rating,

        /// <summary>The ranking of a block's versions, plus the free-text probe.</summary>
        Ranking,

        /// <summary>The closing passage, once, after the last block.</summary>
        Closing,
    }
}
