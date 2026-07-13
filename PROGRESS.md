# GazeControl — Progress Board

Working memory for GazeControl (data-driven gaze control for virtual agents in a
triad conversation: two virtual agents + the user; gaze patterns collected before
turn-taking in real triad conversations drive the agents' gaze).

**Source of truth: this file.** `progress.html` is auto-generated from it on every
commit by the `progress-board` skill's pre-commit hook — never hand-edit the HTML.

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done · `[!]` blocked.
Completed tasks carry a `_(done YYYY-MM-DD · <short-commit>)_` tag (the commit that
finished them, for history tracking); the dashboard keeps full detail for the most
recent few completed items and compacts older ones.

## Tasks

- [x] **Project initialization** — Unity 6 URP project, git, working docs _(done 2026-07-13 · 7c46fe9)_
  - [x] **Initial commit** — Unity 6000.5.3f1 URP template, `.gitignore`/`.gitattributes` (Unity templates, Git LFS), `CLAUDE.md` _(done 2026-07-13 · 49c8ca2)_
  - [x] **Progress board setup** — `PROGRESS.md` + `progress-board` skill + pre-commit dashboard sync, adapted from the VR-Chat project _(done 2026-07-13 · 7c46fe9)_
- [x] **Triad scene setup** — TriadScene.unity: 3 participants on a regular triangle, side 1 m _(done 2026-07-13 · 8601d41)_
  - [x] **SMPL-X agent model** — `Assets/SMPLX/smplx-visemes-unity.fbx` imported (Generic rig, 56 bones, 520 blendshapes incl. visemes, ~1.8 m tall) _(done 2026-07-13 · 8601d41)_
  - [x] **Scene layout** — AgentA/AgentB (SMPL-X instances) + User (Main Camera at 1.6 m eye height) at triangle vertices centered on origin, all facing the centroid; ground plane + URP Global Volume _(done 2026-07-13 · 8601d41)_
  - [x] **`Assets/MotionData/` folder** — drop-in location for motion files _(done 2026-07-13 · 8601d41)_
- [ ] **Gaze data pipeline** — import the collected pre-turn-taking gaze behaviour patterns into Unity
  - What format is the collected gaze data in (CSV/JSON? gaze targets vs angles? timing relative to turn end)?
- [~] **Virtual agents** — two conversational agents with data-driven motion control
  - [x] **SMPL-X .npz motion playback** — `NpyReader`/`SmplxMotionClip`/`SmplxAnimUtils`/`SmplxMotionPlayer` under `Assets/GazeControl/Scripts/Runtime/Motion/`; direct axis-angle → Unity quaternion (no 6D detour); AgentA plays a TalkingWithHands clip _(done 2026-07-13 · 050720b)_
    - Motion verified by user in Play Mode. Both agents now play paired clips (A: `interloctr_000`, B: `main-agent_000`).
  - [x] **Lip sync from conversation audio** — Oculus LipSync drives the 14 viseme blendshapes from each agent's paired 20 s wav (loops in sync with motion); verified visemes firing in Play Mode during speech _(done 2026-07-13 · 9adfa09)_
  - [x] **Kokoro text-to-speech replaces wav playback** — `Assets/TTS/` (inference core adapted from Unity sentis-samples) + `KokoroTts` service + `TtsSpeaker` on both agents (A: am_adam, B: am_michael); model+voices git-ignored under `Assets/Models/` with an editor download menu; verified end-to-end in Play Mode (26 s utterance playing with visemes firing) _(done 2026-07-13 · 4420518)_
    - G2P occasionally misses dictionary words (e.g. "finishes", "Listeners") and skips them — minor speech quality issue to revisit if noticeable.
    - Long texts are chunked at sentence boundaries (≤200 tokens per Kokoro run) and the waveforms concatenated: single long inputs exceeded both the GPU compute dispatch limit (65535 thread groups) and Kokoro's 510-token voice-style table.
  - [x] **Legacy-input console errors fixed** — Oculus LipSync debug/test input (viseme+laughter hotkeys, touchpad helper) polls legacy `UnityEngine.Input`, which throws under Input System-only handling; guarded those paths with `#if ENABLE_LEGACY_INPUT_MANAGER`; Play Mode now error-free _(done 2026-07-13 · 71cef4c)_
  - [x] **Mouth cavity fix** — SMPL-X has no mouth interior (skybox showed through the open mouth as white); added a dark unlit `MouthCavity` blocker sphere under each `head` bone; verified no leak at max jaw opening (`aa`=100) _(done 2026-07-13 · 337940a)_
  - [x] **Official SMPL-X package integration** — imported MPI `SMPLX.cs` (+SimpleJSON, Matrix, regressor JSONs) for pose correctives / betas / expressions; attached to both agents (Male, correctives High); Meshcapade male texture on a URP/Lit material; verified in Play Mode _(done 2026-07-13 · a870451)_
- [~] **Gaze control system** — drive agents' eye/head gaze from the collected patterns (aversion, partner-directed gaze, pre-turn shifts)
  - [x] **First pattern: Fig. 8d double glance** — `GazeController` (eyes + fractional head over mocap) + `ListenerCamera` (user view = listener track); in the last 1 s of A's turn, A glances at B twice, listener stays on A then moves to B at the turn; verified eye-to-target error 0.0° with 23° eye deflection _(done 2026-07-13)_
  - Glance timings are eyeballed from the figures (0.25 s + 0.3 s glances) — ask for the original pattern data to calibrate, and to add more prototypes (6d single glance, 8e gaze-then-avert).
- [~] **Scene 1: agent↔agent turn-taking** — turn-taking between agent A and B; the user only watches
  - [x] **Scripted Q&A first demo** — `TriadConversation` controller: pre-generates both TTS clips, A asks, B answers after a 0.4 s gap, both motion players freeze when B finishes; verified sequencing in Play Mode _(done 2026-07-13 · da68512)_
  - Gaze behaviour before the turn hand-over (A → B) is the next layer, from the collected patterns.
- [ ] **Scene 2: turn-yielding to user** — agent A signals the user to take the turn via gaze
  - How is "user takes the turn" detected (speech/voice activity, key press, something else)?

## Decisions

- 2026-07-13 — Gaze control and agent motion are both **data-driven**, from gaze behaviour patterns collected before turn-taking in real triad conversations.
- 2026-07-13 — Two target scenes: (1) A↔B turn-taking with the user watching; (2) agent A yields the turn to the user with gaze implication.
- 2026-07-13 — Kept the stock GitHub Unity `.gitignore`/`.gitattributes` templates already in the repo; binary media goes through Git LFS (git-lfs 3.4.0 installed).
- 2026-07-13 — Adopted the VR-Chat project's progress-board system (PROGRESS.md → progress.html via pre-commit hook) rather than inventing a new format.
- 2026-07-13 — Agent body model: **SMPL-X with visemes** (`Assets/SMPLX/smplx-visemes-unity.fbx`), imported as Generic rig.
- 2026-07-13 — Triad layout: regular triangle, **side 1 m**, centered on world origin, everyone facing the centroid. Agents at (±0.5, 0, 0.2887); User at (0, 0, −0.5774) represented by the Main Camera at 1.6 m eye height under a `User` root.
- 2026-07-13 — Motion data format: **SMPL-X .npz** (TalkingWithHands): `poses` frames×165 axis-angle (55 joints), `trans`, 60 fps, Y-up right-handed, betas all zero. `*.npz` tracked via Git LFS.
- 2026-07-13 — Motion datasets stay **local-only**; git keeps just two whitelisted example clips (`…interloctr_000` + `…main-agent_000`, via LFS). New `.npz` under `Assets/MotionData/` is git-ignored unless whitelisted in `.gitignore`.
- 2026-07-13 — Clip pairing convention: the two agents always play a **matched take pair** — one plays the `interloctr` clip, the other the `main-agent` clip with the otherwise-identical file name (also in CLAUDE.md).
- 2026-07-13 — Triangle side length increased **1 m → 1.5 m** (agents at (±0.75, 0, 0.4330), user at (0, 0, −0.8660)).
- 2026-07-13 — Coordinate conversion: **direct axis-angle → Unity quaternion**, skipping 6D (that representation is for network regression, not playback). RH→LH mirror across the YZ plane: axis `(x,y,z)` → `(x,−y,−z)`, angle kept; positions `(x,y,z)` → `(−x,y,z)`. No up-axis correction needed (data already Y-up; the FBX pelvis parent chain −90°X·+90°X cancels).
- 2026-07-13 — Root translation applied **relative to the clip's first frame** by default, so agents stay anchored at their triad vertices (raw dataset positions available via a toggle on `SmplxMotionPlayer`).

- 2026-07-13 — Adopted the **official MPI SMPL-X Unity package** (from `E:\Unity_Projects\smplx-unity`) for pose correctives, shape, and expressions — kept verbatim under `Assets/SMPLX/`. Our custom code stays for what the package lacks: npz motion loading/playback and visemes. Its `QuatFromRodrigues` confirmed our axis-angle conversion is identical. License: research use, citation required; textures CC BY-NC.

- 2026-07-13 — Speech: **recorded conversation audio** (TalkingWithHands wavs, paired 1:1 with the npz clips) + **Oculus LipSync** driving the mesh's viseme blendshapes. Viseme map: Oculus `sil` → none, `PP…ou` → blendshapes 506–519; laughter target disabled. Editor `runInBackground` enabled so Play Mode advances while Unity is unfocused.

- 2026-07-13 — Speech switched from recorded wavs to **local TTS**: Kokoro-82M ONNX on Unity Inference Engine, ported from Unity's sentis-samples TextToSpeechSample. Model (~310 MB) + voice bins stay **out of git** (`Assets/Models/` ignored); README links the Hugging Face sources and an editor menu downloads them. Editor-only loading via AssetDatabase for now.

- 2026-07-13 — First gaze pattern: **Fig. 8d turn-taking prototype** from the ICMI paper ("current speaker glances at the next speaker twice" in the last second of the turn). Roles map to the demo as A = current speaker, B = next speaker, **user = listener** (camera plays the listener gaze track: steady on the speaker, then to B at the turn). Eyes do most of the gaze (35° clamp), head contributes a 0.3 fraction over the mocap pose.

## Parked / open questions

- Gaze data format and contents (fields, timing convention, per-role patterns?) — needed before the data pipeline task.
- SMPL-X rig details for gaze: confirm eye bones exist and how eyelids are driven (bones vs blendshapes).
- Platform: desktop screen demo or VR/eye-tracked user? Affects how the "user" participant is represented and sensed.
