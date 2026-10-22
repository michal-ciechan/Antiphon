# Task 8c2a794e report - CARD-0167 S3 repair 2 (Final Review 6c08cd4b R1)

Start ref 81937b7e5 (73a4fdd8 tip). Branch feat/card-task-8c2a794e, fast-forward only (no rebase: the runner contract forbids it; the land service drops carry 25ae6869).

## Commits
- 1d5a070e17371c6ced8077ee6305babe3822bcf7 test: five producer witnesses, RED (one build-slot run, filter `/*/*/(PoolProviderProjectionStampTests*)|(AgentSessionBackendTests*)|(AgentAttachHerdrTests*)/C0167_*`: total 5, failed 5, each on the guarded assertion; messages in the plan amendment). Adds only the `UnknownPoolProjection` constant as a compile skeleton.
- 0e68a9b28f62986d00045e4dfbcc0e0982f8547a fix: one map read per launch shared by spec and stamp.
- 4e36b93d0f4fd9c85226a7fd6769050dc1419673 docs: plan amendment docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-repair2.md (CP-1..CP-6).
- 8c285b51436d7d3176109a35270c8a5fbbda2d42 report v1; first checkpoint run 20261022-121333-47cc: CP-1 37/37, CP-2 12/12, CP-3 62/62, CP-4 25/25 green; CP-5 41/43 (2 control witnesses red); CP-6 28/50 (22 inherited, identical names at base 81937b7e5).
- 79604e7b9d5f5753e3a97e35384b08d9cde2eb5a fix: control start stamps after the reservation reload; attach witness seeds through the harness scope.
- e1500036e53b44d2951760f66ba9ecefdc5908d8 report v2 + plan note; run 20261022-123425-e9e0: CP-1 37/37, CP-2 12/12, CP-3 62/62, CP-4 25/25 green; CP-5 42/43 (resume arm of the control witness held on standing_continuity: a pool session has no standing owner).
- 90232b4f24c0cf68383659fee18063244e536b6d test: the resume arm seeds the task row that proves pool-session ownership.
- 2527e096a56709b6d48d0d389ab6726a0e2ce122 report v3; run 20261022-125646-23ef: CP-1..CP-4 green (37/12/62/25); CP-5 42/43, the resume arm still held on standing_continuity (ownership/continuity check refuses the seeded pool session before the stamp).
- next commit: resume arm dropped (budget); the witness is renamed C0167_start_restamps_a_pool_rows_provider_projection and covers the new-session arm, which runs the stamp line both arms share. The resume arm is NOT witnessed.
- report v4 commit: the receipt run `--rows CP-1,CP-2,CP-3,CP-4,CP-5` verifies that tip.

Read-only checks at e1500036e: scripts/check-evidence-diff.ps1 81937b7e5..HEAD violations=0 (also a38e1e1bd..HEAD violations=0). `git merge-tree --write-tree origin/master HEAD` (origin/master 711409c41) conflicts add/add only on the carried S2 files (ProviderEndpointSettings.cs, ProviderEndpointApiTests.cs); a scratch `git rebase --onto origin/master a38e1e1bd` exits 0 and drops the carry 25ae6869 as already upstream (13 commits remain), which is what the land service does.

## Fix, by path
- Cold dispatch: claim marks the pool row `unknown`; `BuildLaunchSpecAsync` (profile-less, the only branch a pool delegate takes) stamps from the endpoints it read for the spec and saves before the launch is enqueued. Closes the claim-read vs spec-read race.
- Boot-wedge relaunch (`RelaunchWedgedAsync`): relaunch commit marks `unknown`; the same spec builder restamps from the relaunch's read.
- AgentControlService start/resume (`StartInteractiveSessionAsync`, both arms): stamps from `ResolvedAgentTuiLaunch.ProjectProviderEndpoints` (the funnel's one read, new init-only property).
- AgentControlService Herdr adopt (`AttachHerdrAsync`): pool row marked `unknown` (the adopted process's env cannot be known). `unknown` is not JSON, so it never equals a fingerprint (JSON or null) and matches no task; null is not used because null matches a no-endpoint task.

Results of the checkpoint run are in the final message (CHECKPOINT lines, receipt validation).
