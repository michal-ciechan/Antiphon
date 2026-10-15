# CARD-1281 B-3 Code evidence

Original Code task / landing owner: 213a9975-dc75-4198-8497-828688e8e35a.
Branch: feat/card-task-213a9975. Worktree: /work/worktrees/task-213a9975.
Task base: 5dd289dbc37a3eeb82583899c4cb3fbd647f02b2.
Plan: docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md.
Manifest: docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md.

Scope: B-3 services/DTO/endpoints, CardService atomic terminal write, publication-close settings and lifecycle owner. No migration, shared model/admission edits or Program registration. S6 owns HTTP authorization/wiring integration V-33..V-36 and R-8; S0 already owns V-24/V-26. Default close remains Disabled. No automatic sweep is added.

Commissioning explicitly waives CP-5/full Unit (CARD-1287). The appended manifest assigns CP-17..23 to S6, not B-3. This component therefore runs original CP-6 with its exact class filter and a standalone isolated build replacing CP-5 reuse. The extra CP-24 contains existing Unit classes reading the changed lifecycle owner: RunnerBranchContractDocumentationTests, CardFilePrivacyDocumentationTests, BlockedTaskParkProjectionTests, PipelineDefinitionDocumentationTests, InstructionFileStampTests. Inspection found no Unit text pins for the new services/settings/CompletePublishedAsync. Exact copied rows live in ignored .antiphon/c1281-b3-green.md. No assembly or namespace sweeps.

The red-first compile-only acceptance contract committed at dbef949ec8cdc37da501080fff48927cdb03c8e9 intentionally persisted no readiness. Its real migrated-DB test failed at ready-metadata-survives-restart; all 13 unchanged transition methods passed. Only the first new method existed then (14 executions; the eventual 20-case floor was not yet met). The six remaining substantive methods were committed before their production implementation. They cover parent V-13..V-18; deliberate guard bypasses remain Mutation work.

One launcher invocation with an abbreviated SHA was refused before row execution; the corrected invocation below used the full SHA. Tool bootstrap used the host build-slot wrapper (granted, waited=0s). All row evidence remains ignored. Production publication positive uses LandingSafetyHarness/private bare remote/independent fetch, with its declared controlled verifier; it is not deployment evidence. The 30-event qualification test seeds correlated retained rows and is not live Q-4 qualification.

Final execution is recorded in .antiphon/task-213a9975.md after this source commit is frozen. This file does not pre-claim green. Required ordinary selection: V-12..V-18 and R-3 through CP-6, plus CP-24. Live Q-4 remains pending and close must not be enabled. Restart: none for this unregistered component; server after S6.

PC-27..PC-40, PC-58..PC-64 remain pending. Shared B-3 PCs PC-74..PC-83, PC-105..PC-110, PC-114..PC-116, PC-119..PC-121 remain pending for their owning S0/S6/SourceLanding packs. No PC was executed. Mutation owns additional guard discovery and final compiling payload bindings; unbound candidates belong under docs/superpowers/mutations/.

Red checkpoint, unedited:
--- checkpoint report ---
run: 20261015-125636-3d5f   manifest: /work/worktrees/task-213a9975/.antiphon/checkpoints/20261015-125636-3d5f/manifest.resolved.yaml
commit: dbef949ec8cdc37da501080fff48927cdb03c8e9  branch: feat/card-task-213a9975  worktree: /work/worktrees/task-213a9975  host: Debian GNU/Linux 12 (bookworm) cores=24
source: dbef949ec8cdc37da501080fff48927cdb03c8e9 state=clean buildSource=verified
CHECKPOINT CP-6 commit=dbef949ec8cdc37da501080fff48927cdb03c8e9 build=ok filter=/*/*/(PublishedCardCompletionTests*)|(CardWorkTransitionServiceTests*)/* executed=14 passed=13 failed=1 skipped=0 trx=/work/worktrees/task-213a9975/.antiphon/checkpoints/20261015-125636-3d5f/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=dbef949ec8cdc37da501080fff48927cdb03c8e9 sourceState=clean buildSource=verified
PHASES CP-6 slotWait=0s build=415.1276644s startup=50.0359413s testsWall=13.1868159s teardown=1.6017862s hostWall=64.8248555s
FAILED Antiphon.Tests.Application.PublishedCardCompletionTests.C1281_ReadyForRelease (CP-6) ShouldAssertException: view.Readiness     should be "ready for release"     but was null  Additional Info:     ready-metadata-survives-restart -> rows/CP-6/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 8m15s  sequential-equivalent: 1m05s  builds: 1  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
refused: still-present
outputs: refused/kept bin-c1281-b3-red/
evidence: /work/worktrees/task-213a9975/.antiphon/checkpoints/20261015-125636-3d5f/report.md
verdict: RED exit=1
