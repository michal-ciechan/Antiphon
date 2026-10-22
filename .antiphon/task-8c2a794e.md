# Task 8c2a794e report - CARD-0167 S3 repair 2 (Final Review 6c08cd4b R1)

Start ref 81937b7e5 (73a4fdd8 tip). Branch feat/card-task-8c2a794e, fast-forward only (no rebase: the runner contract forbids it; the land service drops carry 25ae6869).

## Commits
- 1d5a070e17371c6ced8077ee6305babe3822bcf7 test: five producer witnesses, RED (one build-slot run, filter `/*/*/(PoolProviderProjectionStampTests*)|(AgentSessionBackendTests*)|(AgentAttachHerdrTests*)/C0167_*`: total 5, failed 5, each on the guarded assertion; messages in the plan amendment). Adds only the `UnknownPoolProjection` constant as a compile skeleton.
- 0e68a9b28f62986d00045e4dfbcc0e0982f8547a fix: one map read per launch shared by spec and stamp.
- 4e36b93d0f4fd9c85226a7fd6769050dc1419673 docs: plan amendment docs/superpowers/plans/2026-10-22-card-0167-provider-endpoint-plan-s3-repair2.md (CP-1..CP-6).
- this commit: report; the final checkpoint run verifies this tip.

## Fix, by path
- Cold dispatch: claim marks the pool row `unknown`; `BuildLaunchSpecAsync` (profile-less, the only branch a pool delegate takes) stamps from the endpoints it read for the spec and saves before the launch is enqueued. Closes the claim-read vs spec-read race.
- Boot-wedge relaunch (`RelaunchWedgedAsync`): relaunch commit marks `unknown`; the same spec builder restamps from the relaunch's read.
- AgentControlService start/resume (`StartInteractiveSessionAsync`, both arms): stamps from `ResolvedAgentTuiLaunch.ProjectProviderEndpoints` (the funnel's one read, new init-only property).
- AgentControlService Herdr adopt (`AttachHerdrAsync`): pool row marked `unknown` (the adopted process's env cannot be known). `unknown` is not JSON, so it never equals a fingerprint (JSON or null) and matches no task; null is not used because null matches a no-endpoint task.

Results of the checkpoint run are in the final message (CHECKPOINT lines, receipt validation).
