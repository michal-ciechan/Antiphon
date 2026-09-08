# CARD-0448 continuation 2: shared-worktree reconciliation

Task: `347a3d88`. Worktree: `C:\Antiphon\worktrees\card-task-c86499fb`.
Branch: `feat/card-task-c86499fb`.

**Acceptance remains incomplete. Do not land or deploy.** This is an additional handoff,
not a replacement for the accepted plan or the continuation matrix. The original continuation
was still executing when a separate continuation began changing the same checkout. Resume
with a single code and mutation-test owner, or with explicitly isolated checkouts.

## Ownership and interrupted execution

The original session observed commits `563e4b18`, `0798caf8`, `2ef363c0`, `b0f1d7b2`, and
`111be4f1` arriving from another actor. It did not create those commits. The continuation 3
report confirms that it inherited and changed the preparation work. Do not reapply the
original session's pending production patches over that implementation.

The original `continuation2-protocol-regression-01` ran an older `bin-c448-next` image,
started at 18:12:14 on 8 September 2026. Its test PID was 57164, parent dotnet PID 50300.
The process remained active after the original tool session became unavailable. At about
19:51 the original session explicitly cancelled that owned test tree after rereading its
exact executable, command line, and parent identity. This was a decision to end a superseded
run during a shared-checkout collision, not a timeout verdict or an automatic stall kill.
**It has no completed test verdict and receives no acceptance credit.** No other continuation's
test host, live server, runner, or AppHost was stopped. A separate test PID 43552 was observed
running afterward and left alone.

## Evidence already recorded

The detailed triage and run provenance remain in
[the continuation 2 report](2026-09-08-card-0448-code-continuation2-report.md).
These are separate historical executions, not a sum of distinct tests or a current-source
combined regression:

| Scope | Result |
|---|---|
| Corrected legacy triage and companions | 68 passed, 0 failed |
| Earlier safety subset | 67 passed, 0 failed |
| Earlier source and target boundaries | 32 passed, 0 failed |
| Corrected recovery subset | 54 passed, 0 failed |
| Earlier expanded matrix | 128 passed, 1 failed; Windows-hidden `.git` fixture corrected afterward, not credited as a full rerun |
| Verification policy | 5 passed / 6 intended failures before fix; 11 passed after rebuild |
| Atomic operation replacement | 1 intended failure before fix; 1 passed after rebuild |
| Client subset | 52 passed; client build passed |
| Corrected stdout-descendant control | 1 GREEN, 1 intended RED, 1 restored GREEN |

Seven earlier limited mutation controls are recorded in the original control manifest.
The expanded manifest contains only the corrected stdout-descendant completed control.
An earlier stdout mutant survived and is rejected as evidence. The **86 definitions** in
`.antiphon/continuation2-control-cases.json` are not 86 executed controls and do not complete
the 50 PC families. Continuation 3 owns its subsequent execution results; this handoff does
not claim those as executions by the original session.

## Pending local artifacts that must not be lost or applied blindly

All paths below are relative to this worktree. They are ignored preparation files, not
compiled tests or checked-in acceptance evidence. Review and adapt them against current HEAD.

| Artifact | State and action |
|---|---|
| `.antiphon/apply-preparation-fix.py` | Obsolete alternative to continuation 3's `LandingGitResult.RebaseHeadSha`. Do not apply. |
| `.antiphon/apply-verified-retry-fix.py` | Obsolete alternative to continuation 3's preparation replacement policy. Do not apply. |
| `.antiphon/strengthen-source-boundaries.py` | Prepared expansion from 18 to 90 boundary/change rows with immediate command/intent oracles. Not applied or executed. Review its exact replacements first. |
| `.antiphon/strengthen-c24-worker.py` | Prepared second OS recovery worker for the killed-worker/live-child C24 case. Not applied or executed. The existing parent-provider check alone is not this additional evidence. |
| `.antiphon/AgentTaskLandAdmissionTests.cs` | Prepared 24 real dispatcher cases: Shared/follow-up writers, four landing modes, and land-first / dispatch-first / dispatch-before-acquire orderings. Not installed, compiled, or executed. |
| `.antiphon/LandingAdmissionControlTests.cs` | Selectors for those admission cases. Not installed or executed. |
| `.antiphon/LandingVerificationReplacementPolicyTests.cs` | Prepared 16 policy cases against an uninstalled helper. Incompatible with current implementation; adapt before installation. |
| `.antiphon/install-final-boundary-fixtures.py` | Bundles the preceding incompatible policy file with admission files and a harness DI hook. Do not run unchanged. Install/adapt deliberately. |
| `.antiphon/run-continuation2-expanded-controls.py` | Mutation runner with exact source restoration and fingerprint-scoped baseline reuse. The many prepared definitions still require individual oracle review and actual runs. Never run concurrently with another writer. |
| `.antiphon/run-continuation2-final.py` | Prepared sequential exact-class regression with TRX and evidence capture. Not executed; selection includes pending classes, so adapt it before use. |

Two installed selectors in `LandingSourceBoundaryControlTests.cs`, `BeforeRebaseIntent`
and `BeforePushIntent`, currently call boundaries that the unchanged
`AgentTaskLandBoundaryTests.C448_V10_EachAcknowledgedBoundaryRechecksSource` callback does
not trigger. Its callback handles acknowledged phase names and the `remote` fetch only.
Those selector rows therefore cannot supply passing coverage until their fixture support
is implemented. The pending script also strengthens `Verified` and `LocalTargetAdvanced`
with immediate boundary assertions. Do not mistake the presence of wrapper methods for
executed evidence.

## Required continuation

Resolve the shared-worktree ownership before further production edits or mutation runs.
Then integrate and validate the pending boundary/admission fixtures, finish the independent
PC variants and remaining V/R/C/F rows, reproduce the baseline incident, refresh the
per-variant ledger, and run the final unmutated combined regression. Historical aggregate
passes cannot replace these requirements. The original session has requested ownership
clarification; it has not authorized landing or declared this Code task complete.
