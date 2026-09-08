"""Participant gaze measures from a study's take logs.

Reads every ``<stem>_user.csv`` under a recordings root (default
``Recordings/Study_02``) together with its sidecar JSON, the agent log and the
segment, and reports per take:

* tracker health — usable-gaze share, blink-like invalid runs, sampling gaps,
  uncalibrated ticks;
* where the participant looked — share of ticks on each agent and elsewhere,
  fixation runs on a person, switches between the two agents;
* gaze around the annotated turn — who they were on in the two seconds before
  and after the boundary, and when they first fixated the next speaker;
* the mutual-gaze columns — share of ticks each agent looked at the participant,
  mutual gaze, and gaze-return latency after each agent-looks-at-user onset.

``--check`` cross-checks the participant log's ``agents_looking_at_user`` bit
set against the agent log's ``mutual_gaze_with_human`` on the shared
conversation clock. Before 2026-09-08 that column was sampled off eye bones
the motion player had just reset, and came out identical across conditions;
takes from before the fix fail this check and their agent-half columns must be
taken from the agent log instead.

    uv run python Tools/analyze_participant_gaze.py
    uv run python Tools/analyze_participant_gaze.py --root Recordings/Study_02 --participant P01 --check
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import sys

import numpy as np
import pandas as pd

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SEGMENTS = os.path.join(REPO, "Assets", "DemoSegments")
HZ = 60
SPEAKER_TO_AGENT = {1: 0, 2: 1}  # speaker 1 is AgentA (id 0), speaker 2 AgentB (id 1)


def run_lengths(mask: np.ndarray) -> np.ndarray:
    """Lengths of the consecutive True runs in ``mask``."""
    m = np.asarray(mask, bool)
    if not m.any():
        return np.array([], int)
    edges = np.diff(np.concatenate([[0], m.astype(int), [0]]))
    return np.flatnonzero(edges == -1) - np.flatnonzero(edges == 1)


def intervals(t: np.ndarray, mask: np.ndarray) -> list[tuple[float, float]]:
    m = np.asarray(mask, bool)
    if not m.any():
        return []
    edges = np.diff(np.concatenate([[0], m.astype(int), [0]]))
    starts = np.flatnonzero(edges == 1)
    ends = np.flatnonzero(edges == -1) - 1
    return [(round(float(t[s]), 2), round(float(t[e]), 2)) for s, e in zip(starts, ends)]


def head_forward(yaw_deg: np.ndarray, pitch_deg: np.ndarray) -> np.ndarray:
    """Unity forward vector from the logged signed yaw/pitch (degrees)."""
    y = np.radians(yaw_deg)
    p = np.radians(pitch_deg)
    return np.stack([np.sin(y) * np.cos(p), -np.sin(p), np.cos(y) * np.cos(p)], 1)


def angle_between(a: np.ndarray, b: np.ndarray) -> np.ndarray:
    an = np.linalg.norm(a, axis=1)
    bn = np.linalg.norm(b, axis=1)
    ok = (an > 0) & (bn > 0)
    out = np.full(len(a), np.nan)
    out[ok] = np.degrees(np.arccos(np.clip((a[ok] * b[ok]).sum(1) / (an[ok] * bn[ok]), -1, 1)))
    return out


def fixations(label: np.ndarray) -> list[tuple[int, float]]:
    """(label, seconds) runs after filling invalid ticks with the last label."""
    filled = pd.Series(label).replace(-1, np.nan).ffill().bfill()
    if filled.isna().all():
        return []
    filled = filled.astype(int).values
    bounds = np.concatenate([[0], np.flatnonzero(np.diff(filled) != 0) + 1, [len(filled)]])
    return [(int(filled[bounds[i]]), (bounds[i + 1] - bounds[i]) / HZ) for i in range(len(bounds) - 1)]


def analyze_take(user_csv: str, check: bool) -> dict:
    stem = user_csv[: -len("_user.csv")]
    side = json.load(open(stem + ".json", encoding="utf-8-sig"))
    with open(os.path.join(SEGMENTS, side["case"], "segment.json"), encoding="utf-8") as f:
        seg = json.load(f)
    u = pd.read_csv(user_csv, encoding="utf-8-sig")
    event = seg["events"][0] if seg.get("events") else None
    turn = event["turnTime"] if event else None
    first_speaker = seg["turns"][0]["speaker"]
    next_speaker = seg["turns"][1]["speaker"] if len(seg["turns"]) > 1 else None

    n = len(u)
    t = u.t.values
    valid = (u.tracker == "Valid").values
    target_type = u.target_type.values
    target_id = u.target_id.values
    on_a = (target_type == "person") & (target_id == 0)
    on_b = (target_type == "person") & (target_id == 1)
    elsewhere = target_type == "elsewhere"

    print("=" * 96)
    print(f"{os.path.basename(stem)}  |  {side['case']} {side['condition']}  |  "
          f"turn at {turn} s, speaker {first_speaker} -> {next_speaker}  |  segment {seg['durationSeconds']} s")

    # --- tracker health
    invalid_runs = run_lengths(~valid)
    new_frames = int(u.device_frame.diff().gt(0).sum())
    capture_dt_ms = np.diff(u.capture_time_ns.values) / 1e6
    print(f"  ticks {n} ({t[-1]:.2f} s), on grid: {np.allclose(t, u.tick / HZ, atol=0.002)}, "
          f"new tracker frames {new_frames}/{n - 1}, capture dt median {np.median(capture_dt_ms):.1f} ms max {capture_dt_ms.max():.0f} ms")
    print(f"  usable gaze {valid.mean() * 100:.1f}%, invalid runs {len(invalid_runs)} "
          f"(median {np.median(invalid_runs) if len(invalid_runs) else 0:.0f} ticks, max {invalid_runs.max() if len(invalid_runs) else 0}), "
          f"uncalibrated ticks {int((u.calibrated == 0).sum())}")
    eye_in_head = angle_between(head_forward(u.head_yaw.values, u.head_pitch.values)[valid],
                                u[["gaze_dx", "gaze_dy", "gaze_dz"]].values[valid])
    print(f"  head yaw [{u.head_yaw.min():.0f}, {u.head_yaw.max():.0f}] deg; eye-in-head angle median "
          f"{np.nanmedian(eye_in_head):.1f} deg, p95 {np.nanpercentile(eye_in_head, 95):.1f} deg")

    # --- where they looked
    print(f"  on A {on_a.mean() * 100:.1f}%  on B {on_b.mean() * 100:.1f}%  elsewhere {elsewhere.mean() * 100:.1f}%  "
          f"invalid {(target_type == 'invalid').mean() * 100:.1f}%")
    ang = u.target_angle_deg.values
    if elsewhere.any():
        print(f"  angle to nearest head when elsewhere: median {np.median(ang[elsewhere]):.1f} deg, "
              f"within 10 deg {(ang[elsewhere] < 10).mean() * 100:.0f}%")
    label = np.where(on_a, 0, np.where(on_b, 1, np.where(elsewhere, 2, -1)))
    fix = fixations(label)
    person_fix = [d for lab, d in fix if lab in (0, 1)]
    agent_only = [lab for lab, _ in fix if lab != 2]
    switches = sum(1 for i in range(1, len(agent_only)) if agent_only[i] != agent_only[i - 1])
    if person_fix:
        print(f"  fixations on a person: {len(person_fix)}, mean {np.mean(person_fix):.2f} s, max {max(person_fix):.2f} s; "
              f"A<->B switches {switches}")

    # --- around the turn
    result = dict(take=os.path.basename(stem), case=side["case"], condition=side["condition"],
                  usable=valid.mean() * 100, on_a=on_a.mean() * 100, on_b=on_b.mean() * 100,
                  elsewhere=elsewhere.mean() * 100, fixations=len(person_fix))
    if turn is not None and next_speaker in SPEAKER_TO_AGENT:
        id_first = SPEAKER_TO_AGENT[first_speaker]
        id_next = SPEAKER_TO_AGENT[next_speaker]
        pre = (t >= turn - 2) & (t < turn)
        post = (t >= turn) & (t < turn + 2)

        def share(mask: np.ndarray, agent: int) -> float:
            return float(((target_type[mask] == "person") & (target_id[mask] == agent)).mean() * 100) if mask.any() else float("nan")

        on_next = (target_type == "person") & (target_id == id_next)
        first_on_next = np.flatnonzero(on_next & (t >= turn - 1))
        shift = (t[first_on_next[0]] - turn) if len(first_on_next) else None
        print(f"  2 s before turn: first speaker {share(pre, id_first):.0f}%, next {share(pre, id_next):.0f}%  |  "
              f"2 s after: first {share(post, id_first):.0f}%, next {share(post, id_next):.0f}%  |  "
              f"first fixation on next speaker at turn {shift:+.2f} s" if shift is not None else
              f"  2 s before turn: first speaker {share(pre, id_first):.0f}%, next {share(pre, id_next):.0f}%  |  "
              f"never on the next speaker after turn-1 s")
        result.update(pre_first=share(pre, id_first), pre_next=share(pre, id_next),
                      post_next=share(post, id_next), shift_s=shift)

    # --- the agent half
    looking = u.agents_looking_at_user.values
    print(f"  agents looking at participant: A {((looking & 1) > 0).mean() * 100:.1f}%  B {((looking & 2) > 0).mean() * 100:.1f}%; "
          f"mutual gaze {u.mutual_gaze.mean() * 100:.1f}% of ticks")
    latencies = []
    for agent, bit in ((0, 1), (1, 2)):
        look = (looking & bit) > 0
        for onset in np.flatnonzero(look[1:] & ~look[:-1]) + 1:
            window = np.flatnonzero((target_type[onset:onset + 3 * HZ] == "person") & (target_id[onset:onset + 3 * HZ] == agent))
            latencies.append((agent, round(float(t[onset]), 2), round(float(window[0]) / HZ, 2) if len(window) else None))
    if latencies:
        print(f"  gaze-return latency per onset (agent, t, s or None within 3 s): {latencies}")
    result["mutual"] = u.mutual_gaze.mean() * 100

    if check:
        result["check"] = check_against_agent_log(stem, u)
    return result


def check_against_agent_log(stem: str, u: pd.DataFrame) -> str:
    """Agreement between the two logs' agent-looks-at-participant columns."""
    a = pd.read_csv(stem + ".csv", encoding="utf-8-sig")
    verdicts = []
    for agent_id, name in ((0, "AgentA"), (1, "AgentB")):
        rows = a[(a.agent_id == name) & (a.t >= 0)]
        if rows.empty:
            verdicts.append(f"{name}: no agent rows")
            continue
        if rows.t.iloc[0] > 5:
            verdicts.append(f"{name}: agent log t starts at {rows.t.iloc[0]:.1f} s: session clock, pre-fix log")
            continue
        rendered = np.interp(u.t.values, rows.t.values, rows.mutual_gaze_with_human.values) > 0.5
        sampled = ((u.agents_looking_at_user.values >> agent_id) & 1) > 0
        agreement = (rendered == sampled).mean() * 100
        verdicts.append(f"{name}: {agreement:.1f}% agreement "
                        f"({intervals(u.t.values, sampled)} vs {intervals(rows.t.values, rows.mutual_gaze_with_human.values == 1)})")
    text = "; ".join(verdicts)
    print(f"  check: {text}")
    return text


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--root", default=os.path.join(REPO, "Recordings", "Study_02"),
                        help="recordings root holding the per-clip take folders")
    parser.add_argument("--participant", default=None, help="only this label's takes (e.g. P01)")
    parser.add_argument("--check", action="store_true",
                        help="cross-check agents_looking_at_user against the agent log")
    args = parser.parse_args(argv)

    pattern = os.path.join(args.root, "*", f"{args.participant or '*'}_*_user.csv")
    files = sorted(glob.glob(pattern))
    if not files:
        print(f"no participant logs match {pattern}", file=sys.stderr)
        return 1

    results = [analyze_take(f, args.check) for f in files]
    print("=" * 96)
    table = pd.DataFrame(results).drop(columns=["check"], errors="ignore")
    print(table.round(1).to_string(index=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
