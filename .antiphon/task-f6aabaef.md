Implemented and verified: the read-only stopped-run seed builder and acknowledged cross-candidate ordering passed all eight checkpoint rows from one fresh build; the complete receipt and every row validate SOURCE VALID. Ready for ordinary Review.

Assigned branch: feat/card-task-f6aabaef. Base: e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3.
Implementation/test source: 8b3cbc438f568c6b6c261d63bee4f1df7891e2fc. Complete reopened checkpoint source: 60318acc795876686caba65e9a3c39ee0bec7899 (only report/plan commits follow the implementation). Report publication follows separately.
Standing caller authority covered this work; I proceeded without requesting additional approval.

Implementation

- /work/worktrees/task-f6aabaef/scripts/export-nightly-stopped-run-seed.ps1 is the explicit builder front door; /work/worktrees/task-f6aabaef/scripts/lib/nightly-stopped-run-seed.ps1 contains export, validation and pure seed ordering. The source directory is read-only. Output inside it, existing output, linked paths, inconsistent plan/owner identity and changing evidence refuse export. Source run/SHA are inferred from immutable suite plans; source candidate metadata can be supplied explicitly when absent from owner JSON.
- The self-contained seed has seedKind=operator-seed-from-stopped-run, source run/candidate/SHA, source-observation and creation timestamps, failed class+method identities with suite/old chunk/outcome, completed/started/unfinished chunk membership, source artifact hashes and a content hash. Failed, Error, Timeout and Aborted are failures. Parameter expansions of one class+method/chunk are deduplicated. Completion requires a closed TRX result, terminal timestamp and sufficient planned observations; a passing prefix/torn result remains unfinished. Supervisor-named root TRX files without a retained plan row still contribute their identities and membership (marked trx-without-plan-row). Empty whole-suite class lists obtain observed membership from TRX. All input file reads are checked for containment. Case-distinct failed classes remain separate.
- /work/worktrees/task-f6aabaef/scripts/lib/nightly-failure-history.ps1 accepts this separate format only with -AcceptCrossCandidateSeed AND -SeedSourceCandidateId equal to its source candidate. Both metadata/payload hash and optional external file digest are checked. Both source-observation and seed creation must be within 14 days (future timestamps beyond five minutes refuse the hint). Invalid hints fall back to the complete original order with a reason. Ordinary closed-run history retains its existing same-candidate/seven-day rules.
- The public nightly-run/nightly-tests wrappers and both implementation functions forward the acknowledgement and source candidate through the outside-checkout bound copy, sync/self-reexec and executor hops. The actual dispatch coordinator consumes the normalized seed. No private supervisor forwarding is claimed.
- /work/worktrees/task-f6aabaef/scripts/lib/nightly-dispatch.ps1 routes the seed through Resolve-NightlyPreviousRunOrder into Resolve-NightlyStoppedSeedOrder. S1 Resolve-NightlyPriorFailures remains the class-to-current-chunk mapper, including its unresolved behavior: one missing historical class does not discard other mapped failures. Failed current chunks come first, unfinished/new chunks next, passed classes last. Within a partition, unknown last-run timestamps retain canonical order and known timestamps sort oldest first. A wholly unmappable seed restores the original roster.
- New-NightlyDispatchSchedule and Invoke-NightlyPreparedSchedule retain canonical references and exact suite/chunk permutation checks. Every fresh Full run still executes every chunk; seed completion never earns a skip, pass, receipt or release credit. Tests inject fresh red work into both the prepared executor and real coordinator and exercise the production outcome reducer.

Graph-plan reuse: this is another input adapter to the existing failure-history/order pipeline, not a competing history store or failure decoder. The future graph planner can consume the validated normalized seed through Resolve-NightlyPriorFailures, then Resolve-NightlyPreviousRunOrder/Resolve-NightlyStoppedSeedOrder over its frozen canonical entries. Read-NightlyTrxIdentities is the existing S1 failure-identity decoder; XML completion-envelope inspection is separate. New-NightlyDispatchSchedule remains the canonical permutation boundary. No DAG executor, receipt reuse, affected-first policy or native activation is introduced here.

Tests and history

New files: /work/worktrees/task-f6aabaef/scripts/test-nightly-stopped-seed.ps1; /work/worktrees/task-f6aabaef/tests/Antiphon.Tests/Scripts/RcStoppedRunSeedTests.cs; synthetic TRX/owner/timing/plan fixtures under /work/worktrees/task-f6aabaef/scripts/fixtures/nightly/stopped-seed/; checkpoint plan /work/worktrees/task-f6aabaef/docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-plan.md. The existing scripts/test-nightly-failed-first.ps1 was not edited.

The fixture covers antiphon, session-runner, pty-host, agents-pty and e2e lanes, group/split/profile chunks, closed results, never-started chunks, a passing unfinished prefix and repeated method expansions. BuilderReadOnly compares complete input-directory file bytes/hashes and file/directory mtimes before and after export and a refused inside-source output attempt. The permutation witness checks 64 deterministic random seeds (1514..1577), 25 current chunks, then 25 actual boundary calls per execution arm; new failures contribute to TESTS rather than inherited green.

Initial availability RED at 7cdd24d8b5d3dad550d556eb1ab4d8ecc7b10992: 0 passed / 7 failed, because the builder did not exist. These are availability failures, not seven independent guard proofs. Narrow implementation diagnostic 1: 6/7 passed; FailedFirstMapping expected a known-timestamp unfinished chunk among unknown-time work. Its expected order was corrected and an independent age-order assertion added. Diagnostic 2: 3/3 passed for mapping, fresh execution and public hops. Coordinator diagnostic: 1/1 passed.

Independent guard controls (each changed only its named guard, ran the named harness case, produced exit 1 with the expected contract assertion, and restored exact source bytes):

| Mutant | Case | Result |
|---|---|---|
| Remove failed-first partition | FailedFirstMapping | killed |
| Ignore acknowledgement | AcknowledgementRequired | killed |
| Accept hash mismatch | HashMustVerify | killed |
| Write into synthetic source directory | BuilderReadOnly | killed |
| Drop a scheduled chunk | ScheduleConservesAllChunks | killed by permutation refusal |
| Treat Timeout as pass | TimeoutIsFailure | killed |
| Drop coordinator PreviousRun forwarding | ScheduleConservesAllChunks | killed |
| Drop public run-wrapper acknowledgement splat | PublicHopsForwardAcknowledgement | killed after witness repair |
| Bypass 14-day age guard | InvalidSeedFallsBack | killed |

These are portable diagnostic mutation controls, not post-land SourceLanding Mutation credit. The first six ran in a 36s gated lease; coordinator control held 24s. The last two ran red then restored green in a 26s lease. At the earlier settlement, restored cases had historical ordinary coverage and latest-source CP-6/7/8 remained owed. The complete reopened run below closes that gap.

Historical checkpoint 20261031-100310-d7e1 at be134adf2c33b328a43fa15c4349dc061d9e0f76: 8 executed / 8 passed / 0 failed / 0 skipped, one isolated bin-f6-seed-green1 build; source validation passed; wall 562.226s, execution 562.217s, admission-only 0.009s; build phase 339.266s. Compiler: 0 errors / 1425 warnings. This is retained historical evidence, superseded for final acceptance below.

An additional public-wrapper guard mutant then survived that old PublicHops witness. Its fake function had no parameter block: the test read FunctionDefinitionAst.ParamBlock instead of Body.ParamBlock, so the fake inherited wrapper variables. da15833114c7dfdb31c89f6d826485cb5b95bf6f repairs the test, requires the actual parameter block, and records a fresh-build contingency. Production code is unchanged by this repair. The corrected test kills the same mutant and passes restored. The initial surviving-mutant probe held 8s; no assertion RED was claimed for it.

Second historical group 20261031-101758-8ae1 at da15833114c7dfdb31c89f6d826485cb5b95bf6f: 8/8 passed, source validated, one bin-f6-seed-green2 build, 445.509s wall / 445.501s execution / 0.008s admission-only. It closes the wrapper-witness repair, not the later input-shape refinements.

Subsequent RED-before-fix input witnesses:
- 6f8c6f7856b173a67ccf73b660dd64241967db93: missing-plan-row fixture, 0 passed / 3 failed (BuilderReadOnly, FailedFirstMapping, TimeoutIsFailure); 25s lease. The builder had ignored that TRX. 359db1559f19fcda7c4a405c44f788e48b7f03cd fixes it; 4/4 targeted probes passed, 27s lease.
- 753e048d047cc99d5e464770f5b73a285950f373: valid empty whole-suite class roster, BuilderReadOnly 0/1 with seed-trx-class-unplanned, 10s lease. 7ce2d5f6d1dc9d14c2667d3a3213330d1e1a1e46 infers observed membership; expanded fixture probe passed 8/8 in a 97s lease.
- d70efad91438ac84a12dcf9bf4bc185dbff5dab8: group 105 now uses Synthetic.antiphon.red while the mapped class is Synthetic.antiphon.Red. FailedFirstMapping was 0/1, 11s lease, because default Group-Object merged them. 5a9073c3dcd2e5eea0a3d9dd24e0ed8bacd04517 adds -CaseSensitive; restored probe 1/1, 12s held after normal admission. Six failed identities remain distinct, five map and only the lower-case class is unresolved.

Run 20261031-103312-2cff at 3070e34816cad1c826f85fd0545591517a9e41e0 was deliberately stopped via the checkpoint tool during build before case-identity source edits. No test ran and no green/RED assertion or receipt credit is claimed. Its retained state has provisional, nonfinal budget data (15s admission recorded); final execution cost is unknown. Stop returned successfully, removed its dead tool copy, and the exact-output process census was empty before edits.

Run 20261031-103902-c159 at 5a9073c3dcd2e5eea0a3d9dd24e0ed8bacd04517 was also deliberately stopped and joined before the snapshot-witness repair. Compiler output was 0 errors / 1425 warnings, 4m22.70s; the run never reached a test row or valid receipt. Admission was 195s. Do not credit this as a passing qualification.

Final byte-witness repair: the snapshot's original array expression grouped the file list as one nested array rather than hashing every file independently. 9e296e2d8 committed a negative control that changes existing owner.json bytes while preserving its mtime and then restores both. BuilderReadOnly was assertion RED at that source, after 150s slot wait / 10s held. 8b3cbc438f568c6b6c261d63bee4f1df7891e2fc flattens the item list and hashes every file. CP-1 in the complete reopened run below is the acceptance proof; earlier read-only witness outcomes remain historical.

After all producers/clients for the obsolete bin-f6-seed-green1/2/3/4 roots were stopped or completed, their exact literal paths were inventoried and removed: 8266 hash-verified files / 1411 directories. Inventory is .antiphon/f6-prior-output-cleanup-inventory.json. This avoided scanning unused compiler trees in the last fresh build. No source, live run or foreign output was deleted.

Previously incomplete run 20261031-105745-2c7c at 8b3cbc438f568c6b6c261d63bee4f1df7891e2fc: 5 tests executed / 5 passed / 0 assertion failures / 0 TUnit skips. CP-1..5 passed (read-only bytes/mtimes, case-distinct mapping, acknowledgement, hash, Timeout). CP-6 never acquired its slot and CP-7/8 never started. Tool rows: 5 green / 1 red admission / 2 skipped-not-run. Terminal exit 5, hard-wall-deadline-exceeded. The fresh bin-f6-seed-green5 build completed with 0 errors / 1425 warnings; build phase 405.286s. Wall 961.387s, execution 521.538s, admission-only 439.849s. CP-6 waited 244s. The final CLI used a 16m hard wall (narrowed to respect the remaining task budget), 24m execution allowance and 12m row/build bounds; the hard wall expired while execution allowance remained. No unleased retry or timeout widening occurred.

At that earlier settlement qualification was INCOMPLETE. Tool validation, even explicitly selecting CP-1..5, refused exit 2 with CHECKPOINT SOURCE INVALID reason=report_failed because the overall report failed. Per-row fields show those five clean/verified executions, but the aggregate source/build verdict is unknown and no full source-clean qualification is claimed. Preserve .antiphon/f6-final-source-validation.log and the complete failed report. Earlier 8/8 reports are historical, not substitutes for latest-source acceptance.

The earlier blocked report requested this continuation (now completed by the reopened run below): Code verification needs an admitted slot/time window for CP-6 ScheduleConservesAllChunks, CP-7 InvalidSeedFallsBack and CP-8 PublicHopsForwardAcknowledgement at the final implementation source. The current validator refuses partial failed reports, so obtain a valid complete qualification report (rerun the closed eight-row group if no approved append-only partial-receipt qualification mechanism is available). Use one fresh isolated output name and the committed plan; do not relabel the failed envelope or transfer a source-clean assertion. This follows docs/orchestration-loop.md D-2: missing required execution routes to Code with the gate incomplete, not land or a Review rerun just to relabel it.

Before reopening, all build/test drivers took host build slots, with TEMP=/tmp, UseAppHost=false and fresh isolated outputs. There was one explicitly listed 15s checkpoint-tool bootstrap build (0 errors / 7 warnings), two historical ordinary builds, two deliberately stopped builds, and the final ordinary build after input-shape/case/snapshot repairs. Each group used one build shared by its selected exact methods; the final group did not complete all eight. The extra historical cost is retained. Direct interpreter runs were listed RED/guard/fixture diagnostics, not ordinary checkpoint substitutes. No native run or full assembly ran. No slot was bypassed.

The first manifest import saw only seven rows because a blank separated CP-8 from the table. The plan was fixed; an attempt to overwrite the existing generated manifest was refused, then fresh imports admitted eight rows. An initial run with a mistyped expected SHA refused source_mismatch before any build or test. Neither is assertion RED. Intermittent controller owner-read timeouts were logged by the tool; receipt/source validation, lease and terminal run status are reported independently.

Performance: per-method/phase timings and admission/build/test/teardown spans are in the checkpoint evidence. Harness IO is included in body wall; granular per-IO attribution remains unknown. No DB, web server, provider or real qualification test host is launched by these new cases; the pwsh harness is a real child under the strict assembly limiter. The private run-core seam emits synthetic argv observations, without executing the proposed external command. Samples remain provisional, not p95; collect the next three ordinary receipts under CARD-1514/CARD-1404 instead of running sample-only work.

Graph, source and cleanup

No definition file changed (no csproj/props/targets/sln/global.json change), no schema/policy/exclusion/cost file changed, and no -Update ran. The real scripts/audit-land-verification-graph.ps1 -Check passed in a granted 30s lease using the actual audit DLL produced by the checkpoint build: digest E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806, count 69, phase=pass2, approved. Five known not-built projects are retained in .antiphon/f6-land-check.log. Subsequent witness and input-shape repairs change scripts, fixtures and the plan; definition/audit-tool source is unchanged. The definition digest and audit-tool source remain unchanged at the final tested source; this valid real check is retained.

Fetched RC origin/rc/2026-10-27 fc064a68247b0186918dfb1f04465e6be0475389 merged cleanly by git merge-tree, tree dd52b929ed4126cbabb256f0ebe59bf6e476576e at the initial tested tip. Fetched master 2b471b21c498cad825035c2669dc2ce2a8c7f17f has exactly the same 48 conflict paths at assigned base and that tip, no added/removed paths. No actual merge was made. This RC-based task does not claim master landability. The later test-only delta and final report are separately visible for Review.

All nine changed PowerShell files are ASCII-only; new production code uses PowerShell 5.1/.NET Framework-compatible APIs. Portable tests ran on PowerShell 7/Linux; no native Windows or Windows PowerShell 5.1 execution is claimed.

After the last remaining producer/client exited, literal bootstrap and final compiler roots were inventoried and removed: 2798 hash-verified files / 489 directories / three roots. Together with prior cleanup, all seven task-created bin roots are removed; inventories and receipts remain ignored. No compiler driver remained for the stopped output roots. Evidence guard over base..implementation: 15 commits, zero generated-evidence entries, zero violations. Final report publication is separately checked. Fresh final merge simulation against RC fc064a68247b0186918dfb1f04465e6be0475389 is clean, tree eceb0df8a1c3d73db876f1aad4c539817282f2dc. Fresh master 2051d327123a5e98524b4bd1ecf31c6e516bdb4e still has 48 conflict paths, exactly the base's set. No new conflict path. Definition-file delta and non-ASCII PowerShell-file checks are empty.

No desktop file was read, no C:\Antiphon file or live d47848cc run was touched, and no tag, publication, deployment, branch switch, merge, rebase, reset, amend or force push occurred. All owned tool clients/executors/children were awaited. The operator commands below are instructions only; they were not executed here.

Operator commands after the old run stops

After ordinary Review and landing this change onto the chosen new RC tip, run from the reviewed desktop task checkout containing these scripts. The public entrypoint below handles the clone sync and normal shared native lock. It does not rely on an uninspected private supervisor to forward the new flags. This example uses the existing rc/2026-10-27 line's NEW tip; change the ref/date together if the operator selects another line.

```powershell
$ErrorActionPreference = 'Stop'
$launcher = 'C:\Antiphon\worktrees\card-task-f6aabaef'
$releaseRoot = 'C:\Antiphon\releases'
$seed = Join-Path $releaseRoot 'ordering-seeds\d47848cc.json'
pwsh -NoProfile -File "$launcher\scripts\export-nightly-stopped-run-seed.ps1" `
  -RunDirectory 'C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638\logs\2026-10-30-0824-d47848cc' `
  -OutputPath $seed -SourceCandidateId 'rc-2026-10-27-cde8def41638' `
  -SourceSha 'cde8def4163829aa5b867df6ec625433be2522cb'
if ($LASTEXITCODE -ne 0) { throw 'Seed export failed; inspect its reason.' }
git -C $launcher fetch origin rc/2026-10-27
if ($LASTEXITCODE -ne 0) { throw 'RC fetch failed.' }
$newSha = (git -C $launcher rev-parse 'origin/rc/2026-10-27^{commit}').Trim()
if ($LASTEXITCODE -ne 0) { throw 'RC SHA resolution failed.' }
$newCandidate = 'rc-2026-10-27-' + $newSha.Substring(0,12)
$state = Join-Path $releaseRoot ('candidates\' + $newCandidate)
$seedDigest = (Get-FileHash -LiteralPath $seed -Algorithm SHA256).Hash.ToLowerInvariant()
pwsh -NoProfile -File "$launcher\scripts\nightly-run.ps1" `
  -Profile rc -Trigger rc -Ref 'rc/2026-10-27' -ExpectedSha $newSha `
  -CandidateId $newCandidate -CheckoutRoot 'C:\Antiphon\releases\checkout' `
  -StateRoot $state -LogRoot (Join-Path $state 'logs') -ReleaseRoot $releaseRoot `
  -CoordinationRoot 'C:\Antiphon\verification' -SelectionMode Full `
  -PriorFailuresPath $seed -PriorFailuresDigest $seedDigest `
  -AcceptCrossCandidateSeed -SeedSourceCandidateId 'rc-2026-10-27-cde8def41638'
```

The output seed must be a new path outside the old run directory. The builder infers the full source run id from retained plan headers, avoiding any assumption that the displayed d47848cc prefix is the full run id. Inspect dispatch-schedule.json orderingReason/historyFallbackReason in the NEW run: accepted seeds say operator-seed-order; rejected/wholly unmappable inputs state their fallback. Every Full chunk still runs, including passed historical chunks. These commands do not cut a candidate journal, tag or publish a release; existing release authority requirements remain in force.

Pushed implementation/test commits

- 7cdd24d8b5d3dad550d556eb1ab4d8ecc7b10992 test: specify stopped-run seed mapping and ordering guards
- 3ffdfc4cfc774a1bb0300bddb260d600b253713b feat: build acknowledged stopped-run seeds for cross-candidate ordering
- 9ac801315a567fa6fd44fdd5d2d050d70a534b41 test: bind seed ages public hops and complete fresh execution
- 2d0f34d4906dc2bf8e6ad265cd019f3f5529ea3b test: witness seed consumption by the real dispatch coordinator
- be134adf2c33b328a43fa15c4349dc061d9e0f76 docs: bind the public seed hop row into the checkpoint table
- da15833114c7dfdb31c89f6d826485cb5b95bf6f test: bind wrapper forwarding probes to real function parameters

- 6f8c6f7856b173a67ccf73b660dd64241967db93 test: retain failed identities from chunks missing a historical plan row
- 359db1559f19fcda7c4a405c44f788e48b7f03cd fix: preserve stopped-run TRX failures when an old plan row is missing
- 753e048d047cc99d5e464770f5b73a285950f373 test: retain observed classes for whole-suite historical plan rows
- 7ce2d5f6d1dc9d14c2667d3a3213330d1e1a1e46 fix: infer observed whole-suite class membership from retained TRX
- 3070e34816cad1c826f85fd0545591517a9e41e0 docs: retain stopped-run input-shape red and green verification costs
- d70efad91438ac84a12dcf9bf4bc185dbff5dab8 test: distinguish historical failure classes that differ only by case
- 5a9073c3dcd2e5eea0a3d9dd24e0ed8bacd04517 fix: keep case-distinct failure class identities separate

- 9e296e2d86759cb289baedb08af35db855c62754 test: require byte snapshots to detect changes with restored mtimes
- 8b3cbc438f568c6b6c261d63bee4f1df7891e2fc test: hash every source file independently in the read-only snapshot

Essential unedited historical incomplete checkpoint provenance

```text
--- checkpoint report ---
run: 20261031-105745-2c7c   manifest: /work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/manifest.resolved.yaml
commit: 8b3cbc438f568c6b6c261d63bee4f1df7891e2fc  branch: feat/card-task-f6aabaef  worktree: /work/worktrees/task-f6aabaef  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 8b3cbc438f568c6b6c261d63bee4f1df7891e2fc state=clean buildSource=unknown
CHECKPOINT CP-1 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=ok filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/BuilderReadOnly executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=30s executed=13.995s wall=44.041s
PHASES CP-1 slotWait=60s build=405.2864482s startup=4.1867354s testsWall=8.4548653s teardown=0.4849583s hostWall=13.1265586s
CHECKPOINT CP-2 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/FailedFirstMapping executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=60s executed=14.119s wall=74.167s
PHASES CP-2 slotWait=60s build=0s startup=4.2654901s testsWall=8.3342049s teardown=0.7478885s hostWall=13.3475836s
CHECKPOINT CP-3 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/AcknowledgementRequired executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=15s executed=16.673s wall=31.729s
PHASES CP-3 slotWait=15s build=0s startup=4.3277723s testsWall=11.0350735s teardown=0.5685353s hostWall=15.9313816s
CHECKPOINT CP-4 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/HashMustVerify executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=14.800s wall=14.838s
PHASES CP-4 slotWait=0s build=0s startup=4.3423988s testsWall=8.864308s teardown=0.7430492s hostWall=13.9497557s
CHECKPOINT CP-5 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/TimeoutIsFailure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/rows/CP-5/run.trx slot=granted waited=60s dirty=0 source=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=60s executed=21.049s wall=81.165s
PHASES CP-5 slotWait=60s build=0s startup=6.8648433s testsWall=11.8434433s teardown=0.8265553s hostWall=19.5348415s
CHECKPOINT CP-6 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=n/a filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/ScheduleConservesAllChunks executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=acquiring waited=244s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-6 waited=244s executed=0.000s wall=244.997s reason=hard-wall-deadline-exceeded
CHECKPOINT CP-7 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=n/a filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/InvalidSeedFallsBack executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-8 commit=8b3cbc438f568c6b6c261d63bee4f1df7891e2fc build=n/a filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/PublicHopsForwardAcknowledgement executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
BUDGET wall=961.387s execution=521.538s admissionOnly=439.849s reason=hard-wall-deadline-exceeded
unlisted: none (the tool ran no other build or test command)
wall: 16m01s  sequential-equivalent: 4m05s  builds: 1  max-concurrent-builds: 1  rows: 5 green 1 red 2 skipped
outputs: refused/kept bin-f6-seed-green5/
evidence: /work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-105745-2c7c/report.md
verdict: RED exit=5 reason=hard-wall-deadline-exceeded

CHECKPOINT SOURCE INVALID reason=report_failed
```


Complete reopened verification

Caller explicitly reopened verification and authorized the complete closed list from one fresh build with --total-timeout 90m. Commit 8bbc5ec3ee9a09d92ccf9e314b5e1165e49cbec8 binds that authority in the plan; 60318acc795876686caba65e9a3c39ee0bec7899 preserves the historical output names. No production or test code changed during this continuation. The earlier report was published at d7a45d2fdf457bacfdbf204dc439126613f8a64d and its failed envelope remains unchanged above. The caller reports the old run now has a closed summary.json; that desktop fact was not inspected here. The explicit stopped-run seed remains usable for the acknowledged cross-candidate ordering flow.

Run 20261031-112435-52a1 at 60318acc795876686caba65e9a3c39ee0bec7899: ONE fresh bin-f6-seed-followup1/ build, eight exact serial rows, 8 executed / 8 passed / 0 failed / 0 skipped. Build compiler: 0 errors / 1425 warnings, elapsed 8m29.95s; checkpoint build phase 563.315s. Complete run wall 775.863s (12m56s), execution 730.847s, admission-only 45.016s. CP-8 waited 45s, other rows and the build reported granted slots with waited=0s. Explicit execution and hard-wall limits were both 5400s (90m); row/build execution bounds stayed 12m. No test failure, row retry, additional guard mutant or source repair occurred during reopening. All nine previously killed guards and their historical costs remain recorded above.

| CP | Exact method | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| CP-1 | BuilderReadOnly | 1 | 1 | 0 | 0 |
| CP-2 | FailedFirstMapping | 1 | 1 | 0 | 0 |
| CP-3 | AcknowledgementRequired | 1 | 1 | 0 | 0 |
| CP-4 | HashMustVerify | 1 | 1 | 0 | 0 |
| CP-5 | TimeoutIsFailure | 1 | 1 | 0 | 0 |
| CP-6 | ScheduleConservesAllChunks | 1 | 1 | 0 | 0 |
| CP-7 | InvalidSeedFallsBack | 1 | 1 | 0 | 0 |
| CP-8 | PublicHopsForwardAcknowledgement | 1 | 1 | 0 | 0 |

The complete receipt and every row separately returned CHECKPOINT SOURCE VALID at the exact committed source. This closes the earlier incomplete ordinary verification gate; the old report_failed receipt is retained as failed, without edits or transferred credit. New evidence: /work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/report.json and report.md; validation logs .antiphon/f6-followup-source-validation.log and .antiphon/f6-followup-row-validation.log. The original receipt files remain generated/ignored. Unedited provenance is preserved below.

The reopened checkpoint tool bootstrap was one explicitly planned, gated isolated build to bin-f6-seed-followup-tool1/: 0 errors / 7 warnings, elapsed 32.65s, slot held 34s. Import admitted all eight rows. The exact execution and validation commands (after recreating the fresh bootstrap output) are:

~~~sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label f6-seed-followup-tool -- dotnet build tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj --property:OutputPath=bin-f6-seed-followup-tool1/ --property:UseAppHost=false --nologo
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-f6-seed-followup-tool1/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-plan.md --out .antiphon/f6-seed-followup-checkpoints1.yaml
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-f6-seed-followup-tool1/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-plan.md --expected-source-sha 60318acc795876686caba65e9a3c39ee0bec7899 --row-timeout 12m --total-timeout 90m --hard-wall-timeout 90m --serial --keep-outputs --max-wait 50s
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-f6-seed-followup-tool1/Antiphon.Checkpoints.dll wait 20261031-112435-52a1 --expected-source-sha 60318acc795876686caba65e9a3c39ee0bec7899 --max-wait 50s
TEMP=/tmp dotnet tools/Antiphon.Checkpoints/bin-f6-seed-followup-tool1/Antiphon.Checkpoints.dll validate --evidence .antiphon/checkpoints/20261031-112435-52a1/report.json --expected-source-sha 60318acc795876686caba65e9a3c39ee0bec7899 --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8
~~~

These are the recorded commands, not instructions to overwrite existing evidence or reuse a compiler directory. A future ordinary run must use a fresh output name/import path and its committed SHA; wait uses its returned run id. Receipt validation itself needs no new build. No full assembly or native test was added. The retained real land-graph -Check above remains applicable: no definition or audit-tool source changed, and no -Update ran.

Fresh fetched merge simulations at tested source: RC fc064a68247b0186918dfb1f04465e6be0475389 clean, tree abed45f9e97372908df085214b1eb7870642bfd2. Master 2051d327123a5e98524b4bd1ecf31c6e516bdb4e has 48 conflict paths at both the assigned base and tested source, zero added/removed paths. This is readiness for RC Review, not a master-landability claim. Evidence guard at tested source: 18 commits / 1 optional Markdown report entry / 0 violations. Final publication is checked separately. Worktree was clean after all rows; an exact-output/executor process census found zero owned producer matches before cleanup. Cleanup inventories and final publication result follow below.

Essential unedited complete checkpoint provenance

~~~text
--- checkpoint report ---
run: 20261031-112435-52a1   manifest: /work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/manifest.resolved.yaml
commit: 60318acc795876686caba65e9a3c39ee0bec7899  branch: feat/card-task-f6aabaef  worktree: /work/worktrees/task-f6aabaef  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 60318acc795876686caba65e9a3c39ee0bec7899 state=clean buildSource=verified
CHECKPOINT CP-1 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=ok filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/BuilderReadOnly executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=14.966s wall=15.034s
PHASES CP-1 slotWait=0s build=563.3146775s startup=3.7324966s testsWall=9.5854035s teardown=0.7123098s hostWall=14.0304478s
CHECKPOINT CP-2 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/FailedFirstMapping executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=17.286s wall=17.338s
PHASES CP-2 slotWait=0s build=0s startup=5.0036853s testsWall=10.6813515s teardown=0.7375616s hostWall=16.4225985s
CHECKPOINT CP-3 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/AcknowledgementRequired executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=15.553s wall=15.608s
PHASES CP-3 slotWait=0s build=0s startup=4.4905734s testsWall=9.4809534s teardown=0.7059004s hostWall=14.6774272s
CHECKPOINT CP-4 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/HashMustVerify executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=15.362s wall=15.424s
PHASES CP-4 slotWait=0s build=0s startup=4.119302s testsWall=9.598151s teardown=0.7536032s hostWall=14.4710564s
CHECKPOINT CP-5 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/TimeoutIsFailure executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=14.858s wall=14.918s
PHASES CP-5 slotWait=0s build=0s startup=5.025182s testsWall=8.1822035s teardown=0.7524056s hostWall=13.9597914s
CHECKPOINT CP-6 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/ScheduleConservesAllChunks executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=20.546s wall=20.584s
PHASES CP-6 slotWait=0s build=0s startup=4.7732066s testsWall=14.2887317s teardown=0.7060711s hostWall=19.7680095s
CHECKPOINT CP-7 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/InvalidSeedFallsBack executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=15.599s wall=15.661s
PHASES CP-7 slotWait=0s build=0s startup=4.2275484s testsWall=10.1882799s teardown=0.4813028s hostWall=14.8971672s
CHECKPOINT CP-8 commit=60318acc795876686caba65e9a3c39ee0bec7899 build=reused filter=/*/Antiphon.Tests.Scripts/RcStoppedRunSeedTests/PublicHopsForwardAcknowledgement executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/rows/CP-8/run.trx slot=granted waited=45s dirty=0 source=60318acc795876686caba65e9a3c39ee0bec7899 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-8 waited=45s executed=18.635s wall=63.692s
PHASES CP-8 slotWait=45s build=0s startup=4.1289616s testsWall=13.2789721s teardown=0.4793992s hostWall=17.887333s
BUDGET wall=775.863s execution=730.847s admissionOnly=45.016s
unlisted: none (the tool ran no other build or test command)
wall: 12m56s  sequential-equivalent: 2m58s  builds: 1  max-concurrent-builds: 1  rows: 8 green 0 red 0 skipped
outputs: refused/kept bin-f6-seed-followup1/
evidence: /work/worktrees/task-f6aabaef/.antiphon/checkpoints/20261031-112435-52a1/report.md
verdict: GREEN exit=0

CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=8
CP-1 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-2 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-3 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-4 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-5 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-6 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-7 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
CP-8 CHECKPOINT SOURCE VALID source=60318acc795876686caba65e9a3c39ee0bec7899 rows=1
~~~

Reopened cleanup completed after the joined successful run, all receipt validations and the zero-producer census. Exact literal roots: /work/worktrees/task-f6aabaef/src/Antiphon.HostCleanup/bin-f6-seed-followup-tool1; /work/worktrees/task-f6aabaef/tools/Antiphon.Checkpoints/bin-f6-seed-followup-tool1; /work/worktrees/task-f6aabaef/tests/Antiphon.Tests/bin-f6-seed-followup1. Removed 2798 individually hash-checked files / 489 directories / 3 owned roots, with inventory .antiphon/f6-followup-output-cleanup-inventory.json and log .antiphon/f6-followup-cleanup.log. A final exact-name scan found none remaining. No source or foreign output was removed. Generated receipts, TRXs, logs and inventories remain ignored. The report is the sole individually tracked evidence document. No desktop/live/native action occurred in the continuation.

--- next stage ---
next: review
handoff: Review the stopped-run seed builder and acknowledged cross-candidate ordering against the complete source-clean 8/8 receipt at 60318acc795876686caba65e9a3c39ee0bec7899. All nine portable guard mutants were killed and restored; retain their historical outcomes and the earlier failed envelope. Review the exact operator export/launch commands above for the caller-reported closed d47848cc run. RC merge simulation is clean; master conflicts are inherited. No native Windows qualification or activation is claimed.
artifact: /work/worktrees/task-f6aabaef/.antiphon/task-f6aabaef.md
