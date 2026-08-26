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

## 2. Baseline A — Role-conditioned ("footing") gaze model, per-agent independent

**Reference framing:** role-specific gaze transition models for speaker / addressee /
side-participant (Goffman's participation framework; Mutlu et al. 2009; Kendon 1967 for turn-boundary
timing; Vertegaal et al. 2001 for target proportions).

### Mechanism

A dwell-and-shift sampler:

```
if (now >= current_dwell_end) {
    next_target = sample_target(self_role, current_target, s)
    dwell       = sample_dwell(self_role, next_target)
    current_dwell_end = now + dwell
    emit shift to next_target
}
```

Plus **two Kendon turn-boundary overrides** that pre-empt the sampler:

- **Turn onset (self is speaker):** for the first `U(0.5, 1.5)` seconds of the agent's own utterance,
  force an aversion target (planning gaze-away). Apply on ~70% of turns, not all.
- **Turn yielding (self is speaker):** during the final ~`U(0.4, 1.0)` seconds before the agent's
  utterance ends, force gaze at the addressee, and hold it through the transition.

The turn-end time is known in advance for a scripted/TTS agent — use the synthesizer's duration.

### Target selection probabilities

Target the following **proportions of total time** (not conditional probabilities — see §5.2).
Implement as a first-order Markov transition matrix over
`{addressee/speaker, other_partner, aversion}`, tuned so the stationary distribution matches:

| Agent's role | Gaze at addressee | Gaze at other partner | Aversion |
|---|---|---|---|
| SPEAKER | 0.45 | 0.15 | 0.40 |
| ADDRESSEE (listening) | 0.65 (at speaker) | 0.10 | 0.25 |
| SIDE_PARTICIPANT | 0.55 (at speaker) | 0.20 (at addressee) | 0.25 |

Forbid self-transitions (a "shift" to the current target); renormalise the row.

These numbers are a defensible starting point from the literature (speakers gaze substantially less
than listeners — Argyle & Cook; listeners concentrate gaze on the current speaker — Vertegaal et al.).
**Expose them as a config file, not constants.** If you have your own triad corpus, re-estimate the
matrix from it and report that you did; that is strictly better than citing published values.

### Dwell durations

Log-normal, truncated to `[0.3, 8.0]` s:

| Target | median | approx. σ (log-space) |
|---|---|---|
| Gaze at a person | 2.0 s | 0.6 |
| Aversion | 1.2 s | 0.5 |

### Mutual gaze breaking

If `mutual_gaze_active` and `mutual_gaze_duration > 2.5 s`, multiply the per-tick hazard of shifting
away by ~3. Without this, two agents locked on each other look uncanny.

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
4. **Turn-end timing must be predictive, not reactive.** The yielding gaze has to begin *before* the
   utterance ends. Use the TTS duration; do not detect the end and then look.
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