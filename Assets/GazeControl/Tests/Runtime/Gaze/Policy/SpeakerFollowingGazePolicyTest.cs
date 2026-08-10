using NUnit.Framework;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class SpeakerFollowingGazePolicyTest
    {
        const float OnsetSeconds = 0.25f;
        const float OffsetSeconds = 0.5f;
        const float BackchannelSeconds = 0.6f;
        const float MinimumDwellSeconds = 0.7f;

        static readonly ParticipantId AgentA = new(0);
        static readonly ParticipantId AgentB = new(1);
        static readonly ParticipantId User = new(2);

        static VoiceActivityTracker CreateVoiceActivityTracker() =>
            new(participantCount: 3, OnsetSeconds, OffsetSeconds);

        // The policy under test drives agent A; the human user is its fallback
        // addressee, so that is also where it starts out looking.
        static SpeakerFollowingGazePolicy CreateSystemUnderTest(VoiceActivityTracker voiceActivity) =>
            new(
                new SpeakerFollowingSettings
                {
                    OnsetSeconds = OnsetSeconds,
                    OffsetSeconds = OffsetSeconds,
                    BackchannelSeconds = BackchannelSeconds,
                    MinimumDwellSeconds = MinimumDwellSeconds,
                },
                voiceActivity,
                fallbackAddressee: User);

        static ConversationState StateAddressing(ParticipantId addressee) => new()
        {
            SelfId = AgentA,
            CurrentAddressee = addressee,
            FirstPartner = AgentB,
            SecondPartner = User,
        };

        /// The schedule during someone else's turn: they hold the floor and this
        /// agent is the one they are addressing.
        static ConversationState StateWhileOtherHoldsFloor(ParticipantId speaker) => new()
        {
            SelfId = AgentA,
            CurrentSpeaker = speaker,
            CurrentAddressee = AgentA,
            FirstPartner = AgentB,
            SecondPartner = User,
        };

        [Test]
        public void Update_SelfSpeaking_LooksAtAddressee()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            voiceActivity.Tick(0.7f, new[] { true, false, false });
            var actual = sut.Update(0.7f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_SelfUtteranceShorterThanBackchannelThreshold_HoldsPreviousTarget()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            // The agent's own "yeah" thrown in while the other agent holds the
            // floor is a backchannel, and §3 says a backchannel triggers no gaze
            // switch — whoever produced it.
            voiceActivity.Tick(0.4f, new[] { true, false, false });
            var actual = sut.Update(0.4f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)), "still on the fallback it started on");
        }

        [Test]
        public void Update_SpeakingOverTheFloorHolder_LooksAtThemRatherThanAtItself()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            // current_addressee belongs to whoever holds the floor, so while B
            // is speaking to A it names A. An agent is not its own addressee —
            // the one it is addressing is the floor-holder it is answering.
            voiceActivity.Tick(0.7f, new[] { true, false, false });
            var actual = sut.Update(0.7f, StateWhileOtherHoldsFloor(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_SpeakingWithNobodyElseNamed_FallsBackToTheHuman()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            // No schedule at all: neither addressee nor speaker names anyone
            // else, so §3's fallback is all that is left.
            voiceActivity.Tick(0.7f, new[] { true, false, false });
            var actual = sut.Update(0.7f, StateAddressing(ParticipantId.None));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        public void Update_PartnerSpeakingPastBackchannelThreshold_LooksAtThatPartner()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            voiceActivity.Tick(0.7f, new[] { false, true, false });
            var actual = sut.Update(0.7f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_PartnerUtteranceShorterThanBackchannelThreshold_HoldsPreviousTarget()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            voiceActivity.Tick(0.4f, new[] { false, true, false });
            var actual = sut.Update(0.4f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        public void Update_WithinMinimumDwellAfterSwitch_HoldsPreviousTarget()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);
            voiceActivity.Tick(0.7f, new[] { false, true, false });
            sut.Update(0.7f, StateAddressing(AgentB));

            // Voiced past the backchannel threshold but still inside the 0.7 s
            // dwell, so it is the dwell alone that holds the target here.
            voiceActivity.Tick(0.65f, new[] { true, false, false });
            var actual = sut.Update(0.65f, StateAddressing(User));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_EveryoneSilent_HoldsLastTarget()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);
            voiceActivity.Tick(0.7f, new[] { false, true, false });
            sut.Update(0.7f, StateAddressing(AgentB));

            voiceActivity.Tick(1.0f, new[] { false, false, false });
            var actual = sut.Update(1.0f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_SecondSpeakerJoinsWhileCurrentTargetStillVoiced_StaysWithCurrentTarget()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);
            voiceActivity.Tick(0.8f, new[] { false, true, false });
            sut.Update(0.8f, StateAddressing(AgentB));

            voiceActivity.Tick(0.8f, new[] { false, true, true });
            var actual = sut.Update(0.8f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
        }

        [Test]
        public void Update_NeverAverts_AlwaysReturnsPersonTarget()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            voiceActivity.Tick(1.0f, new[] { false, false, false });
            var actual = sut.Update(1.0f, StateAddressing(AgentB));

            Assert.That(actual.Type, Is.EqualTo(GazeTargetType.Person));
        }
    }
}
