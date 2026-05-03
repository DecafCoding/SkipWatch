# Phase 0: Skeleton

The following plan should be complete, but it is important that you validate documentation and codebase patterns and task sanity before you start implementing.

Pay special attention to naming of existing utils, types, and models. Import from the right files.

## Phase Description

Close the remaining gaps in the SkipWatch project skeleton so every later phase has a stable foundation to build on. The two-project solution (`SkipWatch` Blazor Server host + `SkipWatch.Core` library), `Directory.Build.props`, harvested UI shell (theme, message center, layout), SQLite + EF Core wiring, `dotnet-ef` local tool, and the `Initial` + `AddVideoFts` migrations are already in place. What remains:

1. **`.editorconfig`** — codify the C# style rules so `dotnet format` is meaningful.
2. **`/health` minimal-API endpoint** — declared in PRD §6 Phase 0; needed as a non-LLM liveness probe and as a smoke target for CI/launcher scripts.
3. **`~/.skipwatch/wiki/` creation on first run** — declared in PRD §6 Phase 0; today `Program.cs` creates `~/.skipwatch/` but not the `wiki/` subdirectory the future wiki worker (Phase 5) writes into.
4. **`SkipWatch.Tests` project** — xUnit + FluentAssertions + bUnit, referenced from `SkipWatch.slnx`, with a small smoke suite (DbContext builds, `/health` returns 200).
5. **CI workflow** — GitHub Actions running build, `dotnet format --verify-no-changes`, and `dotnet test` on push and PR. The PRD calls out "`.editorconfig` + `dotnet format` in CI" as a Phase 0 deliverable.

The phase deliberately does **not** touch any of the worker code, feature services, or UI pages that later phases will own. The two existing `/debug/yt/channel/{...}` and `/debug/transcript/{videoId}` endpoints in `Program.cs` stay — the PRD explicitly schedules their removal for Phases 1 and 2 respectively.

## User Stories

As a SkipWatch developer
I want a clean repository skeleton with a test project, a health probe, and CI that catches style drift
So that every later phase starts from a known-good baseline and regressions are caught before merge.

As the launcher script (Phase 7) and any future operator
I want a `/health` endpoint that returns 200 OK without touching the database, LLM, YouTube API, or Apify
So that I can confirm the server has booted before opening the browser at `http://localhost:7860`.

## Problem Statement

The existing skeleton has working build, EF migrations, DI wiring, and the harvested UI shell — but it is missing the *quality and observability scaffolding* every subsequent phase will rely on. Without `.editorconfig`, `dotnet format` has no rules to enforce. Without a tests project, Phase 1's discovery round has nowhere to land its first unit test. Without CI, lint or test regressions only get caught locally. Without `/health`, Phase 7's launcher script has no way to confirm the binary booted. Without `~/.skipwatch/wiki/` being created at startup, Phase 5's wiki worker would have to defensively `Directory.CreateDirectory` on every job. Each of these is small in isolation but compounds badly if deferred.

## Solution Statement

Add the five missing pieces in dependency order: `.editorconfig` first (so format is enforceable), then the runtime additions (`/health`, `wiki/` directory) and the test project (which will itself test those additions), and finally the CI workflow that exercises everything together. Every change is versioned inside the SkipWatch repo at `c:\Repos\Personal\SkipWatch\`, including this phase doc which lives at `docs/phases/phase-0-skeleton.md` relative to the repo root.

## Phase Metadata

**Phase Type**: Foundation / Mixed (config + small new capability + test project + CI)
**Estimated Complexity**: Low
**Primary Systems Affected**: `SkipWatch/Program.cs`, `SkipWatch.slnx`, repo root (`.editorconfig`, `.github/workflows/`), new `SkipWatch.Tests/` project
**Dependencies**: No prior phases. Requires `dotnet 10` SDK locally and on the CI runner. Requires `gh` CLI for the final PR step.

---

## CONTEXT REFERENCES

### Relevant Codebase Files IMPORTANT: YOU MUST READ THESE FILES BEFORE IMPLEMENTING!

- [SkipWatch/SkipWatch/Program.cs](../../SkipWatch/SkipWatch/Program.cs) — host wiring; this is where `/health` and the `wiki/` directory creation land. Note the existing `dataDir` block at the top (lines 13-17) and the existing minimal-API endpoints at the bottom (`/debug/yt/channel/...`, `/debug/transcript/...`) for the registration pattern.
- [SkipWatch/SkipWatch/SkipWatch.csproj](../../SkipWatch/SkipWatch/SkipWatch.csproj) — `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `UserSecretsId` set. Test project must match `TargetFramework` to share types.
- [SkipWatch/SkipWatch.Core/SkipWatch.Core.csproj](../../SkipWatch/SkipWatch.Core/SkipWatch.Core.csproj) — referenced by both the App and the Tests project.
- [SkipWatch/Directory.Build.props](../../SkipWatch/Directory.Build.props) — `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`. Test project inherits these automatically; nothing to change here.
- [SkipWatch/SkipWatch.slnx](../../SkipWatch/SkipWatch.slnx) — XML solution file. New format (`<Solution><Project Path="..."/></Solution>`). The Tests project is added by inserting another `<Project Path="..."/>` line.
- [SkipWatch/SkipWatch.Core/Db/SkipWatchDbContext.cs](../../SkipWatch/SkipWatch.Core/Db/SkipWatchDbContext.cs) — used by the smoke test to confirm the model still builds.
- [SkipWatch/SkipWatch.Core/Db/DesignTimeDbContextFactory.cs](../../SkipWatch/SkipWatch.Core/Db/DesignTimeDbContextFactory.cs) — pattern for building a `SkipWatchDbContext` outside the host; the test fixture mirrors it but uses an in-memory SQLite (`Data Source=:memory:`) so tests don't touch `~/.skipwatch/`.
- [SkipWatch/dotnet-tools.json](../../SkipWatch/dotnet-tools.json) — local-tool manifest already pinning `dotnet-ef`. CI must run `dotnet tool restore` before any `dotnet ef` call (none in this phase, but the pattern is established).
- [SkipWatch/.gitignore](../../SkipWatch/.gitignore) — confirm `bin/` and `obj/` are ignored before committing test project artifacts.
- [docs/prd.md §6 Phase 0](../prd.md) — single source of truth for what "skeleton complete" means.

### New Files to Create

- `SkipWatch/.editorconfig` — C# style rules consistent with `Directory.Build.props`.
- `SkipWatch/.github/workflows/ci.yml` — GitHub Actions workflow: setup-dotnet, restore, build, format check, test.
- `SkipWatch/SkipWatch.Tests/SkipWatch.Tests.csproj` — xUnit + FluentAssertions + bUnit, references `SkipWatch.csproj` and `SkipWatch.Core.csproj`.
- `SkipWatch/SkipWatch.Tests/Usings.cs` — global `using Xunit;` and `using FluentAssertions;` so individual files stay terse.
- `SkipWatch/SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs` — single fixture proving the model builds and migrations apply against in-memory SQLite.
- `SkipWatch/SkipWatch.Tests/Web/HealthEndpointTests.cs` — bUnit-adjacent test using `WebApplicationFactory<Program>` to call `/health` and assert 200 + body shape.

### Relevant Documentation YOU SHOULD READ THESE BEFORE IMPLEMENTING!

- [.NET runtime config — `appsettings`/Options pattern](https://learn.microsoft.com/aspnet/core/fundamentals/configuration/options) — already wired; referenced as the precedent for adding nothing new in this phase beyond what exists.
- [Minimal API — `MapGet`](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis) — pattern for the `/health` endpoint (already used twice in `Program.cs` for the debug endpoints).
- [`Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<TEntryPoint>`)](https://learn.microsoft.com/aspnet/core/test/integration-tests) — required for the `/health` test. Must add `Microsoft.AspNetCore.Mvc.Testing` package and a `public partial class Program;` declaration in `Program.cs` (top-level statements need this to expose the assembly entry point as a generic argument).
- [xUnit getting started](https://xunit.net/docs/getting-started/v2/netcore/cmd) — xUnit v2 with .NET SDK; `dotnet new xunit` is the canonical scaffold.
- [FluentAssertions docs](https://fluentassertions.com/introduction) — `Should()` chains for readable assertions.
- [bUnit getting started](https://bunit.dev/docs/getting-started/index.html) — only needed in this phase to bring the dependency in; no Razor tests are written yet (none exist worth testing in Phase 0).
- [setup-dotnet GitHub Action](https://github.com/actions/setup-dotnet) — current major is `v4`. Use `dotnet-version: 10.0.x` to track the SDK installed locally.
- [`dotnet format --verify-no-changes`](https://learn.microsoft.com/dotnet/core/tools/dotnet-format) — exits non-zero when formatting would change anything; the canonical CI gate.
- [EditorConfig spec](https://editorconfig.org/) and [.NET-specific EditorConfig keys](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/code-style-rule-options) — for the `.editorconfig` file.

### Patterns to Follow

**Naming Conventions:**
- Folders: PascalCase (`Components/`, `Features/Channels/`, `Services/YouTube/`).
- C# files: PascalCase matching the primary type name.
- Test files: `<TypeUnderTest>Tests.cs` or `<Behavior>Tests.cs` in a folder mirroring the source layout (e.g., `SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs` mirrors `SkipWatch.Core/Db/SkipWatchDbContext.cs`).

**Endpoint registration in `Program.cs`:**
The two existing debug endpoints register inline near the bottom of the file, after `app.MapRazorComponents<App>()`. Mirror that placement for `/health` rather than introducing a new `Endpoints/` folder yet — the PRD §9 tree marks `Endpoints/HealthEndpoints.cs` as outstanding, and a one-line minimal-API call inline is small enough that pulling it into a separate static class adds more ceremony than value at this stage. **Decision logged in NOTES.**

**Configuration access:**
Use `Microsoft.Extensions.Configuration` + `IOptions<T>` (already done for `YouTubeApiSettings`, `ApifySettings` in `Program.cs` lines 29-30). No new settings record is introduced in this phase.

**Logging:**
The harvested `YouTubeQuotaManager` uses constructor-injected `ILogger<T>`. Tests in this phase do not need logger assertions — the smoke test scope is "the model and the host bootstrap without throwing."

**Error Handling:**
`Program.cs` already wires `app.UseExceptionHandler("/Error", ...)` for non-Development environments. The `/health` endpoint must not throw under any circumstance — it returns a literal JSON object with the assembly version and a UTC timestamp; it must not touch the DbContext, the YouTube quota manager, or the Apify client.

---

## IMPLEMENTATION PLAN

**Rendering**: Flat

**Rationale**: 7 tasks plus the mandatory final commit/push/PR task. The work is loosely coupled — `.editorconfig`, `/health`, `wiki/` dir creation, test project scaffold, two test files, CI workflow — and each task is independently validatable. Wrapping these in milestones would be organizational noise. The final commit/push/PR task is rendered as a single-task milestone per the template's mandate.

Tasks always execute one at a time, top to bottom. Each task carries its own VALIDATE that runs the moment that task completes.

**Working directory note:** all task validation commands assume the executor is at `c:\Repos\Personal\SkipWatch` (the git repo root) unless explicitly noted. Planning artifacts (this phase doc, `docs/prd.md`, `docs/scheduled-prompt.md`) live inside the repo under `docs/` and are versioned alongside the source.

#### Task 1: CREATE `.editorconfig` at the SkipWatch repo root

Add the EditorConfig file that `dotnet format` consults. Rules must be consistent with `Directory.Build.props` (which already sets `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`) and with the conventions visible in the existing C# files (4-space indent, file-scoped namespaces, primary constructors used in `SkipWatchDbContext` and `YouTubeQuotaManager`).

- **IMPLEMENT**: Create `c:\Repos\Personal\SkipWatch\.editorconfig` with at minimum:
  - `root = true` at the top
  - `[*]`: `charset = utf-8`, `end_of_line = crlf` (Windows-native repo), `insert_final_newline = true`, `trim_trailing_whitespace = true`, `indent_style = space`, `indent_size = 4`
  - `[*.{json,yml,yaml,csproj,slnx,props,targets}]`: `indent_size = 2`
  - `[*.md]`: `trim_trailing_whitespace = false` (Markdown line breaks)
  - `[*.cs]` block with: `csharp_style_namespace_declarations = file_scoped:warning`, `csharp_using_directive_placement = outside_namespace:warning`, `dotnet_sort_system_directives_first = true`, `dotnet_separate_import_directive_groups = false`
- **PATTERN**: None in repo; follow [.NET EditorConfig reference](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/code-style-rule-options).
- **IMPORTS**: N/A (config file).
- **GOTCHA**: Existing files use file-scoped namespaces and 4-space indents already — verify with `dotnet format --verify-no-changes` after adding `.editorconfig` and *before* committing. If it reports changes, either tighten the rules to match the existing code or run `dotnet format` once and review the diff. Do **not** silently auto-format the harvested CSS or the migration files; if the tool wants to rewrite them, narrow the rules instead.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet format --verify-no-changes` exits 0.

#### Task 2: ADD `/health` minimal-API endpoint to `Program.cs`

Add a single non-throwing JSON endpoint that proves the server has booted. Mirrors the registration style of the existing two `/debug/...` endpoints.

- **IMPLEMENT**: In `SkipWatch/SkipWatch/Program.cs`, after `app.MapRazorComponents<App>().AddInteractiveServerRenderMode();` (line 67) and before the existing `/debug/yt/channel/{handleOrId}` mapping (line 71), add:
  ```csharp
  // Liveness probe. Touches no external dependency — safe to call from the launcher
  // script (Phase 7) before the worker pipeline has warmed up.
  app.MapGet("/health", () => Results.Json(new
  {
      status = "ok",
      version = typeof(Program).Assembly.GetName().Version?.ToString(),
      utc = DateTime.UtcNow
  }));
  ```
  Then at the very bottom of the file (after `app.Run();`), add the partial-class declaration that `WebApplicationFactory<Program>` needs to bind to top-level-statement programs:
  ```csharp
  public partial class Program;
  ```
- **PATTERN**: Two existing debug endpoints in `Program.cs` lines 71-123 use `app.MapGet("/debug/...", async (...) => Results.Json(...))`. Mirror that shape but with no async, no DI parameters, no exception path.
- **IMPORTS**: None new — `Results.Json` is already in scope via implicit usings (the existing debug endpoints use it without an explicit import).
- **GOTCHA**: The `public partial class Program;` line is required for Task 6's `WebApplicationFactory<Program>` test to compile. If you skip it, the test file will fail to build with `CS0122` ("inaccessible due to its protection level") because top-level-statement entry points generate an internal `Program` class by default. Do not put this declaration inside a namespace — top-level statements always emit `Program` in the global namespace.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet build SkipWatch/SkipWatch.csproj -c Debug --nologo -v quiet` exits 0.

#### Task 3: ADD `~/.skipwatch/wiki/` directory creation in `Program.cs`

The PRD §6 Phase 0 says "Default data dir `~/.skipwatch/`; create `~/.skipwatch/wiki/` on first run." Today only the parent is created.

- **IMPLEMENT**: In `SkipWatch/SkipWatch/Program.cs`, in the existing block at lines 13-17, after `Directory.CreateDirectory(dataDir);` add:
  ```csharp
  Directory.CreateDirectory(Path.Combine(dataDir, "wiki"));
  ```
- **PATTERN**: Same `Directory.CreateDirectory` call already present one line above. `Directory.CreateDirectory` is a no-op when the directory exists, so this is safe to call on every boot.
- **IMPORTS**: None new — `System.IO` already imported via implicit usings.
- **GOTCHA**: Do **not** introduce a `WikiRoot` configuration knob in this phase. The PRD locks the wiki location at `<data-dir>/wiki/` and parameterizing it now would add a setting nothing else uses yet. Phase 5 owns the wiki worker and can parameterize then if needed.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet build SkipWatch/SkipWatch.csproj -c Debug --nologo -v quiet` exits 0, and the inline check `dotnet run --project SkipWatch/SkipWatch.csproj --no-build -- --urls http://127.0.0.1:7861` started in the background must produce the wiki directory. Use this single non-interactive snippet:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  rm -rf "$HOME/.skipwatch/wiki" 2>/dev/null || true
  dotnet build SkipWatch/SkipWatch.csproj -c Debug --nologo -v quiet
  dotnet run --project SkipWatch/SkipWatch.csproj --no-build --no-launch-profile -- --urls http://127.0.0.1:7861 > /tmp/sw-run.log 2>&1 &
  PID=$!
  for i in $(seq 1 30); do
    if [ -d "$HOME/.skipwatch/wiki" ]; then break; fi
    sleep 1
  done
  kill $PID 2>/dev/null || true
  wait $PID 2>/dev/null || true
  test -d "$HOME/.skipwatch/wiki"
  ```
  Exit 0 on success (directory exists), non-zero on failure.

#### Task 4: CREATE `SkipWatch.Tests` project and register it in `SkipWatch.slnx`

Scaffold the xUnit + FluentAssertions + bUnit test project that all later phases will extend. This task creates the project shell only; Tasks 5 and 6 add the actual test files.

- **IMPLEMENT**:
  1. From `c:/Repos/Personal/SkipWatch/`, run `dotnet new xunit -n SkipWatch.Tests -f net10.0 -o SkipWatch.Tests`. This produces `SkipWatch.Tests/SkipWatch.Tests.csproj` and a default `UnitTest1.cs`.
  2. Delete the generated `UnitTest1.cs` (Tasks 5 and 6 author the real tests).
  3. Add package references via the CLI (run from `c:/Repos/Personal/SkipWatch/`):
     - `dotnet add SkipWatch.Tests/SkipWatch.Tests.csproj package FluentAssertions --version 6.12.*`
     - `dotnet add SkipWatch.Tests/SkipWatch.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing`
     - `dotnet add SkipWatch.Tests/SkipWatch.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite`
     - `dotnet add SkipWatch.Tests/SkipWatch.Tests.csproj package bunit`
  4. Add project references:
     - `dotnet add SkipWatch.Tests/SkipWatch.Tests.csproj reference SkipWatch/SkipWatch.csproj`
     - `dotnet add SkipWatch.Tests/SkipWatch.Tests.csproj reference SkipWatch.Core/SkipWatch.Core.csproj`
  5. Create `SkipWatch/SkipWatch.Tests/Usings.cs` with:
     ```csharp
     global using FluentAssertions;
     global using Xunit;
     ```
  6. Edit `SkipWatch/SkipWatch.slnx` to add a new `<Project Path="SkipWatch.Tests/SkipWatch.Tests.csproj" />` line below the existing two project lines.
- **PATTERN**: The two existing csproj files in the solution (`SkipWatch.csproj`, `SkipWatch.Core.csproj`) target `net10.0` and inherit from `Directory.Build.props`. The new test csproj will inherit the same `Nullable=enable`, `TreatWarningsAsErrors=true`, etc. — no per-project overrides needed.
- **IMPORTS**: N/A (project scaffold).
- **GOTCHA**: `dotnet new xunit` creates the project targeting whatever framework the SDK defaults to. Pass `-f net10.0` explicitly. If the generated csproj sets `<TargetFramework>` to something else, edit it manually to `net10.0`. The other two projects must have a matching TFM for `WebApplicationFactory<Program>` to bind across the project boundary.
- **GOTCHA**: FluentAssertions 7.x and 8.x changed licensing. Pinning to `6.12.*` keeps the project on the last MIT-licensed line until a deliberate upgrade decision lands. **Decision logged in NOTES.**
- **GOTCHA**: `bunit` is added now even though no Razor tests exist yet — it's listed in PRD §9 Phase 0 deliverables and removing it later if unused is one line. No tests written against it in this phase.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet build SkipWatch.slnx -c Debug --nologo -v quiet` exits 0, and the file `SkipWatch.Tests/SkipWatch.Tests.csproj` exists with all four package references and both project references. Run:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  dotnet build SkipWatch.slnx -c Debug --nologo -v quiet \
    && grep -q 'SkipWatch.Tests' SkipWatch.slnx \
    && grep -q 'FluentAssertions' SkipWatch.Tests/SkipWatch.Tests.csproj \
    && grep -q 'Microsoft.AspNetCore.Mvc.Testing' SkipWatch.Tests/SkipWatch.Tests.csproj \
    && grep -q 'Microsoft.EntityFrameworkCore.Sqlite' SkipWatch.Tests/SkipWatch.Tests.csproj \
    && grep -q 'bunit' SkipWatch.Tests/SkipWatch.Tests.csproj
  ```
  Exit 0 on success.

#### Task 5: CREATE `SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs`

A single test that proves the EF Core model still builds and `Database.Migrate()` runs cleanly against in-memory SQLite. This is the regression gate for any later phase that touches an entity, the DbContext, or a migration.

- **IMPLEMENT**: Create `SkipWatch/SkipWatch.Tests/Db/SkipWatchDbContextSmokeTests.cs`:
  ```csharp
  using Microsoft.Data.Sqlite;
  using Microsoft.EntityFrameworkCore;
  using SkipWatch.Core.Db;

  namespace SkipWatch.Tests.Db;

  public sealed class SkipWatchDbContextSmokeTests
  {
      [Fact]
      public void Migrations_apply_against_in_memory_sqlite()
      {
          using var connection = new SqliteConnection("Data Source=:memory:");
          connection.Open();

          var options = new DbContextOptionsBuilder<SkipWatchDbContext>()
              .UseSqlite(connection)
              .Options;

          using var ctx = new SkipWatchDbContext(options);
          ctx.Database.Migrate();

          ctx.Channels.Should().BeEmpty();
          ctx.Videos.Should().BeEmpty();
          ctx.Topics.Should().BeEmpty();
          ctx.Projects.Should().BeEmpty();
      }
  }
  ```
- **PATTERN**: Follows the connection-lifetime trick from the EF Core docs ([In-memory SQLite for testing](https://learn.microsoft.com/ef/core/testing/testing-without-the-database#sqlite-in-memory)) — keep the `SqliteConnection` open for the lifetime of the test so the in-memory DB survives. Mirrors the production wiring style in [Program.cs lines 22-23](../../SkipWatch/SkipWatch/Program.cs#L22-L23).
- **IMPORTS**: As shown in the snippet. `Xunit` and `FluentAssertions` come from `Usings.cs` (Task 4).
- **GOTCHA**: `Database.Migrate()` (not `EnsureCreated()`) is what production runs at startup — using `EnsureCreated()` here would silently bypass the FTS5 migration even after Phase 6 fills its body in, and the test would lie. Stick with `Migrate()`.
- **GOTCHA**: The in-memory SQLite connection string is exactly `Data Source=:memory:`. Any other string (`":memory:"`, `Filename=:memory:`) opens an on-disk file in the test runner's working directory.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --filter "FullyQualifiedName~SkipWatchDbContextSmokeTests" --nologo -v quiet` exits 0 with one passing test.

#### Task 6: CREATE `SkipWatch.Tests/Web/HealthEndpointTests.cs`

A `WebApplicationFactory<Program>`-backed test that calls `/health` and asserts the response shape. This locks in the contract Phase 7's launcher script will rely on.

- **IMPLEMENT**: Create `SkipWatch/SkipWatch.Tests/Web/HealthEndpointTests.cs`:
  ```csharp
  using System.Net;
  using System.Net.Http.Json;
  using Microsoft.AspNetCore.Mvc.Testing;

  namespace SkipWatch.Tests.Web;

  public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
  {
      private readonly WebApplicationFactory<Program> _factory;

      public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

      [Fact]
      public async Task Get_health_returns_200_with_status_ok()
      {
          using var client = _factory.CreateClient();

          var response = await client.GetAsync("/health");

          response.StatusCode.Should().Be(HttpStatusCode.OK);
          var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();
          payload.Should().NotBeNull();
          payload!.Status.Should().Be("ok");
      }

      private sealed record HealthResponse(string Status, string? Version, DateTime Utc);
  }
  ```
- **PATTERN**: Standard `WebApplicationFactory<TEntryPoint>` pattern from the [Microsoft testing docs](https://learn.microsoft.com/aspnet/core/test/integration-tests).
- **IMPORTS**: As shown.
- **GOTCHA**: `WebApplicationFactory<Program>` requires the partial-class declaration added in Task 2. If the test fails to compile with `CS0122`, go back and confirm `public partial class Program;` was added at the bottom of `Program.cs`.
- **GOTCHA**: The factory boots the *whole* host, including the `Database.Migrate()` call against `~/.skipwatch/skipwatch.db`. This is acceptable for a Phase 0 smoke test and the file is small. If this becomes a problem in later phases (e.g., if a worker hits Apify on startup), the standard fix is `WebApplicationFactory<Program>.WithWebHostBuilder(b => b.ConfigureServices(...))` to swap services. Out of scope for this phase.
- **GOTCHA**: The test creates the real `~/.skipwatch/wiki/` directory on the developer's machine because Task 3's code runs unconditionally at startup. This is by design for now.
- **VALIDATE**: `cd c:/Repos/Personal/SkipWatch && dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --filter "FullyQualifiedName~HealthEndpointTests" --nologo -v quiet` exits 0 with one passing test.

#### Task 7: CREATE `.github/workflows/ci.yml`

Add the GitHub Actions workflow that runs build, format check, and test on push to `master` and on every pull request.

- **IMPLEMENT**: Create `SkipWatch/.github/workflows/ci.yml`:
  ```yaml
  name: ci

  on:
    push:
      branches: [master]
    pull_request:

  jobs:
    build-test:
      runs-on: ubuntu-latest
      steps:
        - uses: actions/checkout@v4

        - uses: actions/setup-dotnet@v4
          with:
            dotnet-version: 10.0.x

        - name: Restore tools
          run: dotnet tool restore

        - name: Restore packages
          run: dotnet restore SkipWatch.slnx

        - name: Format check
          run: dotnet format SkipWatch.slnx --verify-no-changes --no-restore

        - name: Build
          run: dotnet build SkipWatch.slnx --configuration Release --no-restore

        - name: Test
          run: dotnet test SkipWatch.slnx --configuration Release --no-build --nologo
  ```
- **PATTERN**: Standard `setup-dotnet` workflow shape from the [setup-dotnet README](https://github.com/actions/setup-dotnet).
- **IMPORTS**: N/A.
- **GOTCHA**: The default branch is `master`, not `main`. Hard-code `master` in the `push.branches` filter; the autonomous routine and any future PRs target `master`.
- **GOTCHA**: `dotnet format --verify-no-changes` must run *before* `dotnet build` because format violations should fail fast. It does its own restore unless `--no-restore` is passed; pass `--no-restore` after the explicit restore step to keep the workflow tight.
- **GOTCHA**: The .NET 10 SDK is currently in preview/RC at the time this phase is planned (cutoff: January 2026). `setup-dotnet` may need `include-prerelease: true` if the GA build hasn't landed on the runner image. If the workflow fails with a "no SDK matching 10.0.x" error, add `include-prerelease: true` under `with:` in the setup-dotnet step. **Decision logged in NOTES.**
- **VALIDATE**: There is no GitHub Actions runner available locally; verify the workflow file parses as valid YAML *and* every command in it is one we have already validated locally:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('ok')" \
    && dotnet tool restore \
    && dotnet restore SkipWatch.slnx \
    && dotnet format SkipWatch.slnx --verify-no-changes --no-restore \
    && dotnet build SkipWatch.slnx --configuration Release --no-restore \
    && dotnet test SkipWatch.slnx --configuration Release --no-build --nologo
  ```
  Exit 0 on success — every step the CI runner will execute has already passed locally on the same tree, so the first CI run after merge is a known-good run.

### Final Milestone: Commit, push, and open PR (mandatory)

The final milestone of this plan. The autonomous execution loop only ends here.

**Validation checkpoint**: Branch `phase-0-skeleton` pushed to `origin`; PR open against `master` with the correct title and body.

#### Task 8: Commit, push, and open PR

After every prior task's VALIDATE has passed:

- **IMPLEMENT**:
  1. From `c:/Repos/Personal/SkipWatch/`, ensure you are on branch `phase-0-skeleton` (the autonomous routine creates it from `master` on first execution; if running interactively use `git checkout -b phase-0-skeleton master` if not already on it).
  2. Stage all changes: `git add .editorconfig .github/workflows/ci.yml SkipWatch/Program.cs SkipWatch.slnx SkipWatch.Tests/`.
  3. Commit with:
     ```
     Phase 0: Skeleton

     - Add .editorconfig with C# style rules
     - Add /health minimal-API endpoint and Program partial class declaration
     - Create ~/.skipwatch/wiki/ on first run
     - Add SkipWatch.Tests project (xUnit + FluentAssertions + bUnit)
     - Add CI workflow (format check + build + test)
     ```
  4. Push the branch: `git push -u origin phase-0-skeleton`.
  5. Open the PR:
     ```
     gh pr create --base master --head phase-0-skeleton \
       --title "Phase 0: Skeleton" \
       --body "<see body format below>"
     ```
  6. **PR title format**: `Phase 0: Skeleton`.
  7. **PR body format**: copy the ACCEPTANCE CRITERIA list as a checked-off Markdown checklist, followed by a `## Notes` section enumerating the assumptions documented in the NOTES section of this plan plus anything new that came up during execution.
- **GOTCHA**: `gh` CLI must be installed and authenticated. If `gh auth status` fails, the PR step will not succeed. The agent or operator must resolve `gh` auth before this task runs.
- **GOTCHA**: Default branch is `master` (not `main`). `--base master` is mandatory.
- **GOTCHA**: Working directory is `c:/Repos/Personal/SkipWatch/`, not `c:/Repos/Personal/`. `c:/Repos/Personal/` is not a git repo; running `git` there will fail with `fatal: not a git repository`.
- **GOTCHA**: This phase doc lives at `docs/phases/phase-0-skeleton.md` inside the repo. If the executor edits it during execution (e.g., to record a deviation in the NOTES section), those edits are part of the same commit as the source changes — they are intentionally versioned together.
- **VALIDATE**:
  ```bash
  cd c:/Repos/Personal/SkipWatch
  gh pr view --json number,title,state,headRefName,baseRefName \
    | python -c "import json,sys; d=json.load(sys.stdin); assert d['state']=='OPEN' and d['title']=='Phase 0: Skeleton' and d['headRefName']=='phase-0-skeleton' and d['baseRefName']=='master', d; print('ok')"
  ```
  Exit 0 on success.

---

## TESTING STRATEGY

### Unit Tests

xUnit v2 with FluentAssertions for assertions. Two tests in this phase:
- `SkipWatchDbContextSmokeTests.Migrations_apply_against_in_memory_sqlite` — proves the EF model + the `Initial` migration are coherent (the `AddVideoFts` migration body is empty and applies as a no-op, which matches the deferred-to-Phase-6 design recorded in `prd.md` §5).
- `HealthEndpointTests.Get_health_returns_200_with_status_ok` — proves `/health` is reachable and returns the documented JSON shape.

These two tests are deliberately the *only* tests in Phase 0. Each later phase brings its own test files alongside its source.

### Integration Tests

The `HealthEndpointTests` test is technically an integration test (boots the full host) — there's no separate "Integration" folder yet because there's nothing else of that scope in Phase 0.

### Edge Cases

For Phase 0 only:
- `~/.skipwatch/` does not exist before first run → `Directory.CreateDirectory` creates it; covered by Task 3's manual VALIDATE.
- `~/.skipwatch/` exists but `wiki/` does not → covered by Task 3's manual VALIDATE because the script removes `wiki/` first.
- `WebApplicationFactory<Program>` requires `public partial class Program;` → covered by Task 6's compile + run path.
- `dotnet format` finding the harvested CSS or migration files surprising → covered by Task 1's VALIDATE; tighten rules if it reports changes.

---

## VALIDATION COMMANDS

Execute every command to ensure zero regressions and 100% phase correctness. All commands assume the working directory is `c:/Repos/Personal/SkipWatch`.

### Level 1: Syntax & Style

```bash
cd c:/Repos/Personal/SkipWatch
dotnet format SkipWatch.slnx --verify-no-changes
```

**Expected**: Exit code 0. Any non-zero exit means there's a formatting drift; run `dotnet format SkipWatch.slnx` to fix and re-validate.

### Level 2: Unit Tests

```bash
cd c:/Repos/Personal/SkipWatch
dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj --configuration Debug --nologo
```

**Expected**: Two passing tests, zero failures, zero skipped.

### Level 3: Integration Tests

Same command as Level 2 — `HealthEndpointTests` is the integration test in this phase and is included in the same run.

### Level 4: Manual Validation

Single non-interactive smoke test exercising every Phase 0 deliverable:

```bash
cd c:/Repos/Personal/SkipWatch

# Build the solution.
dotnet build SkipWatch.slnx -c Release --nologo -v quiet

# Confirm the workflow file is well-formed YAML.
python -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"

# Confirm .editorconfig exists at the repo root and is rooted.
test -f .editorconfig
head -1 .editorconfig | grep -q 'root = true'

# Confirm the test project is wired into the solution.
grep -q 'SkipWatch.Tests' SkipWatch.slnx

# Boot the host, confirm /health returns 200, confirm wiki/ is created, then tear down.
rm -rf "$HOME/.skipwatch/wiki" 2>/dev/null || true
dotnet run --project SkipWatch/SkipWatch.csproj --no-build --no-launch-profile -- --urls http://127.0.0.1:7861 > /tmp/sw-run.log 2>&1 &
PID=$!
HEALTH_OK=0
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:7861/health > /tmp/sw-health.json 2>/dev/null; then
    HEALTH_OK=1
    break
  fi
  sleep 1
done
kill $PID 2>/dev/null || true
wait $PID 2>/dev/null || true

test "$HEALTH_OK" = "1"
test -d "$HOME/.skipwatch/wiki"
grep -q '"status":"ok"' /tmp/sw-health.json
```

**Expected**: Exit code 0 — every assertion passes.

### Level 5: Additional Validation (Optional)

None required for Phase 0. Phase 7 (packaging) will add a launcher-script smoke test.

---

## ACCEPTANCE CRITERIA

- [ ] `c:/Repos/Personal/SkipWatch/.editorconfig` exists with `root = true` and rules consistent with `Directory.Build.props`
- [ ] `dotnet format SkipWatch.slnx --verify-no-changes` exits 0 on the committed tree
- [ ] `Program.cs` registers `GET /health` returning JSON `{ status: "ok", version, utc }`
- [ ] `Program.cs` declares `public partial class Program;` so `WebApplicationFactory<Program>` can bind
- [ ] `Program.cs` creates `~/.skipwatch/wiki/` at startup alongside `~/.skipwatch/`
- [ ] `SkipWatch.Tests` project exists, targets `net10.0`, references `SkipWatch.csproj` and `SkipWatch.Core.csproj`, and is registered in `SkipWatch.slnx`
- [ ] `SkipWatch.Tests` references `xunit` (via the template), `FluentAssertions` 6.12.*, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.EntityFrameworkCore.Sqlite`, and `bunit`
- [ ] `SkipWatchDbContextSmokeTests.Migrations_apply_against_in_memory_sqlite` passes
- [ ] `HealthEndpointTests.Get_health_returns_200_with_status_ok` passes
- [ ] `.github/workflows/ci.yml` exists, parses as valid YAML, runs `setup-dotnet` for `10.0.x`, runs `dotnet tool restore`, `dotnet restore`, `dotnet format --verify-no-changes`, `dotnet build`, and `dotnet test`
- [ ] Branch `phase-0-skeleton` pushed to `origin`; PR open against `master` titled `Phase 0: Skeleton`
- [ ] No regressions: existing `Channels.razor`, `Topics.razor`, `MessageSidebar.razor`, the harvested CSS, and the two `/debug/...` endpoints continue to compile and behave as before

---

## COMPLETION CHECKLIST

- [ ] All tasks completed in order
- [ ] Each task validation passed immediately after the task ran
- [ ] All validation commands executed successfully:
  - [ ] Level 1: `dotnet format SkipWatch.slnx --verify-no-changes`
  - [ ] Level 2: `dotnet test SkipWatch.Tests/SkipWatch.Tests.csproj`
  - [ ] Level 3: covered by Level 2
  - [ ] Level 4: manual smoke script
- [ ] Full test suite passes (2 of 2 tests green)
- [ ] No linting errors
- [ ] No formatting errors
- [ ] All acceptance criteria met
- [ ] Code reviewed for quality and maintainability
- [ ] Branch pushed and PR opened (final milestone task)

---

## NOTES

**Assumptions resolved at plan time (no user clarification asked):**

1. **Default branch is `master`, not `main`.** Verified via `git remote -v` and `git status`. Every PR command and CI workflow filter uses `master`.
2. **Phase doc and other planning artifacts live inside the repo** under `docs/` (resolved against the repo root at `c:/Repos/Personal/SkipWatch/`). This was changed from an earlier external-to-repo layout via the `docs/move-docs-into-repo` branch so planning history versions alongside source history.
3. **`/health` registers inline in `Program.cs`** rather than being extracted to `Endpoints/HealthEndpoints.cs`. The PRD §9 tree marks `Endpoints/` as a future location, but a single one-line `MapGet` doesn't justify the new folder yet — it would mirror how the two existing `/debug/...` endpoints are inlined. If a third or fourth non-feature endpoint lands later, *that* is when extraction makes sense.
4. **`appsettings.Local.json` overlay (mentioned in PRD §6 Phase 7) is not introduced in Phase 0.** Phase 7 owns the Settings page that writes to it; adding the file plumbing now would be premature.
5. **FluentAssertions pinned to 6.12.\*.** Version 7.0+ changed licensing; staying on the last MIT-licensed line is the right default for a personal-use single-developer project until a deliberate upgrade decision lands.
6. **bUnit is added to the test csproj but no Razor component tests are written in Phase 0.** PRD §9 lists bUnit as a Phase 0 dependency; the actual Razor test surface lands in Phases 4–5.
7. **CI runner uses `dotnet-version: 10.0.x`.** If the GA SDK is not yet available on `ubuntu-latest`, add `include-prerelease: true` to the setup-dotnet step. Document the override in the PR description if the workflow fails on the first run.
8. **Phase 0 does not remove the two `/debug/...` endpoints.** They are scaffolding scheduled for removal in Phases 1 and 2 per PRD §6.
9. **Phase 0 does not fill in the `AddVideoFts` migration body.** That happens in Phase 6 (search). The empty `Up()`/`Down()` are tested by `SkipWatchDbContextSmokeTests` calling `Database.Migrate()` and confirming the migration applies as a no-op.

**Trade-offs:**
- Booting the full host inside `HealthEndpointTests` means the test creates real directories under `~/.skipwatch/`. This is acceptable at MVP scale; if it becomes a friction point a future test base class can `ConfigureAppConfiguration` to redirect `dataDir` to a per-test temp folder.
- `dotnet format` runs against `SkipWatch.slnx` (not individual csproj files) so the entire solution is formatted as one unit. This includes the new `SkipWatch.Tests` project — `.editorconfig` rules apply uniformly.
