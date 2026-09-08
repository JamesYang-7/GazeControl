using System;
using System.IO;
using UnityEngine;

namespace GazeControl.Conversation
{
    /// <summary>
    /// A stretch of a TalkingWithHands conversation, chosen to carry one demo:
    /// where it sits in the recording, the trimmed audio, the turn schedule and
    /// the end-of-turn events inside it.
    ///
    /// Written by <c>Tools/find_demo_segments.py</c>, which is also where the
    /// selection rules live (20-30 s, at least two events, no sentence cut in
    /// half, events far enough apart for their gaze patterns not to collide).
    /// Every time in this file is measured from the start of the segment, so
    /// nothing downstream has to know where in the recording it came from.
    /// </summary>
    [Serializable]
    public sealed class DemoSegment
    {
        /// <summary>Speaker code for the human user in turns and events; 1 and 2 are the corpus's.</summary>
        public const int UserSpeakerCode = 0;

        /// <summary>Format tag; bumped if the schema ever changes incompatibly.</summary>
        public string schema;

        /// <summary>Export folder name, e.g. <c>case1_seg01</c>.</summary>
        public string name;

        /// <summary>Source conversation, e.g. <c>trn_2023_v0_038</c>, or a 3People session id.</summary>
        public string stem;

        /// <summary>Which corpus the segment is cut from; empty in a schema/1 or /2 file, which is TalkingWithHands.</summary>
        public string corpus;

        public float sourceStartSeconds;
        public float sourceEndSeconds;
        public float durationSeconds;

        public int motionFrameRate;

        /// <summary>First frame of the 60 fps grounded npz that belongs to this segment.</summary>
        public int motionStartFrame;

        public int motionFrameCount;

        /// <summary>
        /// Scene 2: the final turn's addressee is the user (<see cref="UserSpeakerCode"/>)
        /// and carries the yield event's real index, so the pre-turn pattern
        /// fires at the yield. Absent in a scene-1 file, which JsonUtility
        /// defaults to false.
        /// </summary>
        public bool yieldsToUser;

        /// <summary>Seconds the recorder holds after the voices stop; 0 means its default.</summary>
        public float tailSeconds;

        /// <summary>
        /// Schema/3 (3People-2022): the listener's seat, measured from the
        /// recording, that the participant takes. Null or invalid in a
        /// TalkingWithHands file, whose agents are placed by the scene's vertices.
        /// </summary>
        public DemoSegmentSeat seat;

        /// <summary>True when the clips place the bodies and the scene must move the room onto the rig.</summary>
        public bool PlacesParticipant => seat != null && seat.valid;

        public DemoSegmentAgent[] agents;
        public DemoSegmentTurn[] turns;
        public DemoSegmentEvent[] events;
        public DemoSegmentUtterance[] utterances;

        /// <summary>Parse a segment document; the caller supplies path context on failure.</summary>
        public static DemoSegment Parse(string json)
        {
            var segment = JsonUtility.FromJson<DemoSegment>(json);
            if (segment == null || segment.turns == null || segment.turns.Length == 0)
                throw new InvalidDataException("not a demo segment (no turn schedule)");

            return segment;
        }

        /// <summary>Read a segment written by the selection tool.</summary>
        /// <param name="path">Absolute, or relative to the project root.</param>
        public static DemoSegment Load(string path)
        {
            var full = Path.IsPathRooted(path)
                ? path
                : Path.Combine(Application.dataPath, "..", path);

            if (!File.Exists(full))
                throw new FileNotFoundException($"demo segment not found: {full}", full);

            try
            {
                return Parse(File.ReadAllText(full));
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"{path}: {e.Message}");
            }
        }

        /// <summary>The record for one speaker, or null if the segment has no such speaker.</summary>
        public DemoSegmentAgent AgentOf(int speaker)
        {
            foreach (var agent in agents)
            {
                if (agent.speaker == speaker)
                    return agent;
            }

            return null;
        }
    }

    /// <summary>One side of the recorded conversation and the media that drive it.</summary>
    [Serializable]
    public sealed class DemoSegmentAgent
    {
        /// <summary>Corpus speaker code: 1 is main-agent, 2 is interloctr.</summary>
        public int speaker;

        public string side;

        /// <summary>Trimmed wav, project-relative.</summary>
        public string audio;

        /// <summary>Full 60 fps grounded npz; the segment is selected with the frame offset.</summary>
        public string motion;

        public int audioSamples;

        /// <summary>
        /// Median F0 over this speaker's own voiced frames, measured by the
        /// exporter because the corpus annotates nothing about who is speaking.
        /// 0 in schema/1 files, which predate the measurement.
        /// </summary>
        public float voicePitchHz;

        /// <summary>"male", "female" or "unclear", derived from <see cref="voicePitchHz"/>.</summary>
        public string voice;

        /// <summary>3People-2022 only: the true PC number behind the remapped speaker code; 0 otherwise.</summary>
        public int pc;

        /// <summary>3People-2022 only: the recorded person's name.</summary>
        public string subject;
    }

    /// <summary>
    /// Where the listener of a three-party clip sat and which way they faced
    /// when the window opens, in Unity's frame of the recorded room: the mean
    /// eye position over the window, and the yaw towards the midpoint of the two
    /// takers' eyes at the first frame (<c>Tools/export_3people_segments.py</c>).
    /// </summary>
    [Serializable]
    public sealed class DemoSegmentSeat
    {
        public bool valid;
        public float x;
        public float y;
        public float z;

        /// <summary>Degrees clockwise from +Z, seen from above.</summary>
        public float yawDegrees;
    }

    /// <summary>
    /// Who holds the floor between two boundaries. Derived from the events: the
    /// turn ending at event <c>k</c> is held by that event's first speaker and
    /// taken by its second. In a scene-1 segment the final turn runs past the
    /// last event and carries <c>eventIndex = -1</c> — nothing is known about
    /// the boundary past the clip, so no gaze pattern fires there. In a
    /// scene-2 segment (<see cref="DemoSegment.yieldsToUser"/>) the final turn
    /// is the yield itself: addressed to <see cref="DemoSegment.UserSpeakerCode"/>
    /// and carrying the yield event's real index.
    /// </summary>
    [Serializable]
    public sealed class DemoSegmentTurn
    {
        public int speaker;
        public int addressee;
        public float startTime;
        public float endTime;
        public int eventIndex;
    }

    /// <summary>One end-of-turn event, from <c>EoT_TWH</c>.</summary>
    [Serializable]
    public sealed class DemoSegmentEvent
    {
        public int index;

        /// <summary>1 interruption, 2 overlapping, 3 turn-taking.</summary>
        public int eotType;

        public string eotTypeName;
        public int firstSpeaker;
        public int secondSpeaker;

        /// <summary>The transition instant — what a pre-turn pattern is anchored on.</summary>
        public float turnTime;

        public float startTime;
        public float endTime;
    }

    /// <summary>One utterance, for captioning and for checking a segment by eye.</summary>
    [Serializable]
    public sealed class DemoSegmentUtterance
    {
        public int speaker;
        public float startTime;
        public float endTime;
        public string text;
    }
}
