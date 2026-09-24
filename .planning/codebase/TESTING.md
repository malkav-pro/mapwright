---
last_mapped_commit: ac4a7877683666650894b025737047a903c69740
last_mapped_at: 2026-09-22
---
# Testing Patterns

**Analysis Date:** 2026-09-22

## Test Framework

**Runner:**

- Framework-free .NET 8 console executable using C# 12: `tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj`.
- Six cases are registered as `(string Name, Func<Task> Run)` tuples in `tests/Mapwright.ContractTests/Program.cs`; each executes sequentially, exceptions become failures, and the process returns 0 only when all pass.
- Godot runtime probes are separately dispatched from `Scripts/App/Main.cs`; they are not cases in the contract executable.

**Assertion Library:** Local `True`, `Equal<T>`, `Throws<T>` and `ThrowsAsync<T>` helpers throwing `InvalidOperationException` in `tests/Mapwright.ContractTests/Program.cs`. No xUnit/NUnit/MSTest dependency is declared.

**Run Commands:**

```powershell
.\Scripts\run-contract-tests.ps1

# Equivalent once the .NET/NuGet environment is prepared:

dotnet run --project tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj

# Separate GPU/import/storage suite, requires an external .ink fixture:

.\Scripts\run-spike.ps1 -InkFixture 'C:\path\fixture.ink'

# Separate measurement probes:

.\Scripts\run-interactive-probe.ps1
.\Scripts\run-import-memory-probe.ps1
.\Scripts\run-terrain-export-16k.ps1
```

- Use `dotnet run`, not an assumed `dotnet test` discovery pipeline: the project is an executable (`tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj`).
- No dedicated watch or coverage command is configured in `Scripts/run-contract-tests.ps1`.
- Runners expect portable .NET 8.0.425, and GPU runners expect portable Godot 4.7.2 in `.tools/`. `Scripts/run-contract-tests.ps1` restores dependencies; GPU runners build with `--no-restore`.

## Test File Organization

**Location:** Standalone contracts under `tests/`; runtime probes live beside implementation under `Scripts/`.

**Naming:** Human-readable case labels plus PascalCase local functions in `tests/Mapwright.ContractTests/Program.cs`; `*Probe.cs` for runtime experiments.

**Structure:**

```text
tests/Mapwright.ContractTests/Program.cs         # Six contracts, fixture, assertions, fakes
Scripts/Rendering/GpuSeamProbe.cs               # Whole/tiled/oracle comparison
Scripts/Core/DurableEditRecoveryProbe.cs        # Forced termination and replay
Scripts/Export/TerrainPipelineExportProbe.cs    # Terrain export validation
Tools/StorageCrashWorker/Program.cs             # Isolated crash-test child
```

## Test Structure

**Suite Organization:** Actual pattern from `tests/Mapwright.ContractTests/Program.cs`:

```csharp
var tests = new (string Name, Func<Task> Run)[]
{
    ("revision conflicts are rejected", RevisionConflictsAreRejected),
    // Other explicitly registered cases follow the same shape.
};
```

**Patterns:**

- Arrange with `Fixture()`, invoke commands or an `EditSession`, and assert state plus invalidation geometry in `tests/Mapwright.ContractTests/Program.cs`.
- Use `await using` for sessions, unique temporary directories for SQLite integration, and `finally` cleanup of test-owned data in `SqliteCommitReopens` (`tests/Mapwright.ContractTests/Program.cs`).
- Assert causal order through an event list: `commit-start,commit-durable,render-enqueued,acknowledged` in the same file.

## Mocking

**Framework:** Handwritten interface fakes in `tests/Mapwright.ContractTests/Program.cs`.

**Patterns:**

```csharp
var events = new List<string>();
var repository = new RecordingRepository(events) { Fail = true };
var renderer = new RecordingRenderer(events);
await using var session = new EditSession(project, repository, renderer);
```

- `RecordingRepository` records durability events and optionally throws `IOException`; `RecordingRenderer` records queue submission; `FixedClock` supplies deterministic time (`tests/Mapwright.ContractTests/Program.cs`).

**What to Mock:** Repository and render-queue ports for ordering/failure isolation; time for deterministic persistence metadata (`src/Mapwright.Application/Ports.cs`, `tests/Mapwright.ContractTests/Program.cs`).

**What NOT to Mock:** Domain command transformations; real SQLite in the reopen case. GPU seam probes use actual renderers and compare outputs in `Scripts/Rendering/GpuSeamProbe.cs`.

## Fixtures and Factories

**Test Data:** `Fixture()` builds a 1024 x 1024 project at revision 7 with four layers, coastline reach 22, and a three-point river. `Brush` supplies a fixed seed and brush attributes in `tests/Mapwright.ContractTests/Program.cs`:

```csharp
static ResolvedBrush Brush(double radius) => new(
    new string('a', 64), radius, 0.8, 0.7, 0.5, 0.2, 0, 42, 1);
```

**Location:** Contract factories are local to `tests/Mapwright.ContractTests/Program.cs`; GPU fixtures are built in `Scripts/Rendering/GpuSeamProbe.cs`. Import probes depend on an external `.ink` file via `MAPWRIGHT_INK_FIXTURE` or a machine-specific fallback in `Scripts/App/Main.cs`; the full runner exposes `-InkFixture` in `Scripts/run-spike.ps1`.

## Coverage

**Requirements:** No code-coverage threshold, coverage collector, or coverage-report command detected in `tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj` and `Scripts/run-contract-tests.ps1`.

**View Coverage:** Not configured. Inspect explicit case registration in `tests/Mapwright.ContractTests/Program.cs` and probe orchestration in `Scripts/App/Main.cs` for functional scope.

- Contracts cover paint invalidation, moved-river invalidation, revision rejection, persist-before-render/acknowledgement, failed-commit state isolation, and SQLite reopen (`tests/Mapwright.ContractTests/Program.cs`).
- No registered contract directly covers `SetRiverWidth`, cancellation, concurrent execution, disposal races, renderer enqueue failure, or mismatched repository acknowledgements; relevant branches are in `src/Mapwright.Domain/Commands.cs` and `src/Mapwright.Application/EditSession.cs`.

## Test Types

**Unit Tests:** Domain geometry/revision and application ordering checks share the console runner (`tests/Mapwright.ContractTests/Program.cs`).

**Integration Tests:** Real SQLite persistence is covered by one reopen case. Runtime storage interruption/replay probes use child processes (`Scripts/Core/DurableEditRecoveryProbe.cs`, `Tools/StorageCrashWorker/Program.cs`).

**E2E Tests:** No general UI automation framework detected. `Scripts/App/Main.cs` orchestrates runtime integration probes; `docs/spike-report.md` explicitly says the connected four-layer editor workflow and sequence-correlated input-to-visible acceptance are unproven.

- Whole/tiled GPU results are compared against each other and a discrete oracle; zero boundary differences are tracked in `Scripts/Rendering/GpuSeamProbe.cs`.
- PNG export validation checks the file structure, CRCs and decompressed data through `Scripts/Export/PngValidator.cs`.
- `Scripts/run-tdr-probe.ps1` deliberately launches the device-loss experiment with a timeout and can terminate its child; it is not a routine contract test. Its launcher exits 0 after reporting, so inspect JSON/child status rather than interpreting launcher success as in-process recovery success.
- The interactive runner quits based on `Completed`, not establishment of a latency gate (`Scripts/App/Main.cs`); distinguish measurement completion from acceptance success.

## Common Patterns

**Async Testing:** Await session execution and check both acknowledged revision and published state in `tests/Mapwright.ContractTests/Program.cs`:

```csharp
var acknowledgement = await session.ExecuteAsync(command);
Equal(project.Revision + 1, acknowledgement.Revision);
Equal(project.Revision + 1, session.Current.Revision);
```

**Error Testing:** Verify exceptions and the absence of downstream effects (`tests/Mapwright.ContractTests/Program.cs`):

```csharp
await ThrowsAsync<IOException>(() => session.ExecuteAsync(command));
Equal(project.Revision, session.Current.Revision);
Equal("commit-start", string.Join(',', events));
```

## Evidence Status

- This map is based on source inspection only. No tests, builds, GPU probes or benchmarks were run while producing it; commands above are reproduction guidance from `Scripts/run-contract-tests.ps1` and the probe runners.
- `docs/spike-report.md` contains reported measurements and gate status for the tested Windows/AMD setup. Treat its results as documented evidence, not fresh verification by this mapping task.
- Rendering, import, export and storage probe evidence is written under `artifacts/spike/` by `Scripts/App/Main.cs`. Reported partial gates include connected editor responsiveness, visible undo, transparency ordering and Linux validation (`docs/spike-report.md`).

---

*Testing analysis: 2026-09-22*
