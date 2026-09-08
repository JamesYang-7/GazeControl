using System;
using System.IO;
using UnityEngine;

namespace GazeControl.Study
{
    /// <summary>
    /// The study's instrument as data: the framing passage, the four per-clip
    /// Likert items, the per-block ranking and free-text probe, and the closing
    /// passage.
    ///
    /// <para>The wording is <c>Assets/Docs/user-study-design.md</c> §6, settled
    /// 2026-08-21. That document stays the human-readable rationale — constructs,
    /// predictions, which items were cut and why — and this file is what gets
    /// rendered. Copying the wording once into data rather than into a screen
    /// prefab or a C# constant is what lets the VR canvas, the operator panel and
    /// the standalone preview (<c>Tools/render_questionnaire.py</c>) all show the
    /// same question.</para>
    ///
    /// <para>Everything here is loaded, not decided at runtime: an instrument that
    /// changed between participants would silently ruin a within-subjects study,
    /// so the file is committed and the schema tag is checked on load.</para>
    /// </summary>
    [Serializable]
    public sealed class QuestionnaireDefinition
    {
        /// <summary>Schema this loader understands; bumped if the shape changes incompatibly.</summary>
        public const string ExpectedSchema = "gazecontrol.questionnaire/1";

        const string DefaultResourcePath = "Questionnaire";

        public string schema;

        /// <summary>Where the wording came from, for whoever opens the file first.</summary>
        public string source;

        public Scale scale;
        public Passage framing;

        /// <summary>
        /// Heading over the four Likert items, so the participant can tell a
        /// rating screen from the framing, the ranking and the close at a
        /// glance. Optional: an instrument without it shows the items alone,
        /// which is what every instrument did before 2026-09-02.
        /// </summary>
        public string ratingTitle;

        /// <summary>
        /// Heading over the ranking and free-text probe, which the participant
        /// reads as one page of two numbered questions. Optional, like
        /// <see cref="ratingTitle"/>.
        /// </summary>
        public string shortAnswerTitle;

        /// <summary>
        /// Foot of the short-answer page, composed with the 1-based group about
        /// to be watched — "Next: group 3". It is the participant's only sense of
        /// where they are in a session of five, and the page it sits on is the
        /// one they read just before a group starts. Optional; nothing is shown
        /// after the last group, which is followed by the closing passage rather
        /// than by another one.
        /// </summary>
        public string nextGroupFormat;

        public Item[] perClipItems;
        public RankingQuestion ranking;
        public CommentQuestion comment;
        public Passage closing;

        /// <summary>Load the committed instrument from <c>Resources</c>.</summary>
        public static QuestionnaireDefinition LoadDefault()
        {
            var asset = Resources.Load<TextAsset>(DefaultResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Questionnaire not found at Resources/{DefaultResourcePath}.json. " +
                    "It is a committed file — restore it rather than regenerating it.");
            }

            return Parse(asset.text);
        }

        /// <summary>Parse and validate an instrument document.</summary>
        /// <exception cref="InvalidDataException">The document is not a usable instrument.</exception>
        public static QuestionnaireDefinition Parse(string json)
        {
            // JsonUtility fills what it recognises and leaves the rest at its
            // default rather than complaining, so a typo'd or truncated file
            // parses into a half-empty object. Validate() is the only thing
            // standing between that and a participant seeing a blank screen.
            var definition = JsonUtility.FromJson<QuestionnaireDefinition>(json);
            if (definition == null)
                throw new InvalidDataException("questionnaire is not valid JSON");

            definition.Validate();
            return definition;
        }

        /// <summary>
        /// The foot of the short-answer page, or empty when there is no group
        /// after this one. <c>{0}</c> is the group about to play and <c>{1}</c>
        /// how many there are, which is the session's number rather than the
        /// instrument's: study 1 ran five groups and study 2 runs seven on the
        /// same items.
        /// </summary>
        public string NextGroupText(int groupNumber, int groupCount) =>
            groupNumber <= 0 || string.IsNullOrWhiteSpace(nextGroupFormat)
                ? string.Empty
                : string.Format(nextGroupFormat, groupNumber, groupCount);

        /// <summary>
        /// The framing passage's body with the session's shape filled in:
        /// <c>{0}</c> groups of <c>{1}</c> versions. A body with no placeholders
        /// is returned as it is.
        /// </summary>
        public string FramingBody(int groupCount, int versionsPerGroup) =>
            framing == null || string.IsNullOrEmpty(framing.body)
                ? string.Empty
                : string.Format(framing.body, groupCount, versionsPerGroup);

        /// <summary>The item with this code, or null.</summary>
        public Item FindItem(string code)
        {
            if (perClipItems == null)
                return null;

            foreach (var item in perClipItems)
            {
                if (item != null && item.code == code)
                    return item;
            }

            return null;
        }

        void Validate()
        {
            if (schema != ExpectedSchema)
                throw new InvalidDataException($"questionnaire schema is '{schema}', expected '{ExpectedSchema}'");

            if (scale == null || scale.min >= scale.max)
                throw new InvalidDataException("questionnaire has no usable response scale");

            // Composed at render time, on the screen the participant is reading,
            // so a stray brace would throw with the headset already on. Compose it
            // once here instead, where the failure is a refused session.
            try
            {
                _ = scale.RangeHintText;
            }
            catch (FormatException e)
            {
                throw new InvalidDataException($"scale rangeHint is not a usable format string: {e.Message}");
            }

            try
            {
                _ = NextGroupText(1, 1);
            }
            catch (FormatException e)
            {
                throw new InvalidDataException($"nextGroupFormat is not a usable format string: {e.Message}");
            }

            RequirePassage(framing, nameof(framing));

            try
            {
                _ = FramingBody(1, 2);
            }
            catch (FormatException e)
            {
                throw new InvalidDataException($"the framing body is not a usable format string: {e.Message}");
            }
            RequirePassage(closing, nameof(closing));

            if (perClipItems == null || perClipItems.Length == 0)
                throw new InvalidDataException("questionnaire has no per-clip items");

            for (var i = 0; i < perClipItems.Length; i++)
            {
                var item = perClipItems[i];
                if (item == null || string.IsNullOrWhiteSpace(item.code) || string.IsNullOrWhiteSpace(item.text))
                    throw new InvalidDataException($"per-clip item {i} has no code or no text");

                for (var j = 0; j < i; j++)
                {
                    if (perClipItems[j].code == item.code)
                        throw new InvalidDataException($"per-clip item code '{item.code}' appears twice");
                }
            }

            if (ranking == null || string.IsNullOrWhiteSpace(ranking.code) ||
                string.IsNullOrWhiteSpace(ranking.prompt))
            {
                throw new InvalidDataException("questionnaire has no ranking question");
            }

            // The participant's only handle on the three versions is the order
            // they were shown in, so a label that cannot take the position is a
            // screen that cannot be answered.
            if (ranking.versionLabelFormat == null || !ranking.versionLabelFormat.Contains("{0}"))
                throw new InvalidDataException("ranking versionLabelFormat must contain '{0}'");

            if (comment == null || string.IsNullOrWhiteSpace(comment.code) ||
                string.IsNullOrWhiteSpace(comment.prompt))
            {
                throw new InvalidDataException("questionnaire has no free-text question");
            }
        }

        static void RequirePassage(Passage passage, string name)
        {
            if (passage == null || string.IsNullOrWhiteSpace(passage.body))
                throw new InvalidDataException($"questionnaire has no {name} text");
        }

        /// <summary>The response scale shared by every per-clip item.</summary>
        [Serializable]
        public sealed class Scale
        {
            public int min;
            public int max;

            /// <summary>
            /// Leads the rating screen's scale line, composed with this scale's own
            /// bounds so the sentence cannot drift from the scale it describes.
            /// It exists because the anchors alone read as a three-option menu —
            /// participants asked whether the choice was 1, 4 or 7 — and it lives
            /// here rather than anywhere the ranking and free-text page could
            /// reach, since a "1 to 7" would be wrong on it.
            /// Optional: an instrument without it shows the anchors alone.
            /// </summary>
            public string rangeHint;

            public string minLabel;
            public string midLabel;
            public string maxLabel;

            /// <summary>How many points the scale offers.</summary>
            public int PointCount => max - min + 1;

            /// <summary>The range sentence, or empty when the instrument carries none.</summary>
            public string RangeHintText =>
                string.IsNullOrWhiteSpace(rangeHint) ? string.Empty : string.Format(rangeHint, min, max);

            public bool Contains(int response) => response >= min && response <= max;
        }

        /// <summary>A screen of text with no response: the framing and the close.</summary>
        [Serializable]
        public sealed class Passage
        {
            public string title;
            public string body;
        }

        /// <summary>One Likert item, asked after every clip.</summary>
        [Serializable]
        public sealed class Item
        {
            /// <summary>Analysis code (N1, T2, A1, I1). Written to the response file; never shown to the participant.</summary>
            public string code;

            public string text;

            /// <summary>What the item is taken to measure, for the analysis plan. Not shown either.</summary>
            public string construct;
        }

        /// <summary>The per-block ranking — the study's primary dependent variable (§6.3).</summary>
        [Serializable]
        public sealed class RankingQuestion
        {
            public string code;
            public string prompt;
            public string instruction;

            /// <summary>Composed with the 1-based position the version was shown in.</summary>
            public string versionLabelFormat;

            /// <summary>Label for the version shown in this 1-based position.</summary>
            public string LabelFor(int versionPosition) =>
                string.Format(versionLabelFormat, versionPosition);
        }

        /// <summary>The per-block free-text probe (§6.3).</summary>
        [Serializable]
        public sealed class CommentQuestion
        {
            public string code;

            /// <summary>
            /// Carries its own "(optional)" — there is no instruction line beside
            /// it on either surface, because the participant's page shows prompts
            /// alone and a second sentence saying the same thing was one more
            /// line for the operator to read past.
            /// </summary>
            public string prompt;

            public bool optional;
        }
    }
}
