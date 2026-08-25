using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class BakedGazePolicyTest
    {
        static readonly ParticipantId AgentB = new(1);
        static readonly ParticipantId User = new(2);

        const float TickSeconds = 1f / 30f;

        /// <summary>Three decisions a second apart: person, aversion, person.</summary>
        static GazeTrackAgent Track() => new()
        {
            agentId = 0,
            name = "AgentA",
            samples = new[]
            {
                GazeTrackSample.From(0f, GazeTarget.AtPerson(AgentB)),
                GazeTrackSample.From(1f, GazeTarget.Away(new Vector2(-20f, 11f))),
                GazeTrackSample.From(2f, GazeTarget.AtPerson(User)),
            },
        };

        static ConversationState State() => new() { SelfId = new ParticipantId(0) };

        /// <summary>Play the track with a clock the caller drives, and return what was emitted.</summary>
        static List<GazeTarget> Play(BakedGazePolicy sut, IEnumerable<float> clockReadings, float[] cursor)
        {
            var emitted = new List<GazeTarget>();
            foreach (var reading in clockReadings)
            {
                cursor[0] = reading;
                var state = State();
                emitted.Add(sut.Update(TickSeconds, in state));
            }

            return emitted;
        }

        /// <summary>
        /// Times computed from an index rather than accumulated: the real clock
        /// is <c>Time.time - start</c>, which does not drift, and an accumulated
        /// test clock lands a sample boundary a tick early or late for reasons
        /// that have nothing to do with the policy.
        /// </summary>
        static IEnumerable<float> Ramp(float from, float to, float step)
        {
            var count = Mathf.FloorToInt((to - from) / step);
            for (var i = 0; i <= count; i++)
                yield return from + i * step;
        }

        [Test]
        public void Update_BeforeThePlaybackClockStarts_HoldsTheFirstDecision()
        {
            var clock = new float[1];
            var sut = new BakedGazePolicy(Track(), () => clock[0]);

            // Elapsed reads negative until the segment has loaded and the voices
            // are scheduled; a long load must not eat the track's opening.
            var emitted = Play(sut, new[] { -3f, -2f, -1f, -0.01f }, clock);

            Assert.That(emitted, Is.All.EqualTo(GazeTarget.AtPerson(AgentB)));
            Assert.That(sut.Cursor, Is.Zero, "the cursor has not advanced");
        }

        [Test]
        public void Update_AtASampleTime_EmitsThatSamplesDecision()
        {
            var clock = new float[1];
            var sut = new BakedGazePolicy(Track(), () => clock[0]);

            var emitted = Play(sut, new[] { 0f, 1f, 2f }, clock);

            Assert.That(emitted, Is.EqualTo(new[]
            {
                GazeTarget.AtPerson(AgentB),
                GazeTarget.Away(new Vector2(-20f, 11f)),
                GazeTarget.AtPerson(User),
            }));
        }

        [Test]
        public void Update_PastTheEndOfTheTrack_HoldsTheLastDecision()
        {
            var clock = new float[1];
            var sut = new BakedGazePolicy(Track(), () => clock[0]);

            var emitted = Play(sut, new[] { 2f, 5f, 60f }, clock);

            Assert.That(emitted, Is.All.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WithDifferentTickCountsOverTheSameTime_EmitsTheSameDecisions()
        {
            // The property the whole bake exists for. The live runner ticks from
            // the moment Play starts while the conversation clock starts only
            // once the segment has loaded, so the number of ticks covering a
            // given stretch of conversation varies between runs. Replay must not
            // care: what is emitted is a function of the clock alone.
            //
            // Stated as "extra ticks at the same instants change nothing" rather
            // than as two different step sizes: the latter compares two
            // accumulated clocks, and the accumulation — not the policy — is
            // what would decide whether an instant lands on a sample boundary.
            var instants = new List<float>();
            for (var i = 0; i <= 60; i++)
                instants.Add(i * 0.05f);

            var onceClock = new float[1];
            var thriceClock = new float[1];

            var once = Play(new BakedGazePolicy(Track(), () => onceClock[0]), instants, onceClock);

            var repeated = new List<float>();
            foreach (var instant in instants)
            {
                repeated.Add(instant);
                repeated.Add(instant);
                repeated.Add(instant);
            }

            var thrice = Play(new BakedGazePolicy(Track(), () => thriceClock[0]), repeated, thriceClock);

            Assert.That(thrice.Count, Is.EqualTo(once.Count * 3), "the two runs really do tick differently");

            for (var i = 0; i < once.Count; i++)
            {
                Assert.That(thrice[i * 3], Is.EqualTo(once[i]), $"decision at t={i * 0.05f:F2}s");
                Assert.That(thrice[i * 3 + 2], Is.EqualTo(once[i]), "extra ticks at one instant change nothing");
            }
        }

        [Test]
        [Category("Acceptance")]
        public void Update_WithAPreRollBeforeTheClockStarts_EmitsTheSameDecisionsAsWithout()
        {
            // Same conversation, one run preceded by a variable-length pre-roll.
            // This is exactly the divergence that made the live proposed
            // condition irreproducible between two runs of one clip.
            var bare = new float[1];
            var delayed = new float[1];

            var withoutPreRoll = Play(new BakedGazePolicy(Track(), () => bare[0]), Ramp(0f, 3f, 0.05f), bare);

            var preRoll = new List<float>();
            for (var i = 0; i < 37; i++)
                preRoll.Add(-1f);
            preRoll.AddRange(Ramp(0f, 3f, 0.05f));

            var withPreRoll = Play(new BakedGazePolicy(Track(), () => delayed[0]), preRoll, delayed);

            Assert.That(withPreRoll.GetRange(37, withoutPreRoll.Count), Is.EqualTo(withoutPreRoll));
        }

        [Test]
        public void Update_WhenTheClockStalls_DoesNotReplayAnEarlierDecision()
        {
            var clock = new float[1];
            var sut = new BakedGazePolicy(Track(), () => clock[0]);

            Play(sut, new[] { 2f }, clock);
            var afterStall = Play(sut, new[] { 0.5f }, clock);

            // Rewinding would show a gaze shift backwards in time and break the
            // log's monotonicity; the last decision is held instead.
            Assert.That(afterStall[0], Is.EqualTo(GazeTarget.AtPerson(User)));
        }

        [Test]
        public void Constructor_WithAnEmptyTrack_IsRejected()
        {
            var empty = new GazeTrackAgent { agentId = 0, samples = new GazeTrackSample[0] };

            Assert.That(() => new BakedGazePolicy(empty, () => 0f), Throws.ArgumentException);
        }
    }
}
