"""List the glitch-free stretches of the converted 3People-2022 sessions.

The converter (``convert_3people_smplx.py``) repairs what can be repaired, but
some Vicon fits fail for stretches at a time -- a subject with a missing heel
marker whose leg flips between two solutions, a head marker that slipped -- and
a demo needs 20-30 s in which all three participants are clean at once. This
script reads the converted clips and finds those windows.

A frame is *bad* for a participant when any watched joint's rotation changes
by more than the joint's threshold between consecutive frames, or the pelvis
translation moves more than ``--trans-mm``. A window is good when no
participant has a bad frame inside it. Thresholds are per frame at 60 fps, so
5° is 300°/s: a fit glitch on a pelvis or head, but within reach of a real
gesture on a shoulder, which is why the arms get a looser default.

Usage::

    uv run python Tools/find_3people_clean_windows.py                 # every session
    uv run python Tools/find_3people_clean_windows.py --date 12-15-2021 --min-seconds 20
    uv run python Tools/find_3people_clean_windows.py --shoulder-deg 5   # stricter arms
"""

from __future__ import annotations

import argparse
import collections
import os
import re
import sys
from pathlib import Path

import numpy as np
from scipy.spatial.transform import Rotation

DEFAULT_ROOT = Path(r"F:\Data\3People-2022-SMPLX")
FRAME_RATE = 60

# SMPL-X joint index -> threshold key. Fingers, wrists and elbows are not
# watched: hand marker swaps are everywhere and do not read as a glitch of the
# body from a conversational distance.
WATCHED = {
    0: "body", 1: "body", 2: "body", 4: "body", 5: "body", 7: "body", 8: "body",
    15: "head", 16: "shoulder", 17: "shoulder",
}
NAMES = {0: "pelvis", 1: "left_hip", 2: "right_hip", 4: "left_knee", 5: "right_knee",
         7: "left_ankle", 8: "right_ankle", 15: "head", 16: "left_shoulder", 17: "right_shoulder"}


def bad_frames(clip: Path, thresholds: dict[str, float], trans_mm: float) -> np.ndarray:
    z = np.load(clip)
    poses = z["poses"].astype(float)
    trans = z["trans"].astype(float)
    bad = np.linalg.norm(np.diff(trans, axis=0), axis=1) > trans_mm / 1000.0
    for j, key in WATCHED.items():
        r = Rotation.from_rotvec(poses[:, 3 * j:3 * j + 3])
        step = np.degrees((r[1:] * r[:-1].inv()).magnitude())
        bad |= step > thresholds[key]
    return np.concatenate([[False], bad])  # index = frame; a step is charged to the later frame


def good_runs(bad: np.ndarray) -> list[tuple[int, int]]:
    runs, i, n = [], 0, len(bad)
    while i < n:
        if bad[i]:
            i += 1
            continue
        j = i
        while j < n and not bad[j]:
            j += 1
        runs.append((i, j))
        i = j
    return runs


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--root", type=Path, default=DEFAULT_ROOT, help="converted clips, <root>/<date>/*.npz")
    ap.add_argument("--date", help="only this capture date")
    ap.add_argument("--session", type=int, help="only this session")
    ap.add_argument("--min-seconds", type=float, default=15.0, help="report windows at least this long")
    ap.add_argument("--body-deg", type=float, default=5.0, help="per-frame step limit, pelvis/hips/knees/ankles")
    ap.add_argument("--head-deg", type=float, default=5.0)
    ap.add_argument("--shoulder-deg", type=float, default=8.0)
    ap.add_argument("--trans-mm", type=float, default=15.0, help="per-frame pelvis translation limit")
    ap.add_argument("--top", type=int, default=4, help="windows per session")
    args = ap.parse_args()
    thresholds = {"body": args.body_deg, "head": args.head_deg, "shoulder": args.shoulder_deg}

    sessions: dict[tuple[str, int], list[Path]] = collections.defaultdict(list)
    for clip in sorted(args.root.glob("*/Session_*_pc*_*.npz")):
        date = clip.parent.name
        s = int(re.search(r"Session_(\d+)", clip.name).group(1))
        if (args.date and date != args.date) or (args.session and s != args.session):
            continue
        sessions[(date, s)].append(clip)
    if not sessions:
        sys.exit(f"no converted sessions under {args.root}")

    results = []
    for (date, s), clips in sessions.items():
        if len(clips) != 3:
            print(f"{date} S{s}: {len(clips)} clips, expected 3 -- skipped", file=sys.stderr)
            continue
        first_frame = int(np.load(clips[0])["frame_index"][0])
        bads = [bad_frames(c, thresholds, args.trans_mm) for c in clips]
        n = min(len(b) for b in bads)
        bad = np.zeros(n, dtype=bool)
        for b in bads:
            bad |= b[:n]
        runs = sorted(good_runs(bad), key=lambda r: r[0] - r[1])
        results.append((date, s, [re.sub(r"^Session_\d+_", "", c.stem) for c in clips], n, float(bad.mean()), runs))

    results.sort(key=lambda r: -(r[5][0][1] - r[5][0][0]) if r[5] else 0)
    print(f"thresholds: body {args.body_deg} deg, head {args.head_deg} deg, shoulders {args.shoulder_deg} deg per frame, "
          f"translation {args.trans_mm} mm; windows >= {args.min_seconds:.0f} s, all three participants clean")
    for date, s, subjects, n, frac, runs in results:
        long = [(a, b) for a, b in runs if (b - a) / FRAME_RATE >= args.min_seconds][:args.top]
        head = f"{date} S{s} ({', '.join(subjects)}): {100 * (1 - frac):.1f}% clean"
        if not long:
            print(f"{head}; longest {(runs[0][1] - runs[0][0]) / FRAME_RATE:.0f} s" if runs else head)
            continue
        print(head)
        for a, b in long:
            print(f"    {(b - a) / FRAME_RATE:5.1f} s   export frames {a}-{b - 1}   (C3D {a + first_frame}-{b - 1 + first_frame}, "
                  f"{a / FRAME_RATE:.1f}-{b / FRAME_RATE:.1f} s into the export)")


if __name__ == "__main__":
    main()
