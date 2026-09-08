using NUnit.Framework;

namespace GazeControl.ThreeParty
{
    [TestFixture]
    public class ThreePartyEventTest
    {
        static readonly string[] k_Sample =
        {
            "eot_type,first_speaker,second_speaker,turn_time,eot_start,eot_end",
            "3,1,2,2700,2200,2700",
            "1,2,3,3859,3359,4100",
            "2,3,1,5200,4700,5200",
        };

        [Test]
        public void Parse_SkipsTheHeaderRow()
        {
            Assert.That(ThreePartyEvent.Parse(k_Sample).Count, Is.EqualTo(3));
        }

        [Test]
        public void Parse_ConvertsMillisecondsToSeconds()
        {
            var first = ThreePartyEvent.Parse(k_Sample)[0];

            Assert.That(first.TurnSeconds, Is.EqualTo(2.7f).Within(1e-4f));
            Assert.That(first.StartSeconds, Is.EqualTo(2.2f).Within(1e-4f));
            Assert.That(first.EndSeconds, Is.EqualTo(2.7f).Within(1e-4f));
        }

        [Test]
        public void Parse_KeepsTheSpeakersInTheOrderTheFloorMovedIn()
        {
            var second = ThreePartyEvent.Parse(k_Sample)[1];

            Assert.That(second.FirstSpeaker, Is.EqualTo(2));
            Assert.That(second.SecondSpeaker, Is.EqualTo(3));
        }

        [Test]
        public void TypeName_NamesTheThreeAnnotatedKinds()
        {
            var events = ThreePartyEvent.Parse(k_Sample);

            Assert.That(events[0].TypeName, Is.EqualTo("turn-taking"));
            Assert.That(events[1].TypeName, Is.EqualTo("interruption"));
            Assert.That(events[2].TypeName, Is.EqualTo("overlapping"));
        }
    }
}
