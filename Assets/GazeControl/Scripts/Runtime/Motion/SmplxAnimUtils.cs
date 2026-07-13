using UnityEngine;

namespace GazeControl.Motion
{
    /// <summary>
    /// Drives the SMPL-X skeleton (Assets/SMPLX/smplx-visemes-unity.fbx) directly
    /// from axis-angle pose data — no intermediate 6D representation. The 6D
    /// round-trip (Assets/Docs/math.md, VR-Chat MathUtils.SixDToQuaternion) exists
    /// for network regression targets; for playback, axis-angle → quaternion plus
    /// the same handedness fix is exact and cheaper.
    /// </summary>
    public static class SmplxAnimUtils
    {
        public const int JointCount = SmplxMotionClip.JointCount;

        /// <summary>SMPL-X joint order; indices match 'poses' triplets, names match the FBX bone names.</summary>
        public static readonly string[] JointNames =
        {
            "pelvis", "left_hip", "right_hip", "spine1", "left_knee", "right_knee", "spine2",
            "left_ankle", "right_ankle", "spine3", "left_foot", "right_foot", "neck",
            "left_collar", "right_collar", "head", "left_shoulder", "right_shoulder",
            "left_elbow", "right_elbow", "left_wrist", "right_wrist",
            "jaw", "left_eye_smplhf", "right_eye_smplhf",
            "left_index1", "left_index2", "left_index3", "left_middle1", "left_middle2", "left_middle3",
            "left_pinky1", "left_pinky2", "left_pinky3", "left_ring1", "left_ring2", "left_ring3",
            "left_thumb1", "left_thumb2", "left_thumb3",
            "right_index1", "right_index2", "right_index3", "right_middle1", "right_middle2", "right_middle3",
            "right_pinky1", "right_pinky2", "right_pinky3", "right_ring1", "right_ring2", "right_ring3",
            "right_thumb1", "right_thumb2", "right_thumb3",
        };

        /// <summary>
        /// Dataset (right-handed, Y-up) axis-angle → Unity (left-handed, Y-up) local rotation.
        /// The YZ-plane mirror (x → −x) maps the frames onto each other; conjugating a rotation
        /// by that mirror keeps the angle and flips the axis to (x, −y, −z). Same net conversion
        /// as VR-Chat's SixDToQuaternion (matrix transpose + axis.x negation), done in one step.
        /// No up-axis correction is needed: TalkingWithHands data is already Y-up (pelvis height
        /// is in trans.y), and in the FBX the pelvis parent chain (−90°X · +90°X) cancels, so the
        /// pelvis local frame equals the model root frame.
        /// </summary>
        public static Quaternion AxisAngleToUnityRotation(Vector3 aa)
        {
            var angle = aa.magnitude;
            if (angle < 1e-8f) return Quaternion.identity;
            var axis = aa / angle;
            return Quaternion.AngleAxis(angle * Mathf.Rad2Deg, new Vector3(axis.x, -axis.y, -axis.z));
        }

        /// <summary>Dataset (right-handed, Y-up) position → Unity position (same YZ-plane mirror).</summary>
        public static Vector3 PositionToUnity(Vector3 p) => new(-p.x, p.y, p.z);

        /// <summary>
        /// Find the 55 SMPL-X joint transforms by name under <paramref name="root"/>.
        /// Returns false (and logs) if any joint is missing.
        /// </summary>
        public static bool BindJoints(Transform root, Transform[] joints)
        {
            Debug.Assert(joints.Length == JointCount);
            var allFound = true;
            for (var i = 0; i < JointCount; i++)
            {
                joints[i] = FindChildRecursive(root, JointNames[i]);
                if (joints[i] == null)
                {
                    Debug.LogError($"SMPL-X joint '{JointNames[i]}' not found under '{root.name}'.", root);
                    allFound = false;
                }
            }

            return allFound;
        }

        /// <summary>Apply one clip frame's joint rotations as local rotations (root translation is separate).</summary>
        public static void SetPose(Transform[] joints, SmplxMotionClip clip, int frame)
        {
            for (var i = 0; i < JointCount; i++)
                joints[i].localRotation = AxisAngleToUnityRotation(clip.GetJointAxisAngle(frame, i));
        }

        public static Transform FindChildRecursive(Transform parent, string childName)
        {
            foreach (Transform child in parent)
            {
                if (child.name == childName) return child;
                var result = FindChildRecursive(child, childName);
                if (result != null) return result;
            }

            return null;
        }
    }
}
