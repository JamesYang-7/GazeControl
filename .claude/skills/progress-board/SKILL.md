---
name: progress-board
description: Maintain the GazeControl working-memory progress board. Use whenever a task starts/finishes, a decision is made, a new task or open question appears, or before any git commit — update PROGRESS.md (the source of truth) and let the dashboard regenerate. Keeps PROGRESS.md and progress.html in sync.
---

# Progress board

Working memory for the GazeControl project. Two artifacts at the repo root:

- **`PROGRESS.md`** — the **source of truth**. Hierarchical checkbox task list,
  a Decisions log, and parked open questions. Hand-edited (by you).
- **`progress.html`** — a curated dashboard auto-generated from `PROGRESS.md`.
  **Never hand-edit it.** It is regenerated and staged by the pre-commit hook.

## When to use

Update `PROGRESS.md` as work happens, and always give it a pass **before committing**:
- A task starts → set it to `[~]`. Finishes → `[x]` with a `_(done YYYY-MM-DD · <short-commit>)_` tag.
  The commit hash is the commit that finished the task — record it for easy history
  tracking. Since the hash isn't known until *after* committing, mark the task done in
  the finishing commit, then add the hash on the next board update (or amend it in).
- A new task/subtask or open question emerges → add it (keep far-future tasks coarse).
- A decision is made → add a dated bullet under `## Decisions`.

## PROGRESS.md format

- Tasks are nested checkboxes; **2 spaces per level**. Each task:
  `- [x] **Title** — short description`
- Status markers: `[ ]` todo · `[~]` in progress · `[x]` done · `[!]` blocked.
- Completed tasks carry `_(done YYYY-MM-DD · <short-commit>)_`. The date drives
  "recently completed" ordering; the short commit hash (7+ hex) renders as a chip.
- Plain bullets (no `[ ]`) nested under a task are **notes / open questions**.
- `## Decisions` — bullets `- YYYY-MM-DD — decision`. `## Parked / open questions` — free bullets.

Keep it lean: don't pre-detail tasks that are several steps away — record the
title and any open questions, leave specifics for when the task is next.

## Regenerating the dashboard

The pre-commit hook does this automatically. To preview manually:

```sh
python .claude/skills/progress-board/render_progress.py PROGRESS.md progress.html
```

The renderer shows: **In progress**, **Upcoming**, **Blocked** (only if any),
**Recently completed** (full detail for the most recent 3, older ones compacted
into a collapsible list), and **Decisions** (most recent few, older collapsed).
Output is deterministic from `PROGRESS.md`, so an unchanged board yields identical
bytes. Open `progress.html` in any browser.

## Commit sync (how the automation works)

A tracked git hook at `.githooks/pre-commit` runs the renderer and `git add`s
`progress.html` on every commit, so the dashboard never drifts from the board.

**One-time setup per clone** (the hooksPath is local config, not committed):

```sh
git config core.hooksPath .githooks
```

Workflow each commit: update `PROGRESS.md` → `git add` your changes → commit.
The hook regenerates and stages `progress.html` as part of that same commit.
