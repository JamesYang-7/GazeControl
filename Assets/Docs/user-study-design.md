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

**Counterbalancing.** Method order within a block has 6 permutations; each participant receives a
different permutation per block, rotated across participants so every method appears in every
serial position equally often. Conversation order is randomised independently per participant.

**N = 30** (a multiple of 6, so the counterbalancing closes exactly). Power for a 3-level within
factor at f = 0.25, α = .05, power .80 is ≈28; run the formal calculation before committing.

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
the 30 participants, and the resulting variance is absorbed by the participant random effect.
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
`SeedFor`, so a scanned seed is the same draw a take will play. Pick a seed near the middle of the
scanned spread rather than accepting whichever one the scene was left on.

---

## 3. Stimulus requirements

Five scene-1 segments, re-selected (the existing `case1_seg02/03/04` are superseded). The
thirteen candidates they are chosen from, with their stable `--stem` addresses, event mixes and
voices, are in `scene1-clip-candidates.md`.

1. **Sentence-clean boundaries are mandatory.** Every window must open just after a sentence-final
   token and close on one — the `[.?!]` test scene 2 already uses. The current clips cut
   mid-sentence, which adds rating variance in every condition and costs power on the effect of
   interest. Measured over the corpus, 737 of 1,807 windows (40.8%) qualify, so the ranking keeps
   plenty to choose from.
2. **Voice type is free** — a clip may be two-male, two-female or mixed.
3. **Texture assignment follows the measured voices**, and mixed-gender pairs are preferred:

   | Clip voices | Agent A | Agent B | Face artifact |
   |---|---|---|---|
   | **Mixed (1M/1F)** | stock `smplx_texture_f_alb.png` | stock `smplx_texture_m_alb.png` | **none — both stock** |
   | Two male | stock `smplx_texture_m_alb.png` | SMPLitex `smplitex_agentB_alb.png` | agent B |
   | Two female | stock `smplx_texture_f_alb.png` | SMPLitex `smplitex_f00021_alb.png` | agent B |

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

---

## 5. Procedure

1. Consent; demographics and screening questionnaire.
2. VR familiarisation for participants without prior VR experience.
3. Eye-height calibration and eye-tracker calibration.
4. Instructions (§6.1).
5. Five blocks (§1), answered in VR via controller pointer — removing the headset fifteen times is
   not viable.
6. Post-study questionnaire (§6.4), which may be answered outside the headset.

---

## 6. Instrument

### 6.1 Framing, read before the first block

> You will watch two people having a conversation. **You are the third person in this
> conversation.** Please pay attention to how the two characters use their eyes, rather than to
> what they are talking about. You will be asked some questions after each conversation.

Deliberately does **not** say the characters may look at the participant (user's call 2026-08-21 —
it primes the answer to I1 and reads as strange). An unprompted effect on I1 is the stronger result.

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

## 7. Objective measures from captured user gaze

Eye tracking makes the human's gaze observable for the first time, which populates the
`mutual_gaze_with_human` column that `baseline-spec.md` §6 has always reserved but could never fill.

Log the participant's head pose and gaze ray at the decision rate, into the take's own folder
alongside the existing 20-column agent log.

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

Fix before data collection; pre-register the primary comparisons.

**Primary — R1 rankings.** Per participant, average the 5 rankings per method into one mean rank.
Friedman test across the three methods on 30 participants; Kendall's *W* for effect size; post-hoc
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

1. **Sentence-final boundary rule** in `Tools/find_demo_segments.py`, then five re-selected
   segments exported (§3).
2. **XR port of `TriadScene`** — stereo rendering, standing play area, eye-height calibration.
3. **In-VR questionnaire UI** — 15 rating screens and 5 ranking screens, controller pointer.
   Likely more work than the XR port itself.
4. **User gaze and head logging** at the decision rate, plus the four derived measures of §7.
5. **Study harness** — sequences the 15 runs, applies the counterbalancing, derives Baseline A's
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
  - **The offline seed scan and the live runner now agree exactly**, which they could not before:
    `SeedScan` has always ticked at `t = k · step` from conversation zero, so anchoring the live
    grid the same way made them the same grid. Scanned seed 8 on `cand_v008` predicts aversion
    0.596 / 0.516 and the bake of that configuration measures 421/706 = 0.596 and 364/706 = 0.516.
    A scan is therefore a sound way to choose a clip's seed without recording it first.
  - **Residual, and the reason a study still replays a baked track rather than re-deriving one:**
    a tick reads the live scene at the frame it fires on, so voice activity and a turn boundary can
    still land one frame either side of the grid. Baking removes that; nothing in a live take can.
  - Baselines A and B were never implicated by the original measurement; B carries no random stream,
    and A was not tested the same way. Both run on the fixed clock now regardless.

- Pilot the full session on 3–5 people before recruiting: session length, whether the framing reads
  naturally, whether the manipulation is perceived at all.
- Formal power calculation to confirm N = 30.
- Whether Baseline A's per-participant seeding is reported as a strength (the condition is sampled,
  not exemplified) or additionally held constant for a secondary comparison.
