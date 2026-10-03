# CARD-1015 Code admission: blocked by an open Code footprint

Original Code task and landing owner: `bc463e02-3fd6-4c05-9883-0ef2f876aaf2`.
Branch: `feat/card-task-bc463e02`. Worktree: `/work/worktrees/task-bc463e02`.
Immutable dispatch base B: `90a936e39f899655e48dddb9d2ed8287222d92e8`.
Plan: `docs/superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md`.
Round: Final. Admission inspected 2026-10-03 approximately 20:01-20:08 UTC.

Implementation did not start. The commissioning brief explicitly orders STOP when
the planned file list overlaps the named open Code owners, including CARD-1011's
idle owner. The pipeline and individual status/detail reads identify:

| Owner | Observed state | Overlap with CARD-1015 |
|---|---|---|
| CARD-1011 `698c0e44-127d-4a7a-9584-7031570573e5` | Blocked; session Running, idle; waiting for Windows qualification | `docs/orchestration-loop.md`; `tests/Antiphon.Tests/Application/InstructionBundleTests.cs` |
| CARD-1008 `52bffc69-6b4a-441c-b658-6ce5b466741a` | Dispatched; session Running, working | Its current frozen implementation list has no intersection. Its cumulative branch contains earlier inherited edits to `docs/orchestration-loop.md` and `docs/testing-and-build.md`; the delta since its recorded continuation base touches neither. |
| CARD-0959 `818582a5-dcea-44e1-9884-e78807cd514a` | Failed; session ended 19:50:40 UTC | No active owner collision asserted. |

CARD-1011's two overlapping files are actual committed changes: the diff from its
recorded base `a3f5951d2700f26f78414c036563eef93cf4f894` to its worktree HEAD
contains 79 added lines in the orchestration owner and 103 in InstructionBundleTests.
No overlap exception was supplied for CARD-1015. Caller must serialize or explicitly
resolve that ownership before resuming implementation. No messages were sent to
other agents, no land was requested and no deployment occurred.

## Current-base inventory

An independent read-only Node calculation consumed native
`git ls-tree -r -z -l --full-tree <full SHA>` metadata. It classified the exact root
component case-insensitively; rejected checkpoint-containing descendant directory
components, nonregular modes, final extensions other than `.md`, and blobs above
1,048,576 bytes. Directory classification excludes the leaf. No evidence payload,
credential or private note was opened; all bytes are committed Git metadata sizes.
Digests below are SHA-256 of Git-order UTF-8 path bytes each followed by NUL.

| Partition at B | Count | Bytes | Path digest |
|---|---:|---:|---|
| All root evidence | 312 | 38,101,068 | `ba48a18494f1c24ff59df50ba5171e967a755be4572865df7c6523e4772bbec0` |
| D(B), authorized deletion set | 289 | 37,541,892 | `4e0a2ec36b9817c7cba9a2d5e05e85ecaa3f5a23b56daeb7469b3fd55b7684ed` |
| Permitted root evidence | 23 | 559,176 | `2e663b26148771f50e1cb73c6e4821b9e3df52b14190504a8cd199588c868ace` |
| Original anchor | 108 | 17,775,541 | `356edf4a223e53669d4137631e40c0f5f7d19127c2568fdd0608fa4364e5ac9c` |
| Original rejected partition | 88 | 17,414,103 | `26abc1b90cf1dc40dec8dfb38bd8f5c75f8c555f22f8042c49268c78f259e95f` |
| Original permitted partition | 20 | 361,438 | `320fcb2d7ffe6607de6c46d3bd477522ffda0ab7355bb10be252e7eac8be05f6` |

All original anchor paths retain mode/OID/classification at B. Classification
signature: `f5082847ba6f5e2b1db50b407ff80b7f8cb36575ef9fe8f90c825c7b4ca09c2b`.
Every count/byte delta against inspected `7af83b0c3270006cd98a25a69531f24e37240a4e`
is zero. Families classify exactly as the accepted plan. No deletion was staged.
Ignored local inventory, not durable raw-artifact custody:
`/work/worktrees/task-bc463e02/.antiphon/c1015-bc463e02-admission/inventory.json`.

The test/shared/census source diff from inspected `7af83b0c` to B is empty.
The independent census remains literal `selected = 377` at
`scripts/lib/checkpoint-usage.ps1:114`. Thus the frozen source roster is unchanged:
4,018 existing Unit selections + eight planned Unit additions - six explicit
Windows exclusions = 4,020 intended Linux executions; 18 history + six deletion
+ two smoke results yield 4,046 ordinary executions. These are source expectations,
not executed test counts; no new compiled recount was claimed.

## Platform and verification disposition

Read `GET /api/runner-defaults` and `GET /api/session-runners` through the documented
environment-resolved API and task-token header. Defaults revision 2 has global
preference server2 and no kind overrides. Desktop and Linux runner are available
and eligible; the temp runner is unavailable/draining. No routing or platform pin
was changed. Git, pwsh, dotnet, bash and Node resolve; jq does not resolve on this
host. The brief permits disclosed inherited jq-gated skips; none was run here.

The real importer admission was **not run**, because the preceding collision gate
orders STOP. No build/test driver launched, no slot was requested, and no alternate
output was created: `slot=not-requested`, `waited=0s`. There are no CP receipt lines
or TRX files for this task. Authorized bootstrap on resumption:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1015-importer -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1015-tool/ --property:UseAppHost=false
dotnet tools/Antiphon.Checkpoints/bin-c1015-tool/net9.0/Antiphon.Checkpoints.dll import --plan docs/superpowers/plans/2026-10-03-card-1015-evidence-git-policy-plan.md
```

Importer must accept the seven final rows before implementation. Verify the actual
tool DLL target path from its project before invoking it. S1/S2/S2b/S3 remain
unimplemented; red-first tests, InventoryOnly agreement, deletion staging proof,
coverage lint and CP-1 through CP-7 all remain not run. Every ordinary ID
V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10, V-11, V-12, V-13,
V-14, V-15, V-16, V-17, V-18, V-19, V-20, V-21, V-22, V-23, V-24, V-25,
R-1, R-2, R-3, R-4, R-5, R-6 and R-7 is **not run**. No Final coverage is
claimed. PC-1 through PC-101, including every vector/variant in the plan, remain
pending for method-scoped post-land SourceLanding Mutation.

No full-assembly run is authorized by the frozen scope. The intended invariants
are Git-object history/deletion compliance, instruction/CI enforcement and source
cleanliness. Scope is the entire eligible Unit lane, full EvidenceDiffGuardTests
and EvidenceDeletionGuardTests, changed workflow/bundle Unit tests and two exact
smokes. Ordinary estimated floor is 29 minutes plus three minutes of preparation;
101-PC Mutation floor is 969.25 minutes, separately commissioned. No unbounded
runtime delivery, landing, lease or persistence class is added by this work.

Restart now: none. After completed implementation, Review and land, the plan
requires server activation; runner restart: none. The caller lands original Code
owner `bc463e02-3fd6-4c05-9883-0ef2f876aaf2`; this task never lands or deploys.

--- next stage ---
next: code
handoff: Resolve the CARD-1011 overlap in docs/orchestration-loop.md and InstructionBundleTests.cs, then resume original Code owner bc463e02. Importer, S1/S2/S2b/S3, red-first proofs and all seven Final checkpoints remain unstarted; all 101 PCs stay pending for post-land Mutation.
artifact: docs/investigations/2026-10-03-card-1015-code-admission-bc463e02.md
