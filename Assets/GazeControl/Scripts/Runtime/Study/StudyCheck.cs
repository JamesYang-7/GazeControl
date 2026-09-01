namespace GazeControl.Study
{
    /// <summary>
    /// One rule a participant run has to satisfy, and whether it does.
    ///
    /// <para>Carried as data rather than reported as a thrown message so that the
    /// same rule set can be read two ways: the runner takes the first failure and
    /// refuses to start, while the operator's window draws every row at once,
    /// before the headset goes on.</para>
    /// </summary>
    public readonly struct StudyCheck
    {
        public StudyCheck(string name, bool ok, string problem = null, bool setAtLaunch = false)
        {
            Name = name;
            Ok = ok;
            Problem = problem;
            SetAtLaunch = setAtLaunch;
        }

        /// <summary>Short label for the operator's checklist.</summary>
        public string Name { get; }

        /// <summary>Whether the rule holds.</summary>
        public bool Ok { get; }

        /// <summary>Why it does not, as a sentence the console log can print; null when it does.</summary>
        public string Problem { get; }

        /// <summary>
        /// Whether starting a session from the operator's window puts this right
        /// on its own.
        ///
        /// <para>These are the fields that used to be the operator's job — the
        /// track mode, logging, the headset switch, the developer overlay. A
        /// launch sets them at play start, so the window shows them as
        /// forthcoming rather than blocking. The runner ignores the flag: by the
        /// time its own guard reads the rules, nothing is going to fix
        /// anything.</para>
        /// </summary>
        public bool SetAtLaunch { get; }

        public override string ToString() => $"{(Ok ? "ok" : "!!")}  {Name}";
    }
}
