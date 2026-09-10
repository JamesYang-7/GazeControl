using System;

namespace GazeControl.Study
{
    /// <summary>
    /// The answers to one screen while they are being typed: a fixed number of
    /// slots, a cursor, and the rules that say what may go in them.
    ///
    /// <para>One class covers both entry shapes the study has — four Likert
    /// items on a 1-7 scale, and three ranks that must be distinct — because
    /// they differ only in their bounds and in whether repeats are allowed.
    /// Keeping it here, pure, is what lets the refusals be tested without an
    /// operator, a headset or a scene.</para>
    ///
    /// <para><b>Nothing here writes.</b> A screen is committed to
    /// <see cref="QuestionnaireResponseWriter"/> as a whole, once the operator
    /// says so, so a mistyped answer can still be corrected — the writer takes
    /// each question exactly once and offers no amendment.</para>
    /// </summary>
    public sealed class QuestionnaireEntry
    {
        /// <summary>A slot nobody has answered yet.</summary>
        public const int Unanswered = 0;

        readonly int[] _values;

        /// <param name="slotCount">How many answers this screen asks for.</param>
        /// <param name="min">Lowest value a slot accepts.</param>
        /// <param name="max">Highest value a slot accepts.</param>
        /// <param name="requireDistinct">True where two slots may not share a value, as a ranking may not tie.</param>
        public QuestionnaireEntry(int slotCount, int min, int max, bool requireDistinct)
        {
            if (slotCount < 1)
                throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "a screen asks at least one thing");

            if (min > max)
                throw new ArgumentOutOfRangeException(nameof(min), min, $"min {min} is above max {max}");

            // Zero is how an unanswered slot reads, so a scale that includes it
            // would make "not yet answered" and "answered 0" the same value.
            if (min <= Unanswered)
                throw new ArgumentOutOfRangeException(nameof(min), min, "the scale must start above zero");

            _values = new int[slotCount];
            Min = min;
            Max = max;
            RequireDistinct = requireDistinct;
        }

        public int SlotCount => _values.Length;

        public int Min { get; }

        public int Max { get; }

        public bool RequireDistinct { get; }

        /// <summary>The slot the next value lands in; equals <see cref="SlotCount"/> once every slot is filled.</summary>
        public int Cursor { get; private set; }

        /// <summary>True once every slot holds a value.</summary>
        public bool IsComplete
        {
            get
            {
                foreach (var value in _values)
                {
                    if (value == Unanswered)
                        return false;
                }

                return true;
            }
        }

        /// <summary>What is in a slot, or <see cref="Unanswered"/>.</summary>
        public int this[int slot] => _values[slot];

        /// <summary>
        /// Put a value in the slot under the cursor and move on, or refuse and
        /// say why. The reason is shown to the operator, so it names the rule
        /// rather than the field.
        /// </summary>
        public bool TryEnter(int value, out string refusal)
        {
            refusal = Refusal(value);
            if (refusal != null)
                return false;

            _values[Cursor] = value;
            Cursor++;
            return true;
        }

        /// <summary>
        /// Whether <see cref="TryEnter"/> would take this value now. The
        /// participant's button row greys out what it would refuse rather than
        /// showing a refusal they cannot read (their panel carries no notice),
        /// and asking the same predicate is what keeps the two from drifting.
        /// </summary>
        public bool Accepts(int value) => Refusal(value) == null;

        /// <summary>Why this value cannot go in the slot under the cursor, or null when it can.</summary>
        string Refusal(int value)
        {
            if (Cursor >= SlotCount)
                return "every answer on this screen is entered.";

            if (value < Min || value > Max)
                return $"{value} is outside the {Min}-{Max} scale.";

            if (!RequireDistinct)
                return null;

            for (var i = 0; i < _values.Length; i++)
            {
                if (i != Cursor && _values[i] == value)
                    return $"{value} is already used, and ties are not allowed.";
            }

            return null;
        }

        /// <summary>
        /// Step back one slot and clear it, so an answer entered wrongly can be
        /// retyped before the screen is committed.
        /// </summary>
        public void Back()
        {
            if (Cursor > 0)
                Cursor--;

            _values[Cursor] = Unanswered;
        }

        /// <summary>Move the cursor to a slot without clearing anything, to correct one answer out of several.</summary>
        public void MoveTo(int slot)
        {
            if (slot < 0 || slot > SlotCount)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, $"this screen has {SlotCount} slots");

            Cursor = slot;
        }

        /// <summary>Forget everything typed, for a screen that is started again.</summary>
        public void Clear()
        {
            Array.Clear(_values, 0, _values.Length);
            Cursor = 0;
        }

        /// <summary>The answers, in slot order. A copy: nothing outside may write them.</summary>
        public int[] ToArray() => (int[])_values.Clone();
    }
}
