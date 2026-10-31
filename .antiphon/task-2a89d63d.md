# CARD-1545 Final review B (task 2a89d63d): S1a census and S1c sanitization

Reviewed on server2/Linux. Read-only: no subject source edited, nothing pushed except this report.

- S1a: `feat/card-task-041adc14` tip `9a05dbfcd4b49fdbed098a520b28c7e7d91cc46c` (owner task 041adc14)
- SAN: `feat/card-task-3a7c132f` tip `5d719f5f080efca7d321f881af06941225eca0ed` (code `da4ad0890bfb6dc0c1b57809abe755f2ddf56a79`, owner task 3a7c132f)
- S1c chain tip `2c550140e8d1f96f50dfa2080461150a966e906b`; origin/master `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`

Verdict: **defects found in both slices.** SAN: D-1, D-2, D-3. S1a: D-4, D-5, D-6. Next: code. ordinaryScopeCompleted: Interim (see Scope).

## Checkpoints (fresh reviewer runs, checkpoint tool built to bin-rv2a89, UseAppHost=false, TEMP=/tmp, fresh output names)

SAN, through the plan's RunPortableRows wrapper logic: copy at `scratchpad/rv-run-portable.ps1`, identical except slotWait 70 (was 25), totals 130/90 m and run key `s1c-ev-rv*`. Changed because the first attempt, run `20261031-160836-4670`, hit `CP-20 slot-timeout` at the wrapper's 25 min slot wait on saturated server2 (4/4) and was stopped.

```
CHECKPOINT CP-20 commit=5d719f5f080efca7d321f881af06941225eca0ed build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-2a89d63d/.antiphon/checkpoints/20261031-164127-301f/rows/CP-20/run.trx slot=granted waited=180s dirty=0 source=5d719f5f080efca7d321f881af06941225eca0ed sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=5d719f5f080efca7d321f881af06941225eca0ed build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneEvidenceTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-2a89d63d/.antiphon/checkpoints/20261031-164127-301f/rows/CP-26/run.trx slot=granted waited=375s dirty=0 source=5d719f5f080efca7d321f881af06941225eca0ed sourceState=clean buildSource=verified
CHECKPOINT CP-29 commit=5d719f5f080efca7d321f881af06941225eca0ed build=n/a filter=pwsh -NoProfile -File scripts/test-live-lane-evidence.ps1 -Case CompletedRows executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=270s dirty=0 source=5d719f5f080efca7d321f881af06941225eca0ed sourceState=clean buildSource=notApplicable
```
Runs `20261031-164127-301f` and `20261031-172238-e5e0`: wait exit 0 each, unlisted none. OuterPublish `publicationCount=1`, `EVIDENCE-SCAN clean`. Receipt and public summary 31 markers each.

S1a, through the worker's `-Case Group` logic: context written by the script's own `Write-LiveLaneGroupContext`, seed `556a20e65ca59c04b205e6e8dfe2f6ae1d0e1dad` (producing source = its parent 11c05c072), one run per row, `--total-timeout 90m`.

```
CHECKPOINT CP-25 commit=9a05dbfcd4b49fdbed098a520b28c7e7d91cc46c build=ok filter=/*/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests/ExactPtyCoreRoster executed=1 passed=1 failed=0 skipped=0 ... slot=granted waited=1500s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=9a05dbfcd4b49fdbed098a520b28c7e7d91cc46c build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneInventoryTests/* executed=2 passed=1 failed=1 skipped=0 ... slot=granted waited=510s dirty=0 sourceState=clean buildSource=verified
```
CP-1 is **red**: ExactCoreRoster `exit=137`, child killed at 150 s (D-6); EveryLiveGateHasDisposition passed. CP-30 did not run as a row because the driver stops on red. I ran its exact command afterwards, unlisted, inside one build-slot lease, as the D-4 demonstration and mutant baseline: `accepted`, exit 0, 55 s. The isolated ExactCoreRoster rerun (unlisted, flake check, same kept build) passed 1/1 in 124 s. Fresh CP-25 export: run `1f41a4b284364a319c8bc3b3dc283f58`, 781 nodes. CP-1 export: 20580 nodes.

Evidence is kept under `/work/worktrees/task-2a89d63d/.antiphon/checkpoints/` (SAN) and `.antiphon/rv2a89-s1a-evidence/` (S1a copy). Both are gitignored.

## Defects

### D-1 (SAN, high): the V-6 and V-7 call-site audits miss bare raw sinks

- Where: `tests/Antiphon.Agents.Pty.Tests/LiveClaudeEvidenceWiringTests.cs:242-267` (`UnsafeText` only flags a SnapshotScreen value inside `+`/interpolation or as a Shouldly receiver; `Tainted` only tracks locals initialised from `SnapshotScreen`). `tests/Antiphon.Tests/Scripts/LiveLaneEvidenceTests.cs:372-376` (`RawSinks` is a substring count of `File.WriteAllText(`, `File.AppendAllText(`, `Console.WriteLine` and the literal `screen:\n`).
- Failure (run, reviewer mutants in a scratch worktree, real CP-20 test dll, source read at runtime): inserting into `Control_auto_connects_a_bridge_session_before_the_first_user`
  - `Console.WriteLine(runner.SnapshotScreen());` -> `CanarySinksCannotBypassSanitizer` **passed** (1/1)
  - `throw new SkipTestException(runner.SnapshotScreen());` -> **passed**
  - `Console.WriteLine("cwd=" + Environment.GetEnvironmentVariable("HOME"));` -> **passed**
  - control `Console.WriteLine("SCREEN:\n" + runner.SnapshotScreen());` -> red `C1545 V-6/raw-console`
  - C680 (static replay of `RawSinks` on a mutated watchdog): `Console.Out.WriteLine(after.RenderedScreen)`, `Console.Error.WriteLine(...)`, `throw new SkipTestException(after.RenderedScreen)`, `after.RenderedScreen.ShouldContain("x")`, `ShouldBeTrue($"screen {after.RenderedScreen}")`, `File.WriteAllBytes(...)` all count 0, so `C680EvidenceIsSanitized` stays green. I confirmed this with the real CP-26 test dll, copied into the mutant worktree so its repo-root lookup read the mutated source. `Console.Out.WriteLine(snapshot.RenderedScreen)` and `throw new SkipTestException(snapshot.RenderedScreen)` both left `C680EvidenceIsSanitized` **passed**. The control `Console.WriteLine("SCREEN:\n" + tool)` went red with `C1545 V-7/raw-file`. Restored: green.
- Why: the test design requires "bounded, method-aware token/AST checks, not whole-file hashes or substring presence" where a "raw string interpolation or Shouldly's implicit rendering of a raw actual value fails", and the plan says "a raw SnapshotScreen interpolation at the canary call site fails". PC-38..42 only insert the concatenated forms the audit already recognises. The most direct raw print of a screen passes. Exception text from reachable helpers is not audited either: `ClaudeRemoteControlAtStartupCanaryTests.cs:174` `File.ReadAllText(file)` on the profile's `settings.json` throws `FileNotFoundException` with the full home-directory path into TRX ErrorInfo. The plan names profile paths as removable.
- Fix: taint every argument whose syntax reaches a screen, transcript, profile path or exception message, not only `+`/interpolation. Treat any non-constant, non-`LiveClaudeEvidence.*` argument to Console.*/TextWriter, SkipTestException or a Should* message as a raw sink. Replace the C680 substring count with the same AST audit. Add PCs for the bare forms above and for `Console.Out`/`Console.Error`.

### D-2 (SAN, medium): the projection emits credential-shaped values verbatim and no witness covers that path

- Where: `tests/Shared/LiveClaude/LiveClaudeEvidenceSanitizer.cs:41-55` and `:60-69`. `Fields` values, `RunId`, `CaseId`, `TestId` and `Kind` are emitted raw whenever they are "tokens": at most 80 characters from `[A-Za-z0-9._:-]`.
- Failure (Add-Type probe of the committed file): `Fields{cli=<JWT eyJ...>, backend=ghp_<36>, code=oauthcode-...}` printed all three values verbatim; `RunId = <JWT>` gave `run=eyJ...`; `CaseId = ghp_...` gave `case=ghp_...`; an 73-char `sk-ant-api03-...` RunId was printed. Paths, emails and `Bearer x` were dropped. Mutants: removing the Fields value check (SM-S4), the identity check (SM-S5) or the 80-char bound (SM-S6) each left `EvidenceIsSanitized` **green**. No witness sends a sentinel through Fields, the identity fields or a long value.
- Why: the plan says "Remove ... credential-like values; unknown raw text is suppressed". The report says "drops ... token-shaped values", but the allowlist is exactly the credential shape. Today's callers pass benign values: cli version, enum, tool, int. The guard is still a fail-open the next caller inherits, and nothing would go red.
- Fix: put `Fields` on a closed key allowlist with per-key value grammars (enum, integer, version), and refuse credential prefixes (`eyJ`, `ghp_`/`gho_`/`github_pat_`, `sk-`, long base64/hex runs) in every emitted token, RunId included. Add sentinel witnesses through Fields, RunId and CaseId so SM-S4/5/6 go red.

### D-3 (SAN, medium): the CP-29 scanner's trust inputs come from the mutable context file, and it ignores file names

- Where: `scripts/lib/live-lane-evidence.ps1:176-183` (`Get-Needles`: context `sentinels` replaces the built-in needles). `:448-450` (`OuterPublish` uses context-supplied `producers` instead of reading state.json). `:249-253` (only file contents are scanned; zero-length files are skipped).
- Failure (real worker artifacts from run `20261031-125359-1559`, copied to scratch, `-Case CompletedRows`/`OuterPublish`):
  - context `sentinels:["NOPE-XYZ"]` + `SENTINEL-TOKEN-3a7c132f` appended to CP-20 console.log -> `EVIDENCE-SCAN clean`, exit 0
  - context `producers` pointing at a clean directory while the real CP-20 console.log holds the sentinel -> `publicationCount=1`, `EVIDENCE-SCAN clean`
  - a file named `SENTINEL-TOKEN-3a7c132f.txt` (or a zero-length file of that name) under rows/CP-20 -> clean
  - controls: sentinel in HTML, in an ANSI-split console line or as an XML-entity in the nested timestamp HTML -> `sentinel-present`
- Why: `ANTIPHON_LIVE_EVIDENCE_CONTEXT` selects that file, so whoever controls it can disable the scan. File names are a sink the brief names.
- Fix: union context sentinels with the fixed set, never replace it. In OuterPublish/CompletedRows, always derive producers from the bound run's state.json; keep context producers for the Scan fixture case only. Scan relative paths and names, including zero-length files.

### D-4 (S1a, medium): CP-30 credits a producer export whose row may have gone red

- Where: `scripts/test-live-lane-manifest.ps1:81-97` writes the CP-25/CP-1 export, with `terminal = $true` hard-coded at `scripts/lib/live-lane-manifest.ps1:738`. It does this **before** `Invoke-LiveLaneRosterMatrix` runs and before the TUnit row reaches a terminal state. `Invoke-LiveLaneCensus` (`:2229-2245`) checks only the export's own fields. No code reads the checkpoint `state.json`/`report.json` (grep: none in the library).
- Failure (it happened in this review): CP-1 run `20261031-163848-c9eb` went red because ExactCoreRoster's child was killed (D-6). The child had already written `.antiphon/live-lane-capture/cp1-export.json` at 17:06 with `cp=CP-1 source=9a05dbfcd run=1f41a4b2... terminal=true nodes=20580`. The CP-30 command run under that same context then gave `accepted`, exit 0, in 55 s (`pwsh scripts/test-live-lane-manifest.ps1 -Case FullCensusUnchanged`, one build-slot lease): CP-30 credited the red CP-1. The `-Case Group` driver's stop-on-nonzero sequencing is the only thing that keeps CP-30 from running here; the census itself does not.
- Why: the test design says a "cut ... between export and terminal state ... gives no preservation credit" and "the wrapper validates terminal receipt before freezing". Here terminal state is self-asserted by the producer.
- Fix: write the export as non-terminal, or into a pending path. Have CP-30 require the bound checkpoint run's state.json rows CP-25 and CP-1 `green`, with matching commit and `buildSource=verified`. Add a negative case: an export present while its row is red or running.

### D-5 (S1a, low): census and Group write outside the declared evidence directory and never clean up

- Where: `scripts/test-live-lane-manifest.ps1:134` (12 MB fixture copy `antiphon-live-lane-fixture-<run>.json` in `GetTempPath()`). `scripts/lib/live-lane-manifest.ps1:2113` (`antiphon-live-lane-<PID>/before-*`), `:2416` (three `antiphon-live-lane-policy-<guid>` per census), `:2526` (`antiphon-live-lane-reader-<guid>`). Only the gate scratch at `:1231` is removed. `:2410` also leaves a dangling `commit-tree` object in the repository.
- Failure: server2 `/tmp` already held 22 such entries, 119 MB, from the worker's runs before this review added its own. Each CP-30 adds about 25-40 MB.
- Why: the brief requires nothing written outside the declared evidence directory. The shared runner `/tmp` grows without bound.
- Fix: put the scratch under the run's owned capture directory (`.antiphon/live-lane-capture/<run>/`) and remove it in `finally`.

### D-6 (S1a, medium): ExactCoreRoster's 150 s hard child budget kills ordinary discovery on a loaded host

- Where: `tests/Antiphon.Tests/Scripts/LiveLaneInventoryTests.cs:64` (`WaitForExit(150_000)`, then `Kill(entireProcessTree)`); same at `tests/Antiphon.Agents.Pty.Tests/LiveClaudeInventoryTests.cs:53`.
- Failure: this review's official CP-1 (`20261031-163848-c9eb`, source 9a05dbfcd, build verified) was `executed=2 passed=1 failed=1`. The message was `ShouldAssertException: parsed should not be null ... C1545 ExactCoreRoster result json exit=137 stderr= stdout=`: the pwsh discovery and roster child was SIGKILLed at 150 s, after discovery and export finished (discovery doc 17:05, export 17:06). The worker's own ExactCoreRoster durations were 88.1/89.5/91.1 s, so 150 s is about 1.65x measured, under the 2x-p95 standard. Server2 was saturated (4/4 slots). An isolated rerun of the same kept build passed: 1/1, 124 s wall for the dll run including the child. So this is a load-sensitive budget, not a logic error, and 124 s is already 83% of the 150 s budget.
- Why: CP-1 is an ordinary `Category("Unit")` test that will run in every Unit lane on loaded hosts. A kill with empty stdout gives no phase evidence, against docs/test-performance-standards.md (budget near 2x p95, record phases).
- Fix: derive the budget from measured p95 (at least about 200 s, or the row's own deadline), report the phase (discovery, export, matrix) and elapsed time on timeout, and keep output on kill.

## Advisories (not defects)

- A-1 S1a `StableArg` (`live-lane-manifest.ps1` engine, commit 9a05dbfcd) normalises every 32-hex or GUID-shaped run in **every** node's argument key. It does not limit this to the C672 hold-sentence node, so a real change between two GUID-valued `[Arguments]` passes as unchanged. Scope it to the named class/method.
- A-2 SAN `Test-PublicationGate` refuses only rawExit 1/2/3/8. Any other nonzero should refuse. The PC-69 fault guards are self-fulfilling: the scanner returns `write-fault` because the context says `fault=...`, so a real swallowed write error is not covered.
- A-3 SAN malformed TRX gives an uncaught `LoadXml` exception with exit 1, not `2 INVALID/evidence-sanitization`. It still fails closed.
- A-4 SAN the scan covers rows/CP-20 and rows/CP-26 only. builds/*/build.log, report.md, executor.log and state.json are not scanned (sentinel appended there: clean).
- A-5 SAN `cli` (from `claude --version`, e.g. `2.x (Claude Code)`) is not a token and is silently dropped from the C680 native evidence.
- A-6 CP-29 harness fix: `scripts/test-live-lane-evidence.ps1` (harness, not product) no longer captures `dotnet ... start` stdout, which the detached executor inherited. The checkpoint tool and land code are unchanged in this slice, so it cannot hang a land. The wrapper's hard-coded `slotWaitMinutes: 25` slot-timed-out CP-20 on loaded server2 in this review, so a reviewer run needed 70.
- A-7 S1a CP-30 is pinned to pre-S1 `1905f840d` and seed `556a20e65`, and master changed `tests/test-execution-policy.json` after cbe3e6b42. The owed SourceLanding CP-30 PCs must use the reviewed pre-land pack, not the landed HEAD, or the census will refuse on policy/reader drift.
- A-8 Culture commit ce2285e4e only adds `using System.Globalization;` so the in-process Roslyn mutant compile (no implicit usings) resolves `CultureInfo`. Counts are invariant. Correct.

## Reviewer mutants (red, then restored green; exact-string, one build-slot lease per batch, scratch worktrees only)

SAN (real CP-20 Pty dll built to `bin-rv2a89m`; C680 via the kept CP-26 dll):

| ID | Mutation | Result |
|---|---|---|
| SM-S0 | concat `Console.WriteLine("SCREEN:\n" + runner.SnapshotScreen())` in Control_auto_connects | red `V-6/raw-console` (control) |
| SM-S1 | bare `Console.WriteLine(runner.SnapshotScreen())` | **survived** (D-1) |
| SM-S2 | bare `throw new SkipTestException(runner.SnapshotScreen())` | **survived** (D-1) |
| SM-S2b | `Console.WriteLine("cwd=" + HOME)` | **survived** (D-1) |
| SM-S3 | `Append(capture.Screen)` instead of Classify | red `V-5 payloadHasSentinel` |
| SM-S4 | drop the Fields value token check | **survived** (D-2) |
| SM-S5 | drop the CaseId/TestId/Kind token check | **survived** (D-2) |
| SM-S6 | drop the 80-char token bound | **survived** (D-2) |
| SM-S7 | `Append(capture.Link)` instead of Classify | red `V-5 payloadHasSentinel` |
| SM-C0 | C680 watchdog `Console.WriteLine("SCREEN:\n" + tool)` | red `V-7/raw-file` (control) |
| SM-C1 | C680 `Console.Out.WriteLine(snapshot.RenderedScreen)` | **survived** (D-1) |
| SM-C2 | C680 `throw new SkipTestException(snapshot.RenderedScreen)` | **survived** (D-1) |

Restored: EvidenceIsSanitized 1/1, CanarySinksCannotBypassSanitizer 1/1, C680EvidenceIsSanitized 1/1. Scanner probes P1/P2/P8/P10 survived (D-3); P3/P4/P5 refused.

S1a (`scripts/test-live-lane-manifest.ps1 -Case FullCensusUnchanged` on the reviewer context, library mutated in a scratch worktree):

| ID | Mutation | Result |
|---|---|---|
| SM-A1 | disable `seed-not-ancestor` | red `expected-refusal-missing seed-nonancestor` |
| SM-A2 | disable per-project `producer-provenance` | red `expected-refusal-missing producer-provenance` |
| SM-A3b | `undeclared-new-uid` never added | red `expected-refusal-missing unknown-new-uid` |
| SM-A4 | ignore removed categories | red `expected-refusal-missing remove-Integration` |
| SM-A5 | accept non-terminal export | red `expected-refusal-missing pending-producer` |
| SM-A6 | disable `seed-range` | red `expected-refusal-missing seed-unlisted-edit` |
| SM-A7b | `ExpansionDiffers` never added | red `expected-refusal-missing extra-expansion` |

Restored: `accepted` (exit 0) twice. The first A3/A7 attempt used `if (false)`, which Add-Type refused as a compile warning; I discarded those and replaced them with A3b/A7b. The census refusal set is well guarded. Its gap is the producer-state binding (D-4), which no internal control covers.

## Land graph, evidence diff, merge-tree

- Land graph `-Check` (no `-Update`), evaluator `tools/Antiphon.LandGraphAudit` built Release to a fresh output:
  - S1a `9a05dbfcd`: `digest=7BDABBA0... count=68` (= master), evaluation `approved`, exit 0.
  - SAN `5d719f5f0`: pre-check refuses on `digest=BAA62938B0DA86DC... count=68` only. Evaluator run directly: `approved`, exit 0; canary removed.
  - Chain tip `2c550140e`: `digest=9D7BBFDE28CEF569... count=68` (the brief's figure). SAN adds `Microsoft.CodeAnalysis.CSharp` to `tests/Antiphon.Tests/Antiphon.Tests.csproj`, giving **BAA62938**.
  - Merged tree (chain+SAN+S1a): `digest=BAA62938... count=68`, evaluator `approved`, exit 0. The Merge's `-Update` must record BAA62938, not 9D7BBFDE.
- `scripts/check-evidence-diff.ps1`: S1a `4e1e6106d..9a05dbfcd` commits=11 violations=0; `cbe3e6b42..9a05dbfcd` commits=26 entries=3 violations=0. SAN `c7a7b2c9e..5d719f5f0` commits=5 entries=1 violations=0; `cbe3e6b42..5d719f5f0` commits=53 entries=8 violations=0.
- `git merge-tree --write-tree` onto origin/master `2051d3271`: S1a, SAN and chain each have exactly **one** add/add conflict, in `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`. All three branches carry the identical plan blob `542b9380` from 4e1e6106d; master has older cherry-picked copies (blob `413074d8`). Confirmed.
- S1a x SAN clean. Chain x SAN clean. Chain x S1a clean. (chain+SAN) x S1a clean. In the merged tree the six `[Category("LiveClaude")]` tags sit on the six correct methods, and the S1a `EveryLiveGateHasDisposition` case passes on it (`failures:[]`).
- Overlap: SAN contains the S1c chain through `c7a7b2c9e` (33 commits after 4e1e6106d); the chain adds 4 more (`4a0e1e585..2c550140e`). S1a shares only the plan commits up to `4e1e6106d`. Merge order after fixes: chain `2c550140e`, then SAN's own commits `02e4c970b..5d719f5f0` (plus fix commits), then S1a `db1935a43..9a05dbfcd` (plus fixes); resolve the plan add/add once with the 4e1e6106d bytes.

## Scope

ordinaryScopeCompleted: **Interim**. Every CP row of both slices ran fresh: CP-20, CP-26, CP-29, CP-25, CP-1, plus the CP-30 command. I added mutants, scanner probes, land graph, evidence diff and merge-tree. The **whole Unit lane did not run**. server2 was saturated at 4/4 build slots for the whole window, and single rows waited 3-25 min for a slot (CP-25 waited 1500 s). The lane is about 70 min wall even with three shards. Both slices also need Code fixes, so a Full sweep of this tip would be spent on code that will change. Live-only UIDs (CP-4..7, CP-21) and every PC stay pending. No live Claude ran and `~/.claude` was not read.

Advisory corrections are not a reason to rerun Review. The Final round after the fixes must run the Unit lane and the CP rows on the fixed tips, and CP-30 under the reviewed pre-land pack (A-7).
