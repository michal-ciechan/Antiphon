# rc bundle C dry-run onto rc/2026-10-20 (2026-10-22)

Task 2a42be76 (Docs/dry-run). This recipe was executed in a detached scratch worktree.
Only this document is published on the assigned task branch. No rc ref or tag was written.
Method follows [bundle A](2026-10-22-rc-bundle-dry-run.md) and
[bundle B](2026-10-22-rc-bundle-b-dry-run.md).

## Outcome and scope decision

The ten applicable landed card sets assemble and compile on rc. **The complete requested
set is blocked by CARD-1495:** its tests require CARD-0167 provider endpoints, which rc does
not contain. The recipe below excludes that card explicitly. The caller must either accept
this scoped bundle or commission the prerequisite CARD-0167 port before adding CARD-1495.
It does not silently introduce that feature and its migrations.

- Base: `0714a9a89e4bff65aae7e0b4b94098694aeb931c` (bundles A and B).
- Source selection snapshot: `79341d09664267c7ccce40175815022b7e7c9c61` on origin/master.
  Every one of the 49 selected full SHAs is an ancestor of that master snapshot.
- Scratch tip: `f5c4f699fe4c91c2720f09e9cb864ed2af7a8d69`.
- **Resulting tree: `93c9459395115248f5793e26bc2b0cfe96d275e4`.** Commit times make the
  tip nondeterministic; a reproduction must match this tree.
- 60 scratch commits: 49 `cherry-pick -x` commits, nine ordinary commits undoing unconsumed
  plan changes, and two generated manifest updates. No history rewrite is used.
- 48 picks were clean; one conflict was confined to an omitted plan. No production or test
  hunk was dropped or hand-resolved. The whole requested CARD-1495 patch was refused separately.
- Net change: 57 files, +3593/-213. No EF migration, `src/` or `client/` change. All pre-existing
  rc plan files remain byte-identical; no new plan file is retained.

## Per-card disposition

The script below is the authoritative ordered list, with full master SHAs. This table explains
why each set is included or withheld; short SHAs here are navigation aids only.

| Card | Picks | Result and omissions |
|---|---:|---|
| 1499 | 3 | `3d9214743`, `153d9a068`, `3c9db6187`; clean. Drop the release-unresolved plan. `ba311ed04` is only report/plan evidence and is omitted. |
| 1494 | 5 | `e6032f85b` through `a01b38a2c` as explicitly listed below, including both early fixture repairs and the busy-produce repair. Clean; drop its plan. Omit report-only `de7b54cd2` and `713801a8f`. |
| 1495 | 0 | **Blocked.** Landed source is `df45aa1d7aa68297945bec894877dbf644e028af`. Its isolated pick conflicts in `LaunchEnvLayersIntegrationTests.cs`; the pick was aborted. See dependency evidence below. |
| 1493 S1 + repair | 4 | `8866e4467`, `f2cfe512d`, `951e5e210`, `4800c50f3`; clean. Drop both S1 and repair plans; omit the three report/plan-only commits in the seven-commit landed chain. The importer and its repair are both retained. |
| 1448 | 2 | `0498c748a`, `a23ca2869`; clean. Drop its plan; omit report/plan-only `e4ef4c621`, `222ceeb00`. This remains the prerequisite for pending 1502. |
| 1486 S1/S2/S3 | 8 | S1 then S2 then S3, all clean. S3 uses landed copies `12a2141e6`, `0fc7a4c2b`, `2316cd6fa` of the brief's `73ee2fb32`, `60d484226`, `ad6fa1db3`. Report-only commits and the independent plan are omitted. |
| 1496 | 3 | `ccac6053e`, `0c4386050`, `9bcbb9cbb`; clean. Drop its plan. The fourth commit, `00651b1df`, is a report. Required CARD-1490 is already in rc bundle A. |
| 1506 | 3 | `87262adf4`, `c15961b94`, `4db0d927a`. The fix has one modify/delete conflict on its already-dropped plan; runtime hunks merge cleanly. Skip empty provenance commit `ba1c88522`, plan/mutation-candidate-only `aaa93dadf`, master manifest `516151924`, and report `c5da1d238`. Generate rc's own manifest twice as described below. |
| 1404 | 3 | Landed copies `3d8650eee`, `7370f414b`, `ffa98a8f7` of `0d7748bb6`, `9fb121e94`, `0db4ca0f5`; clean. Keep measured costs, fixed-listing expectations, the investigation and owner-document changes. |
| 0266 | 6 | All six code/test commits, including the expression-compatible notification assertion and live-snapshot buffer repair. Clean. Omit its standalone plan and four report commits ending in source report `87066db56` (master `5639116f8`). |
| 1483 S1 + repairs | 12 | All S1 tool/test and allowlist/pinning repairs through `b20e12e2f`; clean. Drop the two repair plans, omit report/plan-only commits, and replace manifest-only `d2bf22dc0` with an audit-generated final manifest. S2 is still pending. |

### Brief SHA corrections

`e6ff606f` and `fe146ce4` do not resolve in the fetched repository. The landed CARD-1494 chain
was identified from its master commits and `.antiphon/task-5db5df66.md`; CARD-1495 from
`.antiphon/task-a3ba50d2.md` and `df45aa1d7`. The brief's `cb7af8ccd` is a CARD-1480 report,
not the first CARD-1493 commit. The actual original seven-commit CARD-1493 chain is
`337d9ecf7925ef5fe8c8919ac86ba42b5a97981d` through
`749a26c470705ab2a8042e3945eb3bc7d4f3e56f`, landed as
`8866e446762f3fcb7309ab5e25d3824406a637d7` through
`b87492137c2b4ed30709d17b35913654cb0bdb42`. No broad range beginning at `cb7af8ccd` was picked.

### CARD-1495 dependency evidence

The source report explicitly says master-only and records the same rc refusal. Direct inspection
confirms that rc lacks `ProviderEndpoint`, `ProviderEndpointLayers`,
`Project.ProviderEndpointsJson`, and `Agent.PoolProviderProjectionJson`; the existing
`InheritedEnvMatchesPool` call has no project-endpoint argument. These are used by the added
witnesses. Resolving the textual conflict alone would leave uncompilable tests.

An isolated attempt on the otherwise assembled scratch tree produced a content conflict in
`tests/Antiphon.Tests/ApiKeys/LaunchEnvLayersIntegrationTests.cs`. `git cherry-pick --abort`
restored that tree. This is a dependency refusal, not a test assertion failure. No substitute,
weakened witness or CARD-0167 schema change is part of this recipe.

To append it later: first port and qualify CARD-0167 S1/S2/S3, including producer-stamp repairs
and its rc-specific migrations; then pick `df45aa1d7aa68297945bec894877dbf644e028af`, run its
CP-1 `/*/Antiphon.Tests.ApiKeys/LaunchEnvLayersIntegrationTests/*` (minimum 33), and regenerate
the final manifest. That expanded bundle needs its own resulting tree and verification.

## Manifest regeneration

Both generations use `scripts/audit-land-verification-graph.ps1 -Update`, followed by `-Check`.
No manifest line is hand-edited and no master-only fingerprint commit is picked.

| Inventory point | Exact audit output | Result |
|---|---|---|
| After 1506, before 1483 S1 adds its project | `digest=A1EBB3D0B4C0DF9BB31E417D0AFD7607CDC4BD5A404DACE986CC7CD8EC274DD6 count=57` | Update 0, Check 0; matches the requested value |
| Final rc tree including 1483 S1 | `digest=637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E count=58` | Update 0, Check 0 |

The brief's count 57 cannot describe the final tree while also including 1483 S1. The new
`tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj` and the changed test-project
staging definition explain the new fingerprint. S1's allowlist and all landed pinning repairs
are included before the final audit.

## Build and narrow verification

Every build and test driver runs through `scripts/build-slot.ps1`, with the host's granted
`maxcpucount=6`. Each invocation has a hard 590-second GNU `timeout` limit, with a 10-second
termination grace. The two builds are separate and sequential. Both verify scratch commit
`f5c4f699fe4c91c2720f09e9cb864ed2af7a8d69`, whose tracked tree stayed clean throughout.

| Build | Project and flags | Outcome | Slot |
|---|---|---|---|
| B-1 | `server/Antiphon.Server.csproj --property:OutputPath=bin-rcbundlec/ --property:UseAppHost=false --nologo` | 0 errors, 22 warnings; 75.56 s MSBuild elapsed | `a014773a-8a3d-4784-91f9-c9be4351cebf`, waited 0 s, held 77 s |
| B-2 | `tests/Antiphon.Tests/Antiphon.Tests.csproj` with the same flags | 0 errors, 1111 warnings; 151.37 s MSBuild elapsed | `b36c5085-2865-47f7-825a-89394524f8e6`, waited 30 s, held 153 s |

The warnings include nullable/analyzer warnings, including ASP.NET RouteHandlerAnalyzer AD0001.
They are reported rather than classified as inherited without a base build. No additional
build was needed. This is compile/smoke evidence, not a Full Review or Windows RC qualification.

Four narrow selections provide the brief's one-per-three-card sampling of the eleven requested
landed cards (ten included). Their coverage is limited to the named rows; a sample does not
establish every omitted card's runtime behavior. CARD-1404 CP-2 is one extra lightweight check
because the measured launch recipe is required by the next Windows qualification.

| Card / plan row | Exact selection | Result on the frozen tip | Slot / timing |
|---|---|---|---|
| 1494 CP-1 | `/*/*/(CheckCompactionFixtureTests*)\|(CheckCompactionWorkerLifetimeTests*)/*` | executed 3, passed 3, failed 0, skipped 0 | `f8c60a8d-31d7-4b59-9d24-31ad998e302f`; waited 0 s, held 109 s; test duration 102.458 s |
| 1448 CP-1 | `/*/*/WorktreeManagerGitIntegrationTests/C1448_*` | executed 5, passed 5, failed 0, skipped 0 | `aa99b194-b8ce-4ac6-8981-acd32060f150`; waited 0 s, held 6 s; test duration 2.293 s |
| 0266 CP-2 | Exact combined F1/sweep filter in rerun commands below | executed 11, passed 11, failed 0, skipped 0 | `af6dcf99-cf6d-4bff-a640-7aa407baaf83`; waited 0 s, held 112 s; test duration 106.244 s |
| 1506 CP-1 | `scripts/test-build-driver-environment.ps1` | 6 child scenarios and 4 direct cases passed; 0 failed | `9cdb6ff9-4767-4c97-873a-66a19d4b2378`; waited 0 s, held 21 s |
| 1404 CP-2 (additional) | `scripts/test-nightly-tests.ps1 -Case C1404_Group10MeasuredBudget` with the plan's three temp variables | 1 passed, 0 failed; exit 0 | `83764535-8a29-4f12-8324-5fd5c829f3eb`; waited 60 s, held 7 s |

TRX counters were independently read after the three TUnit runs: total/executed/passed were
3/3/3, 5/5/5 and 11/11/11, with failed/notExecuted both zero. These are 19 green tests.
All four required rows and the additional 1404 row exited 0; no test selection ran zero tests.

CARD-1506 CP-1 ran six child scenarios (6 passed, 0 failed) plus four direct policy cases
(24 setting assertions); the parent environment was unchanged. This uses the script's
Windows-shaped environment seam on Linux, not native Windows memory qualification.

CARD-1404's first invocation omitted the plan's required `TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp`.
It exited 1 with one setup-error row and zero passed; `Join-Path $env:TEMP` received null.
No product assertion was reached. The corrected invocation on the unchanged source used those
three variables and passed 1/1. It proved 158 UIDs exactly once in seven launches, with
`missing=0 double=0 extra=0` and unchanged `watchdog=3600000`. Both outcomes are retained.

The Unit lane, full suite, other CP rows, mutation controls and native Windows acceptance were
not run. Raw logs/TRX remain ignored under the task's `.antiphon/rc-bundle-c/`; the durable
facts and rerun commands are in this document. All 29 `bin-rcbundlec` directories and both owned scratch worktrees were removed after the results were collected and tracked cleanliness was checked.

### Rerun commands on the reproduced tree

Run from the scratch worktree. The Linux toolchain used here has .NET SDK 10 and runtime 9.
The plan documents used to select filters live on master and are intentionally absent from
this rc tree; their exact filters are preserved below.

```bash
EVIDENCE=$(mktemp -d /tmp/rc-bundle-c-evidence.XXXXXX)
export TEMP=/tmp TMP=/tmp TMPDIR=/tmp

timeout --signal=TERM --kill-after=10s 590s pwsh -NoProfile -File scripts/build-slot.ps1 \
  -Label rc-bundle-c-server -- dotnet build server/Antiphon.Server.csproj \
  --property:OutputPath=bin-rcbundlec/ --property:UseAppHost=false --nologo

timeout --signal=TERM --kill-after=10s 590s pwsh -NoProfile -File scripts/build-slot.ps1 \
  -Label rc-bundle-c-tests -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj \
  --property:OutputPath=bin-rcbundlec/ --property:UseAppHost=false --nologo

run_narrow() {
  local card=$1 filter=$2
  timeout --signal=TERM --kill-after=10s 590s pwsh -NoProfile -File scripts/build-slot.ps1 \
    -Label "rc-bundle-c-$card" -- dotnet run --project tests/Antiphon.Tests/Antiphon.Tests.csproj \
    --no-build --property:OutputPath=bin-rcbundlec/ --property:UseAppHost=false -- \
    --treenode-filter "$filter" --report-trx --report-trx-filename "$card.trx" \
    --results-directory "$EVIDENCE/$card"
}
run_narrow 1494 '/*/*/(CheckCompactionFixtureTests*)|(CheckCompactionWorkerLifetimeTests*)/*'
run_narrow 1448 '/*/*/WorktreeManagerGitIntegrationTests/C1448_*'
run_narrow 0266 '/*/Antiphon.Tests.Application/(SessionMessageQueueWedgedHeadTests*)|(AgentTaskCheckSweepTests*)/(C0266_*)|(Parked_head_*)|(Cleared_composer_*)|(the_flush_cancels_a_settled_check*)'
timeout --signal=TERM --kill-after=10s 590s pwsh -NoProfile -File scripts/build-slot.ps1 \
  -Label rc-bundle-c-1506-cp1 -- pwsh -NoProfile -File scripts/test-build-driver-environment.ps1
timeout --signal=TERM --kill-after=10s 590s pwsh -NoProfile -File scripts/build-slot.ps1 \
  -Label rc-bundle-c-1404-cp2 -- pwsh -NoProfile -File scripts/test-nightly-tests.ps1 \
  -Case C1404_Group10MeasuredBudget
```

## Pending appendices: not part of this tree

Final master fetch at 2026-10-22 22:25 UTC: `a141e9afca94dd7766904612d6df445d4f242bcc`. CARD-1502 landed during verification, after the tested tree was frozen; its landed append list is recorded below. The other four pending implementations are still absent from master. A source branch is not proof of master landing. These placeholders
are intentionally excluded from the executable recipe and its tree assertion. After a named
set lands, resolve its landed master copies, retain topological order, drop unconsumed plan/report
paths, rerun the affected narrow selection, regenerate the manifest, and record a new tree.

| Pending set | Prerequisite and exact known source order |
|---|---|
| 1502 S1 | **Landed during this run; pending append/verification.** 1448 first (already included). The reviewed re-cut order is in `.antiphon/task-0d9bea9b.md` on `origin/feat/card-task-0d9bea9b`. The exact six landed code/test commits are below. Their following plan/report commits add no runtime change. Appending them changes the tested tree and requires new verification; they are not counted in the 49 picks above. |
| 1519 + 1457 | Master has plan `5b44186fc19c6a8612e7b17d1647bdd2fa89679f`, but no implementation chain was found in the fetched refs. Append the eventual landed admission-loop and partial-inspection-retention chain; do not pick the plan as a fix. |
| 1483 S2 | S1 and every repair above first. Known source order below, on `origin/feat/card-task-69f144fd`. The brief's `.antiphon/task-69f144fd.md` is absent at its fetched tip `b3d62ad1eade7222e544b1039dce94f7e19a092a`; use the final owner report and landed chain before appending. |
| 1486 S4 | S1/S2/S3 first. No S4 implementation commit was found on master or in the fetched commit subjects. Append the eventual landed closing-line-only report classification test/fix chain, then run its CP-8 selection. |
| 1500 | Only its plan is on master. Known source test/fix/repair order below is on `origin/feat/card-task-05f0443d`, following plan `c6d08dccb8aece1dcf2a757b6a64ffe2824d6ee6`. Use the final reviewed master chain, including any later repairs, before adding the qualification status/recovery scripts and docs. |

Known append orders, oldest first (1502 uses landed master SHAs; the other lists are source-branch SHAs to map after landing):

```text
CARD-1502 S1 (landed; pending append):
a04e42f5a137d7eac8fcd4d9e1c9f63abcf89518
c2c837bc6ff8ff61dfd966252f38ef6486037782
df23c2778e2c8468dc5069a83fd6cd52b71339e9
c2c739e1650cfa518b2214cfd9eb7c146ce4e8a9
8b47b6c0883675c46b694cafcffc80f4321eab10
979876a637a3546742b9cd9fefbcef0b14d9f8a0
Omit docs/report: 4ec059d7befb21db0b2f9f53dce35f5f4e87397c
                 065f9c203ce823e7d0ff1f21970bed3eaeba7236
                 e6bfcbc2c13f4a7431b1d314e2aef87f30de4ca7

CARD-1483 S2:
675f4ad7d0bb9e1a8623ff4e0e1b57223759c9ea
eb1aa9b769d8c8feebda3c6282e930f216047470
fc757ac306cfde3ab6adb3e067c34bad09f3eddb
f84af0d0343771280f81efbad1fc8a472e33019d
b3d62ad1eade7222e544b1039dce94f7e19a092a

CARD-1500:
0d8b75f877d7a1ab8443fd4d5920684d9a32d64b
d5baedb13101fc4e78a48661a273a676530f1b65
74b4e5cd93ad79dca9eba1709beb247e8abf1d88
e90c47450df4247bc254a027390711a555e59980

CARD-1519 + CARD-1457: PENDING landed implementation SHAs
CARD-1486 S4: PENDING landed implementation SHAs
```

## Evidence, activation and limits

The full scratch history passes the evidence guard, exit 0:

```text
EVIDENCE range base=0714a9a89e4bff65aae7e0b4b94098694aeb931c head=f5c4f699fe4c91c2720f09e9cb864ed2af7a8d69
EVIDENCE result commits=60 entries=0 violations=0 base=0714a9a89e4bff65aae7e0b4b94098694aeb931c head=f5c4f699fe4c91c2720f09e9cb864ed2af7a8d69
```

The artifact is a Docs recipe, not a publication, deployment or release approval. The caller
performs the real rc pick after deciding the CARD-1495 disposition. The affected server must be
built/activated from the resulting rc commit; CARD-1506's owner also calls for refreshing the
desktop runner when the active Windows qualification permits it. The bundle adds no migration
and changes no client bundle or session-runner source. No live product process, host, candidate journal,
qualification run or release tag was changed by this task.

## Mechanical pick recipe

Save the **entire** `rc-bundle-c.sh` block below. From a clone with the pinned commits available:

```bash
git fetch origin master rc/2026-10-20
# Refuse if someone has advanced rc since this dry-run.
test "$(git rev-parse origin/rc/2026-10-20)" = 0714a9a89e4bff65aae7e0b4b94098694aeb931c
git worktree add --detach ../rc-bundle-c 0714a9a89e4bff65aae7e0b4b94098694aeb931c
cd ../rc-bundle-c
bash /absolute/path/to/rc-bundle-c.sh
test "$(git rev-parse HEAD^{tree})" = 93c9459395115248f5793e26bc2b0cfe96d275e4
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . \
  -BaseRef 0714a9a89e4bff65aae7e0b4b94098694aeb931c -HeadRef HEAD
# Verification commands above. Publication and release gates remain the caller's work.
```

Expected script result: 48 `PICK clean` lines, one `PICK plan-conflict-dropped` line for
`c15961b94`, both audit values above, and the recorded tree. The helper restores a touched
plan's previous rc copy if it existed, otherwise removes only that touched path. It never
removes the whole plan directory. No added plan is read by an included test; existing
CARD-1409/CARD-1469 test-read plans from bundle B stay intact.

Any `STOP` or nonzero exit means this reproduction is not verified. If a pick is in progress,
abort it and report the unexpected path; do not hand-resolve source conflicts under this recipe.

`rc-bundle-c.sh` (the block executed on the verified scratch tree):

```bash
#!/usr/bin/env bash
set -euo pipefail
BASE=0714a9a89e4bff65aae7e0b4b94098694aeb931c
test "$(git rev-parse HEAD)" = "$BASE" || { echo 'STOP: wrong base'; exit 1; }
test -z "$(git status --porcelain)" || { echo 'STOP: dirty worktree'; exit 1; }
undo() {
  local base=$1 p; shift
  for p in "$@"; do
    if git cat-file -e "$base:$p" 2>/dev/null; then git restore --source="$base" --staged --worktree -- "$p"; else git rm -q -f --ignore-unmatch -- "$p"; fi
  done
}
pick() {
  local s=$1 base bad p
  local -a drop=()
  while IFS= read -r p; do
    case "$p" in docs/superpowers/plans/*|.antiphon/task-*.md) drop+=("$p");; esac
  done < <(git diff-tree --no-commit-id --name-only -r "$s")
  base=$(git rev-parse HEAD)
  if git cherry-pick -x "$s"; then
    if [ "${#drop[@]}" -gt 0 ]; then
      undo "$base" "${drop[@]}"
      if ! git diff --cached --quiet; then git commit -q -m "docs(rc): drop unconsumed plan changes from $s"; fi
    fi
    echo "PICK clean $s"
  else
    bad=$(git diff --name-only --diff-filter=U | grep -v -F -x -f <(printf '%s\n' "${drop[@]}") || true)
    if [ -n "$bad" ]; then echo "STOP $s unexpected conflict: $bad"; exit 1; fi
    undo "$base" "${drop[@]}"
    git -c core.editor=true cherry-pick --continue
    echo "PICK plan-conflict-dropped $s"
  fi
}
# CARD-1499
pick 3d921474345ff25c3af2fdecd8f94b3fa0bdc170
pick 153d9a0683d5b18d587f82f7f6bfcae126ed70a6
pick 3c9db61871cb48f392da59b7ec002651a90a655e
# CARD-1494
pick e6032f85b01d5807cf538f8dbd1ee0813a3ad534
pick f6af11f29d7eb89b74486a766b199d03081e0607
pick 10240653d041ffa00cdc1ed52d12bdf909b5a20e
pick 25c3d343a78dd1afef510b084702cb88f6001526
pick a01b38a2c816a9af7ce3c2872346a6b8685b331d
# CARD-1493 S1 + repair
pick 8866e446762f3fcb7309ab5e25d3824406a637d7
pick f2cfe512d9e3df7d92c55cb62d3cbe09299ffd4f
pick 951e5e210a7f0342557acc436c4ae49ea2116943
pick 4800c50f3216ca5399d900a204004d8eea64ea98
# CARD-1448
pick 0498c748a448f367e6153316c992585d36dea74b
pick a23ca2869618811619bcb0965d386ecb6e786c18
# CARD-1486 S1 S2 S3
pick 27e994af1f6e8de1493408e7a0ec25404199ac0d
pick 8b7edcdde449f7e85e39e27b0f166ebaf92a5b3e
pick 645c7b30b884e3b17352ca4a2dc5c363d09c6c27
pick bcd04d29e8aec21988b281c6b0dc90d21d10a9d3
pick 9516bfd35a3a3e2a075ba71fa4e3c586d7104220
pick 12a2141e6047bc1b40b4f4d74815293f1ef9a7ec
pick 0fc7a4c2b08e6df7354cff2f374c9d603ab5ac03
pick 2316cd6faf3852ad54eb32b77d3a7eae75d910f3
# CARD-1496
pick ccac6053ea4dac9af114361f9de7f955838f3998
pick 0c4386050774c04d2031fd6afd62a3790ea108b6
pick 9bcbb9cbb704239bbb567cdcdb2d0d896a6bd7a6
# CARD-1506 (manifest generated separately)
pick 87262adf4ee92a4eefe8bc0405da3f5775835204
pick c15961b94b3ddef659af9fb9c32175422faf5e5c
pick 4db0d927a9ca1376f8f4a60d94a5b855ce769d36
# Regenerate the CARD-1506-only rc definition inventory before CARD-1483 adds a project.
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check
[ "$(cat server/Application/Services/LandVerificationBuildGraph.manifest.txt)" = "$(printf '%s\n%s' A1EBB3D0B4C0DF9BB31E417D0AFD7607CDC4BD5A404DACE986CC7CD8EC274DD6 57)" ] || { echo 'STOP: intermediate manifest drift'; exit 1; }
git add server/Application/Services/LandVerificationBuildGraph.manifest.txt
git commit -q -m 'build(rc): regenerate CARD-1506 manifest on rc'
# CARD-1404
pick 3d8650eee291b6cef217e7f3367956b45f58cfdb
pick 7370f414ba723a6d41c1ee561f546871cda80503
pick ffa98a8f76588c12df6b0437d24b541762b95f09
# CARD-0266
pick 138c72bdad4fb52b7d4903c91a18f013c7d7b384
pick 96ee5df43209cba8d991ac5e8d11c2d65f90b6d1
pick 60da72847203962d99638fbb4630cdba719bc78b
pick 5f560e91aefb21ffdcd65459b213cf6267ebc3ff
pick 86d03f3fdbcd2f30bec6e73ac0c15dfc093ec101
pick 52debc786ac54e321d0f7b6c2c75e342f468cf34
# CARD-1483 S1 + all landed repairs
pick 7046601e73818ae729fc843e7e6d6b3fbb54d6db
pick 52c2f689f42c1e9b9fd9c90fa5aa3366418dcaaf
pick 19e6c73957bc01e36f2b0b85229e6f0c0ed5e76c
pick a0649997a9b8ef6425bcf327bc98574828f23bdf
pick 111570541259d84f99f41a22bd5b46dff84d13a1
pick 62c61a29fe2797ac3e32bd195a976a2e62382217
pick 296f74948994ace98e0832dd21f10588c8b0eaef
pick 8429109ed37cb4361deba6cbd26054ec27414317
pick 2132c24d3d1bb9ed67eaffd558cf2766a5bb95c7
pick c3d806fb88923dfa798e4a5a91df00650fe07ad9
pick 39c14c776e0c0e357a5f57f9c1c85085f65b35eb
pick b20e12e2f4f2878f25280a1576bffae59659fb57

# Final inventory includes CARD-1483 S1's additional project.
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check
git add server/Application/Services/LandVerificationBuildGraph.manifest.txt
git commit -q -m 'build(rc): regenerate final bundle C land graph'
echo "bundle tip $(git rev-parse HEAD) tree $(git rev-parse HEAD^{tree})"
```
