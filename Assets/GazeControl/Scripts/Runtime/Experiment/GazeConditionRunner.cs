using System;
using System.Globalization;
using System.Text;
using System.Threading;
using GazeControl.Conversation;
using GazeControl.Gaze.Policy;
using GazeControl.Logging;
using UnityEngine;

namespace GazeControl.Experiment
{
    /// <summary>
    /// Runs one experimental condition: builds a gaze policy per agent, ticks it
    /// on a fixed decision clock, applies the chosen target through the shared
    /// animation layer, and logs every frame.
    ///
    /// The decision tick is deliberately decoupled from the frame rate (§0.2):
    /// policies re-decide at <see cref="DecisionHz"/>, animation runs per frame.
    /// Re-deciding every frame would make dwell durations frame-rate dependent.
    /// </summary>
    public class GazeConditionRunner : MonoBehaviour
    {
        /// <summary>Experimental conditions. Only baseline B is implemented so far.</summary>
        public enum GazeCondition
        {
            SpeakerFollowing,
            RoleConditioned,
            Proposed,
        }

        [field: SerializeField]
        [field: Tooltip("Which gaze policy drives the agents this run")]
        public GazeCondition Condition { get; set; } = GazeCondition.SpeakerFollowing;

        [field: SerializeField]
        [field: Tooltip("All three triad members; ids must be 0..N-1 and unique")]
        public GazeParticipant[] Participants { get; set; }

        [field: SerializeField]
        [field: Tooltip("Scripted conversation; its own gaze control is switched off unless the condition is Proposed")]
        public TriadConversation Conversation { get; set; }

        [field: SerializeField]
        [field: Range(10f, 60f)]
        [field: Tooltip("Policy decision rate, Hz (spec says 20-50 is plenty)")]
        public float DecisionHz { get; set; } = 30f;

        [field: SerializeField]
        [field: Tooltip("Baseline B hysteresis thresholds")]
        public SpeakerFollowingSettings SpeakerFollowing { get; set; } = new();

        [field: SerializeField]
        [field: Tooltip("RMS above which a voice window counts as voiced")]
        public float VoiceRmsThreshold { get; set; } = 0.01f;

        [field: SerializeField]
        [field: Range(1f, 45f)]
        [field: Tooltip("Angle within which gaze counts as directed at someone, degrees")]
        public float MutualGazeAngleDegrees { get; set; } = 15f;

        [field: SerializeField]
        [field: Tooltip("Eye angular speed above which the log marks a gaze shift in progress, deg/s")]
        public float ShiftAngularSpeed { get; set; } = 20f;

        [field: SerializeField]
        [field: Tooltip("Study participant (the human subject) identifier written to the log")]
        public string StudyParticipantId { get; set; } = "P00";

        [field: SerializeField]
        [field: Tooltip("Mixed into the per-agent seeds; change it to get a different but reproducible run")]
        public int BaseSeed { get; set; } = 1;

        [field: SerializeField]
        [field: Tooltip("Write the per-frame gaze CSV")]
        public bool LoggingEnabled { get; set; } = true;

        [field: SerializeField]
        [field: Tooltip("Report every voice-activity transition to the console; use it to calibrate the RMS threshold")]
        public bool LogVoiceActivity { get; set; }

        [field: SerializeField]
        [field: Tooltip("Log directory, relative to the project root")]
        public string LogDirectory { get; set; } = "GazeLogs";

        const float VoiceReportInterval = 5f;

        AudioClipVoiceDetector[] _voiceDetectors;
        VoiceActivityTracker _voiceActivity;
        GazeParticipant[] _agents;
        IGazePolicy[] _policies;
        int[] _seeds;
        GazeTarget[] _targets;
        Vector3[] _previousGazeDirections;
        bool[] _rawVoiced;
        GazeLogWriter _log;
        float _accumulator;
        float _sessionTime;
        float _nextVoiceReportTime;
        bool _warnedAboutAversion;

        void Awake()
        {
            // A trial must keep running when the window loses focus — otherwise the
            // player loop stalls, the log stops mid-conversation, and the trial is
            // silently ruined. The PlayerSettings flag does not govern editor play
            // mode and the runtime flag resets on exit, so it is set every session.
            Application.runInBackground = true;

            if (!TryCollectParticipants())
            {
                enabled = false;
                return;
            }

            _voiceActivity = new VoiceActivityTracker(
                Participants.Length, SpeakerFollowing.OnsetSeconds, SpeakerFollowing.OffsetSeconds);
            _rawVoiced = new bool[Participants.Length];

            // One detector per participant: each carries its own playback clock.
            _voiceDetectors = new AudioClipVoiceDetector[Participants.Length];
            for (var i = 0; i < _voiceDetectors.Length; i++)
                _voiceDetectors[i] = new AudioClipVoiceDetector();

            _policies = new IGazePolicy[_agents.Length];
            _seeds = new int[_agents.Length];
            _targets = new GazeTarget[_agents.Length];
            _previousGazeDirections = new Vector3[_agents.Length];

            for (var i = 0; i < _agents.Length; i++)
            {
                _seeds[i] = SeedFor(_agents[i]);
                _policies[i] = CreatePolicy();
                _policies[i]?.Reset(_seeds[i]);
                _targets[i] = GazeTarget.AtPerson(FindHuman().ParticipantId);
            }

            // The scripted pattern playback and a baseline policy would fight over
            // the same gaze targets, so only one of them may drive at a time.
            if (Conversation != null)
                Conversation.DriveGaze = Condition == GazeCondition.Proposed;

            if (LoggingEnabled)
                OpenLog();
        }

        void OnDestroy() => _log?.Dispose();

        void Start() => _ = LogEveryFrameAsync(destroyCancellationToken);

        void Update()
        {
            var step = 1f / DecisionHz;
            _accumulator += Time.deltaTime;
            _sessionTime += Time.deltaTime;

            while (_accumulator >= step)
            {
                Tick(step);
                _accumulator -= step;
            }
        }

        void Tick(float deltaTime)
        {
            for (var i = 0; i < Participants.Length; i++)
            {
                var voiced = _voiceDetectors[Participants[i].Id]
                    .IsVoiced(Participants[i].Voice, VoiceRmsThreshold, deltaTime);
                if (LogVoiceActivity && voiced != _rawVoiced[Participants[i].Id])
                    ReportVoiceActivity(Participants[i], voiced);

                _rawVoiced[Participants[i].Id] = voiced;
            }

            _voiceActivity.Tick(deltaTime, _rawVoiced);

            if (LogVoiceActivity && _sessionTime >= _nextVoiceReportTime)
            {
                _nextVoiceReportTime = _sessionTime + VoiceReportInterval;
                for (var i = 0; i < Participants.Length; i++)
                    ReportVoiceActivity(Participants[i], _rawVoiced[Participants[i].Id]);
            }

            var speaker = ConfirmedSpeaker();

            for (var i = 0; i < _agents.Length; i++)
            {
                if (_policies[i] == null)
                {
                    _targets[i] = ObserveTarget(_agents[i]);
                    continue;
                }

                var state = BuildState(_agents[i], speaker);
                _targets[i] = _policies[i].Update(deltaTime, in state);
                Apply(_agents[i], _targets[i]);
            }
        }

        void ReportVoiceActivity(GazeParticipant participant, bool voiced)
        {
            var voice = participant.Voice;
            var state = voice == null
                ? "no AudioSource"
                : $"playing={voice.isPlaying} clip={(voice.clip != null ? voice.clip.name : "none")} sample={voice.timeSamples}";

            Debug.Log($"[voice] t={_sessionTime:0.00} {participant.DisplayName} raw={voiced} ({state})", this);
        }

        ConversationState BuildState(GazeParticipant self, ParticipantId speaker)
        {
            var addressee = AddresseeOf(speaker);
            var partners = PartnersOf(self);

            return new ConversationState
            {
                Time = _sessionTime,
                CurrentSpeaker = speaker,
                CurrentAddressee = addressee,
                SelfId = self.ParticipantId,
                SelfRole = RoleOf(self.ParticipantId, speaker, addressee),
                FirstPartner = partners.first,
                SecondPartner = partners.second,

                // Turn phase and turn-end prediction need the scripted schedule,
                // which arrives with the ConversationDirector (Baseline A work).
                // Baseline B ignores both fields by design.
                TurnPhase = self.ParticipantId == speaker ? TurnPhase.TurnMiddle : TurnPhase.NotSpeaking,
                PredictedTimeToTurnEnd = -1f,

                MutualGazeActive = IsInMutualGazeWithAnAgent(self),
                MutualGazeDuration = 0f,
            };
        }

        void Apply(GazeParticipant agent, in GazeTarget target)
        {
            if (target.Type == GazeTargetType.Person)
            {
                var person = FindParticipant(target.Person);
                agent.Gaze.SetTarget(person != null ? person.LookAtAnchor : null);
                return;
            }

            // Aversion needs a direction-based fixation point, which the shared
            // animation layer does not support yet (spec §4 rewrite). Baseline B
            // never averts, so this is unreachable today — warn rather than
            // silently hold, so it cannot pass unnoticed once Baseline A lands.
            if (_warnedAboutAversion)
                return;

            _warnedAboutAversion = true;
            Debug.LogWarning($"{name}: aversion targets are not supported by the animation layer yet; holding gaze.", this);
        }

        /// <summary>
        /// The policy for this condition, or null when gaze is driven from outside
        /// the runner. <see cref="GazeCondition.Proposed"/> is the null case today:
        /// <see cref="TriadConversation"/> plays the paper's pattern itself, and the
        /// runner only observes and logs, so that the proposed condition produces
        /// the same per-frame CSV as the baselines (§6 requires every condition to
        /// be logged). It stops being a null case once the pattern moves behind
        /// <see cref="IGazePolicy"/> on a Baseline A substrate.
        /// </summary>
        IGazePolicy CreatePolicy()
        {
            var human = FindHuman();

            return Condition switch
            {
                GazeCondition.SpeakerFollowing =>
                    new SpeakerFollowingGazePolicy(SpeakerFollowing, _voiceActivity, human.ParticipantId),
                GazeCondition.Proposed => null,
                _ => throw new NotImplementedException(
                    $"Condition {Condition} is not implemented yet; " +
                    $"{nameof(GazeCondition.SpeakerFollowing)} and {nameof(GazeCondition.Proposed)} are available."),
            };
        }

        /// <summary>
        /// Read back what the gaze controller was actually told to look at, for
        /// conditions the runner does not drive. A null target is the scripted
        /// pattern's "None", which currently recenters the eyes rather than
        /// choosing an aversion direction — logged as an aversion with no offset.
        /// </summary>
        GazeTarget ObserveTarget(GazeParticipant agent)
        {
            var target = agent.Gaze.Target;
            if (target == null)
                return GazeTarget.Away(Vector2.zero);

            for (var i = 0; i < Participants.Length; i++)
            {
                if (Participants[i].LookAtAnchor == target)
                    return GazeTarget.AtPerson(Participants[i].ParticipantId);
            }

            return GazeTarget.Away(Vector2.zero);
        }

        async Awaitable LogEveryFrameAsync(CancellationToken cancellationToken)
        {
            if (_log == null)
                return;

            try
            {
                while (true)
                {
                    // End of frame, not LateUpdate: the gaze controllers write the
                    // eye and head bones in their own LateUpdate, and component
                    // order between them is not guaranteed. Logging here records
                    // the pose that was actually rendered.
                    await Awaitable.EndOfFrameAsync(cancellationToken);
                    RecordFrame();
                }
            }
            catch (OperationCanceledException)
            {
                // play mode ended / object destroyed; the log is flushed in OnDestroy
            }
        }

        void RecordFrame()
        {
            var speaker = ConfirmedSpeaker();
            var addressee = AddresseeOf(speaker);
            var human = FindHuman();

            for (var i = 0; i < _agents.Length; i++)
            {
                var agent = _agents[i];
                var gazeDirection = agent.Gaze.GazeDirection;
                var head = agent.Gaze.Head;
                var headAngles = YawPitch(head.forward, agent.transform.rotation);
                var eyeAngles = YawPitch(gazeDirection, head.rotation);
                var other = OtherAgent(i);

                var row = new GazeLogRow
                {
                    Time = _sessionTime,
                    ParticipantId = StudyParticipantId,
                    Condition = Condition.ToString(),
                    AgentId = agent.DisplayName,
                    SelfRole = RoleOf(agent.ParticipantId, speaker, addressee).ToString(),
                    TurnPhase = (agent.ParticipantId == speaker ? TurnPhase.TurnMiddle : TurnPhase.NotSpeaking).ToString(),
                    CurrentSpeaker = speaker.Index,
                    CurrentAddressee = addressee.Index,
                    TargetType = _targets[i].Type.ToString(),
                    TargetId = _targets[i].Person.Index,
                    AversionYaw = _targets[i].AversionOffset.x,
                    AversionPitch = _targets[i].AversionOffset.y,
                    IsShifting = AngularSpeed(_previousGazeDirections[i], gazeDirection) > ShiftAngularSpeed,
                    MutualGazeWithHuman = human != null && IsLookingAt(agent, human),
                    MutualGazeWithOtherAgent = other != null && IsLookingAt(agent, other) && IsLookingAt(other, agent),
                    HeadYaw = headAngles.x,
                    HeadPitch = headAngles.y,
                    EyeYaw = eyeAngles.x,
                    EyePitch = eyeAngles.y,
                    Seed = _seeds[i],
                };

                _log.Append(in row);
                _previousGazeDirections[i] = gazeDirection;
            }
        }

        float AngularSpeed(Vector3 previous, Vector3 current) =>
            previous == Vector3.zero || Time.deltaTime <= 0f
                ? 0f
                : Vector3.Angle(previous, current) / Time.deltaTime;

        bool IsLookingAt(GazeParticipant looker, GazeParticipant target)
        {
            if (looker.Gaze == null || target.LookAtAnchor == null)
                return false;

            var toTarget = target.LookAtAnchor.position - looker.Gaze.GazeOrigin;
            return Vector3.Angle(looker.Gaze.GazeDirection, toTarget) <= MutualGazeAngleDegrees;
        }

        /// <summary>
        /// Mutual gaze is measured from the rendered eye directions, never from the
        /// other agent's policy state: seeing that someone is looking at you is
        /// perception, and Baseline A forbids agents sharing anything else (§5.1).
        /// </summary>
        bool IsInMutualGazeWithAnAgent(GazeParticipant self)
        {
            for (var i = 0; i < _agents.Length; i++)
            {
                if (_agents[i] == self)
                    continue;

                if (IsLookingAt(self, _agents[i]) && IsLookingAt(_agents[i], self))
                    return true;
            }

            return false;
        }

        ParticipantId ConfirmedSpeaker()
        {
            var speaker = ParticipantId.None;
            var longest = 0f;

            for (var i = 0; i < Participants.Length; i++)
            {
                var id = Participants[i].ParticipantId;
                if (!_voiceActivity.IsVoiced(id))
                    continue;

                var seconds = _voiceActivity.UtteranceSeconds(id);
                if (seconds <= longest)
                    continue;

                speaker = id;
                longest = seconds;
            }

            return speaker;
        }

        ParticipantId AddresseeOf(ParticipantId speaker)
        {
            var participant = FindParticipant(speaker);
            return participant != null && participant.DefaultAddressee != null
                ? participant.DefaultAddressee.ParticipantId
                : ParticipantId.None;
        }

        static ParticipantRole RoleOf(ParticipantId self, ParticipantId speaker, ParticipantId addressee)
        {
            if (self == speaker)
                return ParticipantRole.Speaker;

            return self == addressee ? ParticipantRole.Addressee : ParticipantRole.SideParticipant;
        }

        (ParticipantId first, ParticipantId second) PartnersOf(GazeParticipant self)
        {
            var first = ParticipantId.None;
            var second = ParticipantId.None;

            for (var i = 0; i < Participants.Length; i++)
            {
                if (Participants[i] == self)
                    continue;

                if (!first.IsValid)
                    first = Participants[i].ParticipantId;
                else if (!second.IsValid)
                    second = Participants[i].ParticipantId;
            }

            return (first, second);
        }

        GazeParticipant FindParticipant(ParticipantId id)
        {
            for (var i = 0; i < Participants.Length; i++)
            {
                if (Participants[i].ParticipantId == id)
                    return Participants[i];
            }

            return null;
        }

        GazeParticipant FindHuman()
        {
            for (var i = 0; i < Participants.Length; i++)
            {
                if (Participants[i].IsHuman)
                    return Participants[i];
            }

            return null;
        }

        GazeParticipant OtherAgent(int agentIndex)
        {
            for (var i = 0; i < _agents.Length; i++)
            {
                if (i != agentIndex)
                    return _agents[i];
            }

            return null;
        }

        static Vector2 YawPitch(Vector3 direction, Quaternion reference)
        {
            var local = Quaternion.Inverse(reference) * direction.normalized;
            var yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            var pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
        }

        bool TryCollectParticipants()
        {
            if (Participants == null || Participants.Length < 2)
            {
                Debug.LogError($"{name}: assign the triad participants.", this);
                return false;
            }

            var agents = new System.Collections.Generic.List<GazeParticipant>();
            var seenIds = new System.Collections.Generic.HashSet<int>();

            for (var i = 0; i < Participants.Length; i++)
            {
                var participant = Participants[i];
                if (participant == null)
                {
                    Debug.LogError($"{name}: participant slot {i} is empty.", this);
                    return false;
                }

                if (participant.Id < 0 || participant.Id >= Participants.Length || !seenIds.Add(participant.Id))
                {
                    Debug.LogError(
                        $"{name}: participant ids must be unique and within 0..{Participants.Length - 1}; " +
                        $"'{participant.DisplayName}' has {participant.Id}.", this);
                    return false;
                }

                if (participant.IsAgent)
                    agents.Add(participant);
            }

            if (agents.Count == 0)
            {
                Debug.LogError($"{name}: no agents found (an agent needs a GazeController and must not be the human).", this);
                return false;
            }

            if (FindHuman() == null)
            {
                Debug.LogError($"{name}: one participant must be marked as the human user (baseline B's fallback addressee).", this);
                return false;
            }

            _agents = agents.ToArray();
            return true;
        }

        /// <summary>
        /// Deterministic seed per (study participant × condition × agent), so any
        /// trial can be replayed exactly (§0.5). Uses FNV-1a rather than
        /// <c>string.GetHashCode</c>, which is randomised per process on modern
        /// .NET and would give a different seed every run.
        /// </summary>
        int SeedFor(GazeParticipant agent)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            var key = $"{StudyParticipantId}|{Condition}|{agent.Id}|{BaseSeed}";
            var hash = offsetBasis;

            for (var i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= prime;
            }

            return (int)(hash & 0x7FFFFFFF);
        }

        void OpenLog()
        {
            var stem = $"{StudyParticipantId}_{Condition}_{DateTime.Now:yyyyMMdd_HHmmss}";
            var directory = System.IO.Path.Combine(Application.dataPath, "..", LogDirectory);

            try
            {
                _log = new GazeLogWriter(directory, stem);
                _log.WriteMetadata(BuildMetadataJson());
                Debug.Log($"{name}: gaze log -> {_log.CsvPath}", this);
            }
            catch (System.IO.IOException e)
            {
                Debug.LogError($"{name}: could not open gaze log: {e.Message}", this);
                _log = null;
            }
        }

        string BuildMetadataJson()
        {
            var c = CultureInfo.InvariantCulture;
            var json = new StringBuilder();

            json.Append("{\n");
            json.Append($"  \"study_participant\": \"{StudyParticipantId}\",\n");
            json.Append($"  \"condition\": \"{Condition}\",\n");
            json.Append($"  \"base_seed\": {BaseSeed.ToString(c)},\n");
            json.Append($"  \"decision_hz\": {DecisionHz.ToString("0.##", c)},\n");
            json.Append($"  \"voice_rms_threshold\": {VoiceRmsThreshold.ToString("0.#####", c)},\n");
            json.Append($"  \"mutual_gaze_angle_deg\": {MutualGazeAngleDegrees.ToString("0.##", c)},\n");
            json.Append("  \"speaker_following\": {\n");
            json.Append($"    \"onset_s\": {SpeakerFollowing.OnsetSeconds.ToString("0.###", c)},\n");
            json.Append($"    \"offset_s\": {SpeakerFollowing.OffsetSeconds.ToString("0.###", c)},\n");
            json.Append($"    \"backchannel_s\": {SpeakerFollowing.BackchannelSeconds.ToString("0.###", c)},\n");
            json.Append($"    \"minimum_dwell_s\": {SpeakerFollowing.MinimumDwellSeconds.ToString("0.###", c)}\n");
            json.Append("  },\n");
            json.Append("  \"agents\": [\n");

            for (var i = 0; i < _agents.Length; i++)
            {
                var comma = i < _agents.Length - 1 ? "," : "";
                json.Append($"    {{ \"id\": {_agents[i].Id.ToString(c)}, \"name\": \"{_agents[i].DisplayName}\", \"seed\": {_seeds[i].ToString(c)} }}{comma}\n");
            }

            json.Append("  ],\n");
            json.Append($"  \"unity_version\": \"{Application.unityVersion}\",\n");
            json.Append("  \"human_gaze_note\": \"mutual_gaze_with_human records only the agent->human direction; the human's gaze is unobserved (no eye tracking)\"\n");
            json.Append("}\n");

            return json.ToString();
        }
    }
}
