#if UNITY_EDITOR
using UnityEditor;

namespace GazeControl.Demo
{
    /// <summary>
    /// "A recorder is still starting up — do not begin the conversation yet."
    ///
    /// <para>Unity Recorder takes a moment to prepare its encoder, and the
    /// conversation is driven by the frame clock, so a segment that starts while
    /// the encoder is still coming up loses its opening seconds off the front of
    /// the video. <c>RecordedConversation</c> waits on this before scheduling the
    /// voices; a play session with no recording never sets it and so never
    /// waits.</para>
    ///
    /// <para>Shared rather than owned by one recorder, because there are two —
    /// <c>DemoRecorder</c>'s single take and <c>DemoTakeRenderer</c>'s queue of
    /// them — and the hold is the conversation's business, not either
    /// recorder's. It used to be <c>DemoRecorder</c>'s own "pending" key, read by
    /// name from the conversation, which meant the second recorder silently had
    /// no hold at all.</para>
    /// </summary>
    public static class RecordingHold
    {
        /// <summary>The session key; named here so nothing has to spell it out.</summary>
        public const string Key = "GazeControl.Recording.Pending";

        /// <summary>True while a recorder has asked the conversation to wait.</summary>
        public static bool Pending => SessionState.GetBool(Key, false);

        /// <summary>Ask the conversation to wait until the encoder is up.</summary>
        public static void Hold() => SessionState.SetBool(Key, true);

        /// <summary>Let the conversation start.</summary>
        public static void Release() => SessionState.EraseBool(Key);
    }
}
#endif
