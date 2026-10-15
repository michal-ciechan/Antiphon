# Release candidate 2026-10-15 — CARD-1281 pilot

Status: **FAIL / incomplete for release**. All commissioned pieces finished; no release qualification or publication credit claimed.

Corrected candidate ref: `release/rc-20261015T215926Z`, verified at the pinned SHA.

Pinned candidate: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
Morning baseline: `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.
Task branch: `feat/card-task-1c14bf28`. Raw generated evidence stays outside Git at
`/work/evidence/task-1c14bf28/raw/`.

## Required coverage and available evidence

The owner `docs/release-gates.md` and schema-v2 `tests/test-execution-policy.json`
(policy hash `922cce2a0064bebfc90e211fe33db10dc74cb61ecb5d9f050bd00ec669e10ada`)
require the full automated **Windows** profile: `antiphon`, `session-runner`,
`pty-host`, `agents-pty`, `messaging`, `client`, `scripts`, and `e2e`.
This includes eligible Unit, Integration and Slow cases, native suites, messaging
against a disposable broker, fresh client build/lint/Vitest, the script census,
and automated E2E. OptIn alone does not exclude a case; only the policy's owned,
reasoned class/method exclusions apply. Live providers, headed canaries,
separately approved distiller tests, QEMU SourceLanding and Linux-only lanes are
explicitly outside that Windows profile, under their named owners.

Every required discovered UID needs one disposition, fresh source-bound execution
and TRX. Missing/duplicate UIDs, required skips, unknown outcomes, zero required
execution or stale evidence block release. E2E prerequisites are npm ci/build/lint,
the E2E build, generated Playwright Chromium installation and a fresh bundle;
missing Docker/browser/owned runner or fixture failures are incomplete/red.
E2E must use its disposable database and loopback random-port runner, with live,
headed, task-token and production override inputs cleared.

RC release credit additionally needs true boolean coverageComplete/testsPassed/
reportDelivered, exitCode 0, runId, matching policy hash, candidate id, valid
`release/rc-YYYYMMDDTHHMMSSZ` ref and matching full SHA/expectedSha. Publication
requires the producer journal, repeated exact remote identity validation, complete
required suite results, delivered report, annotated CalVer tag, GitHub Release,
read-back manifest/assets/digests and build-probed capabilities. No such release
credit or publication is commissioned here. The caller explicitly requested
`release/2026-10-15`; this immutable pilot branch is not the standard cutter's
accepted candidate-ref shape and cannot earn formal rc-release credit.

This native Linux host can produce a new pinned-source build and execution of
the explicit CARD-1287 373-class roster (expected 4,516 cases), inspect/attempt the
documented stack smoke safely, and attempt isolated automated E2E and its
prerequisites. It cannot produce the full Windows/native eight-suite RC profile,
Windows qualification, live Windmill registration/readbacks and two real schedule
slots, publication/tag/manifest/status receipts, or accepted master scheduled
readiness/watchdog/outage/notification receipts and an Interim admission/refusal
pilot. Those pieces remain missing, even if all local tests pass.

Interim stays disabled; no configuration or schedule is changed. Before activation
the owner additionally demands accepted CARD-0545/0487 nightly qualification,
independent watchdog deployment/custody, readiness configuration, the committed
qualification-bound Interim receipt, scheduled master complete-green and fresh
monitor, CARD-0544 pilot with deferred cases run in Final/exact-SHA land/rollback/
costs, and CARD-0599 full release-gate qualification. RC green never supplies
master daily readiness.

D-6 context: daily admission is only for opted-in, bounded, nonurgent cards, up to
10 independent groups per 16:00 Europe/London cut, with urgent Full work retained.
A branch cut is not approval of a train, member acceptance, Full Review, landing,
or activation. Required missing evidence stays an incomplete gate under D-2;
historical outcomes/costs are preserved.

## Execution results

All commissioned pieces have been attempted. Production AppHost/server/runner on
17202/17204 were not stopped or restarted. No product/test fixes, timeout relaxation or fabricated receipts.

Initial isolated Linux build: PASS, source HEAD `067b47e2dc903247f1a7c8aa539032cc4266e4ad`, 123.498s wall, 1,057 warnings, 0 errors, granted slot with 0s wait. Main assembly SHA-256 `3f92aaf671875db0b613e83d60f55d324655748d2febbdbd557a58acb5421290`; pinned informational-version bytes verified. The next documentation-only commit records this result before the long no-build test run; no product/test input changes. Build argv/log/timing/provenance are retained under the raw evidence root.

## Refinement and immutable ref correction

The caller's refinement arrived after the originally requested `release/2026-10-15`
was already pushed. That ref is retained at the pinned SHA; it was not deleted or
moved. The corrected authoritative pilot ref is
`release/rc-20261015T215926Z`, created by a plain non-force push of the pinned SHA
after an absence check and verified with `git ls-remote`. The cutter was not used:
its source rejects linked worktrees, and this assigned checkout is one. No owned
producer clone or candidate journal was fabricated. Remote readback is retained
in `raw/corrected-release-ref.txt`.

No tag or `publish-release.ps1` invocation is authorized or performed. The refined
release-note draft will be `2026-10-15-release-notes-v2026.10.15.1.md`; the proposed
version is not a published release.

## Completed Linux roster and smoke

| Piece | Verdict | Total | Passed | Failed | Skipped | Outer wall | Exit |
|---|---|---:|---:|---:|---:|---:|---:|
| Isolated pinned-source Linux build | PASS | — | — | 0 errors | — | 123.498s | 0 |
| Frozen Linux roster, one run | FAIL | 4,516 | 4,515 | 1 | 0 | 2,699.430s (44m59.430s) | 2 |
| Failed class, one isolated rerun | FAIL | 4 | 3 | 1 | 0 | 24.027s | 2 |
| Documented stack smoke | FAIL / health checks incomplete | — | Docker + owned network | missing dev Postgres + Windows-only cmdlet | — | 4.120s | 1 |

The roster's native TUnit duration is 44m52.566s; outer wall includes driver/slot
handling. Its slot was granted with 0s wait; the rerun waited 15s; smoke waited 0s.
Fresh TRX has exactly all 373 frozen roster classes, no missing/extra classes, and
2,866 distinct observed methods. The main assembly hash remained byte-identical
to the pinned-source build. Runtime checkout HEAD was the evidence-only commit
`cef5e26487da2b2d87fc4aaa9a62365632b74c71`; its only delta from the candidate is
this evidence note. No product/test input was modified or rebuilt for the run.

The single roster failure and its reproduced assertion are detailed in
[the defect fan-out](2026-10-15-release-candidate-defects.md). The missing Aspire
stack and Windows-only listener command leave smoke incomplete on this Linux
host. No production process was changed. E2E was pending in that evidence commit; its completed result is below.

Raw TRX: `/work/evidence/task-1c14bf28/raw/roster/run.trx` and
`/work/evidence/task-1c14bf28/raw/rerun-project-contract/run.trx`.
Exact argv, consoles, timings and machine-readable audits are beside those files.

## Completed E2E and prerequisites

| Piece | Verdict | Results / counts | Outer wall | Exit |
|---|---|---|---:|---:|
| npm ci | PASS | fresh dependencies | 25.908s | 0 |
| npm build | PASS | fresh client/dist | 54.468s | 0 |
| npm lint | PASS | 0 errors / 0 warnings | 77.431s | 0 |
| Isolated E2E build | PASS | 36 warnings / 0 errors | 83.984s | 0 |
| Generated Playwright Chromium install | PASS | browser downloaded; native launch later failed | 25.473s | 0 |
| Compiled E2E discovery | PASS | 171 UIDs: 167 required / 4 excluded; 25 required classes | 4.661s | 0 |
| Required E2E selection, one full run | FAIL | 167 results; 164 executed; 54 passed / 110 failed / 3 required skips | 1,335.173s (22m15.173s) | 2 |
| ContractSnapshotTests, one class-only rerun | FAIL | 0 passed / 5 failed / 0 skipped | 76.510s | 2 |
| ChannelE2ETests, one class-only rerun | FAIL | 0 passed / 1 failed / 0 skipped | 84.833s | 2 |

Every driver above obtained a host build slot, all with 0s wait. The E2E native
TUnit duration was 22m11.236s. Reruns diagnose the original failures and never
replace the original outcome or counts. No full-suite second run occurred.

The three policy-excluded classes are DelegationPipelineE2ETests,
DelegationSequencingE2ETests and OutputDistillationApplyCanaryTests (four compiled
cases total), under CARD-0599's live/headed/approved-canary exclusions. No other
OptIn category was silently removed. The selected 167 cases all have TRX results
across exactly the 25 required classes; three results are required skips, not
executed coverage. All 167 raw terminal UIDs also match discovery with zero
missing/extra/duplicates under an independent diagnostic observation.

**The repository's official diagnostic adapter is incomplete:** it drops all 110
native ErrorTestNodeStateProperty records and returns 57 terminal UIDs only. Its
missing-UID result is retained unchanged. The independent external observation
explains the discrepancy; it is neither a product repair nor a trusted RC receipt.
See D-13 in the defect report.

E2E failures: 69 Windows-only fixture admissions, 35 browser setup failures from
missing libglib-2.0.so.0, one channel session-start timeout, and five contract
snapshot mismatches. The five snapshot failures and the channel timeout reproduced
once in their respective class-only runs. Three F-5 cases skip because their
required opt-ins conflict with the automated safe environment and lack a declared
profile exclusion. All names, exact errors, classification, source attribution
limits and smallest proposed fixes are in the defect report's 13 sections.

E2E build and execution HEAD: 8aabe6da92c258237619f66606d3bda3ea4baf7b. Its complete
delta from the pinned candidate is the two evidence documents; all product, test,
policy, scripts, project and client inputs remain at the pinned candidate. This
E2E assembly truthfully carries that documentation commit's build identity; it is
not relabeled as an exact-candidate formal release build. Its SHA-256 is
bb26cdcb9d53c254df625d35653ee32339ea4c6122de38701b576d10b0af3333.
The initial Linux roster binary was built at the exact pinned SHA and remained
byte-identical through final cleanup.

Live/headed/distiller inputs, task token, production API overrides and inherited
production service configuration were cleared for E2E children. Its app/database/
runner were fixture-owned; no production AppHost, server or runner was restarted
or stopped. No tags, GitHub Releases, publication script or Interim activation
were performed.

## Evidence identities and reproduction

| Run | Actual TRX run id | TRX SHA-256 |
|---|---|---|
| roster | 78c763e2-17e3-407e-993f-6292ca5128f2 | aaeacbb7ce2a10258eb6a175683e11985051c44a874c0e58d03629db659bd4ef |
| rerun-project-contract | cce49f37-0895-4909-9c08-2306f9d4d182 | c26923116c8e669d4f85d3f4691c2936cb592ccc9561c7b4e15786193624ccde |
| e2e | 3f5d463a-f143-4aa3-bd77-11164543dd15 | 92842f6a98e66f6659e38c19095a72b385cb891f5a56b69c3dd9dc409d2bdf6a |
| e2e-rerun-contract | f4c0cc58-19fb-4cb4-9eaa-32d7aa5081d3 | 72812df6c17dcbeeee69aa6418760cd9a045c593c047d437a53e03e8c6a219ce |
| e2e-rerun-channel | d48fa74c-0c27-43b4-8e7a-c110c2cc8598 | 179c36a084cb5503d1bb50c76bc40b59555fcd2d75a91c86b44794ec47075dad |

All native argv arrays, filters, consoles, start/end/wall/exit records, discovery,
TRX, HTML and audits are retained at /work/evidence/task-1c14bf28/raw/.
The main full-run TRX files are raw/roster/run.trx and raw/e2e/run.trx.
The observed roster/filter hashes match their qualified source files; see
final-evidence-index.json. E2E census digest:
dd3cd80d2828d150c7014cfc73dc0ab13e46f66fa01b31b8d73ffb321dee65fe.
These are local evidence identities, not invented Windmill/native RC run receipts.

Rebuild first after cleanup, through the host slot, using the documented CARD-1287
command with OutputPath=bin-rc1015-1c14bf28/. Replay the literal filter from the
committed CARD-1287 filter file and use a fresh results directory. The exact
class-only failure filter is /*/*/CheckpointToolProjectContractTests*/*.
For E2E, rebuild client and tests/Antiphon.E2E with
OutputPath=bin-rce2e-1c14bf28/ and UseAppHost=false, install Chromium using its
generated playwright.ps1, and use the retained compiled-census e2e-filter.txt.
Each raw/<run>/argv.json records the literal command without shell expansion;
change the results directory for any later run and never overwrite these TRX.

## Gate disposition and missing release credit

By source inspection only (publisher was not executed), this manual diagnostic
run **cannot be accepted by release-cut.ps1 / publish-release.ps1**. The cutter
entrypoint creates an owned journal and starts its own full Profile=rc / Trigger=rc
run; it does not adopt arbitrary local TRX. The publisher requires rc-release
complete-green, matching candidate journal/remote SHA, pinned policy authority,
full execution/discovery ledger, teardown and correlated accepted report receipt.
This run is red, partial in required profile coverage, has required skips and
missing official adapter UIDs, and lacks those trusted producer records. Expected
refusals include not-rc-complete-green, missing-candidate-journal and
missing-authority. No real publication attempt was made.

A shell/manual invocation alone is not a universal rejection rule: the full RC
entrypoint itself may be operator-invoked with Trigger=rc. This manually assembled
subset has none of that lane's qualifying identity/authority and cannot be
converted into green by naming a branch, editing JSON or calling a run manual.

| Required rc suite | Evidence produced here / remaining gap |
|---|---|
| antiphon | Frozen Linux subset failed; full eligible Windows Unit/Integration/Slow missing |
| session-runner | Windows suite missing |
| pty-host | Windows native suite missing |
| agents-pty | Windows native suite missing |
| messaging | Disposable-broker suite missing |
| client | ci/build/lint passed; Vitest suite missing |
| scripts | Full script census missing; Linux Unit script cases do not substitute |
| e2e | Full selected local diagnostic run failed; qualified Windows execution missing |

Windmill registration/schedule readbacks, two real RC slot observations, formal
full-profile ledger/discovery/accepted report, published annotated CalVer tag,
release assets/manifest/readbacks/status lookup, master scheduled readiness and
watchdog/pilot/Interim qualification receipts remain missing. Interim remains
disabled. There is no publication, landing approval or train member acceptance.

## Cleanup and deliverables

All owned foreground drivers ended. Removed all 43 producer-inventoried alternate
output roots (28 Linux-build roots and 15 E2E-build roots), with none of those
roots remaining. No retained runner manifests identified a live owned runner;
fixture runner directories had been removed. Final unit binary identity was
verified before deletion. Producer inventories and cleanup records remain in
raw/build-provenance.json, e2e-build-provenance.json, output-cleanup.json and
owned-runner-census.json. No blanket temporary-root or shared-cache deletion ran.

E2E fixture diagnostics collected after the isolation reruns are retained under
raw/owned-e2e-TestOutput-after-reruns/. They are explicitly post-rerun diagnostics;
the original full-run console/TRX/diagnostics remain separately retained.
Generated payloads remain outside Git. Only this evidence note, the defect fan-out
and the requested release-note draft are task-branch deliverables.

--- next stage ---
next: code
handoff: Fan out the 13 defect sections to scoped code/environment owners; preserve all historical outcomes; Review/land repairs, then recut and collect a full qualified Windows RC before publication.
artifact: /work/worktrees/task-1c14bf28/docs/investigations/2026-10-15-release-candidate-defects.md
