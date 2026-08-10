using System.IO;
using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Textures → Pad SMPLitex UV Islands: fills the gaps between
    /// the UV islands of a SMPLitex albedo with the colour of the nearest island.
    ///
    /// SMPLitex leaves the space between islands in whatever colour its generator
    /// produced — black in some outputs, bright yellow-green in others. Those
    /// texels are never sampled at the centre of a triangle, but they are very
    /// much sampled at its edges and by every mip level, so thin features come
    /// out stained: the first female texture rendered with green toes and green
    /// fingertips, because a toe is a couple of texels wide at 512 and its mips
    /// average in the background around it.
    ///
    /// The fix is standard UV padding, and the SMPL-X layout makes it exact: the
    /// mesh's own UVs say which texels a triangle covers, so the rest can be
    /// flood-filled outwards from the island edges.
    ///
    /// The same pass repairs the generator spilling its background *into* an
    /// island, which is what actually stained the toes: 1,174 texels of the first
    /// female texture were background-coloured inside the foot and hand islands,
    /// where no amount of padding would reach them. The background colour is not
    /// guessed — it is the most common colour among the texels the UV mask proves
    /// are outside every island, so it is measured per texture from where the
    /// background provably is, then removed from where it provably should not be.
    /// A texture whose background colour is not clearly dominant, or which would
    /// lose an implausible share of its islands, is left alone and reported.
    ///
    /// Select one or more textures in the Project window and run the menu item.
    /// Idempotent: padding an already-padded texture rewrites the same values.
    /// </summary>
    public static class SmplitexUvPadding
    {
        /// <summary>The mesh whose UV layout defines the islands.</summary>
        const string MeshAssetPath = "Assets/SMPLX/smplx-visemes-unity.fbx";

        /// <summary>
        /// How far to grow the islands, in texels. Four is enough for bilinear
        /// filtering; the rest is for the mip chain, where a 512 texture is still
        /// averaging 8x8 blocks four levels down.
        /// </summary>
        const int Iterations = 16;

        /// <summary>How close a texel must be to the measured background colour to count as spill.</summary>
        const float BackgroundTolerance = 0.1f;

        /// <summary>
        /// The measured background colour must hold at least this share of the
        /// texels outside every island, or it is not one flat background and the
        /// repair is skipped.
        /// </summary>
        const float MinimumBackgroundShare = 0.5f;

        /// <summary>
        /// Refuse to repair if this much of the islands matches the background —
        /// at that point the "background" colour is something the model wears.
        /// </summary>
        const float MaximumSpillShare = 0.05f;

        [MenuItem("GazeControl/Textures/Pad SMPLitex UV Islands")]
        static void PadSelection()
        {
            var targets = Selection.GetFiltered<Texture2D>(SelectionMode.Assets);
            if (targets.Length == 0)
            {
                Debug.LogError("Pad SMPLitex UV Islands: select one or more albedo textures in the Project window.");
                return;
            }

            // Mesh data is only reachable with Read/Write on, which doubles the
            // mesh's memory. Turn it on for the duration rather than leaving the
            // project carrying it for a tool that runs once per texture.
            var importer = (ModelImporter)AssetImporter.GetAtPath(MeshAssetPath);
            var wasReadable = importer != null && importer.isReadable;

            try
            {
                if (importer != null && !wasReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                }

                var mesh = LoadMesh();
                if (mesh == null)
                    return;

                foreach (var target in targets)
                    Pad(AssetDatabase.GetAssetPath(target), mesh);
            }
            finally
            {
                if (importer != null && !wasReadable)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }

            AssetDatabase.Refresh();
        }

        static Mesh LoadMesh()
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(MeshAssetPath))
            {
                if (asset is Mesh mesh && mesh.uv is { Length: > 0 })
                    return mesh;
            }

            Debug.LogError($"Pad SMPLitex UV Islands: no UV-mapped mesh found in '{MeshAssetPath}'.");
            return null;
        }

        static void Pad(string path, Mesh mesh)
        {
            var absolute = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", path);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);

            if (!File.Exists(absolute) || !texture.LoadImage(File.ReadAllBytes(absolute)))
            {
                Debug.LogError($"Pad SMPLitex UV Islands: could not read '{path}' as a PNG.");
                Object.DestroyImmediate(texture);
                return;
            }

            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels();
            var covered = RasterizeUvCoverage(mesh, width, height);

            var islandTexels = 0;
            foreach (var isCovered in covered)
            {
                if (isCovered)
                    islandTexels++;
            }

            var spilled = Unmask(pixels, covered, islandTexels, Path.GetFileName(path));
            var filled = Grow(pixels, covered, width, height);

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(absolute, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"Pad SMPLitex UV Islands: {Path.GetFileName(path)} — {islandTexels} island texels, " +
                $"{spilled} repaired inside islands, {filled} padded over {Iterations} rings.");
        }

        /// <summary>
        /// Drop island texels that carry the generator's background colour out of
        /// the mask, so the padding pass repaints them from real neighbours.
        /// Returns how many were dropped.
        /// </summary>
        static int Unmask(Color[] pixels, bool[] covered, int islandTexels, string name)
        {
            if (!TryMeasureBackground(pixels, covered, out var background, out var share))
            {
                Debug.Log(
                    $"Pad SMPLitex UV Islands: {name} — no single dominant background colour " +
                    $"(best {share:P0}); padding only, nothing repaired.");
                return 0;
            }

            var spill = new System.Collections.Generic.List<int>();
            for (var i = 0; i < pixels.Length; i++)
            {
                if (covered[i] && Matches(pixels[i], background))
                    spill.Add(i);
            }

            if (islandTexels > 0 && spill.Count > islandTexels * MaximumSpillShare)
            {
                Debug.LogWarning(
                    $"Pad SMPLitex UV Islands: {name} — {spill.Count} island texels match the background " +
                    $"colour {background}, which is {(float)spill.Count / islandTexels:P0} of the model. " +
                    "That reads as a colour the model wears rather than spill; nothing repaired.");
                return 0;
            }

            foreach (var index in spill)
                covered[index] = false;

            return spill.Count;
        }

        /// <summary>
        /// The most common colour among texels outside every island, which is the
        /// generator's background wherever it is flat. Colours are bucketed to
        /// 5 bits per channel so that compression noise does not split the mode.
        /// </summary>
        static bool TryMeasureBackground(Color[] pixels, bool[] covered, out Color background, out float share)
        {
            var counts = new System.Collections.Generic.Dictionary<int, int>();
            var outside = 0;

            for (var i = 0; i < pixels.Length; i++)
            {
                if (covered[i])
                    continue;

                outside++;
                var key = Bucket(pixels[i]);
                counts.TryGetValue(key, out var count);
                counts[key] = count + 1;
            }

            var bestKey = 0;
            var bestCount = 0;
            foreach (var pair in counts)
            {
                if (pair.Value <= bestCount)
                    continue;

                bestKey = pair.Key;
                bestCount = pair.Value;
            }

            share = outside > 0 ? (float)bestCount / outside : 0f;
            background = new Color(
                ((bestKey >> 10) & 31) / 31f,
                ((bestKey >> 5) & 31) / 31f,
                (bestKey & 31) / 31f);

            return outside > 0 && share >= MinimumBackgroundShare;
        }

        static int Bucket(Color c) =>
            (Mathf.RoundToInt(Mathf.Clamp01(c.r) * 31) << 10)
            | (Mathf.RoundToInt(Mathf.Clamp01(c.g) * 31) << 5)
            | Mathf.RoundToInt(Mathf.Clamp01(c.b) * 31);

        static bool Matches(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < BackgroundTolerance
            && Mathf.Abs(a.g - b.g) < BackgroundTolerance
            && Mathf.Abs(a.b - b.b) < BackgroundTolerance;

        /// <summary>
        /// Which texels the mesh's UV triangles cover, by texel centre. Under-
        /// covering a thin triangle is the safe direction to be wrong: such a
        /// texel gets filled from its neighbours, which are the island it belongs
        /// to, rather than left showing the background.
        /// </summary>
        static bool[] RasterizeUvCoverage(Mesh mesh, int width, int height)
        {
            var covered = new bool[width * height];
            var uv = mesh.uv;
            var triangles = mesh.triangles;

            for (var t = 0; t < triangles.Length; t += 3)
            {
                var a = ToPixel(uv[triangles[t]], width, height);
                var b = ToPixel(uv[triangles[t + 1]], width, height);
                var c = ToPixel(uv[triangles[t + 2]], width, height);

                var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)));
                var maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)));
                var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)));
                var maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)));

                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        if (Contains(a, b, c, new Vector2(x + 0.5f, y + 0.5f)))
                            covered[y * width + x] = true;
                    }
                }
            }

            return covered;
        }

        static Vector2 ToPixel(Vector2 uv, int width, int height) => new(uv.x * width, uv.y * height);

        static bool Contains(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            var d1 = Cross(p - a, b - a);
            var d2 = Cross(p - b, c - b);
            var d3 = Cross(p - c, a - c);

            var negative = d1 < 0f || d2 < 0f || d3 < 0f;
            var positive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(negative && positive);
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        /// <summary>
        /// Grow the covered region outwards one ring at a time, each new texel
        /// taking the average of the covered neighbours it touched. Returns how
        /// many texels were written.
        /// </summary>
        static int Grow(Color[] pixels, bool[] covered, int width, int height)
        {
            var filled = 0;
            var frontier = new System.Collections.Generic.List<int>();

            for (var ring = 0; ring < Iterations; ring++)
            {
                frontier.Clear();

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var index = y * width + x;
                        if (covered[index])
                            continue;

                        var sum = Color.clear;
                        var count = 0;
                        Accumulate(pixels, covered, width, height, x - 1, y, ref sum, ref count);
                        Accumulate(pixels, covered, width, height, x + 1, y, ref sum, ref count);
                        Accumulate(pixels, covered, width, height, x, y - 1, ref sum, ref count);
                        Accumulate(pixels, covered, width, height, x, y + 1, ref sum, ref count);

                        if (count == 0)
                            continue;

                        pixels[index] = sum / count;
                        frontier.Add(index);
                    }
                }

                if (frontier.Count == 0)
                    break;

                // Marked after the sweep, so a ring is filled from the previous
                // ring only — filling from texels written moments earlier would
                // smear one island's colour along the gap towards another.
                foreach (var index in frontier)
                    covered[index] = true;

                filled += frontier.Count;
            }

            return filled;
        }

        static void Accumulate(
            Color[] pixels, bool[] covered, int width, int height, int x, int y, ref Color sum, ref int count)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;

            var index = y * width + x;
            if (!covered[index])
                return;

            sum += pixels[index];
            count++;
        }
    }
}
