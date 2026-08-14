using System;
using NUnit.Framework;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class HoldingSequenceBankTest
    {
        static string Stretch(string role, string targets, string durations) =>
            $@"{{""role"":""{role}"",""sessionId"":""s"",""gazerId"":""g"",""startFrame"":0,""targets"":[{targets}],""durations"":[{durations}]}}";

        static string Document(params string[] stretches) =>
            @"{""stretches"":[" + string.Join(",", stretches) + "]}";

        static string ValidSp() => Stretch("sp", @"""ad""", "1.0");

        static string ValidAd() => Stretch("ad", @"""sp""", "1.0");

        static string ValidSd() => Stretch("sd", @"""aversion""", "1.0");

        static HoldingSequenceBank CreateSystemUnderTest() => HoldingSequenceBank.LoadDefault();

        static int MinimumFixationCount(HoldingSequenceBank bank, ParticipantRole role)
        {
            var minimum = int.MaxValue;
            for (var i = 0; i < bank.StretchCount(role); i++)
                minimum = Math.Min(minimum, bank.Fixations(role, i).Length);

            return minimum;
        }

        static float MinimumDurationSeconds(HoldingSequenceBank bank, ParticipantRole role)
        {
            var minimum = float.MaxValue;
            for (var i = 0; i < bank.StretchCount(role); i++)
            {
                var fixations = bank.Fixations(role, i);
                for (var j = 0; j < fixations.Length; j++)
                    minimum = Math.Min(minimum, fixations[j].DurationSeconds);
            }

            return minimum;
        }

        /// GazeTargetRole's first three values line up with ParticipantRole by
        /// design, so a fixation targets the stretch's own role exactly when the
        /// two enums carry the same underlying value.
        static int FixationsTargetingOwnRole(HoldingSequenceBank bank, ParticipantRole role)
        {
            var count = 0;
            for (var i = 0; i < bank.StretchCount(role); i++)
            {
                var fixations = bank.Fixations(role, i);
                for (var j = 0; j < fixations.Length; j++)
                    count += (int)fixations[j].Target == (int)role ? 1 : 0;
            }

            return count;
        }

        [TestCase(ParticipantRole.Speaker)]
        [TestCase(ParticipantRole.Addressee)]
        [TestCase(ParticipantRole.SideParticipant)]
        [Category("Acceptance")]
        public void LoadDefault_FromResources_ProvidesStretchesForEveryRole(ParticipantRole role)
        {
            var sut = CreateSystemUnderTest();

            Assert.That(sut.StretchCount(role), Is.GreaterThanOrEqualTo(1));
        }

        [TestCase(ParticipantRole.Speaker)]
        [TestCase(ParticipantRole.Addressee)]
        [TestCase(ParticipantRole.SideParticipant)]
        [Category("Acceptance")]
        public void LoadDefault_FromResources_HoldsNoEmptyStretch(ParticipantRole role)
        {
            var sut = CreateSystemUnderTest();

            Assert.That(MinimumFixationCount(sut, role), Is.GreaterThanOrEqualTo(1));
        }

        [TestCase(ParticipantRole.Speaker)]
        [TestCase(ParticipantRole.Addressee)]
        [TestCase(ParticipantRole.SideParticipant)]
        [Category("Acceptance")]
        public void LoadDefault_FromResources_HoldsOnlyPositiveDurations(ParticipantRole role)
        {
            var sut = CreateSystemUnderTest();

            Assert.That(MinimumDurationSeconds(sut, role), Is.GreaterThan(0f));
        }

        [TestCase(ParticipantRole.Speaker)]
        [TestCase(ParticipantRole.Addressee)]
        [TestCase(ParticipantRole.SideParticipant)]
        [Category("Acceptance")]
        public void LoadDefault_FromResources_TargetsNoStretchsOwnRole(ParticipantRole role)
        {
            var sut = CreateSystemUnderTest();

            Assert.That(FixationsTargetingOwnRole(sut, role), Is.EqualTo(0));
        }

        static TestCaseData[] s_malformedDocuments =
        {
            new TestCaseData("{}").SetName("no stretches key"),
            new TestCaseData(@"{""stretches"":[]}").SetName("empty stretch list"),
            new TestCaseData(Document(ValidSp(), ValidAd())).SetName("role with no stretches"),
            new TestCaseData(Document(Stretch("sp", @"""ad"",""sd""", "1.0"), ValidAd(), ValidSd()))
                .SetName("targets and durations of different lengths"),
            new TestCaseData(Document(Stretch("sp", "", ""), ValidAd(), ValidSd()))
                .SetName("stretch with no fixations"),
            new TestCaseData(Document(Stretch("sp", @"""ad""", "0.0"), ValidAd(), ValidSd()))
                .SetName("zero duration"),
            new TestCaseData(Document(Stretch("sp", @"""ad""", "-1.0"), ValidAd(), ValidSd()))
                .SetName("negative duration"),
            new TestCaseData(Document(Stretch("xx", @"""ad""", "1.0"), ValidSp(), ValidAd(), ValidSd()))
                .SetName("unknown role key"),
            new TestCaseData(Document(Stretch("sp", @"""xx""", "1.0"), ValidAd(), ValidSd()))
                .SetName("unknown target key"),
            new TestCaseData(Document(Stretch("sp", @"""sp""", "1.0"), ValidAd(), ValidSd()))
                .SetName("fixation targeting the stretch's own role"),
        };

        [TestCaseSource(nameof(s_malformedDocuments))]
        [Category("Acceptance")]
        public void Parse_WithMalformedContent_ThrowsArgumentException(string json)
        {
            Assert.That(() => HoldingSequenceBank.Parse(json), Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Parse_WithAWellFormedDocument_AcceptsEveryRole()
        {
            var sut = HoldingSequenceBank.Parse(Document(ValidSp(), ValidSp(), ValidAd(), ValidSd()));

            Assert.That(sut.StretchCount(ParticipantRole.Speaker), Is.EqualTo(2), "speaker stretches");
            Assert.That(sut.StretchCount(ParticipantRole.Addressee), Is.EqualTo(1), "addressee stretches");
            Assert.That(sut.StretchCount(ParticipantRole.SideParticipant), Is.EqualTo(1), "side-participant stretches");
        }

        [Test]
        [Category("Acceptance")]
        public void Fixations_ForAStretchInTheDocument_ReturnsItsFixationsInOrder()
        {
            var sut = HoldingSequenceBank.Parse(Document(
                Stretch("sp", @"""ad"",""aversion"",""sd""", "1.5,0.5,2.0"), ValidAd(), ValidSd()));

            var actual = sut.Fixations(ParticipantRole.Speaker, 0).ToArray();

            Assert.That(actual, Is.EqualTo(new[]
            {
                new HoldingFixation(GazeTargetRole.Addressee, 1.5f),
                new HoldingFixation(GazeTargetRole.Aversion, 0.5f),
                new HoldingFixation(GazeTargetRole.SideParticipant, 2.0f),
            }));
        }
    }
}
