using System;
using UnityEngine;
#if VARJO_XR
using Varjo.XR;
#endif

namespace GazeControl.Xr
{
    /// <summary>
    /// The participant's gaze, read from the Varjo Unity XR plugin.
    ///
    /// <para>This is the vendor path on purpose. OpenXR's
    /// <c>XR_EXT_eye_gaze_interaction</c> was available and enabled on the
    /// instance yet never produced an eye-tracking device on this headset, and
    /// the vendor API gives more than the extension would anyway: per-eye rays,
    /// pupil diameters and a focus distance rather than one combined ray
    /// (CLAUDE.md, "XR — the study rig").</para>
    /// </summary>
    public sealed class VarjoGazeSource : IParticipantGazeSource
    {
        ParticipantGazeSample _last = ParticipantGazeSample.Unavailable;
        string _nativeFailure;

        /// <inheritdoc/>
        public bool IsAvailable => UnavailableReason == null;

        /// <inheritdoc/>
        public bool IsCalibrated
        {
            get
            {
#if VARJO_XR
                return Guarded(VarjoEyeTracking.IsGazeCalibrated);
#else
                return false;
#endif
            }
        }

        /// <inheritdoc/>
        public string UnavailableReason
        {
            get
            {
#if !VARJO_XR
                return "the Varjo XR plugin (com.varjo.xr) is not in this project.";
#else
                if (_nativeFailure != null)
                    return _nativeFailure;

                if (!Guarded(VarjoEyeTracking.IsGazeAllowed))
                    return "Varjo Base has not granted this application permission to use eye tracking.";

                if (!Guarded(VarjoEyeTracking.IsGazeAvailable))
                    return "the tracker reports no gaze — the headset is most likely off the head.";

                if (Guarded(VarjoEyeTracking.IsGazeCalibrating))
                    return "the wearer's eye-tracking calibration is still running.";

                if (!Guarded(VarjoEyeTracking.IsGazeCalibrated))
                    return "this wearer has not completed the headset's eye-tracking calibration " +
                           "(run it from the participant rig, or from Varjo Base, before the first trial).";

                return null;
#endif
            }
        }

        /// <inheritdoc/>
        public ParticipantGazeSample Read()
        {
#if !VARJO_XR
            return ParticipantGazeSample.Unavailable;
#else
            if (_nativeFailure != null)
                return ParticipantGazeSample.Unavailable;

            try
            {
                // GetGazeList, not GetGaze + GetEyeMeasurements: those are two
                // separate native fetches and can straddle a tracker frame, so
                // the pupil sizes would not always belong to the ray logged
                // beside them. The list call fetches once and returns pairs that
                // do belong together.
                var count = VarjoEyeTracking.GetGazeList(out var gaze, out var measurements);
                if (count == 0)
                {
                    // The tracker runs at 100-200 Hz against a 60 Hz decision
                    // grid, so an empty queue means only that no new tracker
                    // frame landed inside this tick. Hold the last sample; its
                    // unchanged DeviceFrame is what tells the analysis that the
                    // row repeats rather than resamples.
                    return _last;
                }

                _last = Convert(gaze[^1], measurements[^1]);
                return _last;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                _nativeFailure =
                    "the Varjo native library did not load — is the Varjo loader active and started? " +
                    $"({e.GetType().Name})";
                Debug.LogError($"Participant gaze: {_nativeFailure}");
                return ParticipantGazeSample.Unavailable;
            }
#endif
        }

#if VARJO_XR
        static ParticipantGazeSample Convert(
            in VarjoEyeTracking.GazeData gaze, in VarjoEyeTracking.EyeMeasurements measurements) => new()
        {
            Status = gaze.status switch
            {
                VarjoEyeTracking.GazeStatus.Valid => ParticipantGazeStatus.Valid,
                VarjoEyeTracking.GazeStatus.Adjust => ParticipantGazeStatus.Adjusting,
                _ => ParticipantGazeStatus.Invalid,
            },

            // The two enums are declared with the same four values in the same
            // order, and the test asserts that so a plugin update cannot silently
            // renumber them under this cast.
            LeftStatus = (ParticipantEyeStatus)gaze.leftStatus,
            RightStatus = (ParticipantEyeStatus)gaze.rightStatus,

            DeviceFrame = gaze.frameNumber,
            CaptureTimeNanoseconds = gaze.captureTime,
            Origin = gaze.gaze.origin,
            Forward = gaze.gaze.forward,
            LeftOrigin = gaze.left.origin,
            LeftForward = gaze.left.forward,
            RightOrigin = gaze.right.origin,
            RightForward = gaze.right.forward,
            FocusDistance = gaze.focusDistance,
            FocusStability = gaze.focusStability,
            InterPupillaryDistanceMm = measurements.interPupillaryDistanceInMM,
            LeftPupilDiameterMm = measurements.leftPupilDiameterInMM,
            RightPupilDiameterMm = measurements.rightPupilDiameterInMM,
            LeftEyeOpenness = measurements.leftEyeOpenness,
            RightEyeOpenness = measurements.rightEyeOpenness,
        };

        /// <summary>
        /// The plugin's native library is only loaded once the Varjo loader has
        /// started, so every one of these calls throws on a desktop Play. The
        /// logger will not poll before XR is running, but a mis-wired scene must
        /// degrade to "no gaze" rather than throw once per tick for a whole take.
        /// </summary>
        static bool Guarded(Func<bool> call)
        {
            try
            {
                return call();
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
#endif
    }
}
