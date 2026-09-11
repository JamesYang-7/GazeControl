# The demo video

A flat 16:9 video of the study's clips played under each of the three methods,
for talks and for the paper's supplementary material. Two stages, deliberately
separated: **render the frames once, cut the text as often as you like.**

    Unity: Demo Video → Render Takes          ffmpeg: Tools/compose_demo_video.py
    ─────────────────────────────────         ────────────────────────────────────
    frames + sound, per clip per method   →   slates, method badge, subtitles,
    Recordings/Demos/<clip>/<method>/          concatenated into one mp4

Everything the second stage adds is text, and text is what gets rewritten. The
first stage costs a play session per take; the second costs a minute. Nothing
about a rewording sends you back through Unity.

## 0. The camera

**The study's own camera cannot shoot this.** It is the participant's eye, the
agents aim at it — it is the human's `LookAtAnchor` — and it stands on the
triad's eye line looking straight ahead. In a headset that is exactly right. In
a 16:9 frame it puts two heads across the middle, spends the top third of the
picture on the ceiling, and cuts both bodies off at the hip.

So there is a second camera. `DemoCamera` (`Assets/GazeControl/Scripts/Runtime/Demo/`)
stands on the participant's sight line, pulled back and tilted down; the
participant's camera does not move, so no rendered gaze changes. Build it with
**`GazeControl → Demo Video → Set Up Demo Camera`**, which also adds the
`DemoCamera` tag the Recorder finds it by.

**The shot is five numbers, tuned by hand** (`DemoCameraFraming`, an asset at
`Assets/GazeControl/Settings/`): how far back, how high, how far down, how wide,
and how far to the side. An asset rather than fields on the component, because
the tuning happens in Play Mode — the agents are in the bind pose until a clip
runs — and a scene field dragged in Play Mode is thrown away when Play Mode
ends.

**`GazeControl → Demo Video → Camera Framing`** is where they are dragged. It
reports, in metres, what the frame actually covers at each agent's distance:
their distance, the heights the top and bottom edges cross at it, the headroom
left, and how much room is left to the side edge. Set the Game view to **16:9**
while tuning or the picture is not the one that will be recorded.

To tune against a real clip, pick one from the session's clip list at the foot of
the framing window and press *Play it through the demo camera*. The window loads
it itself: study 1's Clip Browser came off the menu with the rest of study 1, and
study 2's clips are read out of the 3People corpus by `GazeControl → 3People →
Session Browser`, which is a different job.

**Play it from that button, not with the Play button.** Two things in the scene
are set for participants and wrong for everything else, and both bite a plain
Play: `StudySessionRunner` takes autoplay away from the conversation in its
`Awake` — so Play gives a P00 session waiting on a framing screen rather than a
clip — and `StartXrOnPlay` brings the Varjo runtime up, which takes Unity's audio
output with it and stalls the frame clock on a compositor nobody is wearing.
`DemoPlaySession` suspends both for that one play session and puts them back when
it ends. A render does the same for the length of its queue.

The defaults — 1 m back, 1.6846 m up, 10° down, 42° vertical — hold an agent
from about the knee to a little over the head in the narrowest of the five
session rooms, and both agents' shoulders fit across the frame in the widest.

**One framing for every clip.** The five clips are recordings of real rooms, so
the agents stand 1.14–1.62 m from the seat depending on the clip and a fixed
camera renders them about a fifth larger in the widest room than in the
narrowest. That variation is the recording's, and it is left visible: framing
each clip separately would make the shot a variable between the conditions the
video exists to compare.

**The demo camera never renders a play session it was not asked to.** It has the
higher depth of the two, so a scene saved with it enabled would quietly shoot a
participant session — in a headset — through a camera pulled a metre out of
their head. The editor's demo commands ask for it through `DemoViewRequest`,
which `DemoCamera.Awake` consumes once; pressing Play by hand afterwards is an
ordinary run.

## 0.5. Previewing the text

One command draws one of each kind of picture the video is made of, so a wording
change can be checked everywhere it lands in a single look:

    uv run python Tools/compose_demo_video.py --preview --clip study2_c1 --at 8.6

| File | What it shows |
|---|---|
| `output/demo_preview_title.png` | the opening card |
| `output/demo_preview_slate.png` | a clip slate — conversation, method, blurb |
| `output/demo_preview_clip.png` | a frame of the clip, with the badge and the subtitle of that moment |

They go through the same styles, the same escaping and the same libass call the
finished video does, so what they show is what a take will carry — a preview
drawn any other way would only be a picture of a different program's idea of the
text.

`--method` picks which method the slate and badge name — **baseline A by
default**, because the slate is one of the three pictures being checked and a
method with no blurb set previews a card with a gap on it. `--at` is the moment
(default: the middle of the clip's first line), `--config` a wording file to try,
and `--out` the stem the three names are built from. A slate with no blurb, and
an opening card switched off with `opening.seconds: 0`, are both said rather than
left to be noticed.

**The clip frame comes from a rendered take** when there is one — the very
picture the video will carry, at the frame for that second. The take's own clock
offset is read back off its manifest rather than assumed, because the recorder
starts a fraction before the conversation does.

Before anything is rendered there are no frames to take, so the framing window
fills in: play a clip through the demo camera and press **Save a preview frame**,
which writes `output/demo_preview_frame.png` from the demo camera at 1920×1080 —
rendered, not grabbed from the Game view, so it is the frame the recorder would
write whatever shape the Game view is. `--frame` points at another one.

**A preview never writes the config** — it knows about one take, and a config
that exists is never overwritten, so it would otherwise leave a running order of
one behind for the real compose to obey.

### Size and placement

    uv run python Tools/compose_demo_video.py --write-config

writes `Config/demo_video.json` — before anything is rendered, which is when
there is most reason to edit it — and stops. Edit, re-run the preview, look.

`fontSizes` is in points against a 1080-high frame:

| Key | What it sizes |
|---|---|
| `Paper` | the paper's title on the opening card (bold) |
| `Byline` | the line under it, when there is one |
| `Title` | a clip slate's heading — "Conversation 1" (bold) |
| `Method` | the method's name under it |
| `Blurb` | the sentence under that |
| `Badge` | the method's name in the corner, over the video |
| `Sub` | the subtitles |

`Paper` and `Title` are both titles and are easy to mix up: `Paper` is the one
card at the very start, `Title` is the heading on each of the fifteen slates.

`layout` places the text. It has two kinds of entry:

```json
"layout": {
  "Badge":  { "corner": "top-left", "marginX": 64,  "marginY": 48 },
  "Sub":    { "corner": "bottom",   "marginX": 240, "marginY": 72 },

  "Paper":  { "marginX": 160 },
  "Byline": { "marginX": 240 },
  "Title":  { "marginX": 240 },
  "Method": { "marginX": 240 },
  "Blurb":  { "marginX": 240 }
}
```

**`Badge` and `Sub` are drawn over the video**, so they take a `corner` —
`top-left`, `top`, `top-right`, `left`, `centre`, `right`, `bottom-left`,
`bottom`, `bottom-right` — and margins holding them off it.

**The rest are on cards of their own**, which stack their text vertically, so
only `marginX` means anything for them. What it means is **how wide a line may
run before it wraps**, and that is what decides how much air ends up either side
of the paper's title. It is not the visible gap: the wrap is balanced across the
lines, so a 1920-wide card with `marginX: 240` breaks the title at about 1083 px
and leaves 420 px each side, while `marginX: 160` breaks it at 1523 px and leaves
about 200. Measured, not guessed — and a `corner` or `marginY` set on one of them
is reported as doing nothing rather than quietly ignored.

An unknown name in `layout` is reported and skipped; an unknown corner stops the
run and lists the ones that exist.

Everything in the file is quoted against 1080 and scales with the frame, so a
layout tuned once keeps its proportions if a take is ever rendered larger.
`fontName` is any font installed on the machine.

## 1. Rendering the takes

**`GazeControl → Demo Video → Render Takes`** lists the session's clips and the
three methods, and queues one play session per ticked pair.

The clips and their seeds come from `StudySessionRunner`, not from anything
typed in the window: a seed chosen here by hand would be a different draw of the
prototypes from the one the study ran on, and nothing in the finished video
would say so. The baked track is replayed, so the gaze in the video is the gaze
in the study. A pair whose track has not been baked is refused before the first
play session starts, along with a missing segment, a missing demo camera, and a
scene left in `StudySession`.

Per take, into `Recordings/Demos/<clip>/<method>/`:

| File | What it is |
|---|---|
| `frame_0000.png` … | 1920×1080, one per frame at 30 fps |
| `audio.wav` | both agents, as the participant heard them |
| `take.json` | clip, method, seed, frame rate and count, the segment's path, and the framing it was shot on |

The game clock is locked to the frame rate, so an 18.55 s segment is 556 frames
however fast the machine renders, and the sound stays on the picture. Logging is
off (the baked track is already the record of the gaze), the headset is kept off
(the Varjo runtime would take Unity's audio output, which is the sound being
recorded), the session runner is disabled for the duration, and the developer
overlay is switched off — it names the turn boundaries, and a video is the one
place that mistake is permanent. All of it is put back afterwards.

A PNG per frame is about 1.5 GB per take, so five clips under three methods is
around 25 GB. `Recordings/` is git-ignored.

Left running unattended; one line per take lands in `output/demo_render_*.txt`.

## 2. Cutting the video

    uv run python Tools/compose_demo_video.py

Needs only ffmpeg on PATH (libx264 and libass). It builds, per take, a title
card and then the take itself with the method's name in the corner and the
conversation's own transcript as subtitles, and concatenates the lot into
`output/demo_video.mp4`.

**Everything the video says lives in `Config/demo_video.json`** — edit it and run
the script again. `speakerNames` maps the segment's speaker codes to whatever the
subtitles should call them; the agents' real corpus names are in each
`segment.json`.

**A slate numbers its clip by where it comes in this video**, not by which clip
it is: `clipTitleFormat` (default `"Conversation {n}"`) is filled with the clip's
place among the ones actually composed, so a video of c1, c3 and c7 counts 1, 2,
3. The viewer is counting what they are shown and has no idea the study had
seven, so a gap in the numbering would only invite them to wonder what they
missed. `{clip}` is the folder name if a slate should carry that instead.

### Recomposing

Any change to the config is one command away from a new video — nothing is
re-rendered:

    uv run python Tools/compose_demo_video.py

About a minute for all fifteen takes, a quarter of that for one clip. Add
`--only study2_c1` for a one-off cut that leaves the config alone, or `--out`
to write somewhere other than `output/demo_video.mp4`.

**For the whole thing again**, empty the `clips` array — `"clips": []` means
everything that was rendered — or list all five back. `--only` can only narrow
what `clips` already names, so it cannot put a clip back.

### The opening card

The video opens on the paper's title, held for `opening.seconds` (5 by default;
0 leaves the card out):

```json
"opening": {
  "seconds": 5.0,
  "title": "Interpretable Subsequence Gaze Modeling of Turn Boundaries in Multi-Party Interaction",
  "byline": ""
}
```

The title is the paper's own, read out of its `main.tex`, so the video cannot
quietly carry a different one from the submission — **nothing makes this follow
the paper**, so edit it here if the paper's title changes.

**The byline is empty, and that is deliberate.** CHI reviews double-blind and the
supplementary material is reviewed with the paper, so a video that names its
authors withdraws the submission's anonymity. Empty is the safe default rather
than an oversight: forgetting to anonymise costs far more than forgetting to
de-anonymise. The names are in the paper's `main.tex` and go in here once it is
accepted; a real newline in the byline breaks the line, so the config never
carries the subtitle format's own escape codes.

With no byline the title sits on the middle of the card; with one it rides up to
make room. `fontSizes` sizes them under `Paper` and `Byline`.

Nothing else in the video identifies anyone: the container carries only ffmpeg's
encoder tags, and `speakerNames` is what the subtitles call the two agents.

### What goes in, and in what order

```json
"clips":   ["study2_c1", "study2_c2", "study2_c3", "study2_c4", "study2_c7"],
"methods": ["SpeakerFollowing", "RoleConditioned", "Proposed"]
```

Every clip in `clips`, under every method in `methods`, in that order — so **one
name in `clips` cuts a video of that one conversation**, which is how a clip gets
looked at without waiting for the other four. Two lists rather than a list of
pairs, because what gets chosen is nearly always a clip, and spelling out three
pairs to drop one of them invites a typo. Either list left empty means everything
that was rendered. A listed clip with no rendered take is named on the way past,
since a clip missing from the finished video looks the same whether it was never
rendered or merely misspelt.

`methods` also decides the order within a clip; the default puts the two
baselines before the proposed one, so a viewer has seen what is being improved on
before the improvement.

### Editing it is safe

`--write-config` **fills gaps and changes nothing that is already there**, so
gaining a setting never costs the edits already in the file: a value once set
keeps whatever it was set to, and only keys the script has newly learned about
are added. It also names any key it does not recognise rather than ignoring it.
`--rebuild-config` is the blunt way back and does lose hand edits.

```
--only study2_c1 study2_c3   just these clips
--dry-run                    list what would be cut
--rebuild-config             start the wording over, losing hand edits
--out path.mp4               somewhere else
```

The subtitles are the corpus's own captions, read out of each clip's
`segment.json`. They are automatic captions and carry the corpus's noise — read
a clip with the Session Browser before trusting a line. A segment folder that is
not on this machine costs the subtitles and nothing else.

## Where it stands

All fifteen takes are rendered (2026-09-10): the five session clips at the
study's own chosen seeds, 7,641 frames and 10 GB under `Recordings/Demos/`. The
framing is tuned — 0.76 m back, 1.65 m up, 8° down, 39.6° vertical — and every
take's manifest records the shot it was taken on.

What is left is words:

- The slate blurbs. The line under the method's name is the generator's
  placeholder for two of the three and empty for the proposed one, and it is the
  only text in the video nobody has chosen.
- Whether the subtitles want raising. At the tuned framing, `layout.Sub.marginY`
  of 72 puts them over the agents' legs; legible, but a matter of taste.
