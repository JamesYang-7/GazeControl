# Study 2 — the triadic prototypes played in a triad

Status: **design settled; scene, export and placement built, nothing baked** (2026-09-08). This is the
handoff for the implementation session. It supersedes `user-study-design.md`, which
describes the three-condition, fifteen-clip design of study 1; that study ran and is
written up in `{ICMI_ROOT}`, and nothing here undoes it.

**Revision, 2026-09-08.** The first version of this document settled on *retrieval*: the
participant's tracked gaze would select which prototype the agents play at each end-of-turn.
That was replaced by **baked, open-loop playback with the participant's gaze measured as an
outcome** once the prototypes themselves were checked (§2). The retrieval design is kept in
§2 as the rejected alternative, with the measurement that rejected it.

---

## 1. What changes, and why it is a different experiment

Study 1 played **triadic prototypes over a dyadic replay**. The prototypes are defined over
three conversational roles — current speaker, next speaker, listener — but the agents
reenacted a TalkingWithHands recording, which has two people in it. The participant watched
from a third-party position without being a party to the conversation being replayed, so
the prototypes' **listener column was played by nobody**: the frames in which an agent
looks at the listener pointed at someone who had no role in the recorded exchange.

Study 2 puts a person in that column. The participant stands in the seat of a real side
participant of a **3People-2022** conversation, the agents play the prototypes' current- and
next-speaker columns *at* that person where the prototype says so, and the participant's own
gaze is tracked and measured.

The corpus makes this tight. `Research/corpora/gaze_events/gaze_events_md6.csv` — the
corpus the prototypes were mined from — is 38 sessions over two campaigns, and its 2022
half is 21 sessions named `01-28-2022_Session_1`, `12-15-2021_Session_2` and so on: the
same seven dates, the same session numbering, built from the same `EoT_2022` tables the
segment finder reads. **The clips chosen for this study come from the corpus the vocabulary
came out of.** See §7 for what that does and does not license.

---

## 2. The decision: baked playback, gaze as an outcome

**The agents play a prototype drawn open-loop, exactly as in study 1, and the participant's
gaze is an outcome measure rather than an input.** Each event draws a prototype from the
pool matching its own class with the study-1 seeded draw, the track is baked, and every
participant sees the identical stimulus frame for frame. What is new is the seat: the
listener column now has an occupant, so the agents' listener-directed frames land on a real
person, and whether that person looks back — and how fast — is measured.

**Retrieval, considered and rejected (2026-09-08).** The first design had the participant's
gaze over a fixed lead coded into the prototypes' three-symbol alphabet and scored by DTW
against each candidate's listener column, the agents playing the best match. It is
attractive because the agents still play a literal, unmodified prototype and the selection
is explainable in one sentence. It was rejected because the only part of a retrieval a
participant could ever perceive is the gaze the agents direct at them, and the prototypes
barely contain any:

| prototype | listener column | agent frames aimed at the listener |
| --- | --- | --- |
| Fig6a | watches the current speaker | 10 (current speaker) |
| Fig6b | averts | 18 (current speaker) |
| Fig7c | averts | 13 (next speaker) |
| Fig8c | averts | 19 (current speaker) |
| the other eleven | mixed | 0 |

Only 4 of 15 prototypes look at the listener at all, **60 frames of 450**, the longest run
0.3 s. And the contingency runs the wrong way: a participant who *averts* retrieves the
three prototypes with the most gaze toward them, while one who watches the speaker gets at
most 167 ms. So retrieval cannot produce the effect a reader expects ("I looked, it looked
back"), and whatever it does produce is below what a participant can notice. An experiment
whose manipulation is invisible to its subjects has no hypothesis to state — and it would
have needed an open-loop control differing from it only by which near-indistinguishable
prototype plays, across seven events per condition, with reproducibility resting on a
decision log and replay harness that do not exist. Baking keeps the identical stimulus,
frame-for-frame reproducibility and the study-1 tooling, and the claim study 2 needs (§7)
is made by the seat, not by the selection.

**Also considered and rejected earlier the same day**, recorded so none is re-litigated:

- **Online re-matching** — re-score every decision tick and switch mid-window. Produces a
  track that is no prototype and needs hysteresis to stay coherent.
- **Role assignment from gaze** — let the participant's gaze decide which agent takes the
  next turn. The replay has already fixed who speaks next; it would contradict the audio.
- **Contingent completion** — branch on whether the participant returns an agent's gaze.
  The strongest behavioural manipulation, and the one a participant would actually feel;
  but the branch is not in the data (the corpus has the real listener's gaze and no
  counterfactual), so the fallback would be authored. **Deferred, not dropped** — it is the
  obvious study 3, and this study's gaze-return measurements are its pilot data.
- **Conditional sampling** from the corpus's transition and dwell statistics. Baseline A
  with an extra conditioning variable; throws away the interpretable-prototype claim.

---

## 3. The alphabet, measured

Each prototype is a `(K, 3)` kernel: one column per role, one value per frame at 60 fps,
K ∈ {20, 30, 40} (Figures 6, 7, 8 respectively). Values are `0` aversion, `1` at the
current speaker, `2` at the next speaker, `3` at the listener.

**No role ever gazes at itself.** Over all fifteen printed prototypes (450 frames each):

| column | values used | frame counts |
| --- | --- | --- |
| current speaker | aversion, next speaker, listener | 254 / 149 / 47 |
| next speaker | aversion, current speaker, listener | 265 / 172 / 13 |
| **listener** | **aversion, current speaker, next speaker** | **271 / 164 / 15** |

Two things follow for this study. The participant's tracked gaze codes into the same three
symbols the listener column uses — *averting*, *at the floor-holder*, *at the taker* — and
`GazeSphereTargeting` already resolves which agent is being looked at, so the participant's
track and the corpus listener's track are directly comparable (§7.2). And the agents direct
**60 of 900 frames** at the listener (47 + 13), all of them in the four prototypes tabled in
§2: that is the entire stimulus the seat adds, and it should be known before a pilot rather
than discovered in one.

The listener columns of the fifteen, for the comparison in §7.2:

| listener column | prototypes |
| --- | --- |
| steady at the current speaker | Fig6a, Fig6d, Fig6e, Fig8a, Fig8e |
| steady aversion | Fig6b, Fig7b, Fig7c |
| shifting | Fig6c, Fig7a, Fig7d, Fig7e, Fig8b, Fig8c, Fig8d |

---

## 4. Mechanism

Per clip, the study-1 pipeline in `Study2Scene` (a copy of TriadScene whose agents are placed by the clip):

1. **Seat.** The participant stays on study 1's fixed viewpoint; the recorded room is turned
   and slid (`RecordedRoomPlacement`, `RoomPlacement`) so the listener's seat — mean eye
   position over the window, opening yaw toward the two speakers' midpoint, measured at
   export — lands on that viewpoint and faces its forward axis. The listener's body is
   never instantiated. Rotation tracked, translation not, as in study 1.
2. **Roles.** The two takers are the agents: the event's floor-holder is the current
   speaker, its taker the next speaker, and the participant is the listener. Speaker codes
   are remapped at export onto study 1's contract (takers 1 and 2, listener 0); the true PC
   numbers stay on the agent records.
3. **Draw.** At each annotated event, one prototype from the pool of its own class, by the
   seeded `FNV-1a(BaseSeed, eventIndex)` draw; the mapping goes into the log's sidecar
   before the first boundary. Whether the pool should be class-matched or all fifteen is
   still the §6 question it was, and it now only affects which prototype plays, not what the
   condition means.
4. **Play** the winner's current- and next-speaker columns on the two agents, positioned in
   the pre-turn window by `subseq_start_idx`, over the holding-replay substrate — `Proposed`
   as it stands. The listener column is not played by anyone; it is what the participant is
   compared against.
5. **Bake** the track once per clip and condition, verify it against the loaded segment at
   arm time, as now.
6. **Measure.** `ParticipantGazeLogger` on the decision grid, unchanged, plus the §7
   derived measures — gaze-return latency above all, which needs `agents_looking_at_user` and
   the per-tick resolved target and nothing else.

Nothing here reads the participant's gaze during the take.

---

## 5. What already exists

Reusable unchanged, and now including the baking path:

- **The XR rig and the fixed viewpoint** (`XrParticipantRig`, `ViewRecentring`), and
  `ThreePartyViewpoint`, which measures the listener's seat off each clip.
- **`GazeTrackBaker` + `BakedGazePolicy`** and the track guard — the reproducibility
  argument of study 1 carries over whole.
- **`ProposedGazePolicy`** and the holding-replay substrate; the class-matched seeded draw.
- **`VarjoGazeSource` + `ParticipantGazeLogger`** — built, tested, and **never yet fed by
  real hardware**. Making them produce a real gaze target on the decision grid is the first
  implementation milestone and the one with unknown risk; in this design it gates the
  outcome measure rather than the stimulus, so a failure degrades the study to ratings
  instead of breaking it.
- **`GazeSphereTargeting`** resolves which agent the participant is looking at — the coder
  for the §7.2 comparison.
- **`GazeControl → 3People → Set Up Replay Scene` / `Session Browser`**, the seven chosen
  clips, and `ThreePartyReplay`'s one-clock playback.
- **`GazeController`**, unchanged — the shared animation layer, or the study measures
  animation instead of gaze policy.
- The questionnaire machinery and `StudySessionRunner`'s in-place re-arming.

**Built 2026-09-08, the same day** — the join between the 3People replay and the study
pipeline, on the principle that study 2 replaces study 1's corpus and nothing else:

- `Tools/export_3people_segments.py` writes the seven windows as `study2_c1`…`c7` under
  `Assets/DemoSegments/`, remapping the takers onto speaker codes 1 and 2 and the listener
  onto 0, with the listener's seat (mean eye position, opening yaw) computed by forward
  kinematics and written in Unity's frame.
- `Assets/Scenes/Study2Scene.unity` (`GazeControl → Study 2 → Set Up Scene`) is TriadScene
  with the agents under a `Room` transform that `RecordedRoomPlacement` turns and slides
  per clip so the seat lands on the User vertex and faces its forward axis
  (`RoomPlacement`). The participant never moves; the room does. Everything else in the
  scene — runner, conditions, baker, questionnaire, XR rig — is study 1's.
- The pool is class-matched (user's decision).

Still open: seeds (all 1, unscanned), the bakes, the questionnaire instrument's wording,
and the §7 derived measures.

---

## 6. Open questions the implementation must settle

- **Pool: class-matched or all fifteen.** Class-matched is the study-1 rule and needs no
  argument; the ICMI result that gaze does not separate EoT types is the case for pooling.
- **Comparison conditions.** Baseline B (voice-following, never averts, and it *does* look
  at whoever speaks) and baseline A can both return, since the seat changes nothing about
  how they run. Whether to keep all three or run Proposed against one of them is open.
- **Seven clips carry one event each**, so a prototype fires seven times per condition, and
  only four prototypes ever look at the participant. Either admit multi-event windows from
  the finder, take more clips, or accept it and lean on the ratings for the condition
  effect while the behavioural measure serves study 3.
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
   participant stood, with that person's own gaze on record. The participant's coded track
   against the prototype's listener column is the same comparison the corpus listener would
   get, which is the cleanest statement available of "the prototype applies here".
3. **Triads are where gaze is directional** (the paper's own §2). An agent that can address
   the participant is the only configuration in which that claim is testable behaviourally.
4. **From judgement to behaviour.** Study 1 asked people to rate. This one can measure
   whether they look back, and how fast. Ratings carry demand characteristics; gaze-return
   latency does not.
5. **The manipulation is over targets, not angles**, so the unscaled default body — which
   flattens roughly 7-9° of gaze elevation — cannot confound it.

**Threats to name rather than hide:**

- **Replay is not interaction.** The turn is offered and cannot be taken; the recorded audio
  continues regardless. Scope the claim to uptake *signals*.
- **The listener-directed stimulus is small** (§3): 60 frames across four prototypes. Say
  so, and treat the gaze-return result as a pilot for the contingent study rather than the
  headline.
- **In-sample clips.** The study clips are drawn from sessions that contributed to the
  prototype mining. That is either "the prototypes demonstrably apply here" or "in-sample",
  depending on framing, and a reviewer may choose the second. Cheap mitigation: check
  whether any of the seven windows' events are among the 60 matched samples in
  `manifest.csv` — almost certainly not, 60 samples out of 28,318 — and say so.
- **Coherence risk.** An agent may offer the floor to a participant whose response the
  recorded audio then contradicts. Smaller under baked playback than under retrieval, since
  the offers are the corpus's own and rare, but it should be faced in the briefing.

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
