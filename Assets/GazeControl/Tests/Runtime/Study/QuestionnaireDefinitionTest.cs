using System.IO;
using NUnit.Framework;

namespace GazeControl.Study
{
    [TestFixture]
    public class QuestionnaireDefinitionTest
    {
        [Test]
        public void Parse_OnAValidDocument_ReadsEveryItem()
        {
            var actual = QuestionnaireDefinition.Parse(QuestionnaireFixture.ValidJson);

            Assert.That(actual.perClipItems, Has.Length.EqualTo(2));
        }

        [Test]
        public void Parse_OnAValidDocument_KeepsTheScaleBounds()
        {
            var actual = QuestionnaireDefinition.Parse(QuestionnaireFixture.ValidJson).scale;

            Assert.That(actual.PointCount, Is.EqualTo(7));
        }

        [Test]
        public void Parse_OnAnOlderSchema_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("gazecontrol.questionnaire/1", "gazecontrol.questionnaire/0");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_WithNoItems_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("\"perClipItems\"", "\"unusedItems\"");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_WithAnItemCodeUsedTwice_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("\"code\": \"T2\"", "\"code\": \"N1\"");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_WithAnItemMissingItsText_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("\"text\": \"Item two.\"", "\"text\": \"\"");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_WithAnInvertedScale_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("\"min\": 1, \"max\": 7", "\"min\": 7, \"max\": 1");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_WithNoFramingText_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("\"body\": \"Framing body.\"", "\"body\": \"\"");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_WithAVersionLabelThatCannotTakeThePosition_ThrowsInvalidDataException()
        {
            var json = QuestionnaireFixture.JsonWith("\"versionLabelFormat\": \"Version {0}\"",
                "\"versionLabelFormat\": \"Version\"");

            Assert.That(() => QuestionnaireDefinition.Parse(json), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Parse_OnEmptyText_ThrowsInvalidDataException()
        {
            Assert.That(() => QuestionnaireDefinition.Parse("{}"), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void FindItem_ForACodeTheInstrumentDoesNotCarry_ReturnsNull()
        {
            var sut = QuestionnaireFixture.Definition();

            Assert.That(sut.FindItem("Z9"), Is.Null);
        }

        [Test]
        public void LabelFor_ComposesThePositionIntoTheVersionLabel()
        {
            var sut = QuestionnaireFixture.Definition().ranking;

            Assert.That(sut.LabelFor(2), Is.EqualTo("Version 2"));
        }

        [Test]
        public void Contains_ForAResponseOffTheScale_ReturnsFalse()
        {
            var sut = QuestionnaireFixture.Definition().scale;

            Assert.That(sut.Contains(8), Is.False);
        }

        /// <summary>
        /// The one test that reads the committed instrument. It guards the four
        /// item codes the analysis plan of §8 is written around: renaming one in
        /// the JSON would leave every response file unreadable by the analysis
        /// with nothing else failing.
        /// </summary>
        [Test]
        public void LoadDefault_ReadsTheCommittedInstrument()
        {
            var actual = QuestionnaireDefinition.LoadDefault();

            Assert.That(actual.perClipItems, Has.Length.EqualTo(4), "four per-clip items");
            Assert.That(actual.FindItem("N1"), Is.Not.Null, "N1 gaze naturalness");
            Assert.That(actual.FindItem("T2"), Is.Not.Null, "T2 boundary gaze quality");
            Assert.That(actual.FindItem("A1"), Is.Not.Null, "A1 mutual engagement");
            Assert.That(actual.FindItem("I1"), Is.Not.Null, "I1 inclusion");
            Assert.That(actual.scale.PointCount, Is.EqualTo(7), "7-point scale");
        }
    }
}
