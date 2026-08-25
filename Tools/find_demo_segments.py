"""Find TalkingWithHands segments that can carry a demo, and export one.

The case-1 demo (agent A and agent B taking turns while the user watches) is now
played from the corpus rather than synthesised: both agents speak the dataset's
own audio and move on its own mocap. This script picks the stretch of a
conversation that gets played.

A case-1 segment is usable only if all of the following hold:

* **20-30 s long** -- the video length asked for.
* **At least two end-of-turn events**, each entirely inside the segment with
  room to spare: an event's pre-turn window has to open after the segment has
  started, or the pattern is cut off at the top of the video.
* **Words stay whole.** Both boundaries land on an utterance boundary and no
  utterance of *either* speaker straddles them. Utterances are rebuilt exactly
  as ``EoT_TWH`` built them -- split the word-level TSV wherever the gap between
  consecutive words exceeds 0.3 s -- so the segment boundaries agree with the
  events by construction.
* **Sentences stay whole.** A pause split is not a sentence split: any 0.3 s
  breath ends an utterance, so the rule above permits a window that opens or
  closes mid-sentence, and until 2026-08-21 the exported segments did exactly
  that. Every speaker must now enter the window at a sentence start and leave
  it on ``.``/``?``/``!``, tested per speaker rather than only on the two edge
  utterances. ``--allow-fragment-edges`` restores the old behaviour.
* **Events are separated.** Consecutive turn instants must be at least 2 s
  apart. The corpus is dense (a 25 s window routinely holds 5-8 events) and a
  gaze prototype occupies the second before its event, so events any closer
  would have their patterns cut short by the next one.

Everything that survives is then ranked, because thousands of windows survive.
The score rewards what makes a demo readable rather than what makes it typical:
few, well-spaced events, both agents actually taking turns, and at least one
plain turn-taking (rather than interruption or overlap) boundary.

``--scene2`` switches to the scene-2 search: a window that *ends* at a
turn-taking boundary where the floor-holder yields the turn — in the demo, to
the human user, who never actually takes it (the study questionnaire carries
the measurement). A scene-2 window is a monologue with backchannels: no other
annotated event inside (``--allow-internal-events`` admits interruptions and
overlaps, never other turn-takings — whether a bystander could join *after*
internal turn-taking is a semantic judgment, so those windows are never
selected by rule), the non-yielding speaker limited to backchannels, and the
yielder talking for most of the lead-in. Candidates whose closing utterance is
a question outrank all others: the transcripts are punctuated, so "ends with
'?'" is a direct test. The exported schedule names the user (speaker code 0)
as the taker of the final, real boundary — no ``-1`` final turn — and carries
a ``tailSeconds`` hold so the yield gaze stays watchable after the voices
stop. The scene-1 flags ``--min-events``/``--lead``/``--tail``/
``--min-separation`` are ignored under ``--scene2``.

Usage::

    python Tools/find_demo_segments.py                      # rank and report
    python Tools/find_demo_segments.py --top 40 --csv out.csv
    python Tools/find_demo_segments.py --export 1           # export the best one
    python Tools/find_demo_segments.py --export 1 --name case2
    python Tools/find_demo_segments.py --scene2 --inspect 1 # scene-2 search
    python Tools/find_demo_segments.py --scene2 --export 1 --name case2_seg01

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
import math
import struct
import sys
import wave
import zipfile
from array import array
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DATASET = Path(r"F:\Data\TalkingWithHandsCentered")

# Speaker codes as EoT_TWH writes them (see its _twh_meta.json). Code 0 is
# ours, not the corpus's: the human user, as the taker of a scene-2 yield
# (mirrored by DemoSegment.UserSpeakerCode on the C# side).
SIDE_OF_CODE = {1: "main-agent", 2: "interloctr"}
USER_CODE = 0
EOT_TYPE_NAMES = {1: "interruption", 2: "overlapping", 3: "turn-taking"}
TURN_TAKING = 3

# The pause that separates two utterances, from the EoT builder's own choice.
# Reproduced rather than re-tuned: segment boundaries have to agree with the
# event times, and both are derived from this split.
PAUSE_THRESHOLD = 0.3

# Sentence-final punctuation, as the transcripts carry it. A pause split alone
# is *not* a sentence split -- any 0.3 s breath ends an utterance -- so windows
# chosen on pause boundaries alone open and close mid-sentence. These are the
# characters that say a sentence actually finished.
SENTENCE_FINAL = (".", "?", "!")
# Trailing quotes/brackets sit outside the stop when the transcriber used them.
SENTENCE_TRAILING = "\"')]}"

# Punctuation alone is not enough: the transcripts put full stops on words that
# cannot end a sentence. The window that opened case1_seg02 mid-phrase did so
# because the preceding utterance was the literal token ``my.`` -- a possessive
# determiner with a period on it. So a boundary word must also be a word that
# can plausibly end an utterance.
#
# Closed classes only, and deliberately conservative: ambiguous words that do
# end real utterances are left out (``so`` in "I think so.", ``you`` in "What
# about you?", ``that`` in "things like that.", ``is`` in "what it is."). The
# fillers are here because in this corpus a final ``um``/``uh``/``like`` is the
# hesitation, not a word. Over-rejecting is the safe direction: the search
# returns far more candidates than the study needs.
NON_FINAL_WORDS = frozenset("""
    a an the my your his her its our their
    of to in on at for with from by into onto upon than per
    and but or because nor
    um uh uhm erm hmm-
    like
""".split())

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

# Scene 2. The window ends at a turn-taking event whose instant coincides with
# the end of an utterance by the event's first speaker — the yield; the taker
# is rewritten to the human user (USER_CODE) at export.
# Hold after the voices stop, so the yield gaze is watchable; segment data, read
# by the demo recorder (0 means the recorder's own default).
SCENE2_TAIL_SECONDS = 4.0
# The yielder must be an established participant, not a voice from nowhere.
SCENE2_MIN_OWN_SPEECH = 10.0
# What still counts as a backchannel from the non-yielding speaker.
BACKCHANNEL_MAX_SECONDS = 1.0
BACKCHANNEL_TOTAL_SECONDS = 4.0
# Tolerance between a type-3 turn instant and the yielder's utterance end. The
# annotation and the word-level TSV are the same material at slightly different
# granularity, so genuine yields align within a fraction of a word.
YIELD_ALIGN_SECONDS = 0.15
# Snapping a boundary to a motion frame moves it by at most half a frame; keep
# the moved utterance inside the window rather than dropping it over 8 ms.
SNAP_TOLERANCE = 0.5 / MOTION_FPS

# Peak the exported pair is normalised to, leaving headroom for the two voices
# summing during overlaps.
PEAK_TARGET = 0.7

# Voice pitch. The corpus records nothing about who is speaking, so a segment's
# voices are classified by measuring them: median F0 over that speaker's own
# voiced frames. The bands are the conventional ones for adult speech (male
# ~85-155 Hz, female ~165-255 Hz) with the gap between them left as "unclear"
# rather than forced into one side.
VOICE_MALE_MAX_HZ = 155.0
VOICE_FEMALE_MIN_HZ = 175.0

# F0 search range, and the rate the signal is decimated to before searching.
# 4410 Hz puts the range at lags 14-63, which resolves ~6 Hz either side of the
# male/female boundary — far finer than the 20 Hz gap the bands leave.
PITCH_MIN_HZ = 70.0
PITCH_MAX_HZ = 320.0
PITCH_RATE = 4410
PITCH_DECIMATION = 10

# How many voiced frames to measure per speaker. A median over this many is
# stable to a few Hz, and the whole point is to avoid reading entire files.
PITCH_FRAMES = 60
PITCH_FRAME_SECONDS = 0.04

# Normalised autocorrelation below which a frame is treated as unvoiced.
PITCH_PERIODICITY = 0.35


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
    by_speaker = {speaker: load_utterances(stem, speaker) for speaker in SIDE_OF_CODE}
    utterances = by_speaker[1] + by_speaker[2]
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

            if args.require_type and not any(
                    EOT_TYPE_NAMES[e.eot_type] == args.require_type for e in inside):
                continue

            spoken = [u for u in utterances if u.start >= t0 and u.end <= t1]
            if not args.allow_fragment_edges and not sentence_clean(spoken, by_speaker):
                continue

            found.append(Segment(
                stem=stem,
                start=t0,
                end=t1,
                events=inside,
                utterances=spoken,
            ))

    return found


def ends_sentence(utterance: Utterance) -> bool:
    """Did this utterance finish a sentence, rather than merely pause?

    Two tests, because either alone is fooled by this corpus: the transcript
    must mark a stop, *and* the word carrying it must be one that can end an
    utterance. See ``NON_FINAL_WORDS`` for why the second test exists.
    """
    text = utterance.text.rstrip().rstrip(SENTENCE_TRAILING)
    if not text.endswith(SENTENCE_FINAL):
        return False
    last = text.split()[-1].rstrip("".join(SENTENCE_FINAL) + SENTENCE_TRAILING).lower()
    return last not in NON_FINAL_WORDS


def sentence_clean(segment_utterances: list[Utterance],
                   by_speaker: dict[int, list[Utterance]]) -> bool:
    """Every speaker enters the window at a sentence start and leaves at a stop.

    Applied per speaker rather than only to the two edge utterances: the window
    opens at one speaker's boundary, but the *other* speaker's first utterance
    inside it can still be the back half of a sentence that began outside.

    The pause split does part of the work already -- a candidate edge is always
    followed (or preceded) by a gap over ``PAUSE_THRESHOLD`` -- so this test is
    punctuation *and* a real pause, which is what separates a finished sentence
    from a trailing-off fragment that happens to carry a full stop.
    """
    speakers = {u.speaker for u in segment_utterances}
    for speaker in speakers:
        inside = [u for u in segment_utterances if u.speaker == speaker]
        stream = by_speaker[speaker]

        first_index = stream.index(inside[0])
        # Nothing before it in this speaker's stream is an opening by default.
        if first_index > 0 and not ends_sentence(stream[first_index - 1]):
            return False
        if not ends_sentence(inside[-1]):
            return False
    return True


def min_separation(events: list[Event]) -> float:
    if len(events) < 2:
        return float("inf")
    return min(b.turn_time - a.turn_time for a, b in zip(events, events[1:]))


def length_score(duration: float) -> float:
    """Long enough to breathe, short enough to watch: 25 s ideal, 5 s falloff."""
    return max(0.0, 1.0 - abs(duration - 25.0) / 5.0)


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
    turn_takings = sum(1 for e in events if e.eot_type == TURN_TAKING)
    cleanliness = min(turn_takings, 2) / 2.0

    # Speech should be shared. Silence is fine; one speaker holding 90% of it
    # is not.
    talk = {1: 0.0, 2: 0.0}
    for u in segment.utterances:
        talk[u.speaker] += u.end - u.start
    total = talk[1] + talk[2]
    balance = 0.0 if total <= 0 else 2.0 * min(talk[1], talk[2]) / total

    length = length_score(segment.duration)

    segment.reasons = {
        "separation": separation,
        "density": density,
        "alternation": alternation,
        "cleanliness": cleanliness,
        "balance": balance,
        "length": length,
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
        + 1.0 * length
    )
    return segment


def scene2_candidates(stem: str, args: argparse.Namespace) -> list[Segment]:
    """Windows that end at a yield: a turn-taking boundary closing a monologue.

    The final utterance's end *is* the segment end, so the demo stops the
    instant the floor opens; the taker is rewritten to the user at export. The
    lead-in must be one speaker's floor throughout — with no internal
    turn-taking there is no dyadic rhythm for the user to intrude on, which is
    what makes yielding to a silent third party plausible by construction.
    """
    utterances = load_utterances(stem, 1) + load_utterances(stem, 2)
    utterances.sort(key=lambda u: u.start)
    events = load_events(stem)

    motion_seconds = min(motion_frame_count(stem, side) for side in SIDE_OF_CODE.values()) / MOTION_FPS
    starts = sorted({snap(u.start) for u in utterances})
    # The bodies keep playing mocap through the hold after the voices stop, so
    # the recording needs motion past the yield instant, not just up to it.
    tail = args.hold_tail if args.hold_tail is not None else SCENE2_TAIL_SECONDS

    found: list[Segment] = []
    for event in events:
        if event.eot_type != TURN_TAKING:
            continue

        final = next((u for u in utterances if u.speaker == event.first_speaker
                      and abs(u.end - event.turn_time) <= YIELD_ALIGN_SECONDS), None)
        if final is None:
            continue

        t1 = snap(final.end)
        if t1 + tail > motion_seconds:
            continue
        if any(u.start < t1 < u.end for u in utterances):
            continue

        for t0 in starts:
            duration = t1 - t0
            if duration > args.max_duration:
                continue
            if duration < args.min_duration:
                break  # starts are sorted, so every later window is shorter still
            if any(u.start < t0 < u.end for u in utterances):
                continue

            internal = [e for e in events if e is not event and t0 <= e.turn_time < t1]
            # Relaxed mode admits interruptions/overlaps — talk-over the monologue
            # predicate has already bounded — but never an internal turn-taking:
            # that is a genuine hand-over, and whether a bystander could join
            # after one is a semantic judgment left to --inspect, not a rule.
            blocking = ([e for e in internal if e.eot_type == TURN_TAKING]
                        if args.allow_internal_events else internal)
            if blocking:
                continue

            other = [u for u in utterances
                     if u.speaker != event.first_speaker and u.start < t1 and u.end > t0]
            if any(u.end - u.start > args.backchannel_max for u in other):
                continue
            if sum(min(u.end, t1) - max(u.start, t0) for u in other) > args.backchannel_total:
                continue

            own = sum(min(u.end, t1) - max(u.start, t0) for u in utterances
                      if u.speaker == event.first_speaker and u.start < t1 and u.end > t0)
            if own < args.min_own_speech:
                continue

            found.append(Segment(
                stem=stem,
                start=t0,
                end=t1,
                # The yield event is always last; build_turns and the exporter
                # rely on that ordering.
                events=internal + [event],
                utterances=[u for u in utterances
                            if u.start >= t0 - SNAP_TOLERANCE and u.end <= t1 + SNAP_TOLERANCE],
            ))

    return found


def final_utterance(segment: Segment) -> Utterance:
    """The yielder's closing utterance — the one the window was built to end at.

    scene2_candidates ends the window at a yielder utterance, so the yielder's
    last utterance in the segment is that one by construction.
    """
    yielder = segment.events[-1].first_speaker
    return next(u for u in reversed(segment.utterances) if u.speaker == yielder)


def is_question_final(segment: Segment) -> bool:
    """The transcripts carry punctuation, so this is a direct test on the last word."""
    return final_utterance(segment).text.split()[-1].endswith("?")


def scene2_score(segment: Segment) -> Segment:
    """Rank scene-2 candidates: questions first, then how well the yield reads.

    The question weight exceeds the sum of every other weight, so question-final
    candidates form a hard first tier and open-floor ones a second while the
    score stays a single scalar the shared ranking machinery understands.
    """
    yielder = segment.events[-1].first_speaker

    question = 1.0 if is_question_final(segment) else 0.0

    own = sum(u.end - u.start for u in segment.utterances if u.speaker == yielder)
    own_share = min(own / segment.duration, 1.0)

    final = final_utterance(segment)
    # A full closing sentence reads as an invitation; a fragment reads as a lapse.
    yield_clarity = min((final.end - final.start) / 3.0, 1.0)

    backchannels = [u for u in segment.utterances if u.speaker != yielder]
    # A couple of "mm-hm"s read as a live dyad; total silence reads as a lecture.
    life = min(len(backchannels), 3) / 3.0

    segment.reasons = {
        "question": question,
        "own_share": own_share,
        "yield_clarity": yield_clarity,
        "length": length_score(segment.duration),
        "life": life,
        "own_talk_s": own,
        "backchannel_count": float(len(backchannels)),
        "backchannel_total_s": sum(u.end - u.start for u in backchannels),
        "internal_events": float(len(segment.events) - 1),
        "final_utt_s": final.end - final.start,
    }
    segment.score = (
        10.0 * question
        + 1.5 * own_share
        + 1.5 * yield_clarity
        + 1.0 * segment.reasons["length"]
        + 1.0 * life
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


def matches_voice(segment: Segment, wanted: str) -> bool:
    classes = [voice_class(voice_pitch(segment.stem, speaker)) for speaker in sorted(SIDE_OF_CODE)]

    if wanted == "mixed":
        return set(classes) == {"male", "female"}

    return all(voice == wanted for voice in classes)


def voices_of(segment: Segment) -> str:
    """The measured pitch of both speakers, for reporting. Cached, so it is cheap to repeat."""
    parts = []
    for speaker in sorted(SIDE_OF_CODE):
        pitch = voice_pitch(segment.stem, speaker)
        label = "AB"[speaker - 1]
        parts.append(f"{label} {voice_class(pitch)}" + (f" {pitch:.0f}Hz" if pitch else ""))
    return ", ".join(parts)


def describe(segment: Segment, rank: int, voices: bool = False, scene2: bool = False) -> str:
    head = (
        f"{rank:>3}  {segment.stem}  {segment.start:7.2f}-{segment.end:7.2f}s "
        f"({segment.duration:5.2f}s)  score {segment.score:5.2f}  "
    )
    if scene2:
        tail = (
            f"{'Q' if segment.reasons['question'] else '-'}  "
            f"yielder {'AB'[segment.events[-1].first_speaker - 1]}  "
            f"own {segment.reasons['own_share']:.2f}  "
            f"backchannels {int(segment.reasons['backchannel_count'])}"
        )
    else:
        types = ",".join(EOT_TYPE_NAMES[e.eot_type][0] + str(e.first_speaker) for e in segment.events)
        tail = (
            f"events {len(segment.events)} [{types}]  "
            f"min-sep {segment.reasons['min_sep_s']:4.1f}s  "
            f"balance {segment.reasons['balance']:.2f}"
        )
    line = head + tail
    return line + f"  [{voices_of(segment)}]" if voices else line


def inspect(segment: Segment, scene2: bool = False) -> None:
    """Print the segment as a timeline, so a candidate can be judged by eye.

    Utterances are shown with the speaker who produced them and the events are
    interleaved at their turn instants, which is the only practical way to see
    whether the transcript actually reads as a hand-over rather than as two
    people talking past each other.
    """
    print(f"\n{segment.stem}  {segment.start:.2f}-{segment.end:.2f} s  ({segment.duration:.2f} s)")
    print(f"  agent A = main-agent (speaker 1), agent B = interloctr (speaker 2)")
    print(f"  voices: {voices_of(segment)}\n")

    marks = [(u.start, "utterance", u) for u in segment.utterances]
    marks += [(e.turn_time, "event", e) for e in segment.events]
    marks.sort(key=lambda m: m[0])

    for when, kind, item in marks:
        offset = when - segment.start
        if kind == "utterance":
            who = "A" if item.speaker == 1 else "B"
            print(f"  {offset:6.2f}  {who}  {item.text}")
        elif scene2 and item is segment.events[-1]:
            print(f"  {offset:6.2f}  --- YIELD -> USER "
                  f"({'question' if is_question_final(segment) else 'open floor'}; "
                  f"corpus taker was {'AB'[item.second_speaker - 1]}) ---")
        else:
            print(f"  {offset:6.2f}  --- EoT {EOT_TYPE_NAMES[item.eot_type]}: "
                  f"{'AB'[item.first_speaker - 1]} -> {'AB'[item.second_speaker - 1]} ---")


def build_turns(segment: Segment, yields_to_user: bool = False) -> list[dict]:
    """The turn schedule a gaze policy reads, derived from the events.

    Each event contributes the turn that *ends* at it: ``first_speaker`` holds
    the floor going into the boundary and ``second_speaker`` takes it. A turn
    starts at the previous event's instant, and a final turn runs from the last
    event to the end of the segment with no boundary of its own -- nothing is
    known about what happens after the clip, so no pattern fires there.

    In scene-2 mode (``yields_to_user``) the last event *is* the end of the
    segment: the final turn is the yield itself, held by that event's first
    speaker, taken by the user, and carrying the event's real index so the
    pre-turn pattern fires at the yield. No ``-1`` turn is emitted.

    Consecutive events need not chain (one speaker can be ``first_speaker``
    twice running, the corpus does not guarantee otherwise), so each turn's
    roles come from its own event rather than from the previous turn.
    """
    scheduled = segment.events[:-1] if yields_to_user else segment.events

    turns = []
    previous = 0.0
    for index, event in enumerate(scheduled):
        turns.append({
            "speaker": event.first_speaker,
            "addressee": event.second_speaker,
            "startTime": round(previous, 4),
            "endTime": round(event.turn_time - segment.start, 4),
            "eventIndex": index,
        })
        previous = event.turn_time - segment.start

    last = segment.events[-1]
    speaker, addressee, event_index = (
        (last.first_speaker, USER_CODE, len(segment.events) - 1) if yields_to_user
        else (last.second_speaker, last.first_speaker, -1))
    turns.append({
        "speaker": speaker,
        "addressee": addressee,
        "startTime": round(previous, 4),
        "endTime": round(segment.duration, 4),
        "eventIndex": event_index,
    })
    return turns


_pitch_cache: dict[tuple[str, int], float | None] = {}


def voice_pitch(stem: str, speaker: int) -> float | None:
    """Median F0 of one speaker, in Hz, or None if too little voiced speech.

    Measured only over that speaker's own utterances, taken from the word-level
    TSV, so the other side of the conversation cannot pull the estimate. The
    signal is decimated hard first: F0 lives well below 320 Hz, and searching
    lags at 44.1 kHz would be a hundred times the work for no more accuracy.
    """
    key = (stem, speaker)
    if key in _pitch_cache:
        return _pitch_cache[key]

    _pitch_cache[key] = measure_pitch(stem, speaker)
    return _pitch_cache[key]


def measure_pitch(stem: str, speaker: int) -> float | None:
    side = SIDE_OF_CODE[speaker]
    path = DATASET / "talkingwithHands-Audio" / side / "wav" / f"{stem}_{side}.wav"
    frame_length = int(PITCH_FRAME_SECONDS * PITCH_RATE)
    min_lag = int(PITCH_RATE / PITCH_MAX_HZ)
    max_lag = int(PITCH_RATE / PITCH_MIN_HZ)

    estimates: list[float] = []
    with wave.open(str(path), "rb") as reader:
        rate = reader.getframerate()
        channels = reader.getnchannels()

        for utterance in load_utterances(stem, speaker):
            if len(estimates) >= PITCH_FRAMES:
                break
            if utterance.end - utterance.start < PITCH_FRAME_SECONDS * 2:
                continue

            reader.setpos(int(utterance.start * rate))
            raw = array("h")
            raw.frombytes(reader.readframes(int((utterance.end - utterance.start) * rate)))
            if sys.byteorder == "big":
                raw.byteswap()

            signal = decimate(raw, channels)
            for offset in range(0, len(signal) - frame_length, frame_length):
                if len(estimates) >= PITCH_FRAMES:
                    break

                f0 = frame_pitch(signal[offset:offset + frame_length], min_lag, max_lag)
                if f0 is not None:
                    estimates.append(f0)

    if len(estimates) < 10:
        return None

    estimates.sort()
    return estimates[len(estimates) // 2]


def decimate(samples: array, channels: int) -> list[float]:
    """Average-and-drop down to PITCH_RATE. The averaging is the anti-alias filter."""
    step = PITCH_DECIMATION * channels
    out = []
    for i in range(0, len(samples) - step, step):
        out.append(sum(samples[i:i + step]) / step)
    return out


def frame_pitch(frame: list[float], min_lag: int, max_lag: int) -> float | None:
    """Normalised-autocorrelation F0 of one frame, or None if it is not voiced."""
    mean = sum(frame) / len(frame)
    centred = [v - mean for v in frame]
    energy = sum(v * v for v in centred)
    if energy <= 0:
        return None

    best_lag = 0
    best_score = 0.0
    limit = min(max_lag, len(centred) - min_lag - 1)

    for lag in range(min_lag, limit + 1):
        overlap = len(centred) - lag
        correlation = 0.0
        tail = 0.0
        for i in range(overlap):
            correlation += centred[i] * centred[i + lag]
            tail += centred[i + lag] * centred[i + lag]

        head = sum(centred[i] * centred[i] for i in range(overlap))
        norm = math.sqrt(head * tail)
        if norm <= 0:
            continue

        score = correlation / norm
        if score > best_score:
            best_score = score
            best_lag = lag

    if best_lag == 0 or best_score < PITCH_PERIODICITY:
        return None

    return PITCH_RATE / best_lag


def voice_class(pitch: float | None) -> str:
    if pitch is None:
        return "unknown"
    if pitch <= VOICE_MALE_MAX_HZ:
        return "male"
    return "female" if pitch >= VOICE_FEMALE_MIN_HZ else "unclear"


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


def export(segment: Segment, name: str, *, yields_to_user: bool = False,
           tail_seconds: float = 0.0) -> Path:
    directory = REPO / "Assets" / "DemoSegments" / name
    directory.mkdir(parents=True, exist_ok=True)

    start_frame = int(round(segment.start * MOTION_FPS))
    # The motion covers the hold after the voices stop (tail_seconds is 0 for a
    # scene-1 export), so the bodies keep moving while the yield gaze is held.
    frame_count = int(round((segment.duration + tail_seconds) * MOTION_FPS))

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
        pitch = voice_pitch(segment.stem, code)
        agents.append({
            "speaker": code,
            "side": side,
            "audio": f"Assets/DemoSegments/{name}/{wav_name}",
            "motion": str(DATASET / "SMPLX-60fps-grounded" / f"{segment.stem}_{side}.npz"),
            "audioSamples": written,
            # Measured, not annotated: the corpus records nothing about who is
            # speaking. Kept so the agent's appearance can be matched to it.
            "voicePitchHz": round(pitch, 1) if pitch else None,
            "voice": voice_class(pitch),
        })

    event_records = []
    for index, event in enumerate(segment.events):
        record = {
            "index": index,
            "eotType": event.eot_type,
            "eotTypeName": EOT_TYPE_NAMES[event.eot_type],
            "firstSpeaker": event.first_speaker,
            "secondSpeaker": event.second_speaker,
            "turnTime": round(event.turn_time - segment.start, 4),
            "startTime": round(event.start - segment.start, 4),
            "endTime": round(event.end - segment.start, 4),
        }
        if yields_to_user and index == len(segment.events) - 1:
            # The yield event's taker is rewritten to the user; the corpus's own
            # taker is kept as provenance (JsonUtility drops the unknown key).
            record["secondSpeaker"] = USER_CODE
            record["corpusSecondSpeaker"] = event.second_speaker
        event_records.append(record)

    document = {
        # /2 adds yieldsToUser + tailSeconds and lets a final turn carry a real
        # event index; /1 files stay readable (the C# mirror defaults the new
        # keys) and are still written for scene-1 exports apart from those keys.
        "schema": "gazecontrol.demo-segment/2",
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
        "yieldsToUser": yields_to_user,
        "tailSeconds": round(tail_seconds, 3),
        "agents": agents,
        "turns": build_turns(segment, yields_to_user),
        "events": event_records,
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


def check_scene2_document(document: dict) -> list[str]:
    """Violations of the scene-2 contract in an exported document.

    Run against what was actually written, not what was meant: the C# side
    trusts these invariants (the final turn's real event index is what fires
    the yield pattern, addressee 0 is what resolves to the user), so a quiet
    exporter bug here would surface only as a silently wrong take.
    """
    problems = []
    turns = document["turns"]
    events = document["events"]
    last_turn = turns[-1]
    last_event = events[-1]

    if not document.get("yieldsToUser"):
        problems.append("yieldsToUser is not true")
    if document.get("tailSeconds", 0) <= 0:
        problems.append("tailSeconds is not positive")
    if last_turn["addressee"] != USER_CODE:
        problems.append(f"final turn addressee is {last_turn['addressee']}, not the user ({USER_CODE})")
    if last_turn["eventIndex"] != last_event["index"]:
        problems.append(f"final turn eventIndex {last_turn['eventIndex']} does not name the last event {last_event['index']}")
    if any(turn["eventIndex"] < 0 for turn in turns):
        problems.append("a turn carries eventIndex -1; scene-2 schedules have none")
    if last_event["eotType"] != 3:
        problems.append(f"the yield event is eotType {last_event['eotType']}, not turn-taking")
    if last_event["secondSpeaker"] != USER_CODE:
        problems.append(f"the yield event's taker is {last_event['secondSpeaker']}, not the user ({USER_CODE})")
    if abs(last_turn["endTime"] - document["durationSeconds"]) > 1e-3:
        problems.append("the final turn does not end at the segment end")
    covered = document["motionFrameCount"] / MOTION_FPS
    if covered + 1.0 / MOTION_FPS < document["durationSeconds"] + document["tailSeconds"]:
        problems.append(f"motion covers {covered:.2f} s but the segment plus hold needs "
                        f"{document['durationSeconds'] + document['tailSeconds']:.2f} s")
    if not MIN_DURATION <= document["durationSeconds"] <= MAX_DURATION:
        problems.append(f"duration {document['durationSeconds']} s is outside {MIN_DURATION}-{MAX_DURATION} s")

    return problems


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
    parser.add_argument("--require-type", choices=sorted(EOT_TYPE_NAMES.values()),
                        help="scene 1: keep only windows containing at least one event of this "
                             "class. The three classes are unevenly represented, so this is how "
                             "the rarer ones (overlapping) are found at all")
    parser.add_argument("--allow-fragment-edges", action="store_true",
                        help="scene 1: drop the sentence-boundary requirement, so windows may "
                             "open or close mid-sentence (the pre-2026-08-21 behaviour)")
    parser.add_argument("--scene2", action="store_true",
                        help="search for scene-2 yield segments instead of case-1 windows")
    parser.add_argument("--allow-internal-events", action="store_true",
                        help="scene 2: admit internal interruptions/overlaps (never turn-takings); "
                             "judge these by transcript, the rhythm question is semantic")
    parser.add_argument("--min-own-speech", type=float, default=SCENE2_MIN_OWN_SPEECH,
                        help="scene 2: seconds the yielder must speak inside the window")
    parser.add_argument("--backchannel-max", type=float, default=BACKCHANNEL_MAX_SECONDS,
                        help="scene 2: longest single utterance still counted as a backchannel")
    parser.add_argument("--backchannel-total", type=float, default=BACKCHANNEL_TOTAL_SECONDS,
                        help="scene 2: total non-yielder speech allowed inside the window")
    parser.add_argument("--hold-tail", type=float, default=None,
                        help=f"seconds the recorder holds after the voices stop "
                             f"(default {SCENE2_TAIL_SECONDS} under --scene2, else the recorder's own)")
    parser.add_argument("--per-stem", type=int, default=2,
                        help="how many non-overlapping segments to keep per conversation")
    parser.add_argument("--voice", choices=["any", "male", "female", "mixed"], default="any",
                        help="keep only segments whose two speakers have these measured voice pitches")
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

    generate = scene2_candidates if args.scene2 else candidates
    rank = scene2_score if args.scene2 else score

    pool = [args.stem] if args.stem else stems()
    found: list[Segment] = []
    for stem in pool:
        found.extend(rank(s) for s in generate(stem, args))

    if not found:
        print("no segment satisfies the constraints; loosen "
              + ("--min-own-speech or --backchannel-total" if args.scene2
                 else "--min-separation or --min-events"))
        return 1

    ranked = best_per_stem(found, args.per_stem)
    print(f"{len(found)} windows over {len(pool)} conversations, "
          f"{len(ranked)} kept after per-conversation de-overlap")

    if args.voice != "any":
        # Measured lazily, in rank order: pitch analysis reads audio, and there
        # is no reason to measure a conversation nobody will look at. Stop once
        # there are comfortably more matches than will be reported.
        wanted = max(args.top, args.export or 0, args.inspect or 0, 10)
        kept = []
        for segment in ranked:
            if len(kept) >= wanted:
                break
            if matches_voice(segment, args.voice):
                kept.append(segment)

        print(f"{len(kept)} of the first {len(ranked)} match --voice {args.voice} "
              f"({len(_pitch_cache)} speaker pitches measured)")
        ranked = kept

    print()
    for rank, segment in enumerate(ranked[: args.top], start=1):
        print(describe(segment, rank, voices=args.voice != "any", scene2=args.scene2))

    if args.csv:
        with args.csv.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle)
            shared = ["rank", "stem", "start_s", "end_s", "duration_s", "score"]
            # Each mode reports what it measured; a scene-2 ranking in scene-1
            # vocabulary would silently read as the wrong kind of search.
            writer.writerow(shared + (
                ["question", "yielder", "own_share", "backchannels", "internal_events"]
                if args.scene2 else
                ["n_events", "min_sep_s", "turn_takings", "balance", "event_types"]))
            for rank, segment in enumerate(ranked, start=1):
                row = [rank, segment.stem, f"{segment.start:.3f}", f"{segment.end:.3f}",
                       f"{segment.duration:.3f}", f"{segment.score:.4f}"]
                if args.scene2:
                    row += [int(segment.reasons["question"]),
                            segment.events[-1].first_speaker,
                            f"{segment.reasons['own_share']:.3f}",
                            int(segment.reasons["backchannel_count"]),
                            int(segment.reasons["internal_events"])]
                else:
                    row += [len(segment.events),
                            f"{segment.reasons['min_sep_s']:.3f}",
                            int(segment.reasons["turn_takings"]),
                            f"{segment.reasons['balance']:.3f}",
                            "".join(str(e.eot_type) for e in segment.events)]
                writer.writerow(row)
        print(f"\nwrote {args.csv}")

    if args.inspect:
        if args.inspect > len(ranked):
            print(f"\nrank {args.inspect} does not exist ({len(ranked)} segments)")
            return 1
        inspect(ranked[args.inspect - 1], scene2=args.scene2)

    if args.export:
        if args.export > len(ranked):
            print(f"\nrank {args.export} does not exist ({len(ranked)} segments)")
            return 1
        chosen = ranked[args.export - 1]
        name = args.name or f"{chosen.stem}_{int(chosen.start):03d}s"
        tail = args.hold_tail if args.hold_tail is not None \
            else (SCENE2_TAIL_SECONDS if args.scene2 else 0.0)
        path = export(chosen, name, yields_to_user=args.scene2, tail_seconds=tail)
        print(f"\nexported rank {args.export} -> {path.relative_to(REPO)}")
        print(f"  {chosen.stem} {chosen.start:.2f}-{chosen.end:.2f} s, "
              f"{len(chosen.events)} events, {chosen.duration:.2f} s")

        if args.scene2:
            problems = check_scene2_document(json.loads(path.read_text(encoding="utf-8")))
            for problem in problems:
                print(f"  SELF-CHECK FAILED: {problem}")
            if problems:
                return 1
            print("  scene-2 self-check passed")

    return 0


if __name__ == "__main__":
    sys.exit(main())
