namespace GazeControl.Study
{
    /// <summary>What pressing one of the participant's buttons does.</summary>
    public enum QuestionnaireButtonKind
    {
        /// <summary>Undo the last value entered on this screen.</summary>
        Back,

        /// <summary>Put <see cref="QuestionnaireButton.Value"/> in the slot under the entry cursor.</summary>
        Value,

        /// <summary>Commit the screen, which on a rating screen is what releases the next clip.</summary>
        Next,
    }

    /// <summary>
    /// One button on the participant's own row: what it does, what it says, and
    /// whether it may be pressed right now.
    ///
    /// <para><b>The row is state, not widgets.</b> It is built from the entry
    /// the operator's keys write into, so the two ways of answering — spoken and
    /// typed, or pointed at with a controller — are the same screen underneath
    /// and cannot disagree about what has been answered. The rendering side owns
    /// nothing but the rectangles.</para>
    /// </summary>
    public readonly struct QuestionnaireButton
    {
        public QuestionnaireButton(QuestionnaireButtonKind kind, int value, string label, bool enabled)
        {
            Kind = kind;
            Value = value;
            Label = label;
            Enabled = enabled;
        }

        public QuestionnaireButtonKind Kind { get; }

        /// <summary>The number a <see cref="QuestionnaireButtonKind.Value"/> button enters; 0 on the others.</summary>
        public int Value { get; }

        /// <summary>What the participant reads on the button.</summary>
        public string Label { get; }

        /// <summary>
        /// False where pressing it would be refused — nothing to undo, the slots
        /// full, a rank already used, a rating not yet complete. A disabled
        /// button is shown dimmed and ignored rather than hidden, so the row
        /// keeps the same shape and the same button stays under the same place
        /// on the panel from screen to screen.
        /// </summary>
        public bool Enabled { get; }

        public override string ToString() =>
            $"{Kind}{(Kind == QuestionnaireButtonKind.Value ? $" {Value}" : string.Empty)}" +
            $" '{Label}'{(Enabled ? string.Empty : " (disabled)")}";
    }
}
