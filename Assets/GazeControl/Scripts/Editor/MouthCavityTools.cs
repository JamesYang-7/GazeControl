using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Tuning aids for the MouthCavity blockers (the dark spheres that stand in
    /// for SMPL-X's missing mouth interior).
    ///
    /// The blockers are already parented to the head bone, so they can be edited
    /// straight from the Inspector — what these commands add is the ability to
    /// *see* what you are editing: the mouth is closed in Edit Mode, so the leak
    /// only appears with a viseme driven, and Play Mode tweaks are otherwise lost
    /// on exit.
    /// </summary>
    public static class MouthCavityTools
    {
        // Viseme blendshapes occupy 506-519 (Oculus order minus 'sil'); 515 is
        // 'aa', the widest jaw opening and so the worst case for leaks.
        const int AaVisemeIndex = 515;
        const string BlockerName = "MouthCavity";
        const string BackdropName = "MouthBackdrop";
        const string ClipboardKey = "GazeControl.MouthCavity.Clipboard";

        // Backdrop placement, in metres along the user's sightline through the
        // mouth. Close enough to stay inside the head's silhouette from the user's
        // viewpoint, far enough back to sit behind the lips.
        const float BackdropDistance = 0.06f;
        const float BackdropRadius = 0.05f;
        const float BackdropThickness = 0.012f;

        /// <summary>
        /// Temporary fix for the white mouth leak: a dark lens parented to the head
        /// bone and placed *behind* the mouth along the line from the user's camera,
        /// so the head occludes it from every angle the user can see — except
        /// through the open mouth, where it replaces the skybox.
        ///
        /// Placed by measuring the actual camera direction rather than by a fixed
        /// left/right offset, because each agent is yawed toward the triad centroid
        /// by a different amount; the sightline exits toward A's left and B's right.
        ///
        /// This does not seal the lip corners — it hides what shows through them.
        /// The real fix is blocker geometry that follows the lip line.
        /// </summary>
        [MenuItem("GazeControl/Mouth Cavity/Create Backdrop (temporary)")]
        public static void CreateBackdrop()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("Mouth Cavity: no camera tagged MainCamera — the backdrop is placed relative to the user's viewpoint.");
                return;
            }

            var blockers = FindBlockers();
            if (blockers.Count == 0)
            {
                Debug.LogError($"Mouth Cavity: no '{BlockerName}' found; it provides the mouth reference position.");
                return;
            }

            foreach (var blocker in blockers)
            {
                var head = blocker.parent;
                if (head == null)
                {
                    Debug.LogWarning($"Mouth Cavity: '{Path(blocker)}' has no parent bone; skipped.", blocker);
                    continue;
                }

                var backdrop = CreateOrFindBackdrop(head, blocker);
                var toCameraLocal = head.InverseTransformDirection((camera.transform.position - head.position).normalized);

                Undo.RecordObject(backdrop, "Place Mouth Backdrop");
                backdrop.localPosition = blocker.localPosition - toCameraLocal * BackdropDistance;
                backdrop.localRotation = Quaternion.LookRotation(toCameraLocal, Vector3.up);
                backdrop.localScale = new Vector3(BackdropRadius * 2f, BackdropRadius * 2f, BackdropThickness);
                EditorUtility.SetDirty(backdrop);

                Debug.Log($"Mouth Cavity: backdrop on '{head.root.name}' at head-local {backdrop.localPosition:F4} " +
                          $"(sightline exits toward local {(-toCameraLocal):F2}).", backdrop);
            }
        }

        [MenuItem("GazeControl/Mouth Cavity/Remove Backdrop")]
        public static void RemoveBackdrop()
        {
            var removed = 0;
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (transform.name != BackdropName)
                    continue;

                Undo.DestroyObjectImmediate(transform.gameObject);
                removed++;
            }

            Debug.Log($"Mouth Cavity: removed {removed} backdrop(s).");
        }

        static Transform CreateOrFindBackdrop(Transform head, Transform blocker)
        {
            var existing = head.Find(BackdropName);
            if (existing != null)
                return existing;

            // A flattened sphere, not a quad: closed geometry presents a surface
            // whatever the head rotation does, so there is no backface to catch.
            var created = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            created.name = BackdropName;
            Undo.RegisterCreatedObjectUndo(created, "Create Mouth Backdrop");

            Object.DestroyImmediate(created.GetComponent<Collider>());

            var renderer = created.GetComponent<MeshRenderer>();
            var blockerRenderer = blocker.GetComponent<MeshRenderer>();
            if (blockerRenderer != null)
                renderer.sharedMaterial = blockerRenderer.sharedMaterial;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Undo.SetTransformParent(created.transform, head, "Parent Mouth Backdrop");
            return created.transform;
        }

        [MenuItem("GazeControl/Mouth Cavity/Open Jaw (aa viseme)")]
        public static void OpenJaw() => SetJaw(100f);

        [MenuItem("GazeControl/Mouth Cavity/Close Jaw")]
        public static void CloseJaw() => SetJaw(0f);

        [MenuItem("GazeControl/Mouth Cavity/Select Blockers")]
        public static void SelectBlockers()
        {
            var blockers = FindBlockers();
            if (blockers.Count == 0)
            {
                Debug.LogError($"Mouth Cavity: no '{BlockerName}' objects found in the open scene.");
                return;
            }

            var objects = new Object[blockers.Count];
            for (var i = 0; i < blockers.Count; i++)
                objects[i] = blockers[i].gameObject;

            Selection.objects = objects;
            Debug.Log($"Mouth Cavity: selected {blockers.Count} blocker(s). Edit the Transform in head-local space — x=0 is the facial midline, +z is face-forward.");
        }

        /// <summary>
        /// Copy the first blocker's head-local transform onto every other one, so
        /// only a single blocker has to be tuned by hand.
        /// </summary>
        [MenuItem("GazeControl/Mouth Cavity/Mirror First Blocker To All")]
        public static void MirrorToAll()
        {
            var blockers = FindBlockers();
            if (blockers.Count < 2)
            {
                Debug.LogWarning("Mouth Cavity: need at least two blockers to mirror.");
                return;
            }

            var source = blockers[0];
            for (var i = 1; i < blockers.Count; i++)
            {
                Undo.RecordObject(blockers[i], "Mirror Mouth Cavity");
                CopyLocalTransform(source, blockers[i]);
                EditorUtility.SetDirty(blockers[i]);
            }

            Debug.Log($"Mouth Cavity: copied {Describe(source)} from '{Path(source)}' to {blockers.Count - 1} other blocker(s).");
        }

        /// <summary>
        /// Store the current head-local transform so a configuration found in Play
        /// Mode survives the return to Edit Mode. EditorPrefs rather than
        /// SessionState: SessionState is cleared when the editor restarts, and a
        /// tuning session can easily span one.
        /// </summary>
        [MenuItem("GazeControl/Mouth Cavity/Copy Placement")]
        public static void CopyPlacement()
        {
            var blockers = FindBlockers();
            if (blockers.Count == 0)
            {
                Debug.LogError($"Mouth Cavity: no '{BlockerName}' objects found.");
                return;
            }

            var source = blockers[0];
            var p = source.localPosition;
            var r = source.localEulerAngles;
            var s = source.localScale;

            EditorPrefs.SetString(ClipboardKey, string.Join(",", new[]
            {
                p.x.ToString("R"), p.y.ToString("R"), p.z.ToString("R"),
                r.x.ToString("R"), r.y.ToString("R"), r.z.ToString("R"),
                s.x.ToString("R"), s.y.ToString("R"), s.z.ToString("R"),
            }));

            Debug.Log($"Mouth Cavity: copied {Describe(source)} — use 'Paste Placement To All' after leaving Play Mode.");
        }

        [MenuItem("GazeControl/Mouth Cavity/Paste Placement To All")]
        public static void PastePlacement()
        {
            var stored = EditorPrefs.GetString(ClipboardKey, string.Empty);
            var parts = stored.Split(',');
            if (parts.Length != 9)
            {
                Debug.LogError("Mouth Cavity: nothing copied yet — run 'Copy Placement' first (in Play Mode, once it looks right).");
                return;
            }

            var values = new float[9];
            for (var i = 0; i < 9; i++)
            {
                if (float.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out values[i]))
                    continue;

                Debug.LogError("Mouth Cavity: stored placement is corrupt; copy it again.");
                return;
            }

            var blockers = FindBlockers();
            foreach (var blocker in blockers)
            {
                Undo.RecordObject(blocker, "Paste Mouth Cavity Placement");
                blocker.localPosition = new Vector3(values[0], values[1], values[2]);
                blocker.localEulerAngles = new Vector3(values[3], values[4], values[5]);
                blocker.localScale = new Vector3(values[6], values[7], values[8]);
                EditorUtility.SetDirty(blocker);
            }

            Debug.Log($"Mouth Cavity: pasted placement to {blockers.Count} blocker(s). Save the scene to keep it.");
        }

        /// <summary>
        /// Zero the head-local x offset and yaw. Placements derived from eye-bone
        /// world vectors land ~9 mm off the facial midline and are pose-dependent;
        /// the mouth is symmetric, so the blocker should be too.
        /// </summary>
        [MenuItem("GazeControl/Mouth Cavity/Snap To Facial Midline")]
        public static void SnapToMidline()
        {
            var blockers = FindBlockers();
            foreach (var blocker in blockers)
            {
                Undo.RecordObject(blocker, "Snap Mouth Cavity To Midline");

                var position = blocker.localPosition;
                position.x = 0f;
                blocker.localPosition = position;

                var euler = blocker.localEulerAngles;
                euler.y = 0f;
                blocker.localEulerAngles = euler;

                EditorUtility.SetDirty(blocker);
            }

            Debug.Log($"Mouth Cavity: snapped {blockers.Count} blocker(s) to the midline (local x = 0, yaw = 0).");
        }

        static void SetJaw(float weight)
        {
            var renderers = FindVisemeRenderers();
            if (renderers.Count == 0)
            {
                Debug.LogError("Mouth Cavity: no SMPL-X mesh with viseme blendshapes found in the open scene.");
                return;
            }

            foreach (var renderer in renderers)
            {
                Undo.RecordObject(renderer, "Set Jaw Viseme");
                renderer.SetBlendShapeWeight(AaVisemeIndex, weight);
                EditorUtility.SetDirty(renderer);
            }

            var state = weight > 0f ? "opened" : "closed";
            Debug.Log($"Mouth Cavity: {state} the jaw on {renderers.Count} mesh(es) via blendshape {AaVisemeIndex} ('aa'). " +
                      "Orbit to ±45° — that is where the corner leak shows.");
        }

        static List<Transform> FindBlockers()
        {
            var found = new List<Transform>();
            foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (transform.name == BlockerName)
                    found.Add(transform);
            }

            return found;
        }

        static List<SkinnedMeshRenderer> FindVisemeRenderers()
        {
            var found = new List<SkinnedMeshRenderer>();
            foreach (var renderer in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > AaVisemeIndex)
                    found.Add(renderer);
            }

            return found;
        }

        static void CopyLocalTransform(Transform source, Transform destination)
        {
            destination.localPosition = source.localPosition;
            destination.localRotation = source.localRotation;
            destination.localScale = source.localScale;
        }

        static string Describe(Transform blocker) =>
            $"pos {blocker.localPosition:F4}, euler {blocker.localEulerAngles:F2}, scale {blocker.localScale:F4}";

        static string Path(Transform blocker)
        {
            var path = blocker.name;
            for (var parent = blocker.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;

            return path;
        }
    }
}
