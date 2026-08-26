using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    [TestFixture]
    public class DecisionClockTest
    {
        const float DecisionHz = 30f;
        const float FrameSeconds = 1f / 60f;

        /// <summary>Ticks run at each clock reading, one entry per frame.</summary>
        static List<int> Play(DecisionClock sut, IEnumerable<float> clockReadings)
        {
            var ticks = new List<int>();
            foreach (var reading in clockReadings)
                ticks.Add(sut.Advance(reading));

            return ticks;
        }

        /// <summary>
        /// One reading per rendered frame, from 0 to exactly
        /// <paramref name="duration"/>.
        ///
        /// <para>Times are computed from an index rather than accumulated: the
        /// real clock is <c>Time.time - start</c>, which does not drift, and an
        /// accumulated test clock would land a tick a frame early or late for
        /// reasons that have nothing to do with the clock under test. The last
        /// reading is the duration itself so that two frame rates end on the
        /// same instant rather than a rounding of it.</para>
        /// </summary>
        static List<float> Frames(float duration, float frameSeconds)
        {
            var readings = new List<float>();
            var count = Mathf.FloorToInt(duration / frameSeconds);
            for (var i = 0; i < count; i++)
                readings.Add(i * frameSeconds);

            readings.Add(duration);
            return readings;
        }

        [Test]
        public void Advance_BeforeTheConversationStarts_RunsNoTick()
        {
            var sut = new DecisionClock(DecisionHz);

            // Elapsed reads negative until the segment has loaded and the voices
            // are scheduled. Ticking here is what consumed a policy's draws.
            var ticks = Play(sut, new[] { -3f, -2f, -1f, -0.01f });

            Assert.That(ticks, Is.All.Zero);
            Assert.That(sut.Ticks, Is.Zero);
        }

        [Test]
        public void Advance_OnTheConversationsFirstFrame_RunsOneTick()
        {
            var sut = new DecisionClock(DecisionHz);

            // The first decision lands at conversation t = 0, not one step later:
            // the take must not open on a gaze nobody chose.
            Assert.That(sut.Advance(0f), Is.EqualTo(1));
        }

        [Test]
        public void Advance_WithinTheSameStep_RunsNoFurtherTick()
        {
            var sut = new DecisionClock(DecisionHz);
            sut.Advance(0f);

            Assert.That(sut.Advance(0.5f * sut.Step), Is.Zero);
        }

        [Test]
        public void Advance_OverAStretchOfConversation_RunsOneTickPerStep()
        {
            var sut = new DecisionClock(DecisionHz);

            var ticks = Play(sut, Frames(10f, FrameSeconds));

            Assert.That(sut.Ticks * sut.Step, Is.EqualTo(10f).Within(sut.Step));
            Assert.That(ticks, Is.All.LessThanOrEqualTo(1), "a frame shorter than a step never runs two decisions");
        }

        [Test]
        public void Advance_AfterAStall_CatchesUpToTheSmoothlyTickedCount()
        {
            var stalled = new DecisionClock(DecisionHz);
            var smooth = new DecisionClock(DecisionHz);

            stalled.Advance(0f);
            stalled.Advance(1f);
            Play(smooth, Frames(1f, FrameSeconds));

            // A hitch must not lose decisions: dwell durations are counted in
            // ticks, so skipping them would shorten every fixation in flight.
            Assert.That(stalled.Ticks, Is.EqualTo(smooth.Ticks));
        }

        [Test]
        public void Advance_WhenTheClockGoesBackwards_RunsNoTick()
        {
            var sut = new DecisionClock(DecisionHz);
            sut.Advance(2f);
            var before = sut.Ticks;

            Assert.That(sut.Advance(0.5f), Is.Zero);
            Assert.That(sut.Ticks, Is.EqualTo(before), "the clock did not rewind");
        }

        [Test]
        [Category("Acceptance")]
        public void Advance_WithAPreRollBeforeTheConversationStarts_RunsTheSameTicksAtTheSameTimes()
        {
            // The blocker itself: the same conversation, preceded by pre-rolls of
            // different lengths. Under the old free-running accumulator every
            // pre-roll frame carried a tick, so the two runs put a different
            // number of decisions behind the same conversation and a stochastic
            // policy diverged.
            var conversation = Frames(5f, FrameSeconds);
            var withoutPreRoll = Play(new DecisionClock(DecisionHz), conversation);

            var longPreRoll = new List<float>();
            for (var i = 0; i < 37; i++)
                longPreRoll.Add(-1f);
            longPreRoll.AddRange(conversation);

            var shortPreRoll = new List<float> { -0.2f, -0.1f };
            shortPreRoll.AddRange(conversation);

            var afterLong = Play(new DecisionClock(DecisionHz), longPreRoll).GetRange(37, conversation.Count);
            var afterShort = Play(new DecisionClock(DecisionHz), shortPreRoll).GetRange(2, conversation.Count);

            Assert.That(afterLong, Is.EqualTo(withoutPreRoll));
            Assert.That(afterShort, Is.EqualTo(withoutPreRoll));
        }

        [Test]
        [Category("Acceptance")]
        public void Advance_AtADifferentFrameRate_RunsTheSameTotalOverTheSameConversation()
        {
            // What a stochastic policy spends is the tick count, so the same
            // stretch of conversation must cost the same number of draws
            // whatever frame rate the take happened to render at.
            var atSixty = new DecisionClock(DecisionHz);
            var atTwentyFive = new DecisionClock(DecisionHz);

            Play(atSixty, Frames(20f, FrameSeconds));
            Play(atTwentyFive, Frames(20f, 0.04f));

            Assert.That(atTwentyFive.Ticks, Is.EqualTo(atSixty.Ticks));
        }

        [Test]
        public void TimeOfTick_AcrossACatchUpBurst_GivesEachTickItsOwnInstant()
        {
            var sut = new DecisionClock(DecisionHz);

            // The frame a conversation starts on is regularly longer than a
            // step — Unity clamps it to maximumDeltaTime — so the opening
            // decisions all fall due at one clock reading. They still belong to
            // different instants, and a baked track stamped with the reading
            // instead was not strictly monotone.
            var due = sut.Advance(0.3333f);
            var first = sut.Ticks - due;

            Assert.That(due, Is.GreaterThan(1), "the burst really is a burst");
            Assert.That(sut.TimeOfTick(first), Is.EqualTo(0f));
            Assert.That(sut.TimeOfTick(first + 1) - sut.TimeOfTick(first), Is.EqualTo(sut.Step).Within(1e-6f));
            Assert.That(sut.TimeOfTick(sut.Ticks - 1), Is.LessThanOrEqualTo(0.3333f));
        }

        [Test]
        public void Constructor_WithANonPositiveRate_IsRejected()
        {
            Assert.That(() => new DecisionClock(0f), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
