# Review 38d3c07b (round 1 of 3): CARD-1693 r3 bash-edit-diff snapshot classifier

Verdict: CLEAN. No numbered defect. Three advisories follow (A-1 to A-3).

- Subject: feat/card-task-78e606d5, Code owner 78e606d5-d9fb-4927-b175-c11bf0205b37.
- Tested code: 77950d02eb7bb16ccaaa44221fd0a1a265f042e2 (red witness 113198f1c).
- Branch tip: 1c263f03f8d1ec55994ed20bb5f70e879720d226. It adds only .antiphon/task-78e606d5.md.
- Base: master 2b342761f.

## Runs (server2, Linux, one build-slot lease per build or run, UseAppHost=false, native output)

| Run | Result |
|---|---|
| Build of tests/Antiphon.Tests at 77950d02e | 0 errors, 2m34s |
| `(VolumeRetirementContentTests*)\|(HostJqFixtureClosureTests*)/*`: the whole class (30 methods, every C1609 content audit method included) plus HostJq (2) | TRX total 32, passed 32, failed 0, skipped 0, 9m26s |
| `RollingVolumeRecycleScriptTests*/(C1008_Host_proof_*)\|(C1693*)\|(C1087*)` | total 8, passed 8, failed 0, skipped 0, 5m36s |
| Red witness: the base 2b342761f script with the tip test | 1/1 failed, reason=evidence-name |
| Mutant M1: drop the `$` anchor | 1/1 failed (the `nested` variant is accepted). Restored. |
| Mutant M3: delete the exact commit-bytes compare | 1/1 failed (the `foreign-commit` variant is accepted). Restored. |
| check-evidence-diff 2b342761f..1c263f03f | commits=3, violations=0 |
| merge-tree onto origin/master 0d3643ef2 | clean, for both 77950d02e and 1c263f03f |

## Scratch fixtures

These were run directly against `c1609_tool_snapshot`. Fixtures are under scratchpad only and nothing was committed.

Accepted, as intended:
- the exact shape
- a byte copy of a live snapshot dir. All 371 live dirs on this runner share one layout: alternates `/work/repos/antiphon/.git/objects` plus 1 to 50 local loose objects.

Refused, as intended:
- extra branch, stash, second real commit, parent, foreign message, extra message body, gpgsig header, author time changed by 1 s
- logs, packed-refs, hooks, remote
- detached HEAD, symlinked HEAD, refs/main as a symref
- tag ref, refs/replace, info/grafts, shallow, commondir, an extra top-level file
- `Include.path`, `includeIf` in config.global, core.worktree
- work role
- claude-1654x, claude-0
- uppercase hex in both C and C.UTF-8 locales, hex length 15 and 17
- trailing and embedded newline in the name
- a `..` path, a nested path
- a symlinked uid dir, bash-edit-diff dir, leaf dir, refs or objects

Accepted, contrary to the code comment "objects borrowed through alternates": see A-1.

## Inventory digest binding

`c1609_inventory_digest` (scripts/c1008-retirement.sh:108) sorts every `disposable-fixture` line into the preview/apply digest. Each line carries the path and the main commit OID, and the commit pins the tree. So the token is invalidated if a snapshot dir appears, vanishes, or changes its commit or tree. Extra dangling objects or index changes do not move the digest. That matches A-1.

## Advisories

A-1 (shape narrower in prose than in code; not a loss vector). `objects/` is never inspected. The classifier accepts:
- alternates pointing at `/home/attacker/x/.git/objects`
- no alternates file
- an extra pack dir
- a dangling loose commit with real-work metadata

None of these makes a commit reachable from a ref. Unreachable objects are unaudited in every other repo on both volumes, so the bar is not lowered. To pin the observed shape: require objects/ to hold only `info/alternates` (one absolute line, `*/.git/objects`) plus `<2hex>/<38hex>` loose objects, no pack/, and make every local loose object a tree or blob other than the main commit.

A-2 (test gap). No variant changes the hex length. The mutant `[0-9a-f]{16}` -> `[0-9a-f]+` survives the test by inspection. Production refuses lengths 15 and 17 (fixtures above). Suggested fix: a hex15 or hex17 variant.

A-3 (operator judgement, not blocking). The live dirs' local loose objects are trees and blobs that are not in the main repo. They are past copies of worktree bytes from before each Bash call, including gitignored .antiphon files. I agree with Debug that they are disposable:
- each is an intermediate state of a live checkout, and that checkout is audited where it lives
- plain /tmp files are already unaudited
- they are already listed as `disposable-fixture` rows and bound into the digest

Turning them into C1609_FINDING rows would add about 370 noise rows per preview, with no new protection. Keep as is.

Operational note: every Bash call by any Claude session on the temp runner creates a new snapshot dir. Such a call between preview and apply changes the digest, and apply then refuses. Keep the temp runner idle between the two.
