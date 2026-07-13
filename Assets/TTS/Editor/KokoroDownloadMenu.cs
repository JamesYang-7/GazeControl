using System.IO;
using System.Threading.Tasks;
using Unity.InferenceEngine.Samples.TTS.Inference;
using Unity.InferenceEngine.Samples.TTS.Utils;
using UnityEditor;
using UnityEngine;

namespace GazeControl.TTS.Editor
{
    /// <summary>
    /// Downloads the git-ignored Kokoro TTS files (model + the voices this project
    /// uses) from Hugging Face into Assets/Models/. Run once per fresh clone.
    /// </summary>
    public static class KokoroDownloadMenu
    {
        const string k_RepoId = "onnx-community/Kokoro-82M-v1.0-ONNX";
        static readonly string[] k_Voices = { "am_adam", "am_michael" };

        [MenuItem("GazeControl/Download Kokoro TTS Files")]
        public static async void DownloadMissingFiles()
        {
            var downloader = new HfDownloader(Application.dataPath, k_RepoId);

            if (!File.Exists(KokoroHandler.ModelAssetPath))
            {
                Debug.Log("[Kokoro] Downloading model (~310 MB)...");
                // repo path is onnx/model.onnx; store under our local name
                await downloader.Download("onnx/model.onnx");
                Directory.CreateDirectory(Path.GetDirectoryName(KokoroHandler.ModelAssetPath)!);
                MoveOverwriting(Path.Join(Application.dataPath, "onnx/model.onnx"), KokoroHandler.ModelAssetPath);
                Directory.Delete(Path.Join(Application.dataPath, "onnx"));
            }

            foreach (var voice in k_Voices)
            {
                var localPath = $"{KokoroHandler.VoicesFolder}/{voice}.bin";
                if (File.Exists(localPath))
                    continue;
                Debug.Log($"[Kokoro] Downloading voice {voice}...");
                await downloader.Download($"voices/{voice}.bin");
                Directory.CreateDirectory(KokoroHandler.VoicesFolder);
                MoveOverwriting(Path.Join(Application.dataPath, $"voices/{voice}.bin"), localPath);
            }
            var voicesTmp = Path.Join(Application.dataPath, "voices");
            if (Directory.Exists(voicesTmp))
                Directory.Delete(voicesTmp);

            AssetDatabase.Refresh();
            Debug.Log("[Kokoro] All TTS files present.");
        }

        // netstandard2.1 File.Move has no overwrite overload
        static void MoveOverwriting(string source, string destination)
        {
            if (File.Exists(destination))
                File.Delete(destination);
            File.Move(source, destination);
        }
    }
}
