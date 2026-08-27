using System.Collections.Generic;
using GazeControl.Conversation;
using GazeControl.Xr;
using UnityEngine;

namespace GazeControl.Logging
{
    /// <summary>
    /// Records where the human participant looked, once per decision tick, into
    /// the take's own folder beside the agent log (<c>user-study-design.md</c>
    /// §7). It writes the raw data the four objective measures are computed
    /// from; it computes none of them itself, because they are all whole-take
    /// statistics and belong in the offline analysis.
    ///
    /// <para>Driven by <c>GazeConditionRunner</c> rather than by its own
    /// <c>Update</c>, for the same reason <c>ConversationDirector</c> is: the
    /// runner is the one component that knows which instant on the decision grid
    /// is being decided at, and a self-advancing sampler would land on the
    /// frame's sub-frame remainder instead.</para>
    ///
    /// <para><b>Nothing is written on a desktop Play.</b> Baking tracks,
    /// browsing clips and recording demo videos all run with no headset, and a
    /// folder of all-<c>Unavailable</c> logs beside every take would be noise
    /// that looks like data. Set <see cref="LogWithoutHeadset"/> to record the
    /// head pose alone when checking the wiring at a desk.</para>
    /// </summary>
    public sealed class ParticipantGazeLogger : MonoBehaviour
    {
        /// <summary>How long gaze may stay unusable before the operator is told, seconds.</summary>
        const float SilenceWarningSeconds = 5f;

        [field: SerializeField]
        [field: Tooltip("The participant's rig; the log follows its head camera and only runs while its XR is up")]
        public XrParticipantRig Rig { get; set; }

        [field: SerializeField]
        [field: Range(0.05f, 0.4f)]
        [field: Tooltip("Radius of the head sphere an agent counts as looked at within, metres")]
        public float HeadSphereRadius { get; set; } = 0.15f;

        [field: SerializeField]
        [field: Tooltip("Write a log even with no headset — head pose only, for checking the wiring at a desk")]
        public bool LogWithoutHeadset { get; set; }

        readonly List<GazeSphereTargeting.Head> _heads = new();
        IParticipantGazeSource _source;
        ParticipantGazeLogWriter _writer;
        GazeParticipant[] _agents;
        string _participantId;
        string _condition;
        string _case;
        float _lastValidTime;
        float _nextSilenceWarningTime;
        bool _sawValidGaze;

        /// <summary>True once a file is open and rows are going into it.</summary>
        public bool IsLogging => _writer != null;

        /// <summary>Path of the open log, or null.</summary>
        public string CsvPath => _writer?.CsvPath;

        /// <summary>The gaze source, injectable so the logger can be exercised without a headset.</summary>
        public IParticipantGazeSource Source
        {
            get => _source ??= new VarjoGazeSource();
            set => _source = value;
        }

        /// <summary>
        /// Why this run will not record participant gaze, or null when it will.
        /// Read by the study-session guard, so a participant is never run without
        /// the objective measures being recorded.
        /// </summary>
        public string UnusableReason()
        {
            if (Rig == null)
                return "no XR participant rig is wired to the participant gaze logger.";

            if (Rig.HeadCamera == null)
                return "the participant rig has no head camera, so there is no head pose to log.";

            if (!Rig.IsXrRunning)
                return "XR is not running — the headset must be started before the trial " +
                       $"(check StartXrOnPlay on '{Rig.name}').";

            return Source.UnavailableReason;
        }

        /// <summary>Open the log for one take. Call once, after the rig has started XR.</summary>
        public void Begin(
            string directory, string fileNameStem, string participantId,
            string condition, string caseName, IReadOnlyList<GazeParticipant> agents)
        {
            End();

            _participantId = participantId;
            _condition = condition;
            _case = caseName;
            _agents = new GazeParticipant[agents?.Count ?? 0];
            for (var i = 0; i < _agents.Length; i++)
                _agents[i] = agents[i];

            if (Rig == null || Rig.HeadCamera == null)
            {
                Debug.LogError($"{name}: no rig or head camera, so participant gaze cannot be logged.", this);
                return;
            }

            if (!Rig.IsXrRunning && !LogWithoutHeadset)
            {
                Debug.Log($"{name}: XR is not running, so no participant gaze log is written for this run.", this);
                return;
            }

            var reason = Source.UnavailableReason;
            if (reason != null)
                Debug.LogWarning(
                    $"{name}: starting the log, but the tracker has no gaze yet — {reason} " +
                    "Rows will record the head pose and say why gaze is missing.", this);

            try
            {
                _writer = new ParticipantGazeLogWriter(directory, fileNameStem);
                Debug.Log($"{name}: participant gaze log -> {_writer.CsvPath}", this);
            }
            catch (System.IO.IOException e)
            {
                Debug.LogError($"{name}: could not open the participant gaze log: {e.Message}", this);
                _writer = null;
            }

            _sawValidGaze = false;
            _lastValidTime = 0f;
            _nextSilenceWarningTime = SilenceWarningSeconds;
        }

        /// <summary>
        /// Record one tick.
        /// </summary>
        /// <param name="conversationTime">The tick's instant on the decision grid.</param>
        /// <param name="tick">Index of that tick, so the grid is recoverable exactly.</param>
        /// <param name="agentsLookingAtUser">
        /// Bit set over agent ids of the agents looking at the participant right
        /// now. Passed in rather than measured here so that both directions of
        /// mutual gaze keep using the runner's one criterion, and so the agent
        /// half stays measured from the rendered eye bones (§5.1).
        /// </param>
        public void Sample(float conversationTime, int tick, int agentsLookingAtUser)
        {
            if (_writer == null)
                return;

            var head = Rig.HeadCamera.transform;
            var sample = Rig.IsXrRunning ? Source.Read() : ParticipantGazeSample.Unavailable;

            // Head-relative to world exactly as Varjo's own sample does it: the
            // camera transform already carries the handedness the plugin
            // documents, so no manual axis flip belongs here.
            var origin = sample.HasGaze ? head.TransformPoint(sample.Origin) : head.position;
            var direction = sample.HasGaze ? head.TransformDirection(sample.Forward) : head.forward;

            var hit = sample.HasGaze
                ? GazeSphereTargeting.Resolve(origin, direction, CollectHeads())
                : GazeSphereTargeting.Hit.Nothing;

            var lookedAt = hit.Found ? hit.Id : -1;
            var euler = head.rotation.eulerAngles;

            var row = new ParticipantGazeRow
            {
                Time = conversationTime,
                Tick = tick,
                ParticipantId = _participantId,
                Condition = _condition,
                Case = _case,
                TrackerStatus = sample.Status,
                LeftEyeStatus = sample.LeftStatus,
                RightEyeStatus = sample.RightStatus,
                Calibrated = Rig.IsXrRunning && Source.IsCalibrated,
                DeviceFrame = sample.DeviceFrame,
                CaptureTimeNanoseconds = sample.CaptureTimeNanoseconds,
                HeadPosition = head.position,
                HeadYaw = Signed(euler.y),
                HeadPitch = Signed(euler.x),
                HeadRoll = Signed(euler.z),
                GazeOrigin = origin,
                GazeDirection = direction,
                LeftGazeOrigin = sample.HasGaze ? head.TransformPoint(sample.LeftOrigin) : Vector3.zero,
                LeftGazeDirection = sample.HasGaze ? head.TransformDirection(sample.LeftForward) : Vector3.zero,
                RightGazeOrigin = sample.HasGaze ? head.TransformPoint(sample.RightOrigin) : Vector3.zero,
                RightGazeDirection = sample.HasGaze ? head.TransformDirection(sample.RightForward) : Vector3.zero,
                FocusDistance = sample.FocusDistance,
                FocusStability = sample.FocusStability,
                InterPupillaryDistanceMm = sample.InterPupillaryDistanceMm,
                LeftPupilDiameterMm = sample.LeftPupilDiameterMm,
                RightPupilDiameterMm = sample.RightPupilDiameterMm,
                LeftEyeOpenness = sample.LeftEyeOpenness,
                RightEyeOpenness = sample.RightEyeOpenness,
                TargetType = sample.HasGaze ? (hit.Found ? "person" : "elsewhere") : "invalid",
                TargetId = hit.Id,
                TargetAngleDegrees = hit.AngleDegrees,
                TargetDistance = hit.Distance,
                MutualGaze = lookedAt >= 0 && (agentsLookingAtUser & (1 << lookedAt)) != 0,
                AgentsLookingAtUser = agentsLookingAtUser,
            };

            _writer.Append(in row);
            ReportSilence(sample, conversationTime);
        }

        /// <summary>Close the log and report what it caught. Safe to call more than once.</summary>
        public void End()
        {
            if (_writer == null)
                return;

            var rows = _writer.RowCount;
            var valid = _writer.ValidRowCount;
            var path = _writer.CsvPath;

            _writer.Dispose();
            _writer = null;

            var share = rows > 0 ? 100f * valid / rows : 0f;
            var message = $"{name}: participant gaze log closed — {rows} ticks, " +
                          $"{valid} with usable gaze ({share:F1}%) -> {path}";

            // A trial whose gaze mostly failed still has a questionnaire, so this
            // is not an error; but it must not be discovered during analysis.
            if (rows > 0 && share < 50f)
                Debug.LogWarning($"{message}. Under half the take carries gaze — check the trial before keeping it.", this);
            else
                Debug.Log(message, this);
        }

        void OnDestroy()
        {
            End();
        }

        IReadOnlyList<GazeSphereTargeting.Head> CollectHeads()
        {
            // Rebuilt each tick rather than cached: the agents' head bones are
            // driven by mocap, so their positions change every frame.
            _heads.Clear();
            for (var i = 0; i < _agents.Length; i++)
            {
                if (_agents[i] == null || _agents[i].LookAtAnchor == null)
                    continue;

                _heads.Add(new GazeSphereTargeting.Head(
                    _agents[i].Id, _agents[i].LookAtAnchor.position, HeadSphereRadius));
            }

            return _heads;
        }

        void ReportSilence(in ParticipantGazeSample sample, float conversationTime)
        {
            if (sample.Status == ParticipantGazeStatus.Valid)
            {
                _sawValidGaze = true;
                _lastValidTime = conversationTime;
                _nextSilenceWarningTime = conversationTime + SilenceWarningSeconds;
                return;
            }

            if (conversationTime < _nextSilenceWarningTime)
                return;

            _nextSilenceWarningTime = conversationTime + SilenceWarningSeconds;
            var since = _sawValidGaze
                ? $"since t={_lastValidTime:F1} s"
                : "since the take started";

            Debug.LogWarning(
                $"{name}: no usable participant gaze {since} ({sample.Status}). " +
                $"{Source.UnavailableReason ?? "The headset may have moved or been taken off."}", this);
        }

        /// <summary>Euler angles come back in [0, 360); the log wants them signed.</summary>
        static float Signed(float degrees) => degrees > 180f ? degrees - 360f : degrees;
    }
}
