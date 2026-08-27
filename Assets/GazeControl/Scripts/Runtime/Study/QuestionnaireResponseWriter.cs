using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GazeControl.Study
{
    /// <summary>
    /// One participant's answers, written as they are entered.
    ///
    /// <para>Two files in the participant's own folder: <c>responses.csv</c> for
    /// the Likert ratings and the ranks, and <c>comments.jsonl</c> for the
    /// free-text probe. Free text is kept out of the numeric file because it
    /// contains commas and newlines by nature and must not be able to corrupt
    /// the file the analysis reads.</para>
    ///
    /// <para><b>Every answer is flushed as it is entered</b>, unlike
    /// <c>GazeLogWriter</c>, which buffers because it takes ~120 rows a second
    /// off the render thread. This one takes about twenty rows an hour, so the
    /// trade runs the other way: a session that dies in block 4 must still have
    /// blocks 1-3 on disk. This project has already lost a file to writing at the
    /// end of a run (a bake threw its own track away in <c>OnDestroy</c>,
    /// 2026-08-25).</para>
    ///
    /// <para>The writer is also where the recording rules are enforced, in the
    /// spirit of <c>GazeConditionRunner.StudySessionProblem()</c>: an answer off
    /// the scale, an unknown item code, a block or version position outside the
    /// run, or a second answer to a question already answered are all refused
    /// rather than written. Each of them produces a file that looks complete and
    /// is not.</para>
    /// </summary>
    public sealed class QuestionnaireResponseWriter : IDisposable
    {
        const string ResponsesFileName = "responses.csv";
        const string CommentsFileName = "comments.jsonl";

        const string Header = "participant,block,conversation,version_position,condition,item,response,utc";

        readonly QuestionnaireScript _script;
        readonly string _participantId;
        readonly Func<DateTime> _utcNow;
        readonly HashSet<string> _answered = new();

        StreamWriter _responses;
        StreamWriter _comments;

        /// <param name="script">The run being administered; supplies the instrument and the run's bounds.</param>
        /// <param name="directory">The participant's folder. Created if absent.</param>
        /// <param name="participantId">Study participant id, written into every row.</param>
        /// <param name="utcNow">Clock, for tests. Defaults to the system UTC clock.</param>
        public QuestionnaireResponseWriter(
            QuestionnaireScript script, string directory, string participantId, Func<DateTime> utcNow = null)
        {
            _script = script ?? throw new ArgumentNullException(nameof(script));

            if (string.IsNullOrWhiteSpace(participantId))
                throw new ArgumentException("a participant id is required", nameof(participantId));

            _participantId = participantId;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);

            Directory.CreateDirectory(directory);
            ResponsesPath = Path.Combine(directory, ResponsesFileName);
            CommentsPath = Path.Combine(directory, CommentsFileName);

            // Refuse to reopen a participant's folder rather than appending to it
            // or truncating it: both silently destroy a session — one interleaves
            // two runs in one file, the other deletes the first.
            if (File.Exists(ResponsesPath))
            {
                throw new IOException(
                    $"{ResponsesPath} already exists. Every participant gets their own folder; " +
                    "pick an unused id rather than writing over a recorded session.");
            }

            _responses = new StreamWriter(ResponsesPath, append: false, Encoding.UTF8) { AutoFlush = true };
            _responses.WriteLine(Header);

            _comments = new StreamWriter(CommentsPath, append: false, Encoding.UTF8) { AutoFlush = true };
        }

        public string ResponsesPath { get; }

        public string CommentsPath { get; }

        /// <summary>Answers written so far, ranks included; comments are not counted.</summary>
        public int ResponseCount { get; private set; }

        /// <summary>True once every rating and rank the run asks for has been recorded.</summary>
        public bool IsComplete => ResponseCount == _script.ExpectedResponseCount;

        /// <summary>Record one Likert answer.</summary>
        /// <param name="conversation">Clip export name, e.g. <c>study_c3</c>.</param>
        /// <param name="condition">The method behind this version. Written here and shown nowhere.</param>
        public void AppendRating(
            int block, string conversation, int versionPosition, string condition, string itemCode, int response)
        {
            RequireBlock(block);
            RequireVersionPosition(versionPosition);

            if (_script.Definition.FindItem(itemCode) == null)
                throw new ArgumentException($"'{itemCode}' is not an item of this instrument", nameof(itemCode));

            var scale = _script.Definition.scale;
            if (!scale.Contains(response))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(response), response, $"outside the {scale.min}-{scale.max} response scale");
            }

            Write(block, conversation, versionPosition, condition, itemCode, response);
        }

        /// <summary>Record where one version of a block was ranked (1 = most natural).</summary>
        public void AppendRank(int block, string conversation, int versionPosition, string condition, int rank)
        {
            RequireBlock(block);
            RequireVersionPosition(versionPosition);

            if (rank < 1 || rank > _script.VersionsPerBlock)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rank), rank, $"a rank runs 1-{_script.VersionsPerBlock}");
            }

            Write(block, conversation, versionPosition, condition, _script.Definition.ranking.code, rank);
        }

        /// <summary>
        /// Record the block's free-text answer. A skipped question is written as
        /// a record with no text rather than omitted, so "asked and declined" is
        /// distinguishable from "never reached".
        /// </summary>
        public void AppendComment(int block, string conversation, string text)
        {
            RequireBlock(block);

            var skipped = string.IsNullOrWhiteSpace(text);

            // JsonUtility rather than hand-rolled quoting: the answer is spoken
            // by a participant and typed by an experimenter, so it can hold
            // quotes, newlines and anything else a keyboard produces.
            var record = new CommentRecord
            {
                participant = _participantId,
                block = block,
                conversation = conversation,
                item = _script.Definition.comment.code,
                text = skipped ? string.Empty : text,
                skipped = skipped,
                utc = Timestamp(),
            };

            _comments.WriteLine(JsonUtility.ToJson(record));
        }

        public void Dispose()
        {
            try
            {
                _responses?.Dispose();
                _comments?.Dispose();
            }
            catch (IOException e)
            {
                Debug.LogError($"Failed to close questionnaire responses {ResponsesPath}: {e.Message}");
            }
            finally
            {
                _responses = null;
                _comments = null;
            }
        }

        void Write(int block, string conversation, int versionPosition, string condition, string item, int response)
        {
            var key = $"{block}|{versionPosition}|{item}";
            if (!_answered.Add(key))
            {
                // No amendment path on purpose: correcting an entered answer is an
                // operator-panel affordance that does not exist yet, and silently
                // keeping two answers to one question is the failure this guards.
                throw new InvalidOperationException(
                    $"block {block}, version {versionPosition}, item {item} has already been answered.");
            }

            var line = new StringBuilder()
                .Append(Csv(_participantId)).Append(',')
                .Append(block.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(conversation)).Append(',')
                .Append(versionPosition.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(condition)).Append(',')
                .Append(Csv(item)).Append(',')
                .Append(response.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Csv(Timestamp()));

            _responses.WriteLine(line.ToString());
            ResponseCount++;
        }

        void RequireBlock(int block)
        {
            if (block < 1 || block > _script.BlockCount)
                throw new ArgumentOutOfRangeException(nameof(block), block, $"the run has {_script.BlockCount} blocks");
        }

        void RequireVersionPosition(int versionPosition)
        {
            if (versionPosition < 1 || versionPosition > _script.VersionsPerBlock)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(versionPosition), versionPosition,
                    $"a block shows {_script.VersionsPerBlock} versions");
            }
        }

        // Round-trip ISO 8601, invariant: a machine reading these must not have to
        // know the recording machine's locale — the same rule GazeLogWriter follows.
        string Timestamp() => _utcNow().ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        static string Csv(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            if (field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return field;

            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        [Serializable]
        sealed class CommentRecord
        {
            public string participant;
            public int block;
            public string conversation;
            public string item;
            public string text;
            public bool skipped;
            public string utc;
        }
    }
}
