# Scene-1 study clips — shortlist and final pick

Thirteen candidates kept from the top 25 of the sentence-clean search, narrowed to the five the
study uses (5 conversations × 3 methods = 15 videos per participant).

---

## The five, decided 2026-08-26

| clip | stem | window | len | cast | voices | EoT events | texture artifact |
|---|---|---|---|---|---|---|---|
| 1 | `trn_2023_v0_036` | 324.33–346.25 | 21.9 s | S2 + S3 | 2×M 134/113 | t, t | agent B (SMPLitex) |
| 2 | `trn_2023_v0_040` | 280.73–304.17 | 23.4 s | **S1** + S4 | mixed F220/M110 | i, t | none |
| 3 | `trn_2023_v0_081` | 13.13–42.73 | 29.6 s | S5 + **S1** | mixed M130/F259 | **o**, t | none |
| 4 | `trn_2023_v0_119` | 256.43–285.75 | 29.3 s | **S1** + S6 | 2×F 220/210 | i, t | agent B (SMPLitex) |
| 5 | `trn_2023_v0_120` | 66.33–89.97 | 23.6 s | **S1** + S7 | mixed F210/M116 | t, i, i, t | none |

Twelve EoT events over the five. **All three classes are covered, and `081` is the only clip that
covers overlapping** — it is mandatory, not preferred. The events also sit in different places:
an interruption opens `040` and `119`, two of them fall mid-clip in `120`, `081` opens on its
overlap, and `036` is pure turn-taking — so the prototype pool is exercised at several positions
rather than one, and one clip serves as a plain turn-taking control. Three of five are mixed-voice,
the only configuration where neither agent carries the SMPLitex face misregistration.

**Why this set and not another: only five distinct casts exist in the shortlist.** `008`, `050` and
`119` are the same two women; `026` and `036` are the same two men (see the speaker map below). So
one clip from each cast is the entire degree of freedom, and four of the five were forced once
`081` was required for its overlapping event. Speaker **S1 appears in four of the five** and cannot
be avoided — she is in six of the eight clips that survived the audio check.

Rejected at the last step:

- **`038`, `055`, `060`, `065` — broken audio** (found by listening, 2026-08-25). `055` was the best
  overlapping candidate, which is what makes `081` mandatory rather than merely preferred.
- **`026` over `036`** — same cast, so at most one could be used. `026` reads more fluently, but its
  audio is slightly broken too, and `036`'s 5.7 s near-silent stretch was taken as a *positive*:
  it varies what the holding substrate has to fill, which is a quarter of that clip.
- **`008`, `050`** — same cast as `119`, and `119` was preferred on its own reading.
- **`014`** — overlapping, but never shortlisted for the visual pick.

`081`'s exchange is a monologue with backchannels (balance 0.57) and its content is a tsunami
story. It is in the set on structural grounds: nothing else in the shortlist carries an
overlapping event. Worth a second look during the pilot.

**Note on `081`'s textures**: its agent A is the *male* speaker and agent B the female, the reverse
of `user-study-design.md` §3's mixed-voice row. The rule is per-agent — each agent takes the stock
albedo matching its own measured voice — and the Clip Browser applies it that way already.

## Speaker map

The corpus identifies nobody, so it was measured: `Tools/identify_speakers.py` embeds each side
with ECAPA-TDNN and calibrates the threshold against a same-speaker control (two halves of one
recording). Over the eight clips that survived the audio check: same-speaker control 0.837–0.973,
cross-clip median 0.130 — two populations with a wide gap between them.

| speaker | appears as | in the final five |
|---|---|---|
| **S1** F ~215 Hz | 008A, 040A, 050B, **081B**, 119A, 120A | four clips |
| S2 M ~132 Hz | 026A, **036A** | one |
| S3 M ~100 Hz | 026B, **036B** | one |
| S4 M 110 Hz | **040B** | one |
| S5 M 130 Hz | **081A** | one |
| S6 F ~205 Hz | 008B, 050A, **119B** | one |
| S7 M 116 Hz | **120B** | one |

Seven speakers over five conversations, which is the most this shortlist allows.

## Export

```sh
python Tools/find_demo_segments.py --stem trn_2023_v0_036 --per-stem 1 --export 1 --name study_c1
python Tools/find_demo_segments.py --stem trn_2023_v0_040 --per-stem 1 --export 1 --name study_c2
python Tools/find_demo_segments.py --stem trn_2023_v0_081 --per-stem 1 --require-type overlapping --export 1 --name study_c3
python Tools/find_demo_segments.py --stem trn_2023_v0_119 --per-stem 1 --export 1 --name study_c4
python Tools/find_demo_segments.py --stem trn_2023_v0_120 --per-stem 1 --export 1 --name study_c5
```

---

## The shortlist it was chosen from

Tracked because it is a decision record: `user-study-design.md` picks five clips from this set,
and the search that produced it is re-runnable but the reading behind the shortlist is not.
Full transcripts of all 25 ranked candidates live in `Research/notes/scene1-candidates.md`,
which is local-only (`Research/` is git-ignored) and regenerable with the commands below.

**Selection context.** Cross-block comparability does *not* matter — the participant ranks the
three methods *within* one block, so conversation variance is absorbed entirely and never enters
the condition contrast. Duration spread and event-count spread are therefore free. **Event-type
diversity is a positive**, because the Proposed condition draws each boundary's prototype from the
pool matching that event's class, so a varied set exercises more of the paper's fifteen prototypes.

## How to address a candidate

Global ranks shift whenever the search changes, so use the stem instead — it resolves each
candidate to rank 1 deterministically:

```sh
python Tools/find_demo_segments.py --stem <stem> --per-stem 1 --inspect 1      # read it
python Tools/find_demo_segments.py --stem <stem> --per-stem 1 --export 1 --name <case>
```

For the three overlapping candidates, add `--require-type overlapping` to both commands.

## Main candidates (10)

| # | Stem | Window | Len | Voices | Events | Note |
|---|---|---|---|---|---|---|
| 1 | `trn_2023_v0_038` | 102.13–127.25 | 25.1 s | 2×F 232/220 | t, t | Flight delays and stopovers. Clean, balanced, two well-separated hand-overs. |
| 2 | `trn_2023_v0_036` | 324.33–346.25 | 21.9 s | 2×M 134/113 | t, t | Streaming services replacing the classics. Coherent throughout. |
| 3 | `trn_2023_v0_008` | 234.53–258.07 | 23.5 s | 2×F 210/200 | t, t | A tame social scene; kid falling off a roof. |
| 4 | `trn_2023_v0_040` | 280.73–304.17 | 23.4 s | mixed 220/110 | i, t | "Why are there people like that" — trusting people. |
| 5 | `trn_2023_v0_119` | 256.43–285.75 | 29.3 s | 2×F 220/210 | i, t | Touch ID and identifying trauma patients. Longest of the main set. |
| 6 | `trn_2023_v0_050` | 220.03–243.27 | 23.2 s | 2×F 200/220 | t, i, t | Dorm rooms and roommates. **Best mixed-event candidate** — fluent throughout, ends on a clean answer. |
| 7 | `trn_2023_v0_060` | 176.93–198.35 | 21.4 s | mixed 110/245 | i, t, t | Cable, Stranger Things, Catan. Real overlap at 2.5 s. |
| 8 | `trn_2023_v0_120` | 66.33–89.97 | 23.6 s | mixed 210/116 | t, i, i, t | Recounting a film's plot. **Widest event mix** of any candidate. Slightly halting. |
| 9 | `trn_2023_v0_065` | 23.23–49.55 | 26.3 s | mixed 113/232 | t, t, t, i | Getting-to-know-you. Four events, fast rhythm. One `###` late. |
| 10 | `trn_2023_v0_026` | 74.53–103.52 | 29.0 s | 2×M 130/88 | t, i | Interstellar and relativity. Very fluent; ends on a real interruption. |

## Overlapping-event candidates (3)

Add `--require-type overlapping`. **Only 9 windows in the entire 186-conversation corpus contain an
overlapping event** — it is by far the rarest of the three classes, and most of the nine are too
disfluent to use. These are the three usable ones.

| # | Stem | Window | Len | Voices | Events | Note |
|---|---|---|---|---|---|---|
| O1 | `trn_2023_v0_055` | 111.43–135.98 | 24.6 s | 2×F 200/245 | **o**, i | Leaving a night out early. The most fluent of the overlapping set; one `###`. |
| O2 | `trn_2023_v0_081` | 13.13–42.73 | 29.6 s | mixed 130/259 | **o**, t | A tsunami story. B narrates fluently, A backchannels — balance 0.57, so it reads as monologue-plus-backchannel rather than an exchange. |
| O3 | `trn_2023_v0_014` | 74.22–102.07 | 27.9 s | F 232 / unclear 163 | **o**, **o**, i, t | Books and spoilers. Five events, the busiest window here. B's pitch does not classify cleanly, so check the voices by ear before assigning textures. |

## Texture assignment

Per `Assets/Docs/user-study-design.md` §3 — there is one stock albedo per sex, so:

- **mixed** → agent A stock female, agent B stock male; **neither agent shows the SMPLitex face
  artifact**. Candidates 4, 7, 8, 9, O2.
- **2×F** → A stock female, B SMPLitex `smplitex_f00021_alb.png`; artifact on B.
- **2×M** → A stock male, B SMPLitex `smplitex_agentB_alb.png`; artifact on B.

## Notes for the pick

- Candidates 1, 2, 3 are all-turn-taking and make a clean control group.
- Candidate 6 is the strongest all-round mixed-event clip; 8 has the widest class mix.
- Covering all three EoT classes needs one of O1–O3, since nothing in the main ten has an
  overlapping event.
- O3's speaker B measures 163 Hz, which falls between the male and female bands — verify by ear
  before choosing its textures.
