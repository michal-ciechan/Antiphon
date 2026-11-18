# CARD-1981 S2 lease TMPDIR: Review round 1 (Final)

Review task dd9a3bb3 (Claude Opus; author Grok). Subject `origin/feat/card-task-a7e556cc` at
`05802489ac0f43715a69c342b4429e07da5e0ecf`, range `6414a71967575b2a7dfbf283ee273f930b7a2100..05802489ac`
(5 linear commits). Code landing owner: task a7e556cc-7117-4f56-9879-f6a6406296e5.

## Verdict

One blocker (B-1, guard regression). Every other point is advisory. The delete path is safe.

## Rerun (one checkpoint-tool run 20261118-230527-4d15, server2, at the subject SHA, sourceState=clean, buildSource=verified)

    CHECKPOINT CP-1 build=ok filter=/*/*/BuildSlotScriptTests/C1981_LeaseTempDirConfinesSdkScratch executed=1 passed=1 failed=0 skipped=0
    CHECKPOINT CP-2 build=reused filter=/*/*/C1981LeaseTempDriverTests/* executed=2 passed=2 failed=0 skipped=0
    CHECKPOINT CP-3 build=ok filter=/*/*/C1677TempSweepNativeTests/C1981_dotnet_sdk_empty_accepts_only_the_exact_shape executed=1 passed=1 failed=0 skipped=0
    CHECKPOINT CP-4 build=reused filter=/*/*/C1677TempFamilyRegistryTests/Every_family_producer_still_contains_its_prefix executed=1 passed=1 failed=0 skipped=0
    CHECKPOINT CP-5 build=reused filter=/*/*/C1677TempSweepTests/Unregistered_random_and_process_names_aggregate_by_prefix executed=1 passed=1 failed=0 skipped=0
    CHECKPOINT G-1 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=53 passed=51 failed=2 skipped=0
    CHECKPOINT G-2 build=reused filter=/*/*/C1677TempFamilyRegistryTests/* executed=3 passed=3 failed=0 skipped=0
    unlisted: none   builds: 2   rows: 6 green 1 red   verdict: RED exit=1

Other: `check-evidence-diff.ps1 -Repository . -BaseRef 6414a719 -HeadRef 05802489ac`: commits=5 entries=0
violations=0. No `/tmp/aslot-*` directory remained after the run or after a wrapped `dotnet build`.
`git merge-tree --write-tree origin/master(709c41f16c) 05802489ac`: conflicts only in
`tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs` (family count, master 20 + this 1 = 21)
and `server/Application/Services/LandVerificationBuildGraph.{evaluation,manifest}.txt`. Not resolved; the replay regenerates.

## Blocker

**B-1. NativeLinuxPolicyWitnessTests turns red: 2 failures.**
- Where: `tests/test-execution-policy.json`. The new `[NativeLinuxOnly]` methods `BuildSlotScriptTests.C1981_LeaseTempDirConfinesSdkScratch`,
  `C1981LeaseTempDriverTests.C1981_dotnet_child_tmpdir_is_removed_with_the_lease` and
  `C1981LeaseTempDriverTests.C1981_lease_dispose_does_not_follow_a_replaced_symlink` have no policy row.
- Failure: `Markers_do_not_expand_policy_exclusions` and `Policy_rows_require_pinned_attribute` fail with
  `reverse-coverage suite=antiphon row=BuildSlotScriptTests ... C1981_LeaseTempDirConfinesSdkScratch()` plus the two
  C1981LeaseTempDriverTests signatures. Those are the only diagnostics. All three methods are added by this range, so the red is not inherited.
- Why: this is a repository guard regression. The plan's "No guard union" was wrong: a `[NativeLinuxOnly]` marker is a policy input.
- Fix: add `platform: windows` Linux-only rows (suite `antiphon`) for the BuildSlotScriptTests method and the
  C1981LeaseTempDriverTests class/methods (owner CARD-1981). Reseal the policy digest the witness reports. Rerun
  `NativeLinuxPolicyWitnessTests/*` plus CP-1..CP-5. The session-runner method is already covered by the C1677TempSweepNativeTests class row.

## Judgements requested by the brief (no blocker)

- **Delete path (lease dispose).** The path must match `^/tmp/aslot-[0-9a-f]{8}\z`. statx with AT_SYMLINK_NOFOLLOW
  must see a directory, not a symlink. The inode and device must equal those recorded at create. The `.lease`
  marker must be a regular file holding this lease's random token. Only then are the children removed. A child
  symlink is unlinked, never followed. So the dispose cannot delete another lease (its token differs), a
  replaced symlink, or a foreign path. A different user cannot place entries inside a 0700 directory. Nested
  worktrees under the lease dir are the leased command's own temp and are deleted with it, which is the intent.
  Residual: a same-uid swap of a child dir for a symlink between statx and enumerate (advisory A-4).
- **Symlink test (CP-2 second method), not run red first.** Reading it as mutations: it goes red only if *all* root identity
  checks are removed, i.e. a naive "enumerate children and delete" dispose. No single-guard mutant turns it red:
  the IsSymlink, inode and marker checks each independently stop the delete (statx nofollow makes IsSymlink
  redundant with IsDirectory). So it is a valid whole-guard regression witness, not a per-guard pin.
  Mutation checks are waived (operator 2026-11-12), so this is acceptable.
- **Rewritten assertions.** Registry 12 to 13 is legitimate. The producer-null exemption still requires `DefaultEnabled` false
  for both exempt families. CP-5 is legitimate for the new family, but it gives up the aggregate `<random>` census row for six-char
  names (A-2). Neither rewrite weakens a deletion guard.
- **Red evidence only in the transcript.** Acceptable for round 1. CP-1 and CP-2 cannot pass at base because the wrapper set no TMPDIR. CP-3 and CP-4
  reference `RunnerTempSweep.DotnetSdkEmpty`, which does not compile at base.
- **Sweep family `dotnet-sdk-empty`.** It ships `DefaultEnabled: false`. Before a removal it requires: a top-level six-char name,
  the walk passed (symlink refused), mode exactly 0700, owner uid 1654 *and* the runner uid, empty, at least 1 h old,
  a holder scan, and the CARD-1677 no-follow inventory delete. The `aslot-` names never match it, so a live lease is safe.
- **Server change.** It is needed for the land verifier's `dotnet` children on the Linux server2 server. It is safe:
  the lease is created only while a build slot is held and is flowed through an AsyncLocal, never the process environment.
  It is disposed in finally, and a creation failure returns null and keeps the ambient temp. It does change the land graph
  (new Compile item, records refreshed in 05802489ac; they conflict with master's refresh and are regenerated at land).
- **Non-Linux and unwritable /tmp.** `TryCreate` returns null off Linux. The ps1 helper returns null on Win32NT. Any
  create, chmod or statx failure returns null (the build keeps the ambient temp) and never fails the build.
- **Killed lease holder.** If the wrapper or driver is SIGKILLed, the `aslot-*` dir and its contents leak. Nothing sweeps them:
  there is no `aslot` family. This is not a regression, because the same content previously leaked at /tmp top level (A-1).
- **Master movement.** S2 does not depend on CARD-1981 S1 or CARD-1987. Master's new S1 families do not match six-char names, so nothing is shadowed.

## Advisories (cards; do not hold the land)

- A-1. Register an `aslot-<8hex>` sweep family that validates `.lease`, with the same uid/mode/age rules. Killed leases leak otherwise.
- A-2. Six-char dirs now produce one capped `family_disabled` kept row each, instead of one `<random>` aggregate row.
  With about 21k entries they crowd `MaxReportedKept` and hide other refusals. Consider aggregating disabled-family rows.
- A-3. The sweep's emptiness check is not re-asserted against the delete-time inventory. A file added after `SdkEmptyRefusal`
  would be deleted. The family is off; re-check `entries.Count == 0` before `DeleteOwnedAsync` before enabling it.
- A-4. `RemoveChildren` recurses by path after statx (same-uid TOCTOU). Consider an fd-relative walk if this is ever reused for shared roots.
- A-5. If statx is unavailable (seccomp), `TryCreate` creates and then cannot abandon up to 8 dirs per lease (`Abandon` needs statx). Fall back to a plain rmdir of the just-created empty dir.
- A-6. A long-lived build server started inside a lease (MSBuild reused node, VBCSCompiler) keeps a deleted TMPDIR.
  Roslyn and MSBuild pipes use `/tmp` directly, and MSBuild resets its temp dir per node configuration, so no failure is expected. Not observed.
- A-7. The `build-slot.ps1` lease temp is created via `Add-Type` of a 196-line C# file on every Linux `dotnet` invocation.
  This is a per-build compile cost; measure it under the performance standards.
