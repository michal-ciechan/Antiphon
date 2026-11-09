# CARD-1453 Review round 1 (Final, Full): CLEAN

Subject: Code eab93359 (Grok author), origin/feat/card-task-eab93359 tip 1ccdcd4d36cf412154653bd4a68c251373487534.
Range 0cb7bfa7fcbd98e1fb537aa85dbf669395fe51a7..1ccdcd4d36cf412154653bd4a68c251373487534 (red 52a7054b537b4f07135d6e2cacc476b50aa4bb0d).
Master d861149d0738bef6019e1a437efc69d245512ab9. Reviewer: Claude (server2 runner, git 2.47.3).

No plan exists for CARD-1453 (no `### Checkpoints` table); the brief's named selection is the ordinary scope.

## Evidence (all at the reviewed SHA, each through build-slot)

| Row | Result |
|---|---|
| Build tests/Antiphon.Tests (UseAppHost=false, native output) | exit 0, 0 errors, lease 6d15e293, 231 s |
| Touched: (LandingSourceOptionalLockTests*)\|(ServerGitProcessTests*)\|(AgentTaskLandPublicationTests*) | 88 total: 87 passed, 0 failed, 1 skipped (C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked, False), Windows sharing denial, unchanged); classes 1 + 23 + 64; lease 8d36cc37 |
| Guards: TestClassificationPolicy*\|TestClassificationGuard*\|SlowTestRegistryMerge*\|SlowTestTripwire*\|NativeLinuxPolicyWitnessTests* | 52/52 passed (41 + 1 + 5 + 2 + 3); lease 4be71c98 |
| Red: tip test dll + base-equivalent server dll from 52a7054b5 | 1/1 failed at `stripped.OptionalLocks` ("caller setup cannot re-enable optional locks") |
| Mutant: tip with both `ServerGitProcess.SuppressOptionalLocks(start)` calls deleted | 1/1 failed at `production.OptionalLocks`; killed |
| check-evidence-diff 0cb7bfa7f..1ccdcd4d3 | commits=2 entries=0 violations=0, exit 0 |
| merge-tree origin/master HEAD | clean, tree 5f794a33972ae74d3fffa2de30f8a6a2219c8b16 |
| audit-land-verification-graph.ps1 -Check on the scratch merge 2fef742b7 (evaluator built Release, leased) | exit 0, `approved` (pass2) |
| Policy json / migrations | none changed; no migration, so no downgrade selector rows (count 0) |

## Brief checks

1. Status equivalence (git 2.47.3, scratch repo, `status --porcelain=v1 -z --untracked-files=no`, GIT_OPTIONAL_LOCKS 0 vs 1):
   stat-dirty-only file (mtime moved, same content) gives empty output both ways (git still content-compares
   stat-dirty entries; with 0 it only skips writing the refreshed index, and the index mtime did not change);
   content change, mode change and same-size change with restored mtime all list ` M` identically both ways.
   No false refusal, no false clean. The witness itself asserts empty output on a stat-dirty tree with the
   variable at 0. The Git floor is far above 2.15 (the variable's introduction); LandingGit already needs >= 2.43.
2. Launches: LandingGit has exactly two process starts (ExecuteProcessAsync, and the stdin child used by
   update-ref --stdin); both now call SuppressOptionalLocks after ConfigureProcess, ApplyIdentity,
   DisableAutoMaintenance and the owned marker. The libgit2 probe start is never launched. GIT_OPTIONAL_LOCKS=0
   affects only opportunistic locks, so mandatory index/ref writes (add/commit/checkout/update-ref/push) are
   unaffected, and they already ran with it at base. CARD-1787 `ServerGitIdentity.ScrubInherited` matches only
   GIT_AUTHOR_*/GIT_COMMITTER_*/GIT_CONFIG_KEY_*/VALUE_*/COUNT/PARAMETERS, and runs before the suppression,
   so it cannot remove it.
3. Witness: red at base, mutant killed, control proves a bare status creates index.lock and rewrites the index
   on this host; the guarded and in-process paths create neither.
4. No policy, migration or input-wait change.

## Advisories (do not hold the land)

- A-1: Production behaviour is unchanged from base. Base already set GIT_OPTIONAL_LOCKS=0 on both launches
  (CARD-1463), and nothing in production removed it (no server ConfigureProcess override; the scrub does not
  match it; OwnedChildMarker sets ANTIPHON_REPOSITORY_CHILD). The change hardens against a subclass
  ConfigureProcess. The card's "runs without --no-optional-locks" premise was already false at base.
  rc/2026-11-14 (7bee43614) carries the early assignment at LandingGit.cs:315/1289, so the rc port is hardening, not a hazard fix.
- A-2: The stdin launch's SuppressOptionalLocks has no witness. Deleting only that call survives. It serves
  only mutating update-ref --stdin, where optional locks do not apply. This stays pending as a PC for Mutation.
- A-3: No witness runs with an identity file configured (ApplyIdentity returns early in the fixture). The
  ordering and the scrub predicate make it safe by construction.

PCs: all pending for method-scoped SourceLanding Mutation.
