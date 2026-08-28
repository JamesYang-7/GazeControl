using System;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class AgentSkinsTest
    {
        static AgentSkin[] Sut(params (int, string)[] agents) => AgentSkins.Assign(agents);

        [Test]
        public void Assign_MixedPair_PutsBothAgentsOnStock()
        {
            // The only case where neither agent needs a generated texture, and
            // so the only case where neither carries the SMPLitex face offset.
            Assert.That(Sut((1, "female"), (2, "male")),
                Is.EqualTo(new[] { AgentSkin.FemaleStock, AgentSkin.MaleStock }));

            Assert.That(Sut((1, "male"), (2, "female")),
                Is.EqualTo(new[] { AgentSkin.MaleStock, AgentSkin.FemaleStock }));
        }

        [Test]
        public void Assign_SameSexPair_GivesTheSecondSpeakerTheGeneratedTexture()
        {
            Assert.That(Sut((1, "female"), (2, "female")),
                Is.EqualTo(new[] { AgentSkin.FemaleStock, AgentSkin.FemaleGenerated }));

            Assert.That(Sut((1, "male"), (2, "male")),
                Is.EqualTo(new[] { AgentSkin.MaleStock, AgentSkin.MaleGenerated }));
        }

        [Test]
        public void Assign_DecidesStockBySpeakerCode_NotByTheOrderGiven()
        {
            // The segment file lists its agents in whatever order the exporter
            // wrote them; who takes the one stock albedo must not depend on it.
            Assert.That(Sut((2, "male"), (1, "male")),
                Is.EqualTo(new[] { AgentSkin.MaleGenerated, AgentSkin.MaleStock }));
        }

        [TestCase("unclear")]
        [TestCase("")]
        [TestCase(null)]
        public void Assign_UnmeasurableVoice_LeavesThatAgentAlone(string voice)
        {
            // "unclear" sits between the two pitch bands and is not a third sex:
            // guessing would dress an agent against the voice a participant hears.
            Assert.That(Sut((1, voice), (2, "female")),
                Is.EqualTo(new[] { AgentSkin.Unchanged, AgentSkin.FemaleStock }));
        }

        [Test]
        public void Assign_UnmeasurableVoice_DoesNotHoldTheStockAlbedoBackFromTheOther()
        {
            // Speaker 1 decides nothing here, so speaker 2 is the first female
            // and takes stock rather than falling to the generated texture.
            Assert.That(Sut((1, "unclear"), (2, "female"))[1], Is.EqualTo(AgentSkin.FemaleStock));
        }

        [Test]
        public void Assign_Null_Throws() =>
            Assert.That(() => AgentSkins.Assign(null), Throws.TypeOf<ArgumentNullException>());
    }
}
