using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class ShintaniGazePolicyTest
    {
        const float TickSeconds = 1f / 30f;

        // 20,000 s of simulated conversation. The policy is pure logic, so the
        // run costs milliseconds; the length is what makes the occupancy and
        // transition shares converge tightly enough to be worth asserting on.
        const int LongRunTicks = 600000;

        // The semi-Markov reconstruction does not return the corpus's occupancy
        // exactly: the fitted lognormal is a little short in the tail of "gaze at
        // the speaker while the floor is held", which is where the largest gap
        // sits. A simulation of the published parameters puts the worst cell at
        // 0.046, so anything beyond 0.06 is a real regression rather than noise.
        const float OccupancyTolerance = 0.06f;

        static readonly ParticipantId AgentA = new(0);
        static readonly ParticipantId AgentB = new(1);
        static readonly ParticipantId User = new(2);

        static ShintaniGazeParameters Parameters => ShintaniGazeParameters.LoadDefault();

        static ShintaniGazePolicy CreateSystemUnderTest(int seed = 1)
        {
            var sut = new ShintaniGazePolicy(Parameters, fallbackTarget: User);
            sut.Reset(seed);
            return sut;
        }

        /// A state in which agent A plays <paramref name="role"/>. The speaker and
        /// addressee are chosen so that A lands in the wanted role and the third
        /// participant is always the remaining one.
        static ConversationState StateFor(ParticipantRole role, TurnState turnState)
        {
            var speaker = role == ParticipantRole.Speaker ? AgentA : AgentB;
            var addressee = role == ParticipantRole.Addressee ? AgentA : role == ParticipantRole.Speaker ? AgentB : User;

            return new ConversationState
            {
                SelfId = AgentA,
                SelfRole = role,
                CurrentSpeaker = speaker,
                CurrentAddressee = addressee,
                FirstPartner = AgentB,
                SecondPartner = User,
                TimeSinceTurnInstant = turnState == TurnState.Changing ? 0.5f : 30f,
                PredictedTimeToTurnEnd = turnState == TurnState.Changing ? 0.5f : 30f,
            };
        }

        static GazeTargetRole TargetRoleOf(in GazeTarget target, in ConversationState state)
        {
            var isPerson = target.Type == GazeTargetType.Person;
            var isSpeaker = isPerson && target.Person == state.CurrentSpeaker;
            var isAddressee = isPerson && target.Person == state.CurrentAddressee;

            return !isPerson ? GazeTargetRole.Aversion
                : isSpeaker ? GazeTargetRole.Speaker
                : isAddressee ? GazeTargetRole.Addressee
                : GazeTargetRole.SideParticipant;
        }

        static GazeTarget[] Run(ShintaniGazePolicy sut, ConversationState state, int ticks)
        {
            var targets = new GazeTarget[ticks];
            for (var i = 0; i < ticks; i++)
                targets[i] = sut.Update(TickSeconds, in state);

            return targets;
        }

        static float[] Occupancy(ShintaniGazePolicy sut, ConversationState state, int ticks)
        {
            var counts = new int[4];
            for (var i = 0; i < ticks; i++)
                counts[(int)TargetRoleOf(sut.Update(TickSeconds, in state), in state)]++;

            var occupancy = new float[counts.Length];
            for (var i = 0; i < counts.Length; i++)
                occupancy[i] = (float)counts[i] / ticks;

            return occupancy;
        }

        /// Share of gaze shifts that leave a person for aversion rather than for
        /// the other person — the corpus's "star topology".
        static float AversionShareOfShiftsFromAPerson(ShintaniGazePolicy sut, ConversationState state, int ticks)
        {
            var toAversion = 0;
            var toPerson = 0;
            var previous = sut.Update(TickSeconds, in state);

            for (var i = 1; i < ticks; i++)
            {
                var current = sut.Update(TickSeconds, in state);
                var leftAPerson = previous.Type == GazeTargetType.Person && current != previous;
                toAversion += leftAPerson && current.Type == GazeTargetType.Aversion ? 1 : 0;
                toPerson += leftAPerson && current.Type == GazeTargetType.Person ? 1 : 0;
                previous = current;
            }

            return (float)toAversion / (toAversion + toPerson);
        }

        static int DistinctAversionOffsets(ShintaniGazePolicy sut, ConversationState state, int ticks)
        {
            var offsets = new HashSet<Vector2>();
            for (var i = 0; i < ticks; i++)
            {
                var target = sut.Update(TickSeconds, in state);
                offsets.Add(target.Type == GazeTargetType.Aversion ? target.AversionOffset : Vector2.positiveInfinity);
            }

            offsets.Remove(Vector2.positiveInfinity);
            return offsets.Count;
        }

        [Test]
        public void Update_BeforeAnyoneHasSpoken_HoldsTheFallbackTarget()
        {
            var sut = CreateSystemUnderTest();

            var actual = sut.Update(TickSeconds, new ConversationState { SelfId = AgentA, FirstPartner = AgentB, SecondPartner = User });

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        public void Update_WithTheSameSeed_ReplaysTheSameTargetSequence()
        {
            var state = StateFor(ParticipantRole.Speaker, TurnState.Holding);
            var expected = Run(CreateSystemUnderTest(seed: 42), state, ticks: 2000);

            var actual = Run(CreateSystemUnderTest(seed: 42), state, ticks: 2000);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Update_WithDifferentSeeds_DivergesSoTwoAgentsDoNotCorrelate()
        {
            var state = StateFor(ParticipantRole.Speaker, TurnState.Holding);
            var other = Run(CreateSystemUnderTest(seed: 42), state, ticks: 2000);

            var actual = Run(CreateSystemUnderTest(seed: 43), state, ticks: 2000);

            Assert.That(actual, Is.Not.EqualTo(other));
        }

        [Test]
        public void Reset_AfterALongRun_RestartsTheSameSequence()
        {
            var state = StateFor(ParticipantRole.Addressee, TurnState.Changing);
            var sut = CreateSystemUnderTest(seed: 5);
            var expected = Run(sut, state, ticks: 2000);
            Run(sut, state, ticks: 2000);

            sut.Reset(5);
            var actual = Run(sut, state, ticks: 2000);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Update_OverALongRun_NeverGazesAtItself()
        {
            var state = StateFor(ParticipantRole.Addressee, TurnState.Holding);
            var sut = CreateSystemUnderTest();

            var actual = Run(sut, state, ticks: 20000);

            Assert.That(actual, Has.None.Matches<GazeTarget>(
                target => target.Type == GazeTargetType.Person && target.Person == AgentA));
        }

        [TestCase(ParticipantRole.Speaker, TurnState.Changing, GazeTargetRole.Addressee, 0.3424f)]
        [TestCase(ParticipantRole.Speaker, TurnState.Holding, GazeTargetRole.Aversion, 0.4421f)]
        [TestCase(ParticipantRole.Addressee, TurnState.Changing, GazeTargetRole.Speaker, 0.4052f)]
        [TestCase(ParticipantRole.Addressee, TurnState.Holding, GazeTargetRole.Speaker, 0.5147f)]
        [TestCase(ParticipantRole.SideParticipant, TurnState.Changing, GazeTargetRole.Aversion, 0.4265f)]
        [TestCase(ParticipantRole.SideParticipant, TurnState.Holding, GazeTargetRole.Speaker, 0.4583f)]
        public void Update_OverALongRun_ReproducesTheCorpusGazeRatio(
            ParticipantRole role, TurnState turnState, GazeTargetRole target, float expected)
        {
            var state = StateFor(role, turnState);
            var sut = CreateSystemUnderTest();

            var actual = Occupancy(sut, state, LongRunTicks)[(int)target];

            Assert.That(actual, Is.EqualTo(expected).Within(OccupancyTolerance));
        }

        [Test]
        public void Update_OverALongRun_LeavesAPersonForAversionFarMoreOftenThanForTheOtherPerson()
        {
            var state = StateFor(ParticipantRole.Speaker, TurnState.Holding);
            var sut = CreateSystemUnderTest();

            var actual = AversionShareOfShiftsFromAPerson(sut, state, LongRunTicks);

            // The corpus jump chain puts this at 0.80-0.83; sampling targets
            // i.i.d. from the gaze ratios instead would give roughly 0.65, which
            // is the misimplementation this asserts against.
            Assert.That(actual, Is.GreaterThan(0.75f));
        }

        [Test]
        public void Update_WhileAverting_StaysWithinAPlausibleEyeRange()
        {
            var state = StateFor(ParticipantRole.Speaker, TurnState.Holding);
            var sut = CreateSystemUnderTest();

            var actual = Run(sut, state, ticks: 20000);

            Assert.That(actual, Has.All.Matches<GazeTarget>(target =>
                target.Type == GazeTargetType.Person ||
                (Mathf.Abs(target.AversionOffset.x) <= 35f && Mathf.Abs(target.AversionOffset.y) <= 25f)));
        }

        [Test]
        public void Update_WhileAverting_ReTargetsTheEyesAcrossAllNineDirections()
        {
            var state = StateFor(ParticipantRole.SideParticipant, TurnState.Holding);
            var sut = CreateSystemUnderTest();

            var actual = DistinctAversionOffsets(sut, state, ticks: 20000);

            Assert.That(actual, Is.EqualTo(9));
        }
    }
}
