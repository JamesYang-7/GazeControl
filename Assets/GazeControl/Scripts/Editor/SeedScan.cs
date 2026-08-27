using System;
using System.Collections.Generic;
using System.Linq;
using GazeControl.Conversation;
using GazeControl.Experiment;
using GazeControl.Gaze.Policy;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// Simulates one of the two stochastic conditions over a segment for a range
    /// of base seeds, offline, and reports how much each agent averts.
    ///
    /// <para>Why this exists: the Proposed condition's holding-replay substrate
    /// draws whole recorded fixation stretches, and a stretch can be long
    /// relative to a 25 s clip. Measured over the draw rule, the aversion
    /// fraction of a single take has a standard deviation of about 13 percentage
    /// points — one seed yields an agent that looks attentive, another one that
    /// looks checked out, from the same policy and the same clip. Baseline A
    /// samples dwells and jumps of its own and needs the same treatment, or an
    /// unlucky A draw goes unnoticed while the Proposed draw is checked. Picking
    /// a clip by watching one seed means judging the clip partly on its
    /// seed.</para>
    ///
    /// <para>It drives the real policies and the real
    /// <see cref="ConversationDirector"/> on a simulated clock rather than
    /// reimplementing them, and derives seeds with the runner's own
    /// <see cref="GazeConditionRunner.SeedFor(string, GazeConditionRunner.GazeCondition, int, int)"/>,
    /// so a scanned seed is the draw a live take will actually play.</para>
    /// </summary>
    public static class SeedScan
    {
        /// <summary>What one seed produced, per agent.</summary>
        public readonly struct Result
        {
            public Result(int baseSeed, float[] aversionFractions)
            {
                BaseSeed = baseSeed;
                AversionFractions = aversionFractions;
            }

            public int BaseSeed { get; }

            /// <summary>Fraction of decision ticks spent averting, one per agent, in Speakers order.</summary>
            public float[] AversionFractions { get; }

            public float Mean => AversionFractions.Length == 0 ? 0f : AversionFractions.Average();
        }

        /// <summary>
        /// Run <paramref name="seedCount"/> seeds from <paramref name="firstSeed"/>
        /// in <paramref name="condition"/>. Returns null (after logging) when the
        /// scene is not wired for it, or when the condition carries no random
        /// stream to scan.
        /// </summary>
        public static List<Result> Run(RecordedConversation conversation, GazeConditionRunner runner,
            DemoSegment segment, GazeConditionRunner.GazeCondition condition, int firstSeed, int seedCount)
        {
            if (conversation == null || runner == null || segment == null)
            {
                Debug.LogError("Seed scan: needs a RecordedConversation, a GazeConditionRunner and a segment.");
                return null;
            }

            if (conversation.User == null)
            {
                Debug.LogError("Seed scan: the conversation has no User assigned, so the human cannot be resolved.");
                return null;
            }

            // Both refusals below are the same point: the scan simulates in
            // silence, because the schedule and the clock are what the scanned
            // policies read. Anything driven by voice activity would be scanned
            // against a microphone that never opens — and both cases are
            // deterministic anyway, so there is nothing to scan.
            if (condition == GazeConditionRunner.GazeCondition.SpeakerFollowing)
            {
                Debug.LogWarning(
                    "Seed scan: baseline B carries no random stream and reads voice activity rather than " +
                    "the schedule, so there is nothing to scan.");
                return null;
            }

            if (condition == GazeConditionRunner.GazeCondition.Proposed
                && runner.Proposed.Substrate != ProposedSubstrate.HoldingReplay)
            {
                Debug.LogWarning(
                    "Seed scan: only the HoldingReplay substrate draws stretches, so there is nothing " +
                    "to scan on the SpeakerFollowing ablation — it is deterministic.");
                return null;
            }

            HoldingSequenceBank bank = null;
            ShintaniGazeParameters parameters = null;
            try
            {
                if (condition == GazeConditionRunner.GazeCondition.Proposed)
                    bank = HoldingSequenceBank.LoadDefault();
                else
                    parameters = ShintaniGazeParameters.LoadDefault();
            }
            catch (Exception e)
            {
                Debug.LogError($"Seed scan: could not load what the {condition} condition needs — {e.Message}");
                return null;
            }

            var agents = conversation.Speakers
                .Where(s => s.Participant != null)
                .OrderBy(s => s.SpeakerCode)
                .Select(s => s.Participant)
                .ToArray();

            if (agents.Length == 0)
            {
                Debug.LogError("Seed scan: no speakers with participants are wired on the conversation.");
                return null;
            }

            // The director is a MonoBehaviour, so the scan borrows a throwaway
            // one rather than duplicating its advance rule. HideAndDontSave keeps
            // it out of the scene and out of any save.
            var host = new GameObject("SeedScan (temporary)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var director = host.AddComponent<ConversationDirector>();
                director.Participants = runner.Participants;
                director.TurnWindowSeconds = runner.Director != null ? runner.Director.TurnWindowSeconds : 1f;

                var clock = 0f;
                var schedule = conversation.BuildSchedule(segment);

                var step = 1f / runner.DecisionHz;
                var ticks = Mathf.CeilToInt((segment.durationSeconds + segment.tailSeconds) / step);
                var human = conversation.User.ParticipantId;

                var results = new List<Result>(seedCount);
                for (var s = 0; s < seedCount; s++)
                {
                    var baseSeed = firstSeed + s;
                    var fractions = new float[agents.Length];

                    var policies = new IGazePolicy[agents.Length];
                    for (var i = 0; i < agents.Length; i++)
                    {
                        // The prototype seed handed to ProposedGazePolicy is the
                        // run's, not the agent's, exactly as in the runner: both
                        // agents must draw the same prototype for a boundary.
                        policies[i] = condition == GazeConditionRunner.GazeCondition.Proposed
                            ? new ProposedGazePolicy(
                                new HoldingReplayGazePolicy(bank, human), runner.Proposed, baseSeed)
                            : new ShintaniGazePolicy(parameters, human);

                        policies[i].Reset(GazeConditionRunner.SeedFor(
                            runner.StudyParticipantId, condition, agents[i].Id, baseSeed));
                    }

                    var averted = new int[agents.Length];
                    clock = 0f;

                    // Re-published rather than rewound, once per seed: the
                    // director advances monotonically by design — SyncTo only
                    // ever walks _index forward, because a live conversation
                    // never goes back — so a second sweep from clock 0 would run
                    // the whole simulation with the schedule stuck on the final
                    // turn. Reusing one director across seeds did exactly that:
                    // the first seed scanned was right and every later one was
                    // measured against frozen roles, which is why a single-seed
                    // scan matched a bake exactly while a twelve-seed scan did
                    // not. SetSchedule calls Enter(0), so this resets it.
                    director.SetSchedule(schedule);

                    for (var t = 0; t < ticks; t++)
                    {
                        clock = t * step;
                        director.SyncTo(clock);

                        for (var i = 0; i < agents.Length; i++)
                        {
                            var state = BuildState(agents[i], director, conversation, clock);
                            if (policies[i].Update(step, in state).Type == GazeTargetType.Aversion)
                                averted[i]++;
                        }
                    }

                    for (var i = 0; i < agents.Length; i++)
                        fractions[i] = ticks == 0 ? 0f : averted[i] / (float)ticks;

                    results.Add(new Result(baseSeed, fractions));
                }

                return results;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// The subset of <see cref="ConversationState"/> the scanned conditions
        /// read. Voice activity is absent on purpose: the holding substrate, the
        /// prototypes and baseline A's jump chain are all driven by the schedule
        /// and the clock, never by the microphone, so a silent simulation gives
        /// the same draws as a take. Whatever does read voice — baseline B, and
        /// the speaker-following ablation substrate — is refused by the guards
        /// in <see cref="Run"/>.
        /// </summary>
        static ConversationState BuildState(GazeParticipant self, ConversationDirector director,
            RecordedConversation conversation, float clock)
        {
            var speaker = director.Speaker != null ? director.Speaker.ParticipantId : ParticipantId.None;
            var addressee = director.Addressee != null ? director.Addressee.ParticipantId : ParticipantId.None;

            var partners = conversation.Speakers
                .Where(s => s.Participant != null && s.Participant != self)
                .Select(s => s.Participant.ParticipantId)
                .ToArray();

            return new ConversationState
            {
                Time = clock,
                CurrentSpeaker = speaker,
                CurrentAddressee = addressee,
                SelfId = self.ParticipantId,
                SelfRole = RoleOf(self.ParticipantId, speaker, addressee),
                FirstPartner = partners.Length > 0 ? partners[0] : conversation.User.ParticipantId,
                SecondPartner = partners.Length > 1 ? partners[1] : conversation.User.ParticipantId,
                TurnPhase = director.PhaseOf(self),
                TimeSinceTurnInstant = director.TimeSinceTurnInstant,
                PredictedTimeToTurnEnd = director.PredictedTimeToTurnEnd,
                UpcomingEventIndex = director.UpcomingEventIndex,
                UpcomingEventType = director.UpcomingEventType,
                MutualGazeActive = false,
                MutualGazeDuration = 0f,
            };
        }

        static ParticipantRole RoleOf(ParticipantId self, ParticipantId speaker, ParticipantId addressee)
        {
            if (self == speaker)
                return ParticipantRole.Speaker;

            return self == addressee ? ParticipantRole.Addressee : ParticipantRole.SideParticipant;
        }
    }
}
