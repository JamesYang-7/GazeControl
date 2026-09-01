using System;
using System.Collections.Generic;

namespace GazeControl.Study
{
    /// <summary>One clip in the session: which conversation, which method, where it sits.</summary>
    public readonly struct StudyTrial
    {
        public StudyTrial(int index, int blockNumber, int versionPosition, string conversation, string condition)
        {
            Index = index;
            BlockNumber = blockNumber;
            VersionPosition = versionPosition;
            Conversation = conversation;
            Condition = condition;
        }

        /// <summary>Position in the whole session, 0-based.</summary>
        public int Index { get; }

        /// <summary>Which block this belongs to, 1-based — one conversation per block.</summary>
        public int BlockNumber { get; }

        /// <summary>Where in its block this clip plays, 1..3. The response files record this.</summary>
        public int VersionPosition { get; }

        /// <summary>The conversation's name, matching its folder under <c>Assets/DemoSegments</c>.</summary>
        public string Conversation { get; }

        /// <summary>The gaze method, matching <c>GazeConditionRunner.GazeCondition</c>.</summary>
        public string Condition { get; }

        /// <summary>True on the last clip of a block, where the ranking is asked.</summary>
        public bool IsLastOfBlock => VersionPosition == 3;

        public override string ToString() =>
            $"{Index + 1}/15  block {BlockNumber} version {VersionPosition}: {Conversation} / {Condition}";
    }

    /// <summary>
    /// The running order of a session: 5 conversations × 3 methods, blocked by
    /// conversation, counterbalanced across participants.
    ///
    /// <para><b>Conversation order is identical for every participant</b> and
    /// deliberately not randomised. It cannot enter the contrast between methods,
    /// which is made within a block against the same conversation, so randomising
    /// it buys nothing — and a fixed conversation order means a log that goes
    /// missing or arrives misnamed is still identifiable by the block it sat
    /// in.</para>
    ///
    /// <para><b>Method order within a block is counterbalanced by participant.</b>
    /// Block <i>b</i> of participant ordinal <i>i</i> takes permutation
    /// <c>(i + b) mod 6</c>, which gives two properties at once. Within one
    /// participant the five blocks take five different permutations, so each
    /// method sits in each serial position once or twice — as even as five blocks
    /// over three positions allows — and nobody can learn that "the third one is
    /// always the proposed method". Across any six consecutive participants each
    /// block sees all six permutations, so each method occupies each serial
    /// position exactly equally. The marginal balance is therefore exact whenever
    /// N is a multiple of six, which is what the study's N = 30 rests on.</para>
    ///
    /// <para><b>Pre-generated, not shuffled at run time</b> (2026-08-31,
    /// superseding the single fixed order of 2026-08-27). The schedule is a pure
    /// function of the participant ordinal, so what any participant should have
    /// run is recoverable from their label alone — which is the property the
    /// fixed order was protecting, kept while getting the counterbalancing back.
    /// P01-P04 preceded this and ran one common order; they are declared pilots
    /// and excluded (`user-study-design.md` §0.1), so the schedule covers the
    /// analysed sample exactly.</para>
    /// </summary>
    public static class StudySequence
    {
        /// <summary>
        /// Build the running order. Pure and deterministic: the same inputs give
        /// the same list, on any machine.
        /// </summary>
        /// <param name="conversations">Conversation names, in the order their blocks run.</param>
        /// <param name="conditions">The methods, one block position each.</param>
        /// <param name="participantOrdinal">
        /// 0-based place in the counterbalancing schedule, from
        /// <see cref="ParticipantLabel.ScheduleOrdinal"/>. Rotates which
        /// permutation each block takes.
        /// </param>
        public static IReadOnlyList<StudyTrial> Build(
            IReadOnlyList<string> conversations, IReadOnlyList<string> conditions, int participantOrdinal)
        {
            if (conversations == null || conversations.Count == 0)
                throw new ArgumentException("a session needs at least one conversation", nameof(conversations));

            if (conditions == null || conditions.Count == 0)
                throw new ArgumentException("a session needs at least one method", nameof(conditions));

            var permutations = Permutations(conditions.Count);
            var trials = new List<StudyTrial>(conversations.Count * conditions.Count);

            for (var block = 0; block < conversations.Count; block++)
            {
                // (ordinal + block) rather than block alone: blocks still cycle
                // the permutations so five never repeat one, and the participant
                // offset makes six consecutive participants cover all six
                // permutations in every block. Floored modulo, because a caller
                // that passes a negative ordinal should get a valid schedule
                // rather than an IndexOutOfRange mid-session.
                var rotation = participantOrdinal + block;
                var order = permutations[((rotation % permutations.Count) + permutations.Count) % permutations.Count];

                for (var position = 0; position < conditions.Count; position++)
                {
                    trials.Add(new StudyTrial(
                        trials.Count, block + 1, position + 1,
                        conversations[block], conditions[order[position]]));
                }
            }

            return trials;
        }

        /// <summary>
        /// The permutation table: every cyclic rotation, then every rotation
        /// reversed.
        ///
        /// <para>Rotations alone would put each method in each position exactly
        /// once over three blocks but leave the *relative* order fixed — a
        /// participant would see the same neighbour pairs every time. Adding the
        /// reversals breaks that while keeping the position counts even, and for
        /// three methods the two families together are exactly the six
        /// permutations `user-study-design.md` §1 counts.</para>
        /// </summary>
        static IReadOnlyList<int[]> Permutations(int count)
        {
            var rotations = new List<int[]>(count);
            for (var r = 0; r < count; r++)
            {
                var rotation = new int[count];
                for (var i = 0; i < count; i++)
                    rotation[i] = (r + i) % count;

                rotations.Add(rotation);
            }

            var all = new List<int[]>(rotations);
            for (var r = 0; r < rotations.Count; r++)
            {
                var reversed = new int[count];
                for (var i = 0; i < count; i++)
                    reversed[i] = rotations[r][count - 1 - i];

                all.Add(reversed);
            }

            return all;
        }
    }
}
