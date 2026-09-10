using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class XrFeatureControlNameTest
    {
        [Test]
        public void For_APlainFeature_IsTheNameLowered()
        {
            Assert.That(XrFeatureControlName.For("Trigger"), Is.EqualTo("trigger"));
        }

        /// <summary>The case that started this: a wand's click arrives under a name with a space in it.</summary>
        [Test]
        public void For_ASpacedName_ClosesUpIntoOneControl()
        {
            Assert.That(XrFeatureControlName.For("Trigger Button"), Is.EqualTo("triggerbutton"));
        }

        [Test]
        public void For_PunctuationAndDashes_AreDropped()
        {
            Assert.That(XrFeatureControlName.For("Device - Pose"), Is.EqualTo("devicepose"));
        }

        /// <summary>A pose's parts are named through a path, and the path is what groups them.</summary>
        [Test]
        public void For_APath_KeepsItsSeparator()
        {
            Assert.That(XrFeatureControlName.For("Device - Pose/Position"), Is.EqualTo("devicepose/position"));
        }

        [Test]
        public void For_AnUnderscore_Survives()
        {
            Assert.That(XrFeatureControlName.For("Trigger_2"), Is.EqualTo("trigger_2"));
        }

        [Test]
        public void For_NothingButPunctuationAroundAPath_KeepsOnlyTheSeparator()
        {
            Assert.That(XrFeatureControlName.For("- / -"), Is.EqualTo("/"));
        }

        [Test]
        public void For_NoName_IsEmpty()
        {
            Assert.That(XrFeatureControlName.For(null), Is.Empty);
            Assert.That(XrFeatureControlName.For(string.Empty), Is.Empty);
        }
    }
}
