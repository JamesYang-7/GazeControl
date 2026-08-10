# GazeControl

A gaze control demo for virtual agents in a triad conversation: two virtual agents plus the user, standing on a regular triangle. Gaze behavior patterns collected before turn-taking in real triad conversations drive the agents' gaze. Everything else the agents do is replayed from a real recorded conversation: body motion is TalkingWithHands SMPL-X mocap, speech is that recording's own audio, and the turn boundaries the gaze patterns fire on are its annotated end-of-turn events.

Progress, decisions, and open questions live in [PROGRESS.md](PROGRESS.md) (rendered dashboard: `progress.html`).

## Requirements

- Unity **6000.5.3f1** (URP)
- **git-lfs** (binary assets: models, textures, motion clips)

## Setup after cloning

1. `git lfs install` (once per machine) — LFS assets download on checkout.
2. `git config core.hooksPath .githooks` — enables the progress-board pre-commit hook.
3. Export a demo segment (see below), then open `Assets/Scenes/TriadScene.unity` and press Play.

## Demo segments (not in git)

The demo replays a stretch of a real TalkingWithHands conversation. The corpus lives outside the repo — `F:\Data\TalkingWithHandsCentered` on the development machine, holding synchronised audio, word-level transcripts, 60 fps grounded SMPL-X motion, and annotated end-of-turn events (`EoT_TWH/`).

Pick and export a segment with:

```bash
python Tools/find_demo_segments.py                              # rank candidates
python Tools/find_demo_segments.py --top 0 --inspect 1          # read one as a transcript
python Tools/find_demo_segments.py --top 0 --export 1 --name case1_seg01
```

That writes `Assets/DemoSegments/<name>/` (git-ignored): the two trimmed wavs and a `segment.json` holding the turn schedule, the end-of-turn events and the motion frame offset. Point `RecordedConversation.SegmentPath` at the JSON — the motion npz is read from the corpus in place, so nothing large is copied into the project.

Selection rules (20–30 s, at least two end-of-turn events, no sentence cut in half, events far enough apart that their gaze patterns do not collide) are documented in the script.

## Gaze prototypes

The paper's fifteen printed prototypes (Figures 6–8) are transcribed into `Assets/GazeControl/Scripts/Runtime/Gaze/Policy/GazePatterns.g.cs` by `python Tools/build_gaze_patterns.py`. The generated file is committed; regenerate it only when the raw prototype archives change.

## Motion data (mostly not in git)

`Assets/MotionData/` holds a committed example pair of TalkingWithHands SMPL-X clips (`.npz`, 55-joint axis-angle @ 60 fps) with paired audio (`.wav`), kept as a minimal sample for the motion loader. Everything else dropped into the folder stays local (git-ignored). The two agents always play a matched `interloctr`/`main-agent` pair from the same take.

## Third-party components

- **SMPL-X** body model + official Unity package (`Assets/SMPLX/`) — [SMPL-X model license](https://smpl-x.is.tue.mpg.de/modellicense), research use; cite the SMPL-X paper in publications. Sample textures by Meshcapade (CC BY-NC 4.0).
- **Oculus LipSync** (`Assets/Oculus/LipSync/`) — Oculus Audio SDK License; drives the mesh's viseme blendshapes from played audio.
- **TalkingWithHands** motion/audio data — see the dataset's own license/terms.

The Kokoro-82M text-to-speech integration was removed on 2026-08-10, when the agents' speech became the corpus's own recorded audio. It is in the git history if it is ever wanted back.
