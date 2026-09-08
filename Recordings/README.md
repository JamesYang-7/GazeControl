# Recordings

Everything a play session writes, grouped by what it was for. The folder is
git-ignored apart from this file; nothing here is a source.

Reorganised 2026-09-08 from one flat folder. Each study scene writes under its own
root, which is the `OutputDirectory` of its `GazeConditionRunner` (and of its
`QuestionnaireSession`); the participant roster proposes labels from that root
alone, so `P01` in one study is unrelated to `P01` in another.

| folder | written by | holds |
| --- | --- | --- |
| `Study_01/` | `TriadScene` | Study 1 (TalkingWithHands, five clips × three conditions). `P05`–`P22` are the analysed participants, each with `session.json`, `responses.csv` and `comments.jsonl`; `P01`–`P04` were pilots and never had folders here. `study_c1`–`study_c5` hold every take log of those clips: `<label>_<condition>_<timestamp>.csv` (the agent log, one row per frame), its `.json` sidecar, and `_user.csv` (the participant's gaze, one row per decision tick). `P00_…` logs in there are development runs. |
| `Study_02/` | `Study2Scene` | Study 2 (3People-2022 clips `study2_c1`–`c7`), same layout. Created by the first play. |
| `Demos/` | `GazeControl → Record Demo` and early condition runs | `case*` and `cand_*` take folders with their mp4s and logs, and `segment_candidates.csv`. |
| `Debug/` | development runs on the `P00` label | One timestamped `P00_<date>_<time>` folder per run, holding whatever the questionnaire wrote. |

`Tools/build_study_figures.py` reads `Study_01/P05..P22/responses.csv`.
