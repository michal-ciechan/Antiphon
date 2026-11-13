# Final review: gate-9 selector-fixture retirement exception

Round 1, Final. Verdict: clean. No blocker. Positive controls stay pending.

Subject task `49ae044d-4cc7-4cc2-81f0-d7ae8f95eb2d` (Debug). Reviewed source `0976ca0ca27473bd8d03db76f59199966258a8cf`. Range `90ae19f65bc4aff58a8f10162614c94e14d42bde..0976ca0ca27473bd8d03db76f59199966258a8cf`. CARD-1514's stored description is the slow-test investigation. This SHA is only the stopped rollout's gate-9 selector exception. Landing it does not finish that slow-test scope.

## What lands

`scripts/c590-remote.sh` adds `selector_fixture`, used only when `C1609_CONTENT_V2=1`. A nested repository skips its own publication census only when its git dir is a real non-symlink `.git` directory, `rev-parse --show-toplevel` equals that path, the parent checkout is under the audit root, the path is `"$parent"/.antiphon/selector-evidence/*`, `git check-ignore -q` accepts both `.antiphon/selector-evidence/` and the relative fixture path, the parent has `remote.origin.url`, and the nested repo has no `^remote\.` config key. The parent must later appear in `audited_checkouts` or the audit refuses `selector-parent`. Shallow, promisor, lock, and git-dir confine checks still run before the exception. Inventory uses the same skip and exits before publication. It does not delete.

A no-remote repository with unique commits under that ignored scratch is disposable. That is the documented contract, and `.gitignore` ignores all of `.antiphon/`. Agent worktrees are not that path. `info/exclude` and a local excludes file cannot move the path constraint. `GIT_CONFIG_GLOBAL` and `GIT_CONFIG_SYSTEM` are `/dev/null` inside the audit.

Staged bundle hashes at the reviewed tip: `c590-remote.sh` `27adecf3ccdda8ff6ec85a8e7f13df07a7db0287a9fb711769c3489b49ad428b`; `c1008-retirement.sh` `d3d30788082a248d32ff490117f96faad10544b9214425cdf87a0b29288944ed`; `c1008-content-audit.sh` `01b074c8ad20aa87e6867f89155f402692228d3c25f3b0159d80f2f8e830b6bd`; `c1008-volume-metadata.sh` `9ffc025c8f9df4ba656e4a1cc3b283c793595d6d25598e163d5044390a7f837b`. The three helpers match `ab7af12c699039270af7e22a5d7fd794035bc325`. Wrappers keep that deploy SHA as rollout identity. They do not compare retirement tooling HEAD to it, and they do not reference the selector scripts. This branch changes only `c590-remote.sh` inside the staged bundle. The proof window, apply token, and expiry are unchanged. V2 publication does not use network `ls-remote` or stale tracking refs.

## Adversarial audit

A private harness of the reviewed audit body, with the content-audit splice and `/work` replaced by a scratch root, refused after classification. Disposable: a no-remote repo with unique commits under ignored selector scratch, including a path with a space, an inactive `includeIf`, `url.insteadOf`, and `core.sshCommand` without a remote section, and a parent path containing `*`. Not disposable: origin, a named backup remote, pushurl-only, an active `includeIf` remote, a path outside selector-evidence, an `info/exclude` worktree path, a gitdir file, a symlink `.git`, a fixture under a seed parent, and a sibling path a glob could have swallowed. `url.insteadOf` and `core.sshCommand` without a remote section match the documented contract. No deletion hole.

## Mutants

- Ignoring the no-remote check is killed by the `remote` and `named-remote` vectors.
- Ignoring the ignored-scratch check is killed by `not-ignored` and `outside`.
- Accepting a seed parent as publication proof is killed by `Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof`.
- Swallowing an `ls-remote` error does not apply: the exception never calls `ls-remote`, and a git failure returns to the normal path. `C1008_Recycle_refuses_uninspectable_git` passed inside CP-3.
- Accepting a fixture whose own `.git` is a gitdir file or symlink is refused by the script and by the harness, and no C# test fails if those two guards are removed. The `linked` vector rewrites the parent worktree pointers. The nested fixture is still `git init`. Advisory CARD-1898 (`c89deb89-ec09-4764-badf-339bb0348462`) records that gap and does not hold the land.

## Receipts at 0976ca0c

All of these are `sourceState=clean` `buildSource=verified`. CP-2 is split, as the receipt phase is past 30 minutes.

- CP-1 `/*/*/VolumeRetirementContentTests/*` executed=41 passed=41 failed=0. `trx=.antiphon/checkpoints/CP-1-20261113-142541-6eff/run.trx` slot=granted waited=0s.
- CP-2-fast apply/host-proof/wrapper executed=31 passed=31 failed=0. `trx=.antiphon/checkpoints/CP-2-fast-20261113-154957-6f0e/run.trx` waited=0s.
- CP-2-receipts `/*/*/VolumeRetirementReceiptTests/*` executed=11 passed=11 failed=0. `trx=.antiphon/checkpoints/CP-2-receipts-20261113-161333-429c/run.trx` waited=90s. 31+11=42.
- CP-3 executed=48 passed=48 failed=0. `trx=.antiphon/checkpoints/CP-3-20261113-145639-43db/run.trx` waited=45s.
- CP-4 executed=163 passed=162 failed=1. The failure was `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged`: the test always passes `-NoBuild`, and the checkpoint output does not produce `scripts/regenerate-derived-impact-index/bin/Debug/net9.0/Antiphon.DerivedImpactIndex.dll`. `committed_index_matches_the_builder` passed in that same run. After a native driver build (lease `33457532-c29b-4293-bcbe-efef375e6a55`, no alternate output), CP-4-regen of that one method executed=1 passed=1. `trx=.antiphon/checkpoints/CP-4-regen-20261113-153627-3792/run.trx`.

Evidence diff: commits=10 entries=0 violations=0. `git status --ignored -- .antiphon` on the reviewed worktree shows only checkpoints and inbox. No `.git` and no `selector-evidence` under that `.antiphon/`. No live retire-temp, restart, container, or volume operation.

## Land resolution on current master

`origin/master` at review time is `e4c45f4b4bb58870bb8684087a6d218e1b988cbe`. `git merge-tree --write-tree origin/master 0976ca0ca27473bd8d03db76f59199966258a8cf` has one content conflict, only `tests/Antiphon.Tests/Scripts/ReleaseImpactEnvelopeTests.cs`. Keep master's `CreateAsync`, `DisposeAsync`, temp allocation, and copied-algorithm tests. The location witness is:

```csharp
await using var fixture = await Fixture.CreateAsync();
```

plus the branch's cross-drive relative-path assertion. Do not keep the branch's older `Path.GetTempPath` `CreateAsync`, and do not keep the merge-tree auto-merge of `tests/derived-impact-index.json`. Run `scripts/regenerate-derived-impact-index.ps1` (driver first) on the resolved tree. On `e4c45f4b4` the driver drops `C1677RetentionEngineTests` from the `.gitignore` cap's kept prefix when `VolumeRetirementContentTests` is inserted. `C1677RetentionEngineTests` stays on its own file edge.

That resolution is committed only on the unpushed scratch `/tmp/review-01e2734e-master2` at `61485745e0fc08e251fa3655c8745a1f1e370417`. `c590-remote.sh` there matches the reviewed tip. Checkpoint `scratch-merge2` at that SHA: filter `/*/*/*/(committed_index_matches_the_builder|Fixture_repositories_live_under_system_temp_outside_the_checkout|Selector_fixture*)` executed=11 passed=11 failed=0 skipped=0, `sourceState=clean` `buildSource=verified`, lease `0ab23930-5303-4718-a791-da64df3aec9d` waited=0s, `trx=/tmp/review-01e2734e-master2/.antiphon/checkpoints/scratch-merge2-20261113-170321-6bce/run.trx`. The 11 are the index match, the location witness, the eight selector vectors, and the seed-parent test. After that run, scratch `.antiphon/` has only checkpoint receipts: no selector-evidence git directory. CP-1 through CP-4 were not replayed in full on the scratch. Their script and non-conflicted tests match the receipted SHA above.

PCs remain pending. Mutation checks stayed waived for this round (operator 2026-11-12).
