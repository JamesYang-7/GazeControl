# Study 2 — prototype retrieval driven by the participant's gaze

Status: **design settled, nothing built** (2026-09-08). This is the handoff for the
implementation session. It supersedes `user-study-design.md`, which describes the
three-condition, fifteen-clip design of study 1; that study ran and is written up in
`{ICMI_ROOT}`, and nothing here undoes it.

---

## 1. What changes, and why it is a different experiment

Study 1 played **triadic prototypes over a dyadic replay**. The prototypes are defined over
three conversational roles — current speaker, next speaker, listener — but the agents
reenacted a TalkingWithHands recording, which has two people in it. The participant watched
from a third-party position without being a party to the conversation being replayed, so
the prototypes' **listener column was played by nobody**: the frames in which an agent
looks at the listener pointed at someone who had no role in the recorded exchange.

Study 2 puts a person in that column. The participant stands in the seat of a real side
participant of a **3People-2022** conversation, their gaze is tracked, and the agents'
gaze at each end-of-turn is selected at run time from what the participant is doing.

The corpus makes this tight. `Research/corpora/gaze_events/gaze_events_md6.csv` — the
corpus the prototypes were mined from — is 38 sessions over two campaigns, and its 2022
half is 21 sessions named `01-28-2022_Session_1`, `12-15-2021_Session_2` and so on: the
same seven dates, the same session numbering, built from the same `EoT_2022` tables the
segment finder reads. **The clips chosen for this study come from the corpus the vocabulary
came out of.** See §7 for what that does and does not license.

---

## 2. The decision: retrieval

**The participant's observed gaze selects which prototype the agents play.** At each
annotated end-of-turn event, the participant's own gaze track is scored against the
listener column of each candidate prototype, and the agents play the speaker and
next-speaker columns of the best match.

The agents therefore always play a **literal, unmodified prototype**; nothing is
interpolated or synthesised. The only thing the participant's gaze does is choose which
one — which is a retrieval in exactly the space the prototypes live in, and which stays
explainable in one sentence: *we played the pattern whose side participant was behaving
like you.*

**What was considered and rejected** (2026-09-08), recorded so it is not re-litigated:

- **Online re-matching** — re-score every decision tick and switch mid-window. Switching
  produces a track that is no prototype, needs hysteresis to stay coherent, and gives up
  the explainability the whole approach rests on.
- **Role assignment from gaze** — let the participant's gaze decide which agent occupies
  the next-speaker role. Attractive, because resolving next-speaker selection is exactly
  what gaze does in a triad, but the replay has already fixed who speaks next: it would
  contradict the audio.
- **Contingent completion** — branch on whether the participant returns an agent's gaze.
  The strongest behavioural manipulation, but the branch is not in the data (the corpus has
  the real listener's gaze and no counterfactual), so the fallback would be authored.
  **Deferred, not dropped** — it composes with retrieval and is the obvious study 3.
- **Conditional sampling** from the corpus's transition and dwell statistics instead of the
  prototypes. Naturally online, but it is baseline A with an extra conditioning variable
  and throws away the interpretable-prototype claim.

---

## 3. The alphabet, measured

Each prototype is a `(K, 3)` kernel: one column per role, one value per frame at 60 fps,
K ∈ {20, 30, 40} (Figures 6, 7, 8 respectively). Values are `0` aversion, `1` at the
current speaker, `2` at the next speaker, `3` at the listener.

**No role ever gazes at itself**, which is what makes the participant able to play the
listener column at all. Over all fifteen printed prototypes (450 frames each):

| column | values used | frame counts |
| --- | --- | --- |
| current speaker | aversion, next speaker, listener | 254 / 149 / 47 |
| next speaker | aversion, current speaker, listener | 265 / 172 / 13 |
| **listener** | **aversion, current speaker, next speaker** | **271 / 164 / 15** |

So the participant's track only has to be coded into three symbols — *averting*, *at the
agent holding the floor*, *at the agent about to take it* — and `GazeSphereTargeting`
already resolves which agent is being looked at.

The listener columns are discriminative rather than all alike, which is what makes
retrieval worth doing:

| listener column | prototypes |
| --- | --- |
| steady at the current speaker | Fig6a, Fig6d, Fig6e, Fig8a, Fig8e |
| steady aversion | Fig6b, Fig7b, Fig7c |
| shifting | Fig6c, Fig7a, Fig7d, Fig7e, Fig8b, Fig8c, Fig8d |

A participant who watches the speaker throughout retrieves a different pattern from one who
looks away, and that difference is legible to a reader without any model.

---

## 4. Mechanism

At each annotated event in a clip:

1. **Candidate pool.** Open: the prototypes of the event's own class (the study-1 rule), or
   all fifteen. Retrieval may make class-matching redundant — the ICMI classification result
   was that gaze separates EoT from non-EoT but *not* the EoT types from each other, which
   is an argument for pooling all fifteen and letting the participant's track do the
   selecting. Decide this before writing the policy; it changes what the condition means.
2. **Observation.** The participant's gaze targets over a fixed lead ending at the decision
   instant, coded into `{aversion, current speaker, next speaker}` on the 60 Hz decision
   grid.
3. **Score.** DTW over the categorical sequence, against each candidate's listener column.
   DTW because it is what the prototypes were mined with (`dtw_dist` is in the archives'
   `manifest.csv`) and because it is length-agnostic, which matters — the observation is a
   fixed lead and the columns are 20, 30 or 40 frames.
4. **Commit** before the pattern's first frame, and do not revisit it inside the window.
5. **Play** the winner's speaker and next-speaker columns on the two agents, positioned in
   the pre-turn window by its `subseq_start_idx`, exactly as `ProposedGazePolicy` does now.

**The matching window is the one real design question.** Three resolutions, in increasing
order of principle and of cost:

- **(c) Match the observed lead directly against the listener column.** Implementable today
  from `GazePatterns.g.cs` alone. Comparing a fixed-length observation against a shorter
  column is what DTW absorbs. **Start here.**
- **(b) Match the prefix that precedes the pattern**, against the corresponding stretch of
  each prototype's source windows.
- **(a) Match against the prototype's full 1 s pre-turn window.** The principled version:
  the prototypes were mined from 60-frame windows and each archive carries
  `sample_index_global` for its four matched samples, so a representative full-window
  listener track can be recovered per prototype. Needs the `{GAZETURN}` dataset ordering to
  resolve those indices — see §7.

Neither (a) nor (b) changes the policy's shape, only the vector it matches against, so
starting at (c) does not paint anything into a corner.

---

## 5. What already exists

Reusable unchanged:

- **The XR rig and the fixed viewpoint** (`XrParticipantRig`, `ViewRecentring`), and now
  `ThreePartyViewpoint`, which measures the listener's seat off each clip.
- **`VarjoGazeSource` + `ParticipantGazeLogger`** — built, tested, and **never yet fed by
  real hardware**. Making them produce a real gaze target on the decision grid is the first
  implementation milestone and the one with unknown risk.
- **`GazeSphereTargeting`** resolves which agent the participant is looking at; it is the
  coder for step 2 above.
- **`GazeControl → 3People → Set Up Replay Scene` / `Session Browser`**, the seven chosen
  clips, and `ThreePartyReplay`'s one-clock playback.
- **`GazeController`**, unchanged — the shared animation layer, or the study measures
  animation instead of gaze policy.
- The questionnaire machinery and `StudySessionRunner`'s in-place re-arming.

**What does not transfer: `GazeTrackBaker` and `BakedGazePolicy`.** A policy that reads
live gaze cannot be baked, so the frame-for-frame reproducibility that study 1 rested on is
gone. The replacement is decision logging plus offline replay (the §7.6 replay harness),
and it should be built *with* the policy rather than after it — a take whose decisions were
not logged is not analysable.

---

## 6. Open questions the implementation must settle

- **Pool: class-matched or all fifteen** (§4.1). Decide first; it defines the condition.
- **Lead length** for the observation, and the **commit instant** relative to the pattern.
- **Comparison conditions.** The natural control is *the same prototypes played open-loop*,
  drawn by class as in study 1 — that isolates the retrieval, rather than re-running
  "data-driven beats heuristic". Whether baselines A and B return at all is open.
- **Seven clips carry one event each**, so a retrieval fires seven times per condition.
  That is thin for a within-subjects behavioural measure. Either admit multi-event windows
  from the finder, take more clips, or accept it and lean on the ratings.
- **What the participant is told.** They are now a party to the conversation rather than an
  observer, and being told they can speak — when the replay cannot answer — would be a
  false affordance.

---

## 7. For the paper

The argument, in the order a reviewer will want it:

1. **The vocabulary is triadic; study 1 tested it dyadically.** Say it first. The
   generalisation from role-coded prototype to agent gaze was only half-tested, because the
   listener column had no occupant.
2. **Study 2 returns the prototypes to their own corpus** — same recording campaign, same
   room geometry, same annotated boundaries, and the participant stands where a real side
   participant stood, with that person's own gaze on record as a reference for what a side
   participant did there.
3. **Triads are where gaze is directional** (the paper's own §2). An agent that can address
   the participant is the only configuration in which that claim is testable behaviourally.
4. **From judgement to behaviour.** Study 1 asked people to rate. This one can measure
   whether they look back, and how fast. Ratings carry demand characteristics; gaze-return
   latency does not.
5. **The manipulation is over targets, not angles**, so the unscaled default body — which
   flattens roughly 7-9° of gaze elevation — cannot confound it.

**Threats to name rather than hide:**

- **Replay is not interaction.** The turn is offered and cannot be taken; the recorded audio
  continues regardless. Either scope the claim to uptake *signals*, or build a real turn
  slot.
- **In-sample clips.** The study clips are drawn from sessions that contributed to the
  prototype mining. That is either "the prototypes demonstrably apply here" or "in-sample",
  depending on framing, and a reviewer may choose the second. Cheap mitigation: check
  whether any of the seven windows' events are among the 60 matched samples in
  `manifest.csv` — almost certainly not, 60 samples out of 28,318 — and say so.
- **Reactive policies are not reproducible** without decision logs (§5).
- **Coherence risk.** An agent may offer the floor to a participant whose response the
  recorded audio then contradicts. This is the largest design risk in the whole study and
  should be faced explicitly, not discovered in a pilot.

**Provenance, and what is still unverified.** The prototype archives moved to `{ICMI_ROOT}`
= `F:\Research\ICMI_2026___Explainable_Gaze_Patterns_for_Turn_Taking\raw_prototypes`
(`Tools/build_gaze_patterns.py` points there). Their `manifest.csv` indexes 28,318 mined
samples, and the sixty matched samples behind the fifteen printed prototypes span
**1,081-27,596** — the whole range. So the printed prototypes are **pooled across both
campaigns**, not mined from the 2022 sessions alone; the claim in §7.2 is corpus-level, and
should be written that way rather than as "these conversations produced these patterns".
Resolving individual sample indices to sessions needs the `{GAZETURN}` dataset ordering
(`F:\Code\scnpro\prototype\prototype`) and is worth doing once, because it is what decides
how strongly §7.2 can be phrased. Also to reconcile: `main.tex` says nine conversational
groups, `gaze_events_md6.csv` has eleven.
