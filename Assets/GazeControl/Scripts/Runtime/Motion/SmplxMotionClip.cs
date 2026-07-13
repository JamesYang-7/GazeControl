using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace GazeControl.Motion
{
    /// <summary>
    /// A motion clip loaded from a SMPL-X .npz file (TalkingWithHands / AMASS style):
    /// 'poses' (frames × 165 axis-angle, 55 joints × 3, radians, right-handed Y-up),
    /// 'trans' (frames × 3 root translation, meters) and 'mocap_frame_rate'.
    /// A .npz is a plain zip of .npy entries, so no NumPy/Python is needed at runtime.
    /// </summary>
    public sealed class SmplxMotionClip
    {
        public const int JointCount = 55;
        const int PoseDim = JointCount * 3;

        /// <summary>Flat frames × 165 axis-angle pose data (dataset coordinates).</summary>
        public readonly float[] Poses;

        /// <summary>Flat frames × 3 root translation (dataset coordinates).</summary>
        public readonly float[] Trans;

        public readonly int FrameCount;
        public readonly float FrameRate;

        SmplxMotionClip(float[] poses, float[] trans, int frameCount, float frameRate)
        {
            Poses = poses;
            Trans = trans;
            FrameCount = frameCount;
            FrameRate = frameRate;
        }

        public float Duration => FrameCount / FrameRate;

        /// <summary>Axis-angle of one joint at one frame, in dataset (right-handed) coordinates.</summary>
        public Vector3 GetJointAxisAngle(int frame, int joint)
        {
            var o = frame * PoseDim + joint * 3;
            return new Vector3(Poses[o], Poses[o + 1], Poses[o + 2]);
        }

        /// <summary>Root translation at one frame, in dataset (right-handed) coordinates.</summary>
        public Vector3 GetTranslation(int frame)
        {
            var o = frame * 3;
            return new Vector3(Trans[o], Trans[o + 1], Trans[o + 2]);
        }

        /// <summary>Load a clip from a .npz path (absolute, or relative to the project root).</summary>
        public static SmplxMotionClip Load(string path)
        {
            if (!Path.IsPathRooted(path))
                path = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, path);

            using var zip = ZipFile.OpenRead(path);
            var poses = ReadEntry(zip, "poses.npy", path);
            var trans = ReadEntry(zip, "trans.npy", path);
            var fps = ReadEntry(zip, "mocap_frame_rate.npy", path);

            if (poses.Shape.Length != 2 || poses.Shape[1] != PoseDim)
                throw new InvalidDataException(
                    $"'{path}' poses shape {poses.ShapeString()}; expected (frames, {PoseDim})");
            var frameCount = poses.Shape[0];
            if (trans.Shape.Length != 2 || trans.Shape[0] != frameCount || trans.Shape[1] != 3)
                throw new InvalidDataException(
                    $"'{path}' trans shape {trans.ShapeString()}; expected ({frameCount}, 3)");

            return new SmplxMotionClip(poses.Data, trans.Data, frameCount, fps.Data[0]);
        }

        static NpyArray ReadEntry(ZipArchive zip, string entryName, string path)
        {
            var entry = zip.GetEntry(entryName)
                        ?? throw new InvalidDataException($"'{path}' has no '{entryName}' entry");
            using var stream = entry.Open();
            using var ms = new MemoryStream((int)entry.Length);
            stream.CopyTo(ms);
            return NpyReader.Parse(ms.ToArray(), entryName);
        }
    }
}
