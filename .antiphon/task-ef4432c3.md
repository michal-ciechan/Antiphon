# CARD-1450 Review round 1 (Final): CLEAN

Subject: Code 40db7ba9-080f-41a9-b799-c2c719f96b02, origin/feat/card-task-40db7ba9 tip ee7bf19c362b3f06a0bf6e2e30f3a30bbbf3dd14 (base master 72fada66545c2dc96a0710827fbc57558700dd61, 3 commits: 7a9fd9466 witnesses + stub, a86d7b438 FK-order test fix, ee7bf19c3 fix).
Verdict: no blockers, no regressions. Four advisories below (go to cards, never hold the land).

## Checks

(a) Retention parity. `SweepRefsAsync` lists `refs/antiphon/{land,progress}` once, groups by the 32-hex task segment, and for each
task with a row: skips non-terminal tasks entirely; for terminal tasks uses `AntiphonRefProtection.QueryAsync` (one batch) and
`LandRefRetention.RetainedOperationsAsync` + `IsDeletable` (the S2 code, unchanged), so latest-by-CreatedAt, ActiveLandingId and
every Refused landing keep recovery pins (`source`, `target-before`, `prepared`, `adopt/*`); protected tasks lose observations only.
Tasks with no row lose everything (plan D-5: no row = not protected). `refs/antiphon/retirement/*`, `start-ref-probe`, `salvage/*`,
`refs/heads` and malformed/D-format/upper-case task segments are never listed or never matched. In-flight/queued/Held/NeedsResolution
lands, interrupted Active landings (non-Abandoned) and open sync debts are protected by the shared set.

Adversarial cases (scratch class, detached worktree at the reviewed SHA, all PASS unmutated):
- R1 two repositories: Failed task, older Refused + middle + latest (no ActiveLandingId); sweep of repo B keeps latest and Refused
  (incl. `adopt/source`), deletes only the middle op; a Working task's pins and observation stay; repo A untouched.
- R2 two older Refused, ActiveLandingId on an older Complete, a newer Complete, a superseded PushStarted, and a Canceled task with an
  Active interrupted PushStarted landing: all retained ops kept; superseded, `source-recheck`, progress and the protected task's
  observation deleted; protected task's recovery pins and progress pin kept.
- R3 orphan task refs (no row) deleted incl. `adopt/*` and progress; malformed/foreign namespaces kept.
- R4 a ref moved between list and batch: batch fails on the old-value check, sweep returns null, nothing deleted.

(b) Batch: one `update-ref --stdin` of `delete <ref> <oid>` lines via `LandingGit.UpdateRefsAsync` (old-value guarded, all-or-nothing,
foreign namespace refuses before git runs); run under the child journal (CP-1 witness 2). Errors -> null, nothing deleted (R4).
Lease: the method takes and requires no repository lease (see A-1); it has no production caller yet.

(c) Fixture: per-test isolated schema; the first witness failed on the AgentTasks.ActiveLandingId <-> AgentTaskLandings.TaskId cycle
in one SaveChanges; a86d7b438 saves the landings first, then sets ActiveLandingId in a second SaveChanges. Deterministic.

(d) Scope: two files only (service + test class). No Hangfire registration, settings, migration, job or route.

(e) Mutants (env-gated, one build, method-scoped to RepositoryMaintenanceTests + the scratch class), each killed by
`C1450_sweep_keeps_latest_and_refused_operation_pins` with a Shouldly assertion:
ignore protection (2 failed), skip terminal check (2), drop retained ops (3), return names without UpdateRefsAsync (6).
Red commit a86d7b438 is a compiling stub returning [] (assertion red, not build red).

## Checkpoint run (receipt)

Run 20261110-051230-1256, plan `.antiphon/c1450-review-plan.md`, one build, verdict GREEN, `validate` SOURCE VALID rows=5:
- CP-1 RepositoryMaintenanceTests/* executed=2 passed=2
- CP-2 LandRefRetentionTests/* executed=39 passed=39
- CP-3 LandingGitTests/* executed=83 passed=83
- CP-4 RepositoryMutationLeaseTests/* executed=32 passed=32
- CP-5 guard union (TestLaneCategoryGuard|TestClassificationPolicy|TestClassificationGuard|SlowTestRegistryMerge|SlowTestTripwire|NativeLinuxPolicyWitness) executed=53 passed=53
Earlier run 20261110-045259-23da had the same counts, all passed, but went RED on my plan floor (Min 78 from the brief); the
Antiphon.Tests census of that union is 53 (TestLaneCategoryGuardTests is one method). Unlisted runs: the scratch build plus 5
`dotnet run --no-build` mutant/adversarial runs, each under its own build-slot lease (reason: adversarial cases and mutants).
check-evidence-diff 72fada665..ee7bf19c3: commits=3 entries=0 violations=0. merge-tree vs origin/master 72fada665: clean (8a10f787).

## Advisories (card them; not blocking)

- A-1 Lease not enforced. Plan D-4 makes the ref sweep a leased pass; `LandRefRetention.ReleaseAsync` takes a `RepositoryLease`
  as proof, `SweepRefsAsync` takes none. Unleased, a sweep could delete a protected task's observation pins mid-land and contend
  on packed-refs.lock. Fix: add a `RepositoryLease` parameter (or acquire `RepositoryLeasePurposes.Maintenance` in the S3 job before
  calling it) when the job is wired.
- A-2 Progress pins of terminal RefOnly tasks. The S2/D-4 rule deletes `refs/antiphon/progress/<task>/settlement-sync` for every
  unprotected terminal task; `RefOnlyCommittedSource` (DeliverableBundleService, AgentTaskReplyService) treats that pin as durable read
  custody and returns "committed source unavailable" without it. The sweep extends this to never-landed tasks within 15 minutes once
  scheduled. Pre-existing plan rule; decide whether a RefOnly settlement pin should be retained.
- A-3 N+1 query: `RetainedOperationsAsync` runs once per terminal task with refs; at the investigation's ~19,000-ref scale this is
  thousands of round trips per pass. Batch the landings query by task id.
- A-4 Brief figure: "TestLaneCategoryGuardTests* 78/78" does not match this assembly; the union is 53/53.

PCs: none discharged; every PC stays pending for SourceLanding Mutation.
