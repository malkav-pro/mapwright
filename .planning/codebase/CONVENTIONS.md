---
last_mapped_commit: ac4a7877683666650894b025737047a903c69740
last_mapped_at: 2026-09-22
---
# Coding Conventions

**Analysis Date:** 2026-09-22

## Naming Patterns

**Files:**

- Use PascalCase C# files named for their main responsibility, such as `src/Mapwright.Application/EditSession.cs` and `Scripts/Rendering/GpuJfaTerrainRenderer.cs`. Related records and helper classes can share that file.
- Use kebab-case PowerShell runners (`Scripts/run-contract-tests.ps1`) and snake_case compute shaders (`Shaders/terrain_sdf_jump.glsl`). Preserve directory casing for Linux compatibility.

**Functions:**

- Use PascalCase methods and an `Async` suffix for task-returning operations (`src/Mapwright.Infrastructure/SqliteProjectRepository.cs`). Preserve Godot override names such as `_Ready` in `Scripts/App/Main.cs`.

**Variables:**

- Use camelCase parameters/locals, `_camelCase` private fields, and PascalCase properties/constants; see `src/Mapwright.Application/EditSession.cs` and `Scripts/Rendering/GpuSeamProbe.cs`.
- Shader locals and bindings use snake_case; mirror `Shaders/terrain_sdf_jump.glsl` when extending shader code.

**Types:**

- Prefix interfaces with `I`; use sealed records for commands/results and readonly record structs for value geometry. Examples: `src/Mapwright.Application/Ports.cs`, `src/Mapwright.Domain/Commands.cs`, `Scripts/Core/EditingDocument.cs`.

## Code Style

**Formatting:**

- Follow the four-space indentation, Allman C# braces, file-scoped namespaces, target-typed `new`, and C# 12 collection expressions in `src/Mapwright.Domain/Commands.cs` and `tests/Mapwright.ContractTests/Program.cs`.
- Short guards and expression-bodied helpers are common; multiline arguments align under the containing call in `src/Mapwright.Application/EditSession.cs`.
- GLSL uses same-line braces and explicit buffer layouts in `Shaders/terrain_sdf_jump.glsl`.

**Linting:**

- No repository formatter, `.editorconfig`, custom analyzer configuration, or lint command detected outside bundled tooling. `Mapwright.csproj` and `tests/Mapwright.ContractTests/Mapwright.ContractTests.csproj` enable nullable references, implicit usings and C# 12.
- Do not assume formatting or warnings are enforced by CI; no project CI configuration was detected alongside `Mapwright.csproj`.

## Import Organization

**Order:**

1. File-level `using` directives precede a file-scoped namespace (`src/Mapwright.Infrastructure/SqliteProjectRepository.cs`).
2. System namespaces commonly precede project and external namespaces in the independent projects.
3. Ordering is not uniform: `Scripts/App/Main.cs` places Godot/project namespaces before explicit System imports. Follow the surrounding file rather than claiming an enforced ordering rule.

**Path Aliases:**

- No C# alias scheme detected. Use project references from `Mapwright.csproj` and namespace-qualified imports. Godot resources use `res://Shaders/...` in `Scripts/Rendering/GpuHaloTerrainRenderer.cs`.

## Error Handling

**Patterns:**

- Throw precise exceptions for violated invariants: revision conflicts use `RevisionConflictException`; bad arguments use argument exceptions in `src/Mapwright.Domain/Commands.cs`.
- Let application/storage failures propagate to callers. Release synchronization in `finally`, and use `using`/`await using` for disposable resources (`src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`).
- Probe boundaries convert exceptions into result records with failure details (`Scripts/Core/DurableEditRecoveryProbe.cs`, `Scripts/Export/PngValidator.cs`). UI boundaries report exceptions through `GD.PrintErr` and status text (`Scripts/App/Main.cs`).
- PowerShell runners use `$ErrorActionPreference = "Stop"`, explicit `$LASTEXITCODE` checks, and `Push-Location`/`Pop-Location` protected by `finally` (`Scripts/run-contract-tests.ps1`).

## Logging

**Framework:** Godot `GD.Print`/`GD.PrintErr` in `Scripts/App/Main.cs`; console output in `tests/Mapwright.ContractTests/Program.cs`.

**Patterns:**

- Emit machine-readable probe records with `System.Text.Json` and indented JSON into `artifacts/spike/`; orchestration is in `Scripts/App/Main.cs`.
- Keep contract-runner output to `PASS`/`FAIL` lines and a process exit code (`tests/Mapwright.ContractTests/Program.cs`).
- Persist storage JSON using the repository's explicit `JsonSerializerDefaults.Web` options; do not reuse display-report defaults blindly (`src/Mapwright.Infrastructure/SqliteProjectRepository.cs`).

## Comments

**When to Comment:**

- Sampled application/domain code relies on clear names and explicit guards more than extensive inline commentary (`src/Mapwright.Application/EditSession.cs`, `src/Mapwright.Domain/Commands.cs`). Explain nonobvious numerical/storage constraints when extending these areas.

**JSDoc/TSDoc:** Not applicable to this C# project; no XML-documentation requirement is configured in `Mapwright.csproj`.

## Function Design

**Size:** No enforced limit detected. Keep domain transformations focused as in `src/Mapwright.Domain/Commands.cs`; `Scripts/App/Main.cs` contains larger orchestration routines.

**Parameters:** Use typed IDs and immutable input records in `src/Mapwright.Domain/Commands.cs`. Pass optional `CancellationToken` parameters through asynchronous application/infrastructure calls; infrastructure uses `ConfigureAwait(false)` in `src/Mapwright.Infrastructure/SqliteProjectRepository.cs`.

**Return Values:** Return immutable change/acknowledgement records at the domain/application boundary (`src/Mapwright.Application/Ports.cs`). Probe records include status, metrics and detail (`Scripts/Rendering/GpuSeamProbe.cs`).

## Module Design

**Exports:** Public sealed classes/records expose behavior; internal helpers stay internal, as with `CommandGuard` in `src/Mapwright.Domain/Commands.cs`.

**Barrel Files:** Not applicable. Interfaces and related records are grouped in `src/Mapwright.Application/Ports.cs`; assembly boundaries are established by project references in `Mapwright.csproj`.

- Keep engine-independent domain/application code under `src/`; keep Godot adapters and probes under `Scripts/`. `Mapwright.csproj` excludes `src/**/*.cs`, `tests/**/*.cs` and `Tools/**/*.cs` from direct compilation and references the three library projects.
- Distinguish the mutable spike command model in `Scripts/Core/EditingDocument.cs` from immutable domain commands in `src/Mapwright.Domain/Commands.cs`. Both define similarly named abstractions; extend the intended namespace rather than mixing their state models.
- Quality mapping is source inspection only; no build, formatter, test or benchmark was executed for this document. Evidence paths include `Mapwright.csproj` and `tests/Mapwright.ContractTests/Program.cs`.

---

*Convention analysis: 2026-09-22*
