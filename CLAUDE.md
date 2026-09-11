# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.
It is deliberately a high-level guide: the design records, decisions and progress live in
`Assets/Docs/` and `PROGRESS.md`, indexed below. Do not put details or progress back in here —
add them to the doc that owns the subject and, if a new subject appears, add a doc and an index line.

## Project Overview

A gaze control demo for virtual agents in a triad conversation: two virtual agents (A and B) plus the user. Gaze behavior patterns collected from real triad conversations (gaze shifts preceding turn-taking) drive the agents' gaze control. Motion control of the agents is also data-driven.

Target demo scenes:
1. **Agent-to-agent turn-taking** — turn-taking happens between agent A and agent B while the user just watches.
2. **Turn-yielding to the user** — agent A wants the user to take the turn and signals this with gaze.

## Environment

- **Unity 6000.5.3f1** (Unity 6) with the **Universal Render Pipeline (URP)**.
- Input handled via the **Input System** package (`Assets/InputSystem_Actions.inputactions`).
- **Timeline** and **Unity Test Framework** packages are installed.
- URP settings live under `Assets/Settings/`; `SampleScene.unity` and `Assets/TutorialInfo/` are template leftovers.
- The agents are **SMPL-X** bodies driven by two layers: our motion player under
  `Assets/GazeControl/Scripts/Runtime/Motion/` and the official MPI `Assets/SMPLX/Scripts/SMPLX.cs`
  (imported verbatim — do not restyle). SMPL-X model license (research use); Meshcapade sample
  textures are CC BY-NC. Oculus LipSync under `Assets/Oculus/LipSync/` is third-party and verbatim.
- The study runs live in a **Varjo** headset on tethered PC VR through the Varjo Unity XR plugin;
  XR settings under `Assets/XR/` are committed. XR does not start by itself — see `xr-rig.md`.
- Two scenes: `Assets/Scenes/TriadScene.unity` (study 1, dyadic TalkingWithHands corpus) and
  `Assets/Scenes/Study2Scene.unity` (study 2, three-party 3People-2022 corpus), plus
  `ThreePartyReplay.unity` for watching whole 3People sessions.
- The corpora are machine-local and outside the repo. Study 1's is `F:\Data\TalkingWithHandsCentered`;
  study 2's converted clips are found through the git-ignored `data-roots.json` at the project root
  (`data-roots.example.json` is the template). The gaze prototypes come from `{ICMI_ROOT}` =
  `F:\Research\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking`.
- Everything the editor offers is under the **`GazeControl`** menu; `GazeControl → Help` at its foot
  indexes the commands.

## Where things are documented

All under `Assets/Docs/`. Read the relevant doc **before** touching its subject — most rules there
were found the hard way and the doc says why.

**How the system works**
- `system-architecture.md` — SMPL-X, the recorded conversation and its single clock, demo-segment
  export, lip sync and the mouth bag, the `IGazePolicy` layer and the three conditions, the shared
  `GazeController`, agent textures matched to voices, and motion-data conventions (matched clip
  pairs, root translation, machine-local corpus roots).
- `baseline-spec.md` — the two baseline gaze policies and the shared animation layer, as specified.
- `math.md` — rotation representations used by the converters.
- `xr-rig.md` — the Varjo rig: loader choice, audio routing, recentring, eye height, participant
  gaze logging, and what is still unverified on hardware.

**Running the study**
- `session-runbook.md` — the operator's procedure, start to finish. **Start here for a session.**
- `study-session-notes.md` — how a session is armed, ordered, labelled and logged, and how the
  questionnaire's two surfaces, keys, wand pointer and response files work.
- `questionnaire-ui-design.md` — the questionnaire's UI design; `user-study-design.md` and
  `study-registration.md` — study 1's design, instrument and pre-registered analysis.
- `scene1-clip-candidates.md` — how study 1's five clips were chosen.

**Study 2 and the 3People corpus**
- `study2-design.md` — the design and the handoff: the triadic prototypes played in a triad with
  the participant's gaze as an outcome measure. Read it before touching study-2 work.
- `3people-motion-sources.md` — the four source roots, file formats, conversion plan and traps.
- `3people-conversion.md` — the converter, segment finder, replay scene, seat placement, export
  and what is not done.

**Outputs**
- `demo-video.md` — rendering the flat demo video and its second camera.
- `study-figures.md` — what the paper's figures plot and why (`Tools/build_study_figures.py`).
- `Recordings/README.md` — layout of the recordings root (`Study_01`, `Study_02`, `Demos`, `Debug`).

## Progress board

`PROGRESS.md` at the repo root is the project's working memory: nested checkbox task list, decisions log, and open questions. Keep it current via the `progress-board` skill — update it whenever a task starts/finishes, a decision is made, or before committing. `progress.html` is auto-generated from it by the pre-commit hook in `.githooks/` (enabled per clone with `git config core.hooksPath .githooks`) — never hand-edit the HTML.

## The paper — `{PAPER_ROOT}`

**The CHI 2027 paper is a separate git repository, outside this one.** `{PAPER_ROOT}` stands for it, currently `F:\Research\CHI_2027___Explainable_Gaze_Patterns_for_Turn_Taking`. It moved out of this repository's git-ignored `Research/` tree on 2026-09-06 so that it could have version control of its own; do not look for it under `Research/`, which now holds only corpora, reference PDFs and notes.

Write `{PAPER_ROOT}/...` rather than a bare path when referring to something over there, and expect the same symbol pointing back: inside the paper, `{UNITY_PROJECT_ROOT}/...` means this repository. `figures/` and `README.md` name real things in both places, so an unqualified path is ambiguous rather than merely terse.

- **`{PAPER_ROOT}/style_guidlines.md` is the entry point for anything paper-facing** — voice, length, structure, and an index of the documents it defers to. `{PAPER_ROOT}/todo.md` is the running handoff: what is unfinished and what must not be undone.
- **`Tools/build_study_figures.py` lives here and writes there.** It draws the user study figures into `{PAPER_ROOT}/figures/`, taking that location from the `GAZECONTROL_PAPER_DIR` environment variable or `--out` rather than hard-coding it. Its specification is `Assets/Docs/study-figures.md`, which stays on this side because it documents a generator that lives on this side.
- **Numbers in the paper's prose come from that script and nowhere else.** If a figure and a sentence disagree, regenerate rather than retype.

## Working in this repo

- **Python scripts run from the repo's uv environment**: `pyproject.toml` at the root, `uv sync` once per clone (creates the git-ignored `.venv/`), then `uv run python Tools/<script>.py`. The interpreter on PATH has no numpy. `identify_speakers.py` needs the opt-in `speaker` group (`uv sync --group speaker`, torch + speechbrain).
- This is a Unity project: every asset file has a paired `.meta` file. When adding, moving, or deleting assets outside the Unity Editor, keep `.meta` files consistent (let the Editor generate them where possible, and always commit them together with their asset).
- Scenes, prefabs, and most `.asset` files are Unity YAML (see `.gitattributes`); binary media (models, textures, audio) go through **Git LFS**.
- Tests use the Unity Test Framework and run inside the Unity Editor (Test Runner window, or via the JetBrains Unity test tooling when available).
- **`Research/` holds local-only material** — raw gaze corpora (`corpora/`), reference PDFs (`papers/`), working notes (`notes/`). Everything there is git-ignored except its `README.md`. It sits outside `Assets/` deliberately: Unity imports every file under `Assets/` regardless of `.gitignore`, generating `.meta` files and Library entries for data that is never a runtime asset. Put reference material there, not under `Assets/Docs/`. **The paper is no longer among it** — see `{PAPER_ROOT}` above.
- **Anything read off the scene as a baseline will be read many times**: a session re-arms fifteen
  clips in place without reloading. Sample once for the life of the component, never per re-arm.
- **Gaze is decided by target, never by angle**, and the animation layer is identical across
  conditions; a policy names who to look at and `GazeController` renders it.
- **Generated files are never hand-edited**: `GazePatterns.g.cs`, `progress.html`, the JSON
  parameter files under `Assets/GazeControl/Resources/` and the exported segments all have a
  generator under `Tools/` — change the generator and rerun it.
- **Record decisions where they belong**: the `Assets/Docs` file that owns the subject, dated, with
  the reason, and the decisions log in `PROGRESS.md`. Keep this file to overview, environment,
  index and principles.
