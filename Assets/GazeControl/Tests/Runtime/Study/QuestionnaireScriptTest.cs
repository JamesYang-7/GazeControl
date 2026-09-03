using System;
using System.Linq;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class QuestionnaireScriptTest
    {
        static QuestionnaireScript Sut(int blocks = 2, int versions = 3) =>
            QuestionnaireScript.Build(QuestionnaireFixture.Definition(), blocks, versions);

        [Test]
        public void BuildForStudy_ProducesTheTwentyFourScreensTheDesignCountsOn()
        {
            var sut = QuestionnaireScript.BuildForStudy(QuestionnaireFixture.Definition());

            // Screens.Count, not Has.Count: NUnit resolves Has.Count by
            // reflecting on the runtime type, which is the backing array and so
            // exposes Length rather than Count.
            Assert.That(sut.Screens.Count, Is.EqualTo(24));
        }

        [Test]
        public void Build_OpensWithTheFramingAndClosesWithTheClosing()
        {
            var sut = Sut();

            Assert.That(sut.Screens[0].Kind, Is.EqualTo(QuestionnaireScreenKind.Framing), "first screen");
            Assert.That(sut.Screens[^1].Kind, Is.EqualTo(QuestionnaireScreenKind.Closing), "last screen");
        }

        [Test]
        public void Build_PutsOneRankingAfterEveryBlocksVersions()
        {
            var sut = Sut(blocks: 2, versions: 3);

            var kinds = sut.Screens.Select(screen => screen.Kind).ToArray();

            Assert.That(kinds, Is.EqualTo(new[]
            {
                QuestionnaireScreenKind.Framing,
                QuestionnaireScreenKind.RatingPreview,
                QuestionnaireScreenKind.ShortAnswerPreview,
                QuestionnaireScreenKind.Rating, QuestionnaireScreenKind.Rating, QuestionnaireScreenKind.Rating,
                QuestionnaireScreenKind.Ranking,
                QuestionnaireScreenKind.Rating, QuestionnaireScreenKind.Rating, QuestionnaireScreenKind.Rating,
                QuestionnaireScreenKind.Ranking,
                QuestionnaireScreenKind.Closing,
            }));
        }

        [Test]
        public void Build_NumbersTheScreensFromOneWithNoGaps()
        {
            var sut = Sut();

            var numbers = sut.Screens.Select(screen => screen.Number).ToArray();

            Assert.That(numbers, Is.EqualTo(Enumerable.Range(1, sut.Screens.Count).ToArray()));
        }

        [Test]
        public void Build_GivesEachRatingScreenItsPositionWithinTheBlock()
        {
            var sut = Sut(blocks: 2, versions: 3);

            var secondBlock = sut.Screens
                .Where(screen => screen.Kind == QuestionnaireScreenKind.Rating && screen.BlockNumber == 2)
                .Select(screen => screen.VersionPosition)
                .ToArray();

            Assert.That(secondBlock, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void Build_LeavesThePassagesOutsideAnyBlock()
        {
            var sut = Sut();

            var passages = sut.Screens.Where(screen =>
                screen.Kind is QuestionnaireScreenKind.Framing or QuestionnaireScreenKind.RatingPreview
                    or QuestionnaireScreenKind.ShortAnswerPreview or QuestionnaireScreenKind.Closing);

            Assert.That(passages, Has.All.Matches<QuestionnaireScreen>(screen => screen.BlockNumber == 0));
        }

        [Test]
        public void Build_WithNoBlocks_ThrowsArgumentOutOfRangeException()
        {
            Assert.That(() => Sut(blocks: 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Build_WithASingleVersionPerBlock_ThrowsArgumentOutOfRangeException()
        {
            Assert.That(() => Sut(versions: 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Build_WithNoDefinition_ThrowsArgumentNullException()
        {
            Assert.That(() => QuestionnaireScript.Build(null, 5, 3), Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void ExpectedResponseCount_CountsEveryItemAndRankOfEveryClip()
        {
            var sut = Sut(blocks: 2, versions: 3);

            // 2 items in the fixture plus one rank, per version, per block.
            Assert.That(sut.ExpectedResponseCount, Is.EqualTo(2 * 3 * 3));
        }
    }
}
