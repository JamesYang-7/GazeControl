"""Find 3People-2022 segments where two participants take turns and the third only listens.

The three-party corpus gives the demo something TalkingWithHands cannot: a
real listener. In a 3People session one of the three participants can sit
through a stretch of conversation without ever taking the floor, so a
segment found here has two turn-takers and one participant who only listens
-- with that participant's real gaze and motion on record. What is done with
the three (which becomes an agent, whether the listener's seat is the user's)
is not decided here: the user wants to see full original clips, all three
participants, first.

A segment is usable only if all of the following hold:

* **10-30 s long.**
* **At least one end-of-turn event**, entirely inside the segment with room
  to spare: its pre-turn window has to open after the segment has started or
  the pattern is cut off at the top of the clip, and it must close before the
  segment ends. Every event whose turn instant falls in the window is held to
  this -- an event squeezed against an edge would be a turn change the clip
  contains but no schedule names (``--allow-edge-events`` relaxes it).
* **One participant never takes a turn.** No event inside the window names
  them as either speaker, and they produce no utterance the event builder
  would have counted -- anything that is not a backchannel by the builder's
  own lexical rule. Backchannels ("yeah", "oh", "um", ...) are allowed, since
  that is what a listener does, and are counted against the ranking.
* **Words stay whole.** Both boundaries land on an utterance boundary of some
  participant and no utterance of *any* participant straddles them.
  Utterances are the ``Caption/*_sentence.csv`` rows the events were built
  from, so the boundaries agree with the events by construction.
* **Events are separated.** Consecutive turn instants must be at least 2 s
  apart (``--min-separation``), because a gaze prototype occupies the second
  before its event and a closer neighbour would cut it short.
* **Motion and audio cover it.** The converted SMPL-X clip and the trimmed
  wav both run past the window's end (03-04-2022's motion is 6-15 s shorter
  than its audio).

Motion quality is *reported, not required* (user's call, 2026-09-08: find the
eligible audio first, lower-body glitches are dealt with later). Each window
carries the number of upper-body fit glitches over all three clips --
``find_3people_clean_windows.py``'s per-frame step test on the pelvis, head
and shoulders only, hips/knees/ankles ignored -- and ``--max-glitches N``
turns that into a filter.

Survivors are ranked for demo readability: few, well-spaced events, both
takers actually holding the floor, at least one clean turn-taking boundary,
speech shared between the takers, and a quiet bystander.

Time base
---------
Event times, caption times and the converted motion all run from the same
instant: the trimmed wav's first sample is the mocap's first exported frame
(``Notes.xlsx``'s "Offset (frames)" is a 0-8 frame difference in stream
*length* over ten minutes, not a start shift). So ``t`` seconds into the
audio is motion frame ``round(60 t)`` of the ``.npz``. Speaker codes 1-3 are
the ``PC_<N>`` of the caption files, which is the true participant number
(``Session_<S>_pc<N>_<Name>.npz`` names the same person).

Caveats: the captions are automatic transcripts of per-participant tracks.
Bleed between microphones puts a few phantom utterances on the wrong track
(12-15-2021 Session 1 has PC 2 "saying" "I'm Justice"), and the recogniser
writes ``[Music]`` / ``[Laughter]`` / ``[Applause]`` as utterances of their
own (275 ``[Music]`` rows over 55 files). A phantom utterance makes a phantom
event, so a window containing a tag-only utterance is rejected unless
``--allow-tags`` is given, and ``--inspect`` shows the transcript so a
candidate can be read before it is trusted.

Usage::

    uv run python Tools/find_3people_segments.py                  # rank and report
    uv run python Tools/find_3people_segments.py --top 40 --csv out.csv
    uv run python Tools/find_3people_segments.py --inspect 1      # read one candidate
    uv run python Tools/find_3people_segments.py --date 03-11-2022 --session 2
    uv run python Tools/find_3people_segments.py --require-type turn-taking --bystander 3

Nothing is exported yet: the three-party segment needs its own export
contract, and that waits on watching the full clips.
"""

from __future__ import annotations

import argparse
import ast
import bisect
import csv
import re
import struct
import sys
import wave
import zipfile
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

# Upper-body glitch test: the clean-window finder's thresholds (per frame at
# 60 fps) on the joints that read as a glitch from across a room, with the
# legs left out on purpose.
GLITCH_JOINTS = {0: 5.0, 15: 5.0, 16: 8.0, 17: 8.0}  # pelvis, head, shoulders: degrees
GLITCH_TRANS_MM = 15.0
EOT_ROOT = Path(r"F:\Data\GazePattern\EoT_2022")
DATA_ROOT = Path(r"D:\3People-2022")
SMPLX_ROOT = Path(r"F:\Data\3People-2022-SMPLX")

# The transcript directory the EoT_2022 tables were built from. Each date also
# carries Caption_SentenceBased/, which disagrees substantially; the events
# match Caption/ session by session (gazeturn's eot_events_2022.py pins it).
TRANSCRIPT_DIR = "Caption"

PARTICIPANTS = (1, 2, 3)
EOT_TYPE_NAMES = {1: "interruption", 2: "overlapping", 3: "turn-taking"}
TURN_TAKING = 3
MOTION_FPS = 60

# The event builder drops an utterance whose every word is in this list before
# detecting turns, so these are the utterances a bystander may produce without
# ever having "taken a turn" in the corpus's own terms. Reproduced verbatim.
BACKCHANNEL_WORDS = frozenset(["yeah", "oh", "um", "okay", "cool", "right", "uh"])

# What the recogniser writes when it hears something that is not words. A row
# that is only a tag is not speech, yet the event builder counted it.
TAG_PATTERN = re.compile(r"\[[A-Za-z ]+\]")

# Selection thresholds -- the brief's, every one exposed on the command line.
MIN_DURATION = 10.0
MAX_DURATION = 30.0
MIN_EVENTS = 1
# An event's pattern window opens 1 s before its turn instant; the lead is that
# window plus a moment of settled conversation before it.
LEAD_SECONDS = 1.5
TAIL_SECONDS = 0.5
MIN_EVENT_SEPARATION = 2.0
# Snapping a boundary to a motion frame moves it by at most half a frame; keep
# the moved utterance inside the window rather than dropping it over 8 ms.
SNAP_TOLERANCE = 0.5 / MOTION_FPS


@dataclass
class Utterance:
    speaker: int
    start: float
    end: float
    text: str

    @property
    def is_backchannel(self) -> bool:
        words = self.text.split()
        return bool(words) and all(w.lower() in BACKCHANNEL_WORDS for w in words)

    @property
    def is_tag_only(self) -> bool:
        """``[Music]`` and the like: a recogniser tag with no words around it."""
        return not TAG_PATTERN.sub("", self.text).strip()


@dataclass
class Event:
    index: int  # row in the session's _eot.csv, the corpus's own event id
    eot_type: int
    first_speaker: int
    second_speaker: int
    turn_time: float
    start: float
    end: float


@dataclass
class Session:
    date: str
    number: int
    names: dict[int, str]
    utterances: list[Utterance]  # all three participants, sorted by start
    events: list[Event]
    motion_seconds: float
    audio_seconds: float
    glitches: dict[int, list[int]]  # pc -> sorted frames where that clip's upper body jumps

    @property
    def id(self) -> str:
        return f"{self.date}_Session_{self.number}"


@dataclass
class Segment:
    session: Session
    start: float
    end: float
    events: list[Event]
    utterances: list[Utterance]
    bystander: int
    glitch_frames: int  # upper-body fit glitches over all three clips inside the window
    score: float = 0.0
    reasons: dict[str, float] = field(default_factory=dict)

    @property
    def duration(self) -> float:
        return self.end - self.start

    @property
    def takers(self) -> tuple[int, int]:
        """The two participants who hold the floor, lowest PC first."""
        return tuple(p for p in PARTICIPANTS if p != self.bystander)  # type: ignore[return-value]


# --------------------------------------------------------------------------- loading

def parse_clock(text: str) -> float:
    """``h:mm:ss.fff`` / ``mm:ss.fff`` -> seconds. The captions mix ``0:`` and ``00:`` hours."""
    parts = text.strip().split(":")
    parts = ["0"] * (3 - len(parts)) + parts
    return int(parts[0]) * 3600 + int(parts[1]) * 60 + float(parts[2])


def read_npy_shape(archive: zipfile.ZipFile, name: str) -> tuple[int, ...]:
    """Shape of one array in an .npz, without reading its data."""
    with archive.open(name) as handle:
        if handle.read(6) != b"\x93NUMPY":
            raise ValueError(f"{name} is not a .npy array")
        major = handle.read(1)[0]
        handle.read(1)
        width = 2 if major == 1 else 4
        header_length = struct.unpack("<H" if major == 1 else "<I", handle.read(width))[0]
        header = ast.literal_eval(handle.read(header_length).decode("latin1").strip())
    return tuple(header["shape"])


def upper_body_glitches(clip: Path) -> list[int]:
    """Frames where a watched joint's rotation or the pelvis jumps between consecutive frames."""
    import numpy as np  # local: the search itself is numpy-free
    from scipy.spatial.transform import Rotation

    z = np.load(clip)
    poses = z["poses"].astype(float)
    trans = z["trans"].astype(float)
    bad = np.linalg.norm(np.diff(trans, axis=0), axis=1) > GLITCH_TRANS_MM / 1000.0
    for joint, limit in GLITCH_JOINTS.items():
        r = Rotation.from_rotvec(poses[:, 3 * joint:3 * joint + 3])
        bad |= np.degrees((r[1:] * r[:-1].inv()).magnitude()) > limit
    return [int(i) + 1 for i in bad.nonzero()[0]]  # a step is charged to the later frame


def motion_clips(date: str, number: int) -> dict[int, Path]:
    """``{pc: npz}`` of the converted SMPL-X clips of one session."""
    clips = {}
    for pc in PARTICIPANTS:
        found = sorted((SMPLX_ROOT / date).glob(f"Session_{number}_pc{pc}_*.npz"))
        if len(found) != 1:
            raise FileNotFoundError(f"{date} Session {number} pc{pc}: {len(found)} converted clips")
        clips[pc] = found[0]
    return clips


def load_utterances(date: str, number: int, pc: int) -> list[Utterance]:
    path = DATA_ROOT / "mocap" / date / TRANSCRIPT_DIR / f"Session_{number}_PC_{pc}_sentence.csv"
    utterances = []
    with path.open(encoding="utf-8", newline="") as handle:
        for row in csv.DictReader(handle):
            text = (row.get("Sentence") or "").strip()
            if not text:
                continue
            utterances.append(Utterance(pc, parse_clock(row["start"]), parse_clock(row["end"]), text))
    return utterances


def load_events(date: str, number: int) -> list[Event]:
    events = []
    with (EOT_ROOT / f"{date}_Session_{number}_eot.csv").open(encoding="utf-8", newline="") as handle:
        for index, row in enumerate(csv.DictReader(handle)):
            events.append(Event(
                index=index,
                eot_type=int(row["eot_type"]),
                first_speaker=int(row["first_speaker"]),
                second_speaker=int(row["second_speaker"]),
                turn_time=int(row["turn_time"]) / 1000.0,
                start=int(row["eot_start"]) / 1000.0,
                end=int(row["eot_end"]) / 1000.0,
            ))
    events.sort(key=lambda e: e.turn_time)
    return events


def audio_seconds(date: str, number: int) -> float:
    shortest = float("inf")
    for pc in PARTICIPANTS:
        path = DATA_ROOT / "audio_text" / date / f"Session_{number}_PC_{pc}_audio.wav"
        with wave.open(str(path)) as handle:
            shortest = min(shortest, handle.getnframes() / handle.getframerate())
    return shortest


def load_session(date: str, number: int) -> Session:
    clips = motion_clips(date, number)
    frames = []
    names = {}
    for pc, path in clips.items():
        with zipfile.ZipFile(path) as archive:
            frames.append(read_npy_shape(archive, "poses.npy")[0])
        names[pc] = path.stem.split("_", 3)[3]

    glitches = {pc: upper_body_glitches(path) for pc, path in clips.items()}

    utterances = [u for pc in PARTICIPANTS for u in load_utterances(date, number, pc)]
    utterances.sort(key=lambda u: (u.start, u.speaker))
    return Session(
        date=date,
        number=number,
        names=names,
        utterances=utterances,
        events=load_events(date, number),
        motion_seconds=min(frames) / MOTION_FPS,
        audio_seconds=audio_seconds(date, number),
        glitches=glitches,
    )


def session_keys(date: str | None, number: int | None) -> list[tuple[str, int]]:
    keys = []
    for path in sorted(EOT_ROOT.glob("*_Session_*_eot.csv")):
        stem = path.name[: -len("_eot.csv")]
        d, _, s = stem.rpartition("_Session_")
        if date and d != date:
            continue
        if number and int(s) != number:
            continue
        keys.append((d, int(s)))
    return keys


# ------------------------------------------------------------------------- selection

def snap(seconds: float) -> float:
    """Round to a motion frame, so audio and mocap start on the same instant."""
    return round(seconds * MOTION_FPS) / MOTION_FPS


def min_separation(events: list[Event]) -> float:
    if len(events) < 2:
        return float("inf")
    return min(b.turn_time - a.turn_time for a, b in zip(events, events[1:]))


def find_bystander(events: list[Event], spoken: list[Utterance]) -> int | None:
    """The participant who takes no turn in the window, or None if everyone does.

    Two tests, because they fail differently: an event naming someone means
    they held or took the floor; a non-backchannel utterance means the event
    builder would have made them a speaker, event or not (the first utterance
    of a session gets no event of its own).
    """
    named = {e.first_speaker for e in events} | {e.second_speaker for e in events}
    talkers = {u.speaker for u in spoken if not u.is_backchannel}
    quiet = [p for p in PARTICIPANTS if p not in named and p not in talkers]
    if len(quiet) != 1:
        # Zero: everyone takes a turn. Two: only one person ever speaks, which
        # is a monologue with two bystanders, not turn-taking; nothing to demo.
        return None
    return quiet[0]


def candidates(session: Session, args: argparse.Namespace) -> list[Segment]:
    utterances = session.utterances
    events = session.events
    if len(events) < args.min_events:
        return []

    # A segment that runs past the shorter medium would freeze on the last pose
    # or fall silent.
    limit = min(session.motion_seconds, session.audio_seconds)

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
            if t1 > limit:
                continue
            if any(u.start < t0 - SNAP_TOLERANCE < u.end or u.start < t1 + SNAP_TOLERANCE < u.end
                   for u in utterances if u.start < t1 and u.end > t0):
                continue  # somebody is mid-word on a boundary

            in_window = [e for e in events if t0 <= e.turn_time <= t1]
            inside = [e for e in in_window
                      if e.start >= t0 + args.lead and e.end <= t1 - args.tail]
            if len(inside) < args.min_events:
                continue
            if not args.allow_edge_events and len(inside) != len(in_window):
                continue
            if min_separation(inside) < args.min_separation:
                continue
            if args.require_type and not any(
                    EOT_TYPE_NAMES[e.eot_type] == args.require_type for e in inside):
                continue

            spoken = [u for u in utterances
                      if u.start >= t0 - SNAP_TOLERANCE and u.end <= t1 + SNAP_TOLERANCE]
            if not args.allow_tags and any(u.is_tag_only for u in spoken):
                continue  # a phantom utterance, and so a phantom event
            bystander = find_bystander(in_window, spoken)
            if bystander is None:
                continue
            if args.bystander and bystander != args.bystander:
                continue

            f0, f1 = int(round(t0 * MOTION_FPS)), int(round(t1 * MOTION_FPS))
            glitch_frames = sum(
                bisect.bisect_right(session.glitches[pc], f1) - bisect.bisect_left(session.glitches[pc], f0)
                for pc in PARTICIPANTS)
            if args.max_glitches is not None and glitch_frames > args.max_glitches:
                continue

            found.append(Segment(session, t0, t1, inside, spoken, bystander, glitch_frames))

    return found


def length_score(duration: float) -> float:
    """20 s ideal, 10 s falloff: long enough for a listener to settle, short enough to watch."""
    return max(0.0, 1.0 - abs(duration - 20.0) / 10.0)


def score(segment: Segment) -> Segment:
    """Rank by demo readability, not by how typical the segment is.

    Every term is bounded so no single one can dominate, and every term is kept
    on the segment so a ranking can be argued with rather than just trusted.
    """
    events = segment.events
    a, b = segment.takers

    # Separation: the pattern before an event needs the second before it clear.
    separation = min(min_separation(events), 8.0) / 8.0

    # One to four boundaries read as a conversation; a dozen reads as a
    # scramble and leaves no frame free of a pattern.
    count = len(events)
    density = 1.0 if 1 <= count <= 4 else max(0.0, 1.0 - 0.2 * (count - 4))

    # Both takers should hold the floor, or it is one agent talking at another.
    holders = {e.first_speaker for e in events}
    alternation = 1.0 if len(holders) > 1 else 0.0

    # Interruptions and overlaps are welcome as texture; a segment with no
    # clean hand-over is the wrong demo.
    turn_takings = sum(1 for e in events if e.eot_type == TURN_TAKING)
    cleanliness = min(turn_takings, 2) / 2.0

    # Speech should be shared between the two takers.
    talk = {a: 0.0, b: 0.0}
    for u in segment.utterances:
        if u.speaker in talk:
            talk[u.speaker] += u.end - u.start
    total = talk[a] + talk[b]
    balance = 0.0 if total <= 0 else 2.0 * min(talk[a], talk[b]) / total

    # A listener who keeps saying "yeah" is less of a listener; mild penalty.
    bc = [u for u in segment.utterances if u.speaker == segment.bystander]
    bc_seconds = sum(u.end - u.start for u in bc)
    quiet = max(0.0, 1.0 - bc_seconds / 3.0)

    length = length_score(segment.duration)

    segment.reasons = {
        "separation": separation,
        "density": density,
        "alternation": alternation,
        "cleanliness": cleanliness,
        "balance": balance,
        "quiet": quiet,
        "length": length,
        "min_sep_s": min_separation(events),
        "events": float(count),
        "turn_takings": float(turn_takings),
        "bystander_backchannels": float(len(bc)),
        "bystander_seconds": bc_seconds,
        "glitch_frames": float(segment.glitch_frames),
    }
    segment.score = (
        3.0 * separation
        + 2.0 * density
        + 2.0 * alternation
        + 2.0 * cleanliness
        + 1.5 * balance
        + 1.0 * quiet
        + 1.0 * length
    )
    return segment


def best_per_session(found: list[Segment], per_session: int) -> list[Segment]:
    """Keep the top few non-overlapping segments of each session.

    Neighbouring windows of one session differ by a word and would otherwise
    fill the whole ranking with the same twenty seconds.
    """
    by_session: dict[str, list[Segment]] = {}
    for segment in found:
        by_session.setdefault(segment.session.id, []).append(segment)

    kept: list[Segment] = []
    for group in by_session.values():
        group.sort(key=lambda s: -s.score)
        chosen: list[Segment] = []
        for segment in group:
            if any(segment.start < c.end and c.start < segment.end for c in chosen):
                continue
            chosen.append(segment)
            if len(chosen) >= per_session:
                break
        kept.extend(chosen)

    kept.sort(key=lambda s: -s.score)
    return kept


# ------------------------------------------------------------------------- reporting

def role_letter(segment: Segment, pc: int) -> str:
    """A/B for the two takers (lowest PC is A), L for the listener who takes no turn."""
    if pc == segment.bystander:
        return "L"
    return "AB"[segment.takers.index(pc)]


def describe(segment: Segment, rank: int) -> str:
    s = segment.session
    types = ",".join(EOT_TYPE_NAMES[e.eot_type][0] + role_letter(segment, e.first_speaker)
                     + role_letter(segment, e.second_speaker) for e in segment.events)
    a, b = segment.takers
    return (
        f"{rank:>3}  {s.date} S{s.number}  {segment.start:7.2f}-{segment.end:7.2f}s "
        f"({segment.duration:5.2f}s)  score {segment.score:5.2f}  "
        f"events {len(segment.events)} [{types}]  "
        f"min-sep {segment.reasons['min_sep_s']:4.1f}s  balance {segment.reasons['balance']:.2f}  "
        f"A=pc{a} B=pc{b} L=pc{segment.bystander} ({s.names[segment.bystander]}, "
        f"{int(segment.reasons['bystander_backchannels'])} bc)"
        f"  glitches {segment.glitch_frames}"
    )


def inspect(segment: Segment) -> None:
    """Print the segment as a timeline, so a candidate can be judged by eye."""
    s = segment.session
    a, b = segment.takers
    first_frame = int(round(segment.start * MOTION_FPS))
    print(f"\n{s.id}  {segment.start:.2f}-{segment.end:.2f} s  ({segment.duration:.2f} s)")
    print(f"  A = pc{a} {s.names[a]}, B = pc{b} {s.names[b]}, "
          f"L (listener, takes no turn) = pc{segment.bystander} {s.names[segment.bystander]}")
    print(f"  motion frames {first_frame}-{first_frame + int(round(segment.duration * MOTION_FPS))} "
          f"of the converted npz (session motion {s.motion_seconds:.1f} s, audio {s.audio_seconds:.1f} s); "
          f"{segment.glitch_frames} upper-body glitch frames over the three clips")
    print(f"  score {segment.score:.2f}: " + ", ".join(
        f"{k} {v:.2f}" for k, v in segment.reasons.items()
        if k in ("separation", "density", "alternation", "cleanliness", "balance", "quiet", "length")))
    print()

    marks = [(u.start, 0, "utterance", u) for u in segment.utterances]
    marks += [(e.turn_time, 1, "event", e) for e in segment.events]
    marks.sort(key=lambda m: (m[0], m[1]))

    for when, _, kind, item in marks:
        offset = when - segment.start
        if kind == "utterance":
            who = role_letter(segment, item.speaker)
            tag = "  (backchannel)" if item.speaker == segment.bystander else ""
            print(f"  {offset:6.2f}  {who}  {item.text}{tag}")
        else:
            print(f"  {offset:6.2f}  --- EoT #{item.index} {EOT_TYPE_NAMES[item.eot_type]}: "
                  f"{role_letter(segment, item.first_speaker)} -> "
                  f"{role_letter(segment, item.second_speaker)} ---")


def write_csv(path: Path, ranked: list[Segment]) -> None:
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["rank", "date", "session", "start_s", "end_s", "duration_s", "score",
                         "n_events", "event_types", "event_indices", "min_sep_s", "turn_takings",
                         "balance", "taker_a", "taker_b", "bystander", "bystander_name",
                         "bystander_backchannels", "bystander_seconds", "glitch_frames"])
        for rank, seg in enumerate(ranked, start=1):
            a, b = seg.takers
            writer.writerow([
                rank, seg.session.date, seg.session.number,
                f"{seg.start:.3f}", f"{seg.end:.3f}", f"{seg.duration:.3f}", f"{seg.score:.4f}",
                len(seg.events), "".join(str(e.eot_type) for e in seg.events),
                " ".join(str(e.index) for e in seg.events),
                f"{seg.reasons['min_sep_s']:.3f}", int(seg.reasons["turn_takings"]),
                f"{seg.reasons['balance']:.3f}", a, b, seg.bystander,
                seg.session.names[seg.bystander],
                int(seg.reasons["bystander_backchannels"]), f"{seg.reasons['bystander_seconds']:.2f}",
                seg.glitch_frames,
            ])


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--min-duration", type=float, default=MIN_DURATION)
    parser.add_argument("--max-duration", type=float, default=MAX_DURATION)
    parser.add_argument("--min-events", type=int, default=MIN_EVENTS)
    parser.add_argument("--lead", type=float, default=LEAD_SECONDS,
                        help="clear seconds required before the first event's window")
    parser.add_argument("--tail", type=float, default=TAIL_SECONDS,
                        help="clear seconds required after the last event")
    parser.add_argument("--min-separation", type=float, default=MIN_EVENT_SEPARATION,
                        help="minimum seconds between consecutive turn instants (0 disables)")
    parser.add_argument("--allow-edge-events", action="store_true",
                        help="admit windows with events inside the lead/tail margins; those "
                             "events are left out of the schedule")
    parser.add_argument("--max-glitches", type=int, default=None,
                        help="reject windows with more upper-body glitch frames than this over "
                             "the three clips (default: report only, reject nothing)")
    parser.add_argument("--allow-tags", action="store_true",
                        help="admit windows containing tag-only utterances such as [Music]")
    parser.add_argument("--require-type", choices=sorted(EOT_TYPE_NAMES.values()),
                        help="keep only windows containing at least one event of this class")
    parser.add_argument("--bystander", type=int, choices=PARTICIPANTS,
                        help="keep only windows where this participant is the bystander")
    parser.add_argument("--date", help="restrict to one recording date, e.g. 03-11-2022")
    parser.add_argument("--session", type=int, help="restrict to one session number")
    parser.add_argument("--per-session", type=int, default=3,
                        help="how many non-overlapping segments to keep per session")
    parser.add_argument("--top", type=int, default=25, help="how many to report")
    parser.add_argument("--csv", type=Path, help="write the full ranking here")
    parser.add_argument("--inspect", type=int, metavar="RANK",
                        help="print the transcript and events of the segment at this rank")
    args = parser.parse_args()

    keys = session_keys(args.date, args.session)
    if not keys:
        print("no session matches --date/--session")
        return 1

    found: list[Segment] = []
    windows_per_session = {}
    for date, number in keys:
        session = load_session(date, number)
        windows = [score(s) for s in candidates(session, args)]
        windows_per_session[session.id] = len(windows)
        found.extend(windows)

    if not found:
        print("no segment satisfies the constraints; loosen --min-separation, --lead, "
              "--min-events or --max-glitches")
        return 1

    ranked = best_per_session(found, args.per_session)
    with_hits = sum(1 for n in windows_per_session.values() if n)
    print(f"{len(found)} windows over {with_hits} of {len(keys)} sessions, "
          f"{len(ranked)} kept after per-session de-overlap")
    print("  event codes: t/i/o = turn-taking/interruption/overlapping, then holder -> taker; "
          "A/B = the two takers (lowest PC is A), L = the listener\n")

    for rank, segment in enumerate(ranked[: args.top], start=1):
        print(describe(segment, rank))

    if args.csv:
        write_csv(args.csv, ranked)
        print(f"\nwrote {args.csv}")

    if args.inspect:
        if args.inspect > len(ranked):
            print(f"\nrank {args.inspect} does not exist ({len(ranked)} segments)")
            return 1
        inspect(ranked[args.inspect - 1])

    return 0


if __name__ == "__main__":
    sys.exit(main())
