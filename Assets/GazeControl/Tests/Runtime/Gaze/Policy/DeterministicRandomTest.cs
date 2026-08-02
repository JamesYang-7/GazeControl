using System;
using NUnit.Framework;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class DeterministicRandomTest
    {
        const int Draws = 200000;

        static DeterministicRandom CreateSystemUnderTest(int seed = 1) => new(seed);

        static float[] NextFloats(DeterministicRandom random, int count)
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
                values[i] = random.NextFloat();

            return values;
        }

        static (float Mean, float Variance) GaussianMoments(DeterministicRandom random, int count)
        {
            var sum = 0.0;
            var sumOfSquares = 0.0;

            for (var i = 0; i < count; i++)
            {
                var value = random.NextGaussian();
                sum += value;
                sumOfSquares += value * value;
            }

            var mean = sum / count;
            return ((float)mean, (float)(sumOfSquares / count - mean * mean));
        }

        static float ShareOfIndex(DeterministicRandom random, float[] weights, int index, int count)
        {
            var counts = new int[weights.Length];
            for (var i = 0; i < count; i++)
                counts[random.NextCategorical(weights)]++;

            return (float)counts[index] / count;
        }

        [Test]
        public void NextFloat_SameSeed_ReplaysTheSameSequence()
        {
            var expected = NextFloats(CreateSystemUnderTest(seed: 7), count: 64);

            var actual = NextFloats(CreateSystemUnderTest(seed: 7), count: 64);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void NextFloat_DifferentSeeds_ProduceDifferentSequences()
        {
            var other = NextFloats(CreateSystemUnderTest(seed: 7), count: 64);

            var actual = NextFloats(CreateSystemUnderTest(seed: 8), count: 64);

            Assert.That(actual, Is.Not.EqualTo(other));
        }

        [Test]
        public void NextFloat_OverManyDraws_StaysInTheUnitInterval()
        {
            var sut = CreateSystemUnderTest();

            var actual = NextFloats(sut, Draws);

            Assert.That(actual, Has.All.Matches<float>(value => value >= 0f && value < 1f));
        }

        [Test]
        public void NextGaussian_OverManyDraws_HasZeroMean()
        {
            var sut = CreateSystemUnderTest();

            var actual = GaussianMoments(sut, Draws).Mean;

            Assert.That(actual, Is.EqualTo(0f).Within(0.02f));
        }

        [Test]
        public void NextGaussian_OverManyDraws_HasUnitVariance()
        {
            var sut = CreateSystemUnderTest();

            var actual = GaussianMoments(sut, Draws).Variance;

            Assert.That(actual, Is.EqualTo(1f).Within(0.02f));
        }

        [Test]
        public void NextCategorical_OverManyDraws_MatchesTheWeights()
        {
            var sut = CreateSystemUnderTest();

            var actual = ShareOfIndex(sut, new[] { 0.2f, 0.5f, 0.3f }, index: 1, Draws);

            Assert.That(actual, Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void NextCategorical_WithNoPositiveWeight_ThrowsArgumentException()
        {
            var sut = CreateSystemUnderTest();

            Assert.That(() => sut.NextCategorical(new[] { 0f, 0f, 0f }), Throws.TypeOf<ArgumentException>());
        }
    }
}
