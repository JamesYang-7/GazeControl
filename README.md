# GazeControl

A gaze control demo for virtual agents in a triad conversation: two virtual agents plus the user, standing on a regular triangle. Gaze behavior patterns collected before turn-taking in real triad conversations drive the agents' gaze; body motion is data-driven (TalkingWithHands SMPL-X mocap) and speech is generated locally with text-to-speech.

Progress, decisions, and open questions live in [PROGRESS.md](PROGRESS.md) (rendered dashboard: `progress.html`).

## Requirements

- Unity **6000.5.3f1** (URP)
- **git-lfs** (binary assets: models, textures, motion clips)

## Setup after cloning

1. `git lfs install` (once per machine) — LFS assets download on checkout.
2. `git config core.hooksPath .githooks` — enables the progress-board pre-commit hook.
3. Download the git-ignored TTS files (see below), then open `Assets/Scenes/TriadScene.unity` and press Play.

## Text-to-speech model (not in git)

Speech is synthesized locally by [Kokoro-82M](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX) running on Unity Inference Engine. The model (~310 MB) and voice files are **not committed** — place them under the git-ignored `Assets/Models/`:

| File | Source |
| --- | --- |
| `Assets/Models/Kokoro-82M-v1.0.onnx` | [`onnx/model.onnx`](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/blob/main/onnx/model.onnx) |
| `Assets/Models/Voices/am_adam.bin` | [`voices/am_adam.bin`](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/blob/main/voices/am_adam.bin) |
| `Assets/Models/Voices/am_michael.bin` | [`voices/am_michael.bin`](https://huggingface.co/onnx-community/Kokoro-82M-v1.0-ONNX/blob/main/voices/am_michael.bin) |

Or simply run the editor menu **GazeControl → Download Kokoro TTS Files**, which fetches whatever is missing.

## Motion data (mostly not in git)

`Assets/MotionData/` holds TalkingWithHands SMPL-X clips (`.npz`, 55-joint axis-angle @ 60 fps) with paired conversation audio (`.wav`). Only one example take is committed (via LFS); other data dropped into the folder stays local (git-ignored). The two agents always play a matched `interloctr`/`main-agent` pair from the same take.

## Third-party components

- **SMPL-X** body model + official Unity package (`Assets/SMPLX/`) — [SMPL-X model license](https://smpl-x.is.tue.mpg.de/modellicense), research use; cite the SMPL-X paper in publications. Sample textures by Meshcapade (CC BY-NC 4.0).
- **Oculus LipSync** (`Assets/Oculus/LipSync/`) — Oculus Audio SDK License; drives the mesh's viseme blendshapes from played audio.
- **Kokoro TTS integration** (`Assets/TTS/`) — adapted from Unity's [sentis-samples](https://github.com/Unity-Technologies/sentis-samples) TextToSpeechSample (MisakiSharp G2P, Kokoro inference, notch filtering). Kokoro-82M model: Apache-2.0.
- **TalkingWithHands** motion/audio data — see the dataset's own license/terms.
