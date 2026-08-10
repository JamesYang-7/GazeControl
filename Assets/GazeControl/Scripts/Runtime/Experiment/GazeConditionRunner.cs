using System;
using System.Globalization;
using System.Text;
using System.Threading;
using GazeControl.Conversation;
using GazeControl.Gaze;
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
        /// <summary>Experimental conditions.</summary>
        public enum GazeCondition
        {
            /// <summary>Baseline B (§3): follow whoever is speaking, driven by voice activity alone.</summary>
            SpeakerFollowing,

            /// <summary>Baseline A (§2): the role-conditioned Shintani et al. sampler, fitted to our corpus.</summary>
            RoleConditioned,

            /// <summary>The paper's pre-turn patterns, played over a Baseline B substrate.</summary>
            Proposed,
        }

        [field: SerializeField]
        [field: Tooltip("Which gaze policy drives the agents this run")]
        public GazeCondition Condition { get; set; } = GazeCondition.SpeakerFollowing;

        [field: SerializeField]
        [field: Tooltip("All three triad members; ids must be 0..N-1 and unique")]
        public GazeParticipant[] Participants { get; set; }

        [field: SerializeField]
        [field: Tooltip("The recorded corpus segment being replayed; gaze belongs entirely to the condition's policy")]
        public RecordedConversation Conversation { get; set; }

        [field: SerializeField]
        [field: Tooltip("Publishes the segment's turn schedule; required by the RoleConditioned and Proposed conditions")]
        public ConversationDirector Director { get; set; }

        [field: SerializeField]
        [field: Range(10f, 60f)]
        [field: Tooltip("Policy decision rate, Hz (spec says 20-50 is plenty)")]
        public float DecisionHz { get; set; } = 30f;

        [field: SerializeField]
        [field: Tooltip("Baseline B hysteresis thresholds")]
        public SpeakerFollowingSettings SpeakerFollowing { get; set; } = new();

        [field: SerializeField]
        [field: Tooltip("Proposed condition: which pre-turn prototype plays, and its window")]
        public ProposedSettings Proposed { get; set; } = new();

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
        [field: Tooltip("Output root for this run's video and gaze log, relative to the project root")]
        public string OutputDirectory { get; set; } = "Recordings";

        /// <summary>
        /// Names the folder that one demo's artefacts land in — the three
        /// condition videos and the gaze log that goes with each. Bump it per
        /// take, e.g. case1_01 → case1_02, so a video is never separated from the
        /// log that explains it.
        /// </summary>
        [field: SerializeField]
        [field: Tooltip("Folder for this demo's artefacts, e.g. case1_01. Bump per take.")]
        public string CaseName { get; set; } = "case1_01";

        /// <summary>Where this run's video and gaze log are written, absolute.</summary>
        public string TakeDirectory => System.IO.Path.Combine(
            Application.dataPath, "..", OutputDirectory, CaseName);

        const float VoiceReportInterval = 5f;

        AudioClipVoiceDetector[] _voiceDetectors;
        VoiceActivityTracker _voiceActivity;
        GazeParticipant[] _agents;
        IGazePolicy[] _policies;
        int[] _seeds;
        GazeTarget[] _targets;
        Vector3[] _previousGazeDirections;
        bool[] _rawVoiced;
        ShintaniGazeParameters _roleConditionedParameters;
        GazeLogWriter _log;
        float _accumulator;
        float _sessionTime;
        float _nextVoiceReportTime;
        bool _warnedAboutMissingTarget;

        void Awake()
        {
            // A trial must keep running when the window loses focus — otherwise the
            // player loop stalls, the log stops mid-conversation, and the trial is
            // silently ruined. The PlayerSettings flag does not govern editor play
            // mode and the runtime flag resets on exit, so it is set every session.
            Application.runInBackground = true;

            if (!TryCollectParticipants() || !TryPrepareCondition())
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

            // One policy instance per agent, each with its own seed and no view of
            // the other's target: uncoordinated gaze is the property under study,
            // so sharing a policy would quietly destroy it (§0.3, §5.1).
            for (var i = 0; i < _agents.Length; i++)
            {
                _seeds[i] = SeedFor(_agents[i]);
                _policies[i] = CreatePolicy();
                _policies[i].Reset(_seeds[i]);
                _targets[i] = GazeTarget.AtPerson(FindHuman().ParticipantId);
            }

            if (LoggingEnabled)
                OpenLog();
        }

        void OnDestroy() => _log?.Dispose();

        void Start()
        {
            // The sidecar is written here rather than in Awake because it records
            // the replayed segment's turn boundaries, and the conversation only
            // has them once every component's Awake has run.
            _log?.WriteMetadata(BuildMetadataJson());
            _ = LogEveryFrameAsync(destroyCancellationToken);
        }

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

            // The demo freezes the motion players when the answer ends. Re-deciding
            // gaze past that point leaves the gaze layer as the only thing moving
            // on a still skeleton, which reads as the agent twitching rather than
            // as the demo having ended. Hold the last target instead.
            if (Conversation != null && Conversation.HasFinished)
                return;

            var turn = CurrentTurn();

            for (var i = 0; i < _agents.Length; i++)
            {
                var state = BuildState(_agents[i], turn);
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

        ConversationState BuildState(GazeParticipant self, in Turn turn)
        {
            var partners = PartnersOf(self);

            return new ConversationState
            {
                Time = _sessionTime,
                CurrentSpeaker = turn.Speaker,
                CurrentAddressee = turn.Addressee,
                SelfId = self.ParticipantId,
                SelfRole = RoleOf(self.ParticipantId, turn.Speaker, turn.Addressee),
                FirstPartner = partners.first,
                SecondPartner = partners.second,

                // Only the scripted schedule can say where a turn is going; with
                // no director these stay "unknown", which is all baseline B needs
                // since it ignores them by design.
                TurnPhase = Director != null ? Director.PhaseOf(self) : TurnPhase.NotSpeaking,
                TimeSinceTurnInstant = Director != null ? Director.TimeSinceTurnInstant : float.PositiveInfinity,
                PredictedTimeToTurnEnd = Director != null ? Director.PredictedTimeToTurnEnd : -1f,
                UpcomingEventIndex = Director != null ? Director.UpcomingEventIndex : -1,
                UpcomingEventType = Director != null ? Director.UpcomingEventType : EotType.TurnTaking,

                MutualGazeActive = IsInMutualGazeWithAnAgent(self),
                MutualGazeDuration = 0f,
            };
        }

        void Apply(GazeParticipant agent, in GazeTarget target)
        {
            if (target.Type == GazeTargetType.Aversion)
            {
                agent.Gaze.SetAversion(target.AversionOffset);
                return;
            }

            var person = FindParticipant(target.Person);
            if (person != null)
            {
                agent.Gaze.SetTarget(person.LookAtAnchor);
                return;
            }

            if (_warnedAboutMissingTarget)
                return;

            _warnedAboutMissingTarget = true;
            Debug.LogWarning($"{name}: a policy asked for participant {target.Person}, who is not in the triad; holding gaze.", this);
        }

        /// <summary>The policy this condition runs, one fresh instance per agent.</summary>
        IGazePolicy CreatePolicy()
        {
            var human = FindHuman();

            return Condition switch
            {
                GazeCondition.SpeakerFollowing =>
                    new SpeakerFollowingGazePolicy(SpeakerFollowing, _voiceActivity, human.ParticipantId),
                GazeCondition.RoleConditioned =>
                    new ShintaniGazePolicy(_roleConditionedParameters, human.ParticipantId),

                // Baseline B is the substrate, so outside the pre-turn window this
                // condition is bit-for-bit the speaker-following baseline and the
                // pairwise contrast isolates the pattern (user's call 2026-08-09).
                // The pattern seed is the run's, not the agent's: both agents must
                // draw the same prototype for a boundary or they would play two
                // different patterns' tracks at each other.
                GazeCondition.Proposed =>
                    new ProposedGazePolicy(
                        new SpeakerFollowingGazePolicy(SpeakerFollowing, _voiceActivity, human.ParticipantId),
                        Proposed,
                        BaseSeed),
                _ => throw new NotImplementedException($"Condition {Condition} is not implemented."),
            };
        }

        /// <summary>
        /// Load whatever the selected condition needs before any policy is built.
        /// Both schedule-driven conditions are refused rather than degraded when
        /// the director is missing: baseline A would code every fixation as if it
        /// were far from a turn boundary, and the proposed condition's window
        /// would never open at all — each silently produces a plausible but wrong
        /// trial.
        /// </summary>
        bool TryPrepareCondition()
        {
            if (Condition == GazeCondition.SpeakerFollowing)
                return true;

            if (Director == null)
            {
                Debug.LogError($"{name}: the {Condition} condition needs a ConversationDirector to know when turns end.", this);
                return false;
            }

            // Only baseline A reads the corpus model now; the proposed condition
            // runs on baseline B's substrate and needs no fitted parameters.
            if (Condition != GazeCondition.RoleConditioned)
                return true;

            try
            {
                _roleConditionedParameters = ShintaniGazeParameters.LoadDefault();
            }
            catch (Exception e)
            {
                Debug.LogError($"{name}: could not load the gaze model parameters: {e.Message}", this);
                return false;
            }

            return true;
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
            var turn = CurrentTurn();
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
                    SelfRole = RoleOf(agent.ParticipantId, turn.Speaker, turn.Addressee).ToString(),
                    TurnPhase = (Director != null ? Director.PhaseOf(agent) : TurnPhase.NotSpeaking).ToString(),
                    CurrentSpeaker = turn.Speaker.Index,
                    CurrentAddressee = turn.Addressee.Index,
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

        /// <summary>Who holds the floor and who they address, at one instant.</summary>
        readonly struct Turn
        {
            public Turn(ParticipantId speaker, ParticipantId addressee)
            {
                Speaker = speaker;
                Addressee = addressee;
            }

            public ParticipantId Speaker { get; }

            public ParticipantId Addressee { get; }
        }

        /// <summary>
        /// The scripted schedule when there is one, voice activity otherwise. The
        /// script is preferred because it keeps the roles defined through the gaps
        /// between turns, where the VAD only reports that nobody is speaking —
        /// baseline B is unaffected either way, since it reads voice activity
        /// directly rather than through the conversation state.
        /// </summary>
        Turn CurrentTurn()
        {
            if (Director != null && Director.HasTurn)
                return new Turn(Director.SpeakerId, Director.AddresseeId);

            var speaker = ConfirmedSpeaker();
            return new Turn(speaker, AddresseeOf(speaker));
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

            try
            {
                _log = new GazeLogWriter(TakeDirectory, stem);
                Debug.Log($"{name}: gaze log -> {_log.CsvPath}", this);
            }
            catch (System.IO.IOException e)
            {
                Debug.LogError($"{name}: could not open gaze log: {e.Message}", this);
                _log = null;
            }
        }

        /// <summary>Which corpus segment this take replays, so a log identifies its own material.</summary>
        void AppendSegment(StringBuilder json)
        {
            var segment = Conversation != null ? Conversation.Segment : null;
            if (segment == null)
                return;

            var c = CultureInfo.InvariantCulture;
            json.Append("  \"segment\": {\n");
            json.Append($"    \"name\": \"{segment.name}\",\n");
            json.Append($"    \"stem\": \"{segment.stem}\",\n");
            json.Append($"    \"source_start_s\": {segment.sourceStartSeconds.ToString("0.###", c)},\n");
            json.Append($"    \"duration_s\": {segment.durationSeconds.ToString("0.###", c)},\n");
            json.Append($"    \"motion_start_frame\": {segment.motionStartFrame.ToString(c)},\n");
            json.Append($"    \"eot_events\": {segment.events.Length.ToString(c)}\n");
            json.Append("  },\n");
        }

        /// <summary>
        /// The prototype each boundary will get. The draw is a pure function of
        /// the pattern seed, the event index and the event's class, so the whole
        /// mapping is known before the first boundary arrives — which is what
        /// makes a take reproducible from its own log.
        /// </summary>
        void AppendPatternDraw(StringBuilder json)
        {
            var segment = Conversation != null ? Conversation.Segment : null;
            if (segment == null || segment.events.Length == 0)
            {
                json.Append("    \"patterns\": []\n");
                return;
            }

            json.Append("    \"patterns\": [\n");
            for (var i = 0; i < segment.events.Length; i++)
            {
                var eotType = (EotType)segment.events[i].eotType;
                var pattern = ProposedGazePolicy.SelectPattern(Proposed, BaseSeed, segment.events[i].index, eotType);
                var comma = i < segment.events.Length - 1 ? "," : "";
                json.Append(
                    $"      {{ \"event\": {segment.events[i].index}, \"eot_type\": \"{eotType}\", " +
                    $"\"turn_time_s\": {segment.events[i].turnTime.ToString("0.###", CultureInfo.InvariantCulture)}, " +
                    $"\"prototype\": \"{pattern.Name}\" }}{comma}\n");
            }

            json.Append("    ]\n");
        }

        string BuildMetadataJson()
        {
            var c = CultureInfo.InvariantCulture;
            var json = new StringBuilder();

            json.Append("{\n");
            json.Append($"  \"study_participant\": \"{StudyParticipantId}\",\n");
            json.Append($"  \"case\": \"{CaseName}\",\n");
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

            if (_roleConditionedParameters != null)
            {
                json.Append("  \"gaze_model\": {\n");
                json.Append("    \"parameters\": \"Resources/BaselineAParameters.json\",\n");
                json.Append($"    \"min_dwell_s\": {_roleConditionedParameters.MinDwellSeconds.ToString("0.###", c)},\n");
                json.Append($"    \"max_dwell_s\": {_roleConditionedParameters.MaxDwellSeconds.ToString("0.###", c)},\n");
                json.Append($"    \"turn_window_s\": {_roleConditionedParameters.TurnWindowSeconds.ToString("0.###", c)}\n");
                json.Append("  },\n");
            }

            AppendSegment(json);

            if (Condition == GazeCondition.Proposed)
            {
                json.Append("  \"proposed\": {\n");
                json.Append("    \"substrate\": \"SpeakerFollowing\",\n");
                json.Append($"    \"selection\": \"{Proposed.Selection}\",\n");
                json.Append($"    \"pattern_seed\": {BaseSeed.ToString(c)},\n");
                json.Append($"    \"pre_turn_window_s\": {Proposed.PreTurnWindowSeconds.ToString("0.###", c)},\n");
                json.Append($"    \"pattern_time_scale\": {Proposed.PatternTimeScale.ToString("0.###", c)},\n");
                AppendPatternDraw(json);
                json.Append("  },\n");
            }

            json.Append("  \"agents\": [\n");

            for (var i = 0; i < _agents.Length; i++)
            {
                var comma = i < _agents.Length - 1 ? "," : "";
                json.Append($"    {{ \"id\": {_agents[i].Id.ToString(c)}, \"name\": \"{_agents[i].DisplayName}\", \"seed\": {_seeds[i].ToString(c)} }}{comma}\n");
            }

            json.Append("  ],\n");
            json.Append($"  \"unity_version\": \"{Application.unityVersion}\",\n");
            json.Append($"  \"blink_note\": \"no blink model (SMPL-X has no eyelid shapes); head motion is mocap only, identical in every condition\",\n");
            json.Append("  \"human_gaze_note\": \"mutual_gaze_with_human records only the agent->human direction; the human's gaze is unobserved (no eye tracking)\"\n");
            json.Append("}\n");

            return json.ToString();
        }
    }
}
