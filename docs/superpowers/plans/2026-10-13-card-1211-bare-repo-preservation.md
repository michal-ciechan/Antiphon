# CARD-1211: preserve ignored bare repositories

Code/landing owner: e37a97b6-e6b9-4f2a-9585-a93d27dcd52e (round 2 repair).
Original Code task: 578ae84e-829d-4da6-b4bc-2354e3bb876c.
Repair base: 021f14732650087ddb5a8cfc1d1dcb72ad789bce.
Base: 6e58767debf33d6a09c4e88050095f1cfdf76332 (CARD-0971 landed).
Scope: WorktreeNestedRepositories and real scratch-repository tests only.

## Decision

Extend the filesystem-only preservation census used by both guarded cleanup
readings and by WorktreeNoFollowDelete (initial and recorded set-aside resume).
A bare repository has HEAD, objects/ and refs/ directly in its root; it has no
.git entry. Recognize entry names without case sensitivity. Do not parse config
or spawn private Git in cleanup. A HEAD entry alone is ambiguous administrative
evidence and is preserved conservatively, including links or malformed entries.
V-1..3 protect the direct/deep/suffix/mixed-case bare layouts; V-5 protects
the HEAD-only cleanup layout after objects/refs have disappeared.
This is a preservation heuristic, not proof that an arbitrary HEAD is a valid
repository: ordinary output without .git or HEAD still cleans; output named HEAD
may intentionally remain as residue. A .git suffix alone grants no protection.
Objects/refs without HEAD are explicitly disposable (V-4); unknown/access errors
continue to propagate to fail-closed cleanup. Tests V-1..6 name these boundaries.

Recovery proof refuses exposed strict bare signatures (HEAD file plus objects/
and refs/ directories, without a .git entry), including the direct/deep V-7
layouts that Git enumerates as files. Cleanup alone uses HEAD-only preservation;
V-11 accepts that output in proof and preserves it after publication. A directory
with a .git entry ends this census before the bare check. Existing .git directory
admission and file-pointer refusal remain unchanged (R-1 and V-9).
The proof does not descend inside an accepted nested .git directory: a bare repo
there remains accepted and is preserved with its enclosing repo (V-10). This
is a boundary of the proof, not blanket refusal of bare repositories.

CARD-0971 deletion audit paths requiring this detection: publication, LocalMerge,
settled-task retirement, completed-card removal, initial managed deletion and
recorded set-aside resume. They already share WorktreeNestedRepositories; change
its predicate once. Exact common-directory admin deletion is distinct and remains
unchanged. Detached landing-workspace clean, inventory-bound verification deletion,
creation rollback and diagnostic scratch disposal are outside this owner cleanup.
R-6..10 and V-8 exercise the shared policy and late deletion boundary.

S1: commit/push this design. S2: commit/push tests before implementation; CP-1
must reproduce deletion/refusal defects on unchanged base production. S3: narrow
fix committed/pushed before CP-2..13. No assertion/deadline relaxation or green repeats.

## Verification design

All new tests are in AgentTaskLandBareRepositoryCleanupTests, use real scratch
Git and the real landing queue, and carry the process-spawn limiter.

| ID | Named test / case -> expected | Mutation -> red assertion |
|---|---|---|
| V-1 | C1211_PublicationPreservesBareRepository: obj/private -> bytes and ownership retained | omit bare detection -> missing HEAD after confirmed publication |
| V-2 | same: obj/deeper/cache/private | omit deep detection -> missing HEAD |
| V-3 | same: obj/private.git and obj/twin with HeAd/ObJeCtS/ReFs | name-only / case-sensitive detection -> missing administrative entry |
| V-4 | C1211_DisposableLookalikeIsCleaned: objects/refs without HEAD | preserve any repo-looking layout -> cleanup not Complete |
| V-5 | C1211_PublicationPreservesBareRepository: HEAD only | require complete layout -> missing HEAD |
| V-6 | C1211_DisposableLookalikeIsCleaned: plain obj output | preserve all obj -> cleanup not Complete |
| V-7 | C1211_BareRecoveryIsRefused: direct/deep bare | allow bare recovery -> proof unexpectedly accepted / queue publishes |
| V-8 | C1211_LateBareRepositorySurvivesSetAsideResume | omit deletion guard initially / on resume -> missing HEAD |
| V-9 | C1211_NestedTrackedHeadKeepsExistingAdmission: .git directory accepts despite tracked HEAD; .git file keeps strict refusal and ordinary publication preservation | classify .git directory with HEAD as bare -> F1.GitDirectoryWithTrackedHeadAccepted; omit .git cleanup guard -> F.NestedDirectorySurvivesCleanup |
| V-10 | C1211_BareInsideAcceptedNestedRepositoryIsPreserved: enclosing repo with/without tracked HEAD accepts; enclosed bare bytes survive queue publication | descend into accepted nested repo -> F2.EnclosingRepositoryAdmissionDoesNotDescend; omit enclosing cleanup guard -> F.NestedDirectorySurvivesCleanup |
| V-11 | C1211_HeadOnlyOutputIsPreservedWithoutBareProofRefusal: HEAD-only output accepts proof, survives publication | use cleanup HEAD heuristic for proof -> F1.HeadOnlyIsNotStrictBareSignature |
| R-1 | full LandRecoveryIgnoredNestedRepositoryTests | unchanged 15 proof shapes (plus 8 cleanup shapes in R-6) |
| R-2 | full LandRecoveryCheckoutTests | unchanged checkout proof |
| R-3 | full LandRecoveryIgnoredObstructionTests | unchanged collisions |
| R-4 | full LandingRegistrationFreshnessTests | unchanged registration freshness |
| R-5 | full AgentTaskLandHalfResetTests | unchanged recovery/publication |
| R-6 | full AgentTaskLandNestedRepositoryCleanupTests | unchanged .git preservation |
| R-7 | full WorktreeGuardedCleanupTests | unchanged cleanup guards |
| R-8 | full TaskWorktreeRetirementTests and WorktreeRetirementRaceTests | unchanged retirement authority/races |
| R-9 | full LandingRemovalPolicyControlTests | unchanged removal policy |
| R-10 | full CompletedCardWorktreeRemovalTests | unchanged completed-card authority |

V-4/V-6 and existing refusal controls may already pass on base; do not call them
red. Their deliberate mutants belong to SourceLanding Mutation. Inspect fresh TRX
method/argument rosters and nonzero counts; preserve unedited CHECKPOINT lines,
SHA-qualified receipts and build provenance. Manual: merge-tree against fetched
master, full base..HEAD evidence diff, remote-ref equality, tracked count, clean tree.
Bound owner if ANTIPHON_TASK_TOKEN exists; never print it. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1211-base/` | bare-base | `/*/*/AgentTaskLandBareRepositoryCleanupTests/*` | V-1..8 | vulnerable rows assertion-red; controls green | 10 | 4 | true |
| CP-2 | Repair-fix | `tests/Antiphon.Tests -> bin-c1211-r2-final/` | bare-final | `/*/*/AgentTaskLandBareRepositoryCleanupTests/*` | V-1..11 | all 15 green | 15 | 4 | true |
| CP-3 | Repair-fix | CP-2 | nested-proof | `/*/*/LandRecoveryIgnoredNestedRepositoryTests/*` | R-1 / C971 CP-2 | all green | 15 | 2 | true |
| CP-4 | Repair-fix | CP-2 | checkout-proof | `/*/*/LandRecoveryCheckoutTests/*` | R-2 / C971 CP-3 | all green | 12 | 2 | true |
| CP-5 | Repair-fix | CP-2 | obstructions | `/*/*/LandRecoveryIgnoredObstructionTests/*` | R-3 / C971 CP-4 | 5 passed; 2 Windows skips | 5 | 1 | true |
| CP-6 | Repair-fix | CP-2 | registration | `/*/*/LandingRegistrationFreshnessTests/*` | R-4 / C971 CP-5 | all green | 4 | 1 | true |
| CP-7 | Repair-fix | CP-2 | half-reset | `/*/*/AgentTaskLandHalfResetTests/*` | R-5 / C971 CP-6 | all green | 20 | 4 | true |
| CP-8 | Repair-fix | CP-2 | nested-cleanup | `/*/*/AgentTaskLandNestedRepositoryCleanupTests/*` | R-6 / C971 CP-11 | all 8 green | 8 | 2 | true |
| CP-9 | Repair-fix | CP-2 | guarded-cleanup | `/*/*/WorktreeGuardedCleanupTests/*` | R-7 / C971 CP-12 | green; Windows skips allowed | 1 | 2 | true |
| CP-10 | Repair-fix | CP-2 | retirement | `/*/*/TaskWorktreeRetirementTests/*` | R-8 / C971 CP-13 | all green | 1 | 2 | true |
| CP-11 | Repair-fix | CP-2 | retirement-races | `/*/*/WorktreeRetirementRaceTests/*` | R-8 / C971 CP-14 | all green | 1 | 2 | true |
| CP-12 | Repair-fix | CP-2 | removal-policy | `/*/*/LandingRemovalPolicyControlTests/*` | R-9 / C971 CP-15 | green; Windows skips allowed | 1 | 2 | true |
| CP-13 | Repair-fix | CP-2 | completed-removal | `/*/*/CompletedCardWorktreeRemovalTests/*` | R-10 / C971 CP-16 | green; Windows skips allowed | 1 | 2 | true |

| CP-14 | Repair-tests | `tests/Antiphon.Tests -> bin-c1211-r2-base/` | tracked-head-baseline | `/*/*/AgentTaskLandBareRepositoryCleanupTests/C1211_NestedTrackedHeadKeepsExistingAdmission` | V-9 / F1 | directory assertion-red on 021f14732; file-pointer control green | 2 | 4 | true |
| CP-15 | Repair-tests | CP-14 | enclosing-bare-baseline | `/*/*/AgentTaskLandBareRepositoryCleanupTests/C1211_BareInsideAcceptedNestedRepositoryIsPreserved` | V-10 / F2 | tracked HEAD assertion-red; no HEAD already green on 021f14732 | 2 | 2 | true |
| CP-16 | Repair-tests | CP-14 | head-only-proof-baseline | `/*/*/AgentTaskLandBareRepositoryCleanupTests/C1211_HeadOnlyOutputIsPreservedWithoutBareProofRefusal` | V-11 | assertion-red on 021f14732 | 1 | 1 | true |

Round 2: commit/push tests and this manifest first, select CP-14..16 on unchanged
021f14732 production; then commit/push the fix and select CP-2..13. CP-1 is
historical and is not selected. CP-2 runs the complete 15-case affected class.
The F2 no-HEAD boundary and .git-file control already pass on the repair base;
they cannot honestly be reported red. Deliberate controls remain Mutation work.
Ordinary scope is the brief's explicit closed slice: no broad Unit lane (inherited
Unit findings tracked under CARD-1209/1210). Full named classes bound shared cleanup
impact; no unbounded integration or assembly run. Estimated final floor 26 minutes.
Tool bootstrap alone uses build-slot and bin-c1211-tool/, UseAppHost=false.

### Pending positive controls

PC-1: C1211_PublicationPreservesBareRepository / direct, deep, suffix: omit HEAD detection.
PC-2: same / mixed case: use case-sensitive names.
PC-3: same / HEAD only: demand full layout.
PC-4: C1211_DisposableLookalikeIsCleaned / missing HEAD, plain: protect every ignored directory.
PC-5: C1211_BareRecoveryIsRefused / direct, deep: omit strict bare refusal.
PC-6: C1211_LateBareRepositorySurvivesSetAsideResume / initial, resume: omit deletion guard.
PC-7: C1211_NestedTrackedHeadKeepsExistingAdmission / directory: classify a .git directory with HEAD as bare; file: omit .git cleanup preservation.
PC-8: C1211_BareInsideAcceptedNestedRepositoryIsPreserved / with and without tracked HEAD: descend inside accepted nested repository in proof; omit enclosing cleanup preservation.
PC-9: C1211_HeadOnlyOutputIsPreservedWithoutBareProofRefusal: apply cleanup HEAD-only heuristic to proof; require full layout in cleanup.
All PC-n/variants pending method-scoped SourceLanding Mutation after Review and
caller landing original Code task; ordinary green discharges none. C971 PC-1..11
remain pending as inherited lineage. Next: review after ordinary verification.

## Tests-first baseline

CP-1 at tests-only 14c63d4deda91358586a3b989f4a6cb5f282084c: 10 executed,
2 passed, 8 failed, 0 skipped. Fresh TRX inspected for every argument row.
Production files are identical to base. All five publication variants failed
B.BareHeadSurvivesPublication after B.PublicationConfirmed; the late deletion
case failed B.LateBareHeadSurvivesDelete; both recovery variants failed
B.BareRecoveryRefused (false expected, true observed). Both disposal controls
passed. This is assertion-red, not a fixture/build failure. Bound task owner;
build slot granted waited=0s, row granted waited=45s; clean source and verified
build provenance. Runtime report retains the unedited CHECKPOINT line and TRX.
CP-3 minimum corrected from 23 to 15 before its first execution: 23 refers to
15 existing proof plus 8 cleanup cases, not 23 in the proof class. No selection
or assertion changed. Final ordinary verification is pending at this commit.
