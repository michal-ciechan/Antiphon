# CARD-0824 S4a integration onto master (task 5ec136af)

Subject: the reviewed S4a stack, single branch `origin/feat/card-task-bb0a6ce8` tip
`f458326474217fa34a836aabc15cbd6c1b4ba484` (merge base with master `37d92a8a0`), replayed onto master
`1a22e0d2ca7b32e7f6ac346a658e1c7824252a46` on `feat/card-task-5ec136af` by `git cherry-pick -x`, linear, no
merge commits. The source branch is untouched. S4a stays default ON in code and is not activated in any host by
this work (no configuration, deployment or host file changes).

## Replay

`git cherry origin/master origin/feat/card-task-bb0a6ce8`: 48 commits, all `+`. Picked 41 in
`rev-list --topo-order --reverse` order. Skipped 7:

- report-only `.antiphon/*` commits: 82df1362c, 45f1aaee8, 88e9fc746, c9d9d7a63, 3dcd5965b, f45832647;
- 2291dbfca (B side): adds `docs/investigations/2026-11-04-s4a-independent-audit-b.md` byte-identical to the
  already picked ae99c06ae (A side); it would be an empty pick;
- e893ad24c, the A+B merge: not picked as a commit. Its only hunks beyond its two parents are the
  `GuardedWorktreeRemoval` seam, reproduced exactly while picking 35bb34ad7 (below).

The docs/investigations review/audit records and the `docs/orchestration-loop.md` edits of the stack are kept.
The stack adds no EF migration, no csproj, no `tests/test-execution-policy.json` edit and no host configuration.

Patch-ids are `git patch-id --stable` (first 12 hex) of the source and the replayed commit. Equal ids mean the
same patch; the five differing ids are the three conflict picks and two clean picks whose context drifted
(7846f289f and 2f66c1bbe auto-merged in `TaskWorktreeRetirementService`/`RemoteWorkspaceService` around the
S5b-1 lines).

| source | replayed | source patch-id | replayed patch-id | conflict |
|---|---|---|---|---|
| ae99c06ae | 2cfaa79a9 | 3519ed308b18 | 3519ed308b18 | no |
| fca103d84 | 1b92a522b | 53ac91fd5137 | 53ac91fd5137 | no |
| 73085fe7d | 771f57b03 | b35556656207 | b35556656207 | no |
| 926db578c | eb30f38f8 | c07bca8bf4c9 | c07bca8bf4c9 | no |
| f16271149 | 3f3055368 | c652a59fdaf0 | c652a59fdaf0 | no |
| f9dea6504 | 04b33eaef | d68ae8e7f1bc | d68ae8e7f1bc | no |
| 467b00c25 | 4da21b87f | 35cdd7d6c944 | 35cdd7d6c944 | no |
| fc1afd467 | 3f8b0f4d3 | b4b0ed02256e | b4b0ed02256e | no |
| b2c53c784 | bf126465b | e8f390f7b9e0 | e8f390f7b9e0 | no |
| 841898902 | c8035d408 | 3ad0896ea023 | 3ad0896ea023 | no |
| feec602e7 | 7b7c8d7a7 | f1a4eed3a722 | f1a4eed3a722 | no |
| 3ec62c653 | e3e64893e | 827fbab75ef2 | 827fbab75ef2 | no |
| 633e7edbe | 21149fbb9 | bd79966cb19c | bd79966cb19c | no |
| 4ade77ce8 | f7dad7e5a | b99ed980e7b2 | b99ed980e7b2 | no |
| d52a17392 | 6c4b28172 | 25a29afa4565 | 25a29afa4565 | no |
| 7f6d67036 | b13afc2f4 | 86abde32388f | 86abde32388f | no |
| 356a781b3 | 044f7c535 | 6ccdb0569ea2 | 6ccdb0569ea2 | no |
| 7846f289f | a0e29dd04 | 609749cce2e8 | 7a104f8582cc | no |
| 96d7cde36 | 883fb45ce | 1e9fbf5573e0 | 1794830b6a90 | CONFLICT |
| 2bf599114 | 4a8abfe1b | c9a3a038916c | c9a3a038916c | no |
| 14b6b97c0 | 488f37182 | 7acbcbd9496a | 7acbcbd9496a | no |
| 35bb34ad7 | 52c3f1751 | fb5fb01a02c4 | ac7ac05d583a | CONFLICT |
| 726781dbb | e1861f7f5 | 347b434da2a8 | 347b434da2a8 | no |
| 2637c2328 | f54ff3176 | b41fc8f2f1bd | b41fc8f2f1bd | no |
| a4dca6709 | 31434ba60 | 14239ba847fa | 14239ba847fa | no |
| 76d67c776 | aba1f2cfd | 82b03ceca317 | 82b03ceca317 | no |
| b3873bd12 | 3f168ae2f | 70af87dedffb | 2be2ef9a251a | CONFLICT |
| 4613c5cf1 | 2c27d7b7b | 3a7010340e35 | 3a7010340e35 | no |
| 14d6cbd1e | e80e98dc9 | aadfa6b62b39 | aadfa6b62b39 | no |
| a920fe383 | 901b3c3b4 | c8f4175d2c49 | c8f4175d2c49 | no |
| 80e5686e7 | adf87bdc6 | 812ba5ed6bf8 | 812ba5ed6bf8 | no |
| 2f66c1bbe | 839d798cd | a07fe39622a3 | 66d6fac10039 | no |
| 5b181db47 | b9b81c91e | 5f858f310933 | 5f858f310933 | no |
| 4f3164999 | 29bcfa41b | b90ed2e1a7ca | b90ed2e1a7ca | no |
| 5c0ed9f10 | 558c0accb | ab488ea035d4 | ab488ea035d4 | no |
| bf9159aa1 | 82fc3d991 | dd7609365291 | dd7609365291 | no |
| 585edc1d9 | 1f4b4cd76 | d8a24da8f64f | d8a24da8f64f | no |
| a694ad987 | 32eb3a841 | 0b86f84fba5c | 0b86f84fba5c | no |
| 37988b2de | 2859f4864 | aa2768b26176 | aa2768b26176 | no |
| 7468bd326 | 68272124b | 5ef089c11814 | 5ef089c11814 | no |
| fa9b794b9 | 8c519a759 | f7cdebf63c30 | f7cdebf63c30 | no |

Post-replay tip `8c519a759`. Equivalence check: for every file the stack touches, a 3-way
`git merge-file (master, merge-base 37d92a8a0, stack tip f45832647)` equals the replayed file, except the three
files below, which differ exactly in their one conflict region each. `GuardedWorktreeRemoval.cs` (also edited by
S5b-1, ea89b8436 RefOnly owner fences) composes without conflict.

## Conflict resolutions

### 1. `server/Application/Services/DesktopLegacyReleasePolicyService.cs` (pick of 96d7cde36)

- Master (S5b-1, ea89b8436): in `ActAsync`, after the pre-check, a `before:legacy-action` boundary, re-read of
  `fresh`, and a hold when `DesktopWorkspaceCleanup.Reason(fresh)` names a RefOnly/non-Checkout task, then the
  `unsafe:legacy-action` boundary.
- S4a (CARD-1717 D-1): after the pre-check, re-read the switch and the tick budget (`StopReason`) and write a
  Deferred row with the stop reason, before filing or `TryRetire`.
- Resolution: both, S4a switch first, then master's re-read and hold. With the switch off the row is Deferred with
  the switch reason and nothing else happens (no hold stamp, no filing, no removal), which is the S4a off-switch
  contract (turning off must not stamp a refusal). With the switch on the order is irrelevant to the S5b-1
  witnesses: `StopReason` returns null and master's re-read/hold runs unchanged before any action. The executor
  re-reads the switch again under the repository lease, so the re-read `fresh` cannot carry a stale switch.
- S5b-1 Review advisory A-1 (revision token of a Checkout task re-read at ~298/~405, here the re-read before
  `ReleaseSystemAsync`, which files with `fresh.ConcurrencyToken`): unchanged by this integration. The filed
  revision is the re-read one, as on master; `GuardedWorktreeRemoval` re-proves the tree under the lease before
  any delete. Left as the S5b-1 advisory it was; not repaired here.

### 2. `server/Application/Services/GuardedWorktreeRemoval.cs` (pick of 35bb34ad7, the A+B seam)

- A side (CARD-1717 A, already picked): `CustodyOf`/`DeleteApproved` content-bound deletes, `LateUntrackedAsync`,
  `BindContentAsync`.
- B side (35bb34ad7): `LiveAuthorizationAsync` re-reads before each destructive continuation.
- Resolution: the reviewed merge e893ad24c's own resolution, applied as `git merge-file (HEAD, A tip 82df1362c,
  e893ad24c)`, clean: resumed set-aside reads the switch then custody then `DeleteApproved`; the final-inspection
  read moves after `BindContentAsync` (last await before receipt and move-aside); after unregister the switch read
  precedes `CustodyOf` and `DeleteApproved`. Master's RefOnly owner-row fence lines in this file are unchanged.

### 3. `server/Application/Services/RemoteWorkspaceService.cs` and `TaskWorktreeRetirementService.cs` (pick of b3873bd12)

- `RemoveMirrorAsync`. Master (S5b-1): `BeforeRemoveMirrorCommand?.Invoke()` test hook just before
  `RemoveWorkspaceAsync`; S5b-1 witnesses count it as "a remove command is sent". S4a (B-B-1): the live switch read
  after the runner description, refusal keeps the mirror and records residue. Resolution: switch read, then the
  hook, then the send. A refused send is not counted as a command; nothing awaits between the read and the send.
- `RemoveRemoteMirrorAsync`. Master added `RefuseDesktopReleaseAsync` just above it; S4a changed its signature to
  take the live gate. Resolution: keep `RefuseDesktopReleaseAsync` and the S4a signature.

## Semantic seam found after the textual replay (S4a x S5b-1)

S5b-1 added a NotRequired (RefOnly) branch at the top of `TaskWorktreeRetirementService.TryRetireCoreAsync` that
retires a separately owned runner mirror and returns before the lease-time S4a admission. It still called
`RemoveRemoteMirrorAsync(owner.Id, ct)` (a compile break against the S4a signature, fixed by passing no gate in
a4aff79c0, master's behaviour). With that, a `system:desktop-legacy-policy` release on a RefOnly task removed the
mirror while `WorktreeLegacyRelease` was off: a deletion with the switch off.

- Witness (red first, 22bb9eb8d): `C0824RefOnlyCleanupTests.Remote_cleanup_still_uses_its_binding_and_generation`
  now runs its retirement with `WorktreeLegacyRelease:Execute=false`, first as the system caller (no mirror remove
  command, mirror kept), then as `operator` (master's original assertions). Red at a4aff79c0:
  `s4a-switch-off-sends-no-mirror-remove` (1 failed).
- Fix (f28571c8e): for a system release that branch reads the switch (`StopLegacyContinuationAsync`) before the
  remove and passes the live gate to `RemoveMirrorAsync`, as the S4a mirror continuation does. Caller-filed
  releases are unchanged. Green on the same method (1/1).

## Off-switch inventory after integration

With `WorktreeLegacyRelease` off (`Enabled=false` or `Execute=false`): no automatic filing (policy `StopReason`
and `LegacyReleaseAdmissionAsync(pace:false)`), no removal (lease-time admission, `LiveAuthorization` before
move-aside/unregister/set-aside delete/branch-delete child start), no set-aside, no mirror remove (both mirror
paths now gated), no salvage (`WorktreeReflogSalvage` runs only inside a system removal after admission), no
pruning (prune reads the live switch under the repository lease, 585edc1d9). DeleteSettled/no-follow fd-relative
delete, ContentSeal set-aside records, IgnoredSecretFloor patterns, RunnerWorkspaceRetention and reflog salvage
refs are carried unchanged from the reviewed stack (no conflict touched them).

## Migrations, policy, digests

- EF: the stack has no migration and no model change; master's chain (S5b-1 `20261105110702_Card0824RefOnlyDesktopCoordinates`,
  `20261105121558_Card0824RefOnlyDesktopCleanup`, CARD-1591 S1) is untouched; snapshot unchanged.
- `tests/test-execution-policy.json`, nightly policyHash, live-lane PinnedInputDigest: inputs unchanged, not
  recomputed. No csproj added or changed: build-graph digest untouched.

## Verification

Checkpoint plan `docs/superpowers/plans/2026-11-06-card-0824-s4a-integration-checkpoints.md`; results in the Code
report `.antiphon/task-5ec136af.md`.
