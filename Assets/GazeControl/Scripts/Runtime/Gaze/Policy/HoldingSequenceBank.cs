using System;
using System.Collections.Generic;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Role-indexed store of measured holding-period fixation stretches, loaded
    /// from the committed <c>HoldingSequences.json</c>. Each stretch is a run of
    /// consecutive fixations one corpus participant really produced between
    /// turns, kept in recorded order so the replay policy can play it verbatim.
    ///
    /// The bank lives in a config file rather than in constants for the same
    /// reason baseline A's parameters do: it is extracted from a corpus, not
    /// chosen. <c>Tools/build_holding_sequences.py</c> regenerates the file from
    /// <c>Research/corpora</c>, which is git-ignored, so the committed JSON is
    /// what makes the condition reproducible.
    /// </summary>
    public sealed class HoldingSequenceBank
    {
        const string DefaultResourcePath = "HoldingSequences";
        const int RoleCount = 3;

        readonly HoldingFixation[][][] _stretches = new HoldingFixation[RoleCount][][];

        HoldingSequenceBank()
        {
        }

        /// <summary>Load the committed sequence bank from <c>Resources</c>.</summary>
        public static HoldingSequenceBank LoadDefault()
        {
            var asset = Resources.Load<TextAsset>(DefaultResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Holding sequences not found at Resources/{DefaultResourcePath}.json. " +
                    "Regenerate them with Tools/build_holding_sequences.py.");
            }

            return Parse(asset.text);
        }

        public static HoldingSequenceBank Parse(string json)
        {
            var document = JsonUtility.FromJson<Document>(json);
            if (document == null)
                throw new ArgumentException("Holding sequences are not valid JSON.", nameof(json));

            var bank = new HoldingSequenceBank();
            bank.Fill(document);
            return bank;
        }

        /// <summary>Number of stretches available to a gazer of this role; positive after validation.</summary>
        public int StretchCount(ParticipantRole role) => _stretches[(int)role].Length;

        /// <summary>The fixations of one stretch, in the order the corpus recorded them.</summary>
        public ReadOnlySpan<HoldingFixation> Fixations(ParticipantRole role, int stretchIndex) =>
            _stretches[(int)role][stretchIndex];

        void Fill(Document document)
        {
            if (document.stretches == null || document.stretches.Length == 0)
                throw new ArgumentException("Holding sequences: 'stretches' is missing or empty.");

            var pools = new List<HoldingFixation[]>[RoleCount];
            for (var role = 0; role < RoleCount; role++)
                pools[role] = new List<HoldingFixation[]>();

            foreach (var entry in document.stretches)
            {
                var role = ParseRole(entry.role);
                pools[(int)role].Add(ReadFixations(entry, role));
            }

            for (var role = 0; role < RoleCount; role++)
            {
                if (pools[role].Count == 0)
                    throw new ArgumentException($"Holding sequences: no stretches for role '{RoleKey((ParticipantRole)role)}'.");

                _stretches[role] = pools[role].ToArray();
            }
        }

        /// <summary>
        /// Decode and validate one stretch. The no-own-role rule is a corpus
        /// guarantee (nobody gazes at themselves), enforced here so the replay
        /// policy can rely on a fresh draw never naming the agent itself.
        /// </summary>
        static HoldingFixation[] ReadFixations(StretchEntry entry, ParticipantRole role)
        {
            if (entry.targets == null || entry.targets.Length == 0)
                throw new ArgumentException("Holding sequences: a stretch has no fixations.");

            if (entry.durations == null || entry.durations.Length != entry.targets.Length)
                throw new ArgumentException("Holding sequences: 'targets' and 'durations' must have the same length.");

            var fixations = new HoldingFixation[entry.targets.Length];
            for (var i = 0; i < fixations.Length; i++)
            {
                var target = ParseTarget(entry.targets[i]);
                if ((int)target == (int)role)
                    throw new ArgumentException($"Holding sequences: a '{entry.role}' stretch targets its own role.");

                if (entry.durations[i] <= 0f)
                    throw new ArgumentException("Holding sequences: fixation durations must be positive.");

                fixations[i] = new HoldingFixation(target, entry.durations[i]);
            }

            return fixations;
        }

        static ParticipantRole ParseRole(string value) => value switch
        {
            "sp" => ParticipantRole.Speaker,
            "ad" => ParticipantRole.Addressee,
            "sd" => ParticipantRole.SideParticipant,
            _ => throw new ArgumentException($"Holding sequences: unknown role '{value}'."),
        };

        static GazeTargetRole ParseTarget(string value) => value switch
        {
            "sp" => GazeTargetRole.Speaker,
            "ad" => GazeTargetRole.Addressee,
            "sd" => GazeTargetRole.SideParticipant,
            "aversion" => GazeTargetRole.Aversion,
            _ => throw new ArgumentException($"Holding sequences: unknown gaze target '{value}'."),
        };

        static string RoleKey(ParticipantRole role) => role switch
        {
            ParticipantRole.Speaker => "sp",
            ParticipantRole.Addressee => "ad",
            _ => "sd",
        };

        // JsonUtility only sees public fields, so the file's shape is mirrored in
        // plain field-only DTOs rather than in the project's usual property style.
        // Names must match the JSON keys exactly; sessionId/gazerId/startFrame are
        // provenance the runtime never reads.
        [Serializable]
        sealed class Document
        {
            public StretchEntry[] stretches;
        }

        [Serializable]
        sealed class StretchEntry
        {
            public string role;
            public string sessionId;
            public string gazerId;
            public int startFrame;
            public string[] targets;
            public float[] durations;
        }
    }
}
