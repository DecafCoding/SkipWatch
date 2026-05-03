# PRD Progress Tracker

This file is the source of truth for the autonomous routine. The routine reads it
on every run, finds the next uncompleted task, executes it, and updates this file
in the same commit. Keep it accurate — if it drifts from reality, the agent drifts.

This file is **generated** from the phase docs in `docs/phases/` and the phase list
in `docs/prd.md` by `/create-progress`. Phase docs are the source of truth for
slugs, branch names, dependencies, and tasks for *planned* phases; the PRD is the
source of truth for the full set of phases that exist (planned or not). To change
any of those, edit the underlying source and re-run `/create-progress`. The routine
itself is allowed to update task statuses (`[ ]` → `[x]`, blocker lines) in this
file directly — those edits are preserved across regeneration.

## Conventions

- **Phase docs location**: `docs/phases/phase-<N>-<slug>.md`
- **Phase branch naming**: `phase-<N>-<slug>`
- **Commit message format**: `phase <N> task <X>: <short description>`
- **PR title format**: `Phase <N>: <phase name>`
- **PR opens when**: the last task in a phase is checked off
- **Task numbering**: tasks are numbered contiguously within a phase (`Task 1`, `Task 2`, ...) and reset at each new phase. Numbers match the `#### Task N` headings in the phase doc.

## Phase statuses

- `not started` — phase doc exists, no tasks checked off yet
- `in progress` — at least one task checked off, others outstanding
- `complete` — every task in the phase is `[x]`
- `not planned` — phase listed in PRD but has no phase doc; the autonomous routine writes a `> Blocker:` and stops on this phase until `/plan-phase <N>` is run

## Task statuses

- `[ ]` — not started, eligible to be picked up
- `[x]` — complete (validation passed, committed)
- `[~]` — skipped by a human; agent moves past it
- `> Blocker:` line under a task — blocked; agent will not retry until removed

## How to resolve a blocker

The agent leaves a `> Blocker:` line under any task it couldn't finish (or under a phase header for unplanned phases). To unblock:

1. Read the blocker text on the phase branch (or `master` if the phase isn't yet planned).
2. Either fix the underlying issue (run `/plan-phase <N>`, edit code, add an env var, clarify the phase doc), or change the task description to be more specific.
3. Delete the `> Blocker:` line.
4. Commit and push.
5. The next routine run will pick up where it left off.

To skip a task entirely, change `[ ]` to `[~]`.

---

## Phases

### Phase 0: Skeleton
- **Branch**: `phase-0-skeleton`
- **Phase doc**: `docs/phases/phase-0-skeleton.md`
- **Depends on**: No prior phases. Requires `dotnet 10` SDK locally and on the CI runner. Requires `gh` CLI for the final PR step.
- **Status**: in progress
- **Summary**: Close the remaining gaps in the SkipWatch project skeleton so every later phase has a stable foundation to build on.

Tasks:

- [x] Task 1: CREATE `.editorconfig` at the SkipWatch repo root
- [x] Task 2: ADD `/health` minimal-API endpoint to `Program.cs`
- [x] Task 3: ADD `~/.skipwatch/wiki/` directory creation in `Program.cs`
- [x] Task 4: CREATE `SkipWatch.Tests` project and register it in `SkipWatch.slnx`
- [x] Task 5: CREATE `SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs`
- [x] Task 6: CREATE `SkipWatch.Tests/Web/HealthEndpointTests.cs`
- [ ] Task 7: CREATE `.github/workflows/ci.yml`
- [ ] Task 8: Commit, push, and open PR

### Phase 1: Discovery round
- **Branch**: _to be determined by `/plan-phase 1`_
- **Phase doc**: _none yet — run `/plan-phase 1`_
- **Status**: not planned

### Phase 1b: Topic discovery
- **Branch**: _to be determined by `/plan-phase 1b`_
- **Phase doc**: _none yet — run `/plan-phase 1b`_
- **Status**: not planned

### Phase 2: Transcript worker
- **Branch**: _to be determined by `/plan-phase 2`_
- **Phase doc**: _none yet — run `/plan-phase 2`_
- **Status**: not planned

### Phase 3: Summary worker
- **Branch**: _to be determined by `/plan-phase 3`_
- **Phase doc**: _none yet — run `/plan-phase 3`_
- **Status**: not planned

### Phase 4: Triage UI
- **Branch**: _to be determined by `/plan-phase 4`_
- **Phase doc**: _none yet — run `/plan-phase 4`_
- **Status**: not planned

### Phase 5: Wiki worker and Project view
- **Branch**: _to be determined by `/plan-phase 5`_
- **Phase doc**: _none yet — run `/plan-phase 5`_
- **Status**: not planned

### Phase 6: Library-wide search
- **Branch**: _to be determined by `/plan-phase 6`_
- **Phase doc**: _none yet — run `/plan-phase 6`_
- **Status**: not planned

### Phase 7: Polish and packaging
- **Branch**: _to be determined by `/plan-phase 7`_
- **Phase doc**: _none yet — run `/plan-phase 7`_
- **Status**: not planned
