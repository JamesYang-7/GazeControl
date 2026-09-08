using System.Collections.Generic;
using UnityEngine;

namespace GazeControl.ThreeParty
{
    /// <summary>
    /// Where the study participant stands when they take the listener's place in
    /// a three-party clip, and which way they are facing when it starts.
    ///
    /// <para>Both are read off the recording rather than authored (user's call,
    /// 2026-09-08). The position is the <b>mean</b> eye position over the window,
    /// not the pose at any one frame: a single frame would put the participant
    /// wherever that person happened to be leaning, and the mean is the seat they
    /// occupied. The facing is the direction from there to the <b>midpoint of the
    /// two speakers' eyes at the first frame</b>, so the clip opens with both of
    /// them in view however the room was laid out.</para>
    ///
    /// <para><b>It is the converted body's head, not the recorded person's.</b>
    /// The conversion poses a default SMPL-X male (betas all zero) at the
    /// participant's real room position, so the horizontal seat is theirs but the
    /// height is the default body's — up to 17 cm of stature is thrown away, and
    /// on 12-15-2021 Crystal's own eyes were about 1.54 m against the 1.71 m this
    /// measures. Kept that way deliberately (user, 2026-09-08): <b>the gaze is
    /// derived by target, not by absolute angle</b>, so the flattened heights
    /// change the rendered angle and nothing that any policy or measure names.
    /// The participant also looks at what is <i>rendered</i>, and the two speakers
    /// are raised by their own ground offsets in the same way — putting the
    /// participant at the real person's height under default-bodied speakers
    /// would open a gap the recording did not have. Same argument as
    /// <c>ParticipantEyeHeight.SmplxEyeHeight</c> in the study scene. If
    /// per-participant shape is ever fitted, this needs no change — it measures
    /// the posed skeleton rather than storing a constant.</para>
    ///
    /// <para>Yaw only. Pitch and roll are reported against gravity by the
    /// headset and are already correct; turning them would tilt the virtual
    /// horizon away from the real one. This is the same rule
    /// <c>ViewRecentring</c> applies in the study scene, for the same
    /// reason.</para>
    ///
    /// <para>Eyes, not the head bone: the head bone sits inside the skull, and a
    /// viewpoint 6 cm behind the eyes reads as standing further back. The study
    /// rig measures its own eye height off the same two joints.</para>
    /// </summary>
    public readonly struct ThreePartyViewpoint
    {
        ThreePartyViewpoint(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }

        /// <summary>Mean eye position of the listener over the window, world space.</summary>
        public Vector3 Position { get; }

        /// <summary>Compass heading towards the two speakers, degrees.</summary>
        public float Yaw { get; }

        /// <summary>The opening head orientation: the yaw, level.</summary>
        public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Smallest horizontal distance below which a facing is refused rather than guessed, metres.</summary>
        const float MinimumSeparation = 0.05f;

        /// <summary>
        /// Place the participant in the listener's seat.
        /// </summary>
        /// <param name="listenerEyeSamples">The listener's eye position, sampled over the window.</param>
        /// <param name="firstSpeakerEyes">One speaker's eye position at the window's first frame.</param>
        /// <param name="secondSpeakerEyes">The other speaker's, at the same frame.</param>
        /// <returns>
        /// False when there is nothing to average, or when the seat is level with
        /// the two speakers' midpoint to within <see cref="MinimumSeparation"/> —
        /// a facing is undefined there, and a guessed one would silently point the
        /// participant at a wall.
        /// </returns>
        public static bool TryCompute(
            IReadOnlyList<Vector3> listenerEyeSamples,
            Vector3 firstSpeakerEyes,
            Vector3 secondSpeakerEyes,
            out ThreePartyViewpoint viewpoint)
        {
            viewpoint = default;
            if (listenerEyeSamples == null || listenerEyeSamples.Count == 0)
                return false;

            var position = Vector3.zero;
            foreach (var sample in listenerEyeSamples)
                position += sample;

            position /= listenerEyeSamples.Count;

            var target = 0.5f * (firstSpeakerEyes + secondSpeakerEyes);
            var toTarget = target - position;
            toTarget.y = 0f;
            if (toTarget.magnitude < MinimumSeparation)
                return false;

            // From the horizontal projection rather than from a look-rotation's
            // eulerAngles.y, which mixes roll into yaw near the poles.
            viewpoint = new ThreePartyViewpoint(position, Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg);
            return true;
        }
    }
}
