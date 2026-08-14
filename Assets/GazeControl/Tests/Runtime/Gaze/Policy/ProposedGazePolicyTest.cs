using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class ProposedGazePolicyTest
    {
        const float TickSeconds = 1f / 30f;
        const int PatternSeed = 1;

        // Fig. 7e starts 29 frames into the 1 s window, so the prototype's first
        // segment begins with this much of the turn left to run.
        const float SecondsToEndAtPatternStart = 1f - 29f / 60f;

        static readonly ParticipantId AgentA = new(0);
        static readonly ParticipantId AgentB = new(1);
        static readonly ParticipantId User = new(2);

        /// Most tests pin the prototype so they assert about one known pattern;
        /// the selection tests use the random mode explicitly.
        static ProposedSettings Settings() => new()
        {
            Selection = PatternSelection.Fixed,
            Pattern = PreTurnPattern.Fig7e,
            PreTurnWindowSeconds = 1f,
            PatternTimeScale = 1f,
        };

        // The substrate is baseline B, whose voice tracker the runner ticks in
        // production. Nobody is voiced in these states, so it holds its target and
        // the tests isolate what the pattern does.
        static SpeakerFollowingGazePolicy CreateSubstrate() =>
            new(new SpeakerFollowingSettings(), new VoiceActivityTracker(participantCount: 3, 0.25f, 0.5f), User);

        static ProposedGazePolicy CreateSystemUnderTest(int seed = 1, ProposedSettings settings = null)
        {
            var sut = new ProposedGazePolicy(CreateSubstrate(), settings ?? Settings(), PatternSeed);
            sut.Reset(seed);
            return sut;
        }

        // Bank for the holding-replay substrate tests. The sp pool's lone
        // stretch averts mid-way — which baseline B never does — so an aversion
        // in a run witnesses that the holding material is what plays.
        static HoldingSequenceBank CreateHoldingBank() => HoldingSequenceBank.Parse(
            @"{""stretches"":[" +
            @"{""role"":""sp"",""sessionId"":""s"",""gazerId"":""g"",""startFrame"":0,""targets"":[""ad"",""aversion"",""sd""],""durations"":[0.5,0.5,0.5]}," +
            @"{""role"":""ad"",""sessionId"":""s"",""gazerId"":""g"",""startFrame"":0,""targets"":[""aversion""],""durations"":[5.0]}," +
            @"{""role"":""sd"",""sessionId"":""s"",""gazerId"":""g"",""startFrame"":0,""targets"":[""sp""],""durations"":[5.0]}]}");

        static HoldingReplayGazePolicy CreateHoldingSubstrate() => new(CreateHoldingBank(), User);

        static ProposedGazePolicy CreateSystemUnderTestOnHoldingReplay(int seed)
        {
            var sut = new ProposedGazePolicy(CreateHoldingSubstrate(), Settings(), PatternSeed);
            sut.Reset(seed);
            return sut;
        }

        /// A speaks and addresses B, so A plays the prototype's current-speaker
        /// track, B its next-speaker track, and the user has no track at all.
        static ConversationState StateFor(
            ParticipantId self, ParticipantRole role, float secondsToTurnEnd,
            int eventIndex = 0, EotType eventType = EotType.TurnTaking) => new()
        {
            SelfId = self,
            SelfRole = role,
            CurrentSpeaker = AgentA,
            CurrentAddressee = AgentB,
            FirstPartner = self == AgentA ? AgentB : AgentA,
            SecondPartner = self == User ? AgentB : User,
            TimeSinceTurnInstant = 30f,
            PredictedTimeToTurnEnd = secondsToTurnEnd,
            UpcomingEventIndex = eventIndex,
            UpcomingEventType = eventType,
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

        static GazeTarget[] RunSubstrate(IGazePolicy sut, float secondsToTurnEnd, int ticks)
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
        public void Update_AtATurnNoAnnotatedEventEnds_LeavesTheSubstrateInCharge()
        {
            var sut = CreateSystemUnderTest();

            // The segment's final turn runs to the end of the clip; nothing is
            // known about the boundary past it, so no prototype may fire.
            var state = StateFor(AgentA, ParticipantRole.Speaker, 0.1f, eventIndex: -1);
            sut.Update(TickSeconds, state);

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
            // is drawn from the run's pattern seed rather than the agent's — so
            // the per-agent seed changes nothing. One recording of this condition
            // is the condition, not one draw from it.
            Assert.That(actual, Is.EqualTo(other));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_OutsideTheWindowWithAHoldingReplaySubstrate_DelegatesToTheSubstrate()
        {
            var reference = CreateHoldingSubstrate();
            reference.Reset(3);
            var expected = RunSubstrate(reference, secondsToTurnEnd: -1f, ticks: 2000);

            var actual = RunProposed(CreateSystemUnderTestOnHoldingReplay(seed: 3), secondsToTurnEnd: -1f, ticks: 2000);

            Assert.That(expected, Does.Contain(GazeTarget.Away(Vector2.zero)),
                "the holding material plays — baseline B never averts");
            Assert.That(actual, Is.EqualTo(expected), "every out-of-window tick comes from the substrate");
        }

        [Test]
        public void SelectPattern_ForAnEvent_DrawsFromItsOwnClassPool()
        {
            var settings = new ProposedSettings { Selection = PatternSelection.RandomByEventClass };

            foreach (EotType eotType in System.Enum.GetValues(typeof(EotType)))
            {
                var pool = GazePatterns.PoolFor(eotType);
                for (var eventIndex = 0; eventIndex < 50; eventIndex++)
                {
                    var pattern = ProposedGazePolicy.SelectPattern(settings, PatternSeed, eventIndex, eotType);

                    Assert.That(pool, Contains.Item(pattern),
                        $"{eotType} event {eventIndex} drew {pattern.Name}, which is not in that class's pool");
                    Assert.That(pattern.EotType, Is.EqualTo(eotType));
                }
            }
        }

        [Test]
        public void SelectPattern_OverManyEvents_ReachesEveryPrototypeInThePool()
        {
            var settings = new ProposedSettings { Selection = PatternSelection.RandomByEventClass };
            var drawn = new HashSet<string>();

            for (var eventIndex = 0; eventIndex < 200; eventIndex++)
                drawn.Add(ProposedGazePolicy.SelectPattern(settings, PatternSeed, eventIndex, EotType.TurnTaking).PrototypeId);

            // A hash that collapsed onto one prototype would still be
            // deterministic and still pass every other test here.
            Assert.That(drawn.Count, Is.EqualTo(GazePatterns.PoolFor(EotType.TurnTaking).Count));
        }

        [Test]
        public void SelectPattern_ForTheSameEvent_IsIndependentOfWhichAgentAsks()
        {
            var settings = new ProposedSettings { Selection = PatternSelection.RandomByEventClass };

            // Both agents must play tracks of the *same* prototype, so selection
            // may not depend on anything per-agent. It takes only the run's seed.
            var first = ProposedGazePolicy.SelectPattern(settings, PatternSeed, eventIndex: 3, EotType.Interruption);
            var second = ProposedGazePolicy.SelectPattern(settings, PatternSeed, eventIndex: 3, EotType.Interruption);

            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void SelectPattern_WithFixedSelection_IgnoresTheEventClass()
        {
            var settings = new ProposedSettings { Selection = PatternSelection.Fixed, Pattern = PreTurnPattern.Fig8d };

            var actual = ProposedGazePolicy.SelectPattern(settings, PatternSeed, eventIndex: 7, EotType.Overlapping);

            Assert.That(actual.PrototypeId, Is.EqualTo("p3_ks40_4"));
        }
    }
}
