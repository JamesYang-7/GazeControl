using GazeControl.Conversation;
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
    /// It is deliberately conservative about the camera's pose. The flat rig
    /// (offset at zero, camera at 1.60 m above the vertex) is exactly where the
    /// camera has always been, so every existing desktop workflow — baking gaze
    /// tracks, the Clip Browser, Record Demo — renders the same frames after the
    /// port as before it.
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

            camera.transform.localPosition = new Vector3(0f, EyeHeightCalibration.DefaultReferenceEyeHeight, 0f);
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

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(user.scene);

            Debug.Log(
                $"Set Up XR Rig: '{UserObjectName}' is now a standing play area — '{CameraOffsetName}' carries the " +
                "eye-height calibration and the camera is head-tracked. XR starts only when asked " +
                "(XrParticipantRig.StartXrOnPlay), so desktop workflows are unchanged.", rig);
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
