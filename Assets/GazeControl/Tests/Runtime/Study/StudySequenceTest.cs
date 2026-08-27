using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class StudySequenceTest
    {
        static readonly string[] Conversations =
            { "study_c1", "study_c2", "study_c3", "study_c4", "study_c5" };

        static readonly string[] Conditions =
            { "SpeakerFollowing", "RoleConditioned", "Proposed" };

        static IReadOnlyList<StudyTrial> Sut() => StudySequence.Build(Conversations, Conditions);

        [Test]
        public void Build_ProducesTheFullyCrossedFifteen()
        {
            var sut = Sut();

            Assert.That(sut.Count, Is.EqualTo(15));
            foreach (var conversation in Conversations)
                Assert.That(sut.Count(t => t.Conversation == conversation), Is.EqualTo(3),
                    $"{conversation} should appear once per method");

            foreach (var condition in Conditions)
                Assert.That(sut.Count(t => t.Condition == condition), Is.EqualTo(5),
                    $"{condition} should appear once per conversation");
        }

        [Test]
        public void Build_BlocksByConversation()
        {
            // The three methods of one conversation play back to back; the
            // ranking at the end of a block depends on it.
            foreach (var block in Sut().GroupBy(t => t.BlockNumber))
            {
                Assert.That(block.Select(t => t.Conversation).Distinct().Count(), Is.EqualTo(1),
                    "a block is one conversation");
                Assert.That(block.Select(t => t.Condition).Distinct().Count(), Is.EqualTo(3),
                    "and all three of its methods");
                Assert.That(block.Select(t => t.VersionPosition).OrderBy(p => p), Is.EqualTo(new[] { 1, 2, 3 }));
            }
        }

        [Test]
        public void Build_RunsBlocksInTheOrderTheConversationsWereGiven()
        {
            var sut = Sut();

            for (var block = 1; block <= Conversations.Length; block++)
                Assert.That(sut.First(t => t.BlockNumber == block).Conversation,
                    Is.EqualTo(Conversations[block - 1]));
        }

        [Test]
        public void Build_VariesTheMethodOrderBetweenBlocks()
        {
            // The point of the whole permutation table: back-to-back repetition
            // would otherwise teach a participant that the third clip is always
            // the same method, by block three.
            var orders = Sut()
                .GroupBy(t => t.BlockNumber)
                .Select(b => string.Join(",", b.OrderBy(t => t.VersionPosition).Select(t => t.Condition)))
                .ToList();

            Assert.That(orders.Distinct().Count(), Is.EqualTo(5), "every block uses a different order");
        }

        [Test]
        public void Build_BalancesEachMethodOverTheSerialPositions()
        {
            // The property that keeps a fixed order defensible: within one
            // participant, no method is systematically first. Five blocks over
            // three positions cannot be perfectly even, so 1 or 2 is the best
            // achievable and what is asserted.
            var sut = Sut();

            foreach (var condition in Conditions)
            {
                for (var position = 1; position <= 3; position++)
                {
                    var count = sut.Count(t => t.Condition == condition && t.VersionPosition == position);
                    Assert.That(count, Is.InRange(1, 2),
                        $"{condition} sits at position {position} {count} times; expected 1 or 2");
                }
            }
        }

        [Test]
        public void Build_IsDeterministic()
        {
            // Every participant gets this same list, and a missing log is
            // identified by the position it occupied — both rest on this.
            var first = Sut();
            var second = Sut();

            Assert.That(first.Select(t => t.ToString()), Is.EqualTo(second.Select(t => t.ToString())));
        }

        [Test]
        public void Build_NumbersTrialsInRunningOrder()
        {
            var sut = Sut();

            for (var i = 0; i < sut.Count; i++)
                Assert.That(sut[i].Index, Is.EqualTo(i));
        }

        [Test]
        public void IsLastOfBlock_MarksWhereTheRankingIsAsked()
        {
            var sut = Sut();

            Assert.That(sut.Count(t => t.IsLastOfBlock), Is.EqualTo(5), "one ranking per block");
            foreach (var trial in sut.Where(t => t.IsLastOfBlock))
                Assert.That(trial.VersionPosition, Is.EqualTo(3));
        }

        [Test]
        public void Build_WithNothingToRun_IsRefused()
        {
            Assert.That(() => StudySequence.Build(null, Conditions), Throws.ArgumentException);
            Assert.That(() => StudySequence.Build(Conversations, null), Throws.ArgumentException);
            Assert.That(() => StudySequence.Build(new string[0], Conditions), Throws.ArgumentException);
        }

        [Test]
        public void Build_WithMoreBlocksThanPermutations_KeepsCycling()
        {
            // Three methods give six permutations; a seventh block must reuse the
            // first rather than fall off the table.
            var many = Enumerable.Range(1, 8).Select(i => "c" + i).ToArray();

            var sut = StudySequence.Build(many, Conditions);

            Assert.That(sut.Count, Is.EqualTo(24));
            var first = sut.Where(t => t.BlockNumber == 1).OrderBy(t => t.VersionPosition).Select(t => t.Condition);
            var seventh = sut.Where(t => t.BlockNumber == 7).OrderBy(t => t.VersionPosition).Select(t => t.Condition);
            Assert.That(seventh, Is.EqualTo(first));
        }
    }
}
