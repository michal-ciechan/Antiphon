# Reply-inline Linux fixture attribution

Debug task: 37cb8e72. Follow-up: Antiphon CARD-1899 (High).

The source diagnosis is inherited Linux fixture debt after CARD-1224, rather than
an S1 JIT policy effect. Runtime attribution is pending the requested runs; do not
credit the source diagnosis as a passing or failing test receipt.

## Cause and intended behavior

`OutputDistillationDeliveryTests.DeliveryFixture.CreateAsync` explicitly passes
`modern ? "modern" : "inbox"` to `PtyDeliveryProfile`, then asserts a reply ceiling
of `modern ? 14400 : 3000` (lines 533-543). `PtyBackendPolicy.Resolve`, in
`src/Antiphon.Agents.Pty/PtyBackend.cs`, returns `UnixPty` on non-Windows before
considering those Windows selectors. The profile resolves the resulting backend
through `DelegationSettings.CeilingsFor`.

Commit `b35ed98633c4234f037a0fe589547206cf6b747d` (CARD-1224; authored
2026-11-11 04:46:30 UTC, committed 14:16:40 UTC; cherry-picked from
`bb4f2811c2516d3754db5e34487ce4f6a4497532`) intentionally introduced the Unix
arm: reply 14,400 characters, brief 43,200 bytes, single write 86,400 bytes.
It removed `PtyDeliveryCeilingsTests.Unix_backend_keeps_conservative_limits` but
left the delivery fixture unchanged. CARD-1224 explicitly asks for measured Unix
ceilings and unchanged Windows behavior; lowering the production Unix defaults
would undo that work.

The general `ReplyInlineMaxChars` property still defaults to 3,000. The modern
14,400 property originated in `4dbed26ad79d5ed748fd2fc1291fb24905008e68`
(CARD-0037, 2026-08-12). This was a new Unix backend mapping, not an unconditional
change to the general property. Commit `1ff01861fe5fcb0afdf6ae1cadb62e0639719c79`
(2026-10-24) had already added explicit fake transport sizing on Unix, but only
updated the original inbox properties in the `modern` arm; the later Unix-specific
properties bypass that modeling.

All three named methods belong to one class, `OutputDistillationDeliveryTests`:
`Fallback_delivers_original_raw_or_marked_excerpt`,
`Admission_and_deadline_fallbacks_reach_parent`, and
`Same_root_applied_notes_batch_into_one_intact_spill_file`.

The complete fixture file has the identical Git blob
`ab920cec5da9165d05f6dc3af642eac81ffa4632` at all three requested revisions:

| Revision | Full SHA |
| --- | --- |
| Master specified by brief | `c1fd1b7bc1eb905d2285b4258879955dda4fe96d` |
| S1 base | `70fd156d0677b78218010eb4e682c66db831ad24` |
| S1 tip | `6a42fc4d7884951f0337eb976c7ed9df57269e1f` |

CARD-1224 is an ancestor of the S1 base. S1 changes the database connection's JIT
startup option, without changing the profile/settings/backend policy or fixture.
The failing setup constructs fresh instance settings and occurs before delivery.
No static-settings polluter is necessary to explain it.

## Minimal repair proposal

Repair the TEST fixture, preserving the product defaults and conservative
spill/batching assertions. After the existing `if (modern)` settings block, map
the selected fake envelope to the Unix settings as well:

```csharp
f.Settings.UnixPtyReplyInlineMaxChars = f.Settings.ReplyInlineMaxChars;
f.Settings.UnixPtyBriefInlineMaxBytes = f.Settings.BriefInlineMaxBytes;
f.Settings.UnixPtySingleWriteMaxBytes = f.Settings.PtySingleChunkBytes;
```

Changing only the expected assertion to 14,400 would erase the intended 1,024-byte
spill boundary and make the same-root batching witness invalid. A narrow
instance-scoped profile seam is an alternative if preferred by the repair owner.
No production/test repair is included in this Debug task.

`CreateAsync` must also unwind its schema, service provider and workspace if setup
throws. Its caller's `await using` cannot dispose an instance that was never
returned. `ReportWorkspace` currently creates nested Git repositories under
`.antiphon/test-output/card-0419`; this explains the fixture residue caused by a
setup assertion. Diagnostic worktrees are fresh, so their exact owned fixture root
can be removed after execution, retaining the separate evidence directory.

## Verification and custody

Evidence root: `/work/worktrees/task-37cb8e72/.antiphon/debug-37cb8e72`.
Generated logs, phase journals and TRX files remain ignored. Separate detached
worktrees bind each requested revision, with native default build output and
one build at a time. The DerivedImpactIndex driver build runs first. Every
build and test takes a slot through `scripts/build-slot.ps1`; no unleased retry
or timeout change is authorized.

Requested verification: the complete class at each revision, plus isolated named
methods and a singleton same-root witness to distinguish ordering. Since the
three requested methods are one class, its class run is also the together run.
Runtime results will be appended after the queued build and executions settle.

The desktop-only rc run `97150a0b` was not accessed. The caller must inspect
`C:\Antiphon\releases\candidates\rc-2026-11-19-ab7af12c6990\logs\2026-11-13-0558-97150a0b\*.trx`
for actual qualification outcomes. Linux's unconditional Unix resolution does not
predict the same 3,000/14,400 setup assertion on native Windows.

## Why this escaped earlier selection

The introducing commit is CARD-1224's `b35ed98633c4234f037a0fe589547206cf6b747d`.
The approving review identity, Full/Interim status and actual executed selection
are unknown in this task; no omission or approval is invented. The affected class
is Integration, so a Unit-only selection would not execute it. The cheapest
additional impacted-classes entry is
`server/Application/Settings/DelegationSettings.cs` ->
`/*/*/OutputDistillationDeliveryTests/*`, including native Linux execution.
This issue was caught by S1 Review's broader impact union before S1 landing;
actual rc inclusion/outcome remains for the desktop caller to establish.
