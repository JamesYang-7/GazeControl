using System.Threading;
using GazeControl.Motion;
using UnityEngine;

namespace GazeControl.Gaze
{
    /// <summary>
    /// Drives an agent's gaze target: eye bones look at the target (clamped to a
    /// plausible range), with a partial head turn on top. Runs in LateUpdate so it
    /// layers over the mocap pose that <see cref="SmplxMotionPlayer"/> writes in
    /// Update; head blending is weight-based (not accumulated) because the mocap
    /// rewrites the head rotation every frame. A null target means gaze "None":
    /// eyes return to neutral and the head returns to pure mocap.
    /// </summary>
    public class GazeController : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Eye rotation speed; saccades are near-instant, keep high")]
        public float EyeDegreesPerSecond { get; set; } = 600f;

        [field: SerializeField]
        [field: Range(0f, 60f)]
        [field: Tooltip("Max eye deviation from the head's forward direction")]
        public float MaxEyeAngle { get; set; } = 35f;

        [field: SerializeField]
        [field: Range(0f, 1f)]
        [field: Tooltip("How much the head turns toward the gaze target (0 = eyes only)")]
        public float HeadContribution { get; set; } = 0.3f;

        [field: SerializeField]
        [field: Tooltip("Smoothing rate of the head contribution weight, 1/s")]
        public float HeadBlendSpeed { get; set; } = 3f;

        /// <summary>Current gaze target; null = "None" (neutral eyes, mocap head).</summary>
        public Transform Target { get; private set; }

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

        public void SetTarget(Transform target) => Target = target;

        /// <summary>Look at <paramref name="target"/> for <paramref name="seconds"/>, then return to the previous target.</summary>
        public async Awaitable GlanceAsync(Transform target, float seconds, CancellationToken ct)
        {
            var previous = Target;
            SetTarget(target);
            await Awaitable.WaitForSecondsAsync(seconds, ct);
            SetTarget(previous);
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

            // All SMPL-X joints have identity bind locals aligned to the model root,
            // so a joint's anatomical forward is its world rotation * Vector3.forward.
            var headForward = Head.rotation * Vector3.forward;

            _headWeight = Mathf.MoveTowards(_headWeight, Target != null ? HeadContribution : 0f, HeadBlendSpeed * Time.deltaTime);

            if (Target != null)
            {
                var headDir = (Target.position - Head.position).normalized;
                _lastHeadLook = Quaternion.LookRotation(headDir, Head.parent.up);
            }
            if (_headWeight > 0.001f)
                Head.rotation = Quaternion.Slerp(Head.rotation, _lastHeadLook, _headWeight);

            _writtenHeadLocal = Head.localRotation;
            _hasWrittenHead = true;

            RotateEye(_leftEye, headForward);
            RotateEye(_rightEye, headForward);
        }

        void RotateEye(Transform eye, Vector3 headForward)
        {
            var maxStep = EyeDegreesPerSecond * Time.deltaTime;
            if (Target == null)
            {
                eye.localRotation = Quaternion.RotateTowards(eye.localRotation, Quaternion.identity, maxStep);
                return;
            }

            var dir = (Target.position - eye.position).normalized;
            var maxRadians = MaxEyeAngle * Mathf.Deg2Rad;
            dir = Vector3.RotateTowards(headForward, dir, maxRadians, 0f); // clamp to eye range
            var look = Quaternion.LookRotation(dir, Head.up);
            eye.rotation = Quaternion.RotateTowards(eye.rotation, look, maxStep);
        }
    }
}
