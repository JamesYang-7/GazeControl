using UnityEngine.XR.Management;

namespace GazeControl.Xr
{
    /// <summary>
    /// Which XR loader this project is configured to bring up, and whether it is
    /// running — asked from Edit Mode as well as from a session.
    ///
    /// <para>It lives here rather than in the editor window because this assembly
    /// is the one that references XR Management. The window asks it before a
    /// participant arrives: a project with no loader ticked starts, renders to
    /// the desktop and records fifteen takes of nothing, and the only sign is a
    /// console line nobody reads while running a session.</para>
    /// </summary>
    public static class XrLoaderStatus
    {
        /// <summary>
        /// Reported when the settings object cannot be reached at all — some
        /// editor contexts have no resolved XR settings, and a run must not be
        /// refused over an answer we do not have. Non-empty on purpose, so the
        /// study rules read it as "nothing to object to".
        /// </summary>
        public const string Unknown = "(not readable here)";

        /// <summary>
        /// The loader that will come up: the running one if XR has started,
        /// otherwise the first one configured for this build target. Null only
        /// when the settings exist and name no loader at all.
        /// </summary>
        public static string LoaderName
        {
            get
            {
                var settings = XRGeneralSettings.Instance;
                if (settings == null || settings.Manager == null)
                    return Unknown;

                if (settings.Manager.activeLoader != null)
                    return settings.Manager.activeLoader.name;

                var loaders = settings.Manager.activeLoaders;
                return loaders != null && loaders.Count > 0 ? loaders[0].name : null;
            }
        }

        /// <summary>Whether the subsystems are up — false in Edit Mode and in a flat play.</summary>
        public static bool IsRunning
        {
            get
            {
                var settings = XRGeneralSettings.Instance;
                return settings != null && settings.Manager != null && settings.Manager.isInitializationComplete;
            }
        }
    }
}
