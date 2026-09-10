using System;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class QuestionnaireEntryTest
    {
        static QuestionnaireEntry Ratings() => new(4, 1, 7, requireDistinct: false);

        static QuestionnaireEntry Ranks() => new(3, 1, 3, requireDistinct: true);

        static void Enter(QuestionnaireEntry entry, params int[] values)
        {
            foreach (var value in values)
                Assert.That(entry.TryEnter(value, out var refusal), Is.True, refusal);
        }

        [Test]
        public void TryEnter_FillsSlotsInOrder()
        {
            var sut = Ratings();

            Enter(sut, 5, 2, 7, 1);

            Assert.That(sut.ToArray(), Is.EqualTo(new[] { 5, 2, 7, 1 }));
            Assert.That(sut.Cursor, Is.EqualTo(4));
            Assert.That(sut.IsComplete, Is.True);
        }

        [Test]
        public void IsComplete_WithASlotStillEmpty_IsFalse()
        {
            var sut = Ratings();

            Enter(sut, 5, 2, 7);

            Assert.That(sut.IsComplete, Is.False);
        }

        [TestCase(0)]
        [TestCase(8)]
        [TestCase(-1)]
        public void TryEnter_OffTheScale_IsRefusedAndChangesNothing(int value)
        {
            var sut = Ratings();

            Assert.That(sut.TryEnter(value, out var refusal), Is.False);
            Assert.That(refusal, Does.Contain("1-7"));
            Assert.That(sut.Cursor, Is.EqualTo(0));
            Assert.That(sut[0], Is.EqualTo(QuestionnaireEntry.Unanswered));
        }

        [Test]
        public void TryEnter_PastTheLastSlot_IsRefused()
        {
            var sut = Ranks();
            Enter(sut, 1, 2, 3);

            Assert.That(sut.TryEnter(1, out var refusal), Is.False);
            Assert.That(refusal, Does.Contain("entered"));
            Assert.That(sut.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void TryEnter_RepeatingARank_IsRefusedBecauseTiesAreNotAllowed()
        {
            // The study's primary dependent variable is a strict order of the
            // three versions (user-study-design.md §6.3); a tie is unusable.
            var sut = Ranks();
            Enter(sut, 2);

            Assert.That(sut.TryEnter(2, out var refusal), Is.False);
            Assert.That(refusal, Does.Contain("ties"));
            Assert.That(sut.Cursor, Is.EqualTo(1));
        }

        [Test]
        public void TryEnter_RepeatingARating_IsAllowed()
        {
            // Two clips can honestly be rated the same; only ranks must differ.
            var sut = Ratings();

            Enter(sut, 4, 4);

            Assert.That(sut.ToArray(), Is.EqualTo(new[] { 4, 4, 0, 0 }));
        }

        [Test]
        public void Back_ClearsTheSlotItStepsOntoSoItCanBeRetyped()
        {
            var sut = Ratings();
            Enter(sut, 5, 2);

            sut.Back();

            Assert.That(sut.Cursor, Is.EqualTo(1));
            Assert.That(sut[1], Is.EqualTo(QuestionnaireEntry.Unanswered));
            Assert.That(sut[0], Is.EqualTo(5), "the answer before it is untouched");
        }

        [Test]
        public void Back_AfterTheLastSlot_ClearsThatLastAnswer()
        {
            // The cursor sits past the end when a screen is full, so one Back
            // must reach the answer just typed rather than the one before it.
            var sut = Ranks();
            Enter(sut, 1, 2, 3);

            sut.Back();

            Assert.That(sut.Cursor, Is.EqualTo(2));
            Assert.That(sut[2], Is.EqualTo(QuestionnaireEntry.Unanswered));
            Assert.That(sut.IsComplete, Is.False);
        }

        [Test]
        public void Back_OnAnEmptyScreen_DoesNothingBad()
        {
            var sut = Ratings();

            sut.Back();

            Assert.That(sut.Cursor, Is.EqualTo(0));
        }

        [Test]
        public void MoveTo_ThenEnter_CorrectsOneAnswerWithoutClearingTheRest()
        {
            var sut = Ratings();
            Enter(sut, 5, 2, 7, 1);

            sut.MoveTo(1);
            Enter(sut, 6);

            Assert.That(sut.ToArray(), Is.EqualTo(new[] { 5, 6, 7, 1 }));
        }

        [Test]
        public void MoveTo_ThenEnter_StillRefusesATieWithASlotItIsNotOn()
        {
            var sut = Ranks();
            Enter(sut, 1, 2, 3);

            sut.MoveTo(0);

            Assert.That(sut.TryEnter(3, out _), Is.False, "3 is held by another version");
            Assert.That(sut.TryEnter(1, out _), Is.True, "its own value is not a tie with itself");
        }

        [Test]
        public void Clear_ForgetsEverything()
        {
            var sut = Ratings();
            Enter(sut, 5, 2);

            sut.Clear();

            Assert.That(sut.Cursor, Is.EqualTo(0));
            Assert.That(sut.ToArray(), Is.EqualTo(new[] { 0, 0, 0, 0 }));
        }

        [Test]
        public void ToArray_IsACopy() =>
            Assert.That(Ratings().ToArray(), Is.Not.SameAs(Ratings().ToArray()));

        [Test]
        public void Constructor_WithAScaleThatIncludesZero_IsRejected() =>
            // Zero is what an unanswered slot reads as, so a scale containing it
            // would make "not answered" indistinguishable from an answer.
            Assert.That(() => new QuestionnaireEntry(4, 0, 7, false),
                Throws.TypeOf<ArgumentOutOfRangeException>());

        [Test]
        public void Constructor_WithNoSlots_IsRejected() =>
            Assert.That(() => new QuestionnaireEntry(0, 1, 7, false),
                Throws.TypeOf<ArgumentOutOfRangeException>());

        [Test]
        public void Accepts_AgreesWithTryEnter()
        {
            var sut = Ranks();
            Enter(sut, 2);

            // The same predicate the participant's button row is greyed by, so
            // a button that looks pressable is one that will be taken.
            Assert.That(sut.Accepts(1), Is.True);
            Assert.That(sut.Accepts(2), Is.False, "already ranked");
            Assert.That(sut.Accepts(4), Is.False, "off the scale");
        }

        [Test]
        public void Accepts_WithEverySlotFilled_IsFalseForEverything()
        {
            var sut = Ratings();
            Enter(sut, 5, 5, 5, 5);

            Assert.That(sut.Accepts(5), Is.False);
            Assert.That(sut.Accepts(1), Is.False);
        }
    }
}
