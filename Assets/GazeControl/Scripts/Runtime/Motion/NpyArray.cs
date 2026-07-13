namespace GazeControl.Motion
{
    /// <summary>
    /// A C-order numeric array loaded from a NumPy .npy entry, converted to float32.
    /// Source dtypes wider than float32 (e.g. float64 mocap data) are narrowed on
    /// load: joint angles / translations don't need double precision for animation.
    /// </summary>
    public sealed class NpyArray
    {
        /// <summary>Flat, C-order (row-major) data.</summary>
        public readonly float[] Data;

        public readonly int[] Shape;

        public NpyArray(float[] data, int[] shape)
        {
            Data = data;
            Shape = shape;
        }

        public string ShapeString() => "(" + string.Join(", ", Shape) + ")";
    }
}
