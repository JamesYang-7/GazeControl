using System.IO;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Textures → Composite SMPLitex Eye Disc: paints the eyeball
    /// UV island into a SMPLitex albedo, copied from a stock SMPL-X texture.
    ///
    /// SMPLitex generates everything except the eyes: its output leaves the
    /// eyeball island pure black, and since both eyes share that one island an
    /// agent wearing an untouched SMPLitex texture has two black eyes. The island
    /// is a small disc at a fixed place in the SMPL-X layout, so the fix is to
    /// copy that disc across from a stock albedo, which is what this does.
    ///
    /// Select one or more SMPLitex textures in the Project window and run the
    /// menu item. It rewrites the PNG in place and is idempotent — compositing an
    /// already-composited texture copies the same pixels again.
    ///
    /// Pixels are read from the PNG files rather than from the imported
    /// <see cref="Texture2D"/>s: the importer compresses to DXT1, and round-tripping
    /// a texture through block compression would degrade the whole image to fix
    /// one disc.
    /// </summary>
    public static class SmplitexEyeDisc
    {
        /// <summary>Donor albedo. The stock SMPL-X textures are the only ones with a painted eye island.</summary>
        const string DonorPath = "Assets/SMPLX/Textures/smplx_texture_f_alb.png";

        /// <summary>Reference resolution the geometry below is expressed in.</summary>
        const int ReferenceSize = 512;

        /// <summary>Island centre in the SMPL-X UV layout, top-left origin, at <see cref="ReferenceSize"/>.</summary>
        static readonly Vector2 Centre = new(296f, 221f);

        const float Radius = 19f;

        /// <summary>Width of the blend at the disc's edge, so it does not cut a hard circle.</summary>
        const float Feather = 3f;

        [MenuItem("GazeControl/Textures/Composite SMPLitex Eye Disc")]
        static void CompositeSelection()
        {
            var targets = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
            if (targets.Length == 0)
            {
                Debug.LogError("Composite SMPLitex Eye Disc: select one or more SMPLitex albedo textures in the Project window.");
                return;
            }

            if (!TryLoadPng(DonorPath, out var donor))
                return;

            foreach (var target in targets)
            {
                var path = AssetDatabase.GetAssetPath(target);
                if (path == DonorPath)
                {
                    Debug.LogWarning($"Composite SMPLitex Eye Disc: skipping the donor texture itself ({path}).");
                    continue;
                }

                Composite(path, donor);
            }

            Object.DestroyImmediate(donor);
            AssetDatabase.Refresh();
        }

        static void Composite(string path, Texture2D donor)
        {
            if (!TryLoadPng(path, out var target))
                return;

            var scale = target.width / (float)ReferenceSize;
            var centre = Centre * scale;
            var radius = Radius * scale;
            var feather = Feather * scale;

            var pixels = target.GetPixels();
            var painted = 0;

            var minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - radius));
            var maxX = Mathf.Min(target.width - 1, Mathf.CeilToInt(centre.x + radius));
            var minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - radius));
            var maxY = Mathf.Min(target.height - 1, Mathf.CeilToInt(centre.y + radius));

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                    if (distance > radius)
                        continue;

                    // 1 in the middle, falling to 0 at the rim.
                    var weight = Mathf.Clamp01((radius - distance) / Mathf.Max(feather, 0.001f));

                    // The donor is a different resolution (the stock textures are
                    // 2048-4096 px against SMPLitex's 512), so sample it in UV
                    // space rather than by pixel. This file's geometry is
                    // top-left origin; UV is bottom-left, hence the flip.
                    var u = (x + 0.5f) / target.width;
                    var v = 1f - (y + 0.5f) / target.height;
                    var source = donor.GetPixelBilinear(u, v);

                    var index = FlipRow(target, x, y);
                    pixels[index] = Color.Lerp(pixels[index], source, weight);
                    painted++;
                }
            }

            target.SetPixels(pixels);
            target.Apply();
            File.WriteAllBytes(ToAbsolute(path), target.EncodeToPNG());
            Object.DestroyImmediate(target);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Debug.Log($"Composite SMPLitex Eye Disc: painted {painted} px into {Path.GetFileName(path)}.");
        }

        /// <summary>
        /// Index of a top-left-origin (x, y) in Unity's bottom-left-origin pixel array.
        /// </summary>
        static int FlipRow(Texture2D texture, int x, int y) => (texture.height - 1 - y) * texture.width + x;

        /// <summary>
        /// Decode a PNG straight from disk, bypassing the importer's compression.
        /// The caller owns the returned texture.
        /// </summary>
        static bool TryLoadPng(string path, out Texture2D texture)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            var absolute = ToAbsolute(path);

            if (File.Exists(absolute) && texture.LoadImage(File.ReadAllBytes(absolute)))
                return true;

            Debug.LogError($"Composite SMPLitex Eye Disc: could not read '{path}' as a PNG.");
            Object.DestroyImmediate(texture);
            texture = null;
            return false;
        }

        static string ToAbsolute(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", assetPath);
    }
}
