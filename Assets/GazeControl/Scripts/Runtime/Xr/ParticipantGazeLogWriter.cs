using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace GazeControl.Xr
{
    /// <summary>
    /// Appends <see cref="ParticipantGazeRow"/> records to a CSV in the take's
    /// own folder, beside the agent log and the video.
    ///
    /// <para>Buffered in the same way and for the same reason as
    /// <c>GazeLogWriter</c>: one participant at 60 Hz is 60 rows a second and
    /// flushing each would put file I/O on the render thread.</para>
    /// </summary>
    public sealed class ParticipantGazeLogWriter : IDisposable
    {
        const int FlushEveryRows = 120;

        const string Header =
            "t,tick,participant_id,condition,case," +
            "tracker,left_eye,right_eye,calibrated,device_frame,capture_time_ns," +
            "head_x,head_y,head_z,head_yaw,head_pitch,head_roll," +
            "gaze_ox,gaze_oy,gaze_oz,gaze_dx,gaze_dy,gaze_dz," +
            "left_ox,left_oy,left_oz,left_dx,left_dy,left_dz," +
            "right_ox,right_oy,right_oz,right_dx,right_dy,right_dz," +
            "focus_distance,focus_stability," +
            "ipd_mm,left_pupil_mm,right_pupil_mm,left_openness,right_openness," +
            "target_type,target_id,target_angle_deg,target_distance," +
            "mutual_gaze,agents_looking_at_user";

        readonly StringBuilder _buffer = new();
        readonly StreamWriter _writer;
        int _bufferedRows;

        public ParticipantGazeLogWriter(string directory, string fileNameStem)
        {
            Directory.CreateDirectory(directory);
            CsvPath = Path.Combine(directory, $"{fileNameStem}.csv");

            _writer = new StreamWriter(CsvPath, append: false, Encoding.UTF8);
            _writer.WriteLine(Header);
        }

        public string CsvPath { get; }

        public int RowCount { get; private set; }

        /// <summary>Rows whose tracker status was <see cref="ParticipantGazeStatus.Valid"/>.</summary>
        public int ValidRowCount { get; private set; }

        public void Append(in ParticipantGazeRow row)
        {
            // Invariant culture throughout, for the same reason as the agent log:
            // a machine parsing these files must not have to know the recording
            // machine's decimal separator.
            var c = CultureInfo.InvariantCulture;

            _buffer.Append(row.Time.ToString("0.####", c)).Append(',');
            _buffer.Append(row.Tick.ToString(c)).Append(',');
            _buffer.Append(row.ParticipantId).Append(',');
            _buffer.Append(row.Condition).Append(',');
            _buffer.Append(row.Case).Append(',');
            _buffer.Append(row.TrackerStatus).Append(',');
            _buffer.Append(row.LeftEyeStatus).Append(',');
            _buffer.Append(row.RightEyeStatus).Append(',');
            _buffer.Append(row.Calibrated ? '1' : '0').Append(',');
            _buffer.Append(row.DeviceFrame.ToString(c)).Append(',');
            _buffer.Append(row.CaptureTimeNanoseconds.ToString(c)).Append(',');
            AppendVector(row.HeadPosition, "0.####");
            _buffer.Append(row.HeadYaw.ToString("0.###", c)).Append(',');
            _buffer.Append(row.HeadPitch.ToString("0.###", c)).Append(',');
            _buffer.Append(row.HeadRoll.ToString("0.###", c)).Append(',');
            AppendVector(row.GazeOrigin, "0.####");
            AppendVector(row.GazeDirection, "0.#####");
            AppendVector(row.LeftGazeOrigin, "0.####");
            AppendVector(row.LeftGazeDirection, "0.#####");
            AppendVector(row.RightGazeOrigin, "0.####");
            AppendVector(row.RightGazeDirection, "0.#####");
            _buffer.Append(row.FocusDistance.ToString("0.####", c)).Append(',');
            _buffer.Append(row.FocusStability.ToString("0.####", c)).Append(',');
            _buffer.Append(row.InterPupillaryDistanceMm.ToString("0.##", c)).Append(',');
            _buffer.Append(row.LeftPupilDiameterMm.ToString("0.###", c)).Append(',');
            _buffer.Append(row.RightPupilDiameterMm.ToString("0.###", c)).Append(',');
            _buffer.Append(row.LeftEyeOpenness.ToString("0.###", c)).Append(',');
            _buffer.Append(row.RightEyeOpenness.ToString("0.###", c)).Append(',');
            _buffer.Append(row.TargetType).Append(',');
            _buffer.Append(row.TargetId.ToString(c)).Append(',');
            _buffer.Append(row.TargetAngleDegrees.ToString("0.###", c)).Append(',');
            _buffer.Append(row.TargetDistance.ToString("0.####", c)).Append(',');
            _buffer.Append(row.MutualGaze ? '1' : '0').Append(',');
            _buffer.Append(row.AgentsLookingAtUser.ToString(c)).Append('\n');

            RowCount++;
            if (row.TrackerStatus == ParticipantGazeStatus.Valid)
                ValidRowCount++;

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
                Debug.LogError($"Failed to close participant gaze log {CsvPath}: {e.Message}");
            }
        }

        void AppendVector(Vector3 v, string format)
        {
            var c = CultureInfo.InvariantCulture;
            _buffer.Append(v.x.ToString(format, c)).Append(',');
            _buffer.Append(v.y.ToString(format, c)).Append(',');
            _buffer.Append(v.z.ToString(format, c)).Append(',');
        }
    }
}
