namespace GazeControl.Xr
{
    /// <summary>
    /// Where the participant's eye gaze comes from. An interface rather than a
    /// direct call into the vendor API so the logger can be exercised without a
    /// headset, and so the OpenXR path stays reachable if the loader ever
    /// switches back (see CLAUDE.md — the Varjo plugin was chosen precisely
    /// because <c>XR_EXT_eye_gaze_interaction</c> produced no device).
    /// </summary>
    public interface IParticipantGazeSource
    {
        /// <summary>True when the tracker is present and permitted to report gaze.</summary>
        bool IsAvailable { get; }

        /// <summary>True when this wearer has completed the tracker's own calibration.</summary>
        bool IsCalibrated { get; }

        /// <summary>
        /// Why gaze is missing, in the operator's terms, or null when it is not.
        /// Varjo gates eye tracking behind three separate things — a permission
        /// in Varjo Base, a per-wearer calibration, and the headset being worn —
        /// and a session is lost if the operator has to guess which one failed.
        /// </summary>
        string UnavailableReason { get; }

        /// <summary>Read the most recent sample. Never throws; reports failure through the sample's status.</summary>
        ParticipantGazeSample Read();
    }
}
