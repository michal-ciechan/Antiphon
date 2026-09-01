# CARD-0307 — `test-client.ps1` filter must reach vitest as real argv

**Date:** 2026-09-01 (Plan pass, task 8ff59413 — design only; no code changed)
**Card:** CARD-0307 "`test-client.ps1`'s filter silently no-ops (`npx.ps1` rewrites args from source text)"
**Diagnosis:** done on the card (task `a59a2f0c`, four invocation shapes). This pass confirmed the Node installer shim.

**Sources (verified this pass):** CARD-0307, `scripts/test-client.ps1:30`, `C:\Program Files\nodejs\npx.ps1:25-46` (this machine; `Get-Command npx` → `npx.ps1`), `client/package.json` `"test": "vitest run"` / vitest bin `./vitest.mjs`, `scripts/test-hooks.ps1` (sibling CARD-0069 contract), `scripts/nightly-tests.ps1:344` (calls the wrapper with no filter), Gotcha #18/#19/#74 in `docs/testing-and-build.md`, CARD-0069 plan.

---

## Decision

Stop calling `npx` (the PowerShell shim). Run the **local** vitest binary with `node`, so `@args` is native argv:

```
node "$clientDir\node_modules\vitest\vitest.mjs" run @args
```

Keep the CARD-0069 contract byte-identical: `2>&1 | Tee-Object -FilePath $logFile`, then `CLIENT TESTS EXIT CODE: n (PASS|FAIL …)` and `exit $code`.

The card's two alternatives are not equal:

| Option | Verdict |
|---|---|
| `& npx.cmd vitest run @args` | Works (measured on the card). Still PATH- and installer-layout-dependent; `npm.ps1` has the same Statement trap if someone "fixes" it to `npm exec`. |
| `npm exec -- vitest run -- @args` | Same shim class if `npm` resolves to `npm.ps1`. |
| `node client/node_modules/vitest/vitest.mjs run @args` | **Primary.** Native `node.exe` receives bound splat. Matches `package.json` `"bin": { "vitest": "./vitest.mjs" }`. No npx. ASCII, PS 5.1-safe. |

Do **not** raise execute-time estimates to absorb a full-suite vitest run. The 61m CARD-0239 execute was this bug, not AttentionService cost.

---

## Ground truth

`npx.ps1` (Node's Windows shim, this machine `C:\Program Files\nodejs\npx.ps1`):

- Pipeline or `pwsh -File` invoking **npx.ps1 itself**: uses bound `$args` (`:25-28`).
- Else (a **line inside a script**, which is how `test-client.ps1` calls it): rebuilds argv from `$MyInvocation.Statement` — the **source text** of that line (`:30-45`), then `Invoke-Expression`.

`test-client.ps1:30` is literally `npx vitest run @args 2>&1 | Tee-Object ...`. Statement contains the token `@args`, not the caller's filter. Node sees `vitest run` with no filter. A literal `npx vitest list attentionVisuals` in source works; splat via the shim never does. Card table, four rows, stands.

`$LASTEXITCODE` after `Tee-Object` is still the native command's — CARD-0069 measured that; do not wrap in `cmd /c`.

---

## Slices

### S1 — Invoke local vitest via `node`

**File:** `scripts/test-client.ps1`

After `Push-Location $clientDir`:

1. `$vitest = Join-Path $clientDir 'node_modules\vitest\vitest.mjs'` (forward or backslash; `Join-Path` is fine). If missing: write the EXIT CODE line as `1` with `vitest not installed; run npm ci in client/`, `exit 1`. Do not fall back to `npx`.
2. `node $vitest run @args 2>&1 | Tee-Object -FilePath $logFile` then `$code = $LASTEXITCODE` — same pipeline as today.
3. Comment **why** this is not `npx`: cite `npx.ps1` Statement reconstruction; a future "simplify to npx" reopens the silent full-suite run.
4. Header usage comment stays (`pwsh -File scripts/test-client.ps1 BoardPage`).

No `param()` block required; automatic `$args` is the documented passthrough. Do not add `-WhatIf`.

`nightly-tests.ps1` already calls the wrapper with no extra args — full suite, unchanged.

### S2 — Smoke: a filter runs one file

Two pins, both required:

**Execute-time (the card's proof):**

```powershell
pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test
```

Expect: `Test Files  1 passed (1)` (or `1 failed` if that file is red — still **one** file), ~9 tests, `CLIENT TESTS EXIT CODE:` present, wall-clock seconds not minutes. Filter `attentionVisuals.test` (the file `client/src/features/attention/attentionVisuals.test.ts`) so it cannot match other files that only import the module.

**Automated:** `tests/Antiphon.Tests/Scripts/TestClientFilterTests.cs` (new). `[ParallelLimiter<ProcessSpawnLimit>]`. Skip if `client/node_modules/vitest/vitest.mjs` is absent. Start `pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test`, timeout ~60 s. Assert:

- log or stdout contains `CLIENT TESTS EXIT CODE:`
- vitest summary shows **one** test file (`Test Files  1 ` — space after 1, vitest's shape)
- does **not** contain a second file basename from a known neighbour (`AttentionPanel.test` is the trap if the filter were ignored)

Do not parse total test count as the suite size. Do not run the unfiltered wrapper in CI (5–7 minutes).

### S3 — Docs: filter actually works; namespace-wide verify is not the default

**Gotcha #19** (`docs/testing-and-build.md`): add that `test-client.ps1 <filter>` **does** pass through (CARD-0307; never write `npx vitest` in that script). Isolation re-run stays `pwsh -File scripts/test-client.ps1 <File>`.

**Gotcha #18:** after "Filter by `--treenode-filter`", add: a plan's verify step names the **class(es) touched** (`--treenode-filter "/*/*/AttentionServiceTests/*"`). The namespace-wide form (`/*/Antiphon.Tests.Application/*/*`) is for a genuinely cross-cutting change or for chunking a full Application run of the ~12-minute suite — not the default for one service + its test file. CARD-0239's 2349-test Application filter (and the CARD-0297 red it then had to triage) is the evidence. Gotcha #74 stays (full-suite quiet phase); it does not license namespace-wide verify on a narrow card.

No new convention file. No execute-estimate table change.

---

## What this card does not do

- Changing vitest config, workers, or timeouts.
- Teaching nightly to pass a filter.
- Fixing `npm.ps1` / other scripts that call `npx` (only this wrapper is the documented client runner).
- Making `SettleAsync` or other harness DI gaps cheaper to ignore.

---

## Test matrix

| Layer | Test |
|---|---|
| Script | S1: `pwsh -File scripts/test-client.ps1 attentionVisuals.test` → one file, EXIT CODE line |
| Script | no args still runs the full suite (nightly contract) — execute once, or trust nightly; do not add a 7-minute automated test |
| Application | `TestClientFilterTests` as S2 |
| Missing vitest | wrapper prints EXIT CODE 1 naming `npm ci` (can be a unit-ish check by pointing at a temp copy, optional; execute-time is enough if the file-exists branch is trivial) |

```powershell
pwsh -NoProfile -File scripts/test-client.ps1 attentionVisuals.test
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0307/ -- --treenode-filter "/*/*/TestClientFilterTests/*"
```

Forward slash on OutputPath. Delete `bin-card0307*` after.

---

## Sequencing and risks

**Order: S1, then S2 execute-time smoke (red without S1), then the C# pin, then S3.**

| Risk | Disposition |
|---|---|
| `node` not on PATH | Same as today (`npx` needed node). Fail with a clear EXIT CODE 1 |
| `node_modules` missing | Explicit `npm ci` message; skip C# test |
| Vitest summary wording drifts (`Test Files  1 passed`) | Match `Test Files  1 ` plus absence of `AttentionPanel.test`; if vitest 5 rewords, the execute smoke will show it |
| PS 5.1 `$LASTEXITCODE` after `Tee-Object` | Unchanged pipeline; CARD-0069 |
| Someone "simplifies" back to `npx` | Comment in the script + C# pin goes red (full suite would mention `AttentionPanel`) |
| `attentionVisuals.test` filter matches two files later | Tighten to the path `src/features/attention/attentionVisuals.test.ts` in the smoke if that happens |

---

## Execution notes

- Do not treat a full-suite vitest run as proof. The bug's passing output **is** the full suite.
- After S1, the CARD-0239-style verify is `pwsh -File scripts/test-client.ps1 attentionVisuals.test` plus `--treenode-filter "/*/*/AttentionServiceTests/*"` — seconds, not an Application namespace.
