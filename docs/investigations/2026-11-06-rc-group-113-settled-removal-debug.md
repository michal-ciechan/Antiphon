# rc group 113: C1714 under-limit removal diagnosis

Task: `14afd1ae`. Linux reproduction completed successfully. The leading diagnosis is a Windows-specific test-fixture defect: the test raises the admission limit to 400 characters although the native Windows deletion path documents a 260-character limit. No product or test repair was made; native Windows confirmation and red/green evidence remain outstanding.

## Evidence and classification

The supplied Windows qualification evidence is run `472c7495`,
`antiphon-all-group-113.trx`, candidate `rc-2026-11-07-1038d07e66a5`, reported checkout
`2271e6aa`. Its one supplied failure is
`SettledWorktreeRemovalTests.C1714_Path_past_the_delete_limit_refuses_before_the_move(under)`:
`IsClean` was false, with residue `worktree_removal_incomplete`. This task did not access
the desktop or its TRX. The checkout abbreviation is unavailable in this mirror; the supplied
record does not include `WorktreeRemoval.Detail` or a native error code.

The relevant test is at `tests/Antiphon.Tests/Infrastructure/SettledWorktreeRemovalTests.cs:735`:

- `const int limit = 400` replaces only `WorktreePathLimit`, not the native I/O implementation.
- `SetAsidePath` adds 27 characters: the leading dot, `.removing-`, and 16-character key.
- The under case creates a 372-character original file path, becoming 399 characters after
  the move. The other case creates 373, becoming 400, and expects a pre-move refusal.
- Both cases create real ignored `obj/` content. File creation and snapshots use .NET I/O;
  their success does not establish that a subsequent native no-follow open can delete it.

`WorktreePathLimit.cs` documents `WindowsMaxPath = 260`, including the terminator boundary:
259 fits; 260 does not. Its default is 260 on Windows and unrestricted on Linux. Its walker
uses extended-length paths on Windows, so it can inspect paths the native delete cannot open.
The injected 400 bypasses that default and admits the under case at 399.

`WorktreeNoFollowDelete.OpenNoFollow` calls `CreateFileW` on the plain path, without a
`\\?\` prefix. `DeleteWindows` recurses through held handles and marks each entry deleted,
without following reparse points. Linux uses a separate descriptor-relative `openat` /
`statx` / `unlinkat` walk; 399-character paths do not approach its platform limit.

Classification: the source exposes an invalid Windows fixture precondition, and the reported
Windows failure is consistent with it. No Linux logic defect reproduced. A file lock/AV hold
cannot be conclusively excluded from the supplied residue alone. Native Windows settings,
the actual failing operation, and its error code have not been observed here. This is not a
confirmed master fix or native Windows green result.

## Production path, ownership, and retries

`SettledRemovalHarness` constructs a real Git checkout and isolated PostgreSQL retirement
receipt. Its request passes through `WorktreeManager.TryRemoveAsync` into
`GuardedWorktreeRemoval.RemoveDirectoryAsync`. The manager checks managed-root containment;
the removal validates the lease, durable receipt, registration, source identity, remote
containment, and two content inspections. The path check measures the future set-aside name
and refuses before moving at `length >= limit`.

After admission, the service records its receipt, moves the tree aside, unregisters its Git
administrative entry, then deletes the approved tree without following links. An I/O failure
during deletion returns `worktree_removal_incomplete`, retaining the receipt and set-aside
bytes for a later pass. The exception branch puts `delete_error_<code>` in `Detail`, which
the failing assertion currently omits. Other incomplete branches also exist, so residue alone
does not identify the syscall.

Rename and settled-delete retry policies use five attempts with 100 ms pauses. Rename retries
Windows sharing, lock, and access conflicts; whole-delete retries codes 5/32/33/145 and visible
post-delete residue. The native open itself retries error 32 only. An overlong path is not a
transient sharing hold. Retries neither extend path support nor grant new deletion authority.
No timeout, assertion, lease, ownership, or production policy was changed.

## Changes since the rc candidate

Compared `1038d07e66a54b630a1bc1d394923bc7049b73dd` with fetched master and tested base
`d164e93ae47a79ed175beeb9367212f7a7d5d1d5`. Master remained at that SHA after fetch.
The test, `WorktreePathLimit.cs`, and `WorktreeRenames.cs` are byte-identical across the range.
Their respective Git blob IDs are:

```text
8101b0a1b24bcd1268f00efd811adc04248de085
1c9c2cc1c6fa6e6a4a98355fed5a577ba534c9cf
3e9f7760dd04e32d8027c014a3f7ce0e49c3356a
```

The CARD-1717/S4a stack adds sealed retry custody, retained-evidence checks, late-untracked
refusal, directory identity/content manifests, commit/index binding, per-directory approval
checks, live-switch checks at destructive continuations, clean-form tracked-byte comparison,
and Linux descriptor-relative deletion. Later changes add reflog salvage and verdict-only
in-process dirty checks. Relevant commits include:

```text
f7d360dd2c81246d75648d13eb53e46e1e5a0408 sealed retry custody
341197a4b21a581f10ab784779f9e25bb877da2a Linux descriptor-relative deletion
ddd75783cbac8662db493fd017c33b3e28972d44 directory identity and content binding
60cb6015bf2e6069d63ebe9eb13ee84fa986acc5 commit/index and per-directory checks
33642b314859e04198fdaab67cd60e8ee994a99f live-switch continuation checks
2cbd0c8f12d30076e9103dbded8289a631c47058 Git clean-form comparison
0a867479ae7d4be99a016d309603af17e07f15ad final destructive-step switch checks
7ad1ee9aec23f4d0d877bb73bcd1a03230bba2e9 reflog salvage
```

None fixes the fixture's 400-character admission limit or adds extended-length normalization
to the Windows deletion open. New native entry inspections can reject an overlong fixture
earlier and change its residue; native master behavior remains unverified.

## Linux execution

Build and test both verified clean tracked source at
`d164e93ae47a79ed175beeb9367212f7a7d5d1d5`, with native default outputs,
`UseAppHost=false`, and `TEMP=TMP=TMPDIR=/tmp`. Each command held its own slot. Build completed
with zero errors and 1803 warnings; no source repair was attempted for inherited warnings.
The explicit outer bounds were 40 minutes for the build and 30 minutes for test admission
plus execution. The test used a 15-minute admission bound and 15-minute driver timeout;
existing test timeouts were unchanged. These are provisional loaded-host operational bounds,
not measured-p95 claims.

```text
BUILD SLOT granted lease=2de2382a-36c1-44a4-8cee-8b765071370e waited=195s maxcpucount=6
Build: 0 Error(s); Time Elapsed 00:10:09.10
BUILD SLOT released lease=2de2382a-36c1-44a4-8cee-8b765071370e held=611s
BUILD SLOT granted lease=9e3081d0-7df1-49d8-b7d0-fbedba82a63f waited=60s maxcpucount=6
Test: total 82; succeeded 81; failed 0; skipped 1; duration 46s 496ms
BUILD SLOT released lease=9e3081d0-7df1-49d8-b7d0-fbedba82a63f held=50s
```

Platform: Debian 12, linux-x64, .NET 9.0.22, TUnit 1.44.0.0 / MTP 2.2.2.
Selection was exactly `/*/*/(SettledWorktreeRemovalTests*)/*`, run once. The only skipped
case was `C459_ManagedRootRequired(junction)`, explicitly requiring Windows junctions.
Observed per-case p95 was 0.5636321 seconds and maximum 2.958907 seconds (one Linux sample).

| Relevant case | Outcome | Seconds |
| --- | --- | ---: |
| C1714 path: under | Passed | 0.5793124 |
| C1714 path: over-once-set-aside | Passed | 0.5105265 |
| C1714 warned status: reading 2 | Passed | 0.2722527 |
| C1714 warned status: reading 1 | Passed | 0.1993850 |

Opt-in phase run `debug-14afd1ae`, group `SettledWorktreeRemovalTests`, attempt 1, suite
`Antiphon.Tests` retained 82 completed case records and two shared fixture records.
Assembly MVID: `5925d94ffb314d769b293d47f45eaf93`.
TRX SHA-256 before mandatory output cleanup:
`BAD2A9816968FD9A971B0BA7108B1E56C331D09275B268438C3C40BF3403A09B`.
Raw slot logs and phase JSONL remain ignored under `.antiphon/debug-14afd1ae/`; the
per-case result/duration table below preserves the TRX results independently of cleanup.

For the under case, the phase recorder measured 579 ms: exclusive database 28 ms,
process 140 ms, remainder 411 ms, no incomplete record. Filesystem, host, network, and
fixture-exclusive buckets were zero instrumented time; uninstrumented work remains in the
remainder. This does not establish zero filesystem work. The second path case measured
511 ms: database 30 ms, process 105 ms, remainder 376 ms.

Reproduction commands, from the assigned worktree (set the temporary-directory variables
above; optional phase variables described above):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-14afd1ae-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:UseAppHost=false --no-incremental
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-14afd1ae-class -SlotWaitMinutes 15 -- dotnet run --project tests/Antiphon.Tests --no-build --property:UseAppHost=false -- --treenode-filter '/*/*/(SettledWorktreeRemovalTests*)/*' --timeout 15m --report-trx --report-trx-filename run.trx --results-directory /work/worktrees/task-14afd1ae/.antiphon/debug-14afd1ae/test-results
```

No new local RED, fix SHA, or Windows GREEN exists. Tested SHA is the assigned base above.
The reported historical Windows red belongs to the supplied run; it was not independently
rerun here. Batched repair guards were not run because there was no repair. No green row was
rerun. No full-suite qualification or native Windows acceptance is claimed.

## Smallest next step

Commission a Codex Debug on an approved isolated Windows runner, outside the desktop.
Run only `/*/*/SettledWorktreeRemovalTests/C1714_Path_past_the_delete_limit_refuses_before_the_move`
at the unchanged base, exposing `result.Detail` as well as `Residue` in failure diagnostics.
Record the native error and whether the failure is before or after unregister. If path
opening is confirmed, the small fixture repair is to use `WorktreePathLimit.WindowsMaxPath`
instead of 400, preserving both assertions: 259 proceeds and 260 refuses before the move.
Witness native RED before that change and GREEN after, then run the touched class and the
brief's single batched guard selection, evidence-diff, and merge-tree checks. If it is a
sharing hold at a supported length, retain that distinct diagnosis instead of changing retries.

Decision for caller: which approved non-desktop Windows runner can provide this native
red/green follow-up? Do not credit group 113 Windows qualification from this Linux result.

## Restoration and delivery

No tracked production or test files were changed or mutated. The initial output inventory
contained no `bin`, `obj`, or `TestResults` directories. After both leased commands exited,
all 72 generated `bin`/`obj` roots and the one task-owned test-results root were removed
using individually named literal absolute paths. Slot logs and phase evidence remain ignored;
this Markdown report is the sole tracked deliverable. Repository checks and the pushed report
SHA are reported in the final handoff. No desktop operation, landing, or deployment occurred.

## Per-case Linux results

All cases are in the Integration category and the exact class selection above. Phase columns are milliseconds; duration is seconds. Zero means instrumented bucket time, with uninstrumented work retained as remainder.

| Case | Outcome | Seconds | DB ms | Process ms | Host ms | FS ms | Network ms | Fixture ms | Remainder ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| C1714_Path_past_the_delete_limit_refuses_before_the_move(over-once-set-aside) | Passed | 0.5105265 | 30 | 105 | 0 | 0 | 0 | 0 | 376 |
| C1714_Path_past_the_delete_limit_refuses_before_the_move(under) | Passed | 0.5793124 | 28 | 140 | 0 | 0 | 0 | 0 | 411 |
| C1714_Removal_refuses_a_status_that_could_not_read_the_whole_tree(1) | Passed | 0.1993850 | 30 | 56 | 0 | 0 | 0 | 0 | 114 |
| C1714_Removal_refuses_a_status_that_could_not_read_the_whole_tree(2) | Passed | 0.2722527 | 44 | 67 | 0 | 0 | 0 | 0 | 162 |
| C459_AbsenceNeedsOwnIntent(directory) | Passed | 0.2637551 | 30 | 52 | 0 | 0 | 0 | 0 | 182 |
| C459_AbsenceNeedsOwnIntent(matching-intent) | Passed | 0.3058442 | 38 | 94 | 0 | 0 | 0 | 0 | 174 |
| C459_AbsenceNeedsOwnIntent(no-intent) | Passed | 0.2214157 | 56 | 60 | 0 | 0 | 0 | 0 | 106 |
| C459_AbsenceNeedsOwnIntent(ref) | Passed | 0.2839695 | 29 | 77 | 0 | 0 | 0 | 0 | 178 |
| C459_AbsenceNeedsOwnIntent(registration) | Passed | 0.2446367 | 52 | 61 | 0 | 0 | 0 | 0 | 132 |
| C459_AbsenceNeedsOwnIntent(wrong-intent) | Passed | 0.3333783 | 59 | 95 | 0 | 0 | 0 | 0 | 180 |
| C459_BranchDeleteUsesCas | Passed | 0.3360174 | 50 | 117 | 0 | 0 | 0 | 0 | 169 |
| C459_BranchPrecheckRequired | Passed | 0.3706663 | 64 | 115 | 0 | 0 | 0 | 0 | 192 |
| C459_CommittedReceiptRequired(changed) | Passed | 0.1311273 | 29 | 16 | 0 | 0 | 0 | 0 | 86 |
| C459_CommittedReceiptRequired(missing) | Passed | 0.1605714 | 34 | 16 | 0 | 0 | 0 | 0 | 111 |
| C459_CommittedReceiptRequired(revoked) | Passed | 0.2201569 | 49 | 19 | 0 | 0 | 0 | 0 | 152 |
| C459_CommittedReceiptRequired(schema) | Passed | 0.1848239 | 28 | 20 | 0 | 0 | 0 | 0 | 137 |
| C459_CoordinatesMatchReceipt(admin) | Passed | 0.1507878 | 33 | 29 | 0 | 0 | 0 | 0 | 89 |
| C459_CoordinatesMatchReceipt(branch) | Passed | 0.1713751 | 32 | 23 | 0 | 0 | 0 | 0 | 117 |
| C459_CoordinatesMatchReceipt(common) | Passed | 0.1847475 | 40 | 20 | 0 | 0 | 0 | 0 | 125 |
| C459_CoordinatesMatchReceipt(path) | Passed | 0.1439652 | 28 | 20 | 0 | 0 | 0 | 0 | 96 |
| C459_CoordinatesMatchReceipt(repo) | Passed | 0.1637994 | 48 | 20 | 0 | 0 | 0 | 0 | 96 |
| C459_CoordinatesMatchReceipt(sha) | Passed | 0.1442399 | 27 | 25 | 0 | 0 | 0 | 0 | 92 |
| C459_CoordinatesMatchReceipt(sibling-prefix) | Passed | 0.1334226 | 26 | 19 | 0 | 0 | 0 | 0 | 89 |
| C459_CoordinatesMatchReceipt(task) | Passed | 0.1939613 | 30 | 24 | 0 | 0 | 0 | 0 | 140 |
| C459_DestinationIdentityRequired(pushurl) | Passed | 0.1327388 | 25 | 27 | 0 | 0 | 0 | 0 | 80 |
| C459_DestinationIdentityRequired(target) | Passed | 0.1439417 | 28 | 25 | 0 | 0 | 0 | 0 | 91 |
| C459_FinalAuthorityRequired | Passed | 0.3998127 | 49 | 148 | 0 | 0 | 0 | 0 | 203 |
| C459_FinalContentInspectionRequired(sequencer) | Passed | 0.3373796 | 49 | 92 | 0 | 0 | 0 | 0 | 196 |
| C459_FinalContentInspectionRequired(staged) | Passed | 2.9589070 | 44 | 484 | 0 | 0 | 0 | 0 | 2444 |
| C459_FinalContentInspectionRequired(unstaged) | Passed | 0.3276350 | 46 | 107 | 0 | 0 | 0 | 0 | 175 |
| C459_FinalContentInspectionRequired(untracked) | Passed | 0.3020774 | 51 | 83 | 0 | 0 | 0 | 0 | 168 |
| C459_FinalIgnoredInspectionRequired(·antiphon/report·md) | Passed | 0.5329185 | 64 | 118 | 0 | 0 | 0 | 0 | 351 |
| C459_FinalIgnoredInspectionRequired(·claude/settings·json) | Passed | 0.5780441 | 49 | 116 | 0 | 0 | 0 | 0 | 413 |
| C459_FinalIgnoredInspectionRequired(bin-private/settings·local·json) | Passed | 0.6851512 | 53 | 138 | 0 | 0 | 0 | 0 | 494 |
| C459_FirstContentInspectionRequired(sequencer) | Passed | 0.2912279 | 53 | 69 | 0 | 0 | 0 | 0 | 169 |
| C459_FirstContentInspectionRequired(staged) | Passed | 0.3230686 | 47 | 75 | 0 | 0 | 0 | 0 | 202 |
| C459_FirstContentInspectionRequired(submodule) | Passed | 0.4304125 | 50 | 182 | 0 | 0 | 0 | 0 | 199 |
| C459_FirstContentInspectionRequired(unreadable) | Passed | 0.2753137 | 44 | 57 | 0 | 0 | 0 | 0 | 175 |
| C459_FirstContentInspectionRequired(unstaged) | Passed | 0.2448276 | 45 | 75 | 0 | 0 | 0 | 0 | 125 |
| C459_FirstContentInspectionRequired(untracked) | Passed | 0.2467920 | 27 | 67 | 0 | 0 | 0 | 0 | 153 |
| C459_FirstIgnoredInspectionRequired(·antiphon/report·md) | Passed | 0.4881197 | 27 | 80 | 0 | 0 | 0 | 0 | 381 |
| C459_FirstIgnoredInspectionRequired(·claude/settings·json) | Passed | 0.4776203 | 48 | 69 | 0 | 0 | 0 | 0 | 361 |
| C459_FirstIgnoredInspectionRequired(bin-private/settings·local·json) | Passed | 0.4475664 | 45 | 52 | 0 | 0 | 0 | 0 | 351 |
| C459_GenuineLeaseRequired(disposed) | Passed | 0.1363676 | 27 | 20 | 0 | 0 | 0 | 0 | 89 |
| C459_GenuineLeaseRequired(forged) | Passed | 0.1386141 | 28 | 17 | 0 | 0 | 0 | 0 | 94 |
| C459_GenuineLeaseRequired(null) | Passed | 0.1639631 | 31 | 23 | 0 | 0 | 0 | 0 | 110 |
| C459_GenuineLeaseRequired(other-repo) | Passed | 0.1293045 | 27 | 17 | 0 | 0 | 0 | 0 | 85 |
| C459_InterfaceDefaultCannotDeleteSettledTask | Passed | 0.0014245 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| C459_LockedRegistrationHolds(between) | Passed | 0.2723775 | 38 | 70 | 0 | 0 | 0 | 0 | 165 |
| C459_LockedRegistrationHolds(initial) | Passed | 0.2616114 | 48 | 63 | 0 | 0 | 0 | 0 | 151 |
| C459_ManagedRootRequired(dot-segment) | Passed | 0.2319695 | 49 | 30 | 0 | 0 | 0 | 0 | 153 |
| C459_ManagedRootRequired(in-root) | Passed | 0.3577988 | 60 | 142 | 0 | 0 | 0 | 0 | 156 |
| C459_ManagedRootRequired(junction) | NotExecuted | 0.3223615 | 78 | 32 | 0 | 0 | 0 | 0 | 211 |
| C459_ManagedRootRequired(outside) | Passed | 0.1862065 | 29 | 28 | 0 | 0 | 0 | 0 | 130 |
| C459_ManagedRootRequired(sibling-prefix) | Passed | 0.1630504 | 31 | 16 | 0 | 0 | 0 | 0 | 117 |
| C459_NestedRegistrationHolds | Passed | 0.2878923 | 25 | 75 | 0 | 0 | 0 | 0 | 188 |
| C459_NewCheckoutHoldsBranch | Passed | 0.3284238 | 28 | 114 | 0 | 0 | 0 | 0 | 186 |
| C459_PrunableRegistrationHolds | Passed | 0.1422222 | 32 | 27 | 0 | 0 | 0 | 0 | 83 |
| C459_PurposeCannotBorrowAuthority | Passed | 0.1979414 | 45 | 46 | 0 | 0 | 0 | 0 | 107 |
| C459_RecreatedTreeHolds(directory) | Passed | 0.2601693 | 42 | 99 | 0 | 0 | 0 | 0 | 119 |
| C459_RecreatedTreeHolds(ref) | Passed | 0.4540371 | 50 | 204 | 0 | 0 | 0 | 0 | 200 |
| C459_RecreatedTreeHolds(registration) | Passed | 0.2744976 | 29 | 118 | 0 | 0 | 0 | 0 | 127 |
| C459_RegistrationRequired(list-failure) | Passed | 0.4136437 | 55 | 107 | 0 | 0 | 0 | 0 | 252 |
| C459_RegistrationRequired(present-unregistered) | Passed | 0.3343658 | 58 | 77 | 0 | 0 | 0 | 0 | 200 |
| C459_RemoteContainmentRequired(ahead) | Passed | 0.3057968 | 44 | 85 | 0 | 0 | 0 | 0 | 177 |
| C459_RemoteContainmentRequired(descendant) | Passed | 0.2242335 | 25 | 94 | 0 | 0 | 0 | 0 | 105 |
| C459_RemoteContainmentRequired(divergent) | Passed | 0.1594578 | 26 | 46 | 0 | 0 | 0 | 0 | 88 |
| C459_RemoteContainmentRequired(patch-only) | Passed | 0.2077612 | 36 | 87 | 0 | 0 | 0 | 0 | 85 |
| C459_RemoteContainmentRequired(stale-master) | Passed | 0.1597910 | 26 | 50 | 0 | 0 | 0 | 0 | 84 |
| C459_RemoteContainmentRequired(task-remote-only) | Passed | 0.1922136 | 25 | 82 | 0 | 0 | 0 | 0 | 85 |
| C459_RemoteContainmentRequired(uncontained) | Passed | 0.1571995 | 27 | 42 | 0 | 0 | 0 | 0 | 88 |
| C459_RemoteRefreshBeforeBranch(lost-source) | Passed | 0.2313059 | 28 | 80 | 0 | 0 | 0 | 0 | 123 |
| C459_RemoteRefreshBeforeBranch(unreadable) | Passed | 0.2401453 | 27 | 93 | 0 | 0 | 0 | 0 | 121 |
| C459_RemoteRefreshBeforeDirectory(delete) | Passed | 0.1842834 | 25 | 51 | 0 | 0 | 0 | 0 | 108 |
| C459_RemoteRefreshBeforeDirectory(error) | Passed | 0.1657290 | 26 | 42 | 0 | 0 | 0 | 0 | 98 |
| C459_RemoteRefreshBeforeDirectory(rewrite) | Passed | 0.3779535 | 27 | 257 | 0 | 0 | 0 | 0 | 94 |
| C459_RemovalNeverForces | Passed | 0.5636321 | 52 | 94 | 0 | 0 | 0 | 0 | 418 |
| C459_RetirementRefsCannotBorrowNamespaces(existing-wrong-namespace) | Passed | 0.1381040 | 25 | 38 | 0 | 0 | 0 | 0 | 75 |
| C459_RetirementRefsCannotBorrowNamespaces(heads) | Passed | 0.2538424 | 43 | 39 | 0 | 0 | 0 | 0 | 172 |
| C459_RetirementRefsCannotBorrowNamespaces(land) | Passed | 0.2093066 | 34 | 33 | 0 | 0 | 0 | 0 | 142 |
| C459_RetirementRefsCannotBorrowNamespaces(other-retirement) | Passed | 0.1690789 | 47 | 20 | 0 | 0 | 0 | 0 | 102 |
| C459_RetirementRefsCannotBorrowNamespaces(tags) | Passed | 0.2465085 | 54 | 31 | 0 | 0 | 0 | 0 | 162 |
