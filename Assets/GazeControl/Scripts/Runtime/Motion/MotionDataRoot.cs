using System;
using System.IO;
using UnityEngine;

namespace GazeControl.Motion
{
    /// <summary>
    /// Where the motion corpora live on <i>this</i> machine.
    ///
    /// <para>A segment manifest records the absolute path its motion was read
    /// from — <c>F:\Data\3People-2022-SMPLX\&lt;date&gt;\Session_…npz</c> — because
    /// the exporter reads the corpus in place and the clips are far too large to
    /// commit. That path is the one thing in an exported segment that cannot
    /// survive being moved to another PC, so it is rebased here rather than
    /// trusted: everything up to and including the corpus folder is replaced by
    /// the configured root, and the <c>&lt;date&gt;/&lt;file&gt;.npz</c> tail is kept.</para>
    ///
    /// <para>The root comes from <c>data-roots.json</c> at the project root, and
    /// defaults to <see cref="DefaultRoot"/> — the project itself, so a copy of
    /// the corpus folder dropped beside <c>Assets/</c> is found with no
    /// configuration at all. The file is machine-local and git-ignored;
    /// <c>data-roots.example.json</c> is the committed template.</para>
    ///
    /// <para><b>Only the 3People corpus is rebased</b>, by name. Study 1's
    /// segments point into <c>TalkingWithHandsCentered</c>, which is hundreds of
    /// gigabytes and is never copied into a project, so rebasing every recorded
    /// path would silently redirect those to a root that cannot hold them.
    /// A path naming no known corpus is handed back untouched.</para>
    /// </summary>
    public static class MotionDataRoot
    {
        /// <summary>Machine-local configuration, read from the project root.</summary>
        public const string ConfigFileName = "data-roots.json";

        /// <summary>The project root: where a corpus folder copied into the project sits.</summary>
        public const string DefaultRoot = "./";

        /// <summary>The converted 3People-2022 corpus, as its folder is named under the root.</summary>
        public const string ThreePeopleCorpusFolder = "3People-2022-SMPLX";

        // Read once and kept: Resolve runs per agent per clip, and the log line
        // below is worth exactly one appearance. A domain reload re-reads it, so
        // an edited config takes effect on the next script compile at the
        // latest — and on entering Play Mode, unless domain reload is off.
        static string s_root;

        /// <summary>The configured root, absolute, holding the corpus folders.</summary>
        public static string Root => s_root ??= LoadRoot();

        /// <summary>The converted 3People-2022 corpus on this machine.</summary>
        public static string ThreePeopleSmplx => Combine(Root, ThreePeopleCorpusFolder);

        /// <summary>
        /// The path a motion clip is actually at on this machine, given the path
        /// a segment manifest recorded. Prefers the configured root and falls
        /// back to the recorded path, so a machine that still has the corpus
        /// where it was exported from keeps working.
        /// </summary>
        public static string Resolve(string recordedPath) => Resolve(Root, recordedPath);

        /// <inheritdoc cref="Resolve(string)"/>
        /// <remarks>Against an explicit root, which is how it is tested.</remarks>
        public static string Resolve(string root, string recordedPath)
        {
            var rebased = Rebase(root, recordedPath);
            if (rebased == null)
                return recordedPath;

            if (File.Exists(rebased))
                return rebased;

            if (File.Exists(recordedPath))
                return recordedPath;

            // Neither exists: name both, because which one the reader expected
            // to be there is exactly what has gone wrong.
            Debug.LogWarning(
                $"motion clip not found under the configured data root ('{rebased}') " +
                $"nor where the segment recorded it ('{recordedPath}'). " +
                $"Copy {ThreePeopleCorpusFolder} into '{root}', or point '{ConfigFileName}' at it.");

            return rebased;
        }

        /// <summary>
        /// Replace the part of <paramref name="recordedPath"/> above a known
        /// corpus folder with <paramref name="root"/>. Pure. Null when the path
        /// names no corpus this rebases, which is the caller's cue to leave it
        /// alone rather than a failure.
        /// </summary>
        public static string Rebase(string root, string recordedPath)
        {
            if (string.IsNullOrEmpty(root))
                return null;

            var tail = TailFrom(recordedPath, ThreePeopleCorpusFolder);
            return tail == null ? null : Combine(root, tail);
        }

        /// <summary>
        /// The root a config document asks for, or <see cref="DefaultRoot"/> when
        /// it names none. Pure.
        /// </summary>
        /// <exception cref="ArgumentException">The document is not JSON.</exception>
        public static string RootFrom(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return DefaultRoot;

            var document = JsonUtility.FromJson<Document>(json);
            return string.IsNullOrWhiteSpace(document?.dataRoot) ? DefaultRoot : document.dataRoot.Trim();
        }

        /// <summary>
        /// The corpus-relative tail of a path, from the named folder on, or null
        /// when the folder is not one of its directories. Matched as a whole
        /// segment and case-insensitively, and the folder must have something
        /// under it: the corpus folder itself is never a clip.
        /// </summary>
        static string TailFrom(string path, string folder)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var normalised = path.Replace('\\', '/');
            var needle = folder + "/";
            var index = normalised.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (index < 0 || (index > 0 && normalised[index - 1] != '/'))
                return null;

            return normalised.Substring(index);
        }

        static string LoadRoot()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var configPath = Path.Combine(projectRoot, ConfigFileName);
            var configured = DefaultRoot;
            var source = "default";

            if (File.Exists(configPath))
            {
                try
                {
                    configured = RootFrom(File.ReadAllText(configPath));
                    source = ConfigFileName;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{configPath} is not readable as a data-root config ({e.Message}); using '{DefaultRoot}'.");
                }
            }

            var root = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(projectRoot, configured);

            root = Path.GetFullPath(root).Replace('\\', '/');
            Debug.Log($"motion data root: {root} (from {source})");
            return root;
        }

        static string Combine(string root, string tail) =>
            Path.Combine(root, tail).Replace('\\', '/');

        /// <summary>The config file's shape; one root, holding the corpus folders.</summary>
        [Serializable]
        sealed class Document
        {
            public string dataRoot;
        }
    }
}
