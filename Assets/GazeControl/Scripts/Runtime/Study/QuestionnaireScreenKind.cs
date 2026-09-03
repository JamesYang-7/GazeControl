namespace GazeControl.Study
{
    /// <summary>What a questionnaire screen asks for.</summary>
    public enum QuestionnaireScreenKind
    {
        /// <summary>The framing passage, once, before the first clip.</summary>
        Framing,

        /// <summary>
        /// The four rating items, shown once between the framing and the first
        /// clip so the participant knows what they will be asked after every
        /// version before they watch one. Nothing is answered on it.
        /// </summary>
        RatingPreview,

        /// <summary>
        /// The two short-answer questions, shown once after
        /// <see cref="RatingPreview"/> for the same reason: they end a group,
        /// and knowing that is what makes the first group watchable for them.
        /// Nothing is answered on it either.
        /// </summary>
        ShortAnswerPreview,

        /// <summary>The four Likert items, after one clip.</summary>
        Rating,

        /// <summary>The ranking of a block's versions, plus the free-text probe.</summary>
        Ranking,

        /// <summary>The closing passage, once, after the last block.</summary>
        Closing,
    }
}
