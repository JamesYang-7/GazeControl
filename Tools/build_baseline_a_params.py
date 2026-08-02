"""Build Baseline A's parameter file from the local gaze corpus.

Baseline A is the gaze model of

    Shintani T, Ishi CT, Ishiguro H. Gaze modeling in multi-party dialogues and
    extraversion expression through gaze aversion control. Advanced Robotics
    38(19-20):1470-1485, 2024.

re-estimated on our own three-party end-of-turn corpus rather than on their
published tables. Everything this script reads lives under ``Research/corpora``,
which is git-ignored; the JSON it writes is committed, so the condition is
reproducible without the corpus. Regenerate with:

    python Tools/build_baseline_a_params.py

Three departures from the paper, each forced by what the corpus actually shows
(see the corpus READMEs and PROGRESS.md for the evidence):

* Durations are lognormal, not chi-square. Chi-square needs ``dn > 2`` for an
  interior mode and 0 of 45 role x target x min_dwell cells reach it, so the
  paper's fit degenerates toward exponential here.
* Target selection uses the corpus transition matrix's jump chain, not i.i.d.
  draws from ``S``. Sampling i.i.d. would put person->person shifts at 0.35
  where we measure 0.10.
* The aversion re-target interval is sampled from an empirical quantile table
  rather than a fitted law. Direction segments are clipped by the ends of their
  aversion, so a sixth of them fall below the corpus's 0.1 s fixation floor and
  the tail reaches 16 s; no two-parameter law covers both ends usefully.
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import statistics
import zipfile
import struct
import ast
import io
from collections import defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CORPORA = REPO / "Research" / "corpora"
OUTPUT = REPO / "Assets" / "GazeControl" / "Resources" / "BaselineAParameters.json"

# Corpus state order, matching `states` in transitions_from_events.npz and the
# GazeTargetRole enum on the C# side.
STATES = ["sp", "ad", "sd", "aversion"]
ROLES = ["sp", "ad", "sd"]
TURN_STATES = ["changing", "holding"]
# Shintani's L categories, in the order the C# AversionDirection enum declares.
DIRECTIONS = ["lu", "u", "ru", "l", "f", "r", "ld", "d", "rd"]

# The fixation min-dwell filter the corpus was built with (6 frames @ 60 fps).
# It is the lower support of every duration in the corpus, so it is also the
# floor the samplers resample against.
MIN_DWELL_SECONDS = 0.1
# Long fixations exist but the tail is thin; cap so one draw cannot freeze an
# agent's gaze for the length of a turn. Above the p99 of every cell.
MAX_DWELL_SECONDS = 8.0
# Corpus `turn_state` is `changing` within +/-1 s of the nearest turn instant.
TURN_WINDOW_SECONDS = 1.0
# Quantile resolution of the empirical re-target interval table, and the top
# quantile it spans. The last 1% of segments run from 2.4 s to 16.6 s; letting
# the table reach the maximum would smear that whole range over its top bin.
QUANTILE_STEPS = 41
QUANTILE_MAX = 0.99


def read_npy(raw: bytes):
    """Minimal .npy reader - numpy is not installed in this project's Python."""
    f = io.BytesIO(raw)
    assert f.read(6) == b"\x93NUMPY"
    major = f.read(1)[0]
    f.read(1)
    header_length = struct.unpack("<H" if major == 1 else "<I", f.read(2 if major == 1 else 4))[0]
    header = ast.literal_eval(f.read(header_length).decode("latin1").strip())
    count = 1
    for dimension in header["shape"]:
        count *= dimension

    descr = header["descr"]
    if descr in ("<f8", "|f8"):
        values = list(struct.unpack(f"<{count}d", f.read(8 * count)))
    elif descr.startswith("<U"):
        width = int(descr[2:])
        blob = f.read(4 * width * count)
        values = [blob[i * 4 * width:(i + 1) * 4 * width].decode("utf-32-le").rstrip("\x00") for i in range(count)]
    else:
        raise ValueError(f"unsupported dtype {descr}")

    return header["shape"], values


def load_transitions() -> list[dict]:
    """Per-frame transition matrix -> jump chain (off-diagonals, renormalised).

    The stored matrix is per-frame at 60 Hz, so its diagonal is ~0.98 and its
    implied dwells are geometric. Baseline A is semi-Markov instead: the jump
    chain says where gaze goes next, a separate duration model says when.
    """
    with zipfile.ZipFile(CORPORA / "shintani_model" / "transitions_from_events.npz") as archive:
        shape, flat = read_npy(archive.read("P.npy"))
        _, states = read_npy(archive.read("states.npy"))
        _, roles = read_npy(archive.read("roles.npy"))

    assert states == STATES and roles == ROLES, (states, roles)
    n_roles, n_states, _ = shape

    records = []
    for r in range(n_roles):
        for i in range(n_states):
            row = flat[(r * n_states + i) * n_states:(r * n_states + i) * n_states + n_states]
            off_diagonal = [p if j != i else 0.0 for j, p in enumerate(row)]
            total = sum(off_diagonal)
            if total <= 0.0:
                continue  # the gazer's own role: unreachable, the corpus diagonal is empty

            records.append({
                "role": roles[r],
                "from": states[i],
                "to": [p / total for p in off_diagonal],
            })

    return records


def load_target_ratios() -> list[dict]:
    """S (Eq 6): time-weighted occupancy per (role, turn_state).

    Only used to draw the very first state of a run; steady-state behaviour
    comes from the jump chain and the durations.
    """
    records = []
    with open(CORPORA / "shintani_model" / "shintani_S.csv", newline="") as handle:
        for row in csv.DictReader(handle):
            records.append({
                "role": row["gazer_role"],
                "turnState": row["turn_state"],
                "ratios": [float(row[f"S_{state}"]) for state in STATES],
            })

    return records


def load_dwells() -> list[dict]:
    """Lognormal dwell per (role, turn_state, target), fitted on log durations.

    Censored fixations are dropped (0.15% of rows, cut by session start/end).
    """
    durations = defaultdict(list)
    with open(CORPORA / "gaze_events" / "gaze_events_md6.csv", newline="") as handle:
        for row in csv.DictReader(handle):
            if row["left_censored"].lower() == "true" or row["right_censored"].lower() == "true":
                continue

            key = (row["gazer_role"], row["turn_state"], row["gaze_target"])
            durations[key].append(float(row["duration_s"]))

    records = []
    for (role, turn_state, target), values in sorted(durations.items()):
        logs = [math.log(v) for v in values]
        mu = statistics.fmean(logs)
        sigma = statistics.stdev(logs)
        records.append({
            "role": role,
            "turnState": turn_state,
            "target": target,
            "mu": round(mu, 6),
            "sigma": round(sigma, 6),
            "n": len(values),
            "empiricalMedianSeconds": round(statistics.median(values), 4),
            "empiricalMeanSeconds": round(statistics.fmean(values), 4),
        })

    return records


def load_aversion_directions() -> list[dict]:
    """L (Eq 7): 9-way eyeball-direction marginals per role."""
    records = []
    with open(CORPORA / "aversion_direction" / "aversion_L.csv", newline="") as handle:
        for row in csv.DictReader(handle):
            if row["gazer_role"] == "ALL":
                continue

            records.append({
                "role": row["gazer_role"],
                "weights": [float(row[d]) for d in DIRECTIONS],
            })

    return records


def load_aversion_conditional() -> list[dict]:
    """P(second direction | first direction), 9x9, pooled over roles."""
    records = []
    with open(CORPORA / "aversion_direction" / "aversion_L_cond.csv", newline="") as handle:
        for row in csv.DictReader(handle):
            records.append({
                "from": row["direction_1"],
                "weights": [float(row[d]) for d in DIRECTIONS],
            })

    return records


def load_aversion_geometry() -> tuple[list[dict], dict]:
    """Measured eye-in-head angle per direction bin, and the re-target interval.

    The bins are categories; the angle attached to each is the median of the
    continuous eye-tracker measurements that fell in it, so the agent's aversion
    amplitude is measured rather than guessed.
    """
    azimuths = defaultdict(list)
    elevations = defaultdict(list)
    segments = []

    with open(CORPORA / "aversion_direction" / "aversion_segments.csv", newline="") as handle:
        for row in csv.DictReader(handle):
            # Only segments that ended by re-targeting measure the re-target
            # interval. The last segment of every aversion was cut short by the
            # end of the aversion, and the generator clips its own last segment
            # the same way - including them here would apply that clipping twice.
            if int(row["seg_index"]) < int(row["n_segs_in_event"]) - 1:
                segments.append(float(row["seg_duration"]) / 60.0)

            if not row["az"] or row["az"] == "nan":
                continue  # gaze point outside the scene camera frame

            azimuths[row["direction"]].append(float(row["az"]))
            elevations[row["direction"]].append(float(row["el"]))

    angles = [{
        "direction": direction,
        # az is measured rightward-positive and el upward-positive, which is
        # already the yaw/pitch convention GazeTarget.Away expects.
        "yaw": round(statistics.median(azimuths[direction]), 3),
        "pitch": round(statistics.median(elevations[direction]), 3),
        "n": len(azimuths[direction]),
    } for direction in DIRECTIONS]

    segments.sort()
    quantiles = [
        round(segments[int(round(i / (QUANTILE_STEPS - 1) * QUANTILE_MAX * (len(segments) - 1)))], 4)
        for i in range(QUANTILE_STEPS)
    ]
    interval = {
        "quantiles": quantiles,
        "quantileMax": QUANTILE_MAX,
        "n": len(segments),
        "empiricalMeanSeconds": round(statistics.fmean(segments), 4),
        "empiricalMedianSeconds": round(statistics.median(segments), 4),
    }

    return angles, interval


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()

    angles, interval = load_aversion_geometry()
    document = {
        "meta": {
            "model": "Shintani et al. 2024, re-estimated on our three-party EoT corpus",
            "source": "Research/corpora (git-ignored); regenerate with Tools/build_baseline_a_params.py",
            "corpus": "gaze_events_md6.csv - 120,765 fixations, 38 sessions, 60 fps",
            "durationLaw": "lognormal on log-seconds; chi-square (the paper's Eq 9) has no interior mode on this data",
            "retargetLaw": "empirical quantiles of un-clipped direction segments, spanning p0..p99",
            "states": STATES,
            "directions": DIRECTIONS,
        },
        "minDwellSeconds": MIN_DWELL_SECONDS,
        "maxDwellSeconds": MAX_DWELL_SECONDS,
        "turnWindowSeconds": TURN_WINDOW_SECONDS,
        "targetRatios": load_target_ratios(),
        "transitions": load_transitions(),
        "dwells": load_dwells(),
        "aversionDirections": load_aversion_directions(),
        "aversionConditional": load_aversion_conditional(),
        "aversionAngles": angles,
        "aversionInterval": interval,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {args.output.relative_to(REPO)}")


if __name__ == "__main__":
    main()
