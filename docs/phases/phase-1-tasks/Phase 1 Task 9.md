# Phase 1 — Task 9: Commit, push, and open PR

You are executing the final task from SkipWatch Phase 1 (channel discovery round). The full phase plan is at [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md). This file is self-contained for executing Task 9.

## Prerequisites

Tasks 1-8 complete. All per-task VALIDATEs passed. Run the full validation suite before this task:

```bash
cd c:/Repos/Personal/SkipWatch
dotnet format SkipWatch.slnx --verify-no-changes
dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --configuration Debug --nologo
```

Both must exit 0. Expected test count: 11 passing (2 Phase 0 + 6 cron + 3 runner).

## Working directory

cwd = `c:/Repos/Personal/SkipWatch` — this is the git repo root. `c:/Repos/Personal/` is **not** a git repo.

## Phase context

Final milestone: get the branch into review. The autonomous execution loop ends here.

## Validation checkpoint

Branch `phase-1-discovery` pushed to `origin`; PR open against `master` with the correct title and body.

## The task

### IMPLEMENT

1. From `c:/Repos/Personal/SkipWatch/`, ensure you are on branch `phase-1-discovery` (the autonomous routine creates it from `master` on first execution; if running interactively use `git checkout -b phase-1-discovery master` if not already on it).

2. Stage all changes:

   ```
   git add SkipWatch.Core/SkipWatch.Core.csproj \
           SkipWatch.Core/Services/Discovery/ \
           SkipWatch.Core/Services/Interfaces/IYouTubeApiService.cs \
           SkipWatch.Core/Services/YouTube/YouTubeApiService.cs \
           SkipWatch/Program.cs \
           SkipWatch/Services/Discovery/ \
           SkipWatch/appsettings.json \
           SkipWatch.Tests/Services/Discovery/ \
           docs/phases/phase-1-discovery.md
   ```

3. Commit with the message:

   ```
   Phase 1: Discovery round

   - Add DiscoverySettings + Discovery config section
   - Extend IYouTubeApiService with ListUploadsPageAsync + GetVideoDetailsAsync
   - Add NCrontab + CronSchedule helper with */N shortcut
   - Add ChannelDiscoveryRunner (per-channel logic) and CollectionRoundService (BackgroundService)
   - Wire DI; remove /debug/yt/channel debug endpoint
   - Tests: CronScheduleTests, ChannelDiscoveryRunnerTests
   ```

4. Push:

   ```
   git push -u origin phase-1-discovery
   ```

5. Open PR:

   ```
   gh pr create --base master --head phase-1-discovery \
     --title "Phase 1: Discovery round" \
     --body "<see body format below>"
   ```

6. **PR title format**: `Phase 1: Discovery round`.

7. **PR body format**: copy the ACCEPTANCE CRITERIA list from [`docs/phases/phase-1-discovery.md`](../phase-1-discovery.md) as a checked-off Markdown checklist, followed by a `## Notes` section enumerating the assumptions documented in the NOTES section of the phase plan plus anything new that came up during execution.

### GOTCHAS

- `gh` CLI must be installed and authenticated. If `gh auth status` fails, the PR step will not succeed.
- Default branch is `master` (not `main`). `--base master` is mandatory.
- Working directory is `c:/Repos/Personal/SkipWatch/`, not `c:/Repos/Personal/`. `c:/Repos/Personal/` is not a git repo.

### VALIDATE

```bash
cd c:/Repos/Personal/SkipWatch
gh pr view --json number,title,state,headRefName,baseRefName \
  | python -c "import json,sys; d=json.load(sys.stdin); assert d['state']=='OPEN' and d['title']=='Phase 1: Discovery round' and d['headRefName']=='phase-1-discovery' and d['baseRefName']=='master', d; print('ok')"
```

Must print `ok`.
