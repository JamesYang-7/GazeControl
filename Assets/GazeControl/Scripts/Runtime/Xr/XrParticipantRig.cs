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
    /// whose child <see cref="CameraOffset"/> carries the eye-height offset,
    /// whose child <see cref="HeadCamera"/> is driven by the headset. Nothing
    /// else in the scene changes: the agents already aim at the user's
    /// <c>LookAtAnchor</c>, which is that camera, so in a headset they aim at the
    /// participant's real head — which is the whole point of running the study
    /// live rather than on video (`user-study-design.md` §4).</para>
    ///
    /// <para><b>The viewpoint is a fixed point in the room: head rotation
    /// tracks, head translation does not.</b> The camera sits at the triad vertex
    /// at <see cref="EyeHeight"/> and stays there, so every participant sees the
    /// two agents from the identical position, at the identical distance, on the
    /// identical eye line — height, build and where someone happens to stand all
    /// stop being uncontrolled between-participant variables. There is no
    /// calibration step to run, forget, or take mid-movement.</para>
    ///
    /// <para>It also makes the agents' rendered gaze deterministic. They aim at
    /// the human's <c>LookAtAnchor</c>, which is this camera; with the camera
    /// fixed, that aim is the same for every participant and the same as in a
    /// desktop preview. With positional tracking it varied with whoever was
    /// wearing the headset and how they had drifted.</para>
    ///
    /// <para>The trade is deliberate and it is a real one: <b>no positional
    /// parallax</b>. Swaying or leaning does not move the view, and because the
    /// rotation is applied at the eyes rather than about the neck, turning the
    /// head pivots the world slightly more than a body expects. This is ordinary
    /// 3DOF viewing, tolerated for short passive content, and the task here is to
    /// stand still and watch for 20-30 s. Two consequences worth holding on to:
    /// the pilot should check comfort explicitly, and a participant who walks
    /// gets no visual feedback that they have — so the operator stays in the
    /// room. If parallax is ever wanted back, a neck-pivot model (rotate the eyes
    /// about a point below and behind them) restores most of it while keeping the
    /// geometry identical across participants, since it depends only on head
    /// rotation and not on the body wearing the headset.</para>
    ///
    /// <para><b>XR is opt-in, and off by default.</b> "Initialize XR on Startup"
    /// is deliberately unchecked in XR Plug-in Management: baking gaze tracks,
    /// browsing clips and recording demo videos all press Play on a machine that
    /// may have no headset attached, and auto-initialisation would make every one
    /// of those workflows fail or stall waiting for a runtime. This component
    /// starts the subsystems only when asked. The camera sits in the same place
    /// either way, so a desktop preview and a worn take share one geometry
    /// exactly.</para>
    /// </summary>
    // Early, so the eye height is pinned before GazeController's LateUpdate aims
    // the agents' eyes at this camera. Component order is otherwise arbitrary and
    // the agents would spend every frame aiming one frame behind.
    [DefaultExecutionOrder(-100)]
    public sealed class XrParticipantRig : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Child transform carrying the eye-height calibration; the camera hangs under it")]
        public Transform CameraOffset { get; set; }

        [field: SerializeField]
        [field: Tooltip("The participant's eye camera — the same camera the agents look at")]
        public Camera HeadCamera { get; set; }

        [field: SerializeField]
        [field: Tooltip("Height the participant's view is held at, metres above the floor. " +
                        "Defaults to the SMPL-X agents' own eye line; their heads bob a few cm under mocap, " +
                        "so tune this if you would rather sit on the mean of that motion.")]
        public float EyeHeight { get; set; } = ParticipantEyeHeight.SmplxEyeHeight;

        [field: SerializeField]
        [field: Tooltip("Start the headset when the scene plays; leave off for desktop workflows (baking, clip browsing, recording)")]
        public bool StartXrOnPlay { get; set; }

        [field: SerializeField]
        [field: Tooltip("Turn the play area so the participant's facing points between the two agents, " +
                        "as soon as the headset reports a pose. Recentre again by hand if they were not " +
                        "looking ahead at that moment.")]
        public bool RecentreOnXrStart { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Keep Unity's audio on Windows' default output when the headset starts and stops. The XR " +
                        "runtime hands the audio engine the headset's virtual audio device, which on this Varjo " +
                        "has no speakers, and the switch back on stop fails and leaves the editor silent.")]
        public bool KeepSystemAudioOutput { get; set; } = true;

        /// <summary>How long after XR starts the automatic recentring keeps trying, seconds.</summary>
        const float RecentreWindowSeconds = 15f;

        bool _startedHere;
        bool _recentrePending;
        float _recentreDeadline;

        /// <summary>True while the XR subsystems this rig started are running.</summary>
        public bool IsXrRunning =>
            XRGeneralSettings.Instance != null &&
            XRGeneralSettings.Instance.Manager != null &&
            XRGeneralSettings.Instance.Manager.isInitializationComplete;

        /// <summary>
        /// The participant's own standing eye height as last measured, metres, or
        /// 0 before the first accepted sample. Nothing in the scene depends on it
        /// — the view is held at <see cref="EyeHeight"/> regardless — but it is
        /// the one thing the rig knows about the participant's body, and it
        /// belongs in their record.
        /// </summary>
        public float MeasuredEyeHeight { get; private set; }

        /// <summary>Why the last sample was rejected, or null when it was accepted.</summary>
        public string LastRejection { get; private set; }

        /// <summary>
        /// Degrees the play area is turned by to point the participant between
        /// the agents. Worth having in the participant's record: a large value
        /// means they were set up facing well off the rig's forward.
        /// </summary>
        public float ViewYawOffset { get; private set; }

        /// <summary>True once the automatic recentring has found a head pose to use.</summary>
        public bool HasRecentred { get; private set; }

        void Awake()
        {
            PlaceViewpoint();
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
        /// Bring up the headset. Returns false (after logging) when no XR loader
        /// answers, leaving the scene in flat mode rather than half initialised.
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
                    $"{name}: no XR loader answered. Is the headset connected and is Varjo Base running, " +
                    "and is the Varjo loader ticked in XR Plug-in Management? Staying in flat mode.", this);
                return false;
            }

            settings.Manager.StartSubsystems();
            _startedHere = true;
            RebindAudioToSystemOutput("started");

            // The editor's player loop stalls while the editor is unfocused, and
            // an operator running a session clicks away from the Game view
            // constantly — the participant would see a frozen scene. The
            // PlayerSettings flag does not govern editor play and the runtime one
            // resets on exit, so it is set here, every session (see CLAUDE.md).
            Application.runInBackground = true;

            SetStandingPlayArea();

            // Armed rather than run: the runtime reports no head pose this early,
            // so the recentring happens on the first frame that has one.
            if (RecentreOnXrStart)
            {
                _recentrePending = true;
                _recentreDeadline = Time.time + RecentreWindowSeconds;
            }

            return true;
        }

        /// <summary>Shut the headset down; the camera is already on the vertex and stays there.</summary>
        public void StopXr()
        {
            var settings = XRGeneralSettings.Instance;
            if (settings == null || settings.Manager == null || !settings.Manager.isInitializationComplete)
                return;

            settings.Manager.StopSubsystems();
            settings.Manager.DeinitializeLoader();
            _startedHere = false;
            _recentrePending = false;
            HasRecentred = false;
            PlaceViewpoint();
            RebindAudioToSystemOutput("stopped");
        }

        /// <summary>
        /// Re-initialise the audio engine on Windows' current default output.
        ///
        /// <para>Starting XR makes the runtime point Unity's audio at the
        /// headset's virtual device, and stopping it tries to point it back —
        /// which failed here ("FMOD failed to switch back to normal output")
        /// and left every source playing into a device that had gone: no sound
        /// on the desktop, and a conversation whose scheduled DSP start never
        /// arrived (2026-09-08). This Varjo has no speakers, so the headset
        /// route is never wanted; the sound stays on the room's speakers
        /// whatever the runtime does. A reset stops whatever is playing, so it
        /// is done here, at the switch, and never during a clip.</para>
        /// </summary>
        void RebindAudioToSystemOutput(string when)
        {
            if (!KeepSystemAudioOutput)
                return;

            if (AudioSettings.Reset(AudioSettings.GetConfiguration()))
                Debug.Log($"{name}: audio engine re-initialised on the system output after XR {when}.", this);
            else
                Debug.LogWarning($"{name}: could not re-initialise the audio engine after XR {when}; run GazeControl → Reset Audio.", this);
        }

        /// <summary>
        /// Turn the play area so that the participant's current facing points
        /// between the two agents.
        ///
        /// <para>Call it with the participant wearing the headset and looking
        /// straight ahead. The rig root already faces the triad's centroid, which
        /// for the equilateral layout is the midpoint of the two agents (measured
        /// 0.32 degrees off, which is the agents' own head asymmetry), so
        /// "centred" is simply "the head's yaw relative to this rig is zero".</para>
        ///
        /// <para>Idempotent: it reads the raw device pose, not the already-turned
        /// camera, so calling it a second time re-measures the participant rather
        /// than compounding the first turn.</para>
        /// </summary>
        [ContextMenu("Recentre View")]
        public ViewRecentring.Result RecentreView()
        {
            if (CameraOffset == null)
            {
                Debug.LogError($"{name}: the rig needs a CameraOffset to recentre.", this);
                return default;
            }

            if (!IsXrRunning)
            {
                Debug.LogError($"{name}: start the headset before recentring the view.", this);
                return default;
            }

            if (!TryReadHeadRotation(out var rotation))
            {
                Debug.LogWarning($"{name}: no head pose to recentre from - is the headset being worn?", this);
                return default;
            }

            var result = ViewRecentring.Measure(rotation);
            if (!result.Accepted)
            {
                Debug.LogWarning($"{name}: view not recentred - {result.Reason} The play area has not turned.", this);
                return result;
            }

            CameraOffset.localRotation = Quaternion.Euler(0f, result.YawOffsetDegrees, 0f);
            ViewYawOffset = result.YawOffsetDegrees;
            HasRecentred = true;

            Debug.Log(
                $"{name}: view recentred - play area turned {result.YawOffsetDegrees:+0.0;-0.0} deg so the " +
                "participant faces between the agents.", this);

            return result;
        }

        /// <summary>
        /// Record the participant's own standing eye height, read from the
        /// headset device rather than from the camera.
        ///
        /// <para>It has to come from the device now: the camera no longer follows
        /// the head positionally, so its transform says nothing about how tall
        /// anyone is. The tracking origin is floor-relative, so the device's
        /// height <i>is</i> the height above the floor.</para>
        ///
        /// <para>Nothing in the stimulus depends on this - the viewpoint is fixed
        /// whatever it reads. It is kept because it is the one fact the rig knows
        /// about the participant's body, and `user-study-design.md` section 4
        /// wants it on record as a possible covariate.</para>
        /// </summary>
        void SampleParticipantHeight()
        {
            if (!IsXrRunning)
                return;

            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid)
                return;

            if (!head.TryGetFeatureValue(CommonUsages.centerEyePosition, out var pose) &&
                !head.TryGetFeatureValue(CommonUsages.devicePosition, out pose))
                return;

            var result = ParticipantEyeHeight.Measure(pose.y);
            if (!result.Accepted)
            {
                // Headset on the desk, or tracking lost. Keep the last real
                // measurement rather than overwriting it with a bad one.
                LastRejection = result.Reason;
                return;
            }

            LastRejection = null;
            MeasuredEyeHeight = result.MeasuredEyeHeight;
        }

        /// <summary>
        /// Recentre automatically, once, as soon as the headset reports a usable
        /// pose after XR starts.
        ///
        /// <para>It cannot simply run inside <see cref="StartXr"/>: the runtime
        /// has no pose the instant the subsystems come up, and a participant
        /// often has the headset in their hands rather than on their head. So it
        /// retries - but only until <see cref="RecentreWindowSeconds"/>, because
        /// a recentring that fired ten minutes in, when someone finally picked
        /// the headset up, would snap the view mid-take and quietly ruin the
        /// trial. After that the operator recentres by hand.</para>
        /// </summary>
        void ServicePendingRecentre()
        {
            if (!_recentrePending || !IsXrRunning)
                return;

            if (Time.time > _recentreDeadline)
            {
                _recentrePending = false;
                Debug.LogWarning(
                    $"{name}: gave up recentring the view automatically after {RecentreWindowSeconds:0}s - the " +
                    "headset never reported a usable pose. Recentre by hand (right-click the component, " +
                    "Recentre View) with the participant looking straight ahead.", this);
                return;
            }

            if (!TryReadHeadRotation(out var rotation) || !ViewRecentring.Measure(rotation).Accepted)
                return;

            _recentrePending = false;
            RecentreView();
        }

        /// <summary>
        /// The headset's rotation in tracking space, straight from the device.
        /// Read here rather than off the camera because the camera's rotation
        /// already carries the play-area turn, and recentring from that would
        /// stack turn on turn.
        /// </summary>
        bool TryReadHeadRotation(out Quaternion rotation)
        {
            rotation = Quaternion.identity;

            var head = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!head.isValid)
                return false;

            return head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation) ||
                   head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
        }

        void Update()
        {
            SampleParticipantHeight();
            ServicePendingRecentre();
        }

        /// <summary>
        /// Run the headset's own eye-tracking calibration for this wearer.
        ///
        /// <para>This is the tracker's calibration, and it is now the <i>only</i>
        /// per-participant setup step the rig has: the viewpoint is fixed, so
        /// there is no eye-height calibration to run beside it. It teaches the
        /// headset where this person's pupils point, and gaze is not reported at
        /// all until it has been done.</para>
        /// </summary>
        [ContextMenu("Calibrate Eye Tracking")]
        public void CalibrateEyeTracking()
        {
#if VARJO_XR
            if (!IsXrRunning)
            {
                Debug.LogError($"{name}: start the headset before calibrating eye tracking.", this);
                return;
            }

            if (!Varjo.XR.VarjoEyeTracking.IsGazeAllowed())
            {
                Debug.LogError(
                    $"{name}: Varjo Base has not granted this application permission to use eye tracking, " +
                    "so calibration cannot be requested.", this);
                return;
            }

            if (Varjo.XR.VarjoEyeTracking.RequestGazeCalibration())
                Debug.Log($"{name}: eye-tracking calibration requested — the wearer follows the dots in the headset.", this);
            else
                Debug.LogError($"{name}: the headset refused the eye-tracking calibration request.", this);
#else
            Debug.LogError($"{name}: the Varjo XR plugin is not in this project, so there is no eye tracker to calibrate.", this);
#endif
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
        /// Put the camera on the triad vertex at <see cref="EyeHeight"/>. This is
        /// the viewpoint in <i>both</i> modes now, not a desktop fallback: with
        /// translation untracked the headset never moves the camera off it, so
        /// baking, the Clip Browser, Record Demo and a worn session all render
        /// from this one pose.
        ///
        /// <para>The camera offset is forced back to identity here rather than
        /// merely assumed to be: it is a plain transform anyone can nudge in the
        /// inspector, and a nudge would silently move every participant's
        /// viewpoint.</para>
        /// </summary>
        void PlaceViewpoint()
        {
            if (CameraOffset == null || HeadCamera == null)
                return;

            CameraOffset.localPosition = Vector3.zero;
            CameraOffset.localRotation = Quaternion.identity;
            HeadCamera.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            HeadCamera.transform.localRotation = Quaternion.identity;
        }
    }
}
