using System;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class RankingOrderTest
    {
        [Test]
        public void RanksByVersion_InvertsTheOrder()
        {
            // The participant said "version 2, then 3, then 1".
            var ranks = RankingOrder.RanksByVersion(new[] { 2, 3, 1 });

            // Version 1 came last, version 2 first, version 3 second.
            Assert.That(ranks, Is.EqualTo(new[] { 3, 1, 2 }));
        }

        [Test]
        public void RanksByVersion_IdentityOrder_IsItsOwnInverse()
        {
            Assert.That(RankingOrder.RanksByVersion(new[] { 1, 2, 3 }), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void RanksByVersion_WithAVersionListedTwice_Throws()
        {
            Assert.That(() => RankingOrder.RanksByVersion(new[] { 2, 2, 1 }), Throws.ArgumentException);
        }

        [Test]
        public void RanksByVersion_WithAVersionOutOfRange_Throws()
        {
            Assert.That(() => RankingOrder.RanksByVersion(new[] { 1, 4, 2 }), Throws.ArgumentException);
        }

        /// <summary>
        /// Both surfaces name ranks — the operator enters "1st" while the
        /// participant reads "1st" — so the spelling lives in one place.
        /// </summary>
        [Test]
        public void Ordinal_NamesTheRanksAStudyUses()
        {
            Assert.That(RankingOrder.Ordinal(1), Is.EqualTo("1st"));
            Assert.That(RankingOrder.Ordinal(2), Is.EqualTo("2nd"));
            Assert.That(RankingOrder.Ordinal(3), Is.EqualTo("3rd"));
        }

        [Test]
        public void Ordinal_BeyondThree_FallsBackToTh() =>
            Assert.That(RankingOrder.Ordinal(4), Is.EqualTo("4th"));
    }
}
