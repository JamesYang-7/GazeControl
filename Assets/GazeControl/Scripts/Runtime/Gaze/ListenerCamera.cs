using UnityEngine;

namespace GazeControl.Gaze
{
    /// <summary>
    /// Rotates the user's camera to follow the "listener" gaze track from the
    /// collected patterns (the user is the listener in demo case 1): steady on
    /// whoever it is told to look at, turning at a natural head speed.
    /// </summary>
    public class ListenerCamera : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Head turn speed of the user view")]
        public float DegreesPerSecond { get; set; } = 150f;

        /// <summary>What the user is looking at; null keeps the current orientation.</summary>
        public Transform Target { get; private set; }

        public void SetTarget(Transform target) => Target = target;

        void LateUpdate()
        {
            if (Target == null)
                return;
            var look = Quaternion.LookRotation(Target.position - transform.position, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, DegreesPerSecond * Time.deltaTime);
        }
    }
}
