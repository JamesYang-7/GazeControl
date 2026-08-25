# Scene-1 study clips — shortlist

Thirteen candidates kept from the top 25 of the sentence-clean search, for a visual pick later.
Five will become the study's clips (5 conversations × 3 methods = 15 videos per participant).

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
