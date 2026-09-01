using System;
using System.IO;

namespace GazeControl.Study
{
    /// <summary>
    /// The commit the project is sitting on, read straight out of <c>.git</c>.
    ///
    /// <para>Recorded in a session's <c>session.json</c> so that a participant's
    /// data can be tied to the code that produced it. Reading the files rather
    /// than shelling out to <c>git</c>: this runs at the start of a participant
    /// session, and launching a process there would be a stall with a stranger in
    /// the headset — and on a machine with no git on PATH, an exception.</para>
    ///
    /// <para>Best-effort by design. Every failure returns null and the session
    /// runs anyway; provenance that is merely absent is far better than a session
    /// that refuses to start over it.</para>
    /// </summary>
    public static class GitHead
    {
        /// <summary>
        /// The commit HEAD resolves to under <paramref name="projectRoot"/>, or
        /// null when it cannot be read.
        /// </summary>
        public static string Read(string projectRoot)
        {
            try
            {
                var gitDirectory = ResolveGitDirectory(projectRoot);
                if (gitDirectory == null)
                    return null;

                var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();

                // Detached: HEAD is the commit itself.
                if (!head.StartsWith("ref:", StringComparison.Ordinal))
                    return IsSha(head) ? head : null;

                var reference = head[4..].Trim();
                var loose = Path.Combine(gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(loose))
                {
                    var sha = File.ReadAllText(loose).Trim();
                    return IsSha(sha) ? sha : null;
                }

                // No loose ref: the branch has been packed, which is the normal
                // state of a freshly cloned repository.
                return FromPackedRefs(gitDirectory, reference);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// The real git directory: <c>.git</c> itself, or wherever the one-line
        /// <c>gitdir:</c> file points when the project is a worktree or submodule.
        /// </summary>
        static string ResolveGitDirectory(string projectRoot)
        {
            var candidate = Path.Combine(projectRoot, ".git");

            if (Directory.Exists(candidate))
                return candidate;

            if (!File.Exists(candidate))
                return null;

            var line = File.ReadAllText(candidate).Trim();
            if (!line.StartsWith("gitdir:", StringComparison.Ordinal))
                return null;

            var path = line[7..].Trim();
            return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(projectRoot, path));
        }

        static string FromPackedRefs(string gitDirectory, string reference)
        {
            var packed = Path.Combine(gitDirectory, "packed-refs");
            if (!File.Exists(packed))
                return null;

            foreach (var line in File.ReadAllLines(packed))
            {
                if (line.Length == 0 || line[0] == '#' || line[0] == '^')
                    continue;

                var space = line.IndexOf(' ');
                if (space > 0 && line[(space + 1)..].Trim() == reference)
                {
                    var sha = line[..space];
                    return IsSha(sha) ? sha : null;
                }
            }

            return null;
        }

        static bool IsSha(string value)
        {
            if (value == null || value.Length != 40)
                return false;

            foreach (var c in value)
            {
                if (!Uri.IsHexDigit(c))
                    return false;
            }

            return true;
        }
    }
}
