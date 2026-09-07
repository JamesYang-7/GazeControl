# Gaze Baseline Implementation Spec

Two baseline gaze policies for a triadic conversational setting: **2 virtual agents + 1 human user**.
These are comparison conditions for a user study. Implement them as swappable policies behind a
common interface, sharing one animation layer.

---

## 0. Non-negotiable architectural constraints

Read this section before writing code. These are the things that most often get implemented wrong.

1. **One shared animation layer.** The saccade dynamics, eye–head coupling, blink model, and idle
   micro-motion are implemented **once** and used identically by every condition, including the
   proposed method. Baselines differ *only* in which target they select and when. If the animation
   quality differs between conditions, the study measures animation, not gaze policy.
2. **Two-layer timing.** The *policy* runs on a decision tick, the *animation*
   runs per frame. **The tick is 60 Hz** (2026-08-26), not the 20–50 Hz this line used to call
   plenty: both data sources are 60 fps, so 60 Hz is one tick per recorded frame. The prototypes
   are 1 s windows of 60 frames and map onto the grid 1:1, and the holding corpus's fixation
   durations are exact multiples of 1/60 s — at 30 Hz every other one was rounded, a mean error of
   8.17 ms against 0.02 ms at 60. Do not couple them; do not re-decide the gaze target every frame. The decision
   tick is anchored on the **conversation's** clock, not on the moment Play starts: a free-running
   tick spends the variable pre-roll before a segment loads on decisions nobody sees, and a
   stochastic policy then consumes a different number of draws in every run.
3. **Baseline A agents must not share state.** Each agent instance gets its own RNG with its own
   seed and no knowledge of the other agent's current gaze target. This is deliberate — the whole
   point of the baseline is to show what uncoordinated per-agent gaze looks like.
4. **Log everything, per frame, for every condition.** See §4.
5. **Seeding.** Fix seeds per (participant × condition × agent) and record them, so any trial can be
   replayed exactly.

---

## 1. Common interface

```
enum Role       { SPEAKER, ADDRESSEE, SIDE_PARTICIPANT }
enum TurnPhase  { TURN_START, TURN_MIDDLE, TURN_END_APPROACHING, NOT_SPEAKING }
enum TargetType { PERSON, AVERSION }

struct ConversationState {
    float    t;                    // seconds
    ID       current_speaker;      // may be NONE during gaps
    ID       current_addressee;    // from dialogue manager; may be NONE
    ID       self_id;
    Role     self_role;
    TurnPhase turn_phase;
    float    time_since_turn_start;
    float    predicted_time_to_turn_end;   // negative/NaN if unknown
    ID[]     partners;             // the other two participants
    bool     mutual_gaze_active;   // is a partner currently looking at me
    float    mutual_gaze_duration;
}

struct GazeTarget {
    TargetType type;
    ID         person_id;          // valid iff type == PERSON
    Vec2       aversion_offset;    // yaw/pitch in degrees, valid iff type == AVERSION
}

interface GazePolicy {
    GazeTarget update(float dt, ConversationState s);
    void reset(int seed);
}
```

Aversion targets: sample from a small fixed set of offsets relative to the agent's forward direction,
avoiding both partners' directions. Use roughly `{up-left, up-right, down-left, down-right, down}` at
15–30° yaw and 10–20° pitch. Downward aversion should be the most frequent (thinking/planning);
upward next.

---

## 2. Baseline A — Role-conditioned gaze model (Shintani et al. 2024), per-agent independent

**Reference:** Shintani T, Ishi CT, Ishiguro H. *Gaze modeling in multi-party dialogues and
extraversion expression through gaze aversion control.* Advanced Robotics 38(19–20):1470–1485, 2024.
A copy is in `Research/papers/` (git-ignored). Implemented as `ShintaniGazePolicy` +
`ShintaniGazeParameters`, wired as the `RoleConditioned` condition.

**This section was rewritten on 2026-09-06 and no longer describes a "footing" model.** Baseline A
was originally specified here from the literature (Goffman's participation framework; Mutlu et al.
2009; Kendon 1967 for turn-boundary timing; Vertegaal et al. 2001 for target proportions) with
hand-chosen constants. That was superseded on 2026-07-30, and the old text — its tuned transition
matrix, its dwell table, its two Kendon overrides and its mutual-gaze rule — described something that
was never built. §0, §1, §3, §5 and §6 are unaffected.

### Mechanism

A semi-Markov dwell-and-shift sampler over four gaze states — the corpus's three roles `sp` (current
speaker), `ad` (second speaker, see the caveat below), `sd` (side participant), plus `aversion` —
conditioned on the agent's own role and on whether the turn is `changing` or `holding`. The agent's
own role is never a target, so one of the three person states is always closed off.

```
role = self's corpus role (sp / ad / sd)
turn = (distance to the nearest turn instant <= turn_window) ? CHANGING : HOLDING

if (first decision)                       // nothing to jump away from yet
    state = sample S[role][turn]          // occupancy ratios: what a run settles into
else if (now >= current_dwell_end)
    state = sample P[role][current_state] // jump chain; self-transitions are impossible

on entering a state:
    dwell = truncated_lognormal(mu[role][turn][state], sigma[role][turn][state])
    if (state == aversion) pick a direction, and re-pick one every retarget_seconds
```

`turn_window` is 1.0 s and covers both sides of the instant — time since the last one *and*
predicted time to the next — which is why the turn end has to be known in advance (§5.4). The
`changing` half of the parameters is only observable at all because of that.

### Parameters — fitted, not chosen

Every number lives in `Assets/GazeControl/Resources/BaselineAParameters.json`, regenerated by
`Tools/build_baseline_a_params.py` from `Research/corpora/gaze_events/gaze_events_md6.csv` (120,765
fixations, 38 sessions, 60 fps). The corpus is git-ignored and the JSON is committed, which is what
makes the condition reproducible without it; the file records its own provenance under `meta`. **Do
not hand-edit it, and do not re-introduce literature constants here** — re-estimating on our own
triad corpus is strictly better than citing published tables, and that is the whole reason this
section no longer carries a table of tuned values.

For orientation only, the fitted occupancy ratios `S` — **proportions of total time**, not
conditional probabilities (§5.2):

| Agent's role | Turn | at `sp` | at `ad` | at `sd` | Aversion |
|---|---|---|---|---|---|
| `sp` (speaking) | holding | — | 0.33 | 0.23 | 0.44 |
| `sp` (speaking) | changing | — | 0.34 | 0.23 | 0.43 |
| `ad` (addressed) | holding | 0.51 | — | 0.13 | 0.36 |
| `ad` (addressed) | changing | 0.41 | — | 0.18 | 0.42 |
| `sd` (side) | holding | 0.46 | 0.15 | — | 0.39 |
| `sd` (side) | changing | 0.35 | 0.22 | — | 0.43 |

**Caveat to report: `ad` is not an annotated addressee.** In our corpus it is `second_speaker`, a
hindsight label for whoever actually spoke next — information the participant did not have at the
time — and `first_speaker[k] == second_speaker[k-1]` holds only 47.9% of the time. Shintani's
Addressee is a different variable, and `sd` inherits the same looseness. See the corpus README §6.

### Departures from the published model

Three, each forced by what the corpus shows; the reasoning is in `build_baseline_a_params.py`'s
header and in `PROGRESS.md`'s decisions log.

1. **Lognormal dwells, not chi-square.** The paper's duration law needs `dn > 2` for an interior
   mode and 0 of 45 role × target × `min_dwell` cells reach it (fitted `dn` 0.88–1.78), so it
   degenerates toward exponential on this data; lognormal fits better in 8 of 9 cells (mean KS 0.051
   vs 0.070). Draws are truncated to `[0.1, 8.0]` s — the lower bound is the corpus's own fixation
   min-dwell filter, the upper stops one draw freezing gaze for a whole turn.
2. **Target selection uses the corpus transition matrix's jump chain, not i.i.d. draws from `S`.**
   Sampling i.i.d. would put person→person shifts at 0.35 where we measure 0.10.
3. **Aversion re-targeting keeps the paper's 0.7 s constant over our own measurement** (2026-08-02,
   after watching the first takes). The measured direction-segment median of 0.217 s gives a
   four-second aversion thirteen direction changes, which reads as darting; those segments come from
   a head-mounted eye tracker and include movement below what an annotator would call a gaze shift.
   The corpus's own unbiased estimator says 0.939 s, so 0.7 s sits inside its 0.6–0.94 s range. The
   measured statistics stay in the JSON as provenance for the choice.

**The paper's extraversion axis is dropped.** We have no personality ratings to interpolate between,
so there is one parameter set rather than a spectrum.

### No hand-authored overrides, no mutual-gaze breaking

Both were prescribed by the superseded §2 and are deliberately absent (decision 2026-07-30):

- **No Kendon turn-boundary overrides.** The `changing` half of the fitted parameters *is* the
  empirically-estimated version of the same effect, and a scripted turn-yielding gaze inside
  baseline A would fire in exactly the window the proposed method overrides — contaminating the
  comparison, and, since A was then also the proposed method's substrate, both conditions at once.
- **No mutual-gaze breaking.** Shintani has no such term, and the fitted 0.77–0.88 probability of
  jumping from a person to aversion makes long lock-ins unlikely on its own. `mutual_gaze_*` is
  still logged (§6), so the assumption stays checkable.

### What this baseline is expected to do badly

Do not "fix" these — they are the finding:
- both agents averting at the same moment
- no agent↔agent mutual gaze during agent-to-agent exchanges
- the two agents drifting into unnaturally synchronised or unnaturally divergent attention

---

## 3. Baseline B — Speaker-following (VAD-driven)

Deliberately simple, but it is what most deployed multi-agent systems actually do, and it produces
coherent joint attention for free.

### Rule

```
if (self is speaking)            -> look at current_addressee
                                     (fallback: the human user)
else if (someone else speaking)  -> look at that speaker
else (silence/gap)               -> hold last target
```

### Hysteresis — do not skip this

Naive VAD-following jitters badly and will unfairly weaken the baseline.

- **Onset threshold:** a new speaker must be voiced continuously for **250 ms** before the agent
  switches to them.
- **Offset threshold:** require **500 ms** of silence before treating a turn as ended.
- **Backchannel suppression:** utterances shorter than **600 ms** (e.g. "mm-hm", "yeah") do not
  trigger a gaze switch.
- **Minimum dwell:** once switched, hold the target for at least **700 ms** regardless of VAD.
- **Overlap:** if two people are voiced simultaneously, stay with the current target until one drops
  out past the offset threshold.

### Notes

- Both agents run identical logic, so they naturally align — that is the baseline's strength.
- Add small aversion only if you also add it to the proposed method; otherwise leave it out and
  report that this condition has no aversion behaviour.

---

## 4. Shared animation layer (identical across all conditions)

- **Eye–head split.** Gaze shifts under ~15° are eyes-only. Beyond that the head contributes the
  residual. Clamp eye yaw to ±35°, eye pitch to ±25°.
- **Saccade.** Eye rotation to target in 30–80 ms depending on amplitude, roughly linear in
  amplitude; ease-out profile.
- **Head latency.** Head begins moving 50–100 ms after the eyes, at 100–300 °/s, ease-in-out.
- **VOR.** Counter-rotate the eyes against head motion so the fixation point stays stable while the
  head is still settling.
- **Blinks.** Base rate ~17/min, duration 100–150 ms. Elevate blink probability at the moment of a
  gaze shift (blinks and saccades co-occur in humans).
- **Idle motion.** Small ocular drift/microsaccades (<1°), subtle breathing sway, occasional
  small head reorientations. Constant across conditions.
- **Torso/body.** Keep fixed, or use one slow orientation rule shared by all conditions. Do not let
  body orientation vary between conditions.

---

## 5. Pitfalls to state explicitly

1. **Do not let the two agents in Baseline A coordinate.** No shared RNG, no peeking at the other
   agent's target, no "avoid both looking away" rule.
2. **The published 77% / 88% figures are conditional probabilities** — the probability that, *given a
   person is being looked at*, it is the addressee/speaker. They are **not** proportions of total
   time. Using them as total-time proportions is a common misimplementation and inflates gaze-at-
   person time. The table in §2 already gives total-time proportions.
3. **Aversion is a first-class target, not "idle."** Modelling it as noise or as a screensaver
   behaviour weakens Baseline A and makes the comparison unfair.
4. **Turn-end timing must be predictive, not reactive.** Baseline A's `changing` window and the
   proposed method's prototypes both open *before* the utterance ends, so the boundary has to be
   known in advance; do not detect the end and then react. It comes from the segment's turn
   schedule, published by `ConversationDirector`.
5. **No per-condition tuning of the animation layer.**

---

## 6. Logging (required for the analysis)

Per agent, per frame, append to CSV:

```
t, participant_id, condition, agent_id, self_role, turn_phase,
current_speaker, current_addressee,
target_type, target_id, aversion_yaw, aversion_pitch,
is_shifting, mutual_gaze_with_human, mutual_gaze_with_other_agent,
head_yaw, head_pitch, eye_yaw, eye_pitch, seed
```

This supports the metrics you will need: proportion of gaze per target by role, gaze-shift rate,
dwell-duration distributions, empirical transition matrices, mutual-gaze rate and mean duration
(human–agent and agent–agent separately), and addressee-signalling accuracy.

---

## 7. Suggested build order

1. Shared animation layer + a manual gaze-target driver, verify it looks right in isolation.
2. Logging.
3. Baseline B (simple; validates the interface end-to-end).
4. Baseline A.
5. Proposed method.
6. A replay harness that can re-run a logged conversation transcript through any policy offline, so
   the objective metrics can be computed without running participants.

Do not implement A and B in the same pass. Build the shared layer first and confirm it, then add one
policy at a time.