"""Transcribe the paper's gaze prototypes into the C# pattern library.

The ICMI paper shows fifteen prototypes -- Figures 6, 7 and 8, five subfigures
each, at filter sizes 20, 30 and 40 respectively. Every one is a class-labelled
sub-pattern of gaze in the second before an end-of-turn event, and the proposed
condition now draws one at random from the pool matching each event's class.
Fifteen prototypes is past the point of transcribing by hand, so this reads the
raw prototype archives and writes the library out.

Each ``raw_prototypes/pN_ksK_i.npz`` holds ``kernel`` of shape ``(K, 3)``: one
column per conversational role, in the paper's plotting order

    column 0 = the current speaker's gaze
    column 1 = the next speaker's gaze
    column 2 = the listener's gaze

and one value per role-as-target

    0 = none of the three (gaze aversion)
    1 = at the current speaker
    2 = at the next speaker
    3 = at the listener

which is exactly the y-axis of the paper's figures. ``p_label`` gives the EoT
class (1 interruption, 2 overlapping, 3 turn-taking) and ``subseq_start_idx``
gives, per matched subsequence, where in the 60-frame (1 s at 60 fps) pre-turn
window that subsequence began. The rank-0 entry is used, matching the choice
already recorded for the two hand-transcribed patterns: the median placement
would put a prototype where no single observation actually sat.

The kernel is transcribed rather than any of the four matched subsequences --
the kernel *is* the prototype, and it reproduces the two patterns that were
hand-transcribed earlier frame for frame. That equivalence is asserted below,
so a future change to the decoding cannot silently rewrite the signed-off
demo's behaviour.

Regenerate with::

    python Tools/build_gaze_patterns.py
"""

from __future__ import annotations

import argparse
import ast
import struct
import zipfile
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PROTOTYPES = Path(r"F:\Research\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking\raw_prototypes")
OUTPUT = REPO / "Assets" / "GazeControl" / "Scripts" / "Runtime" / "Gaze" / "Policy" / "GazePatterns.g.cs"

# The fifteen prototypes the paper prints, in figure order. Figure 6 is the
# filter-size-20 plate, 7 is size 30 and 8 is size 40; subfigures run a-e down
# each plate. Keyed by the figure reference because that is how the paper, and
# every note written about this demo so far, refers to them.
FIGURES = {
    "Fig6a": "p1_ks20_5",
    "Fig6b": "p1_ks20_6",
    "Fig6c": "p2_ks20_0",
    "Fig6d": "p3_ks20_3",
    "Fig6e": "p3_ks20_14",
    "Fig7a": "p1_ks30_18",
    "Fig7b": "p2_ks30_9",
    "Fig7c": "p2_ks30_16",
    "Fig7d": "p3_ks30_2",
    "Fig7e": "p3_ks30_17",
    "Fig8a": "p1_ks40_4",
    "Fig8b": "p1_ks40_12",
    "Fig8c": "p2_ks40_9",
    "Fig8d": "p3_ks40_4",
    "Fig8e": "p3_ks40_19",
}

ROLE_OF_VALUE = {0: "None", 1: "CurrentSpeaker", 2: "NextSpeaker", 3: "Listener"}
CLASS_OF_LABEL = {1: "Interruption", 2: "Overlapping", 3: "TurnTaking"}
CLASS_PROSE = {"Interruption": "interruption", "Overlapping": "overlapping", "TurnTaking": "turn-taking"}
TRACKS = ["CurrentSpeakerTrack", "NextSpeakerTrack", "ListenerTrack"]

# Short descriptions of the three patterns the paper itself singles out in
# prose; the rest are described by their structure alone.
NOTES = {
    "p1_ks20_5": "the next speaker turns to the current speaker, the current speaker turns to the listener, and the listener keeps watching the current speaker (paper Sec. 5)",
    "p1_ks30_18": "the listener averts gaze while the next speaker turns to the current speaker, before an interruption (paper Sec. 5)",
    "p3_ks40_4": "the current speaker glances at the next speaker twice before yielding the turn (paper Sec. 5)",
}


def read_npy(raw: bytes):
    """Minimal .npy reader - numpy is not installed in this project's Python."""
    if raw[:6] != b"\x93NUMPY":
        raise ValueError("not a .npy array")
    major = raw[6]
    prefix = 10 if major == 1 else 12
    header_length = struct.unpack("<H" if major == 1 else "<I",
                                  raw[8:10] if major == 1 else raw[8:12])[0]
    header = ast.literal_eval(raw[prefix:prefix + header_length].decode("latin1").strip())
    body = raw[prefix + header_length:]

    count = 1
    for dimension in header["shape"]:
        count *= dimension

    code = {"<f4": "f", "<i8": "q", "<i4": "i"}[header["descr"]]
    values = list(struct.unpack(f"<{count}{code}", body[: count * struct.calcsize(code)]))
    return header["shape"], values


def load(prototype: str) -> dict:
    with zipfile.ZipFile(PROTOTYPES / f"{prototype}.npz") as archive:
        arrays = {name[: -len(".npy")]: read_npy(archive.read(name)) for name in archive.namelist()}

    frames, roles = arrays["kernel"][0]
    kernel = arrays["kernel"][1]
    tracks = [[int(kernel[frame * roles + role]) for frame in range(frames)] for role in range(roles)]

    return {
        "id": prototype,
        "frames": frames,
        "tracks": tracks,
        "eot_class": CLASS_OF_LABEL[arrays["p_label"][1][0]],
        "window_offset": arrays["subseq_start_idx"][1][0],
    }


def run_length(track: list[int]) -> list[tuple[int, int]]:
    runs: list[tuple[int, int]] = []
    for value in track:
        if runs and runs[-1][1] == value:
            runs[-1] = (runs[-1][0] + 1, value)
        else:
            runs.append((1, value))
    return runs


def verify(patterns: dict[str, dict]) -> None:
    """Assert the two hand-transcribed patterns come back unchanged.

    These are the prototypes the existing demo was signed off on. If a decoding
    change ever flips a column or a value code, this is where it should stop.
    """
    expected = {
        "p3_ks40_4": (4, [(1, 0), (2, 2), (1, 0), (3, 2), (33, 0)], [(40, 0)]),
        "p3_ks30_17": (29, [(5, 2), (17, 0), (8, 2)], [(20, 1), (10, 0)]),
    }

    for prototype, (offset, speaker, next_speaker) in expected.items():
        pattern = next(p for p in patterns.values() if p["id"] == prototype)
        actual = (pattern["window_offset"],
                  run_length(pattern["tracks"][0]),
                  run_length(pattern["tracks"][1]))
        if actual != (offset, speaker, next_speaker):
            raise SystemExit(
                f"{prototype} no longer decodes to its hand-transcribed form:\n"
                f"  expected {(offset, speaker, next_speaker)}\n  got      {actual}")


def summarise(pattern: dict) -> str:
    """One line naming what each role does, so the generated file reads."""
    note = NOTES.get(pattern["id"])
    if note:
        return note

    parts = []
    for track, label in zip(pattern["tracks"], ("current speaker", "next speaker", "listener")):
        moves = [ROLE_OF_VALUE[value].lower() for _, value in run_length(track)]
        parts.append(f"{label}: " + " -> ".join("aversion" if m == "none" else m for m in moves))
    return "; ".join(parts)


def emit_track(track: list[int]) -> str:
    lines = []
    for frames, value in run_length(track):
        lines.append(f"                ({frames} * Frame, GazeRole.{ROLE_OF_VALUE[value]}),")
    return "\n".join(lines)


def emit(patterns: dict[str, dict]) -> str:
    out: list[str] = []
    add = out.append

    add("// <auto-generated>")
    add("//     Generated by Tools/build_gaze_patterns.py from the ICMI paper's raw")
    add("//     prototype archives. Do not edit by hand - regenerate instead.")
    add("// </auto-generated>")
    add("")
    add("using System.Collections.Generic;")
    add("")
    add("namespace GazeControl.Gaze.Policy")
    add("{")
    add("    /// <summary>The fifteen pre-turn prototypes printed in the paper, by figure.</summary>")
    add("    public enum PreTurnPattern")
    add("    {")
    for figure, prototype in FIGURES.items():
        add(f"        /// <summary>{prototype}, class {patterns[figure]['eot_class']}.</summary>")
        add(f"        {figure},")
    add("    }")
    add("")
    add("    /// <summary>")
    add("    /// Pre-turn gaze prototypes transcribed from the ICMI paper's raw prototype")
    add("    /// data (Figures 6-8; filter sizes 20, 30 and 40). Each is a one-second")
    add("    /// window at 60 fps ending at an end-of-turn event, with one track per")
    add("    /// conversational role.")
    add("    /// </summary>")
    add("    public static class GazePatterns")
    add("    {")
    add("        const float Frame = 1f / 60f;")
    add("")

    for figure, prototype in FIGURES.items():
        pattern = patterns[figure]
        add(f"        /// <summary>")
        add(f"        /// {figure} ({prototype}, {CLASS_PROSE[pattern['eot_class']]}): {summarise(pattern)}.")
        add(f"        /// Filter size {pattern['frames']}; the rank-0 matched subsequence starts at")
        add(f"        /// window frame {pattern['window_offset']} of 60.")
        add(f"        /// </summary>")
        add(f"        public static readonly GazePattern {figure} = new()")
        add("        {")
        add(f"            Name = \"{figure} {prototype} ({CLASS_PROSE[pattern['eot_class']]})\",")
        add(f"            PrototypeId = \"{prototype}\",")
        add(f"            EotType = EotType.{pattern['eot_class']},")
        add(f"            WindowOffsetSeconds = {pattern['window_offset']} * Frame,")
        for name, track in zip(TRACKS, pattern["tracks"]):
            add(f"            {name} = new[]")
            add("            {")
            add(emit_track(track))
            add("            },")
        add("        };")
        add("")

    add("        /// <summary>Every prototype the paper prints, in figure order.</summary>")
    add("        public static readonly IReadOnlyList<GazePattern> All = new[]")
    add("        {")
    for figure in FIGURES:
        add(f"            {figure},")
    add("        };")
    add("")

    for label in ("Interruption", "Overlapping", "TurnTaking"):
        members = [f for f, p in patterns.items() if p["eot_class"] == label]
        add(f"        /// <summary>The prototypes the paper labels {label.lower()}.</summary>")
        add(f"        public static readonly IReadOnlyList<GazePattern> {label}Pool = new[]")
        add("        {")
        for figure in members:
            add(f"            {figure},")
        add("        };")
        add("")

    add("        /// <summary>The named prototype.</summary>")
    add("        public static GazePattern Of(PreTurnPattern pattern) => All[(int)pattern];")
    add("")
    add("        /// <summary>")
    add("        /// The prototypes measured on events of this class. The proposed")
    add("        /// condition draws from the pool matching each event's own type, so an")
    add("        /// interruption never gets a turn-taking prototype played over it.")
    add("        /// </summary>")
    add("        public static IReadOnlyList<GazePattern> PoolFor(EotType eotType) => eotType switch")
    add("        {")
    add("            EotType.Interruption => InterruptionPool,")
    add("            EotType.Overlapping => OverlappingPool,")
    add("            _ => TurnTakingPool,")
    add("        };")
    add("    }")
    add("}")

    return "\n".join(out) + "\n"


def main() -> None:
    global PROTOTYPES

    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--prototypes", type=Path, default=PROTOTYPES)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    args = parser.parse_args()
    PROTOTYPES = args.prototypes

    patterns = {figure: load(prototype) for figure, prototype in FIGURES.items()}
    verify(patterns)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(emit(patterns), encoding="utf-8")

    counts = {label: sum(1 for p in patterns.values() if p["eot_class"] == label)
              for label in ("Interruption", "Overlapping", "TurnTaking")}
    print(f"wrote {args.output.relative_to(REPO)}")
    print("  pools: " + ", ".join(f"{label} {count}" for label, count in counts.items()))


if __name__ == "__main__":
    main()
