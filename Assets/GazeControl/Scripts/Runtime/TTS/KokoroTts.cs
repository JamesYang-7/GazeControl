using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine.Samples.TTS.Inference;
using Unity.InferenceEngine.Samples.TTS.Utils;
using UnityEngine;

namespace GazeControl.TTS
{
    /// <summary>
    /// Shared Kokoro text-to-speech service: text → phonemes (MisakiSharp) →
    /// Kokoro inference → AudioClip (24 kHz mono). One model/worker is shared by
    /// all speakers, and generations are serialized: a single Inference Engine
    /// worker cannot run two Schedule() calls concurrently.
    /// </summary>
    public static class KokoroTts
    {
        static KokoroHandler s_Handler;
        static readonly SemaphoreSlim s_Gate = new(1, 1);

        public static async Task<AudioClip> GenerateClipAsync(string text, string voiceName, float speed = 1f)
        {
            await s_Gate.WaitAsync();
            try
            {
                if (s_Handler == null)
                {
                    s_Handler = new KokoroHandler();
                    Application.quitting += DisposeHandler; // also fires on play mode exit
                }

                var tokens = MisakiSharp.TokenizeGraphemes(text);
                using var voice = KokoroHandler.GetVoice(voiceName);
                using var waveform = await s_Handler.Execute(tokens, speed, voice);
                return AudioClipUtils.ToAudioClip(waveform, name: $"tts_{voiceName}");
            }
            finally
            {
                s_Gate.Release();
            }
        }

        static void DisposeHandler()
        {
            Application.quitting -= DisposeHandler;
            s_Handler?.Dispose();
            s_Handler = null;
        }
    }
}
