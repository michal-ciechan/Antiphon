# Review: CARD-1770 G-287 unreadable evidence fails closed (round 1, Final)

Review task 777ca96d (Claude Frontier) of Code task 39248db6-f873-4d78-ad31-68fb756290fe (Grok),
branch `feat/card-task-39248db6`, tip `34d383692e89dde6386f424e34fb40233a10b1cf`, range
`d0a3ee89e495baf364e8260abeab326db9c98f76..34d383692e89dde6386f424e34fb40233a10b1cf` (two commits:
red `23e38ffeffcbe63738eae98643dbe3b1e727c66b`, fix `34d383692e89dde6386f424e34fb40233a10b1cf`).
Master at review time `80cb252b9ea548e47e2fdaa28cd775f76ed60261`; `git merge-tree --write-tree
origin/master HEAD` is clean, tree `bc6ac5722d56d918b4f07efe33c83041a2362ff1`. Reviewed on server2
(Linux, uid 1654, .NET 9 test host, pwsh 7 for probes), load 42 on 24 cores at the start.
Security-relevant subject: the release publication authority gate (G-287).

## Verdict

CLEAN. No blocker. One advisory (A-1), not holding the land. PC-1 to PC-7 stay pending for
SourceLanding Mutation; the Code's restored local reds are not credit.

## 1. Fail-closed: how an empty resolver result is consumed

`Resolve-ReleaseAuthorityEvidencePath` has exactly one production caller,
`Test-ReleaseGateExecutionLedger` (`scripts/lib/release-authority.ps1:831-832`). It tests the
result with `[string]::IsNullOrWhiteSpace`, adds `chunk-evidence-escape:<chunk>` and `continue`s,
so no `Test-Path`, digest or TRX read ever sees the empty string and no path join happens on it.
Because the chunk is skipped before its UIDs are recorded, the same loop then adds
`uid-missing:` for each required UID of that chunk; this is the existing consequence the prior
G-287 variants already produce (they assert a subset, the new witness asserts the exact list).
`Reasons` flow unchanged through `Test-ReleaseGateAuthority` (`$reasons += @($ledger.Reasons)`,
`Ok = ($reasons.Count -eq 0)`) and `Test-ReleaseGatePublicationGate` (`foreach ($r in
$Authority.Reasons)`), and `publish-release.ps1:184-186` returns `ExitCode 3`, `Published false`,
`Refusal` = the joined list, after printing the `REFUSED: publication gate (...)` host line and
before any tag inventory, journal or remote write. No caller treats an empty result as "no
evidence needed" or as the root: a missing component is a different state (the resolver returns
the full path and the downstream leaf check refuses it as `chunk-evidence-missing`), and the empty
string never reaches that check. `scripts/test-release-gate.ps1` does not call the resolver
directly; the fixture `scripts/fixtures/c1731-evidence-path.ps1` casts to `[string]` and compares
with `-ceq ''`.

The new witness (`Test-C599A_ExecutionLedger`, CARD-1770 block) asserts all of: result object
present, `ExitCode -eq 3`, `-not Published`, `Refusal -ceq
'chunk-evidence-escape:antiphon-001,uid-missing:antiphon-u1,uid-missing:antiphon-u2'`, the exact
`[publish-release] REFUSED: publication gate (...)` line captured from the Information stream,
`publications.json` absent, and zero remote writes through the observer
(`Test-C599NoRemoteWrite`). The probe `Get-C599AAttributeDenial` must observe a non-not-found
attribute error before the publisher runs; a non-denied probe fails the row with
`attribute read was not denied`, it never skips. The `finally` restores the mode first, removes
the sealed file and directory by literal path, then restores the snapshot.

## 2. Refusal set and the new mid-component row

The pre-loop refusals (blank, `IsPathRooted`, lexical `GetFullPath` + `StartsWith(root + sep)`)
are byte-identical to the reviewed CARD-1731 tip. In the loop the `ReparsePoint` return and the
not-found skip are unchanged; the only behavioural delta is `throw` -> `return ''` for a
non-not-found attribute error. Linux probe on this host (pwsh, `File.GetAttributes`): a child
under a mode-000 directory throws `UnauthorizedAccessException` wrapping `IOException: Permission
denied` (innermost type decides, so it refuses); the mode-000 directory itself reads as
`Directory`; a missing child and a file used as a directory throw `DirectoryNotFoundException`
(skipped, as before); a NUL character throws `ArgumentException` (now refused instead of thrown).

The 14-row `C1731_EvidenceRefusals` set keeps all 13 prior rows with unchanged expectations and
adds `mid-link`: `evidence/mid-link` is created by `New-C1731DirectoryLink`, which on Linux is
`New-Item -ItemType SymbolicLink -ErrorAction Stop` with no fallback (the junction fallback is
Windows-only), so the row is a real directory symlink between `evidence/` and `x.trx`; the walk
reads `x.trx` (not a reparse), then `evidence/mid-link` (`ReparsePoint`) and returns `''`. The
expected values are computed from the candidate path independently of the resolver (no
self-compare). Direct gated run at the tip: 14 of 14 rows PASS including `C1731 a link in a middle
path component refuses` and `C1731 seeded evidence results match the recorded set`.

## 3. Catch narrowness

The `try` body is one `File.GetAttributes` call and a `-band` test that cannot throw. Only an
exception raised by the attribute read of the current component reaches the `catch`; the
innermost exception is classified, `FileNotFoundException`/`DirectoryNotFoundException` skip and
every other type returns `''`. That is the intended fail-closed direction for an existing
component whose attributes cannot be read; nothing else in the function is inside the `try`.

## 4. Note honesty

`docs/superpowers/mutations/2026-11-06-card-1731-candidate.md` now states: a non-not-found
attribute error returns `''` and the gate emits REFUSED; blank, absolute, parent-escape and
reparse refusals unchanged; no cache, staleness none, 300 s harness budget unchanged; the 15 s
walk ceiling still rests on one Linux sample (0.153 s) with the Windows sample outstanding; the
Windows-only shapes, native Windows execution of the seeded rows, the junction fallback are not
witnessed and the mid-component row was witnessed as a Linux symlink; the red SHA and that the
local reds are not SourceLanding credit (A-4 of the 1731 review reconciled in the opening
sentence); PC-7 with the exact method filter. Every statement matches the diff and the runs.

## 5. Performance

`C599A_ExecutionLedger` ran 40.2 s in CP-1 (one added publisher call plus a lock/probe) under a
load of about 40; the 1731 review measured 63.2 s at load 25 to 28 before this row, so the row
sits well inside the unchanged 300 s harness limit. The two-class row took 575.8 s against a
15-minute estimate; the guard row 133.5 s. No timeout was changed.

## Advisory

- A-1 (Windows fixture, not a blocker): the unreadable-directory witness's Windows arm (a deny
  ACE on the sealed directory) has not executed. If the first native Windows run of
  `C599A_ExecutionLedger` finds the attribute read not denied (a privileged account or inheritance
  that does not propagate), the row fails with `attribute read was not denied` by design; classify
  that red as a fixture signal under the already declared Windows obligation, not as a product
  regression. The missing Windows walk sample (A-3 of the 1731 review) remains open.

## Verification evidence

- Checkpoint run `20261108-052505-8b88` at `34d383692e89dde6386f424e34fb40233a10b1cf`, branch
  `feat/card-task-777ca96d` (mirror of the subject tip, working tree clean), `state=clean
  buildSource=verified`, verdict GREEN exit 0; `validate --expected-source-sha` printed
  `CHECKPOINT SOURCE VALID source=34d383692e89dde6386f424e34fb40233a10b1cf rows=2`.
  Plan extract `.antiphon/c1770-review-plan.md` (ignored). Output `bin-c1770-rv/` deleted by the tool.
- `CHECKPOINT CP-1 commit=34d383692e89dde6386f424e34fb40233a10b1cf build=ok filter=/*/*/(C599SnapshotTests*)|(ReleaseGatePublicationAuthorityTests*)/* executed=19 passed=19 failed=0 skipped=0 slot=granted waited=15s dirty=0 sourceState=clean buildSource=verified`;
  `CHECKPOINT-TIMING CP-1 waited=15s executed=575.762s wall=590.817s`. TRX identities: 4
  `C599SnapshotTests` methods and 15 `ReleaseGatePublicationAuthorityTests` methods, all Passed,
  including `Evidence_path_refusals_match_the_seeded_set` (6.4 s) and `C599A_ExecutionLedger` (40.2 s).
- `CHECKPOINT CP-2 commit=34d383692e89dde6386f424e34fb40233a10b1cf build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=75 passed=75 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified`;
  `CHECKPOINT-TIMING CP-2 waited=0s executed=133.542s wall=133.581s`.
- Build: one cold `tests/Antiphon.Tests` build inside the run (326 s). The checkpoint tool itself
  was built once through `build-slot.ps1` (lease `4ac8118e-1b70-45af-94e1-9e281c1dcbe1`).
- Unlisted runs, each with a reason and its own lease: the two harness cases were run directly
  through `build-slot.ps1` because TUnit hides harness console on a pass, so the named rows are
  not in the TRX: `C1731_EvidenceRefusals` (lease `200a5cf1-139a-49b9-ab8c-925cfc044659`,
  `C487: 14 passed, 0 failed, 14 rows`) and `C599A_ExecutionLedger` (lease
  `7cc196f8-be9b-40a5-b6fb-4e57ac445e4a`, `C487: 6 passed, 0 failed, 6 rows`, including `PASS
  C599 G-287: an unreadable evidence directory is refused on a publication line and nothing is
  published`). Results directories were in the session scratchpad, outside the repository.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef d0a3ee89e4… -HeadRef HEAD`:
  `commits=2 entries=0 violations=0`, exit 0.
- Not rerun: the whole Unit lane (owned by the nightly backstop and rc qualification; the diff has
  no compiled production code) and the twelve other release-gate classes that dot-source
  `test-release-gate.ps1`, which drive readable evidence where the resolver's return value is
  unchanged. `GET /api/runner-defaults` and `GET /api/session-runners` were read; nothing is pinned.
- PC-1 to PC-7 stay pending for method-scoped SourceLanding Mutation.
