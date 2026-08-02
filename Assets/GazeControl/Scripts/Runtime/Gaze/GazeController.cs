using GazeControl.Motion;
using UnityEngine;

namespace GazeControl.Gaze
{
    /// <summary>
    /// Drives an agent's gaze: eye bones look at a person or in an averted
    /// direction (clamped to a plausible range), with a partial head turn on top
    /// for person gaze. Runs in LateUpdate so it layers over the mocap pose that
    /// <see cref="SmplxMotionPlayer"/> writes in Update; head blending is
    /// weight-based (not accumulated) because the mocap rewrites the head
    /// rotation every frame.
    ///
    /// This is the shared animation layer: every experimental condition drives it
    /// identically, so the study measures gaze policy rather than animation
    /// quality (§0.1). It deliberately models no blinks (SMPL-X has no eyelid
    /// blendshapes) and no idle micro-motion — idle body motion comes from the
    /// mocap clips, identically in every condition.
    /// </summary>
    public class GazeController : MonoBehaviour
    {
        /// <summary>What the gaze is currently aimed at.</summary>
        public enum GazeMode
        {
            /// <summary>Neutral: eyes recentre in the head, head returns to pure mocap.</summary>
            None,

            /// <summary>Fixating a transform, with a fractional head turn.</summary>
            Person,

            /// <summary>Averted to a sampled direction — eyes only, so it cannot fight the mocap head.</summary>
            Aversion,
        }

        [field: SerializeField]
        [field: Tooltip("Eye rotation speed; saccades are near-instant, keep high")]
        public float EyeDegreesPerSecond { get; set; } = 600f;

        [field: SerializeField]
        [field: Range(0f, 60f)]
        [field: Tooltip("Max eye yaw away from the head's forward direction")]
        public float MaxEyeYaw { get; set; } = 35f;

        [field: SerializeField]
        [field: Range(0f, 45f)]
        [field: Tooltip("Max eye pitch away from the head's forward direction; smaller than yaw, as in humans")]
        public float MaxEyePitch { get; set; } = 25f;

        [field: SerializeField]
        [field: Range(0f, 1f)]
        [field: Tooltip("How much the head turns toward a person being gazed at (0 = eyes only)")]
        public float HeadContribution { get; set; } = 0.5f;

        [field: SerializeField]
        [field: Tooltip("Smoothing rate of the head contribution weight, 1/s")]
        public float HeadBlendSpeed { get; set; } = 3f;

        /// <summary>Current gaze target; null unless <see cref="Mode"/> is <see cref="GazeMode.Person"/>.</summary>
        public Transform Target { get; private set; }

        /// <summary>Which of the three gaze modes is active.</summary>
        public GazeMode Mode { get; private set; }

        /// <summary>Averted yaw/pitch in degrees, eye-in-head; valid only in <see cref="GazeMode.Aversion"/>.</summary>
        public Vector2 AversionOffset { get; private set; }

        /// <summary>This agent's head bone (gaze target for others).</summary>
        public Transform Head { get; private set; }

        /// <summary>
        /// Where the eyes are actually pointing, averaged over both. Measured from
        /// the bones rather than derived from <see cref="Target"/> so the gaze log
        /// records what was rendered, not what was commanded.
        /// </summary>
        public Vector3 GazeDirection => (_leftEye.forward + _rightEye.forward).normalized;

        /// <summary>Midpoint between the eyes — the origin of <see cref="GazeDirection"/>.</summary>
        public Vector3 GazeOrigin => (_leftEye.position + _rightEye.position) * 0.5f;

        Transform _leftEye;
        Transform _rightEye;
        float _headWeight;
        Quaternion _lastHeadLook = Quaternion.identity;
        Quaternion _baseHeadLocal = Quaternion.identity;
        Quaternion _writtenHeadLocal;
        bool _hasWrittenHead;

        void Awake()
        {
            Head = SmplxAnimUtils.FindChildRecursive(transform, "head");
            _leftEye = SmplxAnimUtils.FindChildRecursive(transform, "left_eye_smplhf");
            _rightEye = SmplxAnimUtils.FindChildRecursive(transform, "right_eye_smplhf");
            if (Head == null || _leftEye == null || _rightEye == null)
                Debug.LogError($"{name}: SMPL-X head/eye bones not found.", this);
        }

        /// <summary>Fixate <paramref name="target"/>, or return to neutral when it is null.</summary>
        public void SetTarget(Transform target)
        {
            Target = target;
            Mode = target != null ? GazeMode.Person : GazeMode.None;
        }

        /// <summary>
        /// Avert to <paramref name="yawPitchDegrees"/> relative to the head's
        /// forward direction. Aversion moves the eyes only: Shintani et al. split
        /// "look" from "look away" the same way, and it keeps a sampled direction
        /// from wrestling the mocap head every frame.
        /// </summary>
        public void SetAversion(Vector2 yawPitchDegrees)
        {
            Target = null;
            AversionOffset = new Vector2(
                Mathf.Clamp(yawPitchDegrees.x, -MaxEyeYaw, MaxEyeYaw),
                Mathf.Clamp(yawPitchDegrees.y, -MaxEyePitch, MaxEyePitch));
            Mode = GazeMode.Aversion;
        }

        void LateUpdate()
        {
            // The blend must not accumulate across frames. While mocap plays it
            // rewrites the head each Update, but once motion is frozen our own
            // blended value persists — detect that and restore the base rotation
            // first, so the slerp weight stays a true fraction in both cases.
            if (_hasWrittenHead && Head.localRotation == _writtenHeadLocal)
                Head.localRotation = _baseHeadLocal;
            else
                _baseHeadLocal = Head.localRotation;

            var headTarget = Mode == GazeMode.Person ? HeadContribution : 0f;
            _headWeight = Mathf.MoveTowards(_headWeight, headTarget, HeadBlendSpeed * Time.deltaTime);

            if (Mode == GazeMode.Person)
            {
                var headDir = (Target.position - Head.position).normalized;
                _lastHeadLook = Quaternion.LookRotation(headDir, Head.parent.up);
            }
            if (_headWeight > 0.001f)
                Head.rotation = Quaternion.Slerp(Head.rotation, _lastHeadLook, _headWeight);

            _writtenHeadLocal = Head.localRotation;
            _hasWrittenHead = true;

            // The head may have just moved, so the eyes re-solve against the pose
            // that will actually be rendered. That is the whole VOR story here:
            // the fixation point stays put while the head is still settling.
            RotateEye(_leftEye);
            RotateEye(_rightEye);
        }

        void RotateEye(Transform eye)
        {
            var maxStep = EyeDegreesPerSecond * Time.deltaTime;
            if (Mode == GazeMode.None)
            {
                eye.localRotation = Quaternion.RotateTowards(eye.localRotation, Quaternion.identity, maxStep);
                return;
            }

            var direction = Mode == GazeMode.Aversion
                ? DirectionInHead(AversionOffset.x, AversionOffset.y)
                : ClampToEyeRange((Target.position - eye.position).normalized);

            var look = Quaternion.LookRotation(direction, Head.up);
            eye.rotation = Quaternion.RotateTowards(eye.rotation, look, maxStep);
        }

        /// <summary>
        /// Clamp a world direction into the eyes' range. Yaw and pitch are clamped
        /// separately rather than as one cone: the human ocular range is far wider
        /// horizontally than vertically, and a cone clamp lets a target directly
        /// above the head pull the eyes to an implausible pitch.
        /// </summary>
        Vector3 ClampToEyeRange(Vector3 worldDirection)
        {
            var local = Quaternion.Inverse(Head.rotation) * worldDirection;
            var yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            var pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;

            return DirectionInHead(Mathf.Clamp(yaw, -MaxEyeYaw, MaxEyeYaw), Mathf.Clamp(pitch, -MaxEyePitch, MaxEyePitch));
        }

        /// <summary>
        /// World direction for a yaw/pitch pair measured from the head's forward
        /// direction. All SMPL-X joints have identity bind locals aligned to the
        /// model root, so the head's anatomical forward is simply its world
        /// rotation applied to <see cref="Vector3.forward"/>.
        /// </summary>
        Vector3 DirectionInHead(float yawDegrees, float pitchDegrees) =>
            Head.rotation * (Quaternion.Euler(-pitchDegrees, yawDegrees, 0f) * Vector3.forward);
    }
}
