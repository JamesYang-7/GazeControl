using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine.Samples.TTS.Inference;
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
        const int k_SampleRate = 24000;

        // Long inputs both exceed Kokoro's 510-token voice-style table and blow the
        // GPU compute dispatch limit (65535 thread groups → console error on ~470-token
        // utterances). Text is chunked at sentence boundaries and the waveforms are
        // concatenated instead.
        const int k_MaxChunkTokens = 200;

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

                using var voice = KokoroHandler.GetVoice(voiceName);
                var chunks = new List<float[]>();
                var totalSamples = 0;
                foreach (var chunkTokens in ChunkTokens(text))
                {
                    using var waveform = await s_Handler.Execute(chunkTokens, speed, voice);
                    var samples = waveform.DownloadToArray();
                    chunks.Add(samples);
                    totalSamples += samples.Length;
                }

                if (totalSamples == 0)
                    return null;

                var combined = new float[totalSamples];
                var offset = 0;
                foreach (var samples in chunks)
                {
                    System.Array.Copy(samples, 0, combined, offset, samples.Length);
                    offset += samples.Length;
                }

                var clip = AudioClip.Create($"tts_{voiceName}", totalSamples, 1, k_SampleRate, false);
                clip.SetData(combined, 0);
                return clip;
            }
            finally
            {
                s_Gate.Release();
            }
        }

        /// <summary>
        /// Tokenize text in sentence groups of at most <see cref="k_MaxChunkTokens"/> tokens.
        /// Sentences are tokenized individually and their token arrays concatenated — Kokoro
        /// treats sentence boundaries as prosodic breaks anyway, so no cross-sentence
        /// context is lost.
        /// </summary>
        static IEnumerable<int[]> ChunkTokens(string text)
        {
            var sentences = Regex.Split(text ?? string.Empty, @"(?<=[.!?…])\s+");
            var current = new List<int>();
            foreach (var sentence in sentences)
            {
                if (string.IsNullOrWhiteSpace(sentence))
                    continue;
                var tokens = MisakiSharp.TokenizeGraphemes(sentence);
                if (tokens.Length == 0)
                    continue;
                if (tokens.Length > k_MaxChunkTokens)
                    Debug.LogWarning($"KokoroTts: single sentence of {tokens.Length} tokens exceeds the {k_MaxChunkTokens}-token chunk cap; generating it as-is may glitch. Split it with punctuation.");

                if (current.Count > 0 && current.Count + tokens.Length + 1 > k_MaxChunkTokens)
                {
                    yield return current.ToArray();
                    current.Clear();
                }

                if (current.Count > 0)
                    current.Add(16); // the phoneme-vocab space token, as whole-text tokenization would emit between sentences
                current.AddRange(tokens);
            }

            if (current.Count > 0)
                yield return current.ToArray();
        }

        static void DisposeHandler()
        {
            Application.quitting -= DisposeHandler;
            s_Handler?.Dispose();
            s_Handler = null;
        }
    }
}
