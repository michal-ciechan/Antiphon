# Task d9de1c95: Review round 1 (diff-only) of CARD-1545 S2a-2 environment attestation

**Outcome: Clean. No defects.** Reviewed range `ec25d5f1aaa90f38796b53cf7126fb36c015c932..8a654ab70e87f2d45b92da4d34ef4fdfa06f0a28`
(the tested SHA). The subject tip `740e2a6a2391fec61d52f143f9086d73f2f9782c` differs from it only by
`.antiphon/task-bffd3e70.md`. Rows were rerun at that tip. Original Code landing owner: bffd3e70.
Six advisories for S2b and Mutation are listed below. The Unit lane was not run (see Scope).

## Checkpoints (checkpoint tool, plan `### Checkpoints` imported to `.antiphon/review-cp/manifest.yaml`, Code's pins)

Run `20261102-121405-c16c` (`--after S2a-2 --serial --total-timeout 30m --hard-wall-timeout 35m`), GREEN exit 0:

```text
CHECKPOINT CP-22 commit=740e2a6a2391fec61d52f143f9086d73f2f9782c build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-d9de1c95/.antiphon/checkpoints/20261102-121405-c16c/rows/CP-22/run.trx slot=granted waited=30s dirty=0 source=740e2a6a2391fec61d52f143f9086d73f2f9782c sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=740e2a6a2391fec61d52f143f9086d73f2f9782c build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d9de1c95/.antiphon/checkpoints/20261102-121405-c16c/rows/CP-31/run.trx slot=granted waited=15s dirty=0 source=740e2a6a2391fec61d52f143f9086d73f2f9782c sourceState=clean buildSource=verified
PHASES CP-22 build=294s testsWall=40.8s ; CP-31 testsWall=251.1s (SLOW CLASS LiveLaneFingerprintTests 251s)
unlisted: none
```

Run `20261102-122701-44e3` (`--after S2a-1 --serial --total-timeout 25m --hard-wall-timeout 30m`), GREEN exit 0:

```text
CHECKPOINT CP-8 commit=740e2a6a2391fec61d52f143f9086d73f2f9782c build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d9de1c95/.antiphon/checkpoints/20261102-122701-44e3/rows/CP-8/run.trx slot=granted waited=90s dirty=0 source=740e2a6a2391fec61d52f143f9086d73f2f9782c sourceState=clean buildSource=verified
PHASES CP-8 build=414s testsWall=227.4s
unlisted: none
```

`validate --evidence <report.json> --expected-source-sha 740e2a6a…` returned `CHECKPOINT SOURCE VALID` (rows=2 and rows=1).
The fresh TRX files list exactly the 6 attestation and 7 fingerprint method names, all `Passed`.

## Red record and mutants (explained unlisted runs, one build-slot lease each)

- Build: one native `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` (lease `d9de-review-mutant-build`,
  5m18s). The red-to-green diff touches only PowerShell/JSON (`git diff --stat 7d90e4a5..8a654ab7`: 4
  files). So one dll serves both the red state and the mutants.
- Lease `d9de-review-red-and-mutants` (39 s). Each run used `dotnet Antiphon.Tests.dll --treenode-filter`.
  Files were restored with `git checkout --` after each run, and `git status` was clean at the end.
  - **Red state** (the 4 files at `7d90e4a51`): `LiveLaneAttestationTests/*` gave 6/6 methods failed and
    44 harness rows: **41 FAIL, 3 PASS**. The 3 PASS rows are exactly the design's stub-green rows:
    `CliVersionOrBytesInvalidates/equal`, `ConfigurationInvalidates/volatile-only` and
    `ExternalEpochIsNotInferred/bounded-fresh`. Every failure was an assertion row (`status=complete ...`).
  - **Mutant: clock-skew bound removed** (`if ($age -lt -$script:LiveLaneClockSkew) {...}` -> `if ($false) { }`).
    `ExternalEpochIsNotInferred` went red on exactly `future-skew` (1 FAIL, 10 PASS).
  - **Mutant: credential-name refusal removed** (the `throw 'credential-field-present'` line deleted).
    `ConfigurationInvalidates` went red on exactly `credential-free` (1 FAIL, 9 PASS).

## Verification by priority

1. **Fail-safe / no credit.** `Get-LiveLaneStatus` returns the constants `state='DUE'`, `effectiveMode='Full'`,
   `gateCredit=$false` and `executionEvidence=$false` on every path. The INVALID catch path has the same
   constants. `-History`/`-ReusePolicy` still refuse. The document schema has no policy key (exact top-level
   keys), and `Test-LiveLaneExternalFreshness` defaults to `AlwaysRerun`, so a document cannot switch the
   default on. Nothing in S2a-2 calls the freshness function. I probed the reader directly:
   - Refused with `attestation-invalid`: a duplicate uid, a duplicate case-variant key, a nested extra field,
     `schemaVersion:true`, a directory path, a missing file and an unparsable document.
   - Unknown: a string `pid` (`executable-mismatch`) and one bad row (the whole document is unknown, with a
     null digest and null components).
2. **Secrets.**
   - Error text is a fixed token for each fault. The JSON parse error is caught and replaced, and the status
     catch keeps only the `^[a-z][a-z0-9-]*` reason shape.
   - The credential scan never reads a value. A credential-named key nested under `rows[].observed` gives
     `credential-field-present`.
   - The exporter reads no process environment (only `Variant.Environment` and an owned fake profile) and
     hashes the projections. The sentinel was absent from the document, the consumer output and the status
     stdout/stderr (witnessed by `credential-free`).
3. **Declaration.** I ran the real-tree fingerprint at base `ec25d5f1` and at the tested SHA:
   - Reasons went from 200 to 2. The 2 left are exactly `undeclared-runtime:file.ps1` and
     `undeclared-runtime:outside.ps1`. They are test-argument literals in
     `InternalDecisionPolicyTests`/`AgentTaskServiceIntegrationTests`/`AgentTaskDecisionQuestion*`, and both
     were present at base.
   - All 198 removed reasons are under the three new roots: 160+1 under `tests/Antiphon.SessionRunner.Tests/`,
     28 under `tests/Antiphon.PtyHost.Tests/` and 9 under `docker/`.
   - The inventory grew from 5229 to 5455: 226 added, 0 removed. That is 171 SessionRunner.Tests + 31
     PtyHost.Tests + 20 docker = 222 from the new roots, plus this diff's own 4 new files.
   - Digest `b347cbaf8f2374f576e40828ea3c494978ac97cf38f07c82fb493b9872eda596` and closure 38 match the Code report.
   - Declaring the roots only adds hashed inputs, and no reason outside those prefixes disappeared.
4. **Witnesses.** See above: red 41/44 with the 3 design stub-passes, green 6/6 + 7/7 + 7/7, and both mutants red.
5. **Deviations.** None weakens a witness.
   - (1) The `[W]` vs `[W,W]` comparison adds strength.
   - (2) The folded V-8 rows assert that the fingerprint `environment` equals the attestation digest, that the
     lane digest moves, and that unknown propagates `environment-unknown`.
   - (7) The 8-10 min pins are row timeouts only, and per-method budgets are unchanged (60 s/120 s). This
     host measured CP-31 at 4m11s, so a 4 min pin would cancel.
6. **Gates.**
   - Land graph `-Check` (gated, existing `tools/Antiphon.LandGraphAudit/bin/Debug/net10.0` dll, no `-Update`):
     exit 0, `approved`, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`.
   - `check-evidence-diff -BaseRef ec25d5f1 -HeadRef HEAD`: commits=3, violations=0.
   - `git merge-tree --write-tree` is clean onto `origin/master` `f5283ad0` (tree `7825fad8`) and onto
     `origin/rc/2026-11-01` `e114313d` (tree `54cae215`).
   - S2a-1 (`ec25d5f1`) is not on master yet. This is a master-only feature.

## Advisories (no Review rerun needed; for S2b and Mutation)

- **A-1 (S2b, before wiring):** `Test-LiveLaneExternalFreshness` accepts missing bindings.
  - Repro: `-Candidate @{}`, `-Executed @{epoch='e'}`, and probes with no `nonce`/`host`/`account` return
    `reuseEligible=True, d6-bounded-exception`, because `$null -cne $null` is false.
  - `-Candidate $null` throws `Cannot index into a null array`.
  - Fix: require non-empty strings for candidate, executed and probe nonce/host/account.
  - It has no caller in S2a-2, so there is no credit path today.
- **A-2:** The attestation path follows a symlink (`ReadAllText`; probe: symlinked document -> `complete`)
  and has no size cap. If S2b reads documents a producer did not write in place, add `Get-Item`
  LinkType/length refusals.
- **A-3:** Row uids are neither pattern-checked nor bound to the lane roster.
  - A one-row document is `complete`.
  - `route-not-owned:<uid>` echoes any uid string into the status JSON (probe: `route-not-owned:sk-ant-SECRET4`).
  - S2b should bind attestation rows to the expanded manifest UIDs and refuse out-of-roster uids.
- **A-4:** A probe `atUtc` cast failure echoes the value (`Cannot convert value "..." to type DateTimeOffset`).
  These are probe fields, not credential-bearing, but wrap them if S2b feeds probes from documents.
- **A-5 (Mutation binding):** The credential scan is case-sensitive (`-ccontains`).
  - Case variants (`AccessToken`, `apikey`) still refuse, but as `attestation-invalid` through the exact-key
    schema, so nothing leaks.
  - The `IList` recursion branch has no witness of its own: removing it changes only the reason label.
- **A-6 (Mutation binding):** `host-account` uses Candidate == Executed, so the `-cne $Executed[...]` halves
  (PC-81) have no separating case.

## Scope

- **ordinaryScopeCompleted: Interim.** CP-22, CP-31 and CP-8 ran Full.
- **The Unit lane was not run.** This brief is a diff-only round 1 with a 75 min budget, and the lane takes
  about 67 min serial on server2. The Code task deferred it as well.
- `git grep` finds no consumer of the changed scripts or `tests/live-lane-inputs.json` outside the two
  Integration classes. Source-scanning census tests in the Unit lane could still see the two new test files.
- PCs PC-1..PC-82 stay pending for method-scoped SourceLanding Mutation.
