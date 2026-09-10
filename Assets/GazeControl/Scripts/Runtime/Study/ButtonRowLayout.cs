using System;
using UnityEngine;

namespace GazeControl.Study
{
    /// <summary>
    /// Equal buttons in a row, centred on the row's own origin: where each one
    /// is drawn, and which one a point falls in.
    ///
    /// <para>One layout answers both questions on purpose. A row whose hit test
    /// is computed separately from its rectangles is a row that can be a few
    /// millimetres out for the life of a study without anyone seeing it — the
    /// participant would simply press one thing and get its neighbour.</para>
    ///
    /// <para>The units are the caller's. The panel works in canvas units and
    /// hands those in; the tests work in metres. Nothing here assumes
    /// either.</para>
    /// </summary>
    public readonly struct ButtonRowLayout
    {
        /// <param name="width">Total width of the row, buttons and the gaps between them.</param>
        /// <param name="height">How tall every button is.</param>
        /// <param name="count">How many buttons; zero is a row that is not shown.</param>
        /// <param name="gap">Dead space between neighbours, so a press that lands between two hits neither.</param>
        public ButtonRowLayout(float width, float height, int count, float gap)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "a row cannot have fewer than no buttons");

            if (gap < 0f)
                throw new ArgumentOutOfRangeException(nameof(gap), gap, "the gap between buttons is not negative");

            Width = width;
            Height = height;
            Count = count;
            Gap = gap;
        }

        public float Width { get; }

        public float Height { get; }

        public int Count { get; }

        public float Gap { get; }

        /// <summary>
        /// How wide one button is. Negative when the gaps alone are wider than
        /// the row, which <see cref="RectOf"/> passes through rather than
        /// clamping: a row that cannot fit is a geometry mistake to see, not one
        /// to absorb.
        /// </summary>
        public float ButtonWidth => Count == 0 ? 0f : (Width - Gap * (Count - 1)) / Count;

        /// <summary>Where one button sits, in the row's own space, centred on its origin.</summary>
        public Rect RectOf(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"this row has {Count} buttons");

            var buttonWidth = ButtonWidth;
            return new Rect(
                -Width * 0.5f + index * (buttonWidth + Gap),
                -Height * 0.5f,
                buttonWidth,
                Height);
        }

        /// <summary>The button a point falls in, or -1 for the gaps and everywhere outside the row.</summary>
        public int IndexAt(Vector2 point)
        {
            for (var i = 0; i < Count; i++)
            {
                if (RectOf(i).Contains(point))
                    return i;
            }

            return -1;
        }
    }
}
