"""Cut a demo video out of rendered takes, without re-rendering a frame.

Stage two of the demo-video pipeline. Stage one is Unity's
``GazeControl -> Demo Video -> Render Takes``, which plays each clip under each
method and writes, per take::

    Recordings/Demos/<clip>/<method>/frame_0000.png ...
    Recordings/Demos/<clip>/<method>/audio.wav
    Recordings/Demos/<clip>/<method>/take.json

Everything this script adds is text -- a title card before each take, the
method's name in the corner, and the conversation's own transcript as
subtitles -- and text is the part that gets rewritten. So the frames are
rendered once and kept, and this runs as often as the wording changes.

**All the wording lives in one editable file**, ``output/demo_video.json``,
written on the first run from whatever takes are on disk and never overwritten
afterwards (``--rebuild-config`` if you want it back). Edit the titles there
and run this again; the render is untouched.

The transcript comes from each clip's own ``segment.json``, so the subtitles
are the corpus's captions rather than anything typed here. They are automatic
captions and carry the corpus's own noise -- read a clip before trusting a
line.

Needs only ffmpeg on PATH (with libx264 and libass, which the usual Windows
builds have). Run it from the repo root::

    uv run python Tools/compose_demo_video.py
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

TAKES_ROOT = Path("Recordings/Demos")
CONFIG_PATH = Path("output/demo_video.json")
OUTPUT_PATH = Path("output/demo_video.mp4")

# Where the framing window's "Save a preview frame" button writes, and so where
# --preview looks unless told otherwise.
PREVIEW_FRAME_PATH = Path("output/demo_preview_frame.png")
PREVIEW_OUTPUT_PATH = Path("output/demo_preview.png")
SEGMENT_ROOT = Path("Assets/DemoSegments")

CONFIG_SCHEMA = "gazecontrol.demo-video/1"
TAKE_SCHEMA = "gazecontrol.demo-take/1"

# The three conditions, named for an audience that has not read the code.
DEFAULT_METHOD_NAMES = {
    "SpeakerFollowing": "Baseline B",
    "RoleConditioned": "Baseline A",
    "Proposed": "Proposed",
}

# The order the methods play within a clip: the two baselines, then ours, so a
# viewer has seen what is being improved on before the improvement.
DEFAULT_METHOD_ORDER = ["SpeakerFollowing", "RoleConditioned", "Proposed"]

# Which method the preview shows unless told otherwise. Baseline A rather than
# the proposed one (user, 2026-09-10): the slate is one of the three pictures
# being checked, and a method whose blurb has been emptied previews a slate with
# a blank line on it, which shows the layout rather than the wording.
DEFAULT_PREVIEW_METHOD = "RoleConditioned"

# How a clip's slate names it. **The number is the clip's place in this video**
# (user, 2026-09-10), not a fixed label per clip: cutting a video of clips 1, 3
# and 7 should show Conversation 1, 2, 3, because the viewer is counting what
# they are shown and has no idea the study had seven. `{n}` is that place,
# `{clip}` the folder name.
DEFAULT_CLIP_TITLE_FORMAT = "Conversation {n}"

DEFAULT_METHOD_BLURBS = {
    "SpeakerFollowing": "Looks at whoever is speaking. Never averts.",
    "RoleConditioned": "Role-conditioned gaze, refitted to our corpus.",
    "Proposed": "Measured gaze patterns played before each turn boundary.",
}

# Point sizes against a 1080-high frame. In the config beside the wording,
# because how big a caption is is part of getting the caption right, and the
# whole point of this stage is settling that without re-rendering.
DEFAULT_FONT_SIZES = {
    "Paper": 72,
    "Byline": 38,
    "Title": 96,
    "Method": 64,
    "Blurb": 40,
    "Badge": 42,
    "Sub": 46,
}

# The card the video opens on. The title is the paper's own, from its main.tex,
# so the video cannot quietly carry a different one from the submission; edit it
# here if the paper's changes, because nothing makes this follow it.
#
# **The byline is empty, and that is the safe default rather than an oversight**
# (user, 2026-09-10). CHI reviews double-blind and the supplementary material is
# reviewed with the paper, so a video that names its authors withdraws the
# submission's anonymity. A default that leaks identity is the wrong way round:
# forgetting to anonymise costs far more than forgetting to de-anonymise. The
# real names are in the paper's own main.tex and go in the config once it is
# accepted. A byline set here is drawn under the title; a real newline in it
# breaks the line, so the config never carries the subtitle format's own codes.
DEFAULT_OPENING = {
    "seconds": 5.0,
    "title": "Interpretable Subsequence Gaze Modeling of Turn Boundaries "
             "in Multi-Party Interaction",
    "byline": "",
}

# Where each piece of text sits, and how far it is kept off the frame's edges,
# against a 1080-high frame. The corner is where the text is anchored; marginX
# also bounds where a long line wraps, which is why the subtitles' is wide and
# the badge's is not.
DEFAULT_LAYOUT = {
    # Drawn over the video: anchored to a corner, and held off it by the margins.
    "Badge": {"corner": "top-left", "marginX": 64, "marginY": 48},
    "Sub": {"corner": "bottom", "marginX": 240, "marginY": 72},

    # On a card of their own. The card stacks them vertically, so only marginX
    # means anything here — and what it means is **how wide a line may run
    # before it wraps**, which is what decides how much air ends up either side.
    # The title gets more room than the rest because it is a sentence long.
    "Paper": {"marginX": 160},
    "Byline": {"marginX": 240},
    "Title": {"marginX": 240},
    "Method": {"marginX": 240},
    "Blurb": {"marginX": 240},
}

# Which of the two kinds each piece of text is. Text on a card takes marginX
# alone; anything else set on it is a key that would do nothing, so it is
# reported rather than accepted in silence.
OVERLAY_STYLES = ("Badge", "Sub")
CARD_STYLES = ("Paper", "Byline", "Title", "Method", "Blurb")

# Cards centre their text and place it by hand, with \pos.
CARD_ALIGNMENT = 5

# ASS anchors text by the numeric keypad. Spelled out because "top-left" is what
# someone moving a badge actually wants to write, and 7 is not.
CORNERS = {
    "bottom-left": 1, "bottom": 2, "bottom-centre": 2, "bottom-center": 2, "bottom-right": 3,
    "left": 4, "middle-left": 4,
    "centre": 5, "center": 5, "middle": 5,
    "right": 6, "middle-right": 6,
    "top-left": 7, "top": 8, "top-centre": 8, "top-center": 8, "top-right": 9,
}

# Colours as ASS &HBBGGRR, which is backwards from the usual hex.
SLATE_BACKGROUND = "0x12151c"
SUBTITLE_COLOUR = "&H00FFFFFF"
BADGE_COLOUR = "&H00C8C8C8"


@dataclass(frozen=True)
class Take:
    """One rendered clip-and-method: frames, sound, and what it is."""

    clip: str
    method: str
    seed: int
    directory: Path
    segment_path: Path
    frame_rate: int
    frame_count: int
    width: int
    height: int

    @property
    def audio(self) -> Path:
        return self.directory / "audio.wav"

    @property
    def key(self) -> str:
        return f"{self.clip}/{self.method}"


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)

    if shutil.which("ffmpeg") is None:
        print("ffmpeg is not on PATH; nothing to compose with.", file=sys.stderr)
        return 2

    if args.write_config:
        load_or_create_config(args.config, scan_takes(args.takes), rebuild=args.rebuild_config)
        print(f"Edit {args.config} — fontSizes.Badge is the method's name, fontSizes.Sub the subtitles.")
        return 0

    if args.preview:
        return preview_frame(args)

    takes = scan_takes(args.takes)
    if not takes:
        print(
            f"No rendered takes under {args.takes}/. Render them first with "
            "GazeControl -> Demo Video -> Render Takes.",
            file=sys.stderr,
        )
        return 1

    config = load_or_create_config(args.config, takes, rebuild=args.rebuild_config)
    wanted = running_order(config, takes)
    order = [
        entry
        for entry in wanted
        if f"{entry['clip']}/{entry['method']}" in takes
        and (not args.only or entry["clip"] in args.only)
    ]

    # Said out loud rather than passed over: a clip listed in the config and
    # missing from the video is either a render that has not happened or a
    # typo, and both look identical in the finished file.
    missing = sorted({e["clip"] for e in wanted} - {e["clip"] for e in order})
    if missing and not args.only:
        print(f"  not rendered, so not in the video: {', '.join(missing)}", file=sys.stderr)

    if not order:
        print("Nothing selected to compose.", file=sys.stderr)
        return 1

    order = number_clips(order, config)

    print(f"{len(order)} take(s) to cut, into {args.out}.")
    if args.dry_run:
        for entry in order:
            take = takes[f"{entry['clip']}/{entry['method']}"]
            print(f"  {take.key}: {take.frame_count} frames, seed {take.seed}")
        return 0

    args.out.parent.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="gazecontrol_demo_") as scratch:
        pieces: list[Path] = []
        work = Path(scratch)

        opening = build_opening(takes[f"{order[0]['clip']}/{order[0]['method']}"], config, work)
        if opening is not None:
            pieces.append(opening)

        for index, entry in enumerate(order):
            take = takes[f"{entry['clip']}/{entry['method']}"]

            if config["slateSeconds"] > 0:
                pieces.append(build_slate(take, entry, config, work, index))

            pieces.append(build_body(take, entry, config, work, index))

        concatenate(pieces, args.out, work)

    print(f"Wrote {args.out}.")
    return 0


def parse_args(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--takes", type=Path, default=TAKES_ROOT,
                        help=f"folder of rendered takes (default {TAKES_ROOT})")
    parser.add_argument("--config", type=Path, default=CONFIG_PATH,
                        help=f"the editable wording (default {CONFIG_PATH})")
    parser.add_argument("--out", type=Path, default=OUTPUT_PATH,
                        help=f"the finished video (default {OUTPUT_PATH}); with --preview, the stem the three PNGs are named from")
    parser.add_argument("--only", nargs="*", default=None,
                        help="compose only these clips, by name")
    parser.add_argument("--rebuild-config", action="store_true",
                        help="overwrite the config from what is on disk, losing hand edits")
    parser.add_argument("--write-config", action="store_true",
                        help="write the wording file (titles, names, font sizes) and stop, "
                             "so it can be edited before anything is rendered")
    parser.add_argument("--dry-run", action="store_true",
                        help="list what would be cut and stop")

    preview = parser.add_argument_group(
        "preview", "caption one still frame instead of cutting a video, to check the text")
    preview.add_argument("--preview", action="store_true",
                         help="write a single captioned frame and stop")
    preview.add_argument("--frame", type=Path, default=PREVIEW_FRAME_PATH,
                         help=f"the image to caption (default {PREVIEW_FRAME_PATH})")
    preview.add_argument("--clip", default=None,
                         help="whose transcript to read; defaults to the first clip with a segment")
    preview.add_argument("--method", default=DEFAULT_PREVIEW_METHOD,
                         help=f"which method the slate and badge name (default {DEFAULT_PREVIEW_METHOD})")
    preview.add_argument("--at", type=float, default=None,
                         help="the moment in the clip, seconds; defaults to the middle of its first line")
    return parser.parse_args(argv)


def clip_title(config: dict, clip: str, position: int) -> str:
    """
    What a slate calls a clip: its place in this video, not a fixed label.

    Numbered over what is actually composed, so a video of clips 1, 3 and 7
    counts 1, 2, 3 — the viewer is counting what they are shown, and a gap in
    the numbering only invites them to wonder what they missed.
    """
    template = config.get("clipTitleFormat") or DEFAULT_CLIP_TITLE_FORMAT
    try:
        return template.format(n=position, clip=clip)
    except (KeyError, IndexError) as bad:
        raise SystemExit(
            f"clipTitleFormat {template!r} uses {bad}, which is not something this fills in. "
            "Use {n} for the clip's place in the video and {clip} for its folder name.")


def number_clips(order: list[dict], config: dict) -> list[dict]:
    """Give every entry the title its clip's position earns it."""
    positions: dict[str, int] = {}
    for entry in order:
        positions.setdefault(entry["clip"], len(positions) + 1)

    return [
        {**entry, "title": clip_title(config, entry["clip"], positions[entry["clip"]])}
        for entry in order
    ]


def running_order(config: dict, takes: dict[str, Take]) -> list[dict]:
    """
    What to cut, in order: each clip in ``clips``, under each method in
    ``methods``.

    Two lists rather than a list of pairs, because what gets chosen is almost
    always a *clip* — cutting one conversation to look at it is the whole point
    of having the lists — and spelling out three pairs to drop one clip is a
    chore that invites a typo. Either list left empty means everything that was
    rendered, so a config written before the first take still composes.
    """
    clips = config.get("clips") or sorted({take.clip for take in takes.values()})
    methods = config.get("methods") or [
        m for m in DEFAULT_METHOD_ORDER if any(t.method == m for t in takes.values())
    ]

    return [{"clip": clip, "method": method} for clip in clips for method in methods]


def scan_takes(root: Path) -> dict[str, Take]:
    """Every take under ``root`` that carries a manifest, by "clip/method"."""
    takes: dict[str, Take] = {}

    for manifest_path in sorted(root.glob("*/*/take.json")):
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        if manifest.get("schema") != TAKE_SCHEMA:
            print(f"  skipping {manifest_path}: schema {manifest.get('schema')!r}", file=sys.stderr)
            continue

        take = Take(
            clip=manifest["clip"],
            method=manifest["condition"],
            seed=int(manifest["seed"]),
            directory=manifest_path.parent,
            segment_path=Path(manifest["segmentPath"]),
            frame_rate=int(manifest["frameRate"]),
            frame_count=int(manifest["frameCount"]),
            width=int(manifest["width"]),
            height=int(manifest["height"]),
        )
        takes[take.key] = take

    return takes


def default_config(takes: dict[str, Take]) -> dict:
    """
    Every setting at its default, for a fresh file and as the list of what a
    setting *is*.

    Falls back to what is knowable without a render — the exported segments and
    the three conditions — so the wording can be written and edited before the
    first take exists, which is when there is most reason to edit it.
    """
    clips = sorted({take.clip for take in takes.values()}) or exported_clips()
    methods = sorted({take.method for take in takes.values()}) or sorted(DEFAULT_METHOD_NAMES)

    return {
        "schema": CONFIG_SCHEMA,
        "slateSeconds": 3.0,
        "showBadge": True,
        "showSubtitles": True,
        "fontName": "Segoe UI",
        "fontSizes": dict(DEFAULT_FONT_SIZES),
        "layout": {name: dict(place) for name, place in DEFAULT_LAYOUT.items()},
        "opening": dict(DEFAULT_OPENING),
        "clips": list(clips),
        "methods": list(DEFAULT_METHOD_ORDER),
        "clipTitleFormat": DEFAULT_CLIP_TITLE_FORMAT,
        "methodNames": {m: DEFAULT_METHOD_NAMES.get(m, m) for m in methods},
        "methodBlurbs": {m: DEFAULT_METHOD_BLURBS.get(m, "") for m in methods},
        "speakerNames": {"0": "You", "1": "Agent A", "2": "Agent B"},
    }


def fill_gaps(config: dict, defaults: dict) -> list[str]:
    """
    Add whatever ``config`` has not got, one level into its dictionaries, and
    say what was added. Nothing present is changed.

    One level deep because that is where the settings are: a new font size or a
    newly exported clip's title should appear, and a value the operator has
    already set should not move. A list is left entirely alone once it exists —
    ``clips`` is a choice, and topping it up with clips they took out would undo
    the choice every run.
    """
    added = []

    for key, default in defaults.items():
        if key not in config:
            config[key] = default
            added.append(key)
            continue

        if isinstance(default, dict) and isinstance(config[key], dict):
            for sub, value in default.items():
                if sub not in config[key]:
                    config[key][sub] = value
                    added.append(f"{key}.{sub}")

    return added


def load_or_create_config(path: Path, takes: dict[str, Take], rebuild: bool,
                          write: bool = True) -> dict:
    """
    The wording, read from disk or built fresh.

    **Gaps are filled; nothing that is already there is touched.** A config that
    exists is the operator's, so a value they set keeps whatever they set it to
    for ever — but a key this script has since learned about is added, because
    otherwise gaining a setting would mean deleting the file and retyping every
    edit in it. ``--rebuild-config`` is the blunt way back and says so.

    ``write`` is false for a preview, which knows about exactly one take and
    would otherwise leave behind a config describing only that take.
    """
    if path.exists() and not rebuild:
        config = json.loads(path.read_text(encoding="utf-8"))
        if config.get("schema") != CONFIG_SCHEMA:
            raise SystemExit(f"{path} is schema {config.get('schema')!r}, not {CONFIG_SCHEMA}.")

        added = fill_gaps(config, default_config(takes))
        if added and write:
            path.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
            print(f"{path}: added {', '.join(added)}. Everything already in it is untouched.")

        for key in sorted(set(config) - set(default_config(takes))):
            print(f"  {path}: {key!r} is not a setting this reads; left alone.", file=sys.stderr)

        report_layout_problems(config)
        return config

    config = default_config(takes)

    if not write:
        print(f"No {path} yet; previewing with the default wording. "
              "Run --write-config to get a copy you can edit.")
        return config

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Wrote {path} — edit the titles there and run this again.")
    return config


# --------------------------------------------------------------------------
# Preview
# --------------------------------------------------------------------------

def preview_frame(args: argparse.Namespace) -> int:
    """
    Caption one still frame, to settle the wording and the placement before
    anything is rendered.

    It goes through the same styles, the same escaping and the same libass call
    the video does, so what this shows is what a take will carry -- a preview
    drawn any other way would only be a picture of a different program's idea of
    the text.
    """
    clip = args.clip or first_clip_with_a_segment()
    if clip is None:
        print(f"No segment found under {SEGMENT_ROOT}/; pass --clip.", file=sys.stderr)
        return 1

    segment_path = SEGMENT_ROOT / clip / "segment.json"
    utterances = read_utterances_at(segment_path, clip)
    at = args.at if args.at is not None else default_preview_time(utterances)
    line = utterance_at(utterances, at)

    # A rendered take is the best source there is — it is the picture the video
    # will actually carry — so the Unity capture is only the fallback for
    # previewing before anything has been rendered.
    frame = args.frame
    if args.frame == PREVIEW_FRAME_PATH:
        frame = take_frame(args.takes, clip, args.method, at, segment_path) or frame

    if not frame.exists():
        print(f"{frame} does not exist, and no rendered take to take a frame from. "
              "Either render, or capture one with GazeControl -> Demo Video -> "
              "Camera Framing -> Save a preview frame while a clip is playing.",
              file=sys.stderr)
        return 1

    width, height = png_size(frame)
    take = Take(
        clip=clip,
        method=args.method,
        seed=0,
        directory=frame.parent,
        segment_path=segment_path,
        frame_rate=30,
        frame_count=30,
        width=width,
        height=height,
    )

    config = load_or_create_config(args.config, {take.key: take}, rebuild=False, write=False)

    stem = (args.out if args.out != OUTPUT_PATH else PREVIEW_OUTPUT_PATH).with_suffix("")
    stem.parent.mkdir(parents=True, exist_ok=True)

    badge = config["methodNames"].get(take.method, take.method)
    spoken = f'"{line["text"]}"' if line else "(nothing is being said then)"
    written = []

    with tempfile.TemporaryDirectory(prefix="gazecontrol_preview_") as scratch:
        work = Path(scratch)

        # One of each kind of picture the video is made of, so a wording change
        # can be checked everywhere it lands in a single look.
        title_card = opening_script(take, config)
        if title_card is not None:
            out = stem.with_name(f"{stem.name}_title.png")
            draw_card(title_card, take, work, out, "title")
            written.append((out, "opening card"))
        else:
            print("  opening.seconds is 0, so there is no title card to preview.", file=sys.stderr)

        # Numbered as the video would number it, so the preview does not show a
        # different heading from the one the slate will carry.
        listed = config.get("clips") or []
        position = listed.index(clip) + 1 if clip in listed else 1
        entry = {"title": clip_title(config, clip, position)}

        out = stem.with_name(f"{stem.name}_slate.png")
        draw_card(slate_script_for(take, entry, config), take, work, out, "slate")

        # Said, because an empty blurb and a blurb that failed to draw look the
        # same on the card: a heading, a method, and a gap under it.
        blurb = config["methodBlurbs"].get(take.method, "")
        written.append((out, f"slate — {entry['title']} / {badge}"
                             + ("" if blurb else ", with no blurb set for this method")))

        out = stem.with_name(f"{stem.name}_clip.png")
        ass = work / "clip.ass"
        ass.write_text(preview_script(take, config, line), encoding="utf-8")
        run_ffmpeg(
            ["-i", str(frame.resolve()), "-vf", f"ass={ass.name}",
             "-frames:v", "1", "-y", str(out.resolve())],
            cwd=work,
        )
        written.append((out, f"{clip} at {at:.2f} s, badge {badge!r}, {spoken}"))

    print(f"{width}x{height}:")
    for out, what in written:
        print(f"  {out}  — {what}")
    return 0


def take_frame(takes_root: Path, clip: str, method: str, at: float,
               segment_path: Path) -> Path | None:
    """
    The rendered frame showing conversation second ``at``, or None if that take
    has not been rendered.

    The recorder starts before the conversation's clock does — it waits for the
    encoder, then the voices are scheduled a fraction ahead — so the take's
    frame 0 is not the clip's time 0. The lead is read back off the take rather
    than assumed: it is however many frames the take has beyond the segment's
    own length and tail.
    """
    manifest = takes_root / clip / method / "take.json"
    if not manifest.is_file() or not segment_path.is_file():
        return None

    take = json.loads(manifest.read_text(encoding="utf-8"))
    segment = json.loads(segment_path.read_text(encoding="utf-8"))

    rate = take["frameRate"]
    played = segment["durationSeconds"] + (segment.get("tailSeconds") or 1.5)
    lead = max(0, take["frameCount"] - round(played * rate))

    index = min(max(lead + round(at * rate), 0), take["frameCount"] - 1)
    frame = manifest.parent / f"frame_{index:04d}.png"
    return frame if frame.is_file() else None


def read_utterances_at(segment_path: Path, clip: str) -> list[dict]:
    """The clip's transcript, or nothing when the segment is not on this machine."""
    if not segment_path.is_file():
        print(f"  {clip}: {segment_path} not found; no subtitles for it.", file=sys.stderr)
        return []

    return list(json.loads(segment_path.read_text(encoding="utf-8")).get("utterances", []))


def draw_card(script: str, take: Take, work: Path, out: Path, name: str) -> None:
    """Draw one card's text onto the slate background as a single still."""
    ass = work / f"{name}.ass"
    ass.write_text(script, encoding="utf-8")

    run_ffmpeg(
        ["-f", "lavfi", "-i", f"color=c={SLATE_BACKGROUND}:s={take.width}x{take.height}",
         "-vf", f"ass={ass.name}", "-frames:v", "1", "-y", str(out.resolve())],
        cwd=work,
    )


def preview_script(take: Take, config: dict, line: dict | None) -> str:
    """
    The badge and at most one subtitle, both held for the whole still.

    Frozen rather than timed: a single image has no timeline for libass to place
    events on, so the moment is chosen when the line is picked and the event is
    written to cover it.
    """
    events = []

    if config.get("showBadge", True):
        events.append(event("Badge", 0.0, 1.0, config["methodNames"].get(take.method, take.method)))

    if line is not None and config.get("showSubtitles", True):
        speaker = config["speakerNames"].get(str(line.get("speaker", "")), "")
        label = f"{speaker}: " if speaker else ""
        events.append(event("Sub", 0.0, 1.0, f"{label}{str(line.get('text', '')).strip()}"))

    return ass_header(config, take) + "\n".join(events) + "\n"


def utterance_at(utterances: list[dict], seconds: float) -> dict | None:
    """Whichever line is being spoken then, or None in a gap."""
    for utterance in utterances:
        if float(utterance["startTime"]) <= seconds <= float(utterance["endTime"]):
            return utterance

    return None


def default_preview_time(utterances: list[dict]) -> float:
    """
    The middle of the first line, so the preview lands on a caption rather than
    in a silence and says nothing about the very thing it exists to show.
    """
    if not utterances:
        return 0.0

    first = utterances[0]
    return (max(0.0, float(first["startTime"])) + float(first["endTime"])) / 2.0


def exported_clips() -> list[str]:
    """
    The clips a demo could be made of, whether or not they have been rendered.

    Narrowed to the segments that carry a ``seat`` — study 2's room-placed
    clips — because those are the ones the demo scene can play at all, and the
    folder also holds study 1's five and the two dozen candidates it was chosen
    from. Asked of the segment rather than of the folder's name, which is a
    convention and not a fact.
    """
    clips = []

    for path in sorted(SEGMENT_ROOT.glob("*/segment.json")):
        try:
            segment = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue

        if (segment.get("seat") or {}).get("valid"):
            clips.append(path.parent.name)

    return clips


def first_clip_with_a_segment() -> str | None:
    clips = exported_clips()
    return clips[0] if clips else None


def png_size(path: Path) -> tuple[int, int]:
    """
    Width and height straight out of the PNG's IHDR chunk.

    Read here rather than asked of ffprobe: it is sixteen bytes, and the frame
    has to exist and be a PNG either way.
    """
    header = path.read_bytes()[:24]
    if header[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"{path} is not a PNG.")

    return int.from_bytes(header[16:20], "big"), int.from_bytes(header[20:24], "big")


# --------------------------------------------------------------------------
# Pieces
# --------------------------------------------------------------------------

def opening_seconds(config: dict) -> float:
    return float({**DEFAULT_OPENING, **(config.get("opening") or {})}.get("seconds", 0))


def opening_script(take: Take, config: dict) -> str | None:
    """
    The opening card's text, or None when there is no card to draw.

    Its own function so the still preview and the video draw the same card;
    everything else about the wording is shared for the same reason.
    """
    opening = {**DEFAULT_OPENING, **(config.get("opening") or {})}
    seconds = float(opening.get("seconds", 0))
    title = str(opening.get("title", "")).strip()

    if seconds <= 0 or not title:
        return None

    byline = str(opening.get("byline", "")).strip()

    # The title sits on the middle of the card when it is the only thing on it,
    # and rides up to make room when there is a byline under it — otherwise a
    # card with no byline reads as one with something missing below.
    events = [event("Paper", 0.0, seconds, title,
                    pos=(take.width / 2, take.height * (0.42 if byline else 0.5)))]

    if byline:
        events.append(event("Byline", 0.0, seconds, byline,
                            pos=(take.width / 2, take.height * 0.62)))

    return ass_header(config, take) + "\n".join(events) + "\n"


def build_opening(take: Take, config: dict, work: Path) -> Path | None:
    """
    The card the video opens on, as a held piece of video.

    Sized off the first take, which is also what everything downstream is cut
    at, so the card cannot come out a different shape from the video it opens.
    """
    script = opening_script(take, config)
    if script is None:
        return None

    seconds = opening_seconds(config)
    ass = work / "opening.ass"
    ass.write_text(script, encoding="utf-8")

    out = work / "opening.mp4"
    run_ffmpeg(
        [
            "-f", "lavfi",
            "-i", f"color=c={SLATE_BACKGROUND}:s={take.width}x{take.height}:r={take.frame_rate}:d={seconds}",
            "-f", "lavfi",
            "-i", f"anullsrc=channel_layout=stereo:sample_rate=48000:d={seconds}",
            "-vf", f"ass={ass.name}",
            *ENCODE_ARGS,
            "-t", str(seconds),
            out.name,
        ],
        cwd=work,
    )
    return out


def slate_script_for(take: Take, entry: dict, config: dict) -> str:
    """One clip slate's text, looked up from the wording file."""
    return slate_script(
        entry.get("title", clip_title(config, take.clip, 1)),
        config["methodNames"].get(take.method, take.method),
        entry.get("blurb", config["methodBlurbs"].get(take.method, "")),
        config,
        float(config["slateSeconds"]),
        take,
    )


def build_slate(take: Take, entry: dict, config: dict, work: Path, index: int) -> Path:
    """A held title card: which conversation, which method, and one line of why."""
    seconds = float(config["slateSeconds"])

    ass = work / f"slate_{index:03d}.ass"
    ass.write_text(slate_script_for(take, entry, config), encoding="utf-8")

    out = work / f"slate_{index:03d}.mp4"
    run_ffmpeg(
        [
            "-f", "lavfi",
            "-i", f"color=c={SLATE_BACKGROUND}:s={take.width}x{take.height}:r={take.frame_rate}:d={seconds}",
            "-f", "lavfi",
            "-i", f"anullsrc=channel_layout=stereo:sample_rate=48000:d={seconds}",
            "-vf", f"ass={ass.name}",
            *ENCODE_ARGS,
            "-t", str(seconds),
            out.name,
        ],
        cwd=work,
    )
    return out


def build_body(take: Take, entry: dict, config: dict, work: Path, index: int) -> Path:
    """The take itself, with its method badge and the transcript burnt in."""
    pattern, start_number = frame_pattern(take)

    ass = work / f"body_{index:03d}.ass"
    ass.write_text(body_script(take, entry, config), encoding="utf-8")

    out = work / f"body_{index:03d}.mp4"
    inputs = [
        "-framerate", str(take.frame_rate),
        "-start_number", str(start_number),
        "-i", str(take.directory.resolve() / pattern),
    ]
    if take.audio.exists():
        inputs += ["-i", str(take.audio.resolve())]
    else:
        print(f"  {take.key}: no audio.wav; the take will be silent.", file=sys.stderr)
        inputs += ["-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000"]

    duration = take.frame_count / take.frame_rate
    run_ffmpeg(
        [*inputs, "-vf", f"ass={ass.name}", "-af", "apad", *ENCODE_ARGS,
         "-t", f"{duration:.3f}", out.name],
        cwd=work,
    )
    return out


def frame_pattern(take: Take) -> tuple[str, int]:
    """
    ffmpeg's printf pattern for a take's frames, and the number to start at.

    Read off the files rather than assumed: Unity Recorder pads the frame
    number to four digits today, and a take that was rendered before the first
    frame the encoder accepted does not start at zero.
    """
    frames = sorted(take.directory.glob("frame_*.png"))
    if not frames:
        raise SystemExit(f"{take.directory} has no frames.")

    match = re.fullmatch(r"frame_(\d+)", frames[0].stem)
    if match is None:
        raise SystemExit(f"{frames[0].name} is not a numbered frame.")

    digits = len(match.group(1))
    return f"frame_%0{digits}d.png", int(match.group(1))


# --------------------------------------------------------------------------
# Text
# --------------------------------------------------------------------------

def merged_layout(config: dict) -> dict:
    """
    Where each piece of text goes, the config's answer over the default's.

    Merged per entry rather than per style, so moving the badge does not mean
    restating its margins — and an unknown style name is reported rather than
    ignored, since a typo in a config that is edited by hand otherwise does
    nothing and says nothing.
    """
    layout = {name: dict(place) for name, place in DEFAULT_LAYOUT.items()}

    for name, place in (config.get("layout") or {}).items():
        if name in layout:
            layout[name].update(place)

    return layout


def report_layout_problems(config: dict) -> None:
    """
    Say once what the layout asks for and cannot have.

    Once, and so not from ``merged_layout``, which is called for every
    card drawn — the same complaint eighteen times reads as eighteen faults.
    """
    layout = {name: dict(place) for name, place in DEFAULT_LAYOUT.items()}

    for name, place in (config.get("layout") or {}).items():
        if name not in layout:
            print(f"  layout: no text called {name!r}; known: {', '.join(sorted(layout))}",
                  file=sys.stderr)
            continue

        if name in CARD_STYLES:
            for key in sorted(set(place) - {"marginX"}):
                print(f"  layout.{name}.{key} does nothing: {name} is on a card, which places its "
                      "own text. Only marginX applies, and it sets the width a line may run to.",
                      file=sys.stderr)


def alignment_of(corner: str, name: str) -> int:
    """The ASS keypad number for a spelled-out corner."""
    key = str(corner).strip().lower()
    if key not in CORNERS:
        raise SystemExit(
            f"layout.{name}.corner is {corner!r}; use one of: {', '.join(sorted(set(CORNERS)))}")

    return CORNERS[key]


def ass_header(config: dict, take: Take) -> str:
    """
    Styles for every piece of text the video carries.

    One subtitle file per piece rather than a filter per caption: libass wraps
    and outlines text properly, and a drawtext filter per utterance would mean
    escaping the corpus's own punctuation into a filter graph.
    """
    font = config.get("fontName", "Segoe UI")

    # Everything in the config is quoted against a 1080-high frame, margins as
    # well as sizes, so a layout tuned once keeps its proportions if a take is
    # ever rendered larger.
    sizes = {**DEFAULT_FONT_SIZES, **config.get("fontSizes", {})}
    layout = merged_layout(config)
    scale = take.height / 1080.0

    def scaled(value: float) -> int:
        return max(0, round(float(value) * scale))

    def style(name: str, colour: str, bold: int, outline: int, shadow: int,
              align: int, margin_x: float, margin_y: float) -> str:
        return (f"Style: {name},{font},{max(1, scaled(sizes[name]))},{colour},"
                f"&H000000FF,&H00000000,&H64000000,{bold},0,0,0,100,100,0,0,1,"
                f"{outline},{shadow},{align},"
                f"{scaled(margin_x)},{scaled(margin_x)},{scaled(margin_y)},1")

    def placed(name: str, colour: str, bold: int, outline: int, shadow: int) -> str:
        place = layout[name]
        return style(name, colour, bold, outline, shadow,
                     alignment_of(place["corner"], name), place["marginX"], place["marginY"])

    def on_slate(name: str, colour: str, bold: int) -> str:
        return style(name, colour, bold, 0, 0,
                     CARD_ALIGNMENT, layout[name]["marginX"], 80)

    styles = "\n".join([
        on_slate("Paper", "&H00FFFFFF", -1),
        on_slate("Byline", "&H00B0B0B0", 0),
        on_slate("Title", "&H00FFFFFF", -1),
        on_slate("Method", "&H00FFFFFF", 0),
        on_slate("Blurb", "&H00B0B0B0", 0),
        placed("Badge", BADGE_COLOUR, -1, 3, 0),
        placed("Sub", SUBTITLE_COLOUR, 0, 3, 1),
    ])

    return f"""[Script Info]
ScriptType: v4.00+
PlayResX: {take.width}
PlayResY: {take.height}
WrapStyle: 0
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
{styles}

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""


def slate_script(title: str, method: str, blurb: str, config: dict, seconds: float, take: Take) -> str:
    lines = [
        event("Title", 0.0, seconds, title, pos=(take.width / 2, take.height * 0.38)),
        event("Method", 0.0, seconds, method, pos=(take.width / 2, take.height * 0.52)),
    ]
    if blurb:
        lines.append(event("Blurb", 0.0, seconds, blurb, pos=(take.width / 2, take.height * 0.63)))

    return ass_header(config, take) + "\n".join(lines) + "\n"


def body_script(take: Take, entry: dict, config: dict) -> str:
    duration = take.frame_count / take.frame_rate
    lines: list[str] = []

    if config.get("showBadge", True):
        badge = entry.get("badge") or config["methodNames"].get(take.method, take.method)
        lines.append(event("Badge", 0.0, duration, badge))

    if config.get("showSubtitles", True):
        for utterance in read_utterances(take):
            start = max(0.0, float(utterance["startTime"]))
            stop = min(duration, float(utterance["endTime"]))
            if stop <= start:
                continue

            speaker = config["speakerNames"].get(str(utterance.get("speaker", "")), "")
            text = str(utterance.get("text", "")).strip()
            if not text:
                continue

            label = f"{speaker}: " if speaker else ""
            lines.append(event("Sub", start, stop, f"{label}{text}"))

    return ass_header(config, take) + "\n".join(lines) + "\n"


def read_utterances(take: Take) -> list[dict]:
    """
    The clip's own transcript, or nothing when the segment cannot be found.

    Missing is a warning rather than an error: a segment folder is git-ignored
    and lives on whichever machine exported it, and a video with no subtitles
    is still a video.
    """
    if not take.segment_path.exists():
        print(f"  {take.key}: {take.segment_path} not found; no subtitles for it.", file=sys.stderr)
        return []

    segment = json.loads(take.segment_path.read_text(encoding="utf-8"))
    return list(segment.get("utterances", []))


def event(style: str, start: float, end: float, text: str,
          pos: tuple[float, float] | None = None) -> str:
    """One ASS dialogue line, with the text made safe for the format."""
    placement = f"{{\\pos({pos[0]:.0f},{pos[1]:.0f})}}" if pos else ""
    return (f"Dialogue: 0,{ass_time(start)},{ass_time(end)},{style},,0,0,0,,"
            f"{placement}{escape_ass(text)}")


def escape_ass(text: str) -> str:
    """Braces open an override block in ASS and newlines end a line."""
    return (text.replace("\\", "\\\\")
                .replace("{", "\\{")
                .replace("}", "\\}")
                .replace("\r", " ")
                .replace("\n", "\\N"))


def ass_time(seconds: float) -> str:
    seconds = max(0.0, seconds)
    hours, rest = divmod(seconds, 3600)
    minutes, rest = divmod(rest, 60)
    return f"{int(hours)}:{int(minutes):02d}:{rest:05.2f}"


# --------------------------------------------------------------------------
# ffmpeg
# --------------------------------------------------------------------------

# One encode for every piece, so the concat below can copy streams rather than
# re-encode: a mismatch in any of these makes the join silently drop frames.
ENCODE_ARGS = [
    "-c:v", "libx264",
    "-preset", "slow",
    "-crf", "16",
    "-pix_fmt", "yuv420p",
    "-c:a", "aac",
    "-b:a", "192k",
    "-ar", "48000",
    "-ac", "2",
    "-y",
]


def run_ffmpeg(args: list[str], cwd: Path) -> None:
    """
    Run ffmpeg with the working directory at the scratch folder.

    The ``ass`` filter takes its file as part of a filter graph, where a Windows
    path's drive colon has to be escaped and a backslash is an escape character
    already. Running from the folder and passing a bare filename sidesteps both.
    """
    command = ["ffmpeg", "-hide_banner", "-loglevel", "error", *args]
    result = subprocess.run(command, cwd=cwd, check=False)
    if result.returncode != 0:
        raise SystemExit(f"ffmpeg failed ({result.returncode}): {' '.join(command)}")


def concatenate(pieces: list[Path], out: Path, work: Path) -> None:
    """Join the pieces without re-encoding; they were all made to match."""
    listing = work / "pieces.txt"
    listing.write_text(
        "".join(f"file '{piece.name}'\n" for piece in pieces), encoding="utf-8")

    run_ffmpeg(
        ["-f", "concat", "-safe", "0", "-i", listing.name,
         "-c", "copy", "-movflags", "+faststart", "-y", str(out.resolve())],
        cwd=work,
    )


if __name__ == "__main__":
    raise SystemExit(main())
