using UnityEngine;

namespace GazeControl.TTS
{
    /// <summary>
    /// Speaks text through this GameObject's AudioSource using Kokoro TTS.
    /// Lip sync needs no extra wiring: OVRLipSyncContext analyzes whatever the
    /// AudioSource plays.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class TtsSpeaker : MonoBehaviour
    {
        [field: SerializeField]
        [field: TextArea(3, 8)]
        [field: Tooltip("Text spoken on start (when Play On Start is set)")]
        public string Text { get; set; }

        [field: SerializeField]
        [field: Tooltip("Kokoro voice name, e.g. am_adam or am_michael; Assets/Models/Voices/<name>.bin must exist")]
        public string VoiceName { get; set; } = "am_adam";

        [field: SerializeField]
        [field: Range(0.5f, 2f)]
        [field: Tooltip("Speech rate multiplier")]
        public float Speed { get; set; } = 1f;

        [field: SerializeField]
        public bool PlayOnStart { get; set; } = true;

        AudioSource _audioSource;

        async Awaitable Start()
        {
            _audioSource = GetComponent<AudioSource>();
            if (PlayOnStart && !string.IsNullOrWhiteSpace(Text))
                await SpeakAsync(Text);
        }

        /// <summary>Generate speech for <paramref name="text"/> and play it (replaces whatever is playing).</summary>
        public async Awaitable SpeakAsync(string text)
        {
            var clip = await KokoroTts.GenerateClipAsync(text, VoiceName, Speed);
            if (this == null || !isActiveAndEnabled) return; // destroyed/disabled while generating

            _audioSource.Stop();
            _audioSource.clip = clip;
            _audioSource.Play();
        }
    }
}
