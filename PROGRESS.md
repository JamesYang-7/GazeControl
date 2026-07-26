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
  - [~] **Mouth blocker rework (IN FLIGHT, uncommitted)** — sphere wasn't big enough; user asked for a thin vertical disk. Scene currently holds a half-done thin-disk version (leaks at oblique angles); better configs were found in play-mode experiments that are NOT saved. Findings for whoever continues:
    - The white artifact is the bright skybox horizon seen through the mouth-corner corridor: lips recede backward at the corners, so any convex blocker behind the lip plane leaves a diagonal gap. Mesh is standard SMPL-X topology (20,908 tris) — no teeth geometry.
    - Place blockers in pure **head-bone local space** (x=0 = facial midline, +z = face forward, lips at z≈0.064); eye-bone-derived world-vector placement is ~9 mm off-midline and pose-dependent.
    - **Best config found (play-mode only, re-apply in edit mode and save)**: capsule "MouthCavity" localPos (0, −0.015, 0.028), localRot Euler(0,0,90) (axis across the mouth), localScale (0.10, 0.048, 0.055) + two corner spheres "MouthCavityL/R" at (±0.030, −0.012, 0.040), scale (0.035, 0.06, 0.045), all with the MouthCavity material, shadows off. Verified: front + ±25° fully covered; **±45° still shows a small leak** — corner spheres may need to be slightly wider/forward. Iterate with a green debug material and `aa` viseme (blendshape 515) at 100.
    - Dead ends: double-sided body material (backfaces render blown-out white on the inner lip band — reverted, and the .mat now carries an explicit default cull value, hence the uncommitted diff); wide flat disk (pokes cheeks before covering corners); model-root-frame recentering in play mode (body drifts from origin).
    - Verification gotcha: renders in the SAME editor command as a state change show stale geometry — change state in one command, render in the next.
  - [x] **Mouth cavity fix** — SMPL-X has no mouth interior (skybox showed through the open mouth as white); added a dark unlit `MouthCavity` blocker sphere under each `head` bone; verified no leak at max jaw opening (`aa`=100) _(done 2026-07-13 · 337940a)_
  - [x] **Official SMPL-X package integration** — imported MPI `SMPLX.cs` (+SimpleJSON, Matrix, regressor JSONs) for pose correctives / betas / expressions; attached to both agents (Male, correctives High); Meshcapade male texture on a URP/Lit material; verified in Play Mode _(done 2026-07-13 · a870451)_
  - [x] **Per-agent distinct textures** — Agent B gets its own material `SMPLX-Male-URP-AgentB` driven by a SMPLitex-generated SMPL-X albedo (`SMPLitex-texture-00000`, maroon henley) at `Assets/SMPLX/Textures/smplitex_agentB_alb.png`; Agent A keeps the default. Only Agent B's SkinnedMeshRenderer material override changed in the scene; the shared normal map is reused (SMPLitex gives albedo only). SMPLitex source is 512² (softer than the 4096² default) and omits the eyeball UV island, so the eye disc (default-texture center ≈(2372,1772)/4096, r≈150; both eyes share this one UV island) is composited in from the default 4096² albedo via a feathered circle _(done 2026-07-26)_
    - To swap Agent B's texture later: overwrite `smplitex_agentB_alb.png` with a new 512² SMPLitex output, then re-run the eye-disc composite (paste that disc at 512-scale center ≈(296,221), r≈19), reimport. Material/scene wiring stays put.
- [~] **Gaze control system** — drive agents' eye/head gaze from the collected patterns (aversion, partner-directed gaze, pre-turn shifts)
  - [x] **First pattern: Fig. 8d double glance** — `GazeController` (eyes + fractional head over mocap) + `ListenerCamera` (user view = listener track); in the last 1 s of A's turn, A glances at B twice, listener stays on A then moves to B at the turn; verified eye-to-target error 0.0° with 23° eye deflection _(done 2026-07-13 · cab98d4)_
  - [x] **Pattern calibrated from raw prototype data** — `GazePatterns.TurnYieldingDoubleGlance` transcribed verbatim from `p3_ks40_4.npz` (Fig. 8d): micro-glances of 2 and 3 frames @60 fps early in the window (median subsequence start = frame 10), then sustained aversion to the boundary; corrected the eyeballed structure (glances come early, not late). `PatternTimeScale` stretches for visibility _(done 2026-07-13 · b1031a0)_
  - [x] **Fig. 7e pattern (p3_ks30_17), multi-track** — `GazePattern` now holds per-role tracks; 7e: current speaker check → avert (~280 ms) → re-engage into the boundary, next speaker watches then averts at their own turn onset (Kendon/Novick aversion); tracks play in parallel, selectable via `Pattern` enum on `TriadConversation` (7e is the scene default); B engages A 0.8 s after onset as a heuristic bridge beyond the data window _(done 2026-07-13 · fdf145d)_
  - [x] **Window anchoring corrected** — end-of-turn = when the speaker stops talking: anchor is now the last non-silent sample of the TTS clip (the question clip had 0.686 s trailing silence that would have pushed the whole pattern past speech end), and the pattern starts at the rank-0 `subseq_start_idx` (7e: frame 29 → pattern ends 1 frame before EoT; 8d: frame 4) instead of the median _(done 2026-07-13 · 1e2f9b5)_
  - [x] **One-click demo recording** — menu `GazeControl → Record Demo`: enters Play Mode, records Game view 1080p30 + audio via Unity Recorder to git-ignored `Recordings/*.mp4`, auto-stops 1.5 s after the conversation freezes motion. Recorder 5.1.2 embedded in `Packages/` with 6 `GetInstanceID`→`GetEntityId` patches (stock package fails to compile on Unity 6000.5) _(done 2026-07-13 · eb2919f)_
  - More prototypes available in the raw data to add later (e.g. p3_ks20_3 single glance, p3_ks20_14 gaze-then-avert; `manifest.csv` maps figures to files).
  - Listener-track camera rotation cancelled on request (user wants both agents' eyes visible at once); `ListenerCamera` stays in the codebase, unwired — `TriadConversation.ListenerView` is optional.
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

- 2026-07-26 — Agents get distinct appearances via **per-agent materials + SMPLitex SMPL-X textures**. SMPLitex outputs (512², no eyeball UV island) are used as-is for body/face; the eye disc is copied from the default 4096² SMPL-X albedo since both eyes share that single UV island. Agent A stays on the default `SMPLX-Male-URP`.

## Parked / open questions

- Gaze data format and contents (fields, timing convention, per-role patterns?) — needed before the data pipeline task.
- SMPL-X rig details for gaze: confirm eye bones exist and how eyelids are driven (bones vs blendshapes).
- Platform: desktop screen demo or VR/eye-tracked user? Affects how the "user" participant is represented and sensed.
