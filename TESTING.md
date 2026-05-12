# Testing — Antiphon

## Running tests

Antiphon uses **TUnit** (not xUnit). The TUnit MTP runner is incompatible with VSTest on .NET 10 SDK.

**Use `dotnet run`, not `dotnet test`:**

```bash
dotnet run --project tests/Antiphon.Tests
dotnet run --project tests/Antiphon.Agents.Pty.Tests
dotnet run --project tests/Antiphon.E2E
```

## Filtering tests

TUnit uses `--treenode-filter`, not `--filter`:

```bash
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AnsiStripperTests/*"
```

## Test projects

| Project | Type | Notes |
|---------|------|-------|
| `tests/Antiphon.Tests` | Unit + integration | Use `--no-build` after first build to avoid DLL locks |
| `tests/Antiphon.Agents.Pty.Tests` | Headed PTY tests | Requires `ANTIPHON_HEADED_TESTS=1` env var |
| `tests/Antiphon.E2E` | End-to-end | Requires stack running |

## Headed PTY tests

Tests that invoke real Claude sessions are gated by an environment variable:

```powershell
$env:ANTIPHON_HEADED_TESTS = "1"
dotnet run --project tests/Antiphon.Agents.Pty.Tests
```

- Without the env var, headed tests auto-skip.
- Headed tests must be in `[NotInParallel("Headed")]` group — running in parallel causes Claude API quota contention and flaky quiet-period detection.
- `claude.exe` is at `C:\Users\lndco\.local\bin\claude.exe` (on PATH).

## Gotchas

- **`dotnet test` does not work** — TUnit's MTP runner conflicts with Microsoft.NET.Test.Sdk (VSTest) on .NET 10 SDK. Always use `dotnet run`.
- **`--filter` is wrong** — TUnit uses `--treenode-filter`. Using `--filter` silently runs all tests or fails.
- **Aspire DCP port conflict**: If Postgres auth fails but the container is healthy, check for orphaned `dcpctrl.exe` from a different Aspire project owning port 5432. Fix: `taskkill /F /PID <pid>`.
- **PowerShell env vars invisible to Bash**: Set `ANTIPHON_HEADED_TESTS=1` via PowerShell and run tests via PowerShell — if you switch to the Bash tool, it won't see the env var.
- **ESC/BEL in test constants**: Literal escape bytes in ANSI test strings must be `\x1b` / `\x07` — they are lost during copy-paste or file migration.

## Antiphon-tests project (C--src-antiphon-tests-*)

The `C--src-antiphon-tests-*` folder in `.claude/projects/` is **not a real project**. It accumulates Claude Code sessions spawned by the PTY test suite itself (each test opens a headless Claude instance). These sessions contain only system prompts like "Run systeminfo" or "Remember 7919" — they are test artefacts, not user conversations.
