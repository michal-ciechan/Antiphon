# Final rc bundle C recipe (2026-10-23)

Task a034909f, Docs/dry-run. The complete recipe below extends the
[previous bundle C recipe](2026-10-22-rc-bundle-c-dry-run.md) from its original base.
Publication belongs to the orchestrator. This task writes only its assigned Docs branch;
it does not advance rc, publish a tag, activate a server, or alter qualification journals.

## Frozen selection and expected result

- Base: `0714a9a89e4bff65aae7e0b4b94098694aeb931c`.
- Fetched master selection: `eb6101b1a0eb8b7542cd631f8064c6ae0671733c`.
  The script fetches master at execution and checks every picked/skipped source, plus
  the path-only allowance prerequisite, against both this snapshot and fetched master.
  Later master arrivals do not silently change this closed recipe.
- Verified scratch commit: `2e27e0d8168fd2942b28fee6fa1a29ebeb8270eb`.
- **Expected tree: `ee0de6f2459da6c21892b8565a2b0c7871f23ad1`.** Cherry-pick timestamps
  make the commit IDs variable; the tree is the reproduction assertion.
- 93 scratch commits: 77 source picks, 13 ordinary plan/report/inventory cleanup
  commits, one path-only allowance prerequisite, and two audit-generated manifest updates.
- 68 clean picks, nine picks with conflicts only in omitted documents, and twelve explicit
  plan/report-only skips. No production/test conflict was hand-resolved or dropped.
- 79 changed files, +5949/-269. No change under `src/`, `client/` or `server/Migrations/`.
  Plans, reports, mutation inventories and `.antiphon/` have zero net change from the base.
  Existing rc plans remain byte-identical, including the plans consumed by existing tests.

The initial assembly stopped at the S2 docs-only tip after the helper had removed its
plan/report. That entry was corrected to `skip_docs`, and the complete script was rerun
from a fresh detached checkout of the base. No build/test ran on that incomplete assembly.
CARD-1512 landed during the first verification round. The next selection was
`98e96f029a8117034445d6c236db0cd0c045a0e6`. Its first assembly stopped safely
because the report lived under investigations; the helper now drops that one exact
report path, without exempting other investigation files. The completed CARD-1512
round passed all 49 executions and differed from the first tree only in
`AgentTaskLandPublicationTests.cs`.

CARD-1483 S3 then landed during that round. The final selection is frozen at the
master SHA above, observed before assembling this final pass within the caller's
60-minute budget. CARD-1486 S4/repair had not landed at that cutoff. No later arrival
is silently appended to a verified tree. The final pass includes S3's three mapped
commits, its CP-3 and its bounded real-tree pin/evaluation check. Each source change
required a fresh build per project and the exact requested final-tree selections;
the two earlier green rounds remain historical evidence below, never relabeled.
No build ran on either incomplete assembly.

## Per-card disposition

The full script is the authoritative ordered list; every SHA in it is complete.
The original bundle's SHA corrections and CARD-1495 dependency investigation remain
in the linked dry-run document. This table supersedes its pending-card disposition.

| Card | Source picks | Included behavior and document treatment |
|---|---:|---|
| 1499 | 3 | Release-unresolved fixes; unconsumed plan/report omitted. |
| 1494 | 5 | Fixture lifetime and busy-produce repairs; plan/report omitted. |
| 1493 S1 + repair | 4 | Duplicate-build warning importer and nonnumeric-row recommendation repair; plans/reports omitted. |
| 1448 | 2 | Both worktree code commits precede 1502; the two plan/report-only entries are explicitly skipped in order. |
| 1502 S1 | 6 | All eight reviewed re-cut entries processed in dependency order: six picks, two skips, then final report skip. |
| 1486 S1/S2/S3 | 8 | Re-arm, retained-report and waiting-deadline fixes with repairs. S4 and its repair are pending below. |
| 1496 | 3 | Held-model Retry and wall-reroute refusal; plan/report omitted. |
| 1506 | 3 | Windows build-child environment policy and null restoration; static manifest regenerated for rc, never copied from master. |
| 1404 | 3 | Measured group-10 costs, fixed listing expectations and owner/investigation documents retained. |
| 0266 | 6 | Canceled-composer hold and queue notification repair, including assertion and live-buffer fixture repairs. |
| 1483 S1 + repairs | 12 | Evaluated audit tool and all S1 allowlist/pinning repairs from the prior recipe. |
| 1483 S2 | 5 | Glob reachability, project closure and import trust, including the SDK-owned import repair. The sixth, docs-only refinement is skipped; its CP-2 filter is retained in this verification record. |
| 1483 S3 | 3 | Target/OS element pins, all S3 fixtures and the gate-isolation repair; plan/report/candidate omitted. The twelve landed pins are retained verbatim and checked against this rc tree. |
| 1500 | 5 | Read-only qualification status, journal-schema correction, Windows fixture host selection, and the operator recovery procedures. All eight Review entries are reconciled below; three are plan/report-only skips. |
| 1519 + 1457 | 7 | Independent admission/maintenance loops, owned shutdown, complete queued-snapshot reconciliation and all fixture repairs. Both dispatcher owner-document changes are retained. |
| 1511 | 1 | All 26 removal-boundary fixture variants; skip the earlier diagnostic plan and drop the repair's plan/mutation candidate. No product change. |
| 1512 | 1 | Publication-image fixture repair: protected paths, retained bytes/hash before and after cleanup, and unavailable-retention preservation; skip both plan/report entries. |
| 1232 allowance prerequisite | 0 whole commits | Only the exact allowlist patch from `ba3f39731d9a99ea10717d3b4a9d795ba74f8d27`, in its own scratch commit. |

CARD-1519's allowance hunk uses the CARD-1232 allowance as context. rc lacks both that
allowance and `PreDispatchFailureDeliveryTests`. The script applies only that source
commit's `tests/Antiphon.Tests/slow-tests-allowlist.txt` diff before the seven dispatcher
picks. This preserves the exact CARD-1232 comment and class line explicitly requested by
the brief. It remains a dormant allowance on rc; it imports no CARD-1232 implementation,
selects no extra test, and claims no CARD-1232 qualification. No complete master allowlist
is copied over rc's file.

## Reviewed source to landed master mapping

All 27 pairs below have equal `git patch-id --stable` values. Every landed copy is an
ancestor of the frozen master snapshot. In particular, `c6d08dccb` is CARD-1500's
plan-only prerequisite, not an additional runtime prerequisite. The five implementation
SHAs in the old pending appendix are exactly the five picks in Review's eight-entry order.
The brief's `510f0a3bd` and `5b4ecc581` are source tips, not the rebased master identities.

| Reviewed source | Landed master copy | Disposition |
|---|---|---|
| `675f4ad7d0bb9e1a8623ff4e0e1b57223759c9ea` | `df8c6695a13f95a260f74199109f25de05466df8` | pick |
| `eb1aa9b769d8c8feebda3c6282e930f216047470` | `17dc4a9899cab08323774655c4d9a86f226219dd` | pick |
| `fc757ac306cfde3ab6adb3e067c34bad09f3eddb` | `dd980c82e9932b35bf4bf2d9d6f5726380709e0b` | pick |
| `f84af0d0343771280f81efbad1fc8a472e33019d` | `285aa79bf05fc677d437dc4185be71571321d9ab` | pick |
| `b3d62ad1eade7222e544b1039dce94f7e19a092a` | `aed1449a8a7a6c0321ae81b287fa1066a222ad0e` | pick |
| `69089d6061f79ca93de444ce383b76b5bb4fba6d` | `8ef0baca812807bf83c7c34578173ac61a36eed6` | skip plan/report only |
| `c6d08dccb8aece1dcf2a757b6a64ffe2824d6ee6` | `5d9cdbe9b92a0a8ab5fa94588a8af94d8afdb0f4` | skip plan/report only |
| `0d8b75f877d7a1ab8443fd4d5920684d9a32d64b` | `82083a4043c1cf41b863d1aafadf8e16e6dc5a9a` | pick |
| `d5baedb13101fc4e78a48661a273a676530f1b65` | `c4af6e8c7f679fe2327401404d2e81f7277700e4` | pick |
| `74b4e5cd93ad79dca9eba1709beb247e8abf1d88` | `9c4143a0dd980b54f0b2916578176553dac5921a` | pick |
| `e90c47450df4247bc254a027390711a555e59980` | `f6cf651292a64ba4f7e7edfd9b845697503b8963` | pick |
| `55c0dc6da27c587e9a1b3a2012e099fa98968393` | `9223cac4fd5d3b4a545c2c806ad9db48587a0dd9` | skip plan/report only |
| `9c42243747ba4fe252f85a7ec5c39bebeac0138b` | `565ce3cb987314326392aa0caa99dd4eb79a465d` | pick |
| `510f0a3bd88f42159641e61bd6ffc0b8b83c6cc1` | `5ffed3091a204c08f87421693d9bbb43e1692f82` | skip plan/report only |
| `0673692b40deadfa960941e23c223f09bd050986` | `6d1fdc8f0fa238a4d2b4d06ef9432b831f85ac52` | pick |
| `cb74d7186b1f4c58f2d7939908d88ed2e53c1bec` | `e72e819ba693928e829d0a3e2719cf9e16f37a77` | pick |
| `28b01587944fd0c7d70c8f9882453830043736f8` | `6b80707b3a9a54c0b77775802496b8034d2930ba` | pick |
| `3473dee4383a37a1e42d6990670d5909711fe3e2` | `339a851f82a2342d2ed54e5ddaaaab4b75c440d5` | pick |
| `60b4ea7bcf9acd550efee668d12dcbbb51e3dc9b` | `f974ac3c420f17df00d6adace5bd2a1410444153` | pick |
| `b6a1d9f739b02308bd6285bae9fe4cad30998eab` | `08533c587550e6a89013f437b60e543ebf4151ca` | pick |
| `5b4ecc581849df5dc5511d5ea82b8ef1774d5ba7` | `19a0b6aa27c9cd925ddb045d5de1e6be364f42e2` | pick |
| `7cc17d89d8fe60e6ef820e1a3540641bf4620897` | `5a4d06345b7425417d268dc12a9ac98fca53789c` | skip plan/report only |
| `5d404f6b6662e8bc4f819548925d01d2cd63e374` | `e598d4a920773ccbc0273cdce902ca3dab7c2b01` | pick |
| `e013d933b80d232a4264a4ef486fafe85743a9ba` | `98e96f029a8117034445d6c236db0cd0c045a0e6` | skip plan/report only |
| `8c947567933c371afa3d6a1142ca232b73c74246` | `bc601a4c24774c6e1dde8becccb77223b9a7c969` | pick |
| `92c42ff5d5c3e97e8a1ff17387a96892487a4cf3` | `63df8861705faa7ff603cccd8017bf28615af9cb` | pick |
| `94983dd40c44982679e90137aca7171dce2183ca` | `eb6101b1a0eb8b7542cd631f8064c6ae0671733c` | pick |

## Explicit exclusions and pending placeholders

- **CARD-1495:** no pick of `df45aa1d7aa68297945bec894877dbf644e028af` or its witnesses.
  Caller exclusion remains binding; its endpoint-dependent fixture does not compile on
  rc without the intentionally excluded CARD-0167 prerequisites.
- **CARD-0167:** every provider-endpoint stage and repair remains master-only. No endpoint
  model, schema, migration, client UI, canary or prerequisite is imported.
- **CARD-1486 S4 — PENDING, including its test repair:** source chain
  `31e12f9cdd56ef47b1c597fe9a89209554f330cc`,
  `91585acd67474e1c92a7bd3606bc004700c16541`,
  `eb538317f1e769870cf3cb2d9f839f6607132412`; repair chain
  `0be73e280b707a78fb2a98a921dd07fea06ac062`,
  `886d9240556811853044295ee9ff1ee2c952a389`,
  `758f120ef378fdfecfd536e53b9f6d674158177d`.
  No matching implementation patch landed at the snapshot. S1/S2/S3 stay included.
  A later append must include both S4 and its test repair and verify CP-8. S5 is outside
  this commissioned bundle.
- **Unconsumed documents:** every touched `docs/superpowers/plans/*`,
  `docs/superpowers/reports/*`, `docs/superpowers/mutations/*` and `.antiphon/task-*.md`
  is restored to its preceding rc version, or removed if absent there. This extends the
  original `undo`/`pick`/`skip_docs` helpers to the newly encountered report/inventory paths.
  The exact report `docs/investigations/2026-10-22-card-1512-fixture-repair.md` is also
  omitted; other investigation documents retain their original treatment.
  All twelve fully skipped entries are listed explicitly in the complete script.
- **Master manifest-only commits:** `5161519241a1f781e78307261460b0e7b87617fc` and the
  prior recipe's `d2bf22dc09dfa728bee05be172cfdb797bab1148` remain omitted; rc generates its own inventory. The earlier
  recipe's omitted standalone reports/plans and empty provenance commits remain omitted.
- **Later CARD-1483 slices:** S4 file probes and S5 production wiring remain outside this bundle.
- **Other master changes:** the recipe is a closed list, not a range merge. No unrelated
  master commit, diagnostic document or pending source-branch tip is included.

Conditional-card checks combined master file history, source ancestry and stable-patch-ID
comparison over all new master commits since `5ffed3091a204c08f87421693d9bbb43e1692f82`.
A later landing cannot be inferred from the presence of a remote source branch.
The final fetch reached `72c2464cfe572a4b4a5881c09ab1f0265b43ba51`; it added unrelated CARD-1522
repairs and planning documents, with no further conditional bundle card landed.
Those changes remain outside the closed recipe. CARD-1486 S4/repair was still absent.

## Generated manifest

Both inventory points use `scripts/audit-land-verification-graph.ps1 -Update` followed by
`-Check`; all four audit invocations exited 0. No manifest line was edited by hand.

| Point | Digest | Count |
|---|---|---:|
| After CARD-1506, before the additional S1 tool project | `A1EBB3D0B4C0DF9BB31E417D0AFD7607CDC4BD5A404DACE986CC7CD8EC274DD6` | 57 |
| Final assembled tree | `637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E` | 58 |

S2 and the other new cards do not change the build-definition inventory, so the final
fingerprint is the same as the earlier eleven-card recipe. It is freshly generated here.

## Historical verification before CARD-1512

At `f99d73dfc5b76d16e805ddf6814c4902145c0745` (tree
`a3bcc48d181d8f232c66544a4f84b67f5f2a33f5`), the original two builds and six
selections passed. Server: 0 errors, 22 warnings, 58.48 seconds. Antiphon.Tests:
0 errors, 1115 warnings, 87.89 seconds. CP counts: 1519 CP-2 3/3, CP-4 4/4;
1511 CP-1 26/26; 1500 CP-1 1/1 and CP-2 1/1; 1483 S2 CP-2 10/10. Total
45 passed, 0 failed/skipped. The eight granted slots held for 59/89/63/62/99/7/6/34
seconds, all with zero wait. Its evidence guard recorded 88 commits, two entries, zero
violations. All 29 build output directories and that scratch worktree were removed.
Those logs/TRX stay in `.antiphon/rc-bundle-c-final/`; final-round evidence is in its
`with1512/` subdirectory. The new CARD-1512 landing, rather than a failed green check,
required the final source change and the explicitly attributed new round below.

## Historical verification including CARD-1512, before S3

At `2a429d776c3c6b66f8c501c93699dde74a205935` (tree
`d34d55d3065809392f9527f67a170ea75244c2da`), the two builds and seven selections
passed: 49 executed/passed, 0 failed/skipped. The source stayed clean.

| Run | Result | Slot / timing |
|---|---|---|
| Server build | 0 errors, 22 warnings; 00:01:51.01 | `fcdc4ab2-2e58-4a0f-bcc8-0397f2d49529`; wait 0s, held 112s |
| Antiphon.Tests build | 0 errors, 1115 warnings; 00:02:18.52 | `6495c4f9-dcb8-4943-8d95-5e40093933d9`; wait 0s, held 139s |
| 1519-cp2 | executed/passed 3/3, failed 0, skipped 0; 53s 637ms | `2b5f6cb4-9751-4de4-8045-d4dedfdfb497`; wait 0s, held 57s |
| 1519-cp4 | executed/passed 4/4, failed 0, skipped 0; 1m 03s 391ms | `b51be7a1-53bf-4113-8e63-086e1e0550c9`; wait 0s, held 66s |
| 1511-cp1 | executed/passed 26/26, failed 0, skipped 0; 2m 26s 787ms | `64575fdc-0837-487a-b32e-4121c0a718d0`; wait 0s, held 153s |
| 1500-cp1 | executed/passed 1/1, failed 0, skipped 0; 10s 804ms | `f4c9fe52-3739-44b7-964d-032fdb89ddb5`; wait 0s, held 18s |
| 1500-cp2 | executed/passed 1/1, failed 0, skipped 0; 4s 889ms | `03cf0e27-5a00-4f60-b19b-ed606d4ba436`; wait 0s, held 10s |
| 1483-s2-cp2 | executed/passed 10/10, failed 0, skipped 0; 40s 350ms | `15ae513a-8efd-44d2-a22d-0be5f96ec314`; wait 0s, held 49s |
| 1512-cp1 | executed/passed 4/4, failed 0, skipped 0; 1m 16s 703ms | `b47183b4-382e-44bd-a55c-fc344a988f96`; wait 0s, held 82s |

Its evidence guard recorded 89 commits, two entries, zero violations. All 29 build
output directories and that scratch worktree were removed. Logs/TRX are in
`.antiphon/rc-bundle-c-final/with1512/`; final S3 evidence is in `withs3/`.

## Final-tree verification

Exactly one server build and one Antiphon.Tests build ran, sequentially, with
`OutputPath=bin-rcbundlecfinal/`, `UseAppHost=false`, the build-slot gate and a hard
590-second timeout on each invocation. All eleven invocations exited 0. The host
granted `maxcpucount=6`; no run was unleased. The eight TUnit filters match their
committed master plans byte-for-byte after Markdown pipe unescaping. All results
below belong to the frozen scratch commit above; tracked HEAD/index/source stayed clean
before and after every invocation. The test runs used `--no-build`.

| Run | Result | Slot / timing |
|---|---|---|
| Server build | 0 errors, 22 warnings; 00:01:40.16 | `59aff34c-2297-4162-a9f1-d8d8715c44e5`; wait 0s, held 102s |
| Antiphon.Tests build | 0 errors, 1118 warnings; 00:02:19.65 | `8c15480b-d80c-4ace-9dda-3a23fc456fd1`; wait 30s, held 141s |
| 1519-cp2 | executed/passed 3/3, failed 0, skipped 0; 1m 15s 016ms | `bd5ca2e4-0259-4975-b76d-0f0038e3fe39`; wait 75s, held 78s |
| 1519-cp4 | executed/passed 4/4, failed 0, skipped 0; 57s 073ms | `2c55b3e8-7bfc-4613-9021-80e403027573`; wait 0s, held 63s |
| 1511-cp1 | executed/passed 26/26, failed 0, skipped 0; 1m 49s 082ms | `116ebadd-02c0-4b2b-91e2-32a22fed3ace`; wait 0s, held 113s |
| 1500-cp1 | executed/passed 1/1, failed 0, skipped 0; 5s 201ms | `48d1b236-5abb-4c47-8ab9-a966da1355b0`; wait 0s, held 8s |
| 1500-cp2 | executed/passed 1/1, failed 0, skipped 0; 2s 774ms | `9a65da6b-3a04-4f36-a207-3ae274656e60`; wait 0s, held 6s |
| 1483-s2-cp2 | executed/passed 10/10, failed 0, skipped 0; 50s 323ms | `73ddfd76-5cab-4a92-b64b-ddd30c612922`; wait 0s, held 53s |
| 1512-cp1 | executed/passed 4/4, failed 0, skipped 0; 59s 885ms | `0aca4224-1d4b-4a12-9983-d145e13567ee`; wait 0s, held 63s |
| 1483-s3-cp3 | executed/passed 20/20, failed 0, skipped 0; 56s 417ms | `aed1cd1b-8791-42fc-a0a3-a159da7429de`; wait 0s, held 62s |
| 1483 S3 real-tree audit | exit 0; twelve landed pins matched and solution approved | `7d081688-8589-4629-8f07-8c15db6908d5`; wait 0s, held 17s |

CP-3 includes CP-2’s ten cases; the two required rows overlap intentionally.
Fresh TRX counters and test identities were independently read: **69 executed, 69 passed,
0 failed, 0 skipped**. Build warnings include nullable/analyzer diagnostics and ASP.NET
RouteHandlerAnalyzer AD0001; they are reported without claiming inherited attribution
from an unperformed base build.

The S3 real-tree invocation uses the staged audit tool from the same test build,
with the build-slot gate. Its solution evaluation has the tool's 120-second bound.
The twelve pin lines are the landed source verbatim; the real-tree result checks
their applicability to rc without regeneration or hand-editing. The approved audit
also inventoried three tracked projects outside the solution as `not-built`:
`docs/investigations/evidence/card-0462/Reproduce.csproj`,
`scripts/fixtures/nightly/c1375-probe/Probe.csproj`, and
`scripts/fixtures/nightly/c487-probe/Probe.csproj`.

No Unit lane, full assembly, mutation control, native Windows row, E2E, activation or
release qualification was run. This is the requested Linux compile/narrow-selection
recipe verification, not Full Review or permission to publish a release. The script and
executable examples require ordinary review; this is not a D-2 narrative-only exemption.

The scratch history passes the evidence guard:

```text
EVIDENCE range base=0714a9a89e4bff65aae7e0b4b94098694aeb931c head=2e27e0d8168fd2942b28fee6fa1a29ebeb8270eb
EVIDENCE result commits=93 entries=2 violations=0 base=0714a9a89e4bff65aae7e0b4b94098694aeb931c head=2e27e0d8168fd2942b28fee6fa1a29ebeb8270eb
```

Raw assembly logs, build/test logs, fresh TRX, mapping data and the local script copy stay
ignored under `.antiphon/rc-bundle-c-final/`. The checked-in document and root script are
the durable deliverables; no generated payload is committed.

All 29 producer-owned `bin-rcbundlecfinal` directories from the final round were
removed after verification. The owned detached scratch worktree was then removed
normally; tracked source was clean and the expected tree still matched before removal.

## Reproduce without publishing

The checked-in root file `rc-bundle-c-final.sh` and the complete shell block below are
byte-identical. A report-folder copy also exists at
`/work/worktrees/task-a034909f/.antiphon/rc-bundle-c-final/rc-bundle-c-final.sh`.
The assigned-worktree root copy is
`/work/worktrees/task-a034909f/rc-bundle-c-final.sh`.

```bash
# Extract from this document, or use the checked-in root script directly.
sed -n '/^#!\/usr\/bin\/env bash$/,/^```$/p' \
  docs/investigations/2026-10-23-rc-bundle-c-final.md | sed '$d' > /tmp/rc-bundle-c-final.sh
bash -n /tmp/rc-bundle-c-final.sh
git fetch origin master rc/2026-10-20
test "$(git rev-parse origin/rc/2026-10-20)" = 0714a9a89e4bff65aae7e0b4b94098694aeb931c
git worktree add --detach ../rc-bundle-c-final 0714a9a89e4bff65aae7e0b4b94098694aeb931c
cd ../rc-bundle-c-final
bash /tmp/rc-bundle-c-final.sh
test "$(git rev-parse HEAD^{tree})" = ee0de6f2459da6c21892b8565a2b0c7871f23ad1
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . \
  -BaseRef 0714a9a89e4bff65aae7e0b4b94098694aeb931c -HeadRef HEAD
```

Any nonzero exit, unexpected conflict, changed base or tree mismatch stops reproduction.
If a pick is still in progress, abort that pick and report it. The recipe does not authorize
hand-resolution of production/test conflicts or substitution of a newer pending tip.
The orchestrator owns the actual rc pick/push and all activation/release gates.

### Exact verification commands

Run from the reproduced scratch tree. Each invocation takes its own build slot and has a
590-second hard timeout, with ten seconds for termination. Build exactly once per project,
then reuse the test build for the eight exact master-plan filters. `TEMP`, `TMP` and `TMPDIR`
are set only for this process and its children. Fresh evidence belongs outside the scratch
source tree. The complete command list is preserved below for an authorized rerun.

```bash
set -euo pipefail
EVIDENCE=$(mktemp -d /tmp/rc-bundle-c-final-evidence.XXXXXX)
export TEMP=/tmp TMP=/tmp TMPDIR=/tmp
SOURCE=$(git rev-parse HEAD)
echo "$SOURCE" > "$EVIDENCE/verified-source.txt"
test -z "$(git status --porcelain)"
run() {
  local name=$1 result; shift
  echo "START $name source=$SOURCE $(date -u +%FT%TZ)"
  set +e
  timeout --signal=TERM --kill-after=10s 590s pwsh -NoProfile -File scripts/build-slot.ps1 -Label "rc-bundle-c-final-$name" -- "$@" > "$EVIDENCE/$name.log" 2>&1
  result=$?
  set -e
  echo "$name exit=$result $(date -u +%FT%TZ)" | tee -a "$EVIDENCE/results.txt"
  tail -8 "$EVIDENCE/$name.log"
  test "$(git rev-parse HEAD)" = "$SOURCE"
  test -z "$(git status --porcelain)"
  return "$result"
}
run server dotnet build server/Antiphon.Server.csproj --property:OutputPath=bin-rcbundlecfinal/ --property:UseAppHost=false --nologo
run tests dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-rcbundlecfinal/ --property:UseAppHost=false --nologo
run_narrow() {
  local card=$1 filter=$2
  run "$card" dotnet run --project tests/Antiphon.Tests/Antiphon.Tests.csproj --no-build --property:OutputPath=bin-rcbundlecfinal/ --property:UseAppHost=false -- --treenode-filter "$filter" --report-trx --report-trx-filename "$card.trx" --results-directory "$EVIDENCE/$card"
}
run_narrow 1519-cp2 '/*/Antiphon.Tests.Application/DispatcherAdmissionSchedulingTests/(C1519_freed_seat_is_admitted_while_maintenance_waits*)|(C1519_full_capacity_and_repeated_wakes_preserve_single_admission*)|(C1519_shutdown_joins_loops_and_owned_sweep_scopes*)'
run_narrow 1519-cp4 '/*/Antiphon.Tests.Application/DispatcherSiblingInspectionSchedulingTests/(C1457_partial_ready_pass_preserves_unrelated_inspections*)|(C1428_lease_refusal_reuses_the_inspection_on_the_next_attempt*)|(C1428_stale_observation_at_claim_reinspects_and_keeps_the_waiter*)'
run_narrow 1511-cp1 '/*/*/AgentTaskLandRemovalMatrixTests/C448_V18_LowLevelRemovalRechecksAtBothContentBoundaries'
run_narrow 1500-cp1 '/*/*/RcQualificationStatusTests/C1500_StatusIdentityAndObservation'
run_narrow 1500-cp2 '/*/*/RcQualificationStatusTests/C1500_StatusDoesNotMutate'
run_narrow 1483-s2-cp2 '/*/*/LandGraphEvaluationTests/(C1483_S1_*)|(C1483_S2_*)'
run_narrow 1512-cp1 '/*/*/AgentTaskLandPublicationTests/(C448_V04_IgnoredFilesSurviveConfirmedPublication*)|(C1512_ImageEvidenceIsRetainedBeforeCleanup*)|(C1512_UnavailableImageRetentionPreservesSource*)'
run_narrow 1483-s3-cp3 '/*/*/LandGraphEvaluationTests/(C1483_S1_*)|(C1483_S2_*)|(C1483_S3_*)'
run 1483-s3-real-tree dotnet tests/Antiphon.Tests/bin-rcbundlecfinal/land-graph-audit/Antiphon.LandGraphAudit.dll --repo . --solution Antiphon.sln
```

## Complete rc-bundle-c-final.sh

```bash
#!/usr/bin/env bash
set -euo pipefail
BASE=0714a9a89e4bff65aae7e0b4b94098694aeb931c
test "$(git rev-parse HEAD)" = "$BASE" || { echo 'STOP: wrong base'; exit 1; }
test -z "$(git status --porcelain)" || { echo 'STOP: dirty worktree'; exit 1; }
# Freeze the selection, but prove every source is still reachable from fetched master.
SELECTION_MASTER=eb6101b1a0eb8b7542cd631f8064c6ae0671733c
git fetch origin master
git merge-base --is-ancestor "$SELECTION_MASTER" origin/master
assert_source() {
  git merge-base --is-ancestor "$1" "$SELECTION_MASTER" || { echo "STOP: source outside frozen master: $1"; exit 1; }
  git merge-base --is-ancestor "$1" origin/master || { echo "STOP: source no longer on master: $1"; exit 1; }
}
undo() {
  local base=$1 p; shift
  for p in "$@"; do
    if git cat-file -e "$base:$p" 2>/dev/null; then git restore --source="$base" --staged --worktree -- "$p"; else git rm -q -f --ignore-unmatch -- "$p"; fi
  done
}
pick() {
  local s=$1 base bad p
  assert_source "$s"
  local -a drop=()
  while IFS= read -r p; do
    case "$p" in docs/superpowers/plans/*|docs/superpowers/mutations/*|docs/superpowers/reports/*|docs/investigations/2026-10-22-card-1512-fixture-repair.md|.antiphon/task-*.md) drop+=("$p");; esac
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
# Preserve the full dependency order while omitting unconsumed plan/report-only commits.
skip_docs() {
  local s=$1 p
  assert_source "$s"
  while IFS= read -r p; do
    case "$p" in docs/superpowers/plans/*|docs/superpowers/mutations/*|docs/superpowers/reports/*|docs/investigations/2026-10-22-card-1512-fixture-repair.md|.antiphon/task-*.md) ;;
      *) echo "STOP $s is not plan/report-only: $p"; exit 1;;
    esac
  done < <(git diff-tree --no-commit-id --name-only -r "$s")
  echo "SKIP plan/report-only $s"
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
# CARD-1448's third/fourth commits are plan/report-only; preserve their position.
skip_docs e4ef4c621cb43835254db8d9d21fb79abc18f7bd
skip_docs 222ceeb0074427fb42b7a7cb8b439fa0e17ffa59
# CARD-1502: all eight re-cut commits, now using their landed master copies.
pick a04e42f5a137d7eac8fcd4d9e1c9f63abcf89518
pick c2c837bc6ff8ff61dfd966252f38ef6486037782
pick df23c2778e2c8468dc5069a83fd6cd52b71339e9
pick c2c739e1650cfa518b2214cfd9eb7c146ce4e8a9
pick 8b47b6c0883675c46b694cafcffc80f4321eab10
pick 979876a637a3546742b9cd9fefbcef0b14d9f8a0
skip_docs 4ec059d7befb21db0b2f9f53dce35f5f4e87397c
skip_docs 065f9c203ce823e7d0ff1f21970bed3eaeba7236
# Final re-cut report identifies the landed owner; no runtime delta.
skip_docs e6bfcbc2c13f4a7431b1d314e2aef87f30de4ca7
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

# CARD-1483 S2: five code/test commits and the minimal-verification docs tip.
pick df8c6695a13f95a260f74199109f25de05466df8
pick 17dc4a9899cab08323774655c4d9a86f226219dd
pick dd980c82e9932b35bf4bf2d9d6f5726380709e0b
pick 285aa79bf05fc677d437dc4185be71571321d9ab
pick aed1449a8a7a6c0321ae81b287fa1066a222ad0e
skip_docs 8ef0baca812807bf83c7c34578173ac61a36eed6

# CARD-1483 S3: landed target/OS pin gate after S2, including its isolation repair.
pick bc601a4c24774c6e1dde8becccb77223b9a7c969
pick 63df8861705faa7ff603cccd8017bf28615af9cb
pick eb6101b1a0eb8b7542cd631f8064c6ae0671733c

# CARD-1500: Review order, including explicit prerequisite/scope-plan skips.
skip_docs 5d9cdbe9b92a0a8ab5fa94588a8af94d8afdb0f4
pick 82083a4043c1cf41b863d1aafadf8e16e6dc5a9a
pick c4af6e8c7f679fe2327401404d2e81f7277700e4
pick 9c4143a0dd980b54f0b2916578176553dac5921a
pick f6cf651292a64ba4f7e7edfd9b845697503b8963
skip_docs 9223cac4fd5d3b4a545c2c806ad9db48587a0dd9
pick 565ce3cb987314326392aa0caa99dd4eb79a465d
skip_docs 5ffed3091a204c08f87421693d9bbb43e1692f82

# CARD-1519 / CARD-1457: retain CARD-1232's exact missing allowlist context.
# Only this path from the prerequisite is ported; no CARD-1232 runtime/test code.
ALLOWLIST_SOURCE=ba3f39731d9a99ea10717d3b4a9d795ba74f8d27
assert_source "$ALLOWLIST_SOURCE"
git diff "$ALLOWLIST_SOURCE^" "$ALLOWLIST_SOURCE" -- tests/Antiphon.Tests/slow-tests-allowlist.txt | git apply --index
git commit -q -m "test(rc): carry CARD-1232 slow allowance required by CARD-1519" -m "Path-only prerequisite from $ALLOWLIST_SOURCE; no runtime or test implementation port."
pick 6d1fdc8f0fa238a4d2b4d06ef9432b831f85ac52
pick e72e819ba693928e829d0a3e2719cf9e16f37a77
pick 6b80707b3a9a54c0b77775802496b8034d2930ba
pick 339a851f82a2342d2ed54e5ddaaaab4b75c440d5
pick f974ac3c420f17df00d6adace5bd2a1410444153
pick 08533c587550e6a89013f437b60e543ebf4151ca
pick 19a0b6aa27c9cd925ddb045d5de1e6be364f42e2

# CARD-1511: skip diagnostic plan, then keep the fixture repair, drop plan/candidate.
skip_docs 522cbb93de9073c3e889c21933f173494eb6a3de
pick 745cc90e6d5c838bee86ff9c6a133225641465a6

# CARD-1512: landed during the initial verification; keep its test-only repair.
skip_docs 5a4d06345b7425417d268dc12a9ac98fca53789c
pick e598d4a920773ccbc0273cdce902ca3dab7c2b01
skip_docs 98e96f029a8117034445d6c236db0cd0c045a0e6

# EXCLUDED: CARD-1495 and CARD-0167, by caller decision.
# PENDING at SELECTION_MASTER: CARD-1486 S4 plus its test repair. Do not infer permission to append branch tips.

# Final inventory includes CARD-1483 S1/S2 and every selected addition.
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update
pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check
git add server/Application/Services/LandVerificationBuildGraph.manifest.txt
git commit -q -m 'build(rc): regenerate final bundle C land graph'
cat server/Application/Services/LandVerificationBuildGraph.manifest.txt
echo "bundle tip $(git rev-parse HEAD) tree $(git rev-parse HEAD^{tree})"
```
