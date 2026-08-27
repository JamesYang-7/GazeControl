namespace GazeControl.Study
{
    /// <summary>
    /// One screen in a participant's run of the questionnaire: what it asks and
    /// where in the session it sits.
    ///
    /// <para>It carries no wording. The text lives once in
    /// <see cref="QuestionnaireDefinition"/>, and a screen only says which part
    /// of it to render — so the VR canvas, the operator panel and the preview
    /// cannot drift apart, and a screen stays a value small enough to log.</para>
    ///
    /// <para>It also carries no conversation and no condition. Which clip played
    /// in a block and which method produced it are the study harness's, and the
    /// operator panel must never see the method at all (blinding,
    /// <c>questionnaire-ui-design.md</c> §2).</para>
    /// </summary>
    public readonly struct QuestionnaireScreen
    {
        public QuestionnaireScreen(QuestionnaireScreenKind kind, int number, int blockNumber, int versionPosition)
        {
            Kind = kind;
            Number = number;
            BlockNumber = blockNumber;
            VersionPosition = versionPosition;
        }

        public QuestionnaireScreenKind Kind { get; }

        /// <summary>1-based position in the whole session, for "screen 7 of 22".</summary>
        public int Number { get; }

        /// <summary>1-based block, or 0 on the framing and closing screens.</summary>
        public int BlockNumber { get; }

        /// <summary>
        /// 1-based position of the version being rated within its block, or 0
        /// where the screen is not about a single version.
        /// </summary>
        public int VersionPosition { get; }

        public override string ToString() => Kind switch
        {
            QuestionnaireScreenKind.Rating => $"{Number}: rating, block {BlockNumber}, version {VersionPosition}",
            QuestionnaireScreenKind.Ranking => $"{Number}: ranking, block {BlockNumber}",
            _ => $"{Number}: {Kind}",
        };
    }
}
