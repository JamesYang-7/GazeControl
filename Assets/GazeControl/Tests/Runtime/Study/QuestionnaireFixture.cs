namespace GazeControl.Study
{
    /// <summary>
    /// A complete, valid instrument document for the tests to mutate.
    ///
    /// Deliberately not the committed <c>Questionnaire.json</c>: a test that
    /// edits the real instrument to check a validation rule would be one typo
    /// away from changing what participants are asked. Only
    /// <see cref="QuestionnaireDefinitionTest.LoadDefault_ReadsTheCommittedInstrument"/>
    /// touches the real file, and it only reads it.
    /// </summary>
    static class QuestionnaireFixture
    {
        public const string ValidJson = @"{
  ""schema"": ""gazecontrol.questionnaire/1"",
  ""source"": ""fixture"",
  ""scale"": { ""min"": 1, ""max"": 7, ""minLabel"": ""low"", ""midLabel"": ""mid"", ""maxLabel"": ""high"" },
  ""framing"": { ""title"": ""Before"", ""body"": ""Framing body."" },
  ""perClipItems"": [
    { ""code"": ""N1"", ""text"": ""Item one."", ""construct"": ""One"" },
    { ""code"": ""T2"", ""text"": ""Item two."", ""construct"": ""Two"" }
  ],
  ""ranking"": {
    ""code"": ""R1"",
    ""prompt"": ""Rank them."",
    ""instruction"": ""No ties."",
    ""versionLabelFormat"": ""Version {0}""
  },
  ""comment"": { ""code"": ""D1"", ""prompt"": ""Anything odd? (optional)"", ""optional"": true },
  ""closing"": { ""title"": ""Done"", ""body"": ""Closing body."" }
}";

        public static QuestionnaireDefinition Definition() => QuestionnaireDefinition.Parse(ValidJson);

        /// <summary>The fixture with one substring swapped, to break exactly one rule.</summary>
        public static string JsonWith(string find, string replaceWith) => ValidJson.Replace(find, replaceWith);
    }
}
