using System;
using System.Collections.Generic;

namespace GazeControl.Study
{
    /// <summary>
    /// The full run of screens one participant is taken through: the framing,
    /// then every block's ratings and ranking, then the close.
    ///
    /// <para>For the study as designed (<c>user-study-design.md</c> §1) that is
    /// 5 blocks × (3 ratings + 1 ranking), two passages and the two preview
    /// screens — 24 screens.</para>
    ///
    /// <para>It sequences <em>screens</em> and nothing else. Which conversation a
    /// block uses, which method each version position is, and the counterbalancing
    /// that decides both are the study harness's (§9.5), which does not exist yet.
    /// Keeping them apart is what lets this half be built and tested with no
    /// headset, no scene and no clips.</para>
    /// </summary>
    public sealed class QuestionnaireScript
    {
        /// <summary>Conversations per participant (§1).</summary>
        public const int StudyBlockCount = 5;

        /// <summary>Methods compared within each conversation (§1).</summary>
        public const int StudyVersionsPerBlock = 3;

        readonly QuestionnaireScreen[] _screens;

        QuestionnaireScript(
            QuestionnaireDefinition definition,
            int blockCount,
            int versionsPerBlock,
            QuestionnaireScreen[] screens)
        {
            Definition = definition;
            BlockCount = blockCount;
            VersionsPerBlock = versionsPerBlock;
            _screens = screens;
        }

        public QuestionnaireDefinition Definition { get; }

        public int BlockCount { get; }

        public int VersionsPerBlock { get; }

        public IReadOnlyList<QuestionnaireScreen> Screens => _screens;

        /// <summary>The 5 × 3 run the study actually administers.</summary>
        public static QuestionnaireScript BuildForStudy(QuestionnaireDefinition definition) =>
            Build(definition, StudyBlockCount, StudyVersionsPerBlock);

        /// <summary>
        /// A run of <paramref name="blockCount"/> blocks of
        /// <paramref name="versionsPerBlock"/> versions each. Both are arguments
        /// rather than constants so a pilot can be run short without editing code.
        /// </summary>
        public static QuestionnaireScript Build(
            QuestionnaireDefinition definition, int blockCount, int versionsPerBlock)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (blockCount < 1)
                throw new ArgumentOutOfRangeException(nameof(blockCount), blockCount, "a run needs at least one block");

            // Two is the smallest thing that can be ranked; one version per block
            // would produce a ranking screen with a single answer, which reads as
            // a working session and yields nothing.
            if (versionsPerBlock < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(versionsPerBlock), versionsPerBlock, "a block needs at least two versions to rank");
            }

            var screens = new QuestionnaireScreen[4 + blockCount * (versionsPerBlock + 1)];
            var index = 0;

            void Add(QuestionnaireScreenKind kind, int block, int versionPosition)
            {
                screens[index] = new QuestionnaireScreen(kind, index + 1, block, versionPosition);
                index++;
            }

            Add(QuestionnaireScreenKind.Framing, 0, 0);

            // Both question screens are shown before the first clip, not only
            // after one: a participant who knows they will rate four things and
            // then rank the three versions watches the first group for what they
            // will be asked about, rather than meeting the questions when it is
            // too late to look.
            Add(QuestionnaireScreenKind.RatingPreview, 0, 0);
            Add(QuestionnaireScreenKind.ShortAnswerPreview, 0, 0);

            for (var block = 1; block <= blockCount; block++)
            {
                for (var position = 1; position <= versionsPerBlock; position++)
                    Add(QuestionnaireScreenKind.Rating, block, position);

                Add(QuestionnaireScreenKind.Ranking, block, 0);
            }

            Add(QuestionnaireScreenKind.Closing, 0, 0);

            return new QuestionnaireScript(definition, blockCount, versionsPerBlock, screens);
        }

        /// <summary>How many answers a completed run should contain: every item on every clip, plus every rank.</summary>
        public int ExpectedResponseCount =>
            BlockCount * VersionsPerBlock * (Definition.perClipItems.Length + 1);
    }
}
