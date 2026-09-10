using NUnit.Framework;
using UnityEngine;

namespace GazeControl.Study
{
    [TestFixture]
    public class ButtonRowLayoutTest
    {
        /// <summary>A rating row: ten units wide, nine buttons, unit gaps.</summary>
        static ButtonRowLayout Row() => new(10f, 2f, 9, 0.25f);

        [Test]
        public void ButtonWidth_SharesWhatTheGapsLeave()
        {
            var layout = new ButtonRowLayout(10f, 2f, 4, 1f);

            // Three gaps of one leave seven units for four buttons.
            Assert.That(layout.ButtonWidth, Is.EqualTo(1.75f).Within(1e-5f));
        }

        [Test]
        public void RectOf_SpansTheRowFromEndToEnd()
        {
            var layout = Row();

            Assert.That(layout.RectOf(0).xMin, Is.EqualTo(-5f).Within(1e-5f));
            Assert.That(layout.RectOf(8).xMax, Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void RectOf_IsCentredVerticallyOnTheRow()
        {
            var rect = Row().RectOf(3);

            Assert.That(rect.yMin, Is.EqualTo(-1f).Within(1e-5f));
            Assert.That(rect.yMax, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void RectOf_OffTheRow_Throws()
        {
            Assert.That(() => Row().RectOf(9), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void IndexAt_TheCentreOfAButton_IsThatButton()
        {
            var layout = Row();

            for (var i = 0; i < 9; i++)
                Assert.That(layout.IndexAt(layout.RectOf(i).center), Is.EqualTo(i));
        }

        /// <summary>
        /// The gaps are dead space on purpose: an aim that lands between two
        /// buttons should hit neither, rather than being rounded into whichever
        /// is nearer.
        /// </summary>
        [Test]
        public void IndexAt_BetweenTwoButtons_IsNeither()
        {
            var layout = Row();
            var between = new Vector2((layout.RectOf(0).xMax + layout.RectOf(1).xMin) * 0.5f, 0f);

            Assert.That(layout.IndexAt(between), Is.EqualTo(-1));
        }

        [Test]
        public void IndexAt_AboveTheRow_IsNoButton()
        {
            Assert.That(Row().IndexAt(new Vector2(0f, 3f)), Is.EqualTo(-1));
        }

        [Test]
        public void IndexAt_PastTheEnd_IsNoButton()
        {
            Assert.That(Row().IndexAt(new Vector2(6f, 0f)), Is.EqualTo(-1));
        }

        [Test]
        public void IndexAt_OnAnEmptyRow_IsNoButton()
        {
            Assert.That(new ButtonRowLayout(10f, 2f, 0, 1f).IndexAt(Vector2.zero), Is.EqualTo(-1));
        }

        [Test]
        public void Constructor_WithANegativeGap_Throws()
        {
            Assert.That(
                () => new ButtonRowLayout(10f, 2f, 3, -1f),
                Throws.InstanceOf<System.ArgumentOutOfRangeException>());
        }
    }
}
