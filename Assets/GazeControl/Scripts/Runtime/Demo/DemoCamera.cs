using UnityEngine;

namespace GazeControl.Demo
{
    /// <summary>
    /// The camera a demo video is shot through: a locked-off second camera on the
    /// participant's sight line, pulled back and tilted down far enough to hold
    /// both agents' bodies in a 16:9 frame.
    ///
    /// <para><b>It is a second camera, not a moved one.</b> The participant's
    /// camera is what the agents aim at — it is the human's <c>LookAtAnchor</c> —
    /// so moving it for a nicer picture would change the rendered gaze of every
    /// take shot afterwards. This one renders and nothing looks at it.</para>
    ///
    /// <para><b>It refuses to render itself in Play Mode unless asked.</b> Two
    /// cameras are in the scene and this one has the higher depth, so a scene
    /// saved with it enabled would quietly shoot a participant session through
    /// the wrong lens — and in a headset, over the lens the headset needs. The
    /// editor's demo commands switch it on for the play session they start;
    /// nothing else can. In Edit Mode it stays as the scene left it, which is
    /// what puts the framing in the Game view while it is being tuned.</para>
    /// </summary>
    // Late, so the pose is written after anything that could move the vertex —
    // XR recentring in particular, which slides the play area under it.
    [ExecuteAlways]
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Camera))]
    public sealed class DemoCamera : MonoBehaviour
    {
        /// <summary>Below this, a pose is treated as unchanged and not written.</summary>
        const float PoseEpsilon = 1e-4f;

        [field: SerializeField]
        [field: Tooltip("The triad vertex the participant stands on — the User object. The shot is measured " +
                        "from here, not from the participant's camera, which moves with their head.")]
        public Transform Vertex { get; set; }

        [field: SerializeField]
        [field: Tooltip("The framing being tuned. An asset, so that numbers dragged while a clip plays survive " +
                        "leaving Play Mode.")]
        public DemoCameraFraming Framing { get; set; }

        Camera _camera;

        /// <summary>The camera this component aims.</summary>
        public Camera Camera
        {
            get
            {
                if (_camera == null)
                    _camera = GetComponent<Camera>();

                return _camera;
            }
        }

        /// <summary>Where the framing puts the camera right now, or the identity pose with nothing wired.</summary>
        public Pose FramedPose =>
            Vertex == null || Framing == null
                ? new Pose(transform.position, transform.rotation)
                : DemoCameraFrame.Place(
                    Vertex.position, Vertex.rotation,
                    Framing.PullBackMetres, Framing.LateralOffsetMetres,
                    Framing.HeightMetres, Framing.PitchDegrees);

        void OnEnable() => Apply();

        void Awake()
        {
            // Edit Mode runs this too under [ExecuteAlways], and there the scene's
            // own setting is the whole point of the component.
            if (!Application.isPlaying)
                return;

#if UNITY_EDITOR
            Camera.enabled = DemoViewRequest.Consume();
#else
            Camera.enabled = false; // demo rendering is an editor workflow
#endif
        }

        void LateUpdate() => Apply();

        void OnValidate() => Apply();

        /// <summary>Put the camera where the framing says, and give it the framing's lens.</summary>
        public void Apply()
        {
            if (Vertex == null || Framing == null)
                return;

            var pose = FramedPose;

            // Only written when it actually differs: an unconditional assignment
            // every editor tick marks the scene dirty for ever, and the file then
            // shows up modified in every commit with nothing changed in it.
            if (Vector3.Distance(transform.position, pose.position) > PoseEpsilon ||
                Quaternion.Angle(transform.rotation, pose.rotation) > PoseEpsilon)
            {
                transform.SetPositionAndRotation(pose.position, pose.rotation);
            }

            var camera = Camera;
            if (!Mathf.Approximately(camera.fieldOfView, Framing.VerticalFieldOfView))
                camera.fieldOfView = Framing.VerticalFieldOfView;
        }

        /// <summary>
        /// What the frame covers vertically where <paramref name="worldPoint"/>
        /// stands — the readout that says whether an agent's head and feet are in
        /// the picture.
        /// </summary>
        public DemoCameraFrame.Span SpanAt(Vector3 worldPoint)
        {
            if (Framing == null)
                return new DemoCameraFrame.Span(0f, 0f);

            return DemoCameraFrame.VerticalSpanAt(
                HorizontalDistanceTo(worldPoint),
                Framing.HeightMetres,
                Framing.PitchDegrees,
                Framing.VerticalFieldOfView);
        }

        /// <summary>Metres from the lens to <paramref name="worldPoint"/> along the sight line, ignoring height.</summary>
        public float HorizontalDistanceTo(Vector3 worldPoint)
        {
            var sightLine = Vertex == null
                ? DemoCameraFrame.SightLine(transform.rotation)
                : DemoCameraFrame.SightLine(Vertex.rotation);

            var toPoint = worldPoint - transform.position;
            toPoint.y = 0f;
            return Vector3.Dot(toPoint, sightLine);
        }
    }
}
