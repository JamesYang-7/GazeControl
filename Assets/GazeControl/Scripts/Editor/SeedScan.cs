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
    /// Simulates the Proposed condition over a segment for a range of base
    /// seeds, offline, and reports how much each agent averts.
    ///
    /// <para>Why this exists: the holding-replay substrate draws whole recorded
    /// fixation stretches, and a stretch can be long relative to a 25 s clip.
    /// Measured over the draw rule, the aversion fraction of a single take has a
    /// standard deviation of about 13 percentage points — one seed yields an
    /// agent that looks attentive, another one that looks checked out, from the
    /// same policy and the same clip. Picking a clip by watching one seed means
    /// judging the clip partly on its seed.</para>
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
        /// Run <paramref name="seedCount"/> seeds from <paramref name="firstSeed"/>.
        /// Returns null (after logging) when the scene is not wired for it.
        /// </summary>
        public static List<Result> Run(RecordedConversation conversation, GazeConditionRunner runner,
            DemoSegment segment, int firstSeed, int seedCount)
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

            HoldingSequenceBank bank;
            ShintaniGazeParameters parameters;
            try
            {
                bank = HoldingSequenceBank.LoadDefault();
                parameters = ShintaniGazeParameters.LoadDefault();
            }
            catch (Exception e)
            {
                Debug.LogError($"Seed scan: could not load the gaze model data — {e.Message}");
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
                director.SetSchedule(conversation.BuildSchedule(segment), () => clock);

                var step = 1f / runner.DecisionHz;
                var ticks = Mathf.CeilToInt((segment.durationSeconds + segment.tailSeconds) / step);
                var human = conversation.User.ParticipantId;

                var results = new List<Result>(seedCount);
                for (var s = 0; s < seedCount; s++)
                {
                    var baseSeed = firstSeed + s;
                    var fractions = new float[agents.Length];

                    var policies = new ProposedGazePolicy[agents.Length];
                    for (var i = 0; i < agents.Length; i++)
                    {
                        policies[i] = new ProposedGazePolicy(
                            new HoldingReplayGazePolicy(bank, parameters, human),
                            runner.Proposed, baseSeed, parameters);
                        policies[i].Reset(GazeConditionRunner.SeedFor(
                            runner.StudyParticipantId, GazeConditionRunner.GazeCondition.Proposed,
                            agents[i].Id, baseSeed));
                    }

                    var averted = new int[agents.Length];
                    clock = 0f;
                    director.SyncToClock();

                    for (var t = 0; t < ticks; t++)
                    {
                        clock = t * step;
                        director.SyncToClock();

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
        /// The subset of <see cref="ConversationState"/> the proposed condition
        /// reads. Voice activity is absent on purpose: the holding substrate and
        /// the prototypes are driven by the schedule and the clock, never by the
        /// microphone, so a silent simulation gives the same draws as a take.
        /// The speaker-following ablation substrate *does* read voice, so it is
        /// not scanned — see the caller's guard.
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
