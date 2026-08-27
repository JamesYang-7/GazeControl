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
    /// The fixed running order of a session: 5 conversations × 3 methods,
    /// blocked by conversation.
    ///
    /// <para><b>The same order for every participant</b> (user's call,
    /// 2026-08-27, superseding the per-participant counterbalancing of
    /// `user-study-design.md` §1). The reason is operational: with one known
    /// order, a log file that goes missing or arrives misnamed can still be
    /// identified by the position it occupied, which a per-participant shuffle
    /// makes impossible.</para>
    ///
    /// <para><b>Method order still varies between blocks</b>, and that is what
    /// keeps the design sound. The permutations are chosen so that across the
    /// five blocks each method sits in each serial position either once or twice
    /// — as even as five blocks over three positions allows. Serial position is
    /// therefore balanced <i>within</i> each participant, which is where a
    /// novelty or fatigue effect would otherwise load onto one method. It also
    /// means a participant cannot learn that "the third one is always the
    /// proposed method", which back-to-back repetition would otherwise teach by
    /// block three.</para>
    ///
    /// <para>What is given up against §1: order effects no longer average across
    /// the sample as well as within it, and the N = 30 multiple-of-six argument
    /// no longer applies. Record it as a limitation rather than a property.</para>
    /// </summary>
    public static class StudySequence
    {
        /// <summary>
        /// Build the running order. Pure and deterministic: the same inputs give
        /// the same list, on any machine, for any participant.
        /// </summary>
        /// <param name="conversations">Conversation names, in the order their blocks run.</param>
        /// <param name="conditions">The methods, one block position each.</param>
        public static IReadOnlyList<StudyTrial> Build(
            IReadOnlyList<string> conversations, IReadOnlyList<string> conditions)
        {
            if (conversations == null || conversations.Count == 0)
                throw new ArgumentException("a session needs at least one conversation", nameof(conversations));

            if (conditions == null || conditions.Count == 0)
                throw new ArgumentException("a session needs at least one method", nameof(conditions));

            var permutations = Permutations(conditions.Count);
            var trials = new List<StudyTrial>(conversations.Count * conditions.Count);

            for (var block = 0; block < conversations.Count; block++)
            {
                // Blocks cycle through the permutations in order. With three
                // methods there are six, so five blocks never repeat one.
                var order = permutations[block % permutations.Count];

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
