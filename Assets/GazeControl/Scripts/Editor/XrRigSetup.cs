using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Logging;
using GazeControl.Xr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Set Up XR Rig: turns the open scene's flat User vertex into
    /// a standing XR play area, and repairs it if it is ever half dismantled.
    ///
    /// A menu command for the same reason as
    /// <see cref="BaselineConditionSetup"/>: the scene keeps what this produces,
    /// but the wiring is written down as code rather than as hand-edited scene
    /// YAML, so it can be reviewed and re-run.
    ///
    /// The camera sits on the vertex at
    /// <see cref="ParticipantEyeHeight.SmplxEyeHeight"/>, which is where a
    /// desktop preview frames the triad from and where recentring puts a worn
    /// headset before it tracks freely (position and rotation, since 2026-09-08).
    /// The height moved up 8.5 cm on 2026-08-27, from a generic 1.60 m to the
    /// agents' own eye line; no baked gaze track is affected, because no policy
    /// reads scene geometry (the one field that did, `MutualGazeActive`, has gone
    /// unread since the 2026-07-30 decision to drop mutual-gaze breaking).
    /// </summary>
    public static class XrRigSetup
    {
        const string UserObjectName = "User";
        const string CameraOffsetName = "Camera Offset";

        /// <summary>
        /// Near clip for VR. The flat scene used 0.3 m, which is far too deep a
        /// dead zone in a headset — anything the participant brings near their
        /// face vanishes. Nothing in the triad sits closer than a metre, so this
        /// changes no existing recording.
        /// </summary>
        const float VrNearClipPlane = 0.05f;

        /// <summary>
        /// How far the User vertex may face away from the agents' midpoint before
        /// the setup complains, degrees. Recentring aims the participant along the
        /// vertex's forward axis, so this bounds how off-centre they can end up.
        /// </summary>
        const float MaxVertexFacingErrorDegrees = 3f;

        [MenuItem("GazeControl/Set Up XR Rig")]
        public static void SetUp()
        {
            var user = GameObject.Find(UserObjectName);
            if (user == null)
            {
                Debug.LogError($"Set Up XR Rig: open TriadScene first — no '{UserObjectName}' object in the open scene.");
                return;
            }

            var camera = user.GetComponentInChildren<Camera>();
            if (camera == null)
            {
                Debug.LogError($"Set Up XR Rig: '{UserObjectName}' has no camera under it.");
                return;
            }

            Undo.SetCurrentGroupName("Set Up XR Rig");
            var undoGroup = Undo.GetCurrentGroup();

            var offset = FindOrCreateCameraOffset(user.transform);
            if (camera.transform.parent != offset)
                Undo.SetTransformParent(camera.transform, offset, "Reparent head camera");

            camera.transform.localPosition = new Vector3(0f, ParticipantEyeHeight.SmplxEyeHeight, 0f);
            camera.transform.localRotation = Quaternion.identity;
            Undo.RecordObject(camera, "Configure head camera");
            camera.nearClipPlane = VrNearClipPlane;

            ConfigureTrackedPoseDriver(camera.gameObject);

            var rig = user.GetComponent<XrParticipantRig>();
            if (rig == null)
                rig = Undo.AddComponent<XrParticipantRig>(user);

            Undo.RecordObject(rig, "Configure XR rig");
            rig.CameraOffset = offset;
            rig.HeadCamera = camera;

            // The agents aim at whatever the human participant's LookAtAnchor
            // names, and in a headset that has to be the tracked camera or they
            // would look at where the participant's head was before they moved.
            var participant = user.GetComponent<GazeParticipant>();
            if (participant != null && participant.LookAtAnchor != camera.transform)
            {
                Undo.RecordObject(participant, "Point the look-at anchor at the head camera");
                participant.LookAtAnchor = camera.transform;
            }

            ConfigureParticipantGazeLogger(user, rig);
            WarnIfTheVertexIsNotFacingTheAgents(user);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(user.scene);

            Debug.Log(
                $"Set Up XR Rig: '{UserObjectName}' places the participant's head on the triad vertex at " +
                $"{ParticipantEyeHeight.SmplxEyeHeight:F3} m when the view is recentred, and the head tracks freely " +
                "from there, position and rotation both. XR starts only when asked (XrParticipantRig.StartXrOnPlay).", rig);
        }

        /// <summary>
        /// Check the invariant that view recentring rests on: the User vertex
        /// faces the midpoint of the two agents, so "centred between the agents"
        /// and "yaw zero relative to this rig" are the same thing.
        ///
        /// <para>A warning rather than a repair. Which way the vertex faces is a
        /// scene-layout decision, and silently rotating someone's scene to satisfy
        /// a rig assumption would be worse than telling them it is violated.</para>
        /// </summary>
        static void WarnIfTheVertexIsNotFacingTheAgents(GameObject user)
        {
            var agents = new System.Collections.Generic.List<GazeParticipant>();
            foreach (var participant in Object.FindObjectsByType<GazeParticipant>(FindObjectsInactive.Include))
            {
                if (!participant.IsHuman && participant.LookAtAnchor != null)
                    agents.Add(participant);
            }

            if (agents.Count != 2)
                return;

            var midpoint = (agents[0].LookAtAnchor.position + agents[1].LookAtAnchor.position) * 0.5f;
            var toMidpoint = midpoint - user.transform.position;
            toMidpoint.y = 0f;
            if (toMidpoint.sqrMagnitude < 1e-6f)
                return;

            var forward = user.transform.forward;
            forward.y = 0f;
            var error = Vector3.Angle(forward, toMidpoint);

            // A couple of degrees is the agents' own head asymmetry under mocap,
            // not a layout problem; it measured 0.32 degrees when this was added.
            if (error <= MaxVertexFacingErrorDegrees)
                return;

            Debug.LogWarning(
                $"Set Up XR Rig: '{user.name}' faces {error:F1} deg away from the midpoint of the two agents. " +
                "View recentring points the participant along this object's forward axis, so they would end up " +
                "off-centre by that much. Rotate the vertex to face the triad centroid.", user);
        }

        /// <summary>
        /// Put the participant's own gaze log on the rig and hand it to the
        /// condition runner. It lives on the User vertex because it is the
        /// participant's record, and the runner drives it because the runner owns
        /// the decision grid the samples land on.
        /// </summary>
        static void ConfigureParticipantGazeLogger(GameObject user, XrParticipantRig rig)
        {
            var logger = user.GetComponent<ParticipantGazeLogger>();
            if (logger == null)
                logger = Undo.AddComponent<ParticipantGazeLogger>(user);

            Undo.RecordObject(logger, "Configure participant gaze logger");
            logger.Rig = rig;

            var runner = Object.FindAnyObjectByType<GazeConditionRunner>(FindObjectsInactive.Include);
            if (runner == null)
            {
                Debug.LogWarning(
                    "Set Up XR Rig: no GazeConditionRunner in the scene, so the participant gaze logger is " +
                    "unwired. Run GazeControl → Set Up Baseline Condition first, then this again.", logger);
                return;
            }

            if (runner.ParticipantGaze == logger)
                return;

            Undo.RecordObject(runner, "Wire the participant gaze logger");
            runner.ParticipantGaze = logger;
            EditorUtility.SetDirty(runner);
        }

        static Transform FindOrCreateCameraOffset(Transform user)
        {
            var existing = user.Find(CameraOffsetName);
            if (existing != null)
                return existing;

            var offset = new GameObject(CameraOffsetName).transform;
            Undo.RegisterCreatedObjectUndo(offset.gameObject, "Create camera offset");
            Undo.SetTransformParent(offset, user, "Parent camera offset");
            offset.localPosition = Vector3.zero;
            offset.localRotation = Quaternion.identity;
            offset.localScale = Vector3.one;
            return offset;
        }

        /// <summary>
        /// Drive the camera from the headset's centre eye. The actions are built
        /// here rather than pulled from `InputSystem_Actions.inputactions`: the
        /// head pose is not a game binding anyone should be able to remap, and
        /// embedding it keeps the rig working in a scene opened on its own.
        /// </summary>
        static void ConfigureTrackedPoseDriver(GameObject cameraObject)
        {
            var driver = cameraObject.GetComponent<TrackedPoseDriver>();
            if (driver == null)
                driver = Undo.AddComponent<TrackedPoseDriver>(cameraObject);

            Undo.RecordObject(driver, "Configure tracked pose driver");

            // Position and rotation: the head is placed on the vertex once, by
            // recentring, and tracks freely after that (see XrParticipantRig).
            // Was RotationOnly from 2026-08-27 to 2026-09-08, when the viewpoint
            // was a fixed point; the user chose parallax over the fixed geometry.
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;

            // Before-render as well as per-frame: a head pose sampled only in
            // Update is one frame stale by the time the eyes are rendered, which
            // is the difference between a stable room and a swimming one.
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            driver.positionInput = new InputActionProperty(new InputAction(
                "XR Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(new InputAction(
                "XR Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            driver.trackingStateInput = new InputActionProperty(new InputAction(
                "XR Head Tracking State", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            EditorUtility.SetDirty(driver);
        }
    }
}
