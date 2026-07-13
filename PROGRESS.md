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
  - [x] **SMPL-X .npz motion playback** — `NpyReader`/`SmplxMotionClip`/`SmplxAnimUtils`/`SmplxMotionPlayer` under `Assets/GazeControl/Scripts/Runtime/Motion/`; direct axis-angle → Unity quaternion (no 6D detour); AgentA plays a TalkingWithHands clip _(done 2026-07-13)_
    - Verified in edit mode via posed frame render; awaiting user check in Play Mode.
- [ ] **Gaze control system** — drive agents' eye/head gaze from the collected patterns (aversion, partner-directed gaze, pre-turn shifts)
- [ ] **Scene 1: agent↔agent turn-taking** — turn-taking between agent A and B; the user only watches
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
- 2026-07-13 — Coordinate conversion: **direct axis-angle → Unity quaternion**, skipping 6D (that representation is for network regression, not playback). RH→LH mirror across the YZ plane: axis `(x,y,z)` → `(x,−y,−z)`, angle kept; positions `(x,y,z)` → `(−x,y,z)`. No up-axis correction needed (data already Y-up; the FBX pelvis parent chain −90°X·+90°X cancels).
- 2026-07-13 — Root translation applied **relative to the clip's first frame** by default, so agents stay anchored at their triad vertices (raw dataset positions available via a toggle on `SmplxMotionPlayer`).

## Parked / open questions

- Gaze data format and contents (fields, timing convention, per-role patterns?) — needed before the data pipeline task.
- SMPL-X rig details for gaze: confirm eye bones exist and how eyelids are driven (bones vs blendshapes).
- Platform: desktop screen demo or VR/eye-tracked user? Affects how the "user" participant is represented and sensed.
- Audio/speech: are the conversations voiced (recorded audio, TTS, silent placeholders)?
