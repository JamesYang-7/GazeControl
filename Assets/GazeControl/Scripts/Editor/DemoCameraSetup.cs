using System.Linq;
using GazeControl.Demo;
using GazeControl.Xr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// <c>GazeControl → Demo Video → Set Up Demo Camera</c>: build or repair the
    /// camera a demo video is shot through, in whichever study scene is open.
    ///
    /// <para>Re-runnable, like the other setup commands: it finds what is already
    /// there, adds what is missing, and rewires the rest, so a scene that has
    /// drifted is repaired by pressing it again rather than by hand.</para>
    ///
    /// <para>What it will not do is touch the participant's camera. That camera
    /// is the human's <c>LookAtAnchor</c> — the agents aim at it — so its pose is
    /// part of the stimulus and not a framing choice. The demo camera is a second
    /// one beside it, and the two differ only in where they stand.</para>
    /// </summary>
    public static class DemoCameraSetup
    {
        /// <summary>Tag the Recorder finds the demo camera by, since it cannot be the main one.</summary>
        public const string CameraTag = "DemoCamera";

        /// <summary>Name of the scene object holding the demo camera.</summary>
        public const string ObjectName = "Demo Camera";

        /// <summary>Where the framing the operator tunes is kept.</summary>
        public const string FramingAssetPath = "Assets/GazeControl/Settings/DemoCameraFraming.asset";

        /// <summary>
        /// Drawn over the participant's camera when it is switched on at all.
        /// The participant's sits at -1, Unity's default for a scene camera.
        /// </summary>
        const float CameraDepth = 1f;

        [MenuItem("GazeControl/Demo Video/Set Up Demo Camera")]
        public static void SetUp()
        {
            const string command = "Demo Video → Set Up Demo Camera";

            var vertex = FindVertex();
            if (vertex == null)
            {
                Debug.LogError($"{command}: no XrParticipantRig in the open scene — " +
                               "the shot is measured from the participant's vertex, and there is none to measure from.");
                return;
            }

            EnsureTag(CameraTag);
            var framing = LoadOrCreateFraming();
            var demo = FindOrCreateCamera(vertex);

            Undo.RecordObject(demo, command);
            Undo.RecordObject(demo.Camera, command);
            demo.Vertex = vertex.transform;
            demo.Framing = framing;
            demo.gameObject.tag = CameraTag;

            CopyLens(vertex.HeadCamera, demo.Camera);
            demo.Camera.depth = CameraDepth;

            // Off in the scene as saved: this camera is only ever switched on by a
            // demo command, which asks for it through DemoViewRequest. A scene
            // committed with it rendering would shoot a participant session — in a
            // headset — through a camera pulled a metre out of their head.
            demo.Camera.enabled = false;

            demo.Apply();

            EditorUtility.SetDirty(demo);
            EditorUtility.SetDirty(demo.Camera);
            EditorSceneManager.MarkSceneDirty(demo.gameObject.scene);

            var pose = demo.FramedPose;
            Debug.Log($"{command}: {ObjectName} ready in {demo.gameObject.scene.name} — " +
                      $"{framing.PullBackMetres:F2} m back, {framing.HeightMetres:F3} m up, " +
                      $"{framing.PitchDegrees:F1}° down, {framing.VerticalFieldOfView:F1}° vertical " +
                      $"({DemoCameraFrame.HorizontalFieldOfView(framing.VerticalFieldOfView, 16f / 9f):F1}° across a 16:9 frame). " +
                      $"Standing at {pose.position}. Tune it with Demo Video → Camera Framing.", demo);
        }

        /// <summary>The participant's vertex, which is the rig's own object.</summary>
        public static XrParticipantRig FindVertex() =>
            Object.FindFirstObjectByType<XrParticipantRig>(FindObjectsInactive.Include);

        /// <summary>The scene's demo camera, or null when it has none.</summary>
        public static DemoCamera Find() =>
            Object.FindFirstObjectByType<DemoCamera>(FindObjectsInactive.Include);

        static DemoCamera FindOrCreateCamera(XrParticipantRig vertex)
        {
            var existing = Find();
            if (existing != null)
                return existing;

            var go = new GameObject(ObjectName);
            Undo.RegisterCreatedObjectUndo(go, "Create demo camera");

            // A scene root, deliberately: parented under the rig it would be moved
            // by recentring, which slides the play area to wherever the headset
            // happened to be. A locked-off camera has to be locked off.
            go.transform.SetParent(null, worldPositionStays: true);
            go.transform.SetSiblingIndex(vertex.transform.GetSiblingIndex() + 1);

            var camera = go.AddComponent<Camera>();
            camera.enabled = false;

            return go.AddComponent<DemoCamera>();
        }

        /// <summary>
        /// Give the demo camera the participant camera's lens and clearing, so
        /// that the only difference between the two pictures is where they are
        /// taken from. The field of view is not copied — that is the framing's.
        /// </summary>
        static void CopyLens(Camera from, Camera to)
        {
            if (from == null)
                return;

            to.clearFlags = from.clearFlags;
            to.backgroundColor = from.backgroundColor;
            to.cullingMask = from.cullingMask;
            to.nearClipPlane = from.nearClipPlane;
            to.farClipPlane = from.farClipPlane;
            to.allowHDR = from.allowHDR;
            to.allowMSAA = from.allowMSAA;
            to.usePhysicalProperties = false; // fieldOfView is then the vertical angle the framing names
        }

        static DemoCameraFraming LoadOrCreateFraming()
        {
            var existing = AssetDatabase.LoadAssetAtPath<DemoCameraFraming>(FramingAssetPath);
            if (existing != null)
                return existing;

            EnsureFolder(FramingAssetPath[..FramingAssetPath.LastIndexOf('/')]);
            var framing = ScriptableObject.CreateInstance<DemoCameraFraming>();
            AssetDatabase.CreateAsset(framing, FramingAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Demo Video: framing created at {FramingAssetPath} with the default shot.", framing);
            return framing;
        }

        /// <summary>
        /// Make sure an "Assets/..." folder exists and the asset database knows
        /// about it. A folder made behind the database's back — with
        /// <c>System.IO.Directory</c>, say — is not imported, and writing an
        /// asset into one Unity has not seen fails with nothing useful said
        /// about why.
        /// </summary>
        static void EnsureFolder(string folder)
        {
            var lastSlash = folder.LastIndexOf('/');
            if (lastSlash < 0 || AssetDatabase.IsValidFolder(folder))
                return; // "Assets" itself always exists and has no parent to make

            var parent = folder[..lastSlash];
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder[(lastSlash + 1)..]);
        }

        /// <summary>
        /// Add a tag to the project if it has not got it. Unity Recorder finds a
        /// camera that is not <c>Camera.main</c> only by tag, and the project
        /// ships with no custom tags at all.
        /// </summary>
        static void EnsureTag(string tag)
        {
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
            if (asset == null)
            {
                Debug.LogError($"Demo Video: could not open the tag manager, so the '{tag}' tag was not added.");
                return;
            }

            var manager = new SerializedObject(asset);
            var tags = manager.FindProperty("tags");

            for (var i = 0; i < tags.arraySize; i++)
            {
                if (tags.GetArrayElementAtIndex(i).stringValue == tag)
                    return;
            }

            tags.InsertArrayElementAtIndex(tags.arraySize);
            tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
            manager.ApplyModifiedProperties();
            Debug.Log($"Demo Video: added the '{tag}' tag.");
        }
    }
}
