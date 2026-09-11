using UnityEngine;

namespace GazeControl.Demo
{
    /// <summary>
    /// The five numbers that decide what a rendered demo frame shows. Tuned by
    /// eye against a clip that is actually playing, through
    /// <c>GazeControl → Demo Video → Camera Framing</c>.
    ///
    /// <para><b>An asset rather than fields on the camera component</b>, because
    /// of when the tuning happens: the agents are only in a real pose while a
    /// clip is playing, so the numbers are dragged in Play Mode — and every
    /// change made to a scene object in Play Mode is thrown away when Play Mode
    /// exits. Asset edits are not. Putting them on <see cref="DemoCamera"/> would
    /// mean tuning a shot, watching it, liking it, and losing it.</para>
    ///
    /// <para><b>One framing for every clip</b> (user's call). The five session
    /// clips are recordings of real rooms, so the two agents stand 1.14-1.62 m
    /// from the participant's seat depending on the clip, and a fixed camera
    /// therefore renders them a fifth larger in the widest room than in the
    /// narrowest. That variation is the recording's and is left visible; framing
    /// each clip separately would make the shot itself a variable between
    /// conditions the video is meant to compare.</para>
    /// </summary>
    public sealed class DemoCameraFraming : ScriptableObject
    {
        /// <summary>
        /// Metres to step back from the participant's vertex along their sight
        /// line. The agents stand about a metre away, which is far too close for
        /// a flat frame to hold both of them.
        /// </summary>
        [field: SerializeField]
        [field: Range(0f, 4f)]
        [field: Tooltip("Metres behind the participant's vertex, along their sight line. " +
                        "The agents are only about a metre from the seat, so a frame shot from the seat itself " +
                        "cannot hold both of them without a very wide lens.")]
        public float PullBackMetres { get; set; } = 1f;

        /// <summary>Camera height above the floor plane at y = 0, metres.</summary>
        [field: SerializeField]
        [field: Range(0.5f, 3f)]
        [field: Tooltip("Camera height above the floor, metres. The agents' own eye line is 1.6846 m; " +
                        "staying on it keeps the shot at conversational height rather than looking down on them.")]
        public float HeightMetres { get; set; } = 1.6846f;

        /// <summary>
        /// Downward tilt, degrees. What buys the bodies their room in the frame:
        /// level at eye height, a 16:9 picture spends its top third on the
        /// ceiling.
        /// </summary>
        [field: SerializeField]
        [field: Range(-10f, 30f)]
        [field: Tooltip("Downward tilt in degrees. Positive tips the lens towards the floor, trading empty " +
                        "space above the agents' heads for more of their bodies.")]
        public float PitchDegrees { get; set; } = 10f;

        /// <summary>Vertical field of view, degrees — what Unity's camera takes.</summary>
        [field: SerializeField]
        [field: Range(15f, 80f)]
        [field: Tooltip("Vertical field of view in degrees. Wider fits more body in but exaggerates the " +
                        "perspective at this distance; the framing window reports the horizontal angle it implies.")]
        public float VerticalFieldOfView { get; set; } = 42f;

        /// <summary>
        /// Metres to step to the participant's right. Zero keeps the shot on the
        /// sight line, which is what makes an agent looking at the participant
        /// read as looking at the viewer.
        /// </summary>
        [field: SerializeField]
        [field: Range(-1.5f, 1.5f)]
        [field: Tooltip("Metres to the participant's right. Leave at zero unless a clip's room needs it: " +
                        "off the sight line, an agent looking at the participant stops reading as looking at the viewer.")]
        public float LateralOffsetMetres { get; set; }
    }
}
