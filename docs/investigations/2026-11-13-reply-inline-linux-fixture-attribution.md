# Reply-inline Linux fixture attribution

Debug task: 37cb8e72. Follow-up: Antiphon CARD-1899 (High).

Inherited Linux fixture debt after CARD-1224 is confirmed at all three requested
revisions. The same 19 ceiling-assertion failures recur at master, S1 base and S1
tip, and a single selected batching test fails in a fresh process. Two additional
tip failures have unresolved attribution and are recorded separately below.

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

Completed verification: the complete class at each revision, plus isolated named
methods and a singleton same-root witness. Since the three requested methods are
one class, its class run is also the together run. Every test run exited 2 with a
fresh, nonzero TRX; no run is claimed green.

| Run / TRX basename | Total | Passed | Failed | Ceiling failures | Outer wall seconds |
| --- | ---: | ---: | ---: | ---: | ---: |
| `master-class` | 78 | 59 | 19 | 19 | 250.52 |
| `master-singleton` | 1 | 0 | 1 | 1 | 102.49 |
| `master-fallback` | 12 | 6 | 6 | 6 | 137.44 |
| `master-admission` | 16 | 8 | 8 | 8 | 124.20 |
| `s1base-class` | 78 | 59 | 19 | 19 | 379.09 |
| `s1tip-class` | 78 | 57 | 21 | 19 | 465.63 |

Each TRX is `<evidence-root>/<basename>/<basename>.trx`. Native TRX run IDs:

| Basename | TRX run ID |
| --- | --- |
| `master-class` | `2d3ac682-42d3-49b2-aa67-82cc23b10b4a` |
| `master-singleton` | `7a3b677b-1048-4b5b-a133-c5f0fa2e26bc` |
| `master-fallback` | `12ec0e83-b607-4363-a961-38fee4cb5421` |
| `master-admission` | `e99bbcff-dc8f-4b75-8be7-94a07db7d464` |
| `s1base-class` | `ee214e44-fa78-435c-a92f-0a715ba1f866` |
| `s1tip-class` | `bb0b95da-98a4-4185-b04e-83b46aed702e` |

`results-summary.json` records TRX SHA-256, test DLL SHA-256, exact source SHA,
tracked-source cleanliness and every failing identity. `phase-provenance.json`
records process IDs and assembly module version IDs; `phases/` contains the six
completed runs' per-test/fixture journals. Unrecorded startup components remain
unknown; outer wall includes admission and cannot be treated as test-body time.
All runs were Linux x64, .NET 9.0.23, TUnit 1.44 / MTP 2.2.2. No test selection
included another class.

The 19 matching ceiling identities comprise one `Report_boundaries(3500, False)`,
two `Target_canonical_unavailable_retains_legacy_backstop_without_relabeling_author_file`
cases, one `Canonical_selection_preserves_but_never_advertises_different_legacy_author_file`,
six conservative fallback cases, eight conservative admission cases, and the
same-root batching singleton. The three brief-named methods contribute 15 cases
to this complete failure set. The review's ten observations were partial.

The class filter was `/*/*/OutputDistillationDeliveryTests/*`. Isolation used that
class with the exact method name after the final slash, respectively
`Same_root_applied_notes_batch_into_one_intact_spill_file`,
`Fallback_delivers_original_raw_or_marked_excerpt`, and
`Admission_and_deadline_fallbacks_reach_parent`. The parameterized method runs
execute 12 and 16 cases; the batching run executes exactly one case. Every
conservative case fails before delivery logic; modern cases pass in the isolated
fallback/admission runs. No preceding test or mutable global option is required.

The prerequisite driver and all three test-project builds succeeded with zero
errors. Native build times: prerequisite 4.62 seconds; master 4m11.31s; base
4m34.24s; tip 6m50.65s. The first slot wait was 585 seconds. Test slot waits were
0/30/45/30/150/75 seconds in table order. Existing compiler/analyzer warnings are
retained in the build logs. No timeout was widened, no unleased build ran, and no
private manually created database container was used.

Two diagnostic-wrapper interruptions are retained as execution history: an
initial PowerShell invocation was stopped before it launched a test child or
acquired a test lease, then replaced with literal ProcessStartInfo arguments;
a void-task return value interrupted orchestration after the successful base
build, then was suppressed. Neither interruption caused a completed build or
test row to be rerun. Their time is not included in passing execution credit.

All runs are joined. Exact owned fixture roots were inventoried and deleted:
34 master + 19 base + 19 tip nested fixture repositories. All three diagnostic
`.antiphon` directories contain zero nested Git directories. Their fresh detached
worktrees and native build outputs are retained for follow-up; no live host,
qualification run or session was restarted or modified.

## Separate unresolved tip failures

Follow-up: Antiphon CARD-1900 (Normal), filed separately from CARD-1899.

The tip's two extra failures are not ceiling failures:

- `Mode_and_terminal_status_preserve_report(Shadow, False, True)` expected a
  canonical `seed.Task.ResultFilePath`, but received null (4.329-second body).
- `Apply_preserves_handoff_warnings_and_generated_deliverables(False)` failed
  with `ShouldAssertException: Value cannot be null. (Parameter 'value')`.

Both passed in the master and base full-class runs. Their cause is unresolved:
neither S1 causation nor a flake is established. The explicit 60-minute time box
left no room for isolated base/tip reruns; follow-up must target only these two
methods and inspect report-store/custody timing without widening budgets.
Their original failed receipts remain intact.

This diagnosis does not complete Review 43aa1954's timed-out 38-class union, grant
S1 land credit, or qualify the rc run. Those verification gates remain incomplete.

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
