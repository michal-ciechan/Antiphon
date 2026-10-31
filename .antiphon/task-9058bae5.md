S2 is complete and pushed: the stable global dispatch scheduler and production-loop extraction pass CP-3/4/5 (3 executed, 3 passed, 0 failed, 0 skipped). S3 was not started; public failed-first operation is not yet enabled.

Tested source: `c2d6f95582f041beaa33890af4d0ff83c83ee6a4`, branch `feat/card-task-9058bae5`; base `3fb3c113e8b2554ccd5789e00375dbfd69697981`. This report is the only subsequent narrative change. Standing caller authority covered the work; no additional approval was requested.

Implementation and scope

- `scripts/lib/nightly-dispatch.ps1`: stable-partition canonical chunk references by prior failure across suites, with canonical/dispatch ordinals, history/source digests, unresolved records and preparation state. Validate the exact keyed permutation; never copy, renumber, reprice or drop chunks. Diagnostic projection omits internal references. Preserve lazy absent/empty-history execution; opted-in priority execution prepares all suites before any launch. An incomplete preparation schedule launches nothing. Diagnostic write failures are nonfatal.
- `scripts/lib/nightly-tests-impl.ps1`: extract preparation, chunk invocation and suite completion into dot-sourced helpers. The actual nightly entrypoint invokes the coordinator with no priorities, preserving operational ordering pending S3. The unchanged production outcome reducer is callable by the witness. The original chunk launch/observation body retains 270 nonblank lines unchanged ignoring indentation (corrected extraction receipt in scratch).
- `scripts/lib/test-phase-evidence.ps1`: replace quadratic UID membership with an equivalently case-insensitive HashSet and reuse a supplied discovery index. Add an internal diagnostic TRX reader clock seam. Production retains its existing 200ms budget; the harness supplies a stopped, unexhausted Stopwatch so diagnostic conservation does not depend on host load. Real readers, file/size checks and assertions remain exercised.
- `scripts/test-nightly-failed-first.ps1`, `tests/Antiphon.Tests/Scripts/RcFailedFirstPlannerTests.cs`, and `scripts/fixtures/nightly/c1514-failed-first-planned.json`: two exact-method portable witnesses, real planner/evidence/writer paths and canonical manifest golden bytes produced by the original writer. CP-3 has 10 harness assertions; CP-4 has 23. ProcessSpawnLimit applies.
- Both plan copies are amended identically with performance findings. The card-1514 copy is authoritative. CP-4 estimate increased from 2 to 9 minutes, S2 ordinary floor is 17 minutes, whole-plan floor 72 minutes (historical 65 retained). This is provisional n=1, not p95. Owned CP-4 process bound is 1080 seconds, enclosing bound 1140000ms; the increased budget is an explicit plan amendment, not a silent timeout change.

No definition file changed. No `-Update`, native qualification, desktop/C:\Antiphon access, supervisor/live d47848cc change, tag, deployment, branch switch, rebase, reset, amend or force push occurred. No instructions, execution-policy files, schemas or test-selection manifests changed. Public history flags and forwarding remain later work.

Verification

| Row | Exact method | Executed | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| CP-3 | RcFailedFirstPlannerTests.GlobalReplayPriority | 1 | 1 | 0 | 0 |
| CP-4 | RcFailedFirstPlannerTests.FullBytesAndCoverageConserved | 1 | 1 | 0 | 0 |
| CP-5 | RcFullWatchdogTests.CapFitsAdmittedPlannedCost | 1 | 1 | 0 | 0 |

Final checkpoint run `20261031-053849-e35f`, terminal exit 0. One isolated build group shared by three serial exact rows; tool reports seven project builds, max concurrent builds one. CP-4 replays 19,012 Antiphon nodes in 150 canonical chunks plus six companion-suite chunks: 156 launches per arm, 312 fake process-boundary launches total. It compares full keyed coverage, planned bytes, real journal/metadata writers, release authority and evidence policies. Private loopback authority fixture uses real local git/card API substitutes; this does not prove external publication or native process execution. Policy/diagnostic error matrices use a small fixture; full-size CSV throughput is not claimed. Full coverage/launch/bytes use the full source fixture.

Failure, missing, skipped, selected-class unknown UID, timeout, publication and teardown matrices exercise the actual outcome reducer. Fresh/stale phase JSONL and shared phase fixture bind latest timing with real JSON/CSV/Markdown comparison. No packing/pricing policy changed.

Historical outcomes and cost (retained; superseded runs are not green evidence)

- Initial committed red `35c5578d1ac050168b395797545570b3bd5518aa`: 8/10 assertions; two expected global-priority/cross-suite failures. Slot wait 165s, held 27s. `.antiphon/9058-red.log`.
- Full replay prototypes at `92350f1121950e2d1ec7dc29d5354c6a526c1af4`, `d9e4e947ba426016ec9541f72f0f7db6d390ab94`, and `3dc42495aac91536294a22864eace8f0cdf247a7` deliberately stopped/joined after 100/126/449 held seconds (waits 165/15/30). They established quadratic membership, repeated census and summary-matrix costs. No passing receipt claimed. Ownership checked by exact PID/start/cwd before stopping; children joined. Conservation logs retained ignored.
- Small diagnostics at `3507ffd0a67cdbaa50b1e8f1be406b857bf5bfeb`: 16/17, scratch-root comparison mismatch. Phase diagnostic at `a5d429deb6dc4979f877136edce835fa7e348b99`: 18/18, superseded. Real-outcome diagnostic at `88e435bc08bc90698cc60bb3d65c1eafaef7093e`: 17/18, invalid foreign-class unknown-UID fixture and legacy timeout expectation; corrected at `b6f6cb5f6af1b2a00204d6a5af300eb23a5d6ff4`, 18/18. No inherited product-test failure claimed.
- `20261031-044546-24e7`: CP-3 passed, CP-4 deliberately stopped for stronger evidence; incomplete. `20261031-045734-19c8`: zero tests, exit 2 stale build-source stamp, not assertion red; stamp retained and only task-owned output cleaned. `20261031-045838-2ce7`: stopped build before real-outcome strengthening; incomplete.
- `20261031-050444-7952` at b6f6cb5: CP-3/5 passed, CP-4 22/23. Assertion chose ordinary companion group1 instead of resolved priority group2 after prefix collision. Actual trace already placed priority at position4 before Antiphon at7; corrected expected identity in `06a76ec1f23f463b18dfd6cdfb7b45c7b7b3c2f8`.
- `20261031-052355-bae6` at 06a76ec: CP-3/5 passed, CP-4 22/23. Same TRX digest/real coverage but group33 diagnostic rows empty in one arm under 200ms read budget. Final deterministic diagnostic clock seam fixes witness while preserving production deadline and assertions. Final c2d6f95 run passed all three rows and source validation.

All build/test drivers used the host gate, TEMP=/tmp and isolated outputs; no unleased retry. Direct interpreter probes outside ordinary checkpoint list were explicitly red-first/performance/fixture diagnostics, not qualification credit. No full assembly or native tests. Executors and observers awaited before editing/settlement; all final clients exited and leases released.

Advisory retained under D-2: generic performance attribution says HTTP not used, but CP-4 private loopback authority fixture uses HTTP. Its HTTP attribution is unknown, not zero. No extra build/review rerun solely for this advisory. Collect next three ordinary receipts before claiming p95; no sample-only runs requested.

Land graph and git

Real `scripts/audit-land-verification-graph.ps1 -Check`, using actual audit-project output staged by fresh checkpoint build, passed exit0 in a 30s gated lease. Eight-file SHA inventory: `.antiphon/9058-land-tool-provenance.json`. Digest `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806`, count69, pass2 approved. Five known not-built projects listed in `.antiphon/9058-land-check.log`. No definition update.

Fresh rc `13565c5a480d8f30a52aa4679a5f1681ef2a8c70` merges without conflicts via `git merge-tree --write-tree`, tree `8e31a2ff27395b380a49f7c910a254568cce496a`. Master `256b861468518f1f18489798b2f109f48d78e0c8` has conflicts, including inherited conflicts already present at base; three additional add/add paths are the two amended plans and extended harness. This rc-based task does not claim master landability. No merge performed. Full counts recorded below from merge receipt.

Evidence diff guard over base..tested-source: 12 commits, zero generated-evidence entries and zero violations. Final report-only guard runs after publication. Generated logs/JSON/TRX/archives stay ignored; optional Markdown report is sole committed evidence narrative.

Task-owned `bin-failfirst-s2/` outputs deleted by checkpoint tool. Exact literal bootstrap paths `src/Antiphon.HostCleanup/bin-9058-tool` and `tools/Antiphon.Checkpoints/bin-9058-tool`, and copied audit output `.antiphon/9058-land-tool`, removed after last runs. No cleanup outside task-owned scratch.

Remaining work

Review S2 stable partition, permutation and extraction, including byte/coverage/metadata conservation and diagnostic clock seam. Red-first witness is not post-land Mutation credit. Planned method-scoped controls remain for no-op scheduler/coordinator, drop/duplicate/renumber/reprice, suite matching, lazy/barrier preparation and diagnostic IO guards.

S3 entirely unstarted: actual public failed-first history forwarding, opt-in complete discovery (including suites without exclusions), suite/run/attempt context and legacy heartbeat custody, CP-6/7/8. Preserve S1 fallback rules: missing/malformed/stale/different-candidate records fall back with stated reason; an ordinary failed chunk must not refuse. One unchanged legacy reducer detail needs an S3 witness: TIMEOUT followed by later passing chunk can reduce to TESTS. Current matrix preserves existing behavior; add canonical-terminal-timeout permutation case before wiring reordering so final-outcome conservation is demonstrated.

Then S4a CP-9/10, S4b CP-11/12, S5 portable CP-13/14 and Windows CP-15/16 remain. No Windows qualification claimed. Stop at green S2 boundary because budget does not support another complete slice.

Essential unedited checkpoint provenance and full pushed implementation SHAs follow.

```text
--- checkpoint report ---
run: 20261031-053849-e35f   manifest: /work/worktrees/task-9058bae5/.antiphon/checkpoints/20261031-053849-e35f/manifest.resolved.yaml
commit: c2d6f95582f041beaa33890af4d0ff83c83ee6a4  branch: feat/card-task-9058bae5  worktree: /work/worktrees/task-9058bae5  host: Debian GNU/Linux 12 (bookworm) cores=24
source: c2d6f95582f041beaa33890af4d0ff83c83ee6a4 state=clean buildSource=verified
CHECKPOINT CP-3 commit=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 build=ok filter=/*/Antiphon.Tests.Scripts/RcFailedFirstPlannerTests/GlobalReplayPriority executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9058bae5/.antiphon/checkpoints/20261031-053849-e35f/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=30s executed=40.609s wall=70.995s
PHASES CP-3 slotWait=30s build=435.5870513s startup=5.308958s testsWall=31.4385176s teardown=1.2082128s hostWall=37.9557024s
CHECKPOINT CP-4 commit=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 build=reused filter=/*/Antiphon.Tests.Scripts/RcFailedFirstPlannerTests/FullBytesAndCoverageConserved executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9058bae5/.antiphon/checkpoints/20261031-053849-e35f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=725.688s wall=725.953s
PHASES CP-4 slotWait=0s build=0s startup=8.5107458s testsWall=714.1771459s teardown=1.0491796s hostWall=723.7370713s
SLOW CLASS Antiphon.Tests.Scripts.RcFailedFirstPlannerTests 714s tests=1 (CP-4)
CHECKPOINT CP-5 commit=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9058bae5/.antiphon/checkpoints/20261031-053849-e35f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=9.206s wall=9.263s
PHASES CP-5 slotWait=0s build=0s startup=3.2748939s testsWall=4.3752936s teardown=0.6929572s hostWall=8.3431445s
BUDGET wall=1325.763s execution=1295.733s admissionOnly=30.029s
unlisted: none (the tool ran no other build or test command)
wall: 22m05s  sequential-equivalent: 13m26s  builds: 7  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-failfirst-s2/
evidence: /work/worktrees/task-9058bae5/.antiphon/checkpoints/20261031-053849-e35f/report.md
verdict: GREEN exit=0
```

```text
CHECKPOINT SOURCE VALID source=c2d6f95582f041beaa33890af4d0ff83c83ee6a4 rows=3
```

Merge conflicts: master=45, base against master=42.

Pushed implementation commits:

```text
35c5578d1ac050168b395797545570b3bd5518aa test: extract canonical dispatch seam and add global priority red witness
92350f1121950e2d1ec7dc29d5354c6a526c1af4 feat: stable-partition canonical chunk references and verify real execution conservation
c6ee48fe664a0f099d8cd725ca62ea0b7933dff6 perf: bound planned UID membership lookup for the full conservation replay
d9e4e947ba426016ec9541f72f0f7db6d390ab94 test: bind conservation to original manifest bytes and decoded journal identities
3dc42495aac91536294a22864eace8f0cdf247a7 perf: share immutable discovery and plan setup across order witnesses
3507ffd0a67cdbaa50b1e8f1be406b857bf5bfeb test: retain full launch replay and isolate the diagnostic field matrix
d7c8288a9d3c2a612c44e285f8c521ec8d4ef295 test: record measured replay budget and preserve diagnostic error identities
a5d429deb6dc4979f877136edce835fa7e348b99 test: bind phase summaries and latest timing to reordered execution
88e435bc08bc90698cc60bb3d65c1eafaef7093e test: exercise the production outcome reducer in conservation arms
b6f6cb5f6af1b2a00204d6a5af300eb23a5d6ff4 test: witness selected-class unknown UIDs and preserve timeout label reduction
06a76ec1f23f463b18dfd6cdfb7b45c7b7b3c2f8 test: anchor observed cross-suite priority to the resolved failure chunk
c2d6f95582f041beaa33890af4d0ff83c83ee6a4 test: bind diagnostic TRX conservation to a deterministic reader clock
```

--- next stage ---
next: review
handoff: Review completed S2 at tested source c2d6f95582f041beaa33890af4d0ff83c83ee6a4 with CP-3/4/5 green and source validation. S3 unstarted; report publication is narrative-only.
artifact: /work/worktrees/task-9058bae5/.antiphon/task-9058bae5.md
