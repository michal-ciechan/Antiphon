# CARD-0817 Code: S1 green; S2/S3 blocked by the brief's scope gate

S1 is implemented, committed and pushed. Its 14 intended tests passed. S2 and S3
are not implemented: the brief explicitly requires stopping when a slice needs
a seam outside its file table, and S2 needs the fixture binding described below.
This is an incomplete Final round, not a Review-ready implementation.

Original Code task / landing owner: `2a876d8a-7681-47be-94bb-c8cba4abfade`.
Branch: `feat/card-task-2a876d8a`.
Worktree: `/work/worktrees/task-2a876d8a`.
Task base: `71685b84772b82517c2db5dd5d18e085ca8f360a`.
Plan: `docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md`.
This report's commit is published separately; the final progress marker identifies it.

## Blocking scope decision

The brief says: "If a slice needs a seam outside the plan table, STOP and report it exactly."

Required additional seam:

- `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:646`, in
  `C1008HostFixture.Run`: add an inert scratch `GITHUB_TOKEN_DIR_PATH` binding
  matching the `githubToken` path emitted by the planned change to
  `scripts/fixtures/c994-production-compose-model.mjs`.
- The same binding is missing from
  `scripts/fixtures/c1008-recycle-real-cases.mjs:203`. This harness consumes that
  same production Compose model; its source needs the matching fixture binding
  when S2 changes the roster. Running this real-container harness is outside this
  offline task and is not requested here.

Why this is necessary: `C1008HostFixture` materializes the actual Compose files at
`RollingVolumeRecycleScriptTests.cs:548`, then runs the production c590 script
with scratch paths injected at lines 641-646. S2 adds
`--arg token "$GITHUB_TOKEN_DIR_PATH"` and a strict bind-source comparison to
`c1008_compose_model`. The injection changes `SERVER2_ROOT` and all existing bind
paths but will leave the new token path at its earlier production value. The new
scratch Compose bind and roster source therefore disagree. CP-5's required
`C1008_Recycle_exact_default_volumes` would refuse `RecycleComposeMismatch`.
This is a source-inspection finding, not a claimed executed S2 failure.

Both files are absent from S2's authorized file list. Neither has been edited.
The requested decision is to extend S2 to these two fixture bindings, then resume
Code on this branch for S2, S3 and the outstanding verification. Do not weaken the
production roster to accommodate the fixture. No production credential or
operator acceptance work is needed to resolve this scope issue.

## Committed work

`c25196bb72c5abdda2630b636ed8edae4c3f2d51` implements S1:

- `PushProbeOutcome`: five failure categories plus Authorized, with fixed remedies.
- `RunnerWorkspaceService`: retains the receive-pack dry-run argv, primary
  exemption, unauthorized code and HTTP 409; emits category/remedy without raw
  stderr and logs only repository identity/category.
- `PushCredentialPolicyFile` and `PushCredentialPolicyService`: publish the
  normalized primary followed by the exact configured ordinal prefixes through
  sibling-temp/atomic replacement before phone-home starts. Unset/disabled is a no-op.
- `PhoneHomeSettings` and `Program`: optional POSIX policy path and service ordering.
- Eight new methods and the strengthened existing mirror refusal test.
- CP-2/CP-5 method-filter wildcard corrections requested by the brief.

`a293d255f7c0ac7fb689a62fd5ab355fdde8198b` repairs CP-1/CP-6 combined
class discovery wildcards after CP-1 initially selected zero tests. No implementation
changed after the first commit. This consumed one of the three allowed repair rounds.
No timeout was widened, no assertion loosened, and no retry added.

## Verification outcomes

| Checkpoint | Outcome |
|---|---|
| CP-1 initial | 0 executed; exit 3 (discovery filter), not a pass. |
| CP-1 repair | 8 executed, 8 passed, 0 failed/skipped, at `a293d255f7c0ac7fb689a62fd5ab355fdde8198b`. |
| CP-2 | 6 executed, 6 passed, 0 failed/skipped, at `c25196bb72c5abdda2630b636ed8edae4c3f2d51`. |
| CP-3 | Not run; S2 stopped before implementation. |
| CP-4 | Not run; S2 stopped before implementation. |
| CP-5 | Not run; needs the additional fixture seam. |
| CP-6 | Not run; S3 not implemented. |

Every executed row and build had `slot=granted`, `waited=0s`. Both checkpoint-tool
launcher calls also received granted slots with zero wait. The first launcher
built the tool as prescribed by the plan; the repair launcher used `--no-build`.
No other build or test driver ran. No whole-assembly run, deliberate mutation,
live probe, vault access, host provisioning or container operation ran.

Fresh TRX files were parsed and checked for the exact roster: five
`PushProbeOutcomeTests`, three `PushCredentialPolicyFileTests`, and the six
named `RunnerWorkspaceServiceTests`. The first empty TRX was retained as red evidence.
The source receipt validator passed independently for CP-2 from the first report
and CP-1 from the repair report, each with its actual expected SHA and selected row.
All successful evidence has dirty=0, sourceState=clean and buildSource=verified.

| Invariant | Actual outcome |
|---|---|
| V-1 | Passed: all five classifier methods, including every named stderr marker and rotation remedy exclusivity. |
| V-2 | Passed: primary normalization, ordered prefixes and newline format. |
| V-3 | Passed: parent creation, replacement, no temporary residue, disabled/unset service no-op and enabled publication. |
| V-4 | Passed: null policy path admitted, relative refused, POSIX absolute admitted. |
| V-5 | Passed: 409/code, identity, Unknown category, no raw transport stderr, exact dry-run argv, no refused worktree, disabled-probe mirror succeeds. |
| V-6 | Pending: helper admitted-path behavior, S2. |
| V-7 | Pending: helper other-host refusal, S2. |
| V-8 | Pending: helper allow-list boundary/case behavior, S2. |
| V-9 | Pending: absent policy fails closed, S2. |
| V-10 | Pending: absent/empty token, S2. |
| V-11 | Pending: store/erase no writes, S2. |
| V-12 | Pending: exact Antiphon SSH rewrite and other HTTPS pushes, S2. |
| V-13 | Pending: entrypoint presence-only note, S2. |
| V-14 | Pending: shared read-only directory bind, S2. |
| V-15 | Pending: deploy directory custody/roster, S2. |
| V-16 | Pending: refresh stdin custody, S3. |
| V-17 | Pending: skipped refresh and mode-only diagnostics, S3. |
| V-18 | Pending: token custody/expiry/rotation docs, S3. |
| R-1 | Passed: all five named workspace regression methods. |
| R-2 | Pending: full DindRunnerContractTests, S2. |
| R-3 | Pending: all five named script regression methods, S2. |

The whole Unit lane and full affected-class Final baseline remain unrun; no
Final-scope completion is claimed. Operator acceptance steps 1-5, canary and
rollout are caller-owned after land and remain pending. PC-1, PC-2, PC-3, PC-4,
PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13, PC-14, PC-15, PC-16,
PC-17, PC-18, PC-19 and PC-20 all remain pending for SourceLanding Mutation,
including missing-control discovery. None was discharged by ordinary green.

## Unedited CHECKPOINT evidence

```text
CHECKPOINT CP-1 commit=c25196bb72c5abdda2630b636ed8edae4c3f2d51 build=ok filter=/*/*/(PushProbeOutcomeTests)|(PushCredentialPolicyFileTests)/* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-232424-bc98/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=c25196bb72c5abdda2630b636ed8edae4c3f2d51 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=c25196bb72c5abdda2630b636ed8edae4c3f2d51 build=reused filter=/*/*/RunnerWorkspaceServiceTests/(Mirror_refuses_a_secondary_repository_the_push_credential_cannot_push_to*)|(Mirror_accepts_a_secondary_repository_when_push_dry_run_succeeds*)|(Mirror_refuses_a_repository_outside_the_allowed_clone_sources*)|(Mirror_of_a_second_repository_clones_beside_the_primary_and_removes_through_its_own_checkout*)|(Mirror_creates_worktree_on_branch_at_sha*)|(Publish_pushes_only_own_fast_forward_branch*) executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-232424-bc98/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=c25196bb72c5abdda2630b636ed8edae4c3f2d51 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=a293d255f7c0ac7fb689a62fd5ab355fdde8198b build=ok filter=/*/*/(PushProbeOutcomeTests*)|(PushCredentialPolicyFileTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-2a876d8a/.antiphon/checkpoints/20261005-232557-a559/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a293d255f7c0ac7fb689a62fd5ab355fdde8198b sourceState=clean buildSource=verified
```

Generated receipts remain ignored in the two run directories named above.
`report.json` is beside each `report.md`. The checkpoint tool removed all owned
`bin-c0817-*` outputs after the successful repair; a subsequent filesystem check
found none under src, server, tests or tools. No run remains active.

`scripts/check-evidence-diff.ps1` passed over the full base..implementation range
(two commits, zero violations). It must also pass base..final-report-HEAD before
settlement; that final result is included in the caller-facing summary.

Platform reads completed before implementation: runner-defaults revision 2,
globalRunnerId server2; session-runners returned desktop Windows and both Linux
runners. No runner or platform setting was changed. ANTIPHON_TASK_TOKEN was
checked for presence only and was present.

Restart performed: none. Activation requires a future runner rollout owned by the
caller/operator; server restart is not needed for these runner-only changes.

--- next stage ---
next: decide
handoff: Authorize the two missing S2 fixture path bindings, then resume Code on feat/card-task-2a876d8a for S2/S3 and remaining verification. S1 has 14 passing checkpoint tests; full Final scope is incomplete. Keep operator acceptance caller-owned and PC-1..PC-20 pending for post-land Mutation.
artifact: docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md
