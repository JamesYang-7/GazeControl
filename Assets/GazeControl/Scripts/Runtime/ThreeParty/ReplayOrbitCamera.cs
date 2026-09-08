using UnityEngine;
using UnityEngine.InputSystem;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// The replay's camera, in two modes.
    ///
    /// <para><b>Orbit</b> walks around the triad. Judging a recorded
    /// conversation means looking at faces, and the three participants face each
    /// other rather than the viewer — so a fixed camera always has someone's
    /// back to it. Drag to turn, scroll to close in, middle-drag to slide the
    /// pivot; it keeps itself centred on the three heads, so a session with a
    /// different room layout is framed without touching anything.</para>
    ///
    /// <para><b>Listener seat</b> (press V) is the study's viewpoint: the camera
    /// stands where the listener's eyes were, on average, over the window, and
    /// opens facing the midpoint of the two speakers. Dragging turns the head
    /// from that fixed point and moves it nowhere, which is exactly what the
    /// headset gives a participant — the study rig tracks head rotation and not
    /// head translation. The listener's own body is hidden while this mode is
    /// on, because in the study the person standing there is the
    /// participant.</para>
    ///
    /// <para>The study scene's camera is fixed and this one is not, deliberately:
    /// there the viewpoint is a controlled variable. Nothing here is measured, so
    /// nothing here has to be.</para>
    /// </summary>
    public sealed class ReplayOrbitCamera : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("The replay whose participants this camera frames; empty to keep the pivot where it is")]
        public ThreePartyReplay Replay { get; set; }

        [field: SerializeField]
        [field: Tooltip("What the camera orbits, world space. Recentred on the three heads while a session plays.")]
        public Vector3 Pivot { get; set; } = new(0f, 1.5f, 0f);

        [field: SerializeField]
        [field: Tooltip("Distance from the pivot, metres")]
        public float Distance { get; set; } = 3.5f;

        [field: SerializeField]
        [field: Tooltip("Compass heading, degrees")]
        public float Yaw { get; set; } = 180f;

        [field: SerializeField]
        [field: Tooltip("Elevation, degrees; positive looks down")]
        public float Pitch { get; set; } = 8f;

        [field: SerializeField]
        [field: Tooltip("Degrees per pixel of drag")]
        public float OrbitSensitivity { get; set; } = 0.25f;

        [field: SerializeField]
        [field: Tooltip("Metres per notch of scroll")]
        public float ZoomSensitivity { get; set; } = 0.3f;

        [field: SerializeField]
        [field: Tooltip("Keep the pivot on the mean of the three heads")]
        public bool FollowParticipants { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Toggles between orbiting the triad and standing in the listener's seat")]
        public Key SeatViewKey { get; set; } = Key.V;

        const float k_MinPitch = -80f;
        const float k_MaxPitch = 80f;
        const float k_MinDistance = 0.6f;
        const float k_MaxDistance = 12f;

        /// <summary>Where the head is pointed relative to the seat's own facing, degrees.</summary>
        float _seatYaw;
        float _seatPitch;

        /// <summary>True while the camera is standing in the listener's seat.</summary>
        public bool InListenerSeat => Replay != null && Replay.ViewFromListenerSeat && Replay.HasViewpoint;

        void LateUpdate()
        {
            ReadSeatViewKey();

            if (InListenerSeat)
            {
                UpdateListenerSeat();
                return;
            }

            if (FollowParticipants && TryHeadCentroid(out var centroid))
                Pivot = centroid;

            ReadMouse();

            var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            transform.SetPositionAndRotation(Pivot + rotation * new Vector3(0f, 0f, -Distance), rotation);
        }

        void ReadSeatViewKey()
        {
            if (Replay == null || Keyboard.current == null || !Keyboard.current[SeatViewKey].wasPressedThisFrame)
                return;

            Replay.ViewFromListenerSeat = !Replay.ViewFromListenerSeat;
            if (!Replay.ViewFromListenerSeat)
                return;

            // Face where the seat faces on every entry, rather than resuming the
            // last look direction: the opening framing is the thing being judged.
            _seatYaw = 0f;
            _seatPitch = 0f;

            if (!Replay.HasViewpoint)
                Debug.LogWarning($"{name}: this window singles out no listener, so it has no seat to stand in.", this);
        }

        void UpdateListenerSeat()
        {
            var mouse = Mouse.current;
            if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed))
            {
                var delta = mouse.delta.ReadValue();
                _seatYaw += delta.x * OrbitSensitivity;
                _seatPitch = Mathf.Clamp(_seatPitch - delta.y * OrbitSensitivity, k_MinPitch, k_MaxPitch);
            }

            var viewpoint = Replay.Viewpoint;
            transform.SetPositionAndRotation(
                viewpoint.Position, Quaternion.Euler(_seatPitch, viewpoint.Yaw + _seatYaw, 0f));
        }

        void ReadMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var delta = mouse.delta.ReadValue();

            if (mouse.leftButton.isPressed || mouse.rightButton.isPressed)
            {
                Yaw += delta.x * OrbitSensitivity;
                Pitch = Mathf.Clamp(Pitch - delta.y * OrbitSensitivity, k_MinPitch, k_MaxPitch);
            }

            if (mouse.middleButton.isPressed)
            {
                // Panning moves the pivot, which is only meaningful once it has
                // stopped being recomputed from the heads every frame.
                FollowParticipants = false;
                var rotation = Quaternion.Euler(Pitch, Yaw, 0f);
                Pivot -= rotation * new Vector3(delta.x, delta.y, 0f) * (0.002f * Distance);
            }

            var scroll = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
                Distance = Mathf.Clamp(Distance - Mathf.Sign(scroll) * ZoomSensitivity, k_MinDistance, k_MaxDistance);
        }

        bool TryHeadCentroid(out Vector3 centroid)
        {
            centroid = Vector3.zero;
            if (Replay == null || Replay.Seats == null)
                return false;

            var found = 0;
            foreach (var seat in Replay.Seats)
            {
                if (seat.Head == null)
                    continue;

                centroid += seat.Head.position;
                found++;
            }

            if (found == 0)
                return false;

            centroid /= found;
            return true;
        }
    }
}
