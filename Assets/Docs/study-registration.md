# Study registration — scene-1 gaze policy comparison

**Fixed 2026-08-31, before the first analysed participant (P05) ran. Committed 2026-09-01, by which time P05-P07 had run.** Nothing below has changed since it was written; the commit is later than the writing, and later than three sessions, so treat the commit as the point from which it is tamper-evident rather than as the moment it was decided.

This document fixes the design, hypotheses, measures and analysis of the scene-1 user study ahead
of data collection. It is timestamped by its commit in this repository and **is not lodged with an
external registry** (user's call, 2026-08-31). That limits what may be claimed: the paper says the
analysis was *fixed in advance and recorded in the project repository*, and must **not** say
"pre-registered", which readers take to mean a third-party registry with a citable identifier. If
one is added later, record its identifier here and the claim may be upgraded.

Companion documents: `user-study-design.md` (method in full), `baseline-spec.md` (the three
conditions).

---

## 1. What has already been seen, and what has not

Stated first because it is what makes the rest of this document meaningful.

**Seen.** Four pilot participants (P01-P04) completed the full protocol on 2026-08-27/28. Their
questionnaire responses have been inspected in full: condition means on all four items,
per-participant mean ranks, the serial-position check, and paired effect sizes. The power
calculation in §6 is derived from them.

**Excluded.** P01-P04 are pilots and enter no reported analysis (`user-study-design.md` §0.1). The
ground is that they ran the superseded single fixed method order of 2026-08-27, decided before any
further participant runs, and independent of how they answered.

**Partly seen, and precisely this much.** P05, P06 and P07 ran on 2026-08-31, after this document
was written. Their **presentation order** has been read — block, version position and condition per
trial — because that is what verifies the counterbalancing schedule was actually in force, and it is
independent of anything they answered. Their **responses have not been looked at**: no rating, no
ranking, no free text from P05 onward has been read or summarised. Nothing in this document is
conditioned on the analysed sample's data.

---

## 2. Sample

- **N = 18 analysed participants, P05 through P22.** A multiple of six, which is what makes the
  counterbalancing marginals close exactly (§3).
- **Stopping rule.** Data collection stops at P22. It does not stop early on an interim result, and
  no interim analysis is planned. If a participant is replaced under §2.1 the replacement takes the
  next label, and collection stops when 18 analysable sessions exist.
- **Inclusion.** Sufficient English listening comprehension to follow conversational speech; the
  stimulus audio is conversational English, so this is an inclusion criterion, not a covariate.
- **Pilots** P01-P04 are not counted toward N.

### 2.1 Exclusion rules, fixed in advance

A session is excluded and replaced if any of the following holds. Each is checkable without
reference to the responses.

1. Fewer than 15 completed clips, or any clip cut short (`ClipPauseController.WasCutShort`), which
   already disables recording for that clip's screen.
2. A missing, truncated or misnamed log that cannot be placed by its schedule position.
3. Withdrawal, equipment failure, or discomfort that stops the session.
4. Failure of the inclusion criterion above, discovered during the session.

No exclusion may be made on the basis of a participant's ratings, rankings or free text.

---

## 3. Design

Within-subjects, fully crossed: **5 conversations × 3 methods = 15 clips**, blocked by conversation,
with the three versions of one conversation played back to back and ranked at the end of the block.

- **Method order is counterbalanced per participant** from a schedule fixed in advance: block *b* of
  participant ordinal *i* takes permutation `(i + b) mod 6` (`StudySequence`, `ParticipantLabel.ScheduleOrdinal`;
  P05 → ordinal 0). Within a participant the five blocks take five different permutations, so each
  method sits at each serial position once or twice. Across any six consecutive participants every
  block sees all six permutations, so **at N = 18 each method occupies each serial position exactly
  30 times**. Asserted in `StudySequenceTest`.
- **Conversation order is identical for every participant** and deliberately not randomised: it
  cannot enter the within-block contrast between methods, and a fixed order keeps every trial
  identifiable by the position it occupied.
- **Conditions**: Proposed (prototypes over a holding-replay substrate), Baseline A
  (role-conditioned, Shintani et al. 2024 refitted to our corpus), Baseline B (voice-activity
  speaker-following). One shared animation layer, eyes only, no per-condition tuning.
- **Seeds** for the two stochastic conditions derive from participant × conversation, so
  participants sample each condition rather than judging one draw of it. Every take's seed is
  recorded in its sidecar.

---

## 4. Hypotheses

Directional, and tested as stated.

| | Hypothesis | Prediction |
|---|---|---|
| H<sub>nat</sub> | Gaze naturalness (N1) | Proposed > A, Proposed > B |
| H<sub>bnd</sub> | Boundary gaze quality (T2) | Proposed > A, Proposed > B |
| H<sub>eng</sub> | Mutual engagement (A1) | Proposed ≈ B, both > A |
| H<sub>inc</sub> | Inclusion / being noticed (I1) | Proposed ≫ B |
| H<sub>rank</sub> | Ranked naturalness (R1, **primary**) | Proposed ranked above A and B |

**H<sub>eng</sub> predicts a null between Proposed and B and is registered as such.** It is a
discriminant check on the instrument: a method that wins every item invites a halo or demand
explanation, whereas a method that wins on N1, T2 and I1 while flat on A1 shows a pattern demand
characteristics cannot produce. A Proposed > B result on A1 counts against the instrument, not for
the method.

**H<sub>inc</sub> carries a margin known before the study.** Baseline B directs 97-98% of its gaze
at the other agent and none at the human; the Proposed condition's holding substrate gives the human
4-50% of its out-of-window gaze.

No hypothesis is stated over the participant's own gaze; participant eye tracking is out of scope
(`user-study-design.md` §7).

---

## 5. Measures

**Primary.** R1, the per-block forced ranking of the three versions by naturalness of eye movement,
no ties.

**Secondary.** Four 7-point Likert items per clip — N1, T2, A1, I1 — as given in
`Assets/GazeControl/Resources/Questionnaire.json`. Free text D1 per block.

**Post-study.** Manipulation check, demand check, single simulator-sickness item, demographics.

**Stimulus-side objective measure.** Distributional fidelity of each policy's generated gaze against
the human corpus `Research/corpora/gaze_events/gaze_events_md6.csv`: role-conditioned target
proportions, dwell distributions, and shift timing relative to annotated EoT events, computed over
the baked tracks. This involves no participant-level inference and is descriptive of the stimulus.

---

## 6. Analysis

Fixed here in full. Any departure is reported as exploratory.

**Rankings (primary).** Average each participant's five rankings per method into one mean rank;
Friedman test across the three methods; Kendall's *W* as effect size; pairwise Wilcoxon signed-rank
tests under Holm correction. A cumulative-link mixed model over trial-level rankings, with random
intercepts for participant and conversation, is reported as a secondary analysis.

**Likert items.** One linear mixed model per item, method as a fixed effect, random intercepts for
participant and conversation, Holm correction across the four items, estimated marginal means with
confidence intervals.

**Serial position** is included as a fixed effect in every model and reported, so an order effect is
measured rather than assumed away.

**Unit of analysis.** No test treats the 15 trials, the trial-level rankings, or individual gaze
samples as independent observations.

**Free text** is coded thematically after the numeric analysis, with version position joined to
condition, and reported as qualitative.

### 6.1 Sensitivity, and what N = 18 cannot resolve

At N = 18, a paired test at α = .05 two-sided has 80% power for **d<sub>z</sub> ≥ 0.69**. Measured
on the four pilots (upward-biased at that N, so optimistic):

| contrast | pilot *d<sub>z</sub>* | resolvable at N = 18 |
|---|---|---|
| N1 Proposed vs A | 2.60 | yes |
| I1 Proposed vs B | 1.08 | yes |
| I1 Proposed vs A | 0.92 | yes |
| T2 Proposed vs A | 0.80 | yes |
| **T2 Proposed vs B** | 0.53 | **no** |
| **N1 Proposed vs B** | 0.51 | **no** |

**Registered in advance: the study is not powered for Proposed vs Baseline B on N1 and T2.** The
gaps there are not small — +0.85 and +1.10 scale points — but one pilot in four preferred B strongly
on those items, and that between-participant heterogeneity is what the paired SD reflects. Those two
contrasts will be reported with effect sizes and confidence intervals and **interpreted as
inconclusive if they do not reach significance**, never as evidence of no difference. The
Proposed-vs-B case rests on the ranking and on I1.

---

## 7. Amendments

Any change after this commit is appended below with its date and reason, and the git history is the
record of when each was made. Nothing above is edited in place.

_(none yet)_
