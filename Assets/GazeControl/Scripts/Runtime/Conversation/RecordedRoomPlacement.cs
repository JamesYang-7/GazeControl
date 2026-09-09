using System.Collections.Generic;
using GazeControl.Study;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// Moves the recorded room onto the study rig for a clip whose bodies are
    /// placed by the recording rather than by scene vertices (study 2, the
    /// 3People-2022 clips).
    ///
    /// <para>This transform is the room: both agents sit under it with identity
    /// local transforms and their motion players in raw root-translation mode,
    /// so each body stands where its person stood in the capture, and this
    /// object is turned and slid as one piece so that the segment's seat (the
    /// apex of the equilateral triangle on the two agents, measured at export)
    /// lands on the participant's starting viewpoint and its opening facing
    /// lands on the participant's initial view direction
    /// (<see cref="RoomPlacement"/>). The participant never moves between
    /// clips; the room does.</para>
    ///
    /// <para>Applied by <see cref="RecordedConversation"/> as the segment
    /// loads, before the motion players are armed, so a bake and a session both
    /// see the placed room from the first frame.</para>
    /// </summary>
    public sealed class RecordedRoomPlacement : MonoBehaviour
    {
        [field: SerializeField]
        [field: Tooltip("The participant's fixed viewpoint — the User vertex. Its position and forward axis are "
                        + "what the participant's seat and facing are moved onto.")]
        public Transform Viewpoint { get; set; }

        /// <summary>The pose the room was last moved to; meaningful after <see cref="Apply"/> succeeds.</summary>
        public RoomPlacement.Pose LastPose { get; private set; }

        /// <summary>
        /// Move the room for <paramref name="segment"/> and put every speaker's
        /// motion player into raw root-translation mode.
        /// </summary>
        /// <returns>False with <paramref name="problem"/> set when the segment or the scene wiring cannot be placed.</returns>
        public bool Apply(DemoSegment segment, IReadOnlyList<RecordedConversation.Speaker> speakers, out string problem)
        {
            problem = null;

            if (segment == null || !segment.PlacesParticipant)
            {
                problem = "the segment carries no seat, so there is nothing to place the room by";
                return false;
            }

            if (Viewpoint == null)
            {
                problem = "no Viewpoint is assigned, so the room has nothing to be moved onto";
                return false;
            }

            foreach (var speaker in speakers)
            {
                if (speaker?.Motion == null)
                    continue;

                if (!speaker.Motion.transform.IsChildOf(transform))
                {
                    problem = $"{speaker.Motion.name} is not under the room, so moving the room would not move it";
                    return false;
                }

                // The clip places the body. Relative mode would anchor it on the
                // (identity) seat under this room instead, collapsing both
                // agents onto the room origin.
                speaker.Motion.RootTranslationRelative = false;
            }

            var forward = Viewpoint.forward;
            var pose = RoomPlacement.Solve(
                segment.seat.x, segment.seat.z, segment.seat.yawDegrees,
                Viewpoint.position.x, Viewpoint.position.z, RoomPlacement.YawOf(forward.x, forward.z));

            // The floor stays the floor: the recorded room is grounded per clip
            // and the rig's vertex sits on the floor, so only the horizontal
            // plane is moved.
            transform.SetPositionAndRotation(
                new Vector3(pose.X, Viewpoint.position.y, pose.Z),
                Quaternion.Euler(0f, pose.YawDegrees, 0f));

            LastPose = pose;
            return true;
        }
    }
}
