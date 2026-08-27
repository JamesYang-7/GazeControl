# In-VR Questionnaire — UI Design

Builds requirement 3 of `user-study-design.md` §9 ("15 rating screens and 5 ranking screens").
The **instrument** is not designed here — §6 of that document fixes the items, the scale, the
ranking and when each is asked, settled 2026-08-21. This is only how a participant is shown them
and how the answers are captured.

Status: design, 2026-08-27. Nothing below is built.

---

## 0. The decision this rests on

**The participant reads the item in VR and answers aloud; the experimenter enters the response on
the desktop** (user's call 2026-08-27). §9.3 estimated this screen set as "likely more work than
the XR port itself", and that estimate was for a controller pointer. This decision deletes most of
it:

- No XR Interaction Toolkit, no ray interactor, no pointer/hover/press states, no SteamVR
  dependency for controllers under the Varjo loader — an untested path on the one piece of
  hardware that has already produced one surprise (OpenXR's eye-gaze extension enumerating no
  device, CLAUDE.md "XR — the study rig").
- The VR surface becomes **display-only**: a world-space canvas with no input at all.
- The entry surface is a desktop panel: keyboard, no latency, and it cannot be seen from inside
  the headset.

**The cost, accepted with eyes open:** twenty spoken answers per participant with the experimenter
in the room is this design's one demand-effect exposure. Two mitigations, both cheap, both belong
in the protocol rather than in code — the experimenter reads the item verbatim and acknowledges
answers with nothing more than "thank you", and the operator panel is blinded (§2).

---

## 1. Two surfaces, and why the split costs nothing

Unity renders **World Space** canvases into the eye textures, and renders **Screen Space – Overlay**
canvases and IMGUI `OnGUI` into the desktop window only, which is what the mirror shows. So one
scene already gives two independent surfaces:

| Surface | Rendered by | Seen by | Carries |
|---|---|---|---|
| Item panel | World-space canvas at the triad vertex | Participant | Item text, the 1–7 anchors, "answer aloud" |
| Operator panel | IMGUI, as `DeveloperOverlay` already does | Experimenter, on the mirror | Progress, the item, the number keys, entry state |

`DeveloperOverlay` is the working precedent for the desktop half, including the "must never reach a
participant" reasoning this inherits.

**Confirm on hardware before building on it** (ten minutes of a headset session): that the IMGUI
panel does *not* appear in the Varjo, and that the mirror does show it. If it leaks, the fallback is
a second camera targeting the desktop display with the item canvas on a layer it culls — more
wiring, same design.

## 2. The operator panel is blinded

The experimenter reads items aloud and knows the hypothesis. The panel therefore **never names the
condition**: it shows `Conversation 3 · Version 2 of 3`. The version→condition mapping lives in the
harness and reaches only the response file.

This is `DeveloperOverlay`'s argument one step along. The overlay is barred because it would tell
the *participant* where the boundaries are; the panel is blinded because naming "Proposed" would
tell the *experimenter* which answer is the interesting one, while they are the person speaking the
item aloud.

## 3. Screen inventory

Per participant, 22 in-VR screens over 3 templates:

| Count | Template | When |
|---|---|---|
| 1 | Framing (§6.1) | Before block 1 |
| 15 | Rating — N1, T2, A1, I1 together | After each clip |
| 5 | Ranking — R1, then D1 free text | After each block's third clip |
| 1 | End | After block 5 |

**All four Likert items on one screen, not one item per screen.** Sixty screens against fifteen, for
a set the participant is answering about a 25 s clip they have just watched; seeing the four items
together also makes it plain that they ask about different things.

§6.4's post-study block is **out of scope** — the headset comes off first (§5.6).

## 4. What is on a screen, and what the scene does behind it

**Hide the agents while an item is up.** The runner keeps ticking after
`RecordedConversation.Finish()` — `DecisionTime()` follows `Elapsed`, which keeps growing — so
without this the participant answers "The characters seemed aware that I was there" while two
agents are still gazing at them past the end of the clip. That contaminates I1 with a state no
condition was baked to produce. A neutral field behind the panel also makes "the clip is over,
answer now" unambiguous.

Panel geometry: centred at the participant's calibrated eye height (1.60 m by construction,
`EyeHeightCalibration`), around 1.5 m out, so it sits at a comfortable convergence distance and
inside the Varjo's focus area. Exact size and type scale need a headset — §10.

The ranking screen names the three versions **by the order they were shown** (Version 1 / 2 / 3),
which is the only handle the participant has on them, and states that ties are not allowed (§6.3).

## 5. Flow, and the seam to the harness

One component, `QuestionnaireSession`, with an explicit state machine:

```
Idle → Framing → [ ClipPlaying → Rating ] ×3 → Ranking → Comment → (next block) → Done
```

It does **not** sequence clips, apply counterbalancing or derive seeds — that is the study harness
(§9.5), which does not exist yet. The seam: the harness tells the questionnaire which block and
which version position just played, the questionnaire raises an event when its screen is answered,
and the harness starts the next clip. Designing the seam now is the point of doing this first —
building the harness against a questionnaire that already exists is easier than the reverse.

**Only the experimenter can advance.** A participant who did not understand an item cannot get
stuck, and cannot skip past one.

## 6. The response record

One folder per participant, matching §9.5's "one folder per participant".

- `responses.csv` — `participant, block, conversation, version_position, condition, item, response, utc`
- `comments.jsonl` — D1 free text, one object per block; separate because it contains commas and
  newlines and must not be able to corrupt the numeric file.

**Appended and flushed as each answer is entered**, not written at session end. A session that dies
in block 4 must keep blocks 1–3; this project has already lost a file to end-of-run writing (a bake
threw away its own track in `OnDestroy`, PROGRESS 2026-08-25).

The condition is written to the file and shown nowhere.

## 7. Integrity guards

`GazeConditionRunner.StudySessionProblem()` is the model: one switch, checked before anything runs,
because each of these produces a normal-looking session that is quietly worthless.

A questionnaire session refuses to start when the response files cannot be opened, when the block's
version→condition mapping is missing or incomplete, or when the operator panel is configured to
show condition names. It refuses to *show a rating screen* for a clip that did not play to
`HasFinished` — a rating of a clip that was cut short is worse than a missing one, because nothing
downstream can tell the difference.

## 8. Out of scope

Study harness and counterbalancing (§9.5); post-study questionnaire, consent and information sheet
(§9.6); any ability to re-watch a clip (§1 fixes the block structure at three plays).

## 9. Build order

Ordered so that headset time — the scarce resource — is needed only at step 3.

1. **Data and the writer.** Item/screen definitions and the response writer, pure and unit-tested,
   in the style of `GazeControl.Gaze.Policy`. No Unity dependency beyond `UnityEngine`.
2. **Operator panel** driving the state machine over a stub screen list. Fully exercisable flat, on
   a machine with no headset — which is also how it gets tested.
3. **World-space item canvas** and a legibility pass in the Varjo.
4. **Harness seam** — the event and the block hand-off of §5.

## 10. Open items

- Confirm the overlay/mirror split in a real Varjo session (§1). Everything else assumes it.
- Legibility, panel distance and type size in the headset (§4) — cannot be settled at a desk.
- Whether the framing screen (§6.1) reads naturally when displayed rather than spoken; the pilot of
  3–5 people (`user-study-design.md` §10) is where that gets answered.
- The demand-effect mitigation (§0) is a protocol line and belongs in `user-study-design.md` §6.1,
  not here.
