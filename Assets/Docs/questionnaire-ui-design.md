# In-VR Questionnaire — UI Design

Builds requirement 3 of `user-study-design.md` §9 ("15 rating screens and 5 ranking screens").
The **instrument** is not designed here — §6 of that document fixes the items, the scale, the
ranking and when each is asked, settled 2026-08-21. This is only how a participant is shown them
and how the answers are captured.

Status: **built 2026-08-27**, to this design, and given a second way in on **2026-09-09** (§0.1:
the participant may also answer by pointing a controller at a row of buttons — **working on a real
Vive wand pair the same day**, both hands, aim and trigger).
`QuestionnaireSession` + `QuestionnaireDisplay` + `QuestionnaireOperatorPanel` +
`QuestionnaireControllerPointer`, wired by `GazeControl → Set Up Questionnaire`; §9 steps 1-2 and 4
are done and step 3 is built but has not had its legibility pass in the headset. §1's assumption —
that the IMGUI panel does not reach the eye textures — is still unconfirmed on hardware, and so is
every part of §0.1 that needs a controller.

For a spell on the same day the items were dropped from the headset entirely (recorded only in
`ClipPauseController`'s doc, never in the decisions log); the user restored this design after a VR
verification found that nothing was displayed after a clip.

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

## 0.1 The participant can also press it (2026-09-09)

**A row of buttons under the question page, aimed at with an HTC Vive wand and pressed with the
trigger** (user's call). It is *added to* §0, not put in place of it: every press goes through the
same `Enter` / `Back` / `Commit` on `QuestionnaireSession` that the operator's keys reach, the
operator can take over mid-screen, and `responses.csv` cannot tell which surface an answer came
from. Answering aloud stays the fallback, and stays the only way to answer D1 — there is no
keyboard in the headset.

**What §0 bought is kept.** The row is flat rectangles on the same world-space canvas, aimed at by
intersecting a ray with the panel plane (`PanelRaycast` and `ButtonRowLayout`, both pure and
unit-tested): no XR Interaction Toolkit, no ray interactor, no event system, no collider, nothing
to enumerate. The controller is read straight off the Input System, because the Varjo plugin
publishes its own layouts — `VarjoViveWand`, `VarjoIndexController`, `VarjoController`, all
deriving from `XRController` — and `QuestionnaireControllerPointer` looks `devicePosition`,
`deviceRotation` and `triggerPressed`/`trigger` up by name rather than binding an action asset per
layout, which would silently bind none of them if the loader changed again.

The rows, and what may be pressed:

| Screen | Row |
|---|---|
| Rating | `Undo` `1`…`7` `Next` — Next lights only once all four items are answered, or the clip was cut short and nothing is being recorded |
| Ranking | `Undo` `V1` `V2` `V3` `Next` — a version already placed goes dim, because a tie is refused |
| Free text | the same row with the versions dim and `Next` lit — the probe is optional, so the participant's Next means "leave it as it stands and play the next clip" |
| Both previews | the row of the page being previewed, values and `Undo` dim, `Next` lit |
| Framing | one `Next`, at a short-answer button's width, centred |
| Closing | no row at all |

**Every screen up to the first clip is pressable, and the closing one is not** (user's call, the
same day, replacing an answer-screens-only row): *a participant who can start the session should be
able to finish it*, and a framing screen only the operator could pass is a session that stops on
its first page. The previews carry the row of the page they preview with the values dim, because
seeing the buttons is half of what a preview is for and pressing them there would mean nothing.

The cost, accepted: a participant may press on before the operator has finished reading a passage
aloud. The operator can ask them to wait, and their own key still works — the alternative was a
session that cannot be finished without them.

**The closing passage is the exception**: no row, and the operator's key alone. It is the one
screen with nothing after it — every answer is already written by the time it shows — and a
participant who pressed on would be left looking at an empty room with the headset still on.

**The ranking now shows the order given** — `Your order:  V2  >  V1  >  _` — which
reverses 2026-08-31's "the ranking marks nothing at all". That decision was right while the
participant only spoke: listing the versions invited them to do the ranks-to-versions conversion
themselves. Once they press the buttons, a press that shows nothing back is a press they cannot
check or correct.

**A rank-by-rank spelling was tried and put back** (`1st: V2,  2nd: V1,  3rd: _`, both calls the
user's, 2026-09-09). The chain is the shorter line and reads as one order rather than three
labelled slots. Which end is best is said in the prompt instead, where it applies to the question
rather than to the answer so far. `RankingOrder.Ordinal` stays where it was moved to, with the
operator's panel as its one caller.

**The short-answer page marks the question being asked**, with the same `>` in the same two columns
the rating page uses (user's call, 2026-09-09). Two questions sit on that page and the button row
changes under them — versions while the ranking is open, then only `Next` — so which one the row is
answering has to be visible. The preview marks neither, exactly as the rating preview shows no
cursor.

**And the ranking prompt says "from best to worst"** (user, same day), for the same reason: nothing
else on the page said which end came first.

**Enabling is asked of `QuestionnaireEntry.Accepts`** — the same predicate that refuses the
operator's keystroke — so what is dim and what would be refused cannot drift. A disabled button is
dimmed and never hidden: the row keeps its shape, so the same answer stays in the same place on the
panel from screen to screen. The participant's panel carries no notice for them to read, which is
why a refusal has to be visible before the press rather than after it.

**First tried on a real Vive wand pair, 2026-09-09**, which found two faults, both since fixed:

- **Only one of the two wands could highlight anything**, and the giveaway was the cursor sitting
  at the end of the *idle* controller's ray. `TryAim` reported a hit on the panel's **plane**, which
  is infinite — so a controller hanging at the participant's side and pointing vaguely forward
  "hit" it several metres off to one side, and since the first hit won the cursor, the controller
  actually aimed at a button got neither cursor nor highlight. It now takes a hit only inside the
  canvas rect. Two things behind it were wrong as well: the tie-break preferred whichever controller
  had last *completed a press*, which the trigger fault below made impossible, so the preference
  stayed at -1 and never moved; and the device filter took only `XRController`, while the Varjo
  plugin publishes handed SteamVR *tracker* layouts that do not derive from it, so a runtime that
  enumerates one wand that way loses that hand entirely. Any tracked device with a trigger now
  counts (the headset excepted, being tracked too), and any *pull* claims the cursor rather than
  only a completed press.
- **The trigger did nothing.** `triggerPressed` was preferred and the axis used only where that
  control was *absent*, which leaves no path at all for a layout that declares the button and never
  updates it. Both are now read into one down/up state, so either alone is enough, and it is one
  edge per pull rather than one per control - on a wand the axis crosses the threshold a frame or
  two before the click bottoms out, and two edges from one squeeze would enter two answers. It also
  no longer depends on `wasPressedThisFrame`, whose edge is defined against the input update rather
  than the frame the pose was read in.

**Diagnosis is on the panel, not in a guess.** The operator's header carries the controller count,
what the participant is aiming at and the live trigger reading, because a trigger reporting nothing
and a participant who is simply not pressing look identical from the desk.
`QuestionnaireControllerPointer.LogControlsAsPressed` names every control on a controller as it is
held, which is how an unfamiliar controller's trigger is found without spending another headset
session on the question.

**Working after those two fixes** (user, 2026-09-09): both wands aim, both press, and the trigger
enters an answer. The cursor shows for one controller at a time — whichever was last used — which
is the intended behaviour and reads correctly with both in hand.

**Still unverified**: whether an 11 cm button at 1.5 m is comfortable over a session of twenty
screens, and everything §1 still owes. `QuestionnaireControllerPointer.MouseFallback` walks the
whole row at a desk with the mouse; it is off by default, because in a session the operator's own
clicks land in the same game view and one of them on the panel would answer a question nobody
asked.

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
**The operator enters versions by rank, not ranks by version** (2026-09-09): the panel lists 1st /
2nd / 3rd and takes a version number in each, because a participant says "two, then one, then
three" and typing that verbatim is one conversion fewer for the operator to get wrong. The file is
unchanged — `responses.csv` still holds one `R1` row per version with its rank, inverted at commit by
`RankingOrder` (pure, tested) — so study 1's responses and study 2's read identically.

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

**The participant can advance every screen but the last** (2026-09-09, §0.1), so a session that has
started can be finished without the operator touching anything. They still cannot *skip* an item:
`Next` on an answer screen lights only once the screen is complete, and a participant who did not
understand one cannot get stuck, because the operator's keyboard stays live on every screen. The
closing passage is the operator's alone.

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

1. ~~**Data and the writer.**~~ Done. `QuestionnaireDefinition`, `QuestionnaireScript`,
   `QuestionnaireScreen`, `QuestionnaireResponseWriter` and `QuestionnaireEntry` — pure, unit-tested,
   no Unity dependency beyond `UnityEngine`.
2. ~~**Operator panel**~~ Done, driving the real state machine rather than a stub, and walked flat
   through a full block and through a one-conversation run to the closing screen.
3. **World-space item canvas** — built; the legibility pass in the Varjo is still owed.
4. ~~**Harness seam**~~ Done, as two hold flags: `ClipPauseController.AdvanceHeld` and
   `StudySessionRunner.StartHeld`. The questionnaire releases the next clip when its screen is
   answered, and the harness never learns what was asked.

## 10. Open items

- Confirm the overlay/mirror split in a real Varjo session (§1). Everything else assumes it.
- Confirm a Vive wand enumerates under the Varjo loader, and that a button at 1.5 m is comfortable
  to hit (§0.1). Both need the headset; the mouse fallback covers everything else.
- Legibility, panel distance and type size in the headset (§4) — cannot be settled at a desk.
- Whether the framing screen (§6.1) reads naturally when displayed rather than spoken; the pilot of
  3–5 people (`user-study-design.md` §10) is where that gets answered.
- The demand-effect mitigation (§0) is a protocol line and belongs in `user-study-design.md` §6.1,
  not here.
