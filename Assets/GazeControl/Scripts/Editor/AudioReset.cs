using UnityEditor;
using UnityEngine;

namespace GazeControl.Editor
{
    /// <summary>
    /// GazeControl → Reset Audio: re-initialise the editor's audio engine on
    /// Windows' current default output device.
    ///
    /// <para>Found 2026-09-08: after the Varjo was unplugged, every AudioSource
    /// reported playing, Windows showed Unity's session live on the monitor at
    /// full volume, and nothing was audible — the engine was still bound to the
    /// device that had gone ("FMOD failed to switch back to normal output").
    /// The same reset made the bake overnight possible when the DSP clock had
    /// frozen for the same reason. A restart of the editor also cures it; this
    /// is the version that does not lose the open scene.</para>
    /// </summary>
    public static class AudioReset
    {
        [MenuItem("GazeControl/Reset Audio")]
        public static void Reset()
        {
            var config = AudioSettings.GetConfiguration();
            if (AudioSettings.Reset(config))
                Debug.Log($"Reset Audio: audio engine re-initialised ({config.sampleRate} Hz, {config.speakerMode}). " +
                          "If a clip is playing, restart it.");
            else
                Debug.LogError("Reset Audio: the audio engine refused to reset. Check Windows' default output device, then restart the editor.");
        }
    }
}
