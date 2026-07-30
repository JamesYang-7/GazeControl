using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GazeControl.Logging
{
    /// <summary>
    /// Appends <see cref="GazeLogRow"/> records to a CSV, one file per session,
    /// alongside a sidecar JSON holding everything needed to replay the trial
    /// (seeds, thresholds, tick rate).
    ///
    /// Rows buffer in memory and flush in batches: per-frame logging for two
    /// agents is ~120 rows/second, and flushing each one would put file I/O on
    /// the render thread.
    /// </summary>
    public sealed class GazeLogWriter : IDisposable
    {
        const int FlushEveryRows = 120;

        const string Header =
            "t,participant_id,condition,agent_id,self_role,turn_phase," +
            "current_speaker,current_addressee," +
            "target_type,target_id,aversion_yaw,aversion_pitch," +
            "is_shifting,mutual_gaze_with_human,mutual_gaze_with_other_agent," +
            "head_yaw,head_pitch,eye_yaw,eye_pitch,seed";

        readonly StringBuilder _buffer = new();
        readonly StreamWriter _writer;
        int _bufferedRows;

        public GazeLogWriter(string directory, string fileNameStem)
        {
            Directory.CreateDirectory(directory);
            CsvPath = Path.Combine(directory, $"{fileNameStem}.csv");
            MetadataPath = Path.Combine(directory, $"{fileNameStem}.json");

            _writer = new StreamWriter(CsvPath, append: false, Encoding.UTF8);
            _writer.WriteLine(Header);
        }

        public string CsvPath { get; }

        public string MetadataPath { get; }

        public int RowCount { get; private set; }

        /// <summary>Write the replay sidecar. <paramref name="json"/> is written verbatim.</summary>
        public void WriteMetadata(string json) => File.WriteAllText(MetadataPath, json, Encoding.UTF8);

        public void Append(in GazeLogRow row)
        {
            // Invariant culture throughout: a machine parsing these files must not
            // have to know the recording machine's decimal separator.
            var c = CultureInfo.InvariantCulture;

            _buffer.Append(row.Time.ToString("0.####", c)).Append(',');
            _buffer.Append(row.ParticipantId).Append(',');
            _buffer.Append(row.Condition).Append(',');
            _buffer.Append(row.AgentId).Append(',');
            _buffer.Append(row.SelfRole).Append(',');
            _buffer.Append(row.TurnPhase).Append(',');
            _buffer.Append(row.CurrentSpeaker.ToString(c)).Append(',');
            _buffer.Append(row.CurrentAddressee.ToString(c)).Append(',');
            _buffer.Append(row.TargetType).Append(',');
            _buffer.Append(row.TargetId.ToString(c)).Append(',');
            _buffer.Append(row.AversionYaw.ToString("0.###", c)).Append(',');
            _buffer.Append(row.AversionPitch.ToString("0.###", c)).Append(',');
            _buffer.Append(row.IsShifting ? '1' : '0').Append(',');
            _buffer.Append(row.MutualGazeWithHuman ? '1' : '0').Append(',');
            _buffer.Append(row.MutualGazeWithOtherAgent ? '1' : '0').Append(',');
            _buffer.Append(row.HeadYaw.ToString("0.###", c)).Append(',');
            _buffer.Append(row.HeadPitch.ToString("0.###", c)).Append(',');
            _buffer.Append(row.EyeYaw.ToString("0.###", c)).Append(',');
            _buffer.Append(row.EyePitch.ToString("0.###", c)).Append(',');
            _buffer.Append(row.Seed.ToString(c)).Append('\n');

            RowCount++;
            if (++_bufferedRows < FlushEveryRows)
                return;

            Flush();
        }

        public void Flush()
        {
            if (_bufferedRows == 0)
                return;

            _writer.Write(_buffer.ToString());
            _writer.Flush();
            _buffer.Clear();
            _bufferedRows = 0;
        }

        public void Dispose()
        {
            try
            {
                Flush();
                _writer.Dispose();
            }
            catch (IOException e)
            {
                Debug.LogError($"Failed to close gaze log {CsvPath}: {e.Message}");
            }
        }
    }
}
