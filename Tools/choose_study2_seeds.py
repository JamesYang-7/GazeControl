"""Choose one seed per study 2 clip by a rule fixed in advance.

The Proposed condition draws each event's prototype as ``FNV-1a(seed, eventIndex)``
over the pool of the event's own class, and every study 2 clip has exactly one
event, index 0 — so one seed draws the *same* prototype in every turn-taking
clip, and leaving the seeds equal would test one prototype six times. The rule:

1. **Coverage.** Each turn-taking prototype plays in exactly one clip, assigned
   in figure order over clip order. Each interruption clip plays an interruption
   prototype that aims an agent at the listener (the stimulus the seat adds),
   the first such prototype in pool order not yet used.
2. **Non-degeneracy.** Among the seeds that draw the assigned prototype, take
   the lowest whose Proposed and RoleConditioned bakes are not degenerate: no
   agent averting more than ``--max-avert`` of the clip, none looking at the
   participant never or more than ``--max-user``. (The holding-replay substrate
   is what varies between such seeds, and it is where most participant-directed
   gaze comes from.) If no seed drawing that prototype passes, the prototype is
   offered to the next clip and this clip takes the next prototype in order.
3. **Preview** is by eye and outside this script: watch the seven and reject
   only for a visible artefact, recording the rejection and the seed taken.

Everything is derived: the pools and each prototype's listener-directed frames
from ``GazePatterns.g.cs``, each clip's event class from its ``segment.json``,
and the bake statistics from the tracks via ``summarize_gaze_tracks``. Writes
``Config/study2_seeds.json`` unless ``--dry-run``.

    uv run python Tools/choose_study2_seeds.py
    uv run python Tools/choose_study2_seeds.py --seeds 1-12 --max-avert 0.7 --max-user 0.6
    uv run python Tools/choose_study2_seeds.py --exclude study2_c6
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from summarize_gaze_tracks import CONDITIONS, SEGMENT_ROOT, summarize  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
PATTERNS = REPO / "Assets" / "GazeControl" / "Scripts" / "Runtime" / "Gaze" / "Policies" / "GazePatterns.g.cs"
OUTPUT = REPO / "Config" / "study2_seeds.json"

# The classes the segments name and the pools GazePatterns declares for them.
CLASS_POOL = {"turn-taking": "TurnTakingPool", "interruption": "InterruptionPool", "overlapping": "OverlappingPool"}
CHECKED_CONDITIONS = ("Proposed", "RoleConditioned")  # SpeakerFollowing is seed-independent


def fnv1a(seed: int, event_index: int) -> int:
    """ProposedGazePolicy.Mix, bit for bit."""
    h = 2166136261
    for shift in range(0, 32, 8):
        h = ((h ^ ((seed >> shift) & 0xFF)) * 16777619) & 0xFFFFFFFF
        h = ((h ^ ((event_index >> shift) & 0xFF)) * 16777619) & 0xFFFFFFFF
    return h


def read_patterns(path: Path) -> tuple[dict[str, list[str]], dict[str, int]]:
    """(pool name -> prototype names in order, prototype -> agent frames aimed at the listener)."""
    text = path.read_text(encoding="utf-8")
    pools = {}
    for name, body in re.findall(r"IReadOnlyList<GazePattern> (\w+Pool) = new\[\]\s*\{([^}]*)\}", text):
        pools[name] = re.findall(r"Fig\d[a-e]", body)
    listener_frames = {}
    blocks = re.split(r"public static readonly GazePattern ", text)[1:]
    for block in blocks:
        name = block.split()[0]
        frames = 0
        for track in ("CurrentSpeakerTrack", "NextSpeakerTrack"):
            m = re.search(track + r" = new\[\]\s*\{([^}]*)\}", block)
            if m:
                frames += sum(int(n) for n in re.findall(r"\((\d+) \* Frame, GazeRole\.Listener\)", m.group(1)))
        listener_frames[name] = frames
    return pools, listener_frames


def find_pattern_file() -> Path:
    if PATTERNS.exists():
        return PATTERNS
    hits = list((REPO / "Assets").rglob("GazePatterns.g.cs"))
    if not hits:
        sys.exit("GazePatterns.g.cs not found under Assets/")
    return hits[0]


def bake_stats(clip: str, duration: float) -> dict[int, dict[str, dict]]:
    """seed -> condition -> summarize() result, for the tracks that exist."""
    rows: dict[int, dict[str, dict]] = {}
    for path in (SEGMENT_ROOT / clip).glob("gazetrack_*.json"):
        info = summarize(path, duration)
        rows.setdefault(info["seed"], {})[info["condition"]] = info
    return rows


def degeneracy(stats: dict[str, dict] | None, max_avert: float, max_user: float) -> list[str]:
    """Why this seed's bakes are degenerate; empty when they pass."""
    if stats is None:
        return ["no bakes"]
    reasons = []
    for condition in CHECKED_CONDITIONS:
        info = stats.get(condition)
        if info is None:
            reasons.append(f"{condition} not baked")
            continue
        for agent, a in info["agents"].items():
            if not a["complete"]:
                reasons.append(f"{condition} {agent} track short")
            if a["avert"] > max_avert:
                reasons.append(f"{condition} {agent} averts {a['avert']:.0%}")
            if a["user"] == 0:
                reasons.append(f"{condition} {agent} never looks at the participant")
            elif a["user"] > max_user:
                reasons.append(f"{condition} {agent} looks at the participant {a['user']:.0%}")
    return reasons


def parse_seed_range(text: str) -> list[int]:
    lo, _, hi = text.partition("-")
    return list(range(int(lo), int(hi or lo) + 1))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--seeds", default="1-12", help="seed range to choose from (baked seeds), e.g. 1-12")
    parser.add_argument("--max-avert", type=float, default=0.7, help="an agent averting more than this share is degenerate")
    parser.add_argument("--max-user", type=float, default=0.6, help="an agent looking at the participant more than this is degenerate")
    parser.add_argument("--exclude", nargs="*", default=[], metavar="CLIP",
                        help="clips to leave out of the session (e.g. study2_c6); the rule runs over the rest")
    parser.add_argument("--dry-run", action="store_true", help="print the choice without writing Config/study2_seeds.json")
    args = parser.parse_args(argv)
    seeds = parse_seed_range(args.seeds)

    pools, listener_frames = read_patterns(find_pattern_file())
    clips = sorted(p.name for p in SEGMENT_ROOT.glob("study2_c*") if p.is_dir())
    unknown = set(args.exclude) - set(clips)
    if unknown:
        sys.exit(f"--exclude names clips that do not exist: {sorted(unknown)}")
    clips = [c for c in clips if c not in args.exclude]
    if not clips:
        sys.exit(f"no study2_c* segments under {SEGMENT_ROOT}")

    # Which prototype each seed draws for event 0, per pool.
    draws = {pool: {seed: names[fnv1a(seed, 0) % len(names)] for seed in seeds} for pool, names in pools.items()}

    # Prototype queue per class: turn-taking in figure (pool) order; interruption
    # restricted to listener-directed prototypes, in pool order.
    queues = {
        "turn-taking": list(pools["TurnTakingPool"]),
        "interruption": [p for p in pools["InterruptionPool"] if listener_frames[p] > 0],
        "overlapping": list(pools["OverlappingPool"]),
    }

    chosen = []
    log = []
    for clip in clips:
        segment = json.loads((SEGMENT_ROOT / clip / "segment.json").read_text(encoding="utf-8"))
        events = segment.get("events") or []
        if len(events) != 1:
            sys.exit(f"{clip}: {len(events)} events; the rule assumes exactly one (index 0)")
        cls = events[0]["eotTypeName"]
        pool = CLASS_POOL[cls]
        stats = bake_stats(clip, segment["durationSeconds"])
        queue = queues[cls]
        if not queue:
            sys.exit(f"{clip}: no {cls} prototype left to assign — more clips of this class than the rule covers")

        pick = None
        deferred = []
        while queue and pick is None:
            prototype = queue.pop(0)
            candidates = [s for s in seeds if draws[pool][s] == prototype]
            rejected = {}
            for seed in candidates:
                reasons = degeneracy(stats.get(seed), args.max_avert, args.max_user)
                if reasons:
                    rejected[seed] = reasons
                    continue
                pick = (seed, prototype, candidates, rejected)
                break
            if pick is None:
                log.append(f"{clip}: {prototype} has no non-degenerate seed among {candidates} "
                           f"({'; '.join(f'seed {s}: {', '.join(r)}' for s, r in rejected.items()) or 'none draw it'}); "
                           "offered to the next clip")
                deferred.append(prototype)
        queue[0:0] = deferred  # the skipped prototypes go back to the front for later clips
        if pick is None:
            sys.exit(f"{clip}: no assignable prototype")

        seed, prototype, candidates, rejected = pick
        for s, r in rejected.items():
            log.append(f"{clip}: seed {s} ({prototype}) rejected: {', '.join(r)}")
        chosen.append({
            "clip": clip, "class": cls, "seed": seed, "prototype": prototype,
            "listenerDirectedFrames": listener_frames[prototype],
            "seedsDrawingIt": candidates,
            "proposed": {n: {k: round(v, 3) for k, v in a.items() if k != "n"}
                         for n, a in stats[seed]["Proposed"]["agents"].items()},
        })

    print(f"{'clip':10s} {'class':13s} {'seed':>4s}  {'prototype':10s} {'listener frames':>15s}  seeds drawing it")
    for c in chosen:
        print(f"{c['clip']:10s} {c['class']:13s} {c['seed']:4d}  {c['prototype']:10s} {c['listenerDirectedFrames']:15d}  "
              f"{', '.join(map(str, c['seedsDrawingIt']))}")
    print("\nSeeds in clip order (StudySessionRunner.Seeds):", [c["seed"] for c in chosen])
    if log:
        print("\nrule applications:")
        for line in log:
            print("  " + line)

    if not args.dry_run:
        OUTPUT.parent.mkdir(exist_ok=True)
        record = {
            "rule": __doc__.split("\n\n")[1].strip(),
            "seedsConsidered": seeds,
            "excludedClips": args.exclude,
            "maxAvert": args.max_avert, "maxUser": args.max_user,
            "drawsPerSeed": {pool: {str(s): p for s, p in d.items()} for pool, d in draws.items()},
            "chosen": chosen,
            "log": log,
        }
        OUTPUT.write_text(json.dumps(record, indent=2), encoding="utf-8")
        print(f"\nwritten {OUTPUT.relative_to(REPO)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
