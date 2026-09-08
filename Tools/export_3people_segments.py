"""Export the chosen 3People-2022 windows as study segments.

Study 2 replaces study 1's corpus and nothing else: the seven windows chosen on
2026-09-08 are written as ordinary ``segment.json`` folders under
``Assets/DemoSegments/`` (schema ``gazecontrol.demo-segment/3``), so the study
scene, the gaze conditions, the baker, the clip browser and the session runner
play them through exactly the code that played the TalkingWithHands clips.

What the document carries that a study-1 export does not:

* **Speaker codes are remapped onto the study-1 contract.** The two takers
  become speaker 1 (agent A, the lower PC number) and speaker 2 (agent B); the
  listener -- the participant no end-of-turn event in the window names --
  becomes speaker 0, the user, who is the study participant. The true PC numbers
  and subject names are kept on each agent record as provenance.
* **The listener's seat**, measured from the converted motion rather than
  authored: the mean of the listener's eye position over the window, and the yaw
  towards the midpoint of the two takers' eyes at the first frame. The scene
  turns and slides the whole recorded room so that this seat lands on the study
  rig's fixed viewpoint and that facing lands on the participant's initial view
  direction (``RoomPlacement``). Positions are written in Unity's frame (the
  corpus x is mirrored, as ``SmplxAnimUtils.PositionToUnity`` does), so the
  scene applies them without knowing the corpus's handedness.

The rest follows ``find_demo_segments.py``: the two takers' audio is trimmed to
the window and peak-normalised as a pair, their voices are measured by median
F0 for texture matching, the turn schedule is derived from the events, and the
transcript is written for the clip browser.

Run from the repo's uv environment::

    uv run python Tools/export_3people_segments.py            # all seven
    uv run python Tools/export_3people_segments.py --only study2_c3
"""

from __future__ import annotations

import argparse
import json
import math
import sys
import wave
from array import array
from dataclasses import dataclass
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import convert_3people_smplx as converter  # noqa: E402
import find_3people_segments as finder  # noqa: E402
import find_demo_segments as twh  # noqa: E402

REPO = Path(__file__).resolve().parent.parent
SEGMENT_ROOT = REPO / "Assets" / "DemoSegments"
SCHEMA = "gazecontrol.demo-segment/3"
USER_CODE = 0
LEFT_EYE, RIGHT_EYE = 23, 24  # SMPL-X joint indices of left/right_eye_smplhf

# Unlike TalkingWithHands this corpus names its participants, so a voice the
# pitch bands cannot classify is settled by the register rather than by ear.
# Both measured inside the 155-175 Hz gap (Maryum 170 Hz in 12-15-2021 S2 and
# 200 Hz in S4; May 158 Hz); the measurement is still written.
VOICE_BY_SUBJECT = {"Maryum": "female", "May": "female"}


@dataclass(frozen=True)
class Window:
    name: str
    date: str
    session: int
    start: float
    end: float


# The seven clips chosen by the user on 2026-09-08 (Assets/Docs/3people-motion-sources.md).
CHOSEN = (
    Window("study2_c1", "12-15-2021", 2, 36.97, 55.52),
    Window("study2_c2", "12-15-2021", 2, 491.40, 506.42),
    Window("study2_c3", "12-15-2021", 4, 534.37, 552.87),
    Window("study2_c4", "01-28-2022", 1, 199.38, 212.42),
    Window("study2_c5", "01-28-2022", 2, 427.27, 438.13),
    Window("study2_c6", "01-28-2022", 2, 525.20, 536.18),
    Window("study2_c7", "03-04-2022", 2, 680.70, 691.58),
)


# --------------------------------------------------------------------------- roles


def events_in(session: finder.Session, window: Window) -> list[finder.Event]:
    """The events the schedule is built from: the finder's rule, with its lead and tail.

    An event whose neighbourhood touches the window's edge (study2_c4 ends on
    one) is not scheduled -- its pre-turn second would be cut, and study 1's
    final turn likewise carries no event -- but it still counts for deciding
    who the listener is, since it names two takers.
    """
    return [e for e in session.events
            if e.start >= window.start + finder.LEAD_SECONDS and e.end <= window.end - finder.TAIL_SECONDS]


def events_touching(session: finder.Session, window: Window) -> list[finder.Event]:
    return [e for e in session.events if window.start <= e.turn_time <= window.end]


def resolve_roles(events: list[finder.Event]) -> tuple[int, int, int]:
    """(agent A pc, agent B pc, listener pc): the takers by ascending PC, and the one no event names."""
    named = {e.first_speaker for e in events} | {e.second_speaker for e in events}
    listeners = [pc for pc in finder.PARTICIPANTS if pc not in named]
    if len(listeners) != 1:
        raise ValueError(f"the window names {sorted(named)}; exactly one participant must be unnamed")
    takers = sorted(named)
    return takers[0], takers[1], listeners[0]


# --------------------------------------------------------------------------- audio


def audio_path(window: Window, pc: int) -> Path:
    return finder.DATA_ROOT / "audio_text" / window.date / f"Session_{window.session}_PC_{pc}_audio.wav"


def downmix(samples: array, channels: int) -> array:
    """Stereo to mono by averaging. The 3People tracks are stereo files of one microphone."""
    if channels == 1:
        return samples
    mono = array("h", (
        int(round(sum(samples[i:i + channels]) / channels))
        for i in range(0, len(samples) - channels + 1, channels)))
    return mono


def measure_pitch(window: Window, pc: int, session: finder.Session) -> float | None:
    """Median F0 over the taker's own utterances in the whole session; same estimator as study 1."""
    frame_length = int(twh.PITCH_FRAME_SECONDS * twh.PITCH_RATE)
    min_lag = int(twh.PITCH_RATE / twh.PITCH_MAX_HZ)
    max_lag = int(twh.PITCH_RATE / twh.PITCH_MIN_HZ)
    estimates: list[float] = []

    with wave.open(str(audio_path(window, pc)), "rb") as reader:
        rate = reader.getframerate()
        channels = reader.getnchannels()
        for utterance in session.utterances:
            if utterance.speaker != pc or utterance.is_tag_only or utterance.is_backchannel:
                continue
            if len(estimates) >= twh.PITCH_FRAMES:
                break
            if utterance.end - utterance.start < twh.PITCH_FRAME_SECONDS * 2:
                continue

            reader.setpos(int(utterance.start * rate))
            raw = array("h")
            raw.frombytes(reader.readframes(int((utterance.end - utterance.start) * rate)))
            if sys.byteorder == "big":
                raw.byteswap()

            signal = twh.decimate(raw, channels)
            for offset in range(0, len(signal) - frame_length, frame_length):
                if len(estimates) >= twh.PITCH_FRAMES:
                    break
                f0 = twh.frame_pitch(signal[offset:offset + frame_length], min_lag, max_lag)
                if f0 is not None:
                    estimates.append(f0)

    if len(estimates) < 10:
        return None
    estimates.sort()
    return estimates[len(estimates) // 2]


# --------------------------------------------------------------------------- seat


def eye_positions(clip: Path, first: int, count: int) -> np.ndarray:
    """(count, 3) eye-midpoint positions in the corpus frame (right-handed, Y-up), by FK of the default body."""
    z = np.load(clip)
    poses = z["poses"][first:first + count].astype(float)
    trans = z["trans"][first:first + count].astype(float)
    joints = converter.forward_kinematics(poses, trans, converter.load_template_joints())
    return 0.5 * (joints[:, LEFT_EYE] + joints[:, RIGHT_EYE])


def to_unity(p: np.ndarray) -> np.ndarray:
    """SmplxAnimUtils.PositionToUnity: mirror x."""
    return np.array([-p[0], p[1], p[2]])


def measure_seat(clips: dict[int, Path], roles: tuple[int, int, int], first: int, count: int) -> dict:
    a, b, listener = roles
    listener_eyes = eye_positions(clips[listener], first, count)
    seat = to_unity(listener_eyes.mean(axis=0))
    midpoint = to_unity(0.5 * (eye_positions(clips[a], first, 1)[0] + eye_positions(clips[b], first, 1)[0]))

    to_target = midpoint - seat
    horizontal = math.hypot(to_target[0], to_target[2])
    if horizontal < 0.05:
        raise ValueError("the seat is level with the takers' midpoint; the facing is undefined")

    # Unity yaw: atan2(x, z), clockwise from +Z seen from above -- the same
    # quantity ThreePartyViewpoint measures in the replay scene.
    yaw = math.degrees(math.atan2(to_target[0], to_target[2]))
    return {
        "valid": True,
        "x": round(float(seat[0]), 4),
        "y": round(float(seat[1]), 4),
        "z": round(float(seat[2]), 4),
        "yawDegrees": round(yaw, 3),
        "takersMidpointDistance": round(float(horizontal), 3),
    }


# --------------------------------------------------------------------------- export


def code_of(pc: int, roles: tuple[int, int, int]) -> int:
    a, b, listener = roles
    return {a: 1, b: 2, listener: USER_CODE}[pc]


def build_turns(events: list[finder.Event], window: Window, roles: tuple[int, int, int]) -> list[dict]:
    """As find_demo_segments.build_turns for a scene-1 segment: each event ends a turn, the last runs out."""
    turns = []
    previous = 0.0
    for index, event in enumerate(events):
        turns.append({
            "speaker": code_of(event.first_speaker, roles),
            "addressee": code_of(event.second_speaker, roles),
            "startTime": round(previous, 4),
            "endTime": round(event.turn_time - window.start, 4),
            "eventIndex": index,
        })
        previous = event.turn_time - window.start

    last = events[-1]
    turns.append({
        "speaker": code_of(last.second_speaker, roles),
        "addressee": code_of(last.first_speaker, roles),
        "startTime": round(previous, 4),
        "endTime": round(window.end - window.start, 4),
        "eventIndex": -1,
    })
    return turns


def export(window: Window) -> Path:
    session = finder.load_session(window.date, window.session)
    events = events_in(session, window)
    if not events:
        raise ValueError(f"{window.name}: no end-of-turn event inside {window.start}-{window.end} s")

    roles = resolve_roles(events_touching(session, window))
    if len(events_touching(session, window)) != len(events):
        print(f"  note: {window.name} has an event on its edge that is not scheduled")
    a, b, listener = roles
    clips = finder.motion_clips(window.date, window.session)

    duration = window.end - window.start
    start_frame = int(round(window.start * finder.MOTION_FPS))
    frame_count = int(round(duration * finder.MOTION_FPS))

    directory = SEGMENT_ROOT / window.name
    directory.mkdir(parents=True, exist_ok=True)

    # One gain for both takers from the louder, as study 1 does: the balance
    # between two close-mic tracks is recorded rather than incidental.
    trims = {}
    for pc in (a, b):
        samples, params = twh.read_trim(audio_path(window, pc), window.start, window.end)
        trims[pc] = (downmix(samples, params["channels"]), {**params, "channels": 1})
    peak = max(max(abs(v) for v in samples) for samples, _ in trims.values()) / 32768.0
    gain = twh.PEAK_TARGET / peak if peak > 0 else 1.0

    agents = []
    for code, pc in ((1, a), (2, b)):
        wav_name = f"pc{pc}.wav"
        samples, params = trims[pc]
        written = twh.write_wav(directory / wav_name, samples, params, gain)
        pitch = measure_pitch(window, pc, session)
        voice = twh.voice_class(pitch)
        if voice == "unclear":
            voice = VOICE_BY_SUBJECT.get(session.names[pc], voice)
        agents.append({
            "speaker": code,
            "side": f"pc{pc}",
            "pc": pc,
            "subject": session.names[pc],
            "audio": f"Assets/DemoSegments/{window.name}/{wav_name}",
            "motion": str(clips[pc]),
            "audioSamples": written,
            "voicePitchHz": round(pitch, 1) if pitch else None,
            "voice": voice,
        })

    document = {
        "schema": SCHEMA,
        "name": window.name,
        "stem": session.id,
        "corpus": "3People-2022",
        "sourceStartSeconds": round(window.start, 4),
        "sourceEndSeconds": round(window.end, 4),
        "durationSeconds": round(duration, 4),
        "motionFrameRate": finder.MOTION_FPS,
        "motionStartFrame": start_frame,
        "motionFrameCount": frame_count,
        "audioGain": round(gain, 3),
        "audioPeakBeforeGain": round(peak, 5),
        "yieldsToUser": False,
        "tailSeconds": 0.0,
        "listener": {"pc": listener, "subject": session.names[listener]},
        "seat": measure_seat(clips, roles, start_frame, frame_count),
        "agents": agents,
        "turns": build_turns(events, window, roles),
        "events": [
            {
                "index": index,
                "corpusIndex": event.index,
                "eotType": event.eot_type,
                "eotTypeName": finder.EOT_TYPE_NAMES[event.eot_type],
                "firstSpeaker": code_of(event.first_speaker, roles),
                "secondSpeaker": code_of(event.second_speaker, roles),
                "turnTime": round(event.turn_time - window.start, 4),
                "startTime": round(event.start - window.start, 4),
                "endTime": round(event.end - window.start, 4),
            }
            for index, event in enumerate(events)
        ],
        "utterances": [
            {
                "speaker": code_of(u.speaker, roles),
                "startTime": round(u.start - window.start, 4),
                "endTime": round(u.end - window.start, 4),
                "text": u.text,
            }
            for u in session.utterances
            if u.end > window.start and u.start < window.end and not u.is_tag_only
        ],
        "selection": {"generator": "Tools/export_3people_segments.py", "chosen": "2026-09-08 by hand"},
    }

    for problem in check_document(document):
        raise ValueError(f"{window.name}: {problem}")

    path = directory / "segment.json"
    path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return path


def check_document(document: dict) -> list[str]:
    """The contract the scene relies on, checked on what was written rather than on what was meant."""
    problems = []
    codes = {a["speaker"] for a in document["agents"]}
    if codes != {1, 2}:
        problems.append(f"agents carry speaker codes {sorted(codes)}, not 1 and 2")
    for turn in document["turns"]:
        if turn["speaker"] == USER_CODE or turn["addressee"] == USER_CODE:
            problems.append("a turn names the user; the listener must hold no turn")
    if document["turns"][-1]["eventIndex"] != -1:
        problems.append("the final turn must carry no event")
    if not document["seat"]["valid"]:
        problems.append("no seat")
    for agent in document["agents"]:
        if agent["voice"] not in ("male", "female"):
            problems.append(f"speaker {agent['speaker']} ({agent['subject']}) has voice '{agent['voice']}'")
    expected = document["motionFrameCount"]
    for agent in document["agents"]:
        rate = 48000
        if abs(agent["audioSamples"] / rate - document["durationSeconds"]) > 0.02:
            problems.append(f"speaker {agent['speaker']} audio is {agent['audioSamples'] / rate:.2f} s")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--only", help="export one of the chosen names, e.g. study2_c3")
    args = parser.parse_args()

    for window in CHOSEN:
        if args.only and window.name != args.only:
            continue
        path = export(window)
        document = json.loads(path.read_text(encoding="utf-8"))
        seat = document["seat"]
        agents = ", ".join(f"{a['speaker']}={a['subject']} pc{a['pc']} {a['voice']} {a['voicePitchHz']} Hz"
                           for a in document["agents"])
        print(f"{window.name}: {document['stem']} {window.start:.2f}-{window.end:.2f} s "
              f"({document['durationSeconds']:.1f} s, {len(document['events'])} event) -- {agents}; "
              f"listener {document['listener']['subject']} pc{document['listener']['pc']} "
              f"seat ({seat['x']:.2f}, {seat['y']:.2f}, {seat['z']:.2f}) yaw {seat['yawDegrees']:.1f} "
              f"gain {document['audioGain']}x")
    return 0


if __name__ == "__main__":
    sys.exit(main())
