using System;
using UnityEngine;

namespace GazeControl.Gaze.Policy
{
    /// <summary>
    /// Baseline A's fitted parameters, loaded from <c>BaselineAParameters.json</c>:
    /// target ratios (<c>S</c>), the target-selection jump chain, dwell durations
    /// (<c>D</c>), and the aversion direction model (<c>L</c> plus its measured
    /// angles and re-target cadence).
    ///
    /// They live in a config file rather than in constants (§2) because they are
    /// re-estimated from a corpus, not chosen: <c>Tools/build_baseline_a_params.py</c>
    /// regenerates the file from <c>Research/corpora</c>, which is git-ignored, so
    /// the committed JSON is what makes the condition reproducible.
    /// </summary>
    public sealed class ShintaniGazeParameters
    {
        const string DefaultResourcePath = "BaselineAParameters";
        const int RoleCount = 3;
        const int StateCount = 4;
        const int TurnStateCount = 2;
        const int DirectionCount = 9;

        // Ratio and jump rows arrive whole and replace their slot; dwell cells are
        // filled one scalar at a time, so their leaves have to exist up front.
        readonly float[][][] _targetRatios = NewRows(RoleCount, TurnStateCount);
        readonly float[][][] _jumpProbabilities = NewRows(RoleCount, StateCount);
        readonly float[][][] _dwellMu = NewFilledRows(RoleCount, TurnStateCount, StateCount);
        readonly float[][][] _dwellSigma = NewFilledRows(RoleCount, TurnStateCount, StateCount);
        readonly float[][] _directionMarginals = new float[RoleCount][];
        readonly float[][] _directionConditionals = new float[DirectionCount][];
        readonly Vector2[] _directionAngles = new Vector2[DirectionCount];

        ShintaniGazeParameters()
        {
        }

        /// <summary>Shortest dwell the corpus can resolve — its fixation min-dwell filter.</summary>
        public float MinDwellSeconds { get; private set; }

        /// <summary>Cap on a sampled dwell, so one draw cannot freeze gaze for a whole turn.</summary>
        public float MaxDwellSeconds { get; private set; }

        /// <summary>Distance to the nearest turn instant within which the corpus labels gaze "changing".</summary>
        public float TurnWindowSeconds { get; private set; }

        /// <summary>
        /// How often the eyes pick a new direction while gaze is averted.
        ///
        /// The one quantity taken from the paper rather than from our corpus.
        /// Sampling the measured direction-segment distribution (median 0.217 s)
        /// made a four-second aversion pick up thirteen direction changes, which
        /// reads as darting; those segments come from an eye tracker and include
        /// movement below what an annotator would call a gaze shift. The corpus's
        /// own unbiased estimator says 0.939 s, so 0.7 s sits inside its range.
        /// </summary>
        public float RetargetSeconds { get; private set; }

        /// <summary>Load the committed parameter file from <c>Resources</c>.</summary>
        public static ShintaniGazeParameters LoadDefault()
        {
            var asset = Resources.Load<TextAsset>(DefaultResourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Baseline A parameters not found at Resources/{DefaultResourcePath}.json. " +
                    "Regenerate them with Tools/build_baseline_a_params.py.");
            }

            return Parse(asset.text);
        }

        public static ShintaniGazeParameters Parse(string json)
        {
            var document = JsonUtility.FromJson<Document>(json);
            if (document == null)
                throw new ArgumentException("Baseline A parameters are not valid JSON.", nameof(json));

            var parameters = new ShintaniGazeParameters
            {
                MinDwellSeconds = document.minDwellSeconds,
                MaxDwellSeconds = document.maxDwellSeconds,
                TurnWindowSeconds = document.turnWindowSeconds,
            };

            parameters.Fill(document);
            return parameters;
        }

        /// <summary>S (Eq 6) — occupancy per target, used to draw the run's first state.</summary>
        public ReadOnlySpan<float> TargetRatios(ParticipantRole role, TurnState turnState) =>
            _targetRatios[(int)role][(int)turnState];

        /// <summary>Where gaze goes when it leaves <paramref name="from"/>; self-transitions are absent by construction.</summary>
        public ReadOnlySpan<float> JumpProbabilities(ParticipantRole role, GazeTargetRole from) =>
            _jumpProbabilities[(int)role][(int)from];

        /// <summary>Parameters of the lognormal dwell for one (role × turn state × target) cell.</summary>
        public (float Mu, float Sigma) Dwell(ParticipantRole role, TurnState turnState, GazeTargetRole target) =>
            (_dwellMu[(int)role][(int)turnState][(int)target], _dwellSigma[(int)role][(int)turnState][(int)target]);

        /// <summary>L (Eq 7) — direction weights for the first aversion direction.</summary>
        public ReadOnlySpan<float> AversionDirectionWeights(ParticipantRole role) => _directionMarginals[(int)role];

        /// <summary>P(next direction | current direction) for a re-target within one aversion.</summary>
        public ReadOnlySpan<float> AversionDirectionWeights(AversionDirection from) => _directionConditionals[(int)from];

        /// <summary>Measured eye-in-head yaw/pitch in degrees for a direction category.</summary>
        public Vector2 AversionAngles(AversionDirection direction) => _directionAngles[(int)direction];

        void Fill(Document document)
        {
            RequireSection(document.targetRatios, nameof(document.targetRatios));
            RequireSection(document.transitions, nameof(document.transitions));
            RequireSection(document.dwells, nameof(document.dwells));
            RequireSection(document.aversionDirections, nameof(document.aversionDirections));
            RequireSection(document.aversionConditional, nameof(document.aversionConditional));
            RequireSection(document.aversionAngles, nameof(document.aversionAngles));

            foreach (var entry in document.targetRatios)
                _targetRatios[(int)ParseRole(entry.role)][(int)ParseTurnState(entry.turnState)] = RequireLength(entry.ratios, StateCount, "targetRatios");

            foreach (var entry in document.transitions)
                _jumpProbabilities[(int)ParseRole(entry.role)][(int)ParseState(entry.from)] = RequireLength(entry.to, StateCount, "transitions");

            foreach (var entry in document.dwells)
            {
                var role = (int)ParseRole(entry.role);
                var turnState = (int)ParseTurnState(entry.turnState);
                var target = (int)ParseState(entry.target);
                _dwellMu[role][turnState][target] = entry.mu;
                _dwellSigma[role][turnState][target] = entry.sigma;
            }

            foreach (var entry in document.aversionDirections)
                _directionMarginals[(int)ParseRole(entry.role)] = RequireLength(entry.weights, DirectionCount, "aversionDirections");

            foreach (var entry in document.aversionConditional)
                _directionConditionals[(int)ParseDirection(entry.from)] = RequireLength(entry.weights, DirectionCount, "aversionConditional");

            foreach (var entry in document.aversionAngles)
                _directionAngles[(int)ParseDirection(entry.direction)] = new Vector2(entry.yaw, entry.pitch);

            RetargetSeconds = document.aversionRetarget?.seconds ?? 0f;
            if (RetargetSeconds <= 0f)
                throw new ArgumentException("Baseline A parameters: 'aversionRetarget.seconds' must be positive.");

            Validate();
        }

        void Validate()
        {
            for (var role = 0; role < RoleCount; role++)
            {
                if (_directionMarginals[role] == null)
                    throw new ArgumentException($"Baseline A parameters are missing aversion directions for role {role}.");

                for (var turnState = 0; turnState < TurnStateCount; turnState++)
                {
                    if (_targetRatios[role][turnState] == null)
                        throw new ArgumentException($"Baseline A parameters are missing target ratios for role {role}.");
                }

                // The gazer's own role is unreachable — nobody looks at themselves —
                // so that one row is legitimately absent and is left as zeros.
                for (var state = 0; state < StateCount; state++)
                    _jumpProbabilities[role][state] ??= new float[StateCount];
            }

            for (var direction = 0; direction < DirectionCount; direction++)
            {
                if (_directionConditionals[direction] == null)
                    throw new ArgumentException($"Baseline A parameters are missing a conditional row for direction {direction}.");
            }
        }

        static void RequireSection<T>(T[] entries, string field)
        {
            if (entries == null || entries.Length == 0)
                throw new ArgumentException($"Baseline A parameters: '{field}' is missing or empty.");
        }

        static float[] RequireLength(float[] values, int length, string field)
        {
            if (values == null || values.Length != length)
                throw new ArgumentException($"Baseline A parameters: '{field}' entries must have {length} values.");

            return values;
        }

        static float[][][] NewRows(int roles, int rowsPerRole)
        {
            var table = new float[roles][][];
            for (var role = 0; role < roles; role++)
                table[role] = new float[rowsPerRole][];

            return table;
        }

        static float[][][] NewFilledRows(int roles, int rowsPerRole, int rowLength)
        {
            var table = NewRows(roles, rowsPerRole);
            for (var role = 0; role < roles; role++)
            {
                for (var row = 0; row < rowsPerRole; row++)
                    table[role][row] = new float[rowLength];
            }

            return table;
        }

        static ParticipantRole ParseRole(string value) => value switch
        {
            "sp" => ParticipantRole.Speaker,
            "ad" => ParticipantRole.Addressee,
            "sd" => ParticipantRole.SideParticipant,
            _ => throw new ArgumentException($"Baseline A parameters: unknown role '{value}'."),
        };

        static GazeTargetRole ParseState(string value) => value switch
        {
            "sp" => GazeTargetRole.Speaker,
            "ad" => GazeTargetRole.Addressee,
            "sd" => GazeTargetRole.SideParticipant,
            "aversion" => GazeTargetRole.Aversion,
            _ => throw new ArgumentException($"Baseline A parameters: unknown gaze target '{value}'."),
        };

        static TurnState ParseTurnState(string value) => value switch
        {
            "changing" => TurnState.Changing,
            "holding" => TurnState.Holding,
            _ => throw new ArgumentException($"Baseline A parameters: unknown turn state '{value}'."),
        };

        static AversionDirection ParseDirection(string value) => value switch
        {
            "lu" => AversionDirection.UpLeft,
            "u" => AversionDirection.Up,
            "ru" => AversionDirection.UpRight,
            "l" => AversionDirection.Left,
            "f" => AversionDirection.Forward,
            "r" => AversionDirection.Right,
            "ld" => AversionDirection.DownLeft,
            "d" => AversionDirection.Down,
            "rd" => AversionDirection.DownRight,
            _ => throw new ArgumentException($"Baseline A parameters: unknown aversion direction '{value}'."),
        };

        // JsonUtility only sees public fields, so the file's shape is mirrored in
        // plain field-only DTOs rather than in the project's usual property style.
        // Names must match the JSON keys exactly.
        [Serializable]
        sealed class Document
        {
            public float minDwellSeconds;
            public float maxDwellSeconds;
            public float turnWindowSeconds;
            public TargetRatioEntry[] targetRatios;
            public TransitionEntry[] transitions;
            public DwellEntry[] dwells;
            public DirectionWeightEntry[] aversionDirections;
            public ConditionalWeightEntry[] aversionConditional;
            public DirectionAngleEntry[] aversionAngles;
            public RetargetEntry aversionRetarget;
        }

        [Serializable]
        sealed class TargetRatioEntry
        {
            public string role;
            public string turnState;
            public float[] ratios;
        }

        [Serializable]
        sealed class TransitionEntry
        {
            public string role;
            public string from;
            public float[] to;
        }

        [Serializable]
        sealed class DwellEntry
        {
            public string role;
            public string turnState;
            public string target;
            public float mu;
            public float sigma;
        }

        [Serializable]
        sealed class DirectionWeightEntry
        {
            public string role;
            public float[] weights;
        }

        [Serializable]
        sealed class ConditionalWeightEntry
        {
            public string from;
            public float[] weights;
        }

        [Serializable]
        sealed class DirectionAngleEntry
        {
            public string direction;
            public float yaw;
            public float pitch;
        }

        [Serializable]
        sealed class RetargetEntry
        {
            public float seconds;
        }
    }
}
