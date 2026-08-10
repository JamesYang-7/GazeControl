"""Find TalkingWithHands segments that can carry a case-1 demo, and export one.

The case-1 demo (agent A and agent B taking turns while the user watches) is now
played from the corpus rather than synthesised: both agents speak the dataset's
own audio and move on its own mocap. This script picks the stretch of a
conversation that gets played.

A segment is usable only if all of the following hold:

* **20-30 s long** -- the video length asked for.
* **At least two end-of-turn events**, each entirely inside the segment with
  room to spare: an event's pre-turn window has to open after the segment has
  started, or the pattern is cut off at the top of the video.
* **Sentences stay whole.** Both boundaries land on an utterance boundary and
  no utterance of *either* speaker straddles them, so the clip never opens or
  closes mid-word. Utterances are rebuilt exactly as ``EoT_TWH`` built them --
  split the word-level TSV wherever the gap between consecutive words exceeds
  0.3 s -- so the segment boundaries agree with the events by construction.
* **Events are separated.** Consecutive turn instants must be at least 2 s
  apart. The corpus is dense (a 25 s window routinely holds 5-8 events) and a
  gaze prototype occupies the second before its event, so events any closer
  would have their patterns cut short by the next one.

Everything that survives is then ranked, because thousands of windows survive.
The score rewards what makes a demo readable rather than what makes it typical:
few, well-spaced events, both agents actually taking turns, and at least one
plain turn-taking (rather than interruption or overlap) boundary.

Usage::

    python Tools/find_demo_segments.py                      # rank and report
    python Tools/find_demo_segments.py --top 40 --csv out.csv
    python Tools/find_demo_segments.py --export 1           # export the best one
    python Tools/find_demo_segments.py --export 1 --name case2

Exporting writes ``Assets/DemoSegments/<name>/``: the two trimmed wavs (one per
speaker, sample-identical in length) and ``segment.json``, which carries the
turn schedule and the EoT events with times measured from the segment start.
Motion is *not* copied -- the JSON names the 60 fps grounded npz and the frame
to start it at, so the multi-minute recordings stay where they are.

The exported pair is peak-normalised. The corpus is recorded far below full
scale -- one checked segment peaks at -42 dBFS -- and the demo's voice-activity
detector thresholds absolute RMS, so the raw levels leave it detecting silence
from end to end. Both sides get the *same* gain, taken from the louder of the
two, because the balance between the two speakers is recorded rather than
incidental.
"""

from __future__ import annotations

import argparse
import ast
import csv
import json
import struct
import sys
import wave
import zipfile
from array import array
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DATASET = Path(r"F:\Data\TalkingWithHandsCentered")

# Speaker codes as EoT_TWH writes them (see its _twh_meta.json).
SIDE_OF_CODE = {1: "main-agent", 2: "interloctr"}
EOT_TYPE_NAMES = {1: "interruption", 2: "overlapping", 3: "turn-taking"}

# The pause that separates two utterances, from the EoT builder's own choice.
# Reproduced rather than re-tuned: segment boundaries have to agree with the
# event times, and both are derived from this split.
PAUSE_THRESHOLD = 0.3

MOTION_FPS = 60

# Selection thresholds. Defaults are the ones the demo brief asks for; every one
# is exposed on the command line so a stricter or looser search is one flag away.
MIN_DURATION = 20.0
MAX_DURATION = 30.0
MIN_EVENTS = 2
# An event's pattern window opens 1 s before its turn instant; the lead is that
# window plus a moment of settled conversation before it.
LEAD_SECONDS = 1.5
TAIL_SECONDS = 0.5
MIN_EVENT_SEPARATION = 2.0

# Peak the exported pair is normalised to, leaving headroom for the two voices
# summing during overlaps.
PEAK_TARGET = 0.7


@dataclass
class Utterance:
    speaker: int
    start: float
    end: float
    text: str


@dataclass
class Event:
    eot_type: int
    first_speaker: int
    second_speaker: int
    turn_time: float
    start: float
    end: float


@dataclass
class Segment:
    stem: str
    start: float
    end: float
    events: list[Event]
    utterances: list[Utterance]
    score: float = 0.0
    reasons: dict[str, float] = field(default_factory=dict)

    @property
    def duration(self) -> float:
        return self.end - self.start


def read_npy_shape(archive: zipfile.ZipFile, name: str) -> tuple[int, ...]:
    """Shape of one array in an .npz, without reading its data (no numpy here)."""
    with archive.open(name) as handle:
        magic = handle.read(6)
        if magic != b"\x93NUMPY":
            raise ValueError(f"{name} is not a .npy array")
        major = handle.read(1)[0]
        handle.read(1)
        width = 2 if major == 1 else 4
        header_length = struct.unpack("<H" if major == 1 else "<I", handle.read(width))[0]
        header = ast.literal_eval(handle.read(header_length).decode("latin1").strip())
    return tuple(header["shape"])


def motion_frame_count(stem: str, side: str) -> int:
    path = DATASET / "SMPLX-60fps-grounded" / f"{stem}_{side}.npz"
    with zipfile.ZipFile(path) as archive:
        return read_npy_shape(archive, "poses.npy")[0]


def load_utterances(stem: str, speaker: int) -> list[Utterance]:
    """Word-level TSV -> utterances, split on gaps longer than the threshold.

    Backchannels are *not* dropped here, unlike in the event builder. They are
    speech: a boundary that cuts through one still sounds like a cut.
    """
    side = SIDE_OF_CODE[speaker]
    path = DATASET / "talkingwithHands-Audio" / side / "tsv" / f"{stem}_{side}.tsv"

    words: list[tuple[float, float, str]] = []
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 3:
                continue
            words.append((float(parts[0]), float(parts[1]), parts[2]))

    utterances: list[Utterance] = []
    current: Utterance | None = None
    for start, end, word in words:
        if current is None or start - current.end > PAUSE_THRESHOLD:
            if current is not None:
                utterances.append(current)
            current = Utterance(speaker, start, end, word)
        else:
            current.end = end
            current.text += " " + word
    if current is not None:
        utterances.append(current)

    return utterances


def load_events(stem: str) -> list[Event]:
    path = DATASET / "EoT_TWH" / f"{stem}_eot.csv"
    events: list[Event] = []
    with path.open(encoding="utf-8") as handle:
        for row in csv.DictReader(handle):
            events.append(Event(
                eot_type=int(row["eot_type"]),
                first_speaker=int(row["first_speaker"]),
                second_speaker=int(row["second_speaker"]),
                turn_time=int(row["turn_time"]) / 1000.0,
                start=int(row["eot_start"]) / 1000.0,
                end=int(row["eot_end"]) / 1000.0,
            ))
    events.sort(key=lambda e: e.turn_time)
    return events


def stems() -> list[str]:
    return sorted(p.name[: -len("_eot.csv")] for p in (DATASET / "EoT_TWH").glob("*_eot.csv"))


def snap(seconds: float) -> float:
    """Round to a motion frame, so audio and mocap start on the same instant."""
    return round(seconds * MOTION_FPS) / MOTION_FPS


def candidates(stem: str, args: argparse.Namespace) -> list[Segment]:
    utterances = load_utterances(stem, 1) + load_utterances(stem, 2)
    utterances.sort(key=lambda u: u.start)
    events = load_events(stem)
    if len(events) < args.min_events:
        return []

    # The mocap is the shorter of the two media in a handful of files; a segment
    # that runs past it would freeze on the last pose.
    motion_seconds = min(motion_frame_count(stem, side) for side in SIDE_OF_CODE.values()) / MOTION_FPS

    # Boundaries are snapped to a motion frame so the trimmed audio and the
    # mocap start on the same instant; the shift is under 9 ms and always lands
    # inside the silence around an utterance boundary.
    starts = sorted({snap(u.start) for u in utterances})
    ends = sorted({snap(u.end) for u in utterances})

    found: list[Segment] = []
    for t0 in starts:
        for t1 in ends:
            duration = t1 - t0
            if duration > args.max_duration:
                break  # ends are sorted, so every later one is longer still
            if duration < args.min_duration:
                continue
            if t1 > motion_seconds:
                continue
            if any(u.start < t0 < u.end or u.start < t1 < u.end for u in utterances):
                continue

            inside = [e for e in events
                      if e.start >= t0 + args.lead and e.end <= t1 - args.tail]
            if len(inside) < args.min_events:
                continue
            if min_separation(inside) < args.min_separation:
                continue

            found.append(Segment(
                stem=stem,
                start=t0,
                end=t1,
                events=inside,
                utterances=[u for u in utterances if u.start >= t0 and u.end <= t1],
            ))

    return found


def min_separation(events: list[Event]) -> float:
    if len(events) < 2:
        return float("inf")
    return min(b.turn_time - a.turn_time for a, b in zip(events, events[1:]))


def score(segment: Segment) -> Segment:
    """Rank by demo readability, not by how typical the segment is.

    Each term is bounded so no single one can dominate, and every term is kept
    on the segment so a ranking can be argued with rather than just trusted.
    """
    events = segment.events

    # Separation: the pattern before an event needs the second before it clear.
    separation = min(min_separation(events), 8.0) / 8.0

    # Event count: two to four boundaries read as a conversation; a dozen reads
    # as a scramble and leaves no frame free of a pattern.
    count = len(events)
    density = 1.0 if 2 <= count <= 4 else max(0.0, 1.0 - 0.2 * (count - 4))

    # Both agents should take the floor, or the "turn-taking" demo is one agent
    # talking at another.
    speakers = {e.first_speaker for e in events}
    alternation = 1.0 if len(speakers) > 1 else 0.0

    # Case 1 is about turn-taking; interruptions and overlaps are welcome as
    # texture but a segment with no clean hand-over is the wrong demo.
    turn_takings = sum(1 for e in events if e.eot_type == 3)
    cleanliness = min(turn_takings, 2) / 2.0

    # Speech should be shared. Silence is fine; one speaker holding 90% of it
    # is not.
    talk = {1: 0.0, 2: 0.0}
    for u in segment.utterances:
        talk[u.speaker] += u.end - u.start
    total = talk[1] + talk[2]
    balance = 0.0 if total <= 0 else 2.0 * min(talk[1], talk[2]) / total

    # Long enough to breathe, short enough to watch.
    length = 1.0 - abs(segment.duration - 25.0) / 5.0

    segment.reasons = {
        "separation": separation,
        "density": density,
        "alternation": alternation,
        "cleanliness": cleanliness,
        "balance": balance,
        "length": max(0.0, length),
        "min_sep_s": min_separation(events),
        "events": float(count),
        "turn_takings": float(turn_takings),
        "talk_ratio": balance,
    }
    segment.score = (
        3.0 * separation
        + 2.0 * density
        + 2.0 * alternation
        + 2.0 * cleanliness
        + 1.5 * balance
        + 1.0 * max(0.0, length)
    )
    return segment


def best_per_stem(found: list[Segment], per_stem: int) -> list[Segment]:
    """Keep the top few non-overlapping segments of each conversation.

    Neighbouring windows of one conversation differ by a word and would
    otherwise fill the whole ranking with the same twenty seconds.
    """
    kept: list[Segment] = []
    by_stem: dict[str, list[Segment]] = {}
    for segment in found:
        by_stem.setdefault(segment.stem, []).append(segment)

    for group in by_stem.values():
        group.sort(key=lambda s: -s.score)
        chosen: list[Segment] = []
        for segment in group:
            if any(segment.start < c.end and c.start < segment.end for c in chosen):
                continue
            chosen.append(segment)
            if len(chosen) >= per_stem:
                break
        kept.extend(chosen)

    kept.sort(key=lambda s: -s.score)
    return kept


def describe(segment: Segment, rank: int) -> str:
    types = ",".join(EOT_TYPE_NAMES[e.eot_type][0] + str(e.first_speaker) for e in segment.events)
    return (
        f"{rank:>3}  {segment.stem}  {segment.start:7.2f}-{segment.end:7.2f}s "
        f"({segment.duration:5.2f}s)  score {segment.score:5.2f}  "
        f"events {len(segment.events)} [{types}]  "
        f"min-sep {segment.reasons['min_sep_s']:4.1f}s  "
        f"balance {segment.reasons['balance']:.2f}"
    )


def inspect(segment: Segment) -> None:
    """Print the segment as a timeline, so a candidate can be judged by eye.

    Utterances are shown with the speaker who produced them and the events are
    interleaved at their turn instants, which is the only practical way to see
    whether the transcript actually reads as a hand-over rather than as two
    people talking past each other.
    """
    print(f"\n{segment.stem}  {segment.start:.2f}-{segment.end:.2f} s  ({segment.duration:.2f} s)")
    print(f"  agent A = main-agent (speaker 1), agent B = interloctr (speaker 2)\n")

    marks = [(u.start, "utterance", u) for u in segment.utterances]
    marks += [(e.turn_time, "event", e) for e in segment.events]
    marks.sort(key=lambda m: m[0])

    for when, kind, item in marks:
        offset = when - segment.start
        if kind == "utterance":
            who = "A" if item.speaker == 1 else "B"
            print(f"  {offset:6.2f}  {who}  {item.text}")
        else:
            print(f"  {offset:6.2f}  --- EoT {EOT_TYPE_NAMES[item.eot_type]}: "
                  f"{'AB'[item.first_speaker - 1]} -> {'AB'[item.second_speaker - 1]} ---")


def build_turns(segment: Segment) -> list[dict]:
    """The turn schedule a gaze policy reads, derived from the events.

    Each event contributes the turn that *ends* at it: ``first_speaker`` holds
    the floor going into the boundary and ``second_speaker`` takes it. A turn
    starts at the previous event's instant, and a final turn runs from the last
    event to the end of the segment with no boundary of its own -- nothing is
    known about what happens after the clip, so no pattern fires there.

    Consecutive events need not chain (one speaker can be ``first_speaker``
    twice running, the corpus does not guarantee otherwise), so each turn's
    roles come from its own event rather than from the previous turn.
    """
    turns = []
    previous = 0.0
    for index, event in enumerate(segment.events):
        turns.append({
            "speaker": event.first_speaker,
            "addressee": event.second_speaker,
            "startTime": round(previous, 4),
            "endTime": round(event.turn_time - segment.start, 4),
            "eventIndex": index,
        })
        previous = event.turn_time - segment.start

    last = segment.events[-1]
    turns.append({
        "speaker": last.second_speaker,
        "addressee": last.first_speaker,
        "startTime": round(previous, 4),
        "endTime": round(segment.duration, 4),
        "eventIndex": -1,
    })
    return turns


def read_trim(source: Path, start: float, end: float) -> tuple[array, dict]:
    """The segment's samples, plus the wav parameters needed to write them back."""
    with wave.open(str(source), "rb") as reader:
        if reader.getsampwidth() != 2:
            raise ValueError(f"{source.name} is not 16-bit; the exporter only handles 16-bit PCM")

        rate = reader.getframerate()
        first = int(round(start * rate))
        reader.setpos(first)
        raw = reader.readframes(int(round(end * rate)) - first)

        params = {
            "channels": reader.getnchannels(),
            "width": reader.getsampwidth(),
            "rate": rate,
        }

    samples = array("h")
    samples.frombytes(raw)
    if sys.byteorder == "big":
        samples.byteswap()

    return samples, params


def write_wav(target: Path, samples: array, params: dict, gain: float) -> int:
    scaled = array("h", (clamp16(value * gain) for value in samples))
    if sys.byteorder == "big":
        scaled.byteswap()

    with wave.open(str(target), "wb") as writer:
        writer.setnchannels(params["channels"])
        writer.setsampwidth(params["width"])
        writer.setframerate(params["rate"])
        writer.writeframes(scaled.tobytes())

    return len(scaled) // params["channels"]


def clamp16(value: float) -> int:
    return max(-32768, min(32767, int(round(value))))


def export(segment: Segment, name: str) -> Path:
    directory = REPO / "Assets" / "DemoSegments" / name
    directory.mkdir(parents=True, exist_ok=True)

    start_frame = int(round(segment.start * MOTION_FPS))
    frame_count = int(round(segment.duration * MOTION_FPS))

    # The corpus is recorded very quietly — this segment peaks at -42 dBFS — and
    # the demo's voice-activity detector works on absolute RMS, so without a gain
    # nobody is ever detected as speaking and the speaker-following baseline sits
    # on its fallback target forever. One gain for both sides, from the louder of
    # the two: per-track normalisation would rewrite the balance between the two
    # speakers, which is recorded rather than incidental.
    trims = {}
    for code, side in sorted(SIDE_OF_CODE.items()):
        trims[code] = read_trim(
            DATASET / "talkingwithHands-Audio" / side / "wav" / f"{segment.stem}_{side}.wav",
            segment.start, segment.end)

    peak = max(max(abs(v) for v in samples) for samples, _ in trims.values()) / 32768.0
    gain = PEAK_TARGET / peak if peak > 0 else 1.0
    if gain > 200:
        print(f"  warning: {gain:.0f}x gain to reach peak {PEAK_TARGET} — the source may be near-silent")

    agents = []
    for code, side in sorted(SIDE_OF_CODE.items()):
        wav_name = f"{side}.wav"
        samples, params = trims[code]
        written = write_wav(directory / wav_name, samples, params, gain)
        agents.append({
            "speaker": code,
            "side": side,
            "audio": f"Assets/DemoSegments/{name}/{wav_name}",
            "motion": str(DATASET / "SMPLX-60fps-grounded" / f"{segment.stem}_{side}.npz"),
            "audioSamples": written,
        })

    document = {
        "schema": "gazecontrol.demo-segment/1",
        "name": name,
        "stem": segment.stem,
        "sourceStartSeconds": round(segment.start, 4),
        "sourceEndSeconds": round(segment.end, 4),
        "durationSeconds": round(segment.duration, 4),
        "motionFrameRate": MOTION_FPS,
        "motionStartFrame": start_frame,
        "motionFrameCount": frame_count,
        "audioGain": round(gain, 3),
        "audioPeakBeforeGain": round(peak, 5),
        "agents": agents,
        "turns": build_turns(segment),
        "events": [
            {
                "index": index,
                "eotType": event.eot_type,
                "eotTypeName": EOT_TYPE_NAMES[event.eot_type],
                "firstSpeaker": event.first_speaker,
                "secondSpeaker": event.second_speaker,
                "turnTime": round(event.turn_time - segment.start, 4),
                "startTime": round(event.start - segment.start, 4),
                "endTime": round(event.end - segment.start, 4),
            }
            for index, event in enumerate(segment.events)
        ],
        "utterances": [
            {
                "speaker": u.speaker,
                "startTime": round(u.start - segment.start, 4),
                "endTime": round(u.end - segment.start, 4),
                "text": u.text,
            }
            for u in segment.utterances
        ],
        "selection": {
            "score": round(segment.score, 4),
            "generator": "Tools/find_demo_segments.py",
            **{k: round(v, 4) for k, v in segment.reasons.items()},
        },
    }

    path = directory / "segment.json"
    path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return path


def main() -> int:
    global DATASET

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dataset", type=Path, default=DATASET, help="TalkingWithHandsCentered root")
    parser.add_argument("--min-duration", type=float, default=MIN_DURATION)
    parser.add_argument("--max-duration", type=float, default=MAX_DURATION)
    parser.add_argument("--min-events", type=int, default=MIN_EVENTS)
    parser.add_argument("--lead", type=float, default=LEAD_SECONDS,
                        help="clear seconds required before the first event's window")
    parser.add_argument("--tail", type=float, default=TAIL_SECONDS,
                        help="clear seconds required after the last event")
    parser.add_argument("--min-separation", type=float, default=MIN_EVENT_SEPARATION,
                        help="minimum seconds between consecutive turn instants")
    parser.add_argument("--per-stem", type=int, default=2,
                        help="how many non-overlapping segments to keep per conversation")
    parser.add_argument("--top", type=int, default=20, help="how many to report")
    parser.add_argument("--stem", help="restrict the search to one conversation")
    parser.add_argument("--csv", type=Path, help="write the full ranking here")
    parser.add_argument("--inspect", type=int, metavar="RANK",
                        help="print the transcript and events of the segment at this rank")
    parser.add_argument("--export", type=int, metavar="RANK",
                        help="export the segment at this rank (1-based) to Assets/DemoSegments")
    parser.add_argument("--name", help="export folder name; defaults to the segment id")
    args = parser.parse_args()
    DATASET = args.dataset

    pool = [args.stem] if args.stem else stems()
    found: list[Segment] = []
    for stem in pool:
        found.extend(score(s) for s in candidates(stem, args))

    if not found:
        print("no segment satisfies the constraints; loosen --min-separation or --min-events")
        return 1

    ranked = best_per_stem(found, args.per_stem)
    print(f"{len(found)} windows over {len(pool)} conversations, "
          f"{len(ranked)} kept after per-conversation de-overlap\n")
    for rank, segment in enumerate(ranked[: args.top], start=1):
        print(describe(segment, rank))

    if args.csv:
        with args.csv.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            writer.writerow(["rank", "stem", "start_s", "end_s", "duration_s", "score",
                             "n_events", "min_sep_s", "turn_takings", "balance", "event_types"])
            for rank, segment in enumerate(ranked, start=1):
                writer.writerow([
                    rank, segment.stem, f"{segment.start:.3f}", f"{segment.end:.3f}",
                    f"{segment.duration:.3f}", f"{segment.score:.4f}", len(segment.events),
                    f"{segment.reasons['min_sep_s']:.3f}", int(segment.reasons["turn_takings"]),
                    f"{segment.reasons['balance']:.3f}",
                    "".join(str(e.eot_type) for e in segment.events),
                ])
        print(f"\nwrote {args.csv}")

    if args.inspect:
        if args.inspect > len(ranked):
            print(f"\nrank {args.inspect} does not exist ({len(ranked)} segments)")
            return 1
        inspect(ranked[args.inspect - 1])

    if args.export:
        if args.export > len(ranked):
            print(f"\nrank {args.export} does not exist ({len(ranked)} segments)")
            return 1
        chosen = ranked[args.export - 1]
        name = args.name or f"{chosen.stem}_{int(chosen.start):03d}s"
        path = export(chosen, name)
        print(f"\nexported rank {args.export} -> {path.relative_to(REPO)}")
        print(f"  {chosen.stem} {chosen.start:.2f}-{chosen.end:.2f} s, "
              f"{len(chosen.events)} events, {chosen.duration:.2f} s")

    return 0


if __name__ == "__main__":
    sys.exit(main())
