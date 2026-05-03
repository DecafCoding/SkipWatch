# Setting up the autonomous PRD scheduled task (Cowork)

How to point a Claude Desktop **Cowork Scheduled task** at your repo and have it grind through `progress.md` one task at a time, opening a PR at each phase boundary. This is the local-machine equivalent of the cloud Routines setup in [README-routine-setup.md](README-routine-setup.md).

## What you should have in the repo before starting

- Your existing PRD at `docs/prd.md` — generate one with `/create-prd <source>` if you don't have one yet
- Your existing phase docs at `docs/phases/phase-<N>-<slug>.md` — generate each with `/plan-phase <phase>` if you haven't yet
- `progress.md` at the repo root — copy from the template, then fill in the phase headers and tasks to mirror your phase docs
- All of the above committed and pushed to the default branch (`master`)

If your PRD lives somewhere other than `docs/prd.md`, just update the path in `progress.md`'s Conventions section and in the task prompt. The agent reads the conventions before doing anything else.

## What you need on your machine before starting

- Claude Desktop installed and signed in
- The repo cloned locally to a stable absolute path (note the full path — you'll paste it into the prompt)
- `git` configured with credentials that can push to the repo
- `gh` (GitHub CLI) installed and authenticated — run `gh auth status` to verify it's green. The agent needs this to open PRs.
- Any task-specific secrets (env vars, API keys) available in the shell environment Cowork launches into

Test once interactively: from the repo directory, run `git fetch && gh auth status && gh pr list`. If all three work without prompts, the scheduled task will too.

## Wiring the scheduled task

1. Open **Claude Desktop**, switch to the **Cowork** view.
2. Click **New task**.
3. **Prompt**: paste the entire contents of [scheduled-prompt.md](scheduled-prompt.md). Replace `<ABSOLUTE_PATH_TO_REPO>` with your actual repo path (e.g. `C:\Users\JasonUser\Documents\SecondBrain\Orchestration\my-project`).
4. Run the task once manually first to confirm setup works end-to-end. Inspect the output. Fix any setup issues before scheduling.
5. Once a manual run completes cleanly, type `/schedule` inside that task to convert it into a recurring task.
6. **Cadence**: every 60 minutes is a reasonable starting point. If your tasks are short, every 45 minutes works too. Avoid going faster than 30 minutes — you risk overlapping runs on the same branch.
7. **Awake window**: pick hours your machine is reliably on and not sleeping. Overnight runs (10pm–6am) are great if you leave the laptop plugged in and toggle **Keep awake** in the Scheduled tasks pane.

## What to expect

- **Each run = one task.** After every run, the phase branch advances by one commit (or one blocker commit).
- **PRs appear at phase boundaries.** When the last task in a phase gets checked off, the task opens the PR via `gh`. Review it like you'd review a coworker's PR.
- **Blockers appear inline in `progress.md`** on the phase branch (not on `master`). Pull the branch, read the blocker, fix the underlying issue, push, and the next run picks the task back up.
- **The task is stateless across runs** — it relies entirely on the repo (branches, `progress.md`, phase docs) for state. Skipped runs (machine asleep, crash mid-task) recover automatically on the next trigger.

## Daily rhythm

- Morning: review whatever PRs landed overnight. Merge what's good, push fixes for what isn't.
- Whenever a PR merges to `master`, the next phase that depends on it becomes eligible. The next scheduled run will start it on a fresh branch.
- **Phases gate on the prior PR being merged.** If Phase 1's PR is still open when the task wakes up to start Phase 2, it will write a `> Blocker:` on Phase 2's first task ("waiting on phase 1 PR to merge") and stop. Merge or close the prior PR, delete the blocker line, and the next run picks up.
- If you want to pause: disable the schedule in Cowork. Re-enable when you're ready.

## Common failure modes (and what to do)

- **Run skipped because the machine was asleep.** Expected. The design is stateless — the next scheduled run picks up cleanly. If this happens often, toggle **Keep awake** or narrow the schedule to hours the machine is reliably on.
- **Setup failed (wrong dir, gh not authenticated).** The agent stops without doing work and surfaces the failure. Fix the underlying issue (`gh auth login`, correct the path in the prompt, etc.) and re-trigger.
- **Task ran but nothing happened.** Every remaining task has a `> Blocker:` line, or every task is `[x]` or `[~]`. Check `progress.md` on the latest phase branch.
- **PR never opened.** A task got checked off but the agent crashed before opening the PR. Either open it manually from the branch (`gh pr create`), or trigger one more run — the agent will detect all tasks are done and open the PR.
- **Two phases racing each other.** Don't run multiple scheduled tasks against the same repo. One task, one cadence.
- **Validation passes but the code is wrong.** Your validation commands aren't catching what matters. Tighten them in the phase doc and add a follow-up task to redo the work. The agent is only as honest as your tests.
- **Agent keeps creating blockers on the same task.** The phase doc is too vague. Rewrite the task with concrete acceptance criteria and either remove the blocker or trigger a fresh run.
- **Interactive session collided with a scheduled run.** The agent stops with a blocker about concurrent edits. Finish your interactive work, push, and the next scheduled run resumes.

## Cost / usage notes

Cowork scheduled tasks consume Claude Desktop usage from the same plan as your interactive sessions. Hourly runs over an 8-hour overnight window add up — monitor your weekly burn. If quota burns down faster than expected, lower the cadence (every 90 min instead of every 60) and tighten task descriptions so the agent does less searching.

Unlike cloud Routines, there is no separate per-day run cap to plan around — the only limit is your overall plan usage and your machine being awake.

## When to stop using the scheduled task and go back to interactive

The scheduled task is great for well-scoped, mechanically-verifiable work. Switch back to an interactive Claude Code or Cowork session when:

- You're designing the next phase (the agent shouldn't make architectural decisions).
- You're debugging something the agent couldn't unblock itself on.
- You want to refactor across phase boundaries.

The scheduled task and an interactive session can coexist on the same repo — just don't have both running at the exact same time on the same branch. The agent will detect this and stop with a blocker, but it's cleaner to pause the schedule first.

## Cowork vs cloud Routines — quick comparison

| | Cowork Scheduled task | Cloud Routine |
| --- | --- | --- |
| Where it runs | Your local machine | Anthropic infrastructure |
| Requires machine awake | Yes | No |
| Repo access | Local clone + `git`/`gh` creds | Granted at routine setup |
| Per-day run cap | None (plan usage only) | 15 runs/day on Max |
| Slack/Linear connectors | Not built in | Built in |
| Best for | Daytime/evening cadence on a machine you're already using | Overnight grinding without leaving a laptop on |

Both use the same repo conventions and the same one-task-per-run loop, so you can switch between them without changing `progress.md`, the phase docs, or the PRD.
