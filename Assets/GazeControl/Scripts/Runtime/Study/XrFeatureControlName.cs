using System.Text;

namespace GazeControl.Study
{
    /// <summary>
    /// The name the Input System gives a control it builds from one XR feature
    /// the runtime reports.
    ///
    /// <para><b>Why this is worth a class of its own</b> (2026-09-10, after a
    /// wand whose trigger read 0.00 for a whole questionnaire screen while the
    /// same wand worked SteamVR's own dashboard). An XR device's controls are
    /// assembled by <c>XRLayoutBuilder</c> out of the features its descriptor
    /// lists, one control per feature under this name; the vendor's layout only
    /// says what type each is and what else it might be called. A control the
    /// layout declares and the descriptor does not back is therefore still
    /// <i>there</i> to be looked up by name, and reads zero however hard the
    /// trigger is pulled. Composing the name here is what lets a caller ask for
    /// the controls the hardware is actually feeding rather than the ones the
    /// layout promises.</para>
    ///
    /// <para>The rule is Unity's, reproduced: everything but letters, digits,
    /// underscores and the path separator is dropped, and what is left is
    /// lowered. It is not public API, so this is a copy that can drift - the
    /// consequence of drift is a trigger that is looked up under the wrong name
    /// and falls back to the layout's, which is where this started.</para>
    /// </summary>
    public static class XrFeatureControlName
    {
        /// <summary>
        /// The control name for a feature, or an empty string where the name is
        /// nothing but punctuation.
        /// </summary>
        public static string For(string featureName)
        {
            if (string.IsNullOrEmpty(featureName))
                return string.Empty;

            var name = new StringBuilder(featureName.Length);
            foreach (var letter in featureName)
            {
                // '/' survives because a pose arrives as several features named
                // through it - "Device - Pose/Position" and its siblings - and
                // the builder keeps that path.
                if (char.IsLetterOrDigit(letter) || letter == '_' || letter == '/')
                    name.Append(char.ToLowerInvariant(letter));
            }

            return name.ToString();
        }
    }
}
