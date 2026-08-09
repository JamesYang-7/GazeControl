using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class ProposedGazePolicyTest
    {
        const float TickSeconds = 1f / 30f;

        // Fig. 7e starts 29 frames into the 1 s window, so the prototype's first
        // segment begins with this much of the turn left to run.
        const float SecondsToEndAtPatternStart = 1f - 29f / 60f;

        static readonly ParticipantId AgentA = new(0);
        static readonly ParticipantId AgentB = new(1);
        static readonly ParticipantId User = new(2);

        static ProposedSettings Settings() => new()
        {
            Pattern = PreTurnPattern.CheckAvertReengage7e,
            PreTurnWindowSeconds = 1f,
            PatternTimeScale = 1f,
        };

        // The substrate is baseline B, whose voice tracker the runner ticks in
        // production. Nobody is voiced in these states, so it holds its target and
        // the tests isolate what the pattern does.
        static SpeakerFollowingGazePolicy CreateSubstrate() =>
            new(new SpeakerFollowingSettings(), new VoiceActivityTracker(participantCount: 3, 0.25f, 0.5f), User);

        static ProposedGazePolicy CreateSystemUnderTest(int seed = 1)
        {
            var sut = new ProposedGazePolicy(CreateSubstrate(), Settings());
            sut.Reset(seed);
            return sut;
        }

        /// A speaks and addresses B, so A plays the prototype's current-speaker
        /// track, B its next-speaker track, and the user has no track at all.
        static ConversationState StateFor(ParticipantId self, ParticipantRole role, float secondsToTurnEnd) => new()
        {
            SelfId = self,
            SelfRole = role,
            CurrentSpeaker = AgentA,
            CurrentAddressee = AgentB,
            FirstPartner = self == AgentA ? AgentB : AgentA,
            SecondPartner = self == User ? AgentB : User,
            TimeSinceTurnInstant = 30f,
            PredictedTimeToTurnEnd = secondsToTurnEnd,
        };

        static GazeTarget TargetAt(ProposedGazePolicy sut, ParticipantId self, ParticipantRole role, float secondsToTurnEnd) =>
            sut.Update(TickSeconds, StateFor(self, role, secondsToTurnEnd));

        static GazeTarget[] RunProposed(ProposedGazePolicy sut, float secondsToTurnEnd, int ticks)
        {
            var targets = new GazeTarget[ticks];
            for (var i = 0; i < ticks; i++)
                targets[i] = TargetAt(sut, AgentA, ParticipantRole.Speaker, secondsToTurnEnd);

            return targets;
        }

        static GazeTarget[] RunSubstrate(SpeakerFollowingGazePolicy sut, float secondsToTurnEnd, int ticks)
        {
            var targets = new GazeTarget[ticks];
            for (var i = 0; i < ticks; i++)
                targets[i] = sut.Update(TickSeconds, StateFor(AgentA, ParticipantRole.Speaker, secondsToTurnEnd));

            return targets;
        }

        [Test]
        public void Update_WithNoTurnRunning_DelegatesToTheBaselineSubstrate()
        {
            var expected = RunSubstrate(CreateSubstrate(), secondsToTurnEnd: -1f, ticks: 2000);

            var actual = RunProposed(CreateSystemUnderTest(seed: 0), secondsToTurnEnd: -1f, ticks: 2000);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Update_BeforeTheWindowOpens_DelegatesToTheBaselineSubstrate()
        {
            var sut = CreateSystemUnderTest();

            TargetAt(sut, AgentA, ParticipantRole.Speaker, SecondsToEndAtPatternStart + 0.1f);

            Assert.That(sut.IsPatternActive, Is.False);
        }

        [Test]
        public void Update_AtThePrototypesFirstSegment_LooksAtTheNextSpeaker()
        {
            var sut = CreateSystemUnderTest();

            var actual = TargetAt(sut, AgentA, ParticipantRole.Speaker, SecondsToEndAtPatternStart);

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_AtThePrototypesAversionSegment_RecentresTheEyesInTheHead()
        {
            var sut = CreateSystemUnderTest();

            // 0.40 s to go is 0.117 s into the track — inside 7e's 17-frame avert.
            var actual = TargetAt(sut, AgentA, ParticipantRole.Speaker, 0.40f);

            // A zero offset is "eyes along the head's forward direction". This
            // condition deliberately does NOT use Baseline A's sampled directions
            // inside the window; see ProposedGazePolicy's summary.
            Assert.That(actual.Type, Is.EqualTo(GazeTargetType.Aversion), "aversion target");
            Assert.That(actual.AversionOffset, Is.EqualTo(Vector2.zero), "eyes centred in the head");
        }

        [Test]
        public void Update_AtThePrototypesFinalSegment_ReEngagesTheNextSpeaker()
        {
            var sut = CreateSystemUnderTest();

            // 0.10 s to go is 0.417 s into the track — inside 7e's 8-frame re-engage.
            var actual = TargetAt(sut, AgentA, ParticipantRole.Speaker, 0.10f);

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_ForTheNextSpeaker_PlaysTheNextSpeakerTrack()
        {
            var sut = CreateSystemUnderTest();

            var actual = TargetAt(sut, AgentB, ParticipantRole.Addressee, SecondsToEndAtPatternStart);

            // 7e's next speaker watches the current speaker for the first 20 frames.
            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentA)));
        }

        [Test]
        public void Update_ForAParticipantWithNoTrack_LeavesTheSubstrateInCharge()
        {
            var sut = CreateSystemUnderTest();

            TargetAt(sut, User, ParticipantRole.SideParticipant, SecondsToEndAtPatternStart);

            Assert.That(sut.IsPatternActive, Is.False);
        }

        [Test]
        public void Update_WithTheSameSeed_ReplaysTheSameSequence()
        {
            var expected = RunProposed(CreateSystemUnderTest(seed: 9), secondsToTurnEnd: -1f, ticks: 2000);

            var actual = RunProposed(CreateSystemUnderTest(seed: 9), secondsToTurnEnd: -1f, ticks: 2000);

            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Update_WithDifferentSeeds_StillReplaysTheSameSequence()
        {
            var other = RunProposed(CreateSystemUnderTest(seed: 9), secondsToTurnEnd: -1f, ticks: 2000);

            var actual = RunProposed(CreateSystemUnderTest(seed: 10), secondsToTurnEnd: -1f, ticks: 2000);

            // The substrate is baseline B, which is deterministic, and the pattern
            // draws no randomness — so this condition has no per-agent random
            // stream at all. The seed stays in the signature only so that every
            // condition is seeded and logged the same way (§0.5). One recording of
            // this condition is the condition, not one draw from it.
            Assert.That(actual, Is.EqualTo(other));
        }
    }
}
