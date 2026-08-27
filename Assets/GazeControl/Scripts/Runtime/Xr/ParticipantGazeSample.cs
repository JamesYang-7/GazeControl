using UnityEngine;

namespace GazeControl.Xr
{
    /// <summary>Eye-tracker health for one sample.</summary>
    public enum ParticipantGazeStatus
    {
        /// <summary>No tracker answered at all — no headset, no permission, or the plugin is absent.</summary>
        Unavailable = 0,

        /// <summary>The tracker is running but has no gaze: headset off the head, or eyes not located.</summary>
        Invalid = 1,

        /// <summary>Tracking, but still settling (Varjo's "adjust").</summary>
        Adjusting = 2,

        /// <summary>Usable gaze.</summary>
        Valid = 3,
    }

    /// <summary>Per-eye tracking quality, mirroring Varjo's four-way status.</summary>
    public enum ParticipantEyeStatus
    {
        /// <summary>Not tracked and not visible — typically a closed eye.</summary>
        Invalid = 0,

        /// <summary>Visible but not reliably tracked, e.g. mid-saccade or mid-blink.</summary>
        Visible = 1,

        /// <summary>Tracked with compromised quality, e.g. the headset moved after calibration.</summary>
        Compensated = 2,

        /// <summary>Tracked.</summary>
        Tracked = 3,
    }

    /// <summary>
    /// One reading from the participant's eye tracker, device-agnostic.
    ///
    /// <para>Rays are <b>head-relative</b>, exactly as the tracker reports them —
    /// the same convention Varjo's own sample uses, where world space is reached
    /// through the XR camera's <c>TransformPoint</c>/<c>TransformDirection</c>.
    /// Keeping them head-relative here means a source never needs to know where
    /// the camera is, and the logger converts once.</para>
    /// </summary>
    public struct ParticipantGazeSample
    {
        public ParticipantGazeStatus Status { get; set; }

        public ParticipantEyeStatus LeftStatus { get; set; }

        public ParticipantEyeStatus RightStatus { get; set; }

        /// <summary>The tracker's own frame counter — makes decimation to the decision grid auditable.</summary>
        public long DeviceFrame { get; set; }

        /// <summary>When the eye cameras caught this frame, in the device's nanosecond clock.</summary>
        public long CaptureTimeNanoseconds { get; set; }

        /// <summary>Combined gaze ray origin, head-relative, metres.</summary>
        public Vector3 Origin { get; set; }

        /// <summary>Combined gaze direction, head-relative, normalised.</summary>
        public Vector3 Forward { get; set; }

        public Vector3 LeftOrigin { get; set; }

        public Vector3 LeftForward { get; set; }

        public Vector3 RightOrigin { get; set; }

        public Vector3 RightForward { get; set; }

        /// <summary>Distance to the estimated fixation point, metres.</summary>
        public float FocusDistance { get; set; }

        public float FocusStability { get; set; }

        public float InterPupillaryDistanceMm { get; set; }

        public float LeftPupilDiameterMm { get; set; }

        public float RightPupilDiameterMm { get; set; }

        /// <summary>0 = shut, 1 = fully open. The nearest thing to a blink signal the tracker offers.</summary>
        public float LeftEyeOpenness { get; set; }

        public float RightEyeOpenness { get; set; }

        /// <summary>True when the combined ray may be used geometrically.</summary>
        public readonly bool HasGaze => Status == ParticipantGazeStatus.Valid && Forward != Vector3.zero;

        /// <summary>A sample from a tracker that did not answer.</summary>
        public static ParticipantGazeSample Unavailable => new() { Status = ParticipantGazeStatus.Unavailable };
    }
}
