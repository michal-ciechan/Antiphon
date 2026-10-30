# CARD-1609 continue 2 — S2 content audit is green

S2 is green and pushed. VolumeRetirementContentTests is 15/15 on this tip. CP-1 is 2/2 and CP-14 is 1/1 (run 20261030-073343-38a2, commit 7dd442e396f779a7eab26f70d0e6be7d398ccd97). S3, S4, and S5 are not started.

## Commits

Branch feat/card-task-cba5ec74, fast-forward from 82f627dfdcb4c5dca5a8e9f7cf5b3dbdcc24b959.

| SHA | Subject |
|---|---|
| c422a31a1fed3b1d95ccafdf3201acd84fb7c8ed | test: record CARD-1609 C3-C14 content-retirement witnesses |
| e69af3177bc0f45e1a6cb628a5efa9ceea43a61b | fix: classify CARD-1609 retirement by seed bytes, fetch-head, and role metadata |
| 4b671ea4800a475ef2a2d10ae24cfc405d77bae0 | fix: splice the CARD-1609 content audit without bash replacement metacharacters |
| f6a626c9c32511ab63abab2f0f318e1c39a433ba | fix: classify an off-path nested repo instead of stopping on the parent status |
| 7dd442e396f779a7eab26f70d0e6be7d398ccd97 | test: expire the CARD-1609 C3 reflog before the published-main control |

Earlier on the same branch, still in force: 9c8021277 (S2 admission) and 82f627dfd (admission boundary).

## What S2 does

A temp checkout is disposable only when its raw commit object matches the card-1605 seed. gpgsig, encoding, and custom headers refuse as seed-bytes and the header payload is not echoed. FETCH_HEAD is classified on its own line. Gone-tracking and unadvertised-local refusals append the publish-then-preview recovery and do not push or prune. Host state and dind scans run on preview before Ready. An absent volume is success for that role. A lone sentinel file is allowed. An unknown dind top-level entry refuses.

The v2 functions live in scripts/c1008-content-audit.sh and are spliced into the quoted audit program in scripts/c590-remote.sh. scripts/c1008-volume-metadata.sh is sourced on the host. Missing or marker-tainted audit file keeps the v1 stubs. C1609_CONTENT_V2=1 is set only for the temp project. The fake docker allowlists accept that env.

## Checks

VolumeRetirementContentTests at f6a626c9c: 14 passed, 1 failed (C3 public control). C3 alone at 7dd442e39: 1 passed. The other fourteen methods were not re-run after that witness-only commit. Production code for them is unchanged since f6a626c9c.

Checkpoint run 20261030-073343-38a2, source 7dd442e396f779a7eab26f70d0e6be7d398ccd97, sourceState=clean, buildSource=verified, verdict GREEN, unlisted none.

- CP-1: executed=2 passed=2 failed=0 skipped=0. Build 433s, tests wall 54s. Slot waited 0s.
- CP-14: executed=1 passed=1 failed=0 skipped=0. Build reused. Tests wall 36s.
- CP-2: not run. Still the J1 apply-without-Ready case. It stays red until S3.

Importer warning, not a failure: CP-11's Build cell `bin-c1609-windows` duplicates `bin-c1609-final` from CP-3. Left as written in the plan.

## Deviations

- The audit body stays inside c1008_git_program. v2 is a literal prefix/suffix splice of scripts/c1008-content-audit.sh. `${var//pattern/replacement}` is not used: bash treats `&` as the matched text, so every `&&` in the audit file was rewritten as the v1 stub and the helper exited before classification.
- A second copy of the stub leaves the v1 program unchanged.
- Under C1609_CONTENT_V2, an untracked nested repository is not the parent's dirty content. It is classified on its own pass, so an unapproved path reports no-remote-unclassified. Any other untracked or modified file still refuses RecycleWorktreeDirty.
- C3's published-main control now expires the reflog, and a new assertion records the refusal before that expire. `git branch -D` deletes the branch reflog, but HEAD's reflog still names the private commit. That object is unpublished. The Ready assertion applies only after `git reflog expire --expire=now --all`.
- SeedTemplate stores adversarial commits with `git hash-object --literally -t commit`. Git 2.47.3 fsck rejects a gpgsig header between tree and author (`missingAuthor`). The raw object bytes and the exact seed oid are unchanged.
- Historical-image methods were not moved into C1/J9. That remains the S2 admission deviation.
- Planned S3-S5 files are still absent: scripts/c1008-retirement.sh, scripts/retire-runner-volumes.ps1, VolumeRetirementHostProofTests.cs, VolumeRetirementWrapperTests.cs, VolumeRetirementDockerTests.cs.

## Image and SHA inventory

Publication is not decided by an image id or a source SHA.

- Dry-run with no matching cleanup receipt sets C994_IMAGE from `docker image inspect` of `antiphon-server2/session-testing:${SHA:0:12}` and sets C1609_HISTORICAL_UNPINNED=1. That id is the read-only audit helper. A conflicting historical image prints C994_IMAGE_UNPINNED and is not a content refusal.
- When a receipt names a sha256 image, that id is the helper passed to the audit container. The check is the sha256 shape. It is not the publication proof.
- scripts/c1008-volume-metadata.sh treats a dind directory named `image` as a recognized Docker store directory. That is a directory name, not an image identity.
- scripts/c1008-content-audit.sh does not read an image id or a source SHA.

## Definition files and land checks

No csproj, props, targets, or sln file changed.

scripts/check-evidence-diff.ps1 base 82f627dfdcb4c5dca5a8e9f7cf5b3dbdcc24b959 head 7dd442e396f779a7eab26f70d0e6be7d398ccd97: commits=5 entries=0 violations=0 exit 0.

git fetch origin master, then git merge-tree --write-tree origin/master HEAD: exit 0, tree cbcd2050222a0d0f7dbead1d7492aaff542980d3. origin/master is 98fc3ceb6440a17b68cafe3ecc21c76d190b18ca. Not merged.

scripts/audit-land-verification-graph.ps1 -Check (not -Update): approved, digest 20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6, count=66. The evaluator was built to tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/ and both bin-land-audit directories were removed afterward. Canary was not left behind.

Linux only. No desktop files were read. No native tests were run.

## Owed

From this worktree, TEMP=/tmp, do not wrap the checkpoint tool in build-slot. UseAppHost=false is already applied by the tool on Linux.

After S3, CP-2 should go green for the stated reason (apply without a Ready preview refuses; the existing apply scenarios gain a Ready preview first):

`dotnet run --project tools/Antiphon.Checkpoints --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --rows CP-2 --max-wait 20m`

After S3-S5, the remaining Linux rows. Do not launch the whole manifest as one run over 30 minutes:

`dotnet run --project tools/Antiphon.Checkpoints --property:UseAppHost=false -- run --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-15,CP-16 --max-wait 50m`

Windows, not run here: CP-11, CP-12, CP-13.

## Remaining slices

- S3: preview-required gate together with the plan's disposition of every affected existing test (RetiredTempContainerScriptTests, RemoteScriptContractTests deploy-temp, RetiredTempContainerHostTests, DockerStackDocumentationTests). Each replaced test keeps its protective intent. Existing apply scenarios gain a Ready preview first. None removed, skipped, or relaxed. Also the receipt coordinator and the bounded census retry that stops before the next delete.
- S4: wrapper preflight before /drain/clear in deploy-server2.ps1, and the retained-volume gates.
- S5: docs/docker-stack.md, Windows skip-roster rows and C1210, owed rows listed.
