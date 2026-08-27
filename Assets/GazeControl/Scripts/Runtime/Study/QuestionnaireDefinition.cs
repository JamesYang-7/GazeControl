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

        /// <summary>Shown on every rating screen, because the participant answers aloud (§5.5).</summary>
        public string spokenAnswerInstruction;

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

            RequirePassage(framing, nameof(framing));
            RequirePassage(closing, nameof(closing));

            if (string.IsNullOrWhiteSpace(spokenAnswerInstruction))
                throw new InvalidDataException("questionnaire has no spoken-answer instruction");

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
            public string minLabel;
            public string midLabel;
            public string maxLabel;

            /// <summary>How many points the scale offers.</summary>
            public int PointCount => max - min + 1;

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
            public string prompt;
            public string instruction;
            public bool optional;
        }
    }
}
