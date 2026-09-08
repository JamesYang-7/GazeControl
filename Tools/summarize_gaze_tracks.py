"""Tabulate the baked gaze tracks of the study 2 clips, one row per clip × seed.

For choosing a seed per clip from what exists: which conditions are baked
at each seed, and what each agent's gaze looked like — the share of ticks
averted, at the other agent and at the participant. Study 1 recorded its
seeds as an arbitrary draw; this table is for checking that a draw is not
degenerate (an agent that averts for a whole clip, or never looks at the
participant), not for optimising anything.

    uv run python Tools/summarize_gaze_tracks.py                 # study2_c*
    uv run python Tools/summarize_gaze_tracks.py --clips study_c1 study_c2
    uv run python Tools/summarize_gaze_tracks.py --condition Proposed
"""

from __future__ import annotations

import argparse
import json
import re
from collections import Counter
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SEGMENT_ROOT = REPO / "Assets" / "DemoSegments"
CONDITIONS = ("SpeakerFollowing", "RoleConditioned", "Proposed")
TRACK_NAME = re.compile(r"^gazetrack_(\w+)_seed(\d+)\.json$")

# GazeTargetType: 0 person, 1 aversion. `person` is the participant id the
# agent looks at: agents are 0 and 1, the human user is 2 (BaselineConditionSetup).
PERSON, AVERSION = 0, 1
USER_ID = 2


def summarize(track_path: Path, duration: float) -> dict:
    track = json.loads(track_path.read_text(encoding="utf-8"))
    agents = {}
    for agent in track["agents"]:
        samples = agent["samples"]
        n = len(samples)
        counts = Counter(
            "avert" if s["type"] == AVERSION else ("user" if s["person"] == USER_ID else "agent")
            for s in samples)
        agents[agent["name"]] = {
            "n": n,
            "complete": n >= int(duration * track["decisionHz"]) - 1,
            "avert": counts["avert"] / n if n else 0.0,
            "agent": counts["agent"] / n if n else 0.0,
            "user": counts["user"] / n if n else 0.0,
        }
    return {"seed": track["baseSeed"], "condition": track["condition"], "agents": agents}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--clips", nargs="*", help="clip folder names; default study2_c*")
    parser.add_argument("--condition", choices=CONDITIONS, help="show one condition's shares instead of all three")
    args = parser.parse_args()

    clips = args.clips or sorted(p.name for p in SEGMENT_ROOT.glob("study2_c*") if p.is_dir())
    shown = (args.condition,) if args.condition else CONDITIONS

    for clip in clips:
        folder = SEGMENT_ROOT / clip
        segment = json.loads((folder / "segment.json").read_text(encoding="utf-8"))
        duration = segment["durationSeconds"]
        rows: dict[int, dict[str, dict]] = {}
        for path in folder.glob("gazetrack_*.json"):
            match = TRACK_NAME.match(path.name)
            if not match:
                continue
            info = summarize(path, duration)
            rows.setdefault(info["seed"], {})[info["condition"]] = info

        print(f"\n{clip}: {segment['stem']} {duration:.1f} s, "
              f"{', '.join(f'{a['speaker']}={a.get('subject', a['side'])} ({a['voice']})' for a in segment['agents'])}")
        header = "seed  baked  " + "  ".join(f"{c[:8]:>8s} A avert/agent/user  B avert/agent/user" for c in shown)
        print(header)
        for seed in sorted(rows):
            baked = "".join("x" if c in rows[seed] else "." for c in CONDITIONS)
            cells = []
            for condition in shown:
                info = rows[seed].get(condition)
                if info is None:
                    cells.append(f"{'':>8s} {'-':>18s}  {'-':>18s}")
                    continue
                parts = []
                for name in ("AgentA", "AgentB"):
                    a = info["agents"].get(name)
                    if a is None:
                        parts.append(f"{'?':>18s}")
                        continue
                    flag = "" if a["complete"] else "!"
                    parts.append(f"{a['avert']:5.0%}/{a['agent']:4.0%}/{a['user']:4.0%}{flag:>1s}")
                cells.append(f"{'':>8s} {parts[0]:>18s}  {parts[1]:>18s}")
            print(f"{seed:>4d}  {baked:5s}  " + "  ".join(cells))
    print("\nbaked column: SpeakerFollowing / RoleConditioned / Proposed; '!' marks a track shorter than the clip.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
