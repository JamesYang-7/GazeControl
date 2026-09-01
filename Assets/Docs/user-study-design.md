# Scene-1 User Study Design

Method, instrument and analysis plan for the scene-1 (agent-to-agent turn-taking) evaluation.
Companion to `baseline-spec.md`, which defines the three conditions this study compares.

Status: design settled 2026-08-21; stimuli not yet built. Scene 2 (turn-yielding to the user) is
**out of scope** — discarded from the study for now (user's call 2026-08-21).

---

## 0. What this study claims, and what it does not

**Claim.** With conversation content, audio, body motion, agent appearance and the animation layer
all held constant, the *gaze policy* alone changes how natural and how socially aware two
conversing agents appear to a third person standing with them.

**Not claimed.** That gaze predicts turn transitions. The paper's own position is that gaze alone
is insufficient for turn prediction, and the participant never takes a turn, so no item asks the
participant to predict the next speaker.

The design's central asset is that the stimulus is **recorded playback**: the same corpus audio,
the same mocap, the same textures, the same frame count in every condition. Unlike a live
conversational agent study, comparability is a property of the material rather than an argument
about it. State this explicitly when reporting.

---

## 0.1 Pilot exclusion — declared 2026-08-31, before any further participant

**P01-P04 are pilots and are excluded from analysis. The analysed study begins at P05.**

**Ground.** Those four ran under the single fixed method order of 2026-08-27, which every one of
them received identically. That order was superseded on 2026-08-31 by the per-participant
counterbalanced schedule of §1, so the four sit outside the schedule the analysed sample is drawn
from. The exclusion is a property of *how their session was ordered*, decided before any of the
remaining participants is run, and is not conditioned on anything they answered.

**Disclosure, because it matters for how this reads.** The pilot responses had already been
inspected when this was declared — condition means, per-participant rankings and the serial-position
check are all in the 2026-08-31 discussion. The exclusion rule is nevertheless independent of them:
it would apply identically whichever way those four had ranked the conditions, and it is stated
here in full rather than left implicit. Their data stays in `Recordings/P01`-`P04` as a record; it
is not analysed and does not enter any reported figure.

**What is retained from them.** Nothing inferential. They confirmed the instrument and the session
mechanics run end to end, and they showed that the eye tracker never produced a valid sample —
which is why §7 is now scoped out (see that section).

**Consequence for N.** The registered sample is **18 analysed participants, P05-P22**
(`study-registration.md` §2). A multiple of six, so the counterbalancing marginals close exactly
(§1); the pilots do not count toward it.

---

## 1. Design

**Within-subjects, fully crossed: 5 conversations × 3 methods = 15 clips per participant.**

Presentation is **blocked by conversation**: the three methods of one conversation play
back-to-back, then the participant ranks them. Every participant sees every conversation three
times; repetition is the instrument, not a nuisance.

```
Block k (k = 1..5):
    clip k / method m1   -> 4 Likert items
    clip k / method m2   -> 4 Likert items
    clip k / method m3   -> 4 Likert items
    -> R1 ranking of the three
    -> D1 free text (may skip)
```

| Factor | Levels |
|---|---|
| Method (within) | Proposed, Baseline A (role-conditioned), Baseline B (speaker-following) |
| Conversation (within) | 5 scene-1 segments |

**Counterbalancing.** ~~Every participant sees the same fixed order of all fifteen clips
(2026-08-27).~~ **Superseded 2026-08-31: method order is counterbalanced per participant from a
pre-generated schedule; conversation order stays fixed.**

The 2026-08-27 fixed order was adopted for an operational reason — with one known order, a log that
goes missing or arrives misnamed is still identifiable by the position it occupied — and the
objection it answered was to a *runtime shuffle*. A **pre-generated** schedule keeps that property
in full: what any participant should have run is a pure function of their label, so it is recovered
by looking it up rather than by knowing it by heart. The counterbalancing comes back at no cost to
recoverability, which is why the fixed order was dropped.

**The rule.** Block *b* of participant ordinal *i* takes permutation `(i + b) mod 6`, where the
ordinal is `ParticipantLabel.ScheduleOrdinal` — P05 → 0, P06 → 1, and so on. That gives two
properties at once:

- **Within a participant**, the five blocks take five different permutations, so each method sits
  in each serial position once or twice — as even as five blocks over three positions allows — and
  nobody can learn that "the third one is always the proposed method".
- **Across any six consecutive participants**, every block sees all six permutations, so each
  method occupies each serial position *exactly* equally. The marginal balance is exact whenever
  N is a multiple of six — which is why the registered N = 18 is one, and it restores the
  multiple-of-six argument the fixed order had given up.

Both are asserted in `StudySequenceTest`, the second at N = 6 and again at the registered N = 18.

**N = 20 was considered and rejected**: 20 × 5 = 100 block-orders against 9 method × position cells
gives an ideal of 33.33 per cell, so exact balance is impossible at that N under *any* scheme, not
just this one. 18 and 24 both close; 18 was chosen (user's call, 2026-08-31).

**Conversation order stays identical for every participant**, and deliberately so. It cannot enter
the contrast between methods, which is made within a block against the same conversation, so
randomising it buys nothing and would cost the block-identifiability above.

Ordinal 0 — the schedule slot P05 occupies — happens to be the same permutation sequence the four
pilots ran. That is a legitimate cell of the design, not a repeat of them: the pilots are excluded
(§0.1) and are not in the sample the balance is computed over.

The permutation sequence at ordinal 0 is:

```
block 1  study_c1   B A P        B : pos1=2  pos2=1  pos3=2
block 2  study_c2   A P B        A : pos1=1  pos2=2  pos3=2
block 3  study_c3   P B A        P : pos1=2  pos2=2  pos3=1
block 4  study_c4   P A B
block 5  study_c5   B P A
```

Ordinal 1 shifts every block one permutation on, and so on; the marginals close every sixth
participant.

**Where the order is recorded, and when.** Four places, and the order survives losing any of them:

| Where | What it holds | Written |
|---|---|---|
| `Recordings/<participant>/responses.csv` | `block`, `version_position`, `condition` per answer | header at session start; rows flushed as each screen is committed |
| Take sidecar `<stem>.json` | `study_participant`, `case`, `condition`, **`block`, `version_position`, `schedule_ordinal`** | once per take, when it is armed |
| Take filenames | `{participant}_{condition}_{timestamp}` | per take |
| Console, via `StudySessionRunner.Report()` | the whole fifteen-clip order and the ordinal | once, at `Start()`, before the first clip |

And it is recomputable from nothing but the label: `StudySequence.Build(conversations, conditions,
ParticipantLabel.ScheduleOrdinal(label))` is pure, so P11's order regenerates on any machine with no
files present at all.

The three sidecar fields were added 2026-08-31 so that **a take places itself**. Before them the
sidecar knew the participant and the condition but not the position, so an orphaned take had to be
joined to `responses.csv` and the schedule recomputed. They are written **only** on a session take —
a bake, a preview or a plain Play writes its sidecar from `Start()` and omits them, so their
presence is itself the signal that the take belongs to a participant's session.

**Power, and what N = 18 cannot resolve.** The multiple-of-six property makes N = 18 *close
exactly*, but the sensitivity is what has to be reported. At N = 18 a paired test at α = .05
two-sided has 80% power for **d_z ≥ 0.69**. Estimated from the four pilots (paired *d_z* on
per-participant condition means, and pilot estimates at N = 4 are upward-biased, so these are
optimistic):

| contrast | *d_z* (pilot) | N for 80% power | resolvable at N = 18 |
|---|---|---|---|
| N1 Proposed vs A | 2.60 | ~2 | yes |
| I1 Proposed vs B | 1.08 | ~9 | yes |
| I1 Proposed vs A | 0.92 | ~11 | yes |
| T2 Proposed vs A | 0.80 | ~14 | yes |
| **T2 Proposed vs B** | 0.53 | **~30** | **no** |
| **N1 Proposed vs B** | 0.51 | **~32** | **no** |

**The Proposed-vs-B contrasts on N1 and T2 are the weak ones, and not because the gap is small** —
it is +0.85 and +1.10 scale points. The paired SD is what kills them: one of the four pilots
preferred B strongly on N1, T2 and A1 while rating it 1.4 on I1. This is **registered in advance**
(`study-registration.md` §6.1): those two are reported with effect sizes and confidence intervals
and read as *inconclusive* if they miss significance, never as evidence of no difference. The
Proposed-vs-B case rests on the **ranking** and on **I1**, where the effects are large.

Session length ≈35 min: 15 clips × ~25 s = ~6.5 min of stimulus, the rest instrument and setup.

---

## 2. Conditions

The three conditions of `baseline-spec.md`, unchanged. `GazeController` is identical in all three
(`HeadContribution = 0`, eyes only, no per-condition tuning) — the study measures gaze policy, not
animation.

- **Proposed** — holding-replay substrate + EoT prototypes. **Stochastic.**
- **Baseline A** — Shintani et al. 2024 refitted to our corpus. **Stochastic.**
- **Baseline B** — speaker-following (VAD). Deterministic — it carries no random stream and never
  averts, so one recording really is the condition.

**Baseline A *and* Proposed take a seed derived from participant ID × conversation**
(decision 2026-08-21, extended to Proposed 2026-08-25). A fixed seed would let one draw stand in
for a whole condition — an unlucky draw has already been observed in scene 2, where baseline A
happened to avert 100% of a hold. Per-participant seeding samples each condition properly across
the analysed participants, and the resulting variance is absorbed by the participant random effect.
Record every seed in the take's sidecar.

**Proposed was called deterministic until 2026-08-25 and is not.** That was true when its substrate
was baseline B, which carries no random stream; it stopped being true when holding replay landed on
2026-08-14, and the claim was never re-examined. It matters more than baseline A's case, because the
replay draws *whole recorded fixation stretches* and a stretch can be long relative to a 25 s clip:

- Simulated over the draw rule, a 25 s take's aversion fraction has a **standard deviation of about
  13 percentage points**, mean ≈39%. The scan below over 12 seeds of `cand_v008` measured mean 40%
  with a range of 15–56% — an independent confirmation of the same spread.
- So one seed yields an agent that reads as attentive and another one that reads as checked out,
  from the same policy on the same clip. A clip judged by watching a single seed is judged partly
  on its seed.

**Use `GazeControl → Study → Clip Browser → Scan seeds`** before committing to a clip. It simulates
the condition offline over a range of base seeds and reports each agent's aversion fraction, driving
the real policies and the real `ConversationDirector` and deriving seeds with the runner's own
`SeedFor`, so a scanned seed is the draw a take will play — verified against the five baked clips on
both agents. Use it to *see the spread* — whether what you are watching is a property of the clip or
of one draw — rather than to pick the seed: the study's own seeds are deliberately unselected (§3).

**Both stochastic conditions are swept in one press** (2026-08-26). It scanned Proposed only until
then, which looked for an unlucky draw in one half of the comparison; baseline A samples dwells and
jump targets of its own, and scene 2 has already produced a baseline A take that averted 100% of a
hold. Baseline B is refused rather than scanned — it carries no random stream and reads voice
activity rather than the schedule, and the scan runs in silence. Verified the same way as Proposed:
scanning baseline A at each clip's baked seed reproduces the baked track on **all five clips and
both agents**. Since the tick-grid fix of 2026-08-27 (§10) that agreement is exact — every clip,
condition and agent matches the bake tick for tick, the only difference being the one extra
trailing tick the scan simulates when `duration × 60` is not an integer.

---

## 3. Stimulus requirements

**The five clips, decided 2026-08-26** (superseding `case1_seg02/03/04`). Full reasoning, the
shortlist they came from and the speaker map are in `scene1-clip-candidates.md`.

| # | export | stem | len | voices | EoT events | baked seed |
|---|---|---|---|---|---|---|
| 1 | `study_c1` | `trn_2023_v0_036` | 21.9 s | 2×M | t, t | 8 |
| 2 | `study_c2` | `trn_2023_v0_040` | 23.4 s | mixed | i, t | 4 |
| 3 | `study_c3` | `trn_2023_v0_081` | 29.6 s | mixed | **o**, t | 7 |
| 4 | `study_c4` | `trn_2023_v0_119` | 29.3 s | 2×F | i, t | 7 |
| 5 | `study_c5` | `trn_2023_v0_120` | 23.6 s | mixed | t, i, i, t | 4 |

All fifteen tracks are baked at **60 Hz** beside their clip.

**The base seeds were fixed arbitrarily and recorded — they were not selected** (user's call,
2026-08-26). This is the honest description and the stronger one: nothing about the resulting take
influenced which seed was used, so no clip was chosen for looking good. The alternative was
considered and rejected — seeds at the middle of a twelve-seed scan would make each conversation
*typical* of its condition, but with only five conversations that removes exactly the variability
the 2026-08-21 decision wanted sampled, and it shows the method without its tail.

The consequence is visible and accepted: over the ten agent-clips the Proposed condition's aversion
fraction spans **26.8% to 66.7%**, the top end being `study_c5`'s agent B, which looks away for two
thirds of a 24 s clip. Baseline A spans 34.5–58.3%; baseline B never averts. **Report the twelve-seed
scan spread alongside the results** so a reader sees the condition's variability rather than
inferring consistency from five draws.

**The spread to report, measured 2026-08-26** — seeds 1–12 on all five clips, both agents, so 120
simulated agent-takes per condition. Each condition's baked draw is given as its rank within its own
clip × agent distribution of twelve.

| Condition | mean | sd | range over 120 takes | ranks of the ten baked draws | mean rank |
|---|---|---|---|---|---|
| Proposed | 41.5% | 11.2 pts | 18.0–67.9% | 3, 3, 6, 11, 11, 5, 3, 7, 9, 12 | 7.0 |
| Baseline A | 44.7% | 8.7 pts | 21.8–61.8% | 8, 3, 1, 12, 6, 7, 10, 7, 1, 10 | 6.5 |

Three things follow, and all three are worth a sentence in the paper:

- **Baseline A is stochastic but less variable than the proposed method** (sd 8.7 against 11.2
  points), which is what the mechanisms predict: baseline A samples many short dwells, while holding
  replay draws whole recorded stretches whose length distribution has a long tail.
- **The arbitrary seeds behaved like arbitrary seeds.** Both mean ranks sit at the 6.5 expected of
  uniform draws, and the ten ranks cover 1 to 12 in each condition. That is the empirical form of
  the claim §3 makes in words — the seed choice carried no information about the resulting take.
- **The extremes of the baked set are the extremes of the distribution, not accidents of one clip.**
  `study_c5`'s agent B under Proposed (66.6%) is rank 12 of 12, and baseline A's most extreme draws
  are `study_c2` and `study_c5`, where one agent sits at the bottom of its twelve and the other near
  the top — a visibly unbalanced pair rather than an unlucky single agent. Nothing approaches scene
  2's pathological baseline A take (100% of a hold averted): over all 240 scanned agent-takes the
  maximum is 67.9%.

Twelve events, all three EoT classes, seven distinct speakers — the most this shortlist allows,
because only five distinct speaker pairings exist in it and one woman appears in four of the five.
`081` is the only clip carrying an overlapping event, so it is mandatory rather than preferred.

1. **Sentence-clean boundaries are mandatory**, and the five above satisfy it by construction.
   Every window opens just after a sentence-final token and closes on one — the `[.?!]` test scene
   2 already uses. The superseded clips cut mid-sentence, which added rating variance in every
   condition and cost power on the effect of interest. Measured over the corpus, 737 of 1,807
   windows (40.8%) qualify, so the ranking still had plenty to choose from.
2. **Voice type is free** — a clip may be two-male, two-female or mixed.
3. **Texture assignment follows the measured voices**, and mixed-gender pairs are preferred:

   | Clip voices | Agent A | Agent B | Face artifact |
   |---|---|---|---|
   | **Mixed (1M/1F)** | stock `smplx_texture_f_alb.png` | stock `smplx_texture_m_alb.png` | **none — both stock** |
   | Two male | stock `smplx_texture_m_alb.png` | SMPLitex `smplitex_agentB_alb.png` | agent B |
   | Two female | stock `smplx_texture_f_alb.png` | SMPLitex `smplitex_f00021_alb.png` | agent B |

   The rule is **per agent**, not per row: each agent takes the albedo matching its own measured
   voice, so a mixed clip whose agent A is the male reverses the table above (clip 3, `081`, is
   exactly that). The Clip Browser already applies it that way.

   There is exactly one stock albedo per sex, so a same-sex pair forces one agent onto a SMPLitex
   texture and its known nostril misregistration. **Mixed-gender clips are the only configuration
   where both agents are free of it** — prefer them where the candidate ranking allows.
   Whatever the mix, appearance is constant across the three methods of a given conversation, so
   the artifact can never act as a confound; it only adds noise.

---

## 4. Apparatus

**Live VR, not video playback.** `TriadScene` is ported to XR and the deterministic playback runs
around the participant, who stands at the third triad vertex. This is what gives item I1 its force:
an agent's gaze lands on the participant's own eyes rather than on a camera.

The headset provides **eye tracking**, and the participant's gaze is captured (see §7).

**Eye-height calibration is required per participant.** The agents are standing mocap at fixed
height; a participant materially taller or shorter changes the vertical gaze geometry and therefore
whether an agent's gaze reads as directed at them.

**Decided 2026-08-26: a Varjo headset on tethered PC VR.** Tethered rather than standalone because
the segment's motion streams from the corpus on the lab machine (`F:\Data\...`, an absolute path
baked into every `segment.json`) and because SMPL-X's 486 pose correctives plus Oculus LipSync on
two agents is desktop-CPU work.

**The backend is the Varjo Unity XR plugin, decided 2026-08-27 after testing on the headset.** The
port was built against OpenXR on the portability argument, and on real hardware that path renders
fine — `Varjo OpenXR Runtime 4.14.0`, stereo, floor origin, **90 fps** with both agents, which
closes the frame-rate risk. It would not produce gaze: `XR_EXT_eye_gaze_interaction` was available
*and* enabled on the instance, yet no eye-tracking device ever appeared. Varjo's own plugin also
gives more than the extension does — per-eye rays, pupil size and focus distance rather than one
combined ray — so §7's measures are built on `VarjoEyeTracking`. The OpenXR package and its
feature set stay configured but inactive, so the loader can be switched back if the vendor path
disappoints.

The rig is `User` → `Camera Offset` → the head camera, built by `GazeControl → Set Up XR Rig`.
Two properties of it matter to the study:

- **The agents track the participant's real head.** The human's `LookAtAnchor` is that camera, so
  nothing in the gaze pipeline needed changing — in the headset the agents aim where the
  participant's head actually is, which is what item I1 rests on.
- **The participant's viewpoint is a fixed point in the room** (2026-08-27): the camera sits on the
  triad vertex at 1.6846 m and stays there. Head **rotation** tracks; head **translation** does not
  (`TrackedPoseDriver` is `RotationOnly`). Every participant therefore sees the two agents from the
  identical position, at the identical distance, on the identical eye line — height, build and where
  someone drifts to all stop being uncontrolled between-participant variables, and there is no
  calibration step to run or forget.
  - **The height is the agents' own eye line**, measured off their `left/right_eye_smplhf` bones,
    rather than the generic 1.60 m that preceded it and left the participant 8.5 cm below both of
    them. Under mocap the agents' eyes travel 1.663–1.713 m, with a mean 3 mm below the rest-pose
    value, so a fixed height is level with them to within the agents' own postural sway.
  - **It also makes the agents' rendered gaze deterministic.** They aim at the human's
    `LookAtAnchor`, which is this camera; with the camera fixed, that aim is the same for every
    participant and the same as in a desktop preview. Under positional tracking it varied with
    whoever was wearing the headset and how they had drifted.
  - **The cost is no positional parallax, and the pilot must check it.** Swaying or leaning does not
    move the view, and rotation is applied at the eyes rather than about the neck, so turning the
    head pivots the world slightly more than a body expects. This is ordinary 3DOF viewing and the
    task is to stand still and watch for 20–30 s, but comfort is a pilot question, not an assumption.
    A participant who walks gets no visual feedback that they have, so the operator stays in the
    room. If parallax is wanted back, a neck-pivot model restores most of it while keeping the
    geometry identical across participants, since it depends only on head rotation.
  - **The initial orientation is recentred too** (`RecentreView()`): the headset's yaw origin comes
    from the room's tracking setup, not from where the participant stands, so without it a session
    can start with the triad off to one side. It fires automatically on the first tracked frame and
    the operator can repeat it — "look straight ahead, please" — from the component's context menu.
    Yaw only; pitch and roll come from gravity and are already right, and turning them would tilt
    the virtual horizon. A near-vertical head pose is refused rather than guessed.
  - Record the raw measured height per participant (`XrParticipantRig.MeasuredEyeHeight`, read off
    the headset device rather than the camera, which no longer follows the head): the stimulus does
    not depend on it, but it is the datum if height ever has to be checked as a covariate.

**XR is off unless asked for.** "Initialize XR on Startup" is unchecked and `StartXrOnPlay` is
false in the committed scene, so baking, clip browsing and video recording still run flat on a
machine with no headset. The study harness turns it on.

---

## 5. Procedure

1. Consent; demographics and screening questionnaire.
2. VR familiarisation for participants without prior VR experience.
3. Eye-height calibration and eye-tracker calibration.
4. Instructions (§6.1).
5. Five blocks (§1). The item is **displayed in VR and answered aloud**, and the experimenter
   enters the response on the desktop — removing the headset fifteen times is not viable, and a
   controller pointer would put the study on an untested SteamVR path under the Varjo loader
   (decision 2026-08-27; the UI design is `questionnaire-ui-design.md`).
6. Post-study questionnaire (§6.4), which may be answered outside the headset.

---

## 6. Instrument

### 6.1 Framing, read before the first block

> You will watch two people having a conversation. **You are the third person in this
> conversation.** Please pay attention to how the two characters use their eyes, rather than to
> what they are talking about. You will be asked some questions after each conversation.

Deliberately does **not** say the characters may look at the participant (user's call 2026-08-21 —
it primes the answer to I1 and reads as strange). An unprompted effect on I1 is the stronger result.

**Experimenter conduct, because answers are spoken to a person in the room** (§5.5). Read each item
verbatim from the screen the participant is already looking at, take the number, and acknowledge it
with nothing beyond "thank you" — no repeats back, no "really?", no follow-up. The operator panel
never names the condition (`questionnaire-ui-design.md` §2), so the experimenter does not know
which version is the interesting one while they are speaking.

### 6.2 Per-video items (×15), 7-point Likert, 1 = strongly disagree … 7 = strongly agree

| Code | Item | Construct | Prediction |
|---|---|---|---|
| **N1** | The characters' eye movements looked natural. | Gaze naturalness | Proposed > A, B |
| **T2** | The characters' eye movements around the speaker changes felt well-timed. | Boundary gaze quality | Proposed > A, B |
| **A1** | The characters looked at each other as if they were paying attention to each other. | Mutual engagement | Proposed ≈ B > A |
| **I1** | The characters seemed aware that I was there. | Inclusion / being noticed | **Proposed ≫ B** |

Every item names **eye movements** rather than behaviour. Body motion is identical across methods,
so an item that admits motion into the judgment is partly rating a constant, which dilutes the
effect (user's correction 2026-08-21).

**I1 carries the sharpest prediction.** Baseline B is 97–98% agent-to-agent and never looks at the
participant; the Proposed condition's holding replay gives the user 4–50% of out-of-window gaze.
The conditions differ here by a large, pre-measured margin.

**T2 tests the prototypes specifically** — it is the only item aimed at the pre-turn windows, which
are where the Proposed condition differs from a plain substrate. It asks about the *quality* of
gaze around a boundary, never about predicting who speaks next.

Items removed during design, and why: an overall-believability item ("behaved the way I would
expect real people to behave") admitted body motion, which is constant; a next-speaker-prediction
item contradicted the paper's own position and had no behavioural counterpart, since the
participant cannot take a turn.

### 6.3 Per-block items (×5)

> **R1 (primary DV).** Rank the three versions by how natural the characters' **eye movements**
> were. 1 = most natural, 3 = least natural. No ties.

> **D1.** Did any version feel uncomfortable or unsettling to watch? Which, and why? (may skip)

Ranking is the primary measure rather than the Likert items: it is immune to scale-use bias, far
more sensitive to small differences, and it exploits the fact that the three versions are identical
apart from gaze.

D1 exists to separate "unnatural" from "unnerving" — in VR, baseline B is two agents locked on each
other and ignoring the participant for the whole clip, and that may read as eerie rather than
merely wrong.

### 6.4 Post-study (×1)

- **Manipulation check** — "The three versions of each conversation differed in some way. Did you
  notice what differed?" (free text). This distinguishes "no effect" from "the difference was not
  perceived", and its absence is a standard criticism of comparable published studies.
- **Demand check** — "What do you think this study was investigating?" (free text).
- **Simulator sickness** — single item.
- **Demographics** — age, gender, field, VR experience (none / occasional / regular), prior
  experience with virtual agents, and **English listening comprehension**. The corpus audio is
  conversational English; comprehension is an inclusion criterion, not merely a covariate.

---

## 7. Objective measures — participant gaze is out of scope (2026-08-31)

**Participant eye tracking is not part of this study** (user's call, 2026-08-31). The reason is
construct validity, not capability. §6.1 instructs the participant to attend to the characters'
eye movements, and they can never take a turn — so what the tracker would record is *task-driven
scanning under an instruction*, not the gaze of a third party in a conversation. Every measure
below inherits that:

- **Gaze allocation (face vs body)** would chiefly measure whether the instruction was followed.
- **Mutual gaze proportion** is inflated by the same instruction in every condition.
- **Gaze-return latency** is worse than confounded, it is *floored*: a participant already fixating
  faces has no return to make, so the measure has no room to vary.

The comparison to `ismar2024-agent-behavior-generation-user-study.md` §6.2 is the point — those
participants were conversing freely and unprompted, so their allocation and mutual-gaze numbers
mean what the authors say they mean. Ours would not. **Report this as a design decision with its
reason**, not as a missing measure.

**Confirmed empirically, and it made the decision cheap.** Across P01-P04 every one of ~97,500
logged ticks reads `tracker=Invalid, calibrated=0, target_type=invalid` — the tracker never
delivered a sample in sixty clips. Head pose *was* captured and is real (yaw spans roughly ±25°,
participants do turn between the agents).

**What replaces it: objective evidence about the stimulus rather than about the participant.** The
20-column agent log plus its sidecar already characterise every baked track — target distribution
by role, aversion fraction, shift timing relative to each EoT boundary — and
`Research/corpora/gaze_events/gaze_events_md6.csv` is the human reference to score them against.
That yields a **distributional-fidelity** result: how closely each policy reproduces measured human
gaze statistics. It needs no participants, it is deterministic per seed, and it fits the thesis
better than participant tracking did — *the gaze reproduces human statistics* and *people prefer
it* are the two halves of one argument.

**The capture stays built and keeps running.** `ParticipantGazeLogger`, `VarjoGazeSource` and
`GazeSphereTargeting` are tested and cost nothing to leave in, and head pose may be worth an
exploratory look later (baseline B drew 9.2 head switches/min against 7.0-7.1 for the other two
across the four pilots — suggestive, nothing more, and not something to build on). Nothing here is
promised as an analysis.

---

### 7.1 Superseded — the participant-gaze plan, kept for the record

Eye tracking makes the human's gaze observable for the first time, which populates the
`mutual_gaze_with_human` column that `baseline-spec.md` §6 has always reserved but could never fill.

Log the participant's head pose and gaze ray at the decision rate, into the take's own folder
alongside the existing 20-column agent log.

**Built 2026-08-27** (`ParticipantGazeLogger` + `VarjoGazeSource` + `GazeSphereTargeting`, 18 tests):
`<stem>_user.csv` beside the agent log, one row per decision tick, carrying tracker and per-eye
status, the combined and per-eye rays in world space, head pose, focus distance, pupil diameters and
eye openness, `target_type`/`target_id`/`target_angle_deg` for what the ray landed on, `mutual_gaze`,
and `agents_looking_at_user` as a bit set. The four measures below are **not computed anywhere yet**
— this is the capture, and they are offline analysis over these files. Nothing has been read from a
real headset yet; see §9.2.

1. **Mutual gaze proportion (user ↔ each agent)** — head bounding spheres; A looks at B when A's
   gaze ray intersects B's sphere, mutual when simultaneous. Prediction: Proposed ≫ B.
2. **Gaze allocation on the agent mesh** — per-frame raycast, counts per triangle, rendered as a
   face-versus-body heatmap.
3. **Gaze distribution over the take** — agent A / agent B / elsewhere, and whether it tracks who
   holds the floor.
4. **Gaze-return latency** — when an agent begins looking at the participant, how long until the
   participant looks back, and how often. Specific to this setup, and the most direct evidence that
   an agent's gaze was *read* as directed — the mechanism behind I1.

---

## 8. Analysis plan

**Registered 2026-08-31 in `study-registration.md` §6**, by commit, before any analysed participant
runs. That document is authoritative for what is tested; this section is the working detail behind
it. There is **no external registry** (user's call), so the paper may say the analysis was *fixed in
advance and recorded in the project repository* and must not say "pre-registered".

**Primary — R1 rankings.** Per participant, average the 5 rankings per method into one mean rank.
Friedman test across the three methods on the 18 analysed participants; Kendall's *W* for effect size; post-hoc
pairwise Wilcoxon signed-rank with Holm correction. Report mean rank per method.
Secondary: a cumulative-link mixed model over all 150 trial-level rankings with random intercepts
for participant and conversation.

**Likert items.** One linear mixed model per construct: method as fixed effect, random intercepts
for participant *and* conversation (random slope for method by participant where it converges).
Holm correction across the four constructs. Report estimated marginal means with CIs.

**Eye-tracking measures.** Aggregate to one value per participant per method (mean over the 5
conversations), then repeated-measures ANOVA or its non-parametric equivalent.

**The one thing not to do.** Never run a test that treats the 15 clips, the 150 rankings, or
individual gaze samples as independent observations. Per-sample tests on eye-tracking data are a
common error in this literature and produce impressive F values beside effect sizes near 0.0001.
Aggregate first.

---

## 9. Build requirements

Nothing below exists yet; all of it gates data collection.

1. ~~**Sentence-final boundary rule**~~ done (2026-08-21) and the five clips chosen (2026-08-26,
   §3); **exporting them and baking a track per condition is still to do**.
2. ~~**XR port of `TriadScene`**~~ — stereo rendering, standing play area and eye-height
   calibration are **built** (2026-08-26, §4) and **confirmed on a real Varjo** (2026-08-27,
   through the OpenXR path): subsystems start, floor-relative origin, stereo renders, head pose
   drives the camera, **90 fps** with both agents. Still to confirm in a headset session: the same
   run through the *Varjo loader* (the backend switched after those measurements), eye-height
   calibration on a worn headset, and that the geometry reads as eye-to-eye from inside.
3. **In-VR questionnaire UI** — designed 2026-08-27 in `questionnaire-ui-design.md`, not built.
   22 display-only screens in the headset plus a desktop operator panel for entry. The "likely more
   work than the XR port itself" estimate was for a controller pointer; dropping the pointer
   (§5.5) removes XRI, the interaction layer and the SteamVR dependency, and leaves a build whose
   first two of four steps need no headset at all.
4. ~~**User gaze and head logging** at the decision rate~~ — **built 2026-08-27** (§7), unverified on
   real hardware. **The four derived measures of §7 are still to write**, as offline analysis over
   the captured `_user.csv` files.
5. ~~**Study harness**~~ — **built 2026-08-27**: `StudySessionRunner` runs all fifteen clips in one
   play session, re-arming the conversation and the gaze runner in place between them, with
   `ClipPauseController` stopping the scene after each clip so the questionnaire can be answered.
   One key (Space) starts, skips and advances. Still to do: hooking the response writer to the
   session, and one folder per participant. Formerly: sequences the 15 runs, applies Baseline A's
   *and* Proposed's seed from participant × conversation, and writes one folder per participant.
6. **Ethics approval** and a participant information sheet.
7. ~~**Fix run-to-run reproducibility of the Proposed condition**~~ — done 2026-08-25, see §10.

---

## 10. Open items

- ~~**BLOCKER — the Proposed condition does not reproduce run to run.**~~ **Fixed 2026-08-25.**
  Measured before the fix: two runs of `cand_v014` with identical segment, condition and per-agent
  seeds (902093031 / 1194167276) gave AgentB an identical decision sequence (27 of 27 episodes)
  while **AgentA diverged at episode 6**, so the recorded seed did not fully determine the take.
  - The suspected mechanism was the mechanism. The runner ticked its policies from the moment Play
    started, while the conversation's clock starts only once the segment has loaded and the voices
    are scheduled; those pre-roll ticks consumed draws and advanced every fixation clock, and the
    pre-roll length varies with load time. The decision grid is now anchored on the conversation's
    clock (`DecisionClock`): no tick runs while that clock reads negative, and tick *k* belongs to
    conversation time *k · step* however many frames it took to get there.
  - Verified end to end, not just in unit tests: two independent bakes of `cand_v008` / Proposed /
    seed 8 (per-agent 902093031 and 1194167276 — the seeds that diverged) are **byte-identical
    apart from the `bakedAtUtc` stamp**: 706 samples per agent, 41/41 and 56/56 episodes equal.
  - **The offline seed scan and the live runner tick the same grid now**, which they could not
    before: `SeedScan` has always ticked at `t = k · step` from conversation zero, so anchoring the
    live grid the same way made them one grid. Scanned seed 8 on `cand_v008` predicted aversion
    0.596 / 0.516 and the bake of that configuration measured exactly that.
    - **It briefly stopped agreeing, and the cause was the scan's, not the runner's.**
      `SeedScan` swept twelve seeds through **one** `ConversationDirector`, resetting only the
      clock — but the director advances monotonically by design (`SyncTo` only ever walks
      `_index` forward, because a live conversation never goes back). So the first seed scanned was
      right and every later one ran the whole simulation against a schedule frozen on the final
      turn. That is why a single-seed scan of `cand_v008` matched a bake exactly while the
      twelve-seed scans used to pick the study clips were off by up to 25 points. The scan now
      re-publishes the schedule per seed, which calls `Enter(0)`. **Verified against ground truth:
      it reproduces all five baked clips on both agents to a tenth of a point.**
  - **Baseline B is not bit-reproducible across bakes, though the other two are.** Its input is the
    audio playback position, scheduled against the DSP clock, so *when the first voice onset is
    confirmed* can land a few ticks either side of the decision grid. Measured over a re-bake of all
    five clips at identical seeds: four were identical and `study_c4` moved by 19 ticks (0.32 s),
    changing only how long the agents watch the participant before anyone has spoken. It does not
    affect a study session — participants replay the file — but a re-bake of baseline B is not
    guaranteed byte-identical the way `Proposed` is.
  - **Residual, and the reason a study still replays a baked track rather than re-deriving one:**
    a tick reads the live scene at the frame it fires on, so voice activity and a turn boundary can
    still land one frame either side of the grid. Baking removes that; nothing in a live take can.
    - **That residual is now measurable in the Proposed condition, and the "two bakes are
      byte-identical" claim above does not survive the move to 60 Hz** (measured 2026-08-26, while
      regression-testing the XR port). Five bakes of `study_c5` / Proposed / seed 4 all differ from
      each other by a handful of ticks — the last two run back to back under matched editor
      settings differ at **4 ticks of 2,836** (two agents × 1,418), almost all at t ≈ 3.3–3.5 s
      inside a prototype window, flipping a single tick between a person and an aversion. Seeds,
      sample counts and every other tick are identical, and the aversion fraction moves by at most
      **0.14 percentage points** — which is why the §3 numbers are unaffected.
    - Cause: `ProposedGazePolicy` reads its position in the prototype from
      `state.PredictedTimeToTurnEnd`, which the director derives from the *frame's* conversation
      clock. `DecisionClock` fixes which frame tick *k* runs on, but not the sub-frame remainder of
      that frame's clock reading — so a prototype segment one frame long (13% of segments are
      shorter than a 30 Hz tick, and at 60 Hz a tick is exactly one prototype frame) is sampled or
      missed depending on frame timing. At 30 Hz a tick spanned two prototype frames and filtered
      this out, which is why the earlier byte-identical result was real at the time.
    - **Fixed 2026-08-27 (user's call), and all fifteen tracks re-baked on the fix.**
      `ConversationDirector` no longer pulls a live clock delegate on every read: the runner calls
      `SyncTo(k · step)` once per decision tick, so time-to-boundary, time-since-boundary and turn
      phase are all measured on the decision grid, and `ConversationState.Time` carries the tick's
      instant instead of Play's session clock. The director also stopped advancing in its own
      `Update`, which removes the ordering hazard the delegate existed for — it is now driven by
      the one component that knows what instant is being decided at.
    - **Verified**: two consecutive bakes of `study_c5` / Proposed / seed 4 are byte-identical
      apart from the `bakedAtUtc` stamp — 0 differing ticks of 2,836, against 4 before the fix —
      and the bake lands on the offline scan's prediction exactly (A 0.5028 / B 0.6671). 170/170
      tests green.
    - The remaining scan-vs-bake gap is arithmetic, not disagreement: the scan simulates
      `ceil((duration + tail) / step)` ticks while a bake stops when the conversation finishes, so
      when `duration × 60` is not an integer the scan counts **one extra trailing tick** (e.g.
      `study_c1`: 448/1315 baked against 449/1316 scanned). Within their shared ticks they agree.
  - Baselines A and B were never implicated by the original measurement; B carries no random stream,
    and A was not tested the same way. Both run on the fixed clock now regardless. **A has since
    been tested** (2026-08-26): the offline scan, which ticks the same grid from conversation zero,
    reproduces all five baked baseline A tracks on both agents to within 0.05 percentage points of
    aversion — so a baseline A take is determined by its seed exactly as a Proposed one is.

- Pilot the full session on 3–5 people before recruiting: session length, whether the framing reads
  naturally, whether the manipulation is perceived at all.
- ~~Formal power calculation to confirm N = 30.~~ Done 2026-08-31: N = 18 registered, sensitivity
  d_z ≥ 0.69, and the two contrasts it cannot resolve are named in `study-registration.md` §6.1.
- Whether Baseline A's per-participant seeding is reported as a strength (the condition is sampled,
  not exemplified) or additionally held constant for a secondary comparison.
