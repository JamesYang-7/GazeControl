using GazeControl.Gaze;
using GazeControl.Gaze.Policy;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// One member of the triad — an agent or the human user — as seen by the gaze
    /// condition runner: a stable id, somewhere for others to look at, and (for
    /// agents) the gaze actuator and voice to observe.
    /// </summary>
    public class GazeParticipant : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("Stable index written to the gaze log; must be unique and must not change mid-session")]
        public int Id { get; set; }

        [field: SerializeField]
        [field: Tooltip("Human-readable name for the log, e.g. AgentA")]
        public string DisplayName { get; set; }

        [field: SerializeField]
        [field: Tooltip("The human user: has no gaze controller, and their gaze cannot be measured without eye tracking")]
        public bool IsHuman { get; set; }

        [field: SerializeField]
        [field: Tooltip("Where other participants aim when looking at this one (the head bone)")]
        public Transform LookAtAnchor { get; set; }

        [field: SerializeField]
        [field: Tooltip("Gaze actuator; empty for the human user")]
        public GazeController Gaze { get; set; }

        [field: SerializeField]
        [field: Tooltip("Voice observed by the VAD; empty for the human user (no microphone)")]
        public AudioSource Voice { get; set; }

        [field: SerializeField]
        [field: Tooltip("Who this participant addresses while speaking (the dialogue manager's addressee)")]
        public GazeParticipant DefaultAddressee { get; set; }

        public ParticipantId ParticipantId => new(Id);

        /// <summary>True when this participant is driven by a gaze policy.</summary>
        public bool IsAgent => !IsHuman && Gaze != null;
    }
}
