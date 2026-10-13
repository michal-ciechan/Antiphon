# CARD-1032 S5 — incomplete Code report

Partial S5 implementation and tests are pushed. **Blocked; not complete or
Review-ready.** Production execution remains uncomposed. Restart: none.

Code/landing owner: a7f248c0-cf34-455c-b4ac-bdc4fc638e19.
Branch: feat/card-task-a7f248c0.
Worktree: /work/worktrees/task-a7f248c0.
Base: 99910a78c7ec4295786ca94923a040013c689fcd.
Implementation tip tested: 69a7b005ee783ad24b35b00c6c9650a964c1afcd.
This evidence amendment changes documentation only. Final pushed HEAD and
publication/static checks are supplied in the caller-facing report.

## Blocking dependency

Frozen V-3 requires an eligible bound-rebase positive; PC-246 requires refusing
an unrelated confirmed landing mapping. RunnerCompletedCardWorkspaceRemoval.
CheckProofAsync supports only direct containment, rejects Rebased as
preservation_proof_unsupported, and expressly defers a bound mapping validator
to S5. CompletedCardWorkspaceProof carries opaque ProofIdentity and source SHAs;
there is no recipient validator connected to authoritative task/landing-operation
mapping. The planned mirror-specific producer/admission owner,
server/Application/Services/CardWorktreeMirrorCleanupService.cs (S7), is absent
in this checkout and fetched origin/master at the base SHA. The S6
ICompletedCardWorkspaceClient is also absent; its transport is not needed by the
direct S5 scratch calls and cannot substitute for mapping proof. The existing
local ConfirmedPublicationAsync excludes already-complete local cleanup;
the plan expressly forbids copying it as mirror proof.

The brief requires STOP if S5 needs unlanded S6/S7 work. Resolve the bound mapping
validator's sequencing/ownership, or explicitly revise frozen S5 acceptance,
before continuation. No fabricated landing, success DTO, runner-to-server
reference or relaxed proof assertion was added. Bound-rebase ordinary acceptance
is **not passed**. No production cleanup capability/composition was enabled.

## Remaining implementation/ordinary witnesses

These are deferred requirements, not waivers or passes:

- CardDone evidence admission/physical evidence disposal. Current inspection
  holds evidence_requires_admission; the physical positive covers bin/cache.
- Real foreign repository with equal SHA/admin-coordinate fixtures and genuine
  SourceLanding creation exclusion. Current tests cover binding-field changes
  and missing ordinary binding; those proxies do not certify richer fixtures.
- Start-first destructive ordering and all adoption paths through the actual
  remover. The existing S3 gate-order tests were not rerun here.
- Process-death cuts after rename/unregister/partial deletion/receipt, owned
  joined child and fresh census. Current cuts throw deterministic exceptions
  and reconstruct journal/executor/gate within the same test process.
- Partial-file deletion/remaining allowance assertions, occupied set-aside
  foreign bytes, and independent full receipt-field tamper coverage.
- Final whole Unit lane, full CompletedCardWorkspaceRemovalTests class, named
  adjacent LegacyWorkspaceRemovalGateTests/RunnerWorkspaceServiceTests classes,
  and a final same-SHA CP-7..10 run. No full assembly was run.
- Parent native qualification/activation. Linux descriptor-layout proof is
  limited to this x64 host; other Linux architectures and Windows are untested.

## Changes

- CompletedCardWorkspaceS5Tests.cs: 23 named destructive/refusal/replay witnesses
  with case -> mutation -> decisive red comments, deterministic cuts and
  physical source/registration/ref/sentinel oracles.
- RunnerCompletedCardWorkspaceRemoval.S5.cs: explicitly called executor,
  first/final direct proof, journal-before-effect, component recovery/receipt.
- RunnerCompletedCardWorkspaceRemoval.cs: partial declaration/scope comment;
  read-only inspection implementation unchanged.
- RunnerWorkspaceRemovalJournal.cs: frozen sibling/root identities/manifests,
  phases/remaining allowance/receipt alongside S3 full command and digest.
- RunnerWorkspaceNoFollowDelete.cs: Linux openat/O_NOFOLLOW with descriptor
  enumeration/unlinkat; Windows handle-delete port; frozen-manifest checking.
- RunnerWorkspaceUseGate.cs: explicit removal admission refuses a held use
  lease; S3 TryRetireAsync retains its waiting callback contract. This narrow
  dependency edit preserves the brief's held-lease refusal.
- docs/superpowers/plans/2026-10-13-card-1032-s5-verification.md: same 23-method
  S5 roster, required OR wildcards, one shared isolated build, pending obligations.

No existing assertion was weakened or timeout widened. No legacy API,
registration prune, branch removal, production setting or live path was used.

## Delete/move inventory

| Effect | Target | Gate |
|---|---|---|
| Directory.Move | recorded ordinary tree -> operation-derived sibling | command/binding; shared retirement lease/census/owners; durable intent/set-aside identity; first/final source/remote proof; root identity/frozen inventory; occupied sibling refuses |
| Linux unlinkat / Windows SetFileInformationByHandle | exact owned admin directory/manifest entries | admitted command; durable Unregistering; admin identity and frozen manifest; no repository prune |
| Linux unlinkat / Windows SetFileInformationByHandle | owned set-aside/manifest entries | admitted command; durable Deleting; root identity from rename; remaining manifest; registration absence; original replacement untouched |
| AtomicWrite File.Move(overwrite) | configured external journal/identity | existing S3 root/store checks; S5 Save requires existing operation/digest and valid progress |
| AtomicWrite finally File.Delete | GUID-named temporary sibling | created by that atomic write; retained pre-existing S3 path |
| Fixture Directory.Delete / File.Delete | isolated fixture scratch/cut/swap entries | test-owned roots; fixture reaps/joins owned children |
| Checkpoint output cleanup | manifest-owned bin-c1032-owners/tool shadows | checkpoint owner completion/clean; no production bin or worktree removal |

No touched production path calls Git remove/prune or deletes branch refs. Git
proof/eligibility calls retain S4 configuration-isolated InspectionGitAsync.
Native ancestor/handle race qualification and further control discovery are
pending; no universal no-follow guarantee is claimed.

## Ordinary verification

Execution was bound: ANTIPHON_TASK_TOKEN was present, never printed. Required
GET /api/runner-defaults and /api/session-runners succeeded (revision 2 defaults,
eligible Linux/Windows entries). No Runner or Platform pin. Every selected
build/row: slot=granted, waited=0s. No timeout/unleased retry. A transient owner
read HTTP 502 recovered without owner-unverified settlement.

| Selection | Tested SHA | executed/passed/failed/skipped |
|---|---|---|
| baseline CP-7 | 34ff08aa8f1eb22e9f04f32900b3bcb1e435941a | 5/0/5/0 |
| baseline CP-8 | same | 9/0/9/0 |
| baseline CP-9 | same | 4/0/4/0 |
| baseline CP-10 | same | 5/0/5/0 |
| implementation CP-7 | b80db4fa1a27b90da03da5f937c4d3968685bbf8 | 5/4/1/0 |
| implementation CP-8 | same | 9/8/1/0 |
| implementation CP-9 | same | 4/4/0/0 |
| implementation CP-10 | same | 5/0/5/0 |
| repaired CP-7 | 69a7b005ee783ad24b35b00c6c9650a964c1afcd | 5/5/0/0 |
| repaired CP-8 | same | 9/9/0/0 |
| repaired CP-10 | same | 5/5/0/0 |

All 23 new methods executed red before production changes: missing destructive
API/cut seams, not compilation failure. Baseline reachability is not guard
mutant proof. Fresh TRX class/method rosters matched baseline exactly with
nonzero counts. Receipt validator exited 0 for repaired CP-7/8/10 at 69a7b005
and CP-9 at b80db4fa. Those are **two tested SHAs**, not a four-row Final green
certificate for HEAD.

Build errors were introduced and fixed: test used exception.Type instead of Code,
and helper lacked Contracts import. They are not defect-red evidence. One
incorrectly transcribed expected SHA was refused before build/test. The seven
runtime positive failures were my admin logs/HEAD classification defect; frozen
manifest handling fixed them. No failure was attributed to inherited behavior.

Only unlisted build: checkpoint-tool bootstrap, required to execute the tool:

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1032-s5-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1032-s5-tool/ --property:UseAppHost=false --verbosity:quiet

Bootstrap source: 77f9a54c2 (tests-only); one warning, zero errors;
slot=granted waited=0s. It produced only its tool-project alternate output.
No extra test driver ran. Task-owned alternate outputs were cleaned. Meaningful
fixes were committed/pushed before checkpoint runs; source was frozen in flight.
Each unchanged selection ran once, no loaded repeats or deliberate mutants.

## V/R and PC accounting

| ID | Actual outcome |
|---|---|
| V-1 | not run S5; earlier creation/capture and later coordinator obligations carried forward |
| V-2 | incomplete; later transport/receipt/inert composition pending |
| V-3 | partial CP-8/9 green; bound rebase, richer identity/evidence fixtures pending |
| V-4 | partial CP-7 green; full affected/S3 admission roster pending |
| V-5 | partial physical/admin removal and exception replay green; process death/allowance/evidence pending |
| V-6 | deferred S7/S8; no queue/delivery claim |
| R-1 | not run; Final adjacent classes and later CP-12/16 pending |
| R-2 | not run; prior legacy behavior retained; later transport/inert composition pending |
| R-3 | 23 S5 baseline reds; latest green on two SHAs; frozen vectors/Final incomplete |

S5 PC-53..70, PC-108/109, PC-240..266 and **every variant** remain pending for
method-scoped SourceLanding Mutation. PC-52 (S6) and all earlier/later canonical
plan/repair PCs also remain pending. No post-land Mutation or intentional mutant
was run. Missing-control discovery belongs to Mutation; this partial result
does not waive controls. Unbound candidate root: docs/superpowers/mutations/.
Restart: none.

## Reproduction and source checks

Bootstrap tool through build-slot, then invoke its built DLL without wrapping
checkpoint execution in another slot:

    dotnet exec tools/Antiphon.Checkpoints/bin-c1032-s5-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-13-card-1032-s5-verification.md --rows CP-7,CP-8,CP-9,CP-10 --expected-source-sha <committed HEAD>

Await foreground run and every exit-75 wait. The 23-method roster does not cover
pending richer fixtures or Final scope. Do not compose partial cleanup into
production.

Fetched master: 99910a78c7ec4295786ca94923a040013c689fcd. Merge-tree at implementation
tip exited 0 (tree 2705a5cdc67aecaee1d1a445581602e443259958). Before this report,
git ls-files=7521. Final count, clean check, full base..HEAD evidence guard,
merge-tree and ls-remote are supplied after this evidence-only commit.

## Unedited checkpoint reports

### Run 20261013-083538-ec67

```text
--- checkpoint report ---
run: 20261013-083538-ec67   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083538-ec67/manifest.resolved.yaml
commit: 13d528c92b618c9c7239cd0882dc38ac34915f57  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 13d528c92b618c9c7239cd0882dc38ac34915f57 state=clean buildSource=unknown
CHECKPOINT CP-7 commit=13d528c92b618c9c7239cd0882dc38ac34915f57 build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=13d528c92b618c9c7239cd0882dc38ac34915f57 sourceState=clean buildSource=unknown
CHECKPOINT CP-8 commit=13d528c92b618c9c7239cd0882dc38ac34915f57 build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=13d528c92b618c9c7239cd0882dc38ac34915f57 sourceState=clean buildSource=unknown
CHECKPOINT CP-9 commit=13d528c92b618c9c7239cd0882dc38ac34915f57 build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=13d528c92b618c9c7239cd0882dc38ac34915f57 sourceState=clean buildSource=unknown
CHECKPOINT CP-10 commit=13d528c92b618c9c7239cd0882dc38ac34915f57 build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=13d528c92b618c9c7239cd0882dc38ac34915f57 sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 0m44s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 4 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-083538-ec67
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083538-ec67/report.md
verdict: RED exit=2

```

### Run 20261013-083648-a398

```text
--- checkpoint report ---
run: 20261013-083648-a398   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083648-a398/manifest.resolved.yaml
commit: 34ff08aa8f1eb22e9f04f32900b3bcb1e435941a  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 34ff08aa8f1eb22e9f04f32900b3bcb1e435941a state=clean buildSource=verified
CHECKPOINT CP-7 commit=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083648-a398/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=52.4223708s startup=2.2061227s testsWall=0.787246s teardown=0.4955558s hostWall=3.4889459s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerProcessOwnerHolds (CP-7) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-7/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_UnknownProcessOwnershipHolds (CP-7) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-7/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerStartFence (CP-7) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam BeforeMutationForTest -> rows/CP-7/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerReadoptionFence (CP-7) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam BeforeMutationForTest -> rows/CP-7/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerIntentBeforeIo (CP-7) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam BeforeMutationForTest -> rows/CP-7/failures.md
CHECKPOINT CP-8 commit=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=0 failed=9 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083648-a398/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=2.1914382s testsWall=4.3082822s teardown=0.6370463s hostWall=7.1367672s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerTaskIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerAttemptIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerStoreIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerRepositoryIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerPathIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerBranchIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerHeadIdentity (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerPreservationProof (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerSourceLandingExcluded (CP-8) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-8/failures.md
CHECKPOINT CP-9 commit=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083648-a398/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=2.6466079s testsWall=0.841592s teardown=0.5675555s hostWall=4.055756s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerDirtySource (CP-9) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-9/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerIgnoredPolicy (CP-9) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-9/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerNoForceBypass (CP-9) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-9/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerFreshFinalInspection (CP-9) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam AfterInitialInspectionForTest -> rows/CP-9/failures.md
CHECKPOINT CP-10 commit=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083648-a398/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=34ff08aa8f1eb22e9f04f32900b3bcb1e435941a sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=2.2421156s testsWall=0.8161389s teardown=0.5981412s hostWall=3.656396s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical (CP-10) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerDigestReplay (CP-10) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery (CP-10) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam BeforeMutationForTest -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalOriginalReplacement (CP-10) ShouldAssertException: method     should not be null but was  Additional Info:     S5 guarded destructive entry point must exist -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerNoFollow (CP-10) ShouldAssertException: property     should not be null but was  Additional Info:     S5 deterministic cut seam BeforeDeleteEntryForTest -> rows/CP-10/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 1m14s  sequential-equivalent: 0m20s  builds: 1  max-concurrent-builds: 1  rows: 0 green 4 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-083648-a398
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-083648-a398/report.md
verdict: RED exit=1

```

### Run 20261013-084146-3c42

```text
--- checkpoint report ---
run: 20261013-084146-3c42   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084146-3c42/manifest.resolved.yaml
commit: 97174f49d695faaf89add2de485be4b48901a08a  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 97174f49d695faaf89add2de485be4b48901a08a state=clean buildSource=unknown
CHECKPOINT CP-7 commit=97174f49d695faaf89add2de485be4b48901a08a build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=97174f49d695faaf89add2de485be4b48901a08a sourceState=clean buildSource=unknown
CHECKPOINT CP-8 commit=97174f49d695faaf89add2de485be4b48901a08a build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=97174f49d695faaf89add2de485be4b48901a08a sourceState=clean buildSource=unknown
CHECKPOINT CP-9 commit=97174f49d695faaf89add2de485be4b48901a08a build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=97174f49d695faaf89add2de485be4b48901a08a sourceState=clean buildSource=unknown
CHECKPOINT CP-10 commit=97174f49d695faaf89add2de485be4b48901a08a build=failed filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=97174f49d695faaf89add2de485be4b48901a08a sourceState=clean buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 0m10s  sequential-equivalent: 0m00s  builds: 1  max-concurrent-builds: 1  rows: 0 green 4 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-084146-3c42
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084146-3c42/report.md
verdict: RED exit=2

```

### Run 20261013-084321-af03

```text
--- checkpoint report ---
run: 20261013-084321-af03   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084321-af03/manifest.resolved.yaml
commit: b80db4fa1a27b90da03da5f937c4d3968685bbf8  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: b80db4fa1a27b90da03da5f937c4d3968685bbf8 state=clean buildSource=verified
CHECKPOINT CP-7 commit=b80db4fa1a27b90da03da5f937c4d3968685bbf8 build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=5 passed=4 failed=1 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084321-af03/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=b80db4fa1a27b90da03da5f937c4d3968685bbf8 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=23.8321177s startup=2.5780406s testsWall=2.616414s teardown=0.5747902s hostWall=5.7692466s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerIntentBeforeIo (CP-7) ShouldAssertException: await r.Remove()).GetProperty("complete").GetBoolean(     should be True     but was False -> rows/CP-7/failures.md
CHECKPOINT CP-8 commit=b80db4fa1a27b90da03da5f937c4d3968685bbf8 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=8 failed=1 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084321-af03/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=b80db4fa1a27b90da03da5f937c4d3968685bbf8 sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=2.7264494s testsWall=3.5077843s teardown=0.5725874s hostWall=6.8068214s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerPreservationProof (CP-8) ShouldAssertException: await good.Remove()).GetProperty("complete").GetBoolean(     should be True     but was False -> rows/CP-8/failures.md
CHECKPOINT CP-9 commit=b80db4fa1a27b90da03da5f937c4d3968685bbf8 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerDirtySource*)|(C1017_RunnerIgnoredPolicy*)|(C1017_RunnerFreshFinalInspection*)|(C1017_RunnerNoForceBypass*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084321-af03/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=b80db4fa1a27b90da03da5f937c4d3968685bbf8 sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=2.4441882s testsWall=2.5939588s teardown=0.5742398s hostWall=5.612387s
CHECKPOINT CP-10 commit=b80db4fa1a27b90da03da5f937c4d3968685bbf8 build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=0 failed=5 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084321-af03/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=b80db4fa1a27b90da03da5f937c4d3968685bbf8 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=2.359013s testsWall=2.552006s teardown=0.578237s hostWall=5.4892561s
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalEligiblePhysical (CP-10) ShouldAssertException: await r.Remove(); answer.GetProperty("complete").GetBoolean()     should be True     but was False -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerDigestReplay (CP-10) ShouldAssertException: first.GetProperty("complete").GetBoolean()     should be True     but was False -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalComponentRecovery (CP-10) ShouldAssertException: (bool)result.Complete     should be True     but was False -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1032_RemovalOriginalReplacement (CP-10) ShouldAssertException: await r.Remove()).GetProperty("complete").GetBoolean(     should be True     but was False -> rows/CP-10/failures.md
FAILED Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests.C1017_RunnerNoFollow (CP-10) ShouldAssertException: await r.Remove()).GetProperty("complete").GetBoolean(     should be True     but was False -> rows/CP-10/failures.md
unlisted: none (the tool ran no other build or test command)
wall: 0m52s  sequential-equivalent: 0m26s  builds: 1  max-concurrent-builds: 1  rows: 1 green 3 red 0 skipped
outputs: kept bin-c1032-owners/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261013-084321-af03
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084321-af03/report.md
verdict: RED exit=1

```

### Run 20261013-084529-250d

```text
--- checkpoint report ---
run: 20261013-084529-250d   manifest: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084529-250d/manifest.resolved.yaml
commit: 69a7b005ee783ad24b35b00c6c9650a964c1afcd  branch: feat/card-task-a7f248c0  worktree: /work/worktrees/task-a7f248c0  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 69a7b005ee783ad24b35b00c6c9650a964c1afcd state=clean buildSource=verified
CHECKPOINT CP-7 commit=69a7b005ee783ad24b35b00c6c9650a964c1afcd build=ok filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerProcessOwnerHolds*)|(C1017_RunnerStartFence*)|(C1017_RunnerReadoptionFence*)|(C1017_RunnerIntentBeforeIo*)|(C1017_UnknownProcessOwnershipHolds*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084529-250d/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=69a7b005ee783ad24b35b00c6c9650a964c1afcd sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=19.3567744s startup=2.8329138s testsWall=2.2044285s teardown=0.5717551s hostWall=5.6091352s
CHECKPOINT CP-8 commit=69a7b005ee783ad24b35b00c6c9650a964c1afcd build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerTaskIdentity*)|(C1017_RunnerAttemptIdentity*)|(C1017_RunnerStoreIdentity*)|(C1017_RunnerRepositoryIdentity*)|(C1017_RunnerPathIdentity*)|(C1017_RunnerBranchIdentity*)|(C1017_RunnerHeadIdentity*)|(C1017_RunnerPreservationProof*)|(C1017_RunnerSourceLandingExcluded*) executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084529-250d/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=69a7b005ee783ad24b35b00c6c9650a964c1afcd sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=2.3900797s testsWall=3.2375419s teardown=0.5995472s hostWall=6.227168s
CHECKPOINT CP-10 commit=69a7b005ee783ad24b35b00c6c9650a964c1afcd build=reused filter=/*/*/CompletedCardWorkspaceRemovalTests/(C1017_RunnerNoFollow*)|(C1017_RunnerDigestReplay*)|(C1032_Removal*) executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084529-250d/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=69a7b005ee783ad24b35b00c6c9650a964c1afcd sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=2.6368238s testsWall=3.5359885s teardown=0.5762055s hostWall=6.7490171s
unlisted: none (the tool ran no other build or test command)
wall: 1m00s  sequential-equivalent: 0m39s  builds: 1  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: deleted bin-c1032-owners/
evidence: /work/worktrees/task-a7f248c0/.antiphon/checkpoints/20261013-084529-250d/report.md
verdict: GREEN exit=0

```

## Caller decision and resumed S5 — 2026-10-13

The blocked outcome above is historical. The caller explicitly directed Code to
proceed: move only V-3 bound-rebase positive and PC-60 positive fixture variant
to S7 CP-13 C1032_AdmissionBoundRebaseProof; retain a pinned S5 Rebased refusal.
The plan and test design now enumerate those two moved obligations, add the S7
method/result and name its pending control. No S5 method was dropped.

Resumed implementation admits CardDone evidence only with digest-bearing external
report/artifact/policy proof, validates saved receipts against complete immutable
identity, retains charged action allowance across replay, and rechecks destination
proof after set-aside. Twelve locally owned worker cuts cover Intent, Prepared,
Moved, Unregistering, Unregistered, Deleting, Deleted, Complete, post-rename,
post-unregister, post-delete and partial-file deletion. Each child is identified
by PID plus exact OS birth token, killed and joined; fresh journal/runtime/gate
objects resume the same command. There is no queue/delivery claim from these
local filesystem witnesses; V-6 remains S7/S8's actual transport obligation.

CP-7..10 ran green at e5d8efc179f9a0ed083f534396606f36e10e216f: 5/9/4/5 results,
zero failures/skips, clean source and verified build, slot=granted waited=0s.
Evidence/refusals and receipt tampering were demonstrated red before their fixes;
unedited resumed run receipts are stored in the later completion evidence note.
The Final supplement is pending at this evidence-only amendment commit. The
later completion report supersedes the initial pending-work list using actual
results. All PCs/variants remain pending; no deliberate mutants ran. Restart:none.
