using NUnit.Framework;

namespace GazeControl.ThreeParty
{
    [TestFixture]
    public class ThreePartyCaptionTrackTest
    {
        static readonly string[] k_Sample =
        {
            "start,end,Sentence,SID",
            "00:00:02.200,00:00:02.700,hi,12-15-2021_Session_1_PC_1_1",
            "00:00:06.500,00:00:08.900,I'm Crystal I'm also a computer science major,12-15-2021_Session_1_PC_1_2",
            "00:01:05.000,00:01:06.000,[Music],12-15-2021_Session_1_PC_1_3",
        };

        [Test]
        public void ParseClock_ReadsHoursMinutesAndFractionalSeconds()
        {
            Assert.That(ThreePartyCaptionTrack.ParseClock("01:02:03.500"), Is.EqualTo(3723.5f).Within(1e-4f));
        }

        [Test]
        public void ParseClock_RejectsSomethingThatIsNotAClock()
        {
            Assert.That(float.IsNaN(ThreePartyCaptionTrack.ParseClock("hello")), Is.True);
        }

        [Test]
        public void Parse_SkipsTheHeaderRow()
        {
            Assert.That(ThreePartyCaptionTrack.Parse(k_Sample).Utterances.Count, Is.EqualTo(3));
        }

        [Test]
        public void Parse_ReadsTheTimesAndTheSentence()
        {
            var utterance = ThreePartyCaptionTrack.Parse(k_Sample).Utterances[0];

            Assert.That(utterance.StartSeconds, Is.EqualTo(2.2f).Within(1e-4f));
            Assert.That(utterance.EndSeconds, Is.EqualTo(2.7f).Within(1e-4f));
            Assert.That(utterance.Text, Is.EqualTo("hi"));
        }

        [Test]
        public void Parse_KeepsACommaInsideTheSentence()
        {
            // The sentence field is free recogniser text; only the two clocks and
            // the trailing SID are guaranteed comma-free, which is why the split
            // is on the first two commas and the last one.
            var lines = new[]
            {
                "start,end,Sentence,SID",
                "00:00:01.000,00:00:02.000,well, no,x_1",
            };

            Assert.That(ThreePartyCaptionTrack.Parse(lines).Utterances[0].Text, Is.EqualTo("well, no"));
        }

        [Test]
        public void Utterance_KnowsARecogniserTagFromSpeech()
        {
            var utterances = ThreePartyCaptionTrack.Parse(k_Sample).Utterances;

            Assert.That(utterances[2].IsTag, Is.True);
            Assert.That(utterances[0].IsTag, Is.False);
        }

        [Test]
        public void TextAt_ReturnsTheUtteranceCoveringTheInstant()
        {
            Assert.That(ThreePartyCaptionTrack.Parse(k_Sample).TextAt(7f),
                Is.EqualTo("I'm Crystal I'm also a computer science major"));
        }

        [Test]
        public void TextAt_ReturnsNullWhileTheParticipantIsSilent()
        {
            Assert.That(ThreePartyCaptionTrack.Parse(k_Sample).TextAt(4f), Is.Null);
        }
    }
}
