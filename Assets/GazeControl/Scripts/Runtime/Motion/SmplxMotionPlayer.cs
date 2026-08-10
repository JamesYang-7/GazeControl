using UnityEngine;

namespace GazeControl.Motion
{
    /// <summary>
    /// Plays a SMPL-X .npz motion clip on the SMPL-X skeleton found under this
    /// GameObject. Attach to an agent root (the FBX instance must be a descendant),
    /// set <see cref="ClipPath"/>, and enter Play Mode.
    ///
    /// A clip can be played whole or as a segment: the demo takes a 20-30 s
    /// stretch out of a several-minute recording, so <see cref="StartFrame"/> and
    /// <see cref="FrameCount"/> select it. When something else owns the clock —
    /// the recorded conversation drives motion and audio from one timeline —
    /// clear <see cref="Autoplay"/> and call <see cref="Seek"/> instead.
    /// </summary>
    public class SmplxMotionPlayer : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Path to the SMPL-X .npz clip, absolute or relative to the project root, e.g. F:/Data/TalkingWithHandsCentered/SMPLX-60fps-grounded/trn_2023_v0_038_main-agent.npz")]
        public string ClipPath { get; set; }

        [field: SerializeField]
        [field: Tooltip("First clip frame to play; the demo segment's offset into the full recording")]
        public int StartFrame { get; set; }

        [field: SerializeField]
        [field: Tooltip("How many frames to play from Start Frame; 0 plays to the end of the clip")]
        public int FrameCount { get; set; }

        [field: SerializeField]
        [field: Tooltip("Restart from the segment's first frame when it ends")]
        public bool Loop { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Advance on this component's own clock. Clear it when an external timeline calls Seek.")]
        public bool Autoplay { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Move the pelvis with the clip's root translation ('trans')")]
        public bool ApplyRootTranslation { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Apply root translation relative to the segment's first frame, keeping the agent anchored where it was placed in the scene. Off = raw dataset positions.")]
        public bool RootTranslationRelative { get; set; } = true;

        public SmplxMotionClip Clip { get; private set; }

        /// <summary>Frames in the segment being played — the whole clip unless one was selected.</summary>
        public int SegmentFrameCount { get; private set; }

        /// <summary>Length of the segment being played, seconds.</summary>
        public float SegmentDuration => Clip == null ? 0f : SegmentFrameCount / Clip.FrameRate;

        Transform[] _joints;
        Vector3 _pelvisBindLocalPosition;
        Vector3 _rootAnchor;
        float _time;

        void Start()
        {
            if (Clip == null)
                Initialize();
        }

        void Update()
        {
            if (Clip == null || !Autoplay) return;

            _time += Time.deltaTime;
            Seek(_time);
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

            StartFrame = Mathf.Clamp(StartFrame, 0, Clip.FrameCount - 1);
            var available = Clip.FrameCount - StartFrame;
            SegmentFrameCount = FrameCount > 0 ? Mathf.Min(FrameCount, available) : available;

            // The segment's own first frame is the anchor, not the clip's: several
            // minutes in, the recorded root has wandered far from where it started,
            // and anchoring on frame 0 would drop the agent that far off its vertex.
            _rootAnchor = SmplxAnimUtils.PositionToUnity(Clip.GetTranslation(StartFrame));

            Debug.Log(
                $"{name}: loaded '{ClipPath}' ({Clip.FrameCount} frames @ {Clip.FrameRate} fps); " +
                $"playing {SegmentFrameCount} frames from {StartFrame} ({SegmentDuration:F1} s)", this);
        }

        /// <summary>Pose the skeleton at <paramref name="seconds"/> into the segment.</summary>
        public void Seek(float seconds)
        {
            if (Clip == null) return;

            var frame = (int)(seconds * Clip.FrameRate);
            if (frame >= SegmentFrameCount)
            {
                if (!Loop)
                {
                    ApplyFrame(StartFrame + SegmentFrameCount - 1);
                    return;
                }

                frame %= SegmentFrameCount;
            }

            ApplyFrame(StartFrame + Mathf.Max(frame, 0));
        }

        /// <summary>Pose the skeleton at one clip frame (also usable from editor scripts for scrubbing).</summary>
        public void ApplyFrame(int frame)
        {
            SmplxAnimUtils.SetPose(_joints, Clip, frame);

            if (!ApplyRootTranslation) return;
            var trans = SmplxAnimUtils.PositionToUnity(Clip.GetTranslation(frame));
            // Relative mode keeps the agent at its authored scene position (the triad
            // vertex) and adds only the motion since the segment started; raw dataset
            // positions would teleport it to wherever the capture happened.
            _joints[0].localPosition = RootTranslationRelative
                ? _pelvisBindLocalPosition + trans - _rootAnchor
                : trans;
        }
    }
}
