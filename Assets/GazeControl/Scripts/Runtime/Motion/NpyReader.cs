using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GazeControl.Motion
{
    /// <summary>
    /// Minimal NumPy .npy parser for C-order little-endian arrays, adapted from
    /// VR-Chat's HmdBodyRecon.NpyReader and extended with '&lt;f8' / '&lt;i4'
    /// support (TalkingWithHands .npz entries are float64, frame rate is int32).
    ///
    /// Robustness over convenience: the header (dtype, fortran_order, shape) is
    /// parsed and validated, so a file saved with the wrong dtype/order fails
    /// loudly with a descriptive message instead of silently loading garbage.
    /// Supports .npy format versions 1.0, 2.0 and 3.0.
    /// </summary>
    public static class NpyReader
    {
        static readonly byte[] Magic = { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y' };

        /// <summary>Parse a .npy byte block (e.g. a .npz zip entry). <paramref name="name"/> is only used in error messages.</summary>
        public static NpyArray Parse(byte[] raw, string name)
        {
            if (raw.Length < 10)
                throw new InvalidDataException($"npy: '{name}' too short ({raw.Length} bytes)");
            for (var i = 0; i < Magic.Length; i++)
                if (raw[i] != Magic[i])
                    throw new InvalidDataException($"npy: '{name}' bad magic; not a .npy entry");

            int major = raw[6];
            int headerLen, headerStart;
            if (major == 1)
            {
                headerLen = raw[8] | (raw[9] << 8); // uint16 LE
                headerStart = 10;
            }
            else // 2.x / 3.x use a uint32 LE header length
            {
                headerLen = raw[8] | (raw[9] << 8) | (raw[10] << 16) | (raw[11] << 24);
                headerStart = 12;
            }

            if (headerStart + (long)headerLen > raw.Length)
                throw new InvalidDataException($"npy: '{name}' header length out of range");

            var header = Encoding.ASCII.GetString(raw, headerStart, headerLen);

            var descr = MatchField(header, @"'descr'\s*:\s*'([^']+)'", name, "descr");
            var fortran = MatchField(header, @"'fortran_order'\s*:\s*(True|False)", name, "fortran_order");
            var shapeStr = MatchField(header, @"'shape'\s*:\s*\(([^)]*)\)", name, "shape");

            if (fortran == "True")
                throw new NotSupportedException(
                    $"npy: '{name}' is Fortran-ordered; expected C-order. Re-save with np.ascontiguousarray(arr).");

            var shape = ParseShape(shapeStr);
            long count = 1;
            foreach (var d in shape) count *= d;

            var elementSize = descr switch
            {
                "<f4" or "|f4" => 4,
                "<f8" or "|f8" => 8,
                "<i4" or "|i4" => 4,
                _ => throw new NotSupportedException(
                    $"npy: '{name}' dtype '{descr}' unsupported; expected '<f4', '<f8' or '<i4'."),
            };
            if (count * elementSize > int.MaxValue)
                throw new NotSupportedException($"npy: '{name}' has {count} elements; too large for this reader.");

            var dataStart = headerStart + headerLen;
            long dataBytes = raw.LongLength - dataStart;
            if (dataBytes < count * elementSize)
                throw new InvalidDataException(
                    $"npy: '{name}' data block {dataBytes} bytes < expected {count * elementSize} for shape ({string.Join(", ", shape)})");

            var data = new float[count];
            switch (elementSize)
            {
                case 4 when descr.EndsWith("f4", StringComparison.Ordinal):
                    // little-endian platform + '<f4' => raw bytes map straight onto float32
                    Buffer.BlockCopy(raw, dataStart, data, 0, (int)(count * 4L));
                    break;
                case 8:
                    for (var i = 0; i < count; i++)
                        data[i] = (float)BitConverter.ToDouble(raw, dataStart + i * 8);
                    break;
                default: // <i4
                    for (var i = 0; i < count; i++)
                        data[i] = BitConverter.ToInt32(raw, dataStart + i * 4);
                    break;
            }

            return new NpyArray(data, shape);
        }

        static string MatchField(string header, string pattern, string name, string what)
        {
            var m = Regex.Match(header, pattern);
            if (!m.Success)
                throw new InvalidDataException($"npy: '{name}' header missing '{what}'. Header: {header.Trim()}");
            return m.Groups[1].Value;
        }

        static int[] ParseShape(string inner)
        {
            inner = inner.Trim();
            if (inner.Length == 0) return Array.Empty<int>(); // 0-d scalar
            var parts = inner.Split(',');
            var dims = new List<int>(parts.Length);
            foreach (var p in parts)
            {
                var s = p.Trim();
                if (s.Length == 0) continue; // tolerate trailing comma, e.g. "(5,)"
                dims.Add(int.Parse(s, CultureInfo.InvariantCulture));
            }

            return dims.ToArray();
        }
    }
}
