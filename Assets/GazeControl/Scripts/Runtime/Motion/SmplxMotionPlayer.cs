using UnityEngine;

namespace GazeControl.Motion
{
    /// <summary>
    /// Plays a SMPL-X .npz motion clip on the SMPL-X skeleton found under this
    /// GameObject. Attach to an agent root (the FBX instance must be a descendant),
    /// set <see cref="ClipPath"/>, and enter Play Mode.
    /// </summary>
    public class SmplxMotionPlayer : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Path to the SMPL-X .npz clip, absolute or relative to the project root, e.g. Assets/MotionData/TalkingWithHands/trn_2023_v0_000_main-agent_000.npz")]
        public string ClipPath { get; set; }

        [field: SerializeField]
        [field: Tooltip("Restart from frame 0 when the clip ends")]
        public bool Loop { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Move the pelvis with the clip's root translation ('trans')")]
        public bool ApplyRootTranslation { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Apply root translation relative to the clip's first frame, keeping the agent anchored where it was placed in the scene. Off = raw dataset positions.")]
        public bool RootTranslationRelative { get; set; } = true;

        public SmplxMotionClip Clip { get; private set; }

        Transform[] _joints;
        Vector3 _pelvisBindLocalPosition;
        float _time;

        void Start()
        {
            Initialize();
        }

        void Update()
        {
            if (Clip == null) return;

            _time += Time.deltaTime;
            var frame = (int)(_time * Clip.FrameRate);
            if (frame >= Clip.FrameCount)
            {
                if (!Loop) return;
                _time %= Clip.Duration;
                frame %= Clip.FrameCount;
            }

            ApplyFrame(frame);
        }

        /// <summary>Load the clip and bind the skeleton. Safe to call again after changing <see cref="ClipPath"/>.</summary>
        public void Initialize()
        {
            _joints = new Transform[SmplxAnimUtils.JointCount];
            if (!SmplxAnimUtils.BindJoints(transform, _joints))
            {
                _joints = null;
                return;
            }

            _pelvisBindLocalPosition = _joints[0].localPosition;
            _time = 0f;
            Clip = SmplxMotionClip.Load(ClipPath);
            Debug.Log($"{name}: loaded '{ClipPath}' ({Clip.FrameCount} frames @ {Clip.FrameRate} fps, {Clip.Duration:F1} s)", this);
        }

        /// <summary>Pose the skeleton at one clip frame (also usable from editor scripts for scrubbing).</summary>
        public void ApplyFrame(int frame)
        {
            SmplxAnimUtils.SetPose(_joints, Clip, frame);

            if (!ApplyRootTranslation) return;
            var trans = SmplxAnimUtils.PositionToUnity(Clip.GetTranslation(frame));
            // Relative mode keeps the agent at its authored scene position (the triad
            // vertex) and adds only the motion since frame 0; raw dataset positions
            // would teleport it to wherever the capture happened.
            _joints[0].localPosition = RootTranslationRelative
                ? _pelvisBindLocalPosition + trans - SmplxAnimUtils.PositionToUnity(Clip.GetTranslation(0))
                : trans;
        }
    }
}
