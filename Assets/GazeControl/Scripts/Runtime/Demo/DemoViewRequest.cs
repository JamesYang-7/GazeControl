#if UNITY_EDITOR
using UnityEditor;

namespace GazeControl.Demo
{
    /// <summary>
    /// Permission for the play session about to start to render through
    /// <see cref="DemoCamera"/> instead of the participant's camera.
    ///
    /// <para>Asked for by the demo commands — framing preview, and the render
    /// itself — and consumed once by <see cref="DemoCamera"/> as it wakes, the
    /// same shape as <c>StudyLaunchRequest</c> and for the same reason: the
    /// alternative is a box in the scene that someone leaves ticked, and the
    /// thing it would spoil is a participant session, in a headset, where the
    /// demo camera would be drawn over the one the headset needs.</para>
    ///
    /// <para>Consumed rather than merely read, so that pressing Play again by
    /// hand after a preview is an ordinary run.</para>
    /// </summary>
    public static class DemoViewRequest
    {
        const string k_Key = "GazeControl.DemoView.Pending";

        /// <summary>True while a demo view has been asked for and not yet taken.</summary>
        public static bool Pending => SessionState.GetBool(k_Key, false);

        /// <summary>Ask for the next play session to render through the demo camera.</summary>
        public static void Set() => SessionState.SetBool(k_Key, true);

        /// <summary>Withdraw a pending request.</summary>
        public static void Clear() => SessionState.EraseBool(k_Key);

        /// <summary>Take the pending request, leaving none behind.</summary>
        public static bool Consume()
        {
            if (!Pending)
                return false;

            Clear();
            return true;
        }
    }
}
#endif
