using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace GazeControl.Study
{
    /// <summary>
    /// One participant's session as a record on disk — <c>session.json</c> in
    /// their own folder, written when the session starts and updated as it runs.
    ///
    /// <para>Written at the start rather than at the end, for two reasons. It is
    /// what makes an interrupted session visible: fifteen clips that stopped at
    /// nine otherwise look exactly like fifteen that finished, because the take
    /// logs are named per conversation and a missing one is indistinguishable
    /// from a clip nobody reached. And it is what puts the participant's folder
    /// on disk from the first moment, so <see cref="ParticipantRoster"/> hands the
    /// next participant a free label even if this session ends before anyone
    /// answers a single question.</para>
    ///
    /// <para>It also carries the provenance a recorded session needs to be
    /// reproducible after the fact: the running order, the counterbalancing slot
    /// it came from, the Unity version and the commit the build was made at.</para>
    /// </summary>
    [Serializable]
    public sealed class StudySessionRecord
    {
        /// <summary>The file this is written to, inside a participant's folder.</summary>
        public const string FileName = "session.json";

        /// <summary>One trial of the running order, flattened for JsonUtility.</summary>
        [Serializable]
        public sealed class Trial
        {
            public int index;
            public int block;
            public int versionPosition;
            public string conversation;
            public string condition;
        }

        public string participant;
        public int scheduleOrdinal;
        public string startedUtc;

        /// <summary>Empty until the last clip has been shown; that is what "finished" means.</summary>
        public string completedUtc;

        public int clipsCompleted;
        public int clipCount;
        public string unityVersion;

        /// <summary>The commit the session ran at, or empty when it could not be read.</summary>
        public string gitCommit;

        public Trial[] trials;

        /// <summary>Whether every clip was shown.</summary>
        public bool IsComplete => !string.IsNullOrEmpty(completedUtc);

        /// <summary>How far a session got, for the operator's window.</summary>
        public string Progress => IsComplete
            ? $"complete, {clipsCompleted}/{clipCount} clips"
            : $"INCOMPLETE, {clipsCompleted}/{clipCount} clips";

        /// <summary>The record for a session about to start.</summary>
        public static StudySessionRecord Begin(
            string participant, int scheduleOrdinal, IEnumerable<StudyTrial> trials,
            string unityVersion, string gitCommit, DateTime utcNow)
        {
            var list = new List<Trial>();
            if (trials != null)
            {
                foreach (var trial in trials)
                {
                    list.Add(new Trial
                    {
                        index = trial.Index,
                        block = trial.BlockNumber,
                        versionPosition = trial.VersionPosition,
                        conversation = trial.Conversation,
                        condition = trial.Condition,
                    });
                }
            }

            return new StudySessionRecord
            {
                participant = participant,
                scheduleOrdinal = scheduleOrdinal,
                startedUtc = Stamp(utcNow),
                completedUtc = string.Empty,
                clipsCompleted = 0,
                clipCount = list.Count,
                unityVersion = unityVersion,
                gitCommit = gitCommit ?? string.Empty,
                trials = list.ToArray(),
            };
        }

        /// <summary>Mark the session finished. Idempotent — the first end time is the one kept.</summary>
        public void Complete(DateTime utcNow)
        {
            if (!IsComplete)
                completedUtc = Stamp(utcNow);
        }

        /// <summary>Write the record into <paramref name="directory"/>, creating it if absent.</summary>
        public void Save(string directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(PathIn(directory), JsonUtility.ToJson(this, prettyPrint: true));
        }

        /// <summary>The record in <paramref name="directory"/>, or null when there is none to read.</summary>
        public static StudySessionRecord Load(string directory)
        {
            var path = PathIn(directory);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonUtility.FromJson<StudySessionRecord>(File.ReadAllText(path));
            }
            catch (Exception e) when (e is IOException or ArgumentException)
            {
                // A session record that cannot be read is worth reporting but must
                // not stop the next participant: the roster still sees the folder,
                // which is what protects the label.
                Debug.LogWarning($"Session record at {path} could not be read: {e.Message}");
                return null;
            }
        }

        public static string PathIn(string directory) => Path.Combine(directory, FileName);

        static string Stamp(DateTime utc) => utc.ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
