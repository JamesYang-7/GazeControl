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

- [x] **Project initialization** — Unity 6 URP project, git, working docs _(done 2026-07-13)_
  - [x] **Initial commit** — Unity 6000.5.3f1 URP template, `.gitignore`/`.gitattributes` (Unity templates, Git LFS), `CLAUDE.md` _(done 2026-07-13 · 49c8ca2)_
  - [x] **Progress board setup** — `PROGRESS.md` + `progress-board` skill + pre-commit dashboard sync, adapted from the VR-Chat project _(done 2026-07-13)_
- [ ] **Gaze data pipeline** — import the collected pre-turn-taking gaze behaviour patterns into Unity
  - What format is the collected gaze data in (CSV/JSON? gaze targets vs angles? timing relative to turn end)?
- [ ] **Virtual agents** — two conversational agents with data-driven motion control
  - Where do the character models/rigs come from, and what motion data drives them?
- [ ] **Gaze control system** — drive agents' eye/head gaze from the collected patterns (aversion, partner-directed gaze, pre-turn shifts)
- [ ] **Scene 1: agent↔agent turn-taking** — turn-taking between agent A and B; the user only watches
- [ ] **Scene 2: turn-yielding to user** — agent A signals the user to take the turn via gaze
  - How is "user takes the turn" detected (speech/voice activity, key press, something else)?

## Decisions

- 2026-07-13 — Gaze control and agent motion are both **data-driven**, from gaze behaviour patterns collected before turn-taking in real triad conversations.
- 2026-07-13 — Two target scenes: (1) A↔B turn-taking with the user watching; (2) agent A yields the turn to the user with gaze implication.
- 2026-07-13 — Kept the stock GitHub Unity `.gitignore`/`.gitattributes` templates already in the repo; binary media goes through Git LFS (git-lfs 3.4.0 installed).
- 2026-07-13 — Adopted the VR-Chat project's progress-board system (PROGRESS.md → progress.html via pre-commit hook) rather than inventing a new format.

## Parked / open questions

- Gaze data format and contents (fields, timing convention, per-role patterns?) — needed before the data pipeline task.
- Character models: source and rig requirements (eyes as separate bones? blendshapes for eyelids?).
- Platform: desktop screen demo or VR/eye-tracked user? Affects how the "user" participant is represented and sensed.
- Audio/speech: are the conversations voiced (recorded audio, TTS, silent placeholders)?
