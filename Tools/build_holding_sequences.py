"""Build the holding-sequence bank from the local gaze corpus.

The proposed condition's substrate replays measured gaze verbatim: outside the
pre-turn prototype windows each agent plays real holding-period fixation
sequences ("stretches") drawn from our three-party corpus, conditioned on its
current conversational role. Replay is non-parametric on purpose - side-
participant gaze and aversion time come out right by construction, with no
duration law to argue about (chi-square and even lognormal fit these durations
poorly; see dwell_family.md in the corpus).

Everything this script reads lives under ``Research/corpora``, which is
git-ignored; the JSON it writes is committed, so the condition is reproducible
without the corpus. Regenerate with:

    python Tools/build_holding_sequences.py

A stretch is a maximal run of consecutive fixations from one (session, gazer)
channel that all start outside +/-1 s of the nearest annotated turn instant,
with a constant gazer role. Consecutive is checked on frame numbers - the
corpus tiles each channel's timeline losslessly, so a dropped (censored) or
non-holding row leaves a frame gap that breaks the run by itself.

The committed bank is a sample, not the full pool (10,384 stretches would be a
megabyte of JSON nobody audits). Per role, the pool is sorted by total stretch
duration and evenly spaced indices are taken, which preserves the duration
distribution's quantiles exactly, needs no PRNG, and diffs cleanly on
regeneration.
"""

from __future__ import annotations

import argparse
import csv
import json
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CORPUS = REPO / "Research" / "corpora" / "gaze_events" / "gaze_events_md6.csv"
OUTPUT = REPO / "Assets" / "GazeControl" / "Resources" / "HoldingSequences.json"

# Corpus role/target keys, matching ParticipantRole / GazeTargetRole on the C#
# side ("sp" speaker, "ad" addressee, "sd" side participant).
ROLES = ["sp", "ad", "sd"]

# A fixation is "holding" when it starts more than this many frames (60 fps)
# from the nearest annotated turn instant - the corpus's own +/-1 s convention.
HOLDING_THRESHOLD_FRAMES = 60
# Stretches kept per role. A ~25 s take consumes only a handful per agent, so
# this is not about coverage - it is about the bank's duration-weighted target
# composition matching the corpus S ratios, which uniform stretch draws then
# reproduce. The long tail dominates that composition, so a small sample rides
# on a few tail picks: at 250/role the worst cell sits 0.08 off the corpus,
# at 500/role it is 0.014 (measured; the occupancy test asserts 0.06).
SAMPLE_PER_ROLE = 500


def load_stretches() -> dict[str, list[dict]]:
    """Extract holding stretches from the event table, keyed by gazer role."""
    channels = defaultdict(list)
    with open(CORPUS, newline="") as handle:
        for row in csv.DictReader(handle):
            if row["left_censored"].lower() == "true" or row["right_censored"].lower() == "true":
                continue

            if abs(int(row["frames_to_turn"])) <= HOLDING_THRESHOLD_FRAMES:
                continue

            channels[(row["session_id"], row["gazer_id"])].append({
                "role": row["gazer_role"],
                "target": row["gaze_target"],
                "start_frame": int(row["start_frame"]),
                "duration_frames": int(row["duration_frames"]),
                "duration_s": float(row["duration_s"]),
            })

    stretches = defaultdict(list)
    for (session_id, gazer_id), rows in channels.items():
        rows.sort(key=lambda r: r["start_frame"])
        run = []
        for row in rows:
            contiguous = run and run[-1]["start_frame"] + run[-1]["duration_frames"] == row["start_frame"]
            if not (contiguous and run[-1]["role"] == row["role"]):
                if run:
                    stretches[run[0]["role"]].append(make_stretch(session_id, gazer_id, run))
                run = []

            run.append(row)

        if run:
            stretches[run[0]["role"]].append(make_stretch(session_id, gazer_id, run))

    return stretches


def make_stretch(session_id: str, gazer_id: str, run: list[dict]) -> dict:
    return {
        "role": run[0]["role"],
        "sessionId": session_id,
        "gazerId": gazer_id,
        "startFrame": run[0]["start_frame"],
        "targets": [r["target"] for r in run],
        "durations": [round(r["duration_s"], 4) for r in run],
    }


def sample(pool: list[dict], count: int) -> list[dict]:
    """Evenly spaced picks from the duration-sorted pool - deterministic, and
    the sample's duration quantiles equal the pool's by construction."""
    ordered = sorted(pool, key=lambda s: (sum(s["durations"]), s["sessionId"], s["gazerId"], s["startFrame"]))
    if len(ordered) <= count:
        return ordered

    return [ordered[round(i * (len(ordered) - 1) / (count - 1))] for i in range(count)]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--per-role", type=int, default=SAMPLE_PER_ROLE)
    args = parser.parse_args()

    pools = load_stretches()
    sampled = {role: sample(pools[role], args.per_role) for role in ROLES}

    document = {
        "meta": {
            "generator": "Tools/build_holding_sequences.py",
            "corpus": "Research/corpora/gaze_events/gaze_events_md6.csv",
            "holdingThresholdFrames": HOLDING_THRESHOLD_FRAMES,
            "corpusStretchTotals": {role: len(pools[role]) for role in ROLES},
            "sampledPerRole": args.per_role,
            "samplingRule": "duration-sorted pool, evenly spaced indices",
            "note": "meta is provenance for humans; the C# parser ignores it",
        },
        "stretches": [stretch for role in ROLES for stretch in sampled[role]],
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    shown = args.output.relative_to(REPO) if args.output.is_relative_to(REPO) else args.output
    print(f"wrote {shown}")


if __name__ == "__main__":
    main()
