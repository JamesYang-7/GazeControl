#if UNITY_EDITOR
using UnityEditor;

namespace GazeControl.Experiment
{
    /// <summary>
    /// One participant's session, requested by the operator's Start Session
    /// window and consumed by <see cref="StudySessionRunner"/> as play begins.
    ///
    /// <para><b>Why a request rather than scene edits.</b> Starting a real
    /// recording used to mean setting six fields across three components by hand
    /// — the participant label, <c>StudySession</c>, <c>Tracks</c>, logging, the
    /// session runner's enabled box and the rig's headset switch — and then
    /// remembering to put every one of them back afterwards. Editing them in Edit
    /// Mode leaves the committed scene dirty in study state, which the next bake
    /// or demo recording then inherits. Applying them at play start instead means
    /// the scene asset is never touched at all: the changes live only in the play
    /// session and are gone when it exits.</para>
    ///
    /// <para>Held in <see cref="SessionState"/>, which survives a domain reload
    /// but not an editor restart — exactly the lifetime of "I pressed Start
    /// Session a moment ago". It is consumed once, so exiting play and pressing
    /// Play again by hand gives an ordinary development run rather than a second,
    /// unwanted session under the same label.</para>
    /// </summary>
    public static class StudyLaunchRequest
    {
        const string k_ParticipantKey = "GazeControl.StudyLaunch.Participant";
        const string k_PreviewKey = "GazeControl.StudyLaunch.Preview";
        const string k_PreviewHeadsetKey = "GazeControl.StudyLaunch.PreviewHeadset";

        /// <summary>The participant a launch is pending for, or empty when none is.</summary>
        public static string Pending => SessionState.GetString(k_ParticipantKey, string.Empty);

        /// <summary>Ask for the next play to be <paramref name="participant"/>'s session.</summary>
        public static void Set(string participant) =>
            SessionState.SetString(k_ParticipantKey, participant ?? string.Empty);

        /// <summary>Withdraw a pending request.</summary>
        public static void Clear() => SessionState.EraseString(k_ParticipantKey);

        /// <summary>
        /// Take the pending request, if there is one, leaving none behind.
        /// </summary>
        public static bool TryConsume(out string participant)
        {
            participant = Pending;
            if (string.IsNullOrEmpty(participant))
                return false;

            Clear();
            return true;
        }

        /// <summary>
        /// Ask for the next play to be a <b>preview</b>: the session's clip order
        /// with the baked tracks replayed, no questionnaire, no logging, the
        /// developer overlay on, and each clip running straight into the next.
        /// For checking bakes by eye; nothing it shows is a take.
        /// </summary>
        /// <param name="inHeadset">
        /// Start the headset for the preview, to watch the bakes from the seat.
        /// Off by default: a desktop preview is what a bake check needs, and the
        /// Varjo runtime up with nobody wearing it stalls the frame clock.
        /// </param>
        public static void SetPreview(bool inHeadset = false)
        {
            SessionState.SetBool(k_PreviewKey, true);
            SessionState.SetBool(k_PreviewHeadsetKey, inHeadset);
        }

        /// <summary>Take a pending preview request, leaving none behind.</summary>
        /// <param name="inHeadset">Whether the preview asked for the headset.</param>
        public static bool TryConsumePreview(out bool inHeadset)
        {
            inHeadset = false;
            if (!SessionState.GetBool(k_PreviewKey, false))
                return false;

            inHeadset = SessionState.GetBool(k_PreviewHeadsetKey, false);
            SessionState.EraseBool(k_PreviewKey);
            SessionState.EraseBool(k_PreviewHeadsetKey);
            return true;
        }
    }
}
#endif
