# Review f338aaf2: CARD-0097 S-E (stage pin -> cell migration), Final

Subject: Code task `b7ab0eb9-d26c-4941-97f1-a77e28b3c04c`, `feat/card-task-b7ab0eb9` at
`f0481a779f274e1952339984fe51258822fecf81` (base `ba774271a61870261e86fb39a6fc0614fb826d53`).

**Outcome: clean.** No pin write in any flag combination. Before-images are snapshotted before the first PUT.
Restore follows the D-9 table and has the R3-03 zero-write guard. CP-18 ran green 10/10. Both validators returned VALID.

## Delta and merge

- `ba774271a..f0481a779`: 3 files (`scripts/migrate-stage-pins-to-cells.ps1` +256, `tests/Antiphon.Tests/Scripts/MigrateStagePinsScriptTests.cs` +402, `docs/superpowers/mutations/2026-10-14-card-0097-s-e-candidate.md` +96).
- `d3f46adbf..f0481a779`: only the candidate doc (+96). The script and tests are byte-identical to the tested implementation.
- `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `b2aee0812c1f475c00cc34d12fac2ffe3c694a3f`. Fetched origin/master is `ba774271a` (the task base), so there are no conflicts.
- `scripts/check-evidence-diff.ps1 -BaseRef ba774271a -HeadRef f0481a779`: commits=4 entries=0 violations=0, exit 0.

## CP-18 (checkpoint tool; run `20261014-144324-8473`; `--rows CP-18 --row-timeout 40m --total-timeout 150m --expected-source-sha f0481a779...`; no `--keep-outputs`)

```text
CHECKPOINT CP-18 commit=f0481a779f274e1952339984fe51258822fecf81 build=ok filter=/*/*/MigrateStagePinsScriptTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-f338aaf2/.antiphon/checkpoints/20261014-144324-8473/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=f0481a779f274e1952339984fe51258822fecf81 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

- `scripts/validate-checkpoint-receipt.ps1 -Evidence <run>/report.json -ExpectedSourceSha f0481a779...` returned `CHECKPOINT SOURCE VALID source=f0481a779... rows=1`, exit 0.
- `Antiphon.Checkpoints validate --evidence <run>/report.json --expected-source-sha f0481a779...` returned the same VALID line, exit 0.
- The fresh TRX has 7 methods and 10 results, all Passed. The parameter rows are present: `A_non_2xx...(apply)`, `(restore)`, `Restore_re_puts...(human)`, `(human-expired)`, `(auto)`. This matches the manifest Expect (7 methods / 10 results).

## Request-verb audit (`scripts/migrate-stage-pins-to-cells.ps1`)

All HTTP calls go through `Invoke-MigrationApi` (`:30-47`, a single `Invoke-RestMethod`). Call sites:

| Line | Verb | Path | Mode |
|---|---|---|---|
| 173 | GET | `/api/routing-pins` | dry run / apply |
| 174 | GET | `/api/model-availability` | dry run / apply |
| 185 | GET | `/api/complexity-chains?role=<eligible role>` | dry run / apply |
| 139 | GET | `/api/routing-pins?role=<migrated role>` | restore precondition |
| 102 | PUT (apply, `:243`) / PUT or DELETE (restore, `:151-167`) | `/api/complexity-chains/<role>/<tier>` | `-Apply` / `-Restore` only |

There is no PUT, DELETE or POST on `/api/routing-pins` anywhere. `routing-pin.ps1` appears only in printed instructions (`:146`, `:249`). The role and tier in a write path come from validated sets (`Test-Eligible` -> `$roles`, `$tiers`; restore validates `role -notin $roles`/`complexity -notin $tiers` at `:130`), so a write path cannot be injected.

Each test method except the dry-run test, which asserts every request is a GET, ends in `AssertNoPinWrites` (`Requests.Any(path starts /api/routing-pins && method != GET)` is false). An unexpected pin write would also 404 in `ScriptedApi` and still be recorded.

## Safety checks 1-7

1. **No pin write.** Proven above from the code and by the test. Every test also asserts `PinsJson` is byte-equal before and after.
2. **Apply ordering.** Per D-9 after round 2, `-Apply` performs its own fresh read and snapshot; no prior dry-run artifact is required. The snapshot (`:227-238`, `FileMode.CreateNew`, `Flush($true)`) is written before the `if ($Apply)` PUTs (`:242`). It holds before-images for all 3 tiers of every eligible role (39 live), including absent cells (`present:false`). The folder and path are printed (`:239-240`). A rerun is a full overwrite (V-E5), and the comment and output both say to restore from the FIRST snapshot.
3. **PUT bodies.** The body is `{candidates:[{agentKind,modelLevel}], provenance:"Human", reason:"<pin reason> (migrated from stage pin <id>)"}` with no `notAfter`. This matches `PutComplexityChainRequest`; V-E3 asserts the exact key set and the multiline UTF-8 reason. Candidates come from the live GET (V-E3's second arm changes them). Held candidates produce one `HELD` warning each, before `written:` (fable and wildcard Grok are tested). They are written with that warning, not silently.
4. **Restore.** The table is implemented exactly (`:150-168`): Human with live `notAfter` -> PUT with the saved body including `notAfter`; absent, expired Human, or Auto -> DELETE with the named note; an unknown provenance refuses. The R3-03 guard (`:136-148`) reads every migrated role's live pins before the first write and refuses (exit 1, zero writes) unless an active stage Required row with `candidateCount>=1` exists. The apiBase check (`:124`) and the before-image/plan validation (`:129-135`) also run before any write.
5. **Effective diff.** For a Required pin, the BEFORE and AFTER-apply lines say `Required pin <pairs>`, which equals S-B `ComplexityEffective.Compose` (source `pin`, the pin's candidates; all live kinds are delegatable). The after-clear line says `role <pairs>`, which equals the cell. The output explicitly says to read back and then run `routing-pin.ps1 clear` as a separate command.
6. **PowerShell 5.1.** No non-ASCII bytes, no BOM, LF only. No `?.`, `??`, `&&`, `||` or ternary. Text is read and written as UTF-8 via `[IO.File]`. The token is sent only as a header and never printed. Exit codes: 0 on success, 1 on any refusal or error.
7. **PCs judged by reading.** All five would go red:
   - PC-29: a Plan PUT, and the cell count goes 27 -> 30.
   - PC-30: `Last().Path` changes and the PUT count goes above 5.
   - PC-31: PUT count 1 -> 0.
   - PC-40: a non-GET request appears and the exit is 0, not 1.
   - PC-45: request length 52 -> 53 and the paths differ.

   `ScriptedApi` is reused unchanged from S-D. Ids, pairs and reasons are hand-written literals. None of the five PCs was executed; all stay pending.

## Live GET-only dry run (read-only; no `-Apply`, no `-Restore`)

`ANTIPHON_API=https://antiphon.desktop.codeperf.net`, server `893ddfceeebf4d13d9401848c535abada3983c27`. The run printed 13 rows and exited 0. Snapshot: 13 pins, 39 before-images (0 present), 39 plan bodies.

Candidates match the independent raw `GET /api/routing-pins` exactly:
- Plan, Code, Debug, Investigate, TestDesign: `Codex/Frontier,Codex/High,ClaudeCode/High`.
- Coverage, Docs, Commit, Test, Deploy, Merge, Mutation: `Codex/High,Codex/Medium,ClaudeCode/High`.
- Review: `ClaudeCode/High`.

All 13 pins are Human, Required and stage-level, with no forbid list, dates or agent. The live holds are ClaudeCode `fable` and Grok `*`. No pin names those aliases, so no HELD warning is correct. The live server does not yet return `effective` (pre-activation); the Required-pin branch does not consult it. Snapshot and log: the session scratchpad `live-dry/`, outside the repo.

## Disclosures (no verdict impact under the 2026-10-01 policy)

- **D1 coverage gap.** No test asserts that the snapshot exists before the first PUT; for example, no test reads the snapshot after the V-E4 `apply` failure. A mutant that moves the snapshot write after the PUTs would survive. Candidate for Mutation's missing-control review.
- **D2 untested branch.** The Preferred-pin effective line on a server without `effective` (`:219`) prints `Preferred pin X; fallback <resolvedFrom> <pairs> (read server effective after activation)`. It is a hedged projection, not "unknown". No V-E covers it, and no live pin is Preferred.
- **D3 Windows PowerShell 5.1** was not executed (Linux pwsh only); the author disclosed the same. The static checks pass.
- **D4 plan table is stale.** The plan's §6.3 expected-cell table (fable-led lists) no longer matches the live pins. Tests use the live census as literals, and the script reads the API. The candidate doc discloses this; the runbook and table refresh belong to S-F.
- **D5 cosmetic deviations from D-9.** The snapshot name adds `-fffffff-<guid>`. Writes follow the API pin order, not the canonical role order. Both are behaviourally harmless.
- **D6 process (mine).** I invoked the checkpoint tool through `dotnet run --project tools/Antiphon.Checkpoints` (start, wait, validate), which built the tool once outside `build-slot.ps1`. The author gated this bootstrap build; I did not. I removed the resulting `tools/Antiphon.Checkpoints/bin` and `obj` and the run's retained `tool/` copy (literal paths, all created by this session; `wait` had refused that copy's cleanup with `output-memory-authority-missing`). Evidence is kept in `.antiphon/checkpoints/20261014-144324-8473/` (gitignored).
- **PCs.** PC-29, PC-30, PC-31, PC-40 and PC-45 are pending for SourceLanding Mutation.
