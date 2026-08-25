using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// A recorded gaze decision sequence for one take: what every agent looked
    /// at, stamped with the conversation time it was decided at.
    ///
    /// <para>Baked once and replayed for every study participant, so the
    /// stimulus is identical by construction rather than by argument. This is
    /// what makes the study's within-subject comparison honest: a participant
    /// ranking three conditions must be ranking the policies, not three
    /// different draws of one policy.</para>
    ///
    /// <para><b>Samples carry a timestamp rather than an index.</b> That is the
    /// whole point. Indexing by tick number reproduces the bug this replaces:
    /// the runner ticks from the moment Play starts while the conversation's
    /// clock only starts once the segment has loaded and both voices are
    /// scheduled, and that gap varies with load time — so tick <c>n</c> falls on
    /// a different moment of the conversation in every run. Sampling by
    /// conversation time is immune to load time, frame rate and tick alignment
    /// alike.</para>
    ///
    /// <para>Baking is only sound because no policy input depends on the human
    /// participant: voice activity comes from the agents' own clips, the turn
    /// schedule from the segment, and mutual gaze from the agents' rendered eye
    /// bones. If an agent is ever made to react to the participant's gaze, a
    /// baked track stops being equivalent and this must be revisited.</para>
    /// </summary>
    [Serializable]
    public sealed class GazeTrack
    {
        public const string CurrentSchema = "gazecontrol.gaze-track/1";

        public string schema = CurrentSchema;

        /// <summary>Segment this was baked against; replaying it on another is refused.</summary>
        public string segment;

        public string condition;
        public string substrate;
        public int baseSeed;
        public float decisionHz;
        public string bakedAtUtc;

        /// <summary>Provenance for a reader who finds the file alone.</summary>
        public string generator = "GazeConditionRunner, TrackMode.Bake";

        public GazeTrackAgent[] agents = Array.Empty<GazeTrackAgent>();

        public static GazeTrack Parse(string json)
        {
            var track = JsonUtility.FromJson<GazeTrack>(json);

            if (track == null || string.IsNullOrEmpty(track.schema))
                throw new ArgumentException("Not a gaze track: no schema field.");

            if (track.schema != CurrentSchema)
                throw new ArgumentException($"Unsupported gaze track schema '{track.schema}'; expected '{CurrentSchema}'.");

            if (track.agents.Length == 0)
                throw new ArgumentException("Gaze track carries no agents.");

            foreach (var agent in track.agents)
            {
                if (agent.samples.Length == 0)
                    throw new ArgumentException($"Gaze track agent {agent.agentId} carries no samples.");
            }

            return track;
        }

        public static GazeTrack Load(string path) => Parse(File.ReadAllText(path));

        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonUtility.ToJson(this, prettyPrint: true));
        }

        public GazeTrackAgent AgentOf(int agentId)
        {
            foreach (var agent in agents)
            {
                if (agent.agentId == agentId)
                    return agent;
            }

            return null;
        }

        /// <summary>Where a take's track lives: beside the segment it replays.</summary>
        public static string PathFor(string segmentDirectory, string condition, int baseSeed) =>
            Path.Combine(segmentDirectory, $"gazetrack_{condition}_seed{baseSeed}.json").Replace('\\', '/');
    }

    /// <summary>One agent's decision sequence.</summary>
    [Serializable]
    public sealed class GazeTrackAgent
    {
        public int agentId;
        public string name;

        /// <summary>The per-agent seed the bake ran with, for provenance only.</summary>
        public int seed;

        public GazeTrackSample[] samples = Array.Empty<GazeTrackSample>();
    }

    /// <summary>
    /// One decision, stamped with the conversation time it was taken at.
    /// Flattened to primitives because <see cref="JsonUtility"/> is the only
    /// serializer this assembly has, and a take is a few thousand of these.
    /// </summary>
    [Serializable]
    public struct GazeTrackSample
    {
        /// <summary>Conversation elapsed time, seconds.</summary>
        public float t;

        /// <summary>0 = person, 1 = aversion. Mirrors <see cref="GazeTargetType"/>.</summary>
        public int type;

        /// <summary>Participant id when <see cref="type"/> is a person.</summary>
        public int person;

        public float yaw;
        public float pitch;

        public static GazeTrackSample From(float time, in GazeTarget target) => new()
        {
            t = time,
            type = target.Type == GazeTargetType.Aversion ? 1 : 0,
            person = target.Type == GazeTargetType.Aversion ? -1 : target.Person.Index,
            yaw = target.AversionOffset.x,
            pitch = target.AversionOffset.y,
        };

        public readonly GazeTarget ToTarget() => type == 1
            ? GazeTarget.Away(new Vector2(yaw, pitch))
            : GazeTarget.AtPerson(new ParticipantId(person));
    }

    /// <summary>Accumulates samples during a bake.</summary>
    public sealed class GazeTrackRecorder
    {
        readonly List<GazeTrackSample> _samples = new();

        public int Count => _samples.Count;

        /// <summary>
        /// Record a decision. Consecutive identical targets are still stored:
        /// the file is small enough that run-length encoding would trade a
        /// reader's ability to diff two tracks line by line for nothing.
        /// </summary>
        public void Add(float conversationTime, in GazeTarget target) =>
            _samples.Add(GazeTrackSample.From(conversationTime, in target));

        public GazeTrackSample[] ToArray() => _samples.ToArray();
    }
}
