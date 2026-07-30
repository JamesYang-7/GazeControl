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

        [Test]
        public void Update_SelfSpeaking_LooksAtAddressee()
        {
            var voiceActivity = CreateVoiceActivityTracker();
            var sut = CreateSystemUnderTest(voiceActivity);

            voiceActivity.Tick(0.3f, new[] { true, false, false });
            var actual = sut.Update(0.3f, StateAddressing(AgentB));

            Assert.That(actual, Is.EqualTo(GazeTarget.AtPerson(AgentB)));
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

            voiceActivity.Tick(0.3f, new[] { true, false, false });
            var actual = sut.Update(0.3f, StateAddressing(User));

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
