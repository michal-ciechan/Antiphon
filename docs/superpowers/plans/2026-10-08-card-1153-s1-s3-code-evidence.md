# CARD-1153 S1-S3 Code evidence

Code task `4a4aacaa` (landing owner), branch `feat/card-task-4a4aacaa`, runner mirror
worktree `/work/worktrees/task-4a4aacaa`. Plan
`docs/superpowers/plans/2026-10-08-card-1153-runner-absence-evidence-plan.md`, test design
`docs/superpowers/plans/2026-10-08-card-1153-test-design.md` (checkpoint `--plan` input).
Branch base `061e29c3492b8433b874cbed6c26dfe01e0a1ecb`. Round: Final profile, ordinary scope
narrowed by the brief to the S1-S3 rows plus the registry guards; no whole-Unit run.

## Commits

| Slice | SHA | Content |
|---|---|---|
| S1 | `2dcc2c91a13228c3eb1933549151e19351cbffe9` | Contracts `RunnerAbsenceEvidence.cs`; runner `RunnerAbsenceEvidenceStore.cs` (strict reads), `RunnerAbsenceEvidenceService.cs` (prepare/certify whitelist, random epoch, A-10 seam, latch); V-1..V-4, V-23 |
| S2 | `7bcaa29918181ba83a251644331f45799e656408` | Runtime markers, ClosedUnused fence, gate-held prepare/certify, adoption readiness; V-5..V-8 |
| S2 fix | `8abd0ab75d3158dc3f9c15977ff4a8cc0665a729` | CP-10 red at 7bcaa2991 (5/12 UnixPtyArgvAdmissionTests: runtime ctor created `absence-evidence/`, marker ran before validation). Lazy store root; read-only fence at gate entry; marker after validation, before the first disk/provider effect |
| S2 fix | `d30c740fa2ef5ccb1b0cb785fd2f56025a74d657` | Dev smoke found `HerdrAttachTests.Unreachable_herdr_is_503_and_writes_nothing` red: attach marker now follows herdr reachability |
| S3 | `9b1ec2e152fe234b190b2fda2cf90c057f475db1` | HMAC transports, routes, phone-home ops 38/39, capability gating, server client methods, validator; V-9..V-13, V-22 |
| S3 test | `4169c6f864f8d22c3d777d7f1e7423934c8152c4` | V-11 now proves foreign-store frames never enter the runtime (a quick mutation had survived) |

## Code obligations A-1..A-10

- A-1: route id, body id and MAC-covered id: body digest and route id are in the request canonical; route/body disagreement is 400 after MAC verification, no state change (V-10 changed-request-field).
- A-2: phone-home admission refusals before `StartAsync` never reach the marker; the watermark is a retained-artifact exclusion at prepare and certify (V-2 custody-or-watermark, V-23 watermark-present).
- A-3: `SessionIdentityClosedException` maps to HTTP 409 `session_identity_closed` (shared middleware) and the `phone_home_session_identity_closed` 409 frame (V-11 delayed Launch).
- A-4: `RunnerAbsenceEvidenceService.Epoch = Guid.NewGuid()` per instance, memory only, equality only.
- A-5: read-only fence at gate entry before custody `AcquireSession`/`PrepareStart`; marker before custody admission (custody path), before the Grok rules write, before registration/manifest/sidecar/launch; attach marker after herdr reachability and before registration (deviation from the design's "before ConnectAndValidateAsync", required by the existing unreachable-writes-nothing assertion; nothing is created for the id before that point).
- A-6: validator and runner request parser both require the exact version-1 member set.
- A-7: HTTP client uses the bound store if any and requires it to equal the runner's advertised store; a local session uses the advertised store (non-empty); phone-home uses the connection's registered store.
- A-8: V-22 (35 cases) and V-23 (22 cases) one-flip tables, each with an independent pristine control. The due-path statement ceiling (V-24) is S4 and is not run here.
- A-9: R-10 (CP-17) and R-11 (CP-10) run in this round.
- A-10: `IRunnerAbsenceInspection` seam (runtime entry, artifacts, adoption, custody, watermark); runtime wires the real inspection.

## Checkpoint receipts (unedited lines, TRX paths elided)

Run `20261008-011859-6ee3` (S1, `--rows CP-1..CP-5`, verdict GREEN, wall 3m43s, 1 build):

```
CHECKPOINT CP-1 commit=2dcc2c91a13228c3eb1933549151e19351cbffe9 build=ok filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_records_only_a_fresh_identity* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=30s dirty=0 source=2dcc2c91a13228c3eb1933549151e19351cbffe9 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=2dcc2c91a13228c3eb1933549151e19351cbffe9 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Prepare_refuses_existing_evidence* executed=9 passed=9 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=2dcc2c91a13228c3eb1933549151e19351cbffe9 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=2dcc2c91a13228c3eb1933549151e19351cbffe9 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Restart_or_store_change_never_renews_proof* executed=3 passed=3 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=2dcc2c91a13228c3eb1933549151e19351cbffe9 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=2dcc2c91a13228c3eb1933549151e19351cbffe9 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Unknown_or_unreadable_state_is_not_absence* executed=5 passed=5 failed=0 skipped=0 slot=granted waited=30s dirty=0 source=2dcc2c91a13228c3eb1933549151e19351cbffe9 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=2dcc2c91a13228c3eb1933549151e19351cbffe9 build=reused filter=/*/*/RunnerAbsenceEvidenceTests/C1153_Certify_requires_every_fact* executed=22 passed=22 failed=0 skipped=0 slot=granted waited=30s dirty=0 source=2dcc2c91a13228c3eb1933549151e19351cbffe9 sourceState=clean buildSource=verified
```

Run `20261008-013543-35d6` (S2, `--rows CP-6..CP-10` at 7bcaa2991, verdict RED, wall 0m57s): CP-6..CP-9 green 3/1/2/4;

```
CHECKPOINT CP-10 commit=7bcaa29918181ba83a251644331f45799e656408 build=reused filter=/*/*/UnixPtyArgvAdmissionTests/* executed=12 passed=7 failed=5 skipped=0 slot=granted waited=0s dirty=0 source=7bcaa29918181ba83a251644331f45799e656408 sourceState=clean buildSource=verified
```

Introduced by S2 (assertion `Directory.Exists(root)` false), repaired in `8abd0ab75`; not inherited.

Run `20261008-014018-a363` (S2 rerun at 8abd0ab75, verdict GREEN, wall 3m04s): CP-6 3/3, CP-7 1/1, CP-8 2/2, CP-9 4/4, CP-10 12/12, all `dirty=0 sourceState=clean buildSource=verified`.

Run `20261008-021338-2f12` (S2+S3, `--rows CP-6..CP-17` at 9b1ec2e15, verdict GREEN, wall 11m36s, 3 builds, `unlisted: none`):

```
CHECKPOINT CP-6 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=ok filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Creation_consumes_proof_before_effects* executed=3 passed=3 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Store_failure_disables_proof_without_stopping_work* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=30s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Certificate_and_launch_race_is_serialized* executed=2 passed=2 failed=0 skipped=0 slot=granted waited=45s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/RunnerAbsenceEvidenceRuntimeTests/C1153_Closed_identity_refuses_delayed_creation* executed=4 passed=4 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/UnixPtyArgvAdmissionTests/* executed=12 passed=12 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=ok filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Real_unknown_transcript_has_a_separate_certificate* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=60s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/RunnerAbsenceEvidenceContractTests/C1153_Http_authentication_covers_request_and_response* executed=4 passed=4 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/RunnerAbsenceEvidencePhoneHomeTests/C1153_Authenticated_operation_preserves_binding* executed=3 passed=3 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=ok filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Rejects_noncertificate_wire_shapes* executed=14 passed=14 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/SessionRunnerAbsenceEvidenceClientTests/C1153_Freshness_and_cancellation_are_bounded* executed=4 passed=4 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/RunnerAbsenceEvidenceValidatorTests/C1153_Validator_requires_every_fact* executed=35 passed=35 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=9b1ec2e152fe234b190b2fda2cf90c057f475db1 build=reused filter=/*/*/PhoneHomeConnectionTests/Authentication_is_required_at_both_endpoints* executed=1 passed=1 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=9b1ec2e152fe234b190b2fda2cf90c057f475db1 sourceState=clean buildSource=verified
```

Run `20261008-023535-8264` (supplemental hand-made plan, `--after S3` at HEAD 4169c6f86, verdict
GREEN, wall 6m48s, 2 builds, `unlisted: none`; validate: `CHECKPOINT SOURCE VALID rows=20`).
Stated reasons: brief-required registry guards; adjacent launch/transport classes S2/S3 touch;
V-11 rerun after its test tightened; S1 rows rerun after the lazy-root store change.
CP-101 TestClassificationGuardTests (runner) 1/1; CP-102 HerdrAttachTests 23/23; CP-103
TerminalSeatReleaseTests 38/38; CP-104 RunnerCapabilitiesTests 7/7; CP-105
PhoneHomeCommandDispatcherTests 43/43; CP-106 GrokRulesFileLaunchTests 21/21; CP-107
GrokRulesRunnerRefusalTests 11/11; CP-108 HerdrRunnerSessionTests 7/7; CP-109
HerdrLaunchShapeTests 37/37; CP-110 HerdrAdoptionSweepTests 22/22; CP-111
TestClassificationGuardTests (server) 1/1; CP-112 SlowTestTripwireTests 2/2; CP-113
PhoneHomeConnectionTests 29/29; CP-114 SessionRunnerGenerationWireTests 9/9; CP-115 V-11 3/3;
CP-116..CP-120 V-1 1/1, V-2 9/9, V-3 3/3, V-4 5/5, V-23 22/22. Every line `dirty=0
source=4169c6f864f8d22c3d777d7f1e7423934c8152c4 sourceState=clean buildSource=verified`.

## Quick mutations (not PCs; every PC stays pending for SourceLanding Mutation)

One method-scoped cycle each, applied by script, build, run, `git checkout` restore, tree clean:
V-1 skip Prepared write red 1/1; V-2 bypass history exclusion red 7/9 (record-state cases refused
by state); V-3 drop epoch check red 2/3 (store case independent); V-4 denied read as absence red
denied-read; V-23 drop adoption check red adoption-incomplete; V-5 drop start marker red start;
V-6 drop latch red; V-7 certify outside gate red 2/2; V-8 fence returns red 4/4; V-9 certify via
GetTranscript red; V-10 skip request MAC red wrong-request-MAC, changed-request-field; V-11 drop
store binding initially GREEN (defence in depth) -> test tightened -> red; V-12 always Verified
red unsigned, bad-MAC; V-13 no deadline red over-5s-deadline; V-22 drop processPresent predicate
red process-true.

## Not run / deferred

- Whole Unit lane: not run (brief forbids it; not part of this ordinary scope).
- S4/S5 rows CP-18..CP-25, CP-32 and final-group rows CP-26..CP-34 (R-1, R-2, R-4..R-8): deferred to S4/S5 Code; not passed.
- Windows rows CP-35 (R-9), CP-36 (R-5): not run (final group, Windows host required).
- Every PC-1..PC-29 variant: pending SourceLanding Mutation.

## Activation and compatibility

Runner first (`pwsh -NoProfile -File scripts/restart-session-runner.ps1` from the canonical
checkout, no `-KillSessions`, no `-AllowWorktree`; server2 by the rolling phases), then AppHost
(`scripts/restart-apphost.ps1`, check `/api/version`). New runner / old server: markers written,
launches unchanged, nothing calls prepare/certify. New server / old runner: no
`sessionAbsenceEvidenceV1`, client returns Unsupported without a POST/frame. HTTP certification
also needs `SessionRunner:AbsenceEvidence:KeyPath` on both sides (operator custody; no key was
created). S1-S3 add no dispatcher caller, so nothing changes task outcomes until S4 lands.
No migration; AgentTaskDispatcher.cs, SessionMessageQueueService*, SessionReconciliationService.cs
and scripts/c590-remote.sh are untouched, so the boot-stall tail hash is unchanged.
