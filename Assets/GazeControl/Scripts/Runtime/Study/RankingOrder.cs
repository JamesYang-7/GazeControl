using System;

namespace GazeControl.Study
{
    /// <summary>
    /// Turns a ranking entered as an order of versions into the rank of each
    /// version, which is what <see cref="QuestionnaireResponseWriter.AppendRank"/>
    /// records.
    ///
    /// <para>The operator types what the participant says, and a participant
    /// says "two, then one, then three" — versions in order of preference — not
    /// "version one is second". Entering versions by rank therefore matches the
    /// spoken answer, while <c>responses.csv</c> keeps one row per version with
    /// its rank, so nothing downstream changes. The two are inverse permutations
    /// of each other; this is that inversion, kept pure so it can be tested.</para>
    /// </summary>
    public static class RankingOrder
    {
        /// <summary>
        /// The rank (1 = best) of each 1-based version position, from the version
        /// positions listed best first.
        /// </summary>
        /// <param name="versionsByRank">Element <c>r-1</c> is the version placed at rank <c>r</c>.</param>
        /// <returns>Element <c>v-1</c> is the rank given to version <c>v</c>.</returns>
        /// <exception cref="ArgumentException">The order is not a permutation of 1..n.</exception>
        public static int[] RanksByVersion(int[] versionsByRank)
        {
            if (versionsByRank == null)
                throw new ArgumentNullException(nameof(versionsByRank));

            var count = versionsByRank.Length;
            var ranks = new int[count];

            for (var rank = 1; rank <= count; rank++)
            {
                var version = versionsByRank[rank - 1];
                if (version < 1 || version > count)
                    throw new ArgumentException($"version {version} is outside 1-{count}", nameof(versionsByRank));

                if (ranks[version - 1] != 0)
                    throw new ArgumentException($"version {version} is listed twice", nameof(versionsByRank));

                ranks[version - 1] = rank;
            }

            return ranks;
        }

        /// <summary>
        /// A rank as the operator's panel lists it: 1st, 2nd, 3rd, each taking a
        /// version number.
        ///
        /// <para>Here rather than on the panel because this is where the
        /// ranking's other rules live and where they can be tested without an
        /// operator. The participant's own panel shows the order as a chain
        /// ("V2 &gt; V1 &gt; _") and names no ranks, so this has one caller
        /// today.</para>
        /// </summary>
        public static string Ordinal(int rank) => rank switch
        {
            1 => "1st",
            2 => "2nd",
            3 => "3rd",
            _ => $"{rank}th",
        };
    }
}
