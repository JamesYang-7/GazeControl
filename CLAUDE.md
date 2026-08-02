# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A gaze control demo for virtual agents in a triad conversation: two virtual agents (A and B) plus the user. Gaze behavior patterns collected from real triad conversations (gaze shifts preceding turn-taking) drive the agents' gaze control. Motion control of the agents is also data-driven.

Target demo scenes:
1. **Agent-to-agent turn-taking** — turn-taking happens between agent A and agent B while the user just watches.
2. **Turn-yielding to the user** — agent A wants the user to take the turn and signals this with gaze.

## Environment

- **Unity 6000.5.3f1** (Unity 6) with the **Universal Render Pipeline (URP)**.
- Input handled via the **Input System** package (`Assets/InputSystem_Actions.inputactions`).
- **Timeline** and **Unity Test Framework** packages are installed.
- The project is currently a fresh URP template: `Assets/Scenes/SampleScene.unity`, URP settings under `Assets/Settings/`, and removable template files under `Assets/TutorialInfo/`.

## SMPL-X

The agents use the SMPL-X body model. Two code layers drive it:
- `Assets/GazeControl/Scripts/Runtime/Motion/` (ours) — .npz clip loading and playback (`SmplxMotionPlayer` writes joint local rotations in `Update`).
- `Assets/SMPLX/Scripts/SMPLX.cs` (official MPI package, imported verbatim — do not restyle) — pose correctives (486 blendshapes recomputed in `LateUpdate` from current joint rotations, composing cleanly with our player), betas/expressions, hand/body pose presets. Depends on `Assets/SMPLX/ThirdParty/` and the regressor JSONs in `Assets/SMPLX/Resources/`.

License: SMPL-X model license (research use; cite the SMPL-X paper in publications). The Meshcapade sample textures are CC BY-NC.

## Text-to-speech

Agents speak via Kokoro-82M TTS running locally on Unity Inference Engine. `Assets/TTS/` holds the inference core adapted from Unity's sentis-samples TextToSpeechSample (`MisakiSharp` G2P + `KokoroHandler`); `GazeControl.TTS.KokoroTts` is the shared service (one worker, serialized generations) and `TtsSpeaker` (on each agent, next to the AudioSource) generates and plays speech — lipsync needs no extra wiring. The model (`Assets/Models/Kokoro-82M-v1.0.onnx`, ~310 MB) and voice `.bin` files are **git-ignored**; the editor menu **GazeControl → Download Kokoro TTS Files** fetches them (see README.md). Model/voice loading uses `AssetDatabase`, so TTS is editor-only for now; builds would need them moved into Resources.

## Lip sync

Oculus LipSync (`Assets/Oculus/LipSync/`, third-party, verbatim) drives the mesh's 14 viseme blendshapes (indices 506–519, Oculus order minus `sil`) from each agent's `AudioSource`. Per agent: `AudioSource` (paired wav, loop) + `OVRLipSyncContext` (`audioLoopback` **on**, or the voice is muted) + `OVRLipSyncContextMorphTarget` (`laughterBlendTarget` must be **−1**; the default 15 would drive an expression blendshape). The scene needs one `LipSync` GameObject with the `OVRLipSync` component. Audio wavs pair 1:1 with the npz clips (same name) and are exactly the same 20 s length, so independent looping stays in sync.

The mesh carries a **mouth bag** (added in Blender) so the open mouth is no longer a hole. The bag is unmapped — all 161 of its vertices sit on UV (0, 0) — so it would otherwise take the colour of the albedo's bottom-left texel, which differs per agent texture. `SmplxMouthInteriorSubmesh` (an `AssetPostprocessor`, editor-only) therefore moves every triangle whose three vertices are all on UV (0, 0) onto submesh 1 at import and assigns `Assets/SMPLX/Materials/SMPLX-MouthInterior.mat`. Adjust that material to change how dark the cavity reads; no reimport needed. `GazeControl → Visemes → Open Jaw` drives the `aa` viseme in Edit Mode so you can see inside.

Caveat: with the Unity editor unfocused, the player loop and edit-mode skinning stall (`Time.time` freezes in Play Mode; `Camera.Render()` captures show stale meshes). Set `Application.runInBackground = true` **each Play Mode session** when driving the editor unfocused (the `PlayerSettings` flag does not govern editor play, and the runtime flag resets on exit). Skinning/blendshape changes made in the same editor command they're rendered in won't show — render in a later command.

## Gaze control

Gaze patterns come from the ICMI paper in `Assets/Docs/` (Figures 6–8: top gaze subsequences in the 1 s before turn events, 60 Hz, encoded over conversational roles — red = current speaker, blue = next speaker, green = listener; y-axis = gaze target). `GazeController` (per agent) drives the SMPL-X eye bones toward a target Transform (clamped, fast) with a fractional head turn layered in LateUpdate over the mocap pose; the blend is made non-accumulating so it works both while mocap plays and after motion freezes. `ListenerCamera` (on Main Camera) plays the listener's gaze track — the user is the listener in demo case 1. `TriadConversation` currently instantiates the Fig. 8d turn-taking prototype (`GazePatterns.TurnYieldingDoubleGlance`), transcribed verbatim from the paper's raw prototype data (`raw_prototypes/p3_ks40_4.npz` under `F:\aF\My_Papers\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking\`; figure↔file mapping via the tex labels, e.g. `fig:prottp_p3_ks40_4` → `p3_ks40_4.npz`; `manifest.csv` lists matched subsequences): two micro-glances (2 and 3 frames @60 fps) early in the last second, then sustained aversion to the boundary. `PatternTimeScale` on `TriadConversation` stretches it for visibility (1 = data-faithful).

## Motion data conventions

TalkingWithHands clips come in pairs from the same recorded take: one `interloctr` file and one `main-agent` file whose names differ only in that token (e.g. `trn_2023_v0_000_interloctr_000.npz` / `trn_2023_v0_000_main-agent_000.npz`). **The two agents must always play a matched pair**: one agent uses the `interloctr` clip and the other the `main-agent` clip of the same take. Only two example clips are committed (via LFS); other `.npz` files under `Assets/MotionData/` are git-ignored.

## Progress board

`PROGRESS.md` at the repo root is the project's working memory: nested checkbox task list, decisions log, and open questions. Keep it current via the `progress-board` skill — update it whenever a task starts/finishes, a decision is made, or before committing. `progress.html` is auto-generated from it by the pre-commit hook in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) — never hand-edit the HTML.

## Working in this repo

- This is a Unity project: every asset file has a paired `.meta` file. When adding, moving, or deleting assets outside the Unity Editor, keep `.meta` files consistent (let the Editor generate them where possible, and always commit them together with their asset).
- Scenes, prefabs, and most `.asset` files are Unity YAML (see `.gitattributes`); binary media (models, textures, audio) go through **Git LFS**.
- Tests use the Unity Test Framework and run inside the Unity Editor (Test Runner window, or via the JetBrains Unity test tooling when available).
- **`Research/` holds local-only material** — raw gaze corpora (`corpora/`), reference PDFs (`papers/`), working notes (`notes/`). Everything there is git-ignored except its `README.md`. It sits outside `Assets/` deliberately: Unity imports every file under `Assets/` regardless of `.gitignore`, generating `.meta` files and Library entries for data that is never a runtime asset. Put reference material there, not under `Assets/Docs/`.
