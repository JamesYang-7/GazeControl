using System;
using NUnit.Framework;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class ShintaniGazeParametersTest
    {
        static ShintaniGazeParameters CreateSystemUnderTest() => ShintaniGazeParameters.LoadDefault();

        static float SelfTransitionMass(ShintaniGazeParameters parameters, ParticipantRole role)
        {
            var mass = 0f;
            for (var state = 0; state < 4; state++)
                mass += parameters.JumpProbabilities(role, (GazeTargetRole)state)[state];

            return mass;
        }

        static float RowSum(ReadOnlySpan<float> row)
        {
            var sum = 0f;
            for (var i = 0; i < row.Length; i++)
                sum += row[i];

            return sum;
        }

        static float LargestRetargetSample(ShintaniGazeParameters parameters, int steps)
        {
            var largest = 0f;
            for (var i = 0; i < steps; i++)
                largest = Math.Max(largest, parameters.SampleRetargetSeconds((float)i / (steps - 1)));

            return largest;
        }

        [Test]
        public void LoadDefault_FromResources_ReadsTheCorpusTurnWindow()
        {
            var sut = CreateSystemUnderTest();

            Assert.That(sut.TurnWindowSeconds, Is.EqualTo(1f).Within(0.001f));
        }

        [TestCase(ParticipantRole.Speaker)]
        [TestCase(ParticipantRole.Addressee)]
        [TestCase(ParticipantRole.SideParticipant)]
        public void JumpProbabilities_ForAnyRole_ContainNoSelfTransition(ParticipantRole role)
        {
            var sut = CreateSystemUnderTest();

            var actual = SelfTransitionMass(sut, role);

            Assert.That(actual, Is.EqualTo(0f));
        }

        [TestCase(ParticipantRole.Speaker, GazeTargetRole.Aversion)]
        [TestCase(ParticipantRole.Addressee, GazeTargetRole.Speaker)]
        [TestCase(ParticipantRole.SideParticipant, GazeTargetRole.Addressee)]
        public void JumpProbabilities_ForAReachableState_SumToOne(ParticipantRole role, GazeTargetRole from)
        {
            var sut = CreateSystemUnderTest();

            var actual = RowSum(sut.JumpProbabilities(role, from));

            Assert.That(actual, Is.EqualTo(1f).Within(0.001f));
        }

        [TestCase(ParticipantRole.Speaker)]
        [TestCase(ParticipantRole.Addressee)]
        [TestCase(ParticipantRole.SideParticipant)]
        public void AversionDirectionWeights_ForAnyRole_SumToOne(ParticipantRole role)
        {
            var sut = CreateSystemUnderTest();

            var actual = RowSum(sut.AversionDirectionWeights(role));

            Assert.That(actual, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void AversionAngles_ForDownLeft_PointLeftAndDown()
        {
            var sut = CreateSystemUnderTest();

            var actual = sut.AversionAngles(AversionDirection.DownLeft);

            Assert.That(actual.x, Is.LessThan(-10f), "yaw is to the left");
            Assert.That(actual.y, Is.LessThan(-6f), "pitch is downward");
        }

        [Test]
        public void SampleRetargetSeconds_AcrossTheUnitInterval_StaysUnderTheCorpusTail()
        {
            var sut = CreateSystemUnderTest();

            var actual = LargestRetargetSample(sut, steps: 1000);

            // The table is cut at the 99th percentile of measured direction
            // segments, so no draw may exceed it.
            Assert.That(actual, Is.LessThanOrEqualTo(2.4f));
        }

        [Test]
        public void Parse_WithAnEmptyDocument_ThrowsArgumentException()
        {
            Assert.That(() => ShintaniGazeParameters.Parse("{}"), Throws.TypeOf<ArgumentException>());
        }
    }
}
