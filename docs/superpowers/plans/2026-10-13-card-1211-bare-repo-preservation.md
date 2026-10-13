# CARD-1211: preserve ignored bare repositories

Code/landing owner: 578ae84e-829d-4da6-b4bc-2354e3bb876c.
Base: 6e58767debf33d6a09c4e88050095f1cfdf76332 (CARD-0971 landed).
Scope: WorktreeNestedRepositories and real scratch-repository tests only.

## Decision

Extend the filesystem-only preservation census used by both guarded cleanup
readings and by WorktreeNoFollowDelete (initial and recorded set-aside resume).
A bare repository has HEAD, objects/ and refs/ directly in its root; it has no
.git entry. Recognize entry names without case sensitivity. Do not parse config
or spawn private Git in cleanup. A HEAD entry alone is ambiguous administrative
evidence and is preserved conservatively, including links or malformed entries.
Thus every full bare layout is protected, even if objects/refs have disappeared.
This is a preservation heuristic, not proof that an arbitrary HEAD is a valid
repository: ordinary output without .git or HEAD still cleans; output named HEAD
may intentionally remain as residue. A .git suffix alone grants no protection.
Objects/refs without HEAD are explicitly disposable (V-4); unknown/access errors
continue to propagate to fail-closed cleanup. Tests V-1..6 name these boundaries.

Recovery proof remains stricter: ignored bare repositories must be refused even
if Git enumerates their files rather than returning a directory record. Add a
filesystem census refusal before accepting recovery (V-7). Ordinary reviewed
adoption supplies publication acceptance for V-1..6; do not broaden recovery.
Existing .git proof admission and pointer refusal remain unchanged (R-1..5).

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
| R-1 | full LandRecoveryIgnoredNestedRepositoryTests | unchanged 23-shape proof |
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
| CP-2 | S3 | `tests/Antiphon.Tests -> bin-c1211-final/` | bare-final | `/*/*/AgentTaskLandBareRepositoryCleanupTests/*` | V-1..8 | all green | 10 | 4 | true |
| CP-3 | S3 | CP-2 | nested-proof | `/*/*/LandRecoveryIgnoredNestedRepositoryTests/*` | R-1 / C971 CP-2 | all green | 23 | 2 | true |
| CP-4 | S3 | CP-2 | checkout-proof | `/*/*/LandRecoveryCheckoutTests/*` | R-2 / C971 CP-3 | all green | 12 | 2 | true |
| CP-5 | S3 | CP-2 | obstructions | `/*/*/LandRecoveryIgnoredObstructionTests/*` | R-3 / C971 CP-4 | 5 passed; 2 Windows skips | 5 | 1 | true |
| CP-6 | S3 | CP-2 | registration | `/*/*/LandingRegistrationFreshnessTests/*` | R-4 / C971 CP-5 | all green | 4 | 1 | true |
| CP-7 | S3 | CP-2 | half-reset | `/*/*/AgentTaskLandHalfResetTests/*` | R-5 / C971 CP-6 | all green | 20 | 4 | true |
| CP-8 | S3 | CP-2 | nested-cleanup | `/*/*/AgentTaskLandNestedRepositoryCleanupTests/*` | R-6 / C971 CP-11 | all 8 green | 8 | 2 | true |
| CP-9 | S3 | CP-2 | guarded-cleanup | `/*/*/WorktreeGuardedCleanupTests/*` | R-7 / C971 CP-12 | green; Windows skips allowed | 1 | 2 | true |
| CP-10 | S3 | CP-2 | retirement | `/*/*/TaskWorktreeRetirementTests/*` | R-8 / C971 CP-13 | all green | 1 | 2 | true |
| CP-11 | S3 | CP-2 | retirement-races | `/*/*/WorktreeRetirementRaceTests/*` | R-8 / C971 CP-14 | all green | 1 | 2 | true |
| CP-12 | S3 | CP-2 | removal-policy | `/*/*/LandingRemovalPolicyControlTests/*` | R-9 / C971 CP-15 | green; Windows skips allowed | 1 | 2 | true |
| CP-13 | S3 | CP-2 | completed-removal | `/*/*/CompletedCardWorktreeRemovalTests/*` | R-10 / C971 CP-16 | green; Windows skips allowed | 1 | 2 | true |

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
All PC-n/variants pending method-scoped SourceLanding Mutation after Review and
caller landing original Code task; ordinary green discharges none. C971 PC-1..11
remain pending as inherited lineage. Next: review after ordinary verification.
