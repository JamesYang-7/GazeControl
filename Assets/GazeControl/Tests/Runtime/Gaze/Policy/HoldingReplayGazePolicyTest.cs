using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class HoldingReplayGazePolicyTest
    {
        const float TickSeconds = 1f / 30f;

        // 20,000 s of simulated conversation, matching ShintaniGazePolicyTest:
        // the policy is pure logic, so the run costs milliseconds, and the length
        // is what makes the occupancy shares converge tightly enough to assert on.
        const int LongRunTicks = 600000;

        // Same bar as ShintaniGazePolicyTest. The bank is a 250-per-role sample
        // of the corpus stretches, so the replayed occupancy carries sampling
        // error on top of the run's own noise.
        const float OccupancyTolerance = 0.06f;

        static readonly ParticipantId AgentA = new(0);
        static readonly ParticipantId AgentB = new(1);
        static readonly ParticipantId User = new(2);

        static string Stretch(string role, string targets, string durations) =>
            $@"{{""role"":""{role}"",""sessionId"":""s"",""gazerId"":""g"",""startFrame"":0,""targets"":[{targets}],""durations"":[{durations}]}}";

        static HoldingSequenceBank Parse(params string[] stretches) =>
            HoldingSequenceBank.Parse(@"{""stretches"":[" + string.Join(",", stretches) + "]}");

        // The replay cursor is private, so the banks below give each role's pool
        // a target set no other role's pool uses: the role of the stretch behind
        // an emitted target is identifiable from the target alone.

        /// One sp stretch cycling addressee, aversion, side participant; 0.5 s
        /// (15 ticks) per fixation.
        static HoldingSequenceBank SingleStretchBank() => Parse(
            Stretch("sp", @"""ad"",""aversion"",""sd""", "0.5,0.5,0.5"),
            Stretch("ad", @"""aversion""", "5.0"),
            Stretch("sd", @"""sp""", "5.0"));

        /// Every pool holds one fixation far longer than any test run.
        static HoldingSequenceBank LongFixationBank() => Parse(
            Stretch("sp", @"""ad""", "5.0"),
            Stretch("ad", @"""aversion""", "5.0"),
            Stretch("sd", @"""sp""", "5.0"));

        static HoldingSequenceBank AversionOpeningBank() => Parse(
            Stretch("sp", @"""aversion""", "5.0"),
            Stretch("ad", @"""sp""", "5.0"),
            Stretch("sd", @"""sp""", "5.0"));

        /// The sp stretch's first fixation lasts 15 ticks and its second is never
        /// reached in the role-change tests; only the ad pool averts.
        static HoldingSequenceBank RoleChangeBank() => Parse(
            Stretch("sp", @"""sd"",""sd""", "0.5,5.0"),
            Stretch("ad", @"""aversion""", "5.0"),
            Stretch("sd", @"""sp""", "5.0"));

        /// An addressee pool varied enough that two draw streams diverge fast.
        static HoldingSequenceBank VariedBank() => Parse(
            Stretch("sp", @"""ad""", "1.0"),
            Stretch("ad", @"""sp""", "0.5"),
            Stretch("ad", @"""sd""", "1.0"),
            Stretch("ad", @"""aversion""", "0.7"),
            Stretch("ad", @"""sp"",""aversion""", "1.5,0.5"),
            Stretch("ad", @"""aversion"",""sd""", "0.9,1.1"),
            Stretch("sd", @"""sp""", "1.0"));

        /// Four addressee stretches, each a 3-tick aversion separator followed by
        /// a person fixation whose (person, run length) pair is unique — the
        /// drawn stretch is decodable from the emitted targets alone.
        static HoldingSequenceBank BalancedDrawBank() => Parse(
            Stretch("ad", @"""aversion"",""sp""", "0.1,0.1"),
            Stretch("ad", @"""aversion"",""sp""", "0.1,0.2"),
            Stretch("ad", @"""aversion"",""sd""", "0.1,0.1"),
            Stretch("ad", @"""aversion"",""sd""", "0.1,0.2"),
            Stretch("sp", @"""ad""", "1.0"),
            Stretch("sd", @"""sp""", "1.0"));

        static HoldingReplayGazePolicy CreateSystemUnderTest(HoldingSequenceBank bank, int seed = 1)
        {
            var sut = new HoldingReplayGazePolicy(bank, fallbackTarget: User);
            sut.Reset(seed);
            return sut;
        }

        /// A state in which agent A sees <paramref name="speaker"/> addressing
        /// <paramref name="addressee"/>; A's own role follows from the pair.
        static ConversationState StateWith(ParticipantId speaker, ParticipantId addressee)
        {
            var role = speaker == AgentA ? ParticipantRole.Speaker
                : addressee == AgentA ? ParticipantRole.Addressee
                : ParticipantRole.SideParticipant;

            return new ConversationState
            {
                SelfId = AgentA,
                SelfRole = role,
                CurrentSpeaker = speaker,
                CurrentAddressee = addressee,
                FirstPartner = AgentB,
                SecondPartner = User,
                TimeSinceTurnInstant = 30f,
                PredictedTimeToTurnEnd = 30f,
            };
        }

        /// A state in which agent A plays <paramref name="role"/>, with the same
        /// speaker/addressee mapping ShintaniGazePolicyTest uses.
        static ConversationState StateFor(ParticipantRole role)
        {
            var speaker = role == ParticipantRole.Speaker ? AgentA : AgentB;
            var addressee = role == ParticipantRole.Addressee ? AgentA : role == ParticipantRole.Speaker ? AgentB : User;

            return StateWith(speaker, addressee);
        }

        /// Same roles as <see cref="StateFor"/> but right on a turn boundary —
        /// material only to a policy that reads the turn fields.
        static ConversationState NearTurnState(ParticipantRole role)
        {
            var state = StateFor(role);
            state.TimeSinceTurnInstant = 0.5f;
            state.PredictedTimeToTurnEnd = 0.5f;
            state.UpcomingEventIndex = 0;
            return state;
        }

        /// No valid speaker/addressee pair has been seen yet.
        static ConversationState RolelessState() =>
            new() { SelfId = AgentA, FirstPartner = AgentB, SecondPartner = User };

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

        static GazeTarget[] Run(HoldingReplayGazePolicy sut, ConversationState state, int ticks)
        {
            var targets = new GazeTarget[ticks];
            for (var i = 0; i < ticks; i++)
                targets[i] = sut.Update(TickSeconds, in state);

            return targets;
        }

        static float[] Occupancy(HoldingReplayGazePolicy sut, ConversationState state, int ticks)
        {
            var counts = new int[4];
            for (var i = 0; i < ticks; i++)
                counts[(int)TargetRoleOf(sut.Update(TickSeconds, in state), in state)]++;

            var occupancy = new float[counts.Length];
            for (var i = 0; i < counts.Length; i++)
                occupancy[i] = (float)counts[i] / ticks;

            return occupancy;
        }

        static int DistinctTargetCount(GazeTarget[] targets)
        {
            var distinct = new HashSet<GazeTarget>();
            for (var i = 0; i < targets.Length; i++)
                distinct.Add(targets[i]);

            return distinct.Count;
        }

        static GazeTarget[] DistinctConsecutive(GazeTarget[] targets)
        {
            var collapsed = new List<GazeTarget>();
            for (var i = 0; i < targets.Length; i++)
            {
                if (collapsed.Count == 0 || targets[i] != collapsed[^1])
                    collapsed.Add(targets[i]);
            }

            return collapsed.ToArray();
        }

        static int[] RunLengths(GazeTarget[] targets)
        {
            var lengths = new List<int>();
            var index = 0;
            while (index < targets.Length)
            {
                var start = index;
                while (index < targets.Length && targets[index] == targets[start])
                    index++;
                lengths.Add(index - start);
            }

            return lengths.ToArray();
        }

        /// Counts draws per stretch of <see cref="BalancedDrawBank"/>'s addressee
        /// pool by each person-fixation run's unique (person, length) signature;
        /// the 3-tick aversion runs are the separators between draws.
        static int[] StretchDrawCounts(HoldingReplayGazePolicy sut, ConversationState state, int ticks)
        {
            var targets = Run(sut, state, ticks);
            var counts = new int[4];
            var index = 0;
            while (index < targets.Length)
            {
                var start = index;
                while (index < targets.Length && targets[index] == targets[start])
                    index++;
                var length = index - start;

                if (targets[start] == GazeTarget.AtPerson(AgentB) && length == 3)
                    counts[0]++;
                else if (targets[start] == GazeTarget.AtPerson(AgentB) && length == 6)
                    counts[1]++;
                else if (targets[start] == GazeTarget.AtPerson(User) && length == 3)
                    counts[2]++;
                else if (targets[start] == GazeTarget.AtPerson(User) && length == 6)
                    counts[3]++;
            }

            return counts;
        }

        [Test]
        public void Constructor_WithoutABank_ThrowsArgumentNullException()
        {
            Assert.That(() => new HoldingReplayGazePolicy(null, fallbackTarget: User),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void Constructor_WithAnInvalidFallbackTarget_ThrowsArgumentException()
        {
            Assert.That(() => new HoldingReplayGazePolicy(SingleStretchBank(), ParticipantId.None),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        [Category("Acceptance")]
        public void Update_BeforeAnyoneHasSpoken_HoldsTheFallbackTarget()
        {
            var sut = CreateSystemUnderTest(SingleStretchBank());

            var actual = sut.Update(TickSeconds, RolelessState());

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        public void Update_AfterRolesAreKnownAndThenIncomplete_KeepsReplayingTheStretch()
        {
            var sut = CreateSystemUnderTest(SingleStretchBank());
            sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            var actual = sut.Update(TickSeconds, RolelessState());

            // The sp stretch opens on the addressee; the roles retained from the
            // last valid state keep resolving it, not the fallback.
            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WithASingleStretchBank_PlaysItsFixationsInRecordedOrder()
        {
            var sut = CreateSystemUnderTest(SingleStretchBank());

            var actual = DistinctConsecutive(Run(sut, StateFor(ParticipantRole.Speaker), ticks: 45));

            Assert.That(actual, Is.EqualTo(new[]
            {
                GazeTarget.AtPerson(AgentB),
                GazeTarget.Away(Vector2.zero),
                GazeTarget.AtPerson(User),
            }));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WithASingleStretchBank_HoldsEachFixationForItsRecordedDuration()
        {
            var sut = CreateSystemUnderTest(SingleStretchBank());

            var actual = RunLengths(Run(sut, StateFor(ParticipantRole.Speaker), ticks: 45));

            // 0.5 s per fixation at the 30 Hz decision rate.
            Assert.That(actual, Is.EqualTo(new[] { 15, 15, 15 }));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_AfterAStretchIsExhausted_DrawsAFurtherStretch()
        {
            var sut = CreateSystemUnderTest(SingleStretchBank());
            Run(sut, StateFor(ParticipantRole.Speaker), ticks: 45);

            var actual = sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            // The sp pool holds one stretch, so the fresh draw replays it from
            // its first fixation instead of holding the exhausted stretch's last.
            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_AtAnAversionFixation_RecentresTheEyesInTheHead()
        {
            var sut = CreateSystemUnderTest(AversionOpeningBank());

            var actual = sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            Assert.That(actual.Type, Is.EqualTo(GazeTargetType.Aversion), "aversion target");
            Assert.That(actual.AversionOffset, Is.EqualTo(Vector2.zero), "eyes recentred in the head");
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WhenAnotherParticipantTakesTheFixationsRole_RetargetsWithinTheFixation()
        {
            // A is the side participant; the sd pool's lone fixation dwells on
            // the speaker for 5 s.
            var sut = CreateSystemUnderTest(LongFixationBank());
            sut.Update(TickSeconds, StateWith(speaker: AgentB, addressee: User));

            var actual = sut.Update(TickSeconds, StateWith(speaker: User, addressee: AgentB));

            // The fixation names a role, not a person: the new speaker inherits
            // the gaze mid-fixation.
            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WhenTheAgentsRoleChanges_FinishesTheCurrentFixation()
        {
            var sut = CreateSystemUnderTest(RoleChangeBank());
            sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            var actual = Run(sut, StateFor(ParticipantRole.Addressee), ticks: 14);

            // The sp fixation on the side participant has 14 of its 15 ticks
            // left; it survives A's own move into the addressee role.
            Assert.That(actual, Has.All.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_AfterTheCurrentFixationEndsOnARoleChange_PlaysAStretchDrawnForTheNewRole()
        {
            var sut = CreateSystemUnderTest(RoleChangeBank());
            sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));
            Run(sut, StateFor(ParticipantRole.Addressee), ticks: 14);

            var actual = sut.Update(TickSeconds, StateFor(ParticipantRole.Addressee));

            // Only the addressee pool averts: this is a fresh ad stretch, not
            // the abandoned sp stretch's second fixation (5 s on the side
            // participant).
            Assert.That(actual, Is.EqualTo(GazeTarget.Away(Vector2.zero)));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WhenARoleChangeWouldMakeTheAgentGazeAtItself_EndsTheFixationOnThatTick()
        {
            // The sp pool's lone fixation dwells on the addressee for 5 s.
            var sut = CreateSystemUnderTest(LongFixationBank());
            sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            // A becomes the addressee the fixation names.
            var actual = sut.Update(TickSeconds, StateFor(ParticipantRole.Addressee));

            Assert.That(actual, Is.Not.EqualTo(GazeTarget.AtPerson(AgentA)), "never gazes at itself");
            Assert.That(actual, Is.EqualTo(GazeTarget.Away(Vector2.zero)),
                "a stretch from the addressee pool, which only averts");
        }

        [Test]
        public void Update_OverALongRun_NeverGazesAtItself()
        {
            var sut = CreateSystemUnderTest(VariedBank());

            var actual = Run(sut, StateFor(ParticipantRole.Addressee), ticks: 20000);

            Assert.That(actual, Has.None.Matches<GazeTarget>(
                target => target.Type == GazeTargetType.Person && target.Person == AgentA));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WithTheSameSeed_ReplaysTheSameTargetSequence()
        {
            var state = StateFor(ParticipantRole.Addressee);
            var expected = Run(CreateSystemUnderTest(VariedBank(), seed: 42), state, ticks: 2000);

            var actual = Run(CreateSystemUnderTest(VariedBank(), seed: 42), state, ticks: 2000);

            Assert.That(DistinctTargetCount(expected), Is.GreaterThan(1), "the replay is non-trivial");
            Assert.That(actual, Is.EqualTo(expected), "same seed, same sequence");
        }

        [Test]
        public void Update_WithDifferentSeeds_DivergesSoTwoAgentsDoNotCorrelate()
        {
            var state = StateFor(ParticipantRole.Addressee);
            var other = Run(CreateSystemUnderTest(VariedBank(), seed: 42), state, ticks: 2000);

            var actual = Run(CreateSystemUnderTest(VariedBank(), seed: 43), state, ticks: 2000);

            Assert.That(actual, Is.Not.EqualTo(other));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WithStatesDifferingOnlyInTurnState_ProducesTheSameTargetSequence()
        {
            var expected = Run(CreateSystemUnderTest(VariedBank(), seed: 7),
                StateFor(ParticipantRole.Addressee), ticks: 2000);

            var actual = Run(CreateSystemUnderTest(VariedBank(), seed: 7),
                NearTurnState(ParticipantRole.Addressee), ticks: 2000);

            Assert.That(DistinctTargetCount(expected), Is.GreaterThan(1), "the replay is non-trivial");
            Assert.That(actual, Is.EqualTo(expected), "holding replay never reads the turn fields");
        }

        [Test]
        [Category("Acceptance")]
        public void Update_OverManyStretches_DrawsEveryStretchInTheRolePool()
        {
            var sut = CreateSystemUnderTest(BalancedDrawBank());

            var actual = StretchDrawCounts(sut, StateFor(ParticipantRole.Addressee), ticks: 30000);

            // ~4,000 draws over 4 stretches; a uniform draw puts each near
            // 1,000 (binomial sd ~27). The band catches a draw that skips or
            // heavily favours a stretch, not sampling noise.
            Assert.That(actual, Has.All.InRange(600, 1400));
        }

        [TestCase(ParticipantRole.Speaker, GazeTargetRole.Addressee, 0.3305f)]
        [TestCase(ParticipantRole.Speaker, GazeTargetRole.SideParticipant, 0.2274f)]
        [TestCase(ParticipantRole.Speaker, GazeTargetRole.Aversion, 0.4421f)]
        [TestCase(ParticipantRole.Addressee, GazeTargetRole.Speaker, 0.5147f)]
        [TestCase(ParticipantRole.Addressee, GazeTargetRole.SideParticipant, 0.1263f)]
        [TestCase(ParticipantRole.Addressee, GazeTargetRole.Aversion, 0.3590f)]
        [TestCase(ParticipantRole.SideParticipant, GazeTargetRole.Speaker, 0.4583f)]
        [TestCase(ParticipantRole.SideParticipant, GazeTargetRole.Addressee, 0.1520f)]
        [TestCase(ParticipantRole.SideParticipant, GazeTargetRole.Aversion, 0.3897f)]
        [Category("Acceptance")]
        public void Update_OverALongRun_ReproducesTheCorpusHoldingGazeRatio(
            ParticipantRole role, GazeTargetRole target, float expected)
        {
            var sut = CreateSystemUnderTest(HoldingSequenceBank.LoadDefault());

            var actual = Occupancy(sut, StateFor(role), LongRunTicks)[(int)target];

            Assert.That(actual, Is.EqualTo(expected).Within(OccupancyTolerance));
        }

        [Test]
        [Category("Acceptance")]
        public void Reset_AfterALongRun_RestartsTheSameSequence()
        {
            var state = StateFor(ParticipantRole.Addressee);
            var sut = CreateSystemUnderTest(VariedBank(), seed: 5);
            var expected = Run(sut, state, ticks: 2000);
            Run(sut, state, ticks: 2000);

            sut.Reset(5);
            var actual = Run(sut, state, ticks: 2000);

            Assert.That(DistinctTargetCount(expected), Is.GreaterThan(1), "the replay is non-trivial");
            Assert.That(actual, Is.EqualTo(expected), "reset restarts the draw stream");
        }

        [Test]
        [Category("Acceptance")]
        public void Reset_AfterRolesAreKnown_ReturnsToTheFallbackTarget()
        {
            var sut = CreateSystemUnderTest(SingleStretchBank());
            sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            sut.Reset(1);
            var actual = sut.Update(TickSeconds, RolelessState());

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [TestCase("ad", GazeTargetRole.Addressee)]
        [TestCase("aversion", GazeTargetRole.Aversion)]
        public void State_DuringAFixation_ReportsTheGazeInCorpusRoleCoding(
            string fixationTarget, GazeTargetRole expected)
        {
            var bank = Parse(
                Stretch("sp", $@"""{fixationTarget}""", "5.0"),
                Stretch("ad", @"""aversion""", "5.0"),
                Stretch("sd", @"""sp""", "5.0"));
            var sut = CreateSystemUnderTest(bank);

            sut.Update(TickSeconds, StateFor(ParticipantRole.Speaker));

            Assert.That(sut.State, Is.EqualTo(expected));
        }
    }
}
