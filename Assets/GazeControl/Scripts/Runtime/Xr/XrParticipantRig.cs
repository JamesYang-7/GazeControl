using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace GazeControl.Xr
{
    /// <summary>
    /// The participant's play area: the standing rig the human user occupies at
    /// the third triad vertex, and the switch between running the scene in a
    /// headset and running it flat on the desktop.
    ///
    /// <para>The rig is three levels — this component sits on the vertex object,
    /// whose child <see cref="CameraOffset"/> carries the eye-height calibration,
    /// whose child <see cref="HeadCamera"/> is driven by the headset. Nothing
    /// else in the scene changes: the agents already aim at the user's
    /// <c>LookAtAnchor</c>, which is that camera, so in a headset they aim at the
    /// participant's real head — which is the whole point of running the study
    /// live rather than on video (`user-study-design.md` §4).</para>
    ///
    /// <para><b>XR is opt-in, and off by default.</b> "Initialize XR on Startup"
    /// is deliberately unchecked in XR Plug-in Management: baking gaze tracks,
    /// browsing clips and recording demo videos all press Play on a machine that
    /// may have no headset attached, and auto-initialisation would make every one
    /// of those workflows fail or stall waiting for a runtime. This component
    /// starts the subsystems only when asked, and parks the camera at
    /// <see cref="FallbackEyeHeight"/> when it has not, so a flat Play renders
    /// from exactly where the camera has always been.</para>
    /// </summary>
    public sealed class XrParticipantRig : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Child transform carrying the eye-height calibration; the camera hangs under it")]
        public Transform CameraOffset { get; set; }

        [field: SerializeField]
        [field: Tooltip("The participant's eye camera — the same camera the agents look at")]
        public Camera HeadCamera { get; set; }

        [field: SerializeField]
        [field: Tooltip("Eye height every participant is calibrated to, in metres")]
        public float ReferenceEyeHeight { get; set; } = EyeHeightCalibration.DefaultReferenceEyeHeight;

        [field: SerializeField]
        [field: Tooltip("Where the camera sits with no headset running, in metres above the floor")]
        public float FallbackEyeHeight { get; set; } = EyeHeightCalibration.DefaultReferenceEyeHeight;

        [field: SerializeField]
        [field: Tooltip("Start the headset when the scene plays; leave off for desktop workflows (baking, clip browsing, recording)")]
        public bool StartXrOnPlay { get; set; }

        bool _startedHere;

        /// <summary>True while the XR subsystems this rig started are running.</summary>
        public bool IsXrRunning =>
            XRGeneralSettings.Instance != null &&
            XRGeneralSettings.Instance.Manager != null &&
            XRGeneralSettings.Instance.Manager.isInitializationComplete;

        /// <summary>The last calibration sample, for the participant's record.</summary>
        public EyeHeightCalibration.Result LastCalibration { get; private set; }

        void Awake()
        {
            ParkFlat();
        }

        void Start()
        {
            if (StartXrOnPlay)
                StartXr();
        }

        void OnDestroy()
        {
            if (_startedHere)
                StopXr();
        }

        /// <summary>
        /// Bring up the headset. Returns false (after logging) when no OpenXR
        /// runtime answers, leaving the scene in flat mode rather than half
        /// initialised.
        /// </summary>
        public bool StartXr()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null)
            {
                Debug.LogError($"{name}: no XR settings for this build target — check Project Settings → XR Plug-in Management.", this);
                return false;
            }

            if (settings.Manager.isInitializationComplete)
                return true;

            settings.Manager.InitializeLoaderSync();
            if (settings.Manager.activeLoader == null)
            {
                Debug.LogError(
                    $"{name}: no OpenXR runtime answered. Is the headset connected and is Varjo Base " +
                    "(or whichever runtime is set as the active OpenXR runtime) running? Staying in flat mode.", this);
                return false;
            }

            settings.Manager.StartSubsystems();
            _startedHere = true;

            // The editor's player loop stalls while the editor is unfocused, and
            // an operator running a session clicks away from the Game view
            // constantly — the participant would see a frozen scene. The
            // PlayerSettings flag does not govern editor play and the runtime one
            // resets on exit, so it is set here, every session (see CLAUDE.md).
            Application.runInBackground = true;

            SetStandingPlayArea();
            return true;
        }

        /// <summary>Shut the headset down and return the camera to its flat pose.</summary>
        public void StopXr()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null || !settings.Manager.isInitializationComplete)
                return;

            settings.Manager.StopSubsystems();
            settings.Manager.DeinitializeLoader();
            _startedHere = false;
            ParkFlat();
        }

        /// <summary>
        /// Sample the participant's standing eye height and move the play area so
        /// that it reads as <see cref="ReferenceEyeHeight"/>. Call it with the
        /// participant standing upright and looking ahead.
        /// </summary>
        [ContextMenu("Calibrate Eye Height")]
        public EyeHeightCalibration.Result Calibrate()
        {
            if (HeadCamera == null || CameraOffset == null)
            {
                Debug.LogError($"{name}: the rig needs both a CameraOffset and a HeadCamera to calibrate.", this);
                return default;
            }

            // Measured against the rig root, which stands on the floor, and with
            // any previous calibration subtracted back out — so a second
            // calibration measures the participant again rather than measuring
            // the first calibration's result.
            var appliedOffset = CameraOffset.localPosition.y;
            var measured = transform.InverseTransformPoint(HeadCamera.transform.position).y - appliedOffset;

            var result = EyeHeightCalibration.Measure(measured, ReferenceEyeHeight);
            LastCalibration = result;

            if (!result.Accepted)
            {
                Debug.LogWarning($"{name}: eye-height calibration rejected — {result.Reason} The rig has not moved.", this);
                return result;
            }

            var local = CameraOffset.localPosition;
            CameraOffset.localPosition = new Vector3(local.x, result.Offset, local.z);
            Debug.Log(
                $"{name}: eye height measured {result.MeasuredEyeHeight:F3} m, " +
                $"play area offset {result.Offset:+0.000;-0.000} m to sit the participant at {ReferenceEyeHeight:F2} m.", this);

            return result;
        }

        /// <summary>
        /// Standing play area: poses are reported from the floor, so the
        /// participant's real height is the camera's height and the calibration
        /// above is the only thing that changes it. Seated ("Device") origins
        /// would put the eyes wherever the headset happened to be at startup.
        /// </summary>
        void SetStandingPlayArea()
        {
            var subsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);

            foreach (var subsystem in subsystems)
            {
                if (!subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                    Debug.LogWarning(
                        $"{name}: the runtime refused a floor-relative tracking origin, so eye height will be " +
                        "measured from wherever the headset started. Check the room-scale boundary setup.", this);
            }
        }

        /// <summary>
        /// Put the camera where it sits with no headset: straight above the
        /// vertex at <see cref="FallbackEyeHeight"/>. Every desktop workflow —
        /// baking, the Clip Browser, Record Demo — renders from here, and it must
        /// stay the pose those recordings have always used.
        /// </summary>
        void ParkFlat()
        {
            if (CameraOffset == null || HeadCamera == null)
                return;

            CameraOffset.localPosition = Vector3.zero;
            CameraOffset.localRotation = Quaternion.identity;
            HeadCamera.transform.localPosition = new Vector3(0f, FallbackEyeHeight, 0f);
            HeadCamera.transform.localRotation = Quaternion.identity;
        }
    }
}
