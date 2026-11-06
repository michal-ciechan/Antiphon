# CARD-1737 R2 salvage-source verification design

Date: 2026-11-06. TestDesign task `0ba30c05`, branch `feat/card-task-0ba30c05`.
Inspected checkout and Plan base: `d83a52d8c320583bcd191e250be73f2ce04923e2`.

This appends the verification contract for **R2 only** to the unchanged
[fix design](../plans/2026-11-06-card-1737-s4a-witness-repairs-plan.md).
It follows the R1 design at
`b8efe5cc91d3ae4015b100a9bcf89d40e92827b7:docs/superpowers/testdesign/2026-11-06-card-1737-r1-testdesign.md`.
Plan ledger rows **07/U1, 08/U3 and 10/U5** become seven methods, seven independent
controls and ten argument-expanded results. R2 refines the Plan's CP-4/5 into the
local CP-1/2/3 below, adding old-ID and classification regressions. It does not
rewrite the fix design, broaden R2 into R3, retire inherited controls, or claim
S4a enablement. TestDesign ran **zero builds, tests and PCs**.

## Verification design

### Inspection

`SAL` = `tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs`;
`G/` = `server/Infrastructure/Git/`; `H/` = `tests/Antiphon.Tests/TestHelpers/`.
All new helpers remain private in SAL. No new test file or shared fixture is needed.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Plan D-1..8, R2/R3 recipes, dispositions, ownership, performance and checkpoint sections; investigation `e10e6f30073a247c248c413d62a4ea0c977c8d48:docs/investigations/2026-11-06-investigate-s4a-witness-gaps.md`; C2 continuation ledger at `d4fcc1422a3e61f510168320a38af75b9bc6626a`; P's S1..12 definitions at `0037ffbd94ab6d42074d77b31f90015f43ea6a34` | New bindings V-1..7; existing S2..5/S7..10 preserved in the explicit exclusion ledger below |
| R1 design; performance Debug `c318d8edfc97e046bb9a332ec34c76973f9a4f4a:docs/investigations/2026-11-05-debug-s5b-1-performance.md`, measurements and P1..7 | Same structure, serialization of both policy identities, three-row maximum, separate admission and execution |
| `docs/project-context.md`; testing-and-build checkpoint schema, runner, classification, PC execution and restoration; test-performance-standards R1..9; orchestration-loop stage order/D-2 | Test-only scope, numeric budgets, ordinary Review then land then sourced Mutation |
| SAL complete: all 15 existing test bodies, RewriteAsync, FailVerifyAfterWrite, RefFileAsync, AllRefsAsync, SalvageRefsAsync and class metadata | Real rewrite, raw old IDs, write/verify, lifetime, lease and collision worlds -> V-1..7/R-1..2 and inherited ledger; no reason-only preservation verdict |
| `Application/DesktopLegacyWorld.cs` complete; H/SettledRemovalHarness complete | Real Git and isolated DB, production policy/retirement/removal graph, fake clock/settings, independent reader, snapshots and real registration -> V-1..7 |
| H/LandingGitFixture complete; H/LandingSafetyHarness Initialize/BuildServices/Seed/CreateContext/Dispose; H/TestDbFixture assembly startup and cloned-store entry; H/SharedStoreWarmup call-graph warmup | BeforeCommand/AfterCommand boundaries and independent FixtureGit are sufficient; genuine lease and persistent DB; shared template/container/model and per-case clones; no helper changes |
| G/WorktreeReflogSalvage.cs complete; G/GuardedWorktreeRemoval first-pass pre-salvage through bind/move; DesktopLegacyReleasePolicyService tick prune admission and PruneSalvageAsync | Three source tuples, raw read/parse, final membership check -> G-1..7; later content/live/removal checks remain intact; service completion and helper age explain legacy masks |
| LP `Status_that_cannot_read_a_posix_directory_holds_it` and AssertStatusReadHoldsTree; NativeLinuxOnlyAttribute; GitProcessSpawnLimit/ProcessSpawnLimit | Real mode-000 and finally restoration precedent; U5R Linux/non-root admission -> V-4; no subprocess or sudo substitute |
| TestClassificationGuardTests, TestLaneCategoryGuardTests, NativeLinuxPolicyWitnessTests and forward/reverse/hash validation; test-execution-policy rc schema | Exact new method marker requires its policy row, not a class-wide exclusion -> R-3 |
| LiveLaneRunnerTests.WrapperOnlyPaidManifest/Run; ScriptHarness.RunHarnessCaseAsync/Validate; test-live-lane-run.ps1 protected-input digest, full-input and wrapper cases; nightly-policy Get-NightlyPolicyHash | Refresh policyHash and PinnedInputDigest together; offline wrapper witness -> R-3 |
| Antiphon.Tests.csproj and Antiphon.Server.csproj ProjectReference entries | Git-operations tests only. Server depends on Pty, messaging/client, runner contracts, resilience and observability; test build also reaches runner, gateway/Slack, fake providers and fixture/checkpoint/audit children. No changed production downstream consumer; no whole dependent-assembly execution |

All proposed bodies, hooks, assertion locations, selector patches and payloads
**remain binding required after Code**. Record author C and landed L, exact
path/symbol/diff, patch SHA-256, producer/staged DLL hashes, Class.Method/argument,
selector, reached marker and decisive assertion. Source inspection and snippet
hashes are not compilation or execution evidence.

### Delivery inventory

**No new or changed asynchronous delivery path.** R2 changes test witnesses and
classification metadata. The awaited tick/Git/DB calls are not a queue or session
delivery change. There is no new producer, destination queue, persistence handoff,
recipient or delivery/recovery guard in this slice. No request, event, Sent flag,
queue insert or acknowledgement receives delivery credit. Any expansion into
delivery must return to TestDesign for the real queue, busy and already-eligible
recipients, crash/enqueue-failure recovery at each handoff and actual recipient
evidence; session input requires the matching complete UserPrompt transcript.

The synchronous preservation identity is `(repository/common directory, task GUID,
worktree/admin paths, source full ref, P, L, refs/antiphon/salvage/<task-guid-D>/L)`.
Creation intent, successful update-ref, a trace, and DB Complete alone do not prove
salvage: V-1/2 and R-1/2 read the actual ref/OID and reachability independently
after real removal. V-3 demonstrates a write followed by missing durable ref at
verification, not power-loss durability. V-4..7 inspect the actual retained tree,
registration/ref and log/evidence bytes after the refusal.

Substitutes: DesktopLegacyWorld uses a fake clock and mutable settings, but real
Git/filesystem, cloned PostgreSQL and the production service/removal graph. Its
hooks schedule filesystem/ref changes; they must not return fabricated successful
Git results. The separate FixtureGit reader has no hooks. The U5 missing/empty/
malformed inputs deliberately alter history; they prove fail-closed source
custody when evidence is uncertain, not that the product caused the fixture's
log loss. Existing FailVerifyAfterWrite returns a failed command; it does not
prove V-3's successful-but-incomplete verification. No fixture proves crash,
remote recipient, native Windows or full-product atomicity.

### Proves it works now

One `[Test]` per method. Only V-5 and V-7 have Arguments, exactly the strings in
the table. Case IDs, preflight checks and internal assertions are not extra TUnit
results. All are git-operations / **Integration**, inheriting SAL's
`ParallelLimiter<GitProcessSpawnLimit>`; V-4 additionally has a method-level
NativeLinuxOnly marker. All run on an unprivileged Linux process for R2 acceptance.

| ID / case / exact SAL method | Recipe / variant / layer / classification | Expected oracle and reachability |
|---|---|---|
| V-1 / W-U1H / `C1737_HeadReflogOnlyCommitSurvivesRemoval` | Source-exclusive real HEAD log, clean pushed P, rewritten L; end-to-end service/Git integration, portable | Exact direct salvage ref names L and independent rev-list --all includes L after removal; branch log/current HEAD/other refs exclude L and descendants. PC-1 omits only this source tuple |
| V-2 / W-U1B / `C1737_BranchReflogOnlyCommitSurvivesRemoval` | Twin with L only in source branch log; same layer/classification | Same ref and reachability oracle; HEAD log/current HEAD/other refs cannot rescue L. PC-2 omits only branch tuple |
| V-3 / W-U3 / `C1737_MissingVerifiedSalvageRefKeepsTree` | Real write, independent delete at final successful enumeration; service/Git integration, portable | Source directory/bytes, registration, source ref=P and source log remain while exact salvage ref is absent. Final read succeeds and misses L; PC-3 bypasses only final membership |
| V-4 / W-U5R / `C1737_UnreadableRawReflogKeepsTree` | HEAD old-ID-only L; mode-000 after successful listing/path resolution; integration, Linux/non-root | Original source custody and log bytes survive after mode restoration. File.Exists is true and same-process read throws UnauthorizedAccessException/IOException. PC-4 turns read failure into empty success |
| V-5 / W-U5P / `C1737_MalformedRawReflogKeepsTree` | Three arguments: `short-fields`, `bad-old`, `bad-new`; actual malformed record plus valid P record after listing; portable integration | Source custody and exact faulty log bytes remain. Valid P record satisfies later nonempty guard under PC-5, which skips only invalid records |
| V-6 / W-U5M / `C1737_MissingRawReflogKeepsTree` | Move the listed HEAD log to owned evidence path at resolution; portable integration | Source directory/bytes, registration, branch=P and displaced evidence remain; expected raw path stays absent. PC-6 mistakes listed absence for empty success |
| V-7 / W-U5E / `C1737_EmptyRawReflogKeepsTree` | Two arguments: `empty`, `all-zero-ids`; successful listing then empty/all-zero actual HEAD log; portable integration | Source custody and exact altered log remain. All other inputs valid; PC-7 omits only listed-but-empty refusal |

#### BOUNDARY WIRING: common world and independent oracles

1. Use DesktopLegacyWorld.CreateAsync unchanged; AddTaskAsync(ageHours: 60)
   creates one eligible pushed task. Record P before SAL's RewriteAsync creates
   real child L and resets to P. Settings remain Enabled/Execute=true. Do not
   edit authority rows, production gates, object storage or other tasks. Do not
   invoke gc, expire object storage, or rely on immediate object-file loss.
2. Use a new `LandingGitFixture.FixtureGit(Path.Combine(w.Fixture.Root,"home"),
   w.Fixture.TaskId)` for all independent read/write observations. A SAL-local
   Required reader must assert Succeeded before returning Output; the existing
   DesktopLegacyWorld.ReadAsync does not check exit status. Never let a failed
   read's empty stdout satisfy an absence check. For absence require the exact
   documented missing-ref exit (show-ref --verify --hash: 1), not any failure.
3. Resolve each exact raw path with real `rev-parse --path-format=absolute
   --git-path logs/HEAD` in a.Path and `... logs/<a.FullRef>` in Repository.
   Require successful, absolute, single-line, existing, distinct paths inside
   the fixture-owned common/admin directory. Save exact bytes, mode (Linux) and
   tree snapshot. Match hooks by repository/path AND complete argument vector,
   not all rev-parse or all for-each-ref calls. No shared hook is replaced beyond
   SAL's BeforeCommand/AfterCommand properties; preserve BeforeObservedCommand.
4. For deterministic logs write UTF-8 without BOM, LF-terminated valid records
   using the actual OID width: `old new C1737 Fixture <fixture@example.invalid>
   1700000000 +0000\tfixture-message\n` on one line. Z is the all-zero OID.
   This timestamp is log syntax only; it grants no prune/completion authority.
   Parse both raw old/new fields independently in the fixture, and compare both
   real `reflog show --no-abbrev --format=%H` outputs. Confirm current HEAD and
   source ref=P, L is a valid real commit distinct from P, rev-list --all omits
   L and all nonselected sources exclude L and its descendants. P cannot reach
   its child L. Origin refs are captured independently and must remain unchanged.
5. For V-3..7 first decisive assertion after the tick is
   `Directory.Exists(a.Path).ShouldBeTrue("preserved:<case>:source-tree")`.
   Then compare snapshot, registration, exact source OID P, origin, raw/evidence
   bytes as applicable, and only then refusal/state diagnostics. Assertions must
   run before fixture disposal. A missing source directory under a mutant is the
   preservation red; a different reason, hook count or setup exception is not.
6. Restore hooks in finally. Restore modes before reading preserved log bytes
   or disposing the world; if mutation removed the path, do not recreate it or
   throw a restoration error that masks the directory assertion. Restore only
   an existing owned node. Invalid/empty logs remain as installed until observed;
   the missing-log evidence stays outside the removed worktree/admin tree.

The construction removes **overlapping fixture roots**, not a second production
guard. Receipt/content/live gates remain satisfied by clean P, a genuine lease,
unchanged tree/identity and on settings. A raw admin-log fault does not dirty the
tracked worktree or introduce untracked bytes into it. If a different live guard
still refuses the mutant, report the exact unreached boundary to Plan; never
disable that guard to obtain a kill.

#### BOUNDARY WIRING: W-U1H and W-U1B

For the selected log write two valid records `Z L ...` then `L P ...`; for the
other write only `Z P ...`. L therefore appears in the selected log's shown and
raw IDs; the other log shows/contains only P. No ref, current HEAD or other log
contains L. Verify these facts before installing production command observers.
There is no fault injection during the tick for these two methods.

Record each actual source tuple and raw-path call, the candidate rev-list argv
and result, the successful salvage write and final read. Run the real tick.
Establish the post-tick reachability prerequisite independently: source and
set-aside directories absent, registration and source branch absent, and both
original raw logs absent. Require these physical facts before claiming a lost
salvage root; an upstream refusal that keeps the logs is not PC-1/2's kill.
Before diagnostic/state assertions, independently `show-ref --verify --hash
<salvage>` and require Succeeded with message
`preserved:W-U1H:salvage-ref-present` (W-U1B for the twin), exact OID L, directness
(symbolic-ref -q exits 1) and rev-list --all includes L. Then AssertRetiredAsync
and require both original raw-log paths absent and origin unchanged.

PC-1/2 must still finish removal, but lose the exact root and ref reachability;
the first missing-ref assertion is red. Preserve post-tick registration/log and
rev-list observations in evidence even when the assertion aborts. In a mutant,
absence of the omitted tuple is expected, not a setup failure. The preflight
marker `reached:W-U1H:exclusive-head` / `reached:W-U1B:exclusive-branch` proves
the world's isolation; selector-off traces additionally prove each production
source was consulted. Object existence alone is never the oracle.

#### BOUNDARY WIRING: W-U3

Use the HEAD-exclusive world above. Set `wrote` only in AfterCommand after the
exact create for this salvage name returned Succeeded; independently verify L
at that name. At BeforeCommand for the next exact `for-each-ref
--format=%(refname) %(objectname) %(symref) <scope/>` when wrote=true, delete only
that ref with independent real `update-ref --no-deref -d <name> <L>`.
Require delete success and missing-ref exit 1; return null to execute the real
enumeration. No injected output or fabricated exit status. AfterCommand must
observe its exit 0 and absence of the exact name/OID line. Count this cut once.

Capture the real written/deleted/check observations as
`reached:W-U3:successful-verify-missing-ref`. The HEAD log still contains L and
all source custody is unchanged at that cut. After tick, assert source-tree
existence first, then snapshot, registration, source=P, raw log bytes, missing
salvage and `reflog_salvage_failed`; release is not Complete. PC-3 alone makes
the real deletion sequence run with no salvage root. Preserve both successful
enumeration and the post-tick root/log observations in finally-owned evidence.

#### BOUNDARY WIRING: W-U5R/P/M/E

Use HEAD **old-ID-only** L: the selected HEAD log contains one valid `L P ...`
record; the branch log contains `Z P ...`. Both Git listings show P, never L;
both current heads are P and no ordinary ref reaches L. This prevents a listed
new-ID candidate or branch log from rescuing the faulty raw read. R-2 separately
retains the existing old-ID regression.

Install AfterCommand with two states: observe the exact HEAD `reflog show`
Succeeded with nonempty P output, then the immediately associated successful
raw `rev-parse ... logs/HEAD`. Validate the returned path equals the pre-resolved
owned path; **only then** install the fault. Returning from this hook delivers
the real successful path result to LoggedIdsAsync before its actual File read.
Require one cut and emit `reached:<case>:after-list-before-raw-read` with variant.
No timing loop, sleeping writer, status stub, or pre-list fault is acceptable.

- **W-U5R:** Require Linux, effective UID nonzero (read the process's Uid entry
  from /proc/self/status) and demonstrate actual read denial under that same
  test identity. Save mode, set only the selected file to UnixFileMode.None,
  require File.Exists=true and an independent File.ReadAllTextAsync throws
  UnauthorizedAccessException/IOException. Do not change directory permissions,
  unlink/replace the file, run under another identity, or return a fake Git error.
  Root/capability-readable input is fixture unavailable, never a skip credited
  as a passed R2 row. Finally restore mode if present; then assert original
  source and raw bytes. PC-4 returns empty from the catch; it does not pass
  through the later empty-IDs check, so P from the shown/current/branch inputs
  is sufficient and the genuine removal can proceed.
- **W-U5P:** Replace the raw HEAD log with malformed record followed by valid
  `Z P ...`. `short-fields`: a line containing only L. `bad-old`: `not-an-oid L
  <valid suffix>`. `bad-new`: `L not-an-oid <valid suffix>`. Save exact installed
  bytes; assert independently that precisely the named parse predicate is bad
  and the second record yields P. With PC-5 the bad record is skipped and P
  keeps ids.Count>0; there is no invalid OID sent to rev-list. A malformed-only
  file would remain masked by G-7 and is expressly excluded.
- **W-U5M:** Move the file to `Path.Combine(w.Fixture.Root,
  "c1737-displaced-head.log")`, outside the worktree/admin tree. Require source
  raw path absent and displaced bytes equal original. The earlier nonempty
  listing makes listed=true. PC-6's empty return occurs before parsing/G-7.
  Primary red is source-tree removal; the fixture, not the mutant, moved the log.
- **W-U5E:** `empty` writes zero bytes; `all-zero-ids` writes one valid `Z Z ...`
  record. Both pass file read; zero-record or all-zero processing leaves no IDs
  while listed=true from the earlier Git observation. All-zero syntax passes
  every parse predicate. Save exact installed bytes. PC-7 falls through with
  an empty collection and the other safe candidates P permit actual removal.

For all four methods compare source/registration/ref and actual final raw or
displaced bytes **before** `reflog_salvage_failed` and non-Complete release. For
M/E do not assert that L remains reachable: the fixture deliberately removed
that evidence. The asserted invariant is retention of the source's physical
custody until its history can be proved.

### Guards the regression

| ID | Exact existing test / decisive assertion / layer |
|---|---|
| R-1 | SAL.`Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref`: rewritten commit has its exact salvage ref/OID and rev-list --all reaches it after both real trees retire; pushed control gets none. Integration; broad happy path, not independent source attribution |
| R-2 | SAL.`Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref`: reflog show omits L in both logs, raw reset old ID still protects it, then exact salvage ref and ref reachability survive removal. Integration; retain historical S6-oldids binding, no repeat of completed waived PC |
| R-3 | `Antiphon.TestSupport.TestClassificationGuardTests.Registry_matches_compiled_metadata`: no registry errors; `Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`: empty missing/both/methodBoth; `Antiphon.TestSupport.NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute`: report.Success including forward/reverse/hash checks; `Antiphon.Tests.Scripts.LiveLaneRunnerTests.WrapperOnlyPaidManifest`: full-input digest and Required/Excluded sentinels plus all 21 named internal harness checks (one TUnit result) |

The seven V methods are also the new regressions for G-1..7. Base-green is the
expected outcome: these are witness repairs, not a reproduced unmutated product
failure. No old test assertion is removed or relaxed. R-2's inherited old-ID
guard keeps its existing S6 mapping; R2 does not claim another S6 mutation kill.

### Guard inventory

| Guard | Plan ref + safety-critical invariant | Distinct control |
|---|---|---|
| G-1 | R2 W-U1H / ledger 07: include the worktree HEAD reflog tuple and its raw old/new IDs when computing salvage roots | PC-1 |
| G-2 | R2 W-U1B / ledger 07: independently include the source branch reflog tuple and its raw IDs | PC-2 |
| G-3 | R2 W-U3 / ledger 08: successful final enumeration must contain every required exact direct name/OID before source custody is removed | PC-3 |
| G-4 | R2 W-U5R / ledger 10: IO/UnauthorizedAccess failure of raw log read must return unknown/failure, never empty success | PC-4 |
| G-5 | R2 W-U5P / ledger 10: reject a malformed raw record, never silently skip it and discard possible old/new history | PC-5 |
| G-6 | R2 W-U5M / ledger 10: raw log absent after a nonempty Git listing must refuse | PC-6 |
| G-7 | R2 W-U5E / ledger 10: listed entries but no nonzero parsed IDs must refuse | PC-7 |

These are all independently bypassable safety guards repaired by R2, including
the currently unimplemented/unrun bindings. G-4, G-6 and G-7 are separate returns
and must never share a selector. G-5 is the Plan's single malformed-record
disposition with three argument variants, all required; it is not three credited
guards. Removing an individual IsOid term and letting an invalid ID reach Git
would exercise another refusal, not this skip-record defect. Current-HEAD input,
Git command success/path validity, old-ID inclusion, reachability/tip reduction,
write flags, collision, removal authority and prune guards are unchanged
prerequisites/inherited obligations, explicitly reserved below; this inventory
does not assert whole-product mutation completion.

### Positive controls

**All seven PCs and all ten argument results run.** The Plan D-6 author-side
packed proof is additional to ordinary V/R; Review judges source-bound ordinary
evidence and this pending design before land. Formal Mutation runs baseline,
break/red/restore/green on published L afterwards. Author evidence never replaces
that sourced pass. Nothing has executed in this TestDesign.

Each filter below is the entire method selection. Parameterized selectors use
the method prefix with `*`, never literal argument suffixes or a class filter.

| Control | Break guard by selected compiling defect | Exact method filter; min per phase | Expected preservation red |
|---|---|---|---|
| PC-1 | G-1: skip only HEAD reflog tuple, including raw read; keep current HEAD and branch | `/*/*/C1708ReflogSalvageTests/C1737_HeadReflogOnlyCommitSurvivesRemoval`; 1 | `preserved:W-U1H:salvage-ref-present`: missing exact ref after actual removal |
| PC-2 | G-2: skip only branch reflog tuple/raw read | `/*/*/C1708ReflogSalvageTests/C1737_BranchReflogOnlyCommitSurvivesRemoval`; 1 | `preserved:W-U1B:salvage-ref-present` |
| PC-3 | G-3: unconditional success at final membership only; command-success guard stays | `/*/*/C1708ReflogSalvageTests/C1737_MissingVerifiedSalvageRefKeepsTree`; 1 | `preserved:W-U3:source-tree` |
| PC-4 | G-4: return empty IDs from the IO/access catch | `/*/*/C1708ReflogSalvageTests/C1737_UnreadableRawReflogKeepsTree`; 1 | `preserved:W-U5R:source-tree` after actual read denial |
| PC-5 | G-5: continue over the invalid record, retaining other records | `/*/*/C1708ReflogSalvageTests/C1737_MalformedRawReflogKeepsTree*`; 3 | `preserved:W-U5P:source-tree` in each of short-fields/bad-old/bad-new |
| PC-6 | G-6: treat missing raw file as empty success even when listed | `/*/*/C1708ReflogSalvageTests/C1737_MissingRawReflogKeepsTree`; 1 | `preserved:W-U5M:source-tree`; displaced log remains |
| PC-7 | G-7: omit only listed-and-no-IDs refusal | `/*/*/C1708ReflogSalvageTests/C1737_EmptyRawReflogKeepsTree*`; 2 | `preserved:W-U5E:source-tree` in both empty/all-zero-ids |

Temporary helper inside WorktreeReflogSalvage (absent from every committed diff):

```csharp
private static bool C1737R2Mutation(string id) =>
    string.Equals(Environment.GetEnvironmentVariable("ANTIPHON_C1737_R2_PC"), id, StringComparison.Ordinal);
```

PC-1 and PC-2 insert these statements at the top of SalvageAsync's source-tuple
foreach, before its RunAsync; neither matches the current-HEAD rev-parse tuple:

```csharp
if (C1737R2Mutation("PC-1") && arguments[0] == "reflog" && arguments[^1] == "HEAD") continue;
if (C1737R2Mutation("PC-2") && arguments[0] == "reflog" && arguments[^1] == source.SourceFullRef) continue;
```

PC-3 replaces only the final return after the successful final read:

```csharp
return C1737R2Mutation("PC-3") || tips.All(sha => present.Contains(namespaceRefs + sha + " " + sha)) ? null : Failed;
```

PC-4 replaces the return inside LoggedIdsAsync's existing IO/access catch:

```csharp
return C1737R2Mutation("PC-4") ? Array.Empty<string>() : null;
```

PC-5 replaces only the malformed-record if/return:

```csharp
if (fields.Length < 3 || !LandingGit.IsOid(fields[0]) || !LandingGit.IsOid(fields[1]))
{
    if (C1737R2Mutation("PC-5")) continue;
    return null;
}
```

PC-6 replaces only the !File.Exists return inside the try:

```csharp
if (!File.Exists(path))
{
    if (C1737R2Mutation("PC-6")) return [];
    return listed ? null : [];
}
```

PC-7 replaces only the final listed-and-empty guard:

```csharp
if (!C1737R2Mutation("PC-7") && listed && ids.Count == 0) return null;
```

Snippet SHA-256 bindings (UTF-8, LF, exact csharp fence contents including final
LF; PC-1/2 hashes the two adjacent statements together):

| Snippet | SHA-256 |
|---|---|
| Helper | `e4f8ab58c9c16c86bf9c417c36a9bdb8382cbe786121b412d878f03a914ca5cb` |
| PC-1/2 | `b970a41322921f960b56361a337a5a52a60a90fdb359e198d1624447c2a0453f` |
| PC-3 | `0972d4877549ba2d12959cc110d6f4f254b0c86fd5279abf672e2da4ea20d544` |
| PC-4 | `23f029568109eb6995d0c28e0705c4977bcc342e0ec82ced3062a676ee6add23` |
| PC-5 | `79249e67255212cce9047d6783f44ef4e0255fc31c4eefd93e1ad0b3b501f143` |
| PC-6 | `adf4f8fa888c2c8993495071cae50177d4226b7ecee9ac3adabc5f6bc86a3595` |
| PC-7 | `c4cb5aff5d17cc497ceee2d8b456c541a100fb9962b2bc117d5c8705cc5c5603` |

The statements use existing names/types and preserve definite assignment and
every other refusal; actual patch/source/DLL binding and successful compilation
remain mandatory after Code. Exactly one recognized selector per child. Unset
means original behavior. A test must never read the selector to change its own
arrangement or assertions.

Author execution: commit/push clean tests and metadata first; one gated clean
native incremental compile before the cold checkpoint build (Debug P4). Save
original source and patch/hashes in ignored task evidence, then compile the one
temporary logical pack in native default output with UseAppHost=false. All PCs
touch the same production file: run sequentially, one selector active at a time.
Packet A1..A7 each runs just its method off then on, two host invocations; PC-5/7
run every argument. Do not claim simultaneous independent mutations. Restore
the exact source path, remove the helper with that restoration, verify bytes
against C and refresh timestamps, then build restored source once. Restored
packets B1=(PC-1..3), B2=(PC-4..6), B3=(PC-7) use separate exact-method children,
at most three invocations per packet. These are **21 host observations / 30
TUnit results: 10 off, 10 intended red, 10 restored green**. Then run the three
clean ordinary CP rows; no dirty pack is a checkpoint clean-source certificate.

Each leg retains its own TRX, counts, reach marker, assertion failure, patch/
source/binary identity and timing. Zero/skipped tests, build/setup errors,
permission-readable worlds, timeouts, or reason-only red are not kills. A survivor
returns to Plan with the exact remaining guard; no broader bypass or retry.
For PC-3..7, collect the physical post-tick observations before the first assertion
can abort: source and set-aside existence, registration, source ref/OID, both raw
logs, displaced evidence (PC-6) and salvage refs. Accepted red requires actual
completed source removal, not merely a rename into a still-preserved set-aside.
The test's arrangement and assertion do not branch on the selector; the mutation
reviewer checks these observations alongside the named preservation failure.

Formal post-land Mutation uses its exact L snapshot and external evidence root,
one PC at a time, method-scoped baseline/red/restore/green phase drivers from
the testing owner. Fresh isolated build and receipt per phase, no author pack
reuse or cross-PC baseline waiver. All ten argument results are required in all
three phases. Use local inherited children only, await every driver, restore
before final green and never commit/push from the snapshot. Any repair needs
separate Code/ordinary Review/land and a new L. Preserve author and sourced
outcomes separately. No tracked-source edits while any owned driver is in flight.

### Out of scope

- Production fixes, shared helpers/DI/schema/project changes, new test classes,
  R1/R3/R4 witness implementation, whole assembly/lane/native suites, settings,
  deployment, cleanup enablement, tracker changes and land-graph updates.
- U2's resource caps and Plan's closed/reclassified non-preservation bindings;
  a new independent control would need its own Plan. U4/U6 CAS/no-deref/prune
  repairs belong to R3. Formal SourceLanding completion is never inferred from
  historical waived or author evidence.
- Windows native receipt/walker identity, per-entry/listing/restore, real 8.3/
  junction and long-path/order obligations remain Plan N1, including U9/A23
  native implications. Linux permission evidence cannot satisfy them. No desktop
  execution. U8/O-1 (authorization across native retries) and U10/O-2 (name or
  retire the unspecified control) stay separate operator decisions and do not
  block R2.

#### Inherited salvage ledger: retained, not silently added to R2

This explicitly carries the requested S2..5 and S7..10 rows. It is an exclusion
ledger, **not additional R2 CP/PC scope or a declaration that every candidate is
independently killable**. Exact methods are in SAL; a future method-scoped filter
is `/*/*/C1708ReflogSalvageTests/<exact-method>` for each single-result method
named here. All are Integration / real DesktopLegacyWorld, no arguments. No
existing completed green/red is rerun merely to inflate this slice's evidence.

| Historical row / guard and control recipe | Exact case/world and oracle | Reachability / classification / ownership |
|---|---|---|
| S1 whole salvage bypass | Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref; overlapping HEAD/branch rewrite, exact root plus rev-list | Completed waived C2 kill; R-1 ordinary only. Does not isolate G-1/G-2 |
| S6-oldids old-ID omission | Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref; delete commit entries from both logs, L remains only reset old ID, salvage root survives | Completed waived C1 kill; R-2 ordinary only. Distinct from HEAD/branch tuple omission |
| S2 ignore SalvageAsync failure in GuardedWorktreeRemoval | Salvage_failure_refuses_the_removal; direct ref at task namespace blocks child creation, all source tree/registration/branch bytes must remain | Existing candidate/unrun. Required salvage result reaches caller; other removal proofs valid. Parent blocking ref itself reaches L, so claim source custody, not disappearance of L |
| S3 pass null live callback to salvage | Late_switch_at_the_salvage_write_writes_no_ref_and_keeps_the_tree; flip Execute at exact write; no salvage ref, source retained | Existing candidate/unrun. Later live removal guard may retain tree even after unauthorized ref write; no-ref side-effect assertion is the binding, not an assumed tree-loss kill. Keep separate from global child-start gate |
| S4 omit helper ref-age check | Age_pruning_keeps_young_salvage_refs; at day29 keep, day31 delete | Existing unvalidated candidate. At day29 service completion-age may exclude scope before helper age; ordinary green is not proof of that first helper guard. Requires its commissioned mutation reachability classification, no new R2 repair/credit |
| S5 prune despite execute=false at outer tick | Age_pruning_keeps_young_salvage_refs; off at day31 keeps root, later on removes | Existing unvalidated candidate. Inner live checks remain; outer-only bypass is not promised destructive. Preserve original control definition and unrun state; never remove live checks to credit outer guard |
| S7-lease continue with null lease | Prune_needs_the_repository_lease; completed aged task, genuine lease held elsewhere, root remains; released lease control prunes | Existing candidate/unrun. Completion, helper age, direct-name/OID and live checks satisfied; held lease is distinguishing barrier |
| S8-completion mark every scope prunable | Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion; verification fails after first write, retry31d later completes, same tick/29d retains, 31d-after-completion prunes | Existing candidate/unrun. Ref's original write is already older than retention; helper age is satisfied when service completion fence distinguishes |
| S8-completion additional unfinished binding | Unfinished_release_keeps_its_salvage_ref_past_the_retention; MaxRefusedAttempts=1, one unfinished/exhausted release and completed control; day31 retains only unfinished root | **Mandatory second S8 method remains pending.** Both real ref timestamps old; completed control proves prune reached; require unfinished root/OID before reason. Never discharge with aged-retry method alone |
| S9-prunegate original compound live+ambient bypass | Late_switch_at_the_prune_delete_keeps_every_salvage_ref; two completed aged roots, Execute flips at first delete, all refs stay, cut fires once | Existing compound candidate/unrun. Do not split historical kill attribution into two guards or combine with global switch mutant; future isolated controls need their own design |
| S10-collision original compound collision/flags revert, direct binding | Direct_salvage_collision_refuses_and_keeps_the_existing_ref; static direct salvage name holds foreign valid master OID, source tree/branch kept and name/OID unchanged | Existing candidate/unrun. Explicit symbolic probe returns1 for a direct name, so old compound control can reach write. This is not isolated CAS/no-deref evidence; R3 U4C/U4N own those |
| S10-collision additional symbolic binding | Symbolic_salvage_collision_refuses_and_never_moves_another_branch; static symref points to precious branch, preserve branch/ref bytes and source | Plan row13 **b/closed as independent preservation binding**: explicit symbolic probe returns0 and refuses before older compound write mutant. Keep historical unrun/masked provenance; no invented S10 symbolic kill or S10+S12 combination |
| S11-prunesym / S12 whole dangling probe | Prune_never_deletes_through_a_symbolic_salvage_ref / Dangling_symbolic_salvage_collision_refuses_and_keeps_the_symbolic_ref | Completed waived C2/C1 kills, no repeat here. Narrow S12 exit0-only bypass remains reason-only because exit-not1 guard stays |
| Other unchanged SAL regressions | Off_switch_writes_no_salvage_ref / Prune_keeps_a_dangling_symbolic_salvage_ref | Existing admission/preview and dangling-prune coverage retained; unchanged methods outside this closed repair selection, no new control or rerun |

U3 failed-verification-read candidates remain
`Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion` and
`Unfinished_release_keeps_its_salvage_ref_past_the_retention`, through the existing
FailVerifyAfterWrite. They are present/unvalidated, not missing, not equivalent
to V-3 and not new R2 mutation credit. Preserve all historical reports unchanged.

#### Classification, serialization and R3 dependency

Keep SAL class Integration, GitProcessSpawnLimit and its existing class timeout.
No method-level Unit/Integration conflict, new Slow category, class move or
shared marker edit. New body limits below refine only the new methods. Add only
to V-4:

```text
[NativeLinuxOnly("Linux-only by construction: mode-000 raw-reflog denial requires an unprivileged Linux process; Windows needs separate permission evidence.")]
```

Use the existing attribute namespace. Add the exact rc exclusion:
suite `antiphon`, class `C1708ReflogSalvageTests`, methods containing only
`C1737_UnreadableRawReflogKeepsTree`, platform `windows`, owner `CARD-1737`, reason
equal to the marker above beginning `Linux-only by construction:`. Keep the
method Required on Linux. Do not exclude SAL wholesale or silently skip Linux
root/capability-readable execution. A suitable Linux runner is a prerequisite;
this inspection's uid was 1654, which is not proof of future runtime denial.

**R1 lands first; R2 metadata integration follows.** R1 owns PC/NF/GC test files;
R2 owns only SAL, so test bodies are disjoint. Both touch
`tests/test-execution-policy.json` and `scripts/test-live-lane-run.ps1`: complete
Code branches are not file-disjoint. R2's final integrated source must include
R1's exact NativeLinuxOnly exclusion, then add R2's own row and recompute
`policyHash` via Get-NightlyPolicyHash. After saving that policy, recompute
`$script:PinnedInputDigest` via `test-live-lane-run.ps1 -PrintInputDigest` against
the same integrated protected inputs. Commit both identities with the row and
marker; no copied hash, changed algorithm, weakened sentinel or old R2 pin.
R-3 certifies that combined source. If the ordered source cannot be supplied,
serialize Code dispatches; do not merge/rebase/reset a delegate branch or edit a
branch under Review. Landing owns rebasing; any resulting policy/hash change
requires its proper source-bound ordinary verification, not stale receipts.

**R3 starts after R2 is integrated and SAL leaves Review.** It consumes R2's
private real-source/log-path, independent-reader, exact-hook and preservation
helpers plus unchanged RewriteAsync/RefFileAsync/SalvageRefsAsync. R3 must inspect
their landed bodies and rebind its late collision/prune fixtures to that SHA;
it must not cherry-pick guessed helper names or write SAL concurrently. R2 helper
names/implementation are binding required after Code; their behavioral contract
is frozen above. R3 retains every R2 test, and if it changes a local helper it
selects affected R2 consumers once at the changed source. No shared-world,
LandingGitFixture, DI or project edit is assumed; a necessary expansion returns
to Plan/TestDesign for consumer closure before implementation.

### Checkpoints

Closed ordinary scope: **7 new methods / 10 results + 2 retained methods + 4
metadata/policy methods = 16 TUnit executions**. One source-bound isolated build,
three serial rows, one exact filter per row, all results passed and zero skips
on Linux. CP-2/3 reuse CP-1's build at the same C and properties; the owner schema
explicitly permits that binding. No full-class or inherited S sweep is implicit.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | R2 | tests/Antiphon.Tests -> bin-c1737-r2/ | r2-source-roots | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_HeadReflogOnlyCommitSurvivesRemoval*)\|(C1737_BranchReflogOnlyCommitSurvivesRemoval*)\|(C1737_MissingVerifiedSalvageRefKeepsTree*)\|(Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref*)\|(Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref*) | V-1,V-2,V-3,R-1,R-2 | exactly these 5 methods, 5 passed, 0 failed/skipped | 5 | 15 | true |
| CP-2 | R2 | CP-1 | r2-raw-evidence | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_UnreadableRawReflogKeepsTree*)\|(C1737_MalformedRawReflogKeepsTree*)\|(C1737_MissingRawReflogKeepsTree*)\|(C1737_EmptyRawReflogKeepsTree*) | V-4,V-5,V-6,V-7 | exactly 4 methods / 7 argument results specified above, 7 passed, 0 failed/skipped | 7 | 5 | true |
| CP-3 | R2 | CP-1 | r2-classification-policy | /*/*/(TestClassificationGuardTests*)\|(TestLaneCategoryGuardTests*)\|(NativeLinuxPolicyWitnessTests*)\|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)\|(every_test_class_is_tagged_unit_xor_integration*)\|(Policy_rows_require_pinned_attribute*)\|(WrapperOnlyPaidManifest*) | R-3 | exactly the 4 R-3 class/method pairs, 4 passed, 0 failed/skipped | 4 | 4 | true |

CP-3's Cartesian OR intentionally selects only the four pairs in R-3, not four
classes. Check suffix/homonym expansion at Code and compare every fresh TRX's
Class.Method/argument with the exact roster, not just Min. SAL's two retained
methods and all metadata methods are single-result. No C1737 method existed at
inspection. New case assertions do not inflate Min.

#### PERFORMANCE MANIFEST

Apply Debug 5ae98d31 P1..7: one native incremental compile first, no repeated
green row, at most three rows per run, explicit phase bounds, each driver takes
its own host slot. Import then pin the ignored YAML; execute the **manifest
positional argument**, since run --plan would reimport and lose the pins.
Tool bootstrap if unavailable is a separately reported gated native build.

```text
dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false -- import --plan docs/superpowers/testdesign/2026-11-06-card-1737-r2-testdesign.md --out .antiphon/c1737-r2-checkpoints.yaml
```

Pins: `build.slotWaitMinutes: 10`, `build.properties.UseAppHost: false`,
`timeouts.rowMinutes: 28` (build ceiling), per-row `timeoutMinutes` CP-1=6,
CP-2=6, CP-3=6, all serial; TEMP=/tmp, selector unset. Use the actual CP ID for
ANTIPHON_TEST_PHASE_GROUP_ID and enable existing CARD-1514 timing. Direct native
build/test drivers run through `pwsh -NoProfile -File scripts/build-slot.ps1
-Label <label> -SlotWaitMinutes 10 -- <command>`, with no -o/OutputPath and the
same UseAppHost=false on build and --no-build execution. Do not wrap a loop in
one lease. The checkpoint launcher delegates its child build/row leases itself.

```text
dotnet run --project tools/Antiphon.Checkpoints --no-build -p:UseAppHost=false -- run .antiphon/c1737-r2-checkpoints.yaml --rows CP-1,CP-2,CP-3 --expected-source-sha <C> --total-timeout 46m --hard-wall-timeout 86m --total-timeout-override-reason "CARD-1737 R2: Debug 5ae98d31 phase budget, 28 build + three 6min rows; admission separate"
```

46 charged minutes =28+6+6+6; 86 hard wall =46+4x10 admission. The explicit
override replaces the default derived 58min (2x24+10), and importer floors are
not p95 evidence. Caps are deadlines, not expected runtimes. The checkpoint
executor owns long work; await run/wait to terminal, using <=60s observations
and exit75 as still-running. Do not end a delegate turn with an owned run alive.
Exit4 slot timeout is not-run, never a reason for NoSlot or an unleased retry.
No green rerun for timing or an additional pre-land PC-clean claim.

| Phase | Provenance and bound |
|---|---|
| Admission | Debug n=39, mean46.5s, p95 255s, max405s; finite 10min per acquisition, recorded separately |
| Isolated build | Debug n=11 successful cold builds, p50 431.6s, empirical p95/max817.1s; estimate12min, cap28min rounded from 2x max. CP-1 includes this once |
| Native clean compile/restored build | Plan B2/C2 comparators144.2/119.4s, small sample; estimate3min each, provisional5min execution cap; native default outputs only |
| Ordinary hosts | CP-1=3min, CP-2=5min, CP-3=4min estimates; caps6min each, provisional CARD-1737. Debug short-host max42-85s, metadata nearby max181s, startup up to67s; first required exact-roster runs calibrate |
| Exact PC hosts | PC-1..4/6 estimate1min each, PC-5 1.5min (3 arguments), PC-7 1.25min (2); cap3min each. C2 SAL hosts22-37s observed for different methods; new cases unmeasured, no invented p95 |
| New bodies | V-1/2/4..7 40s per result; V-3 60s; Plan simple/multi-pass provisional CARD-1737. Existing SAL180s class limit is inherited, not a new justified body budget; do not widen it |
| Fixture setup | One shared PostgreSQL container/readiness/model/migration template per host/session; cloned DB per world and immutable Git seed template with owned mutable refs/trees. Readiness60s, migrations/seed30s, clone5s expected; no AppHost/web/runner bootstrap. Do not introduce extra containers per argument |
| Calls and teardown | Bind new local Git calls to cancellation/deadline10s, DB/hook work5s where exposed, process exit/drain10s and teardown15s provisional; preserve stricter existing deadlines. Do not alter shared helper timeouts. Wrapper retains existing30s/10s bounds and ProcessSpawnLimit |

Record each result, run/source/build, host/OS/load, category/roster, acquisition
wait/queue position, build, startup, template/clone, body, IO/hash/log-mode/Git/DB
and teardown spans. Missing subphases stay unknown; HTTP is not applicable, not
a measured zero. Separate wall, nested totals and unattributed remainder. Keep
ordinary receipts and unedited CHECKPOINT lines under ignored
`.antiphon/checkpoints/<run-id>/`, author packs under a separate ignored task
root, and sourced evidence outside its snapshot. Code/Review run
scripts/check-evidence-diff.ps1 over assigned base..HEAD.

The normal ordinary estimate24min + mean admission3.1min is below30min.
Contended builds/admission can exceed30min: retain exact selection/source/run/
host/load/phase evidence and request the required performance Debug disposition;
the cited Debug is not an exemption. Author/formal PC cycles are bounded packets
per control, not one giant scheduled run. No project extraction or fixture
refactor is commissioned (CARD-1546 remains separate). Scoped ordinary evidence
does not claim a scoped rc qualification gate.

### Cost

All numbers are **estimated minutes**, informed by cited historical samples;
no run was measured here. Floors include execution/setup as stated; queue wait
is additional and caps are not floors. Filters are the exact CP and PC selections
above, never whole classes.

| Work | Numeric floor / reservation |
|---|---|
| Ordinary V/R (Code) | **24 = CP-1 15 + CP-2 5 + CP-3 4**; 12 isolated build +12 host/setup/body/teardown, no double count |
| Native clean compile (Debug P4) | **3**, once before the packed/cold work; explicitly listed extra driver reason: catch compile errors cheaply |
| Optional checkpoint bootstrap | **3**, one gated native build if a matching tool is unavailable; otherwise0 |
| Additional commissioned author proof | **41.75 =12 packed build +3 restored build +3x7.75 method-host phases +7x0.5 patch/binding/restoration accounting**; 7.75=5x1 +1.5 +1.25 |
| Formal PC-1 cycle | **39.5 =3 isolated phase builds x12 +3 method hosts x1 +0.5 binding/restoration**; exact PC-1 filter |
| Formal PC-2 cycle | **39.5**, same components, exact PC-2 filter |
| Formal PC-3 cycle | **39.5**, same components, exact PC-3 filter |
| Formal PC-4 cycle | **39.5**, same components, exact PC-4 filter |
| Formal PC-5 cycle | **41 =36 builds +3x1.5 hosts +0.5**, all3 variants each phase, exact PC-5 filter |
| Formal PC-6 cycle | **39.5**, exact PC-6 filter |
| Formal PC-7 cycle | **40.25 =36 builds +3x1.25 hosts +0.5**, both variants each phase, exact PC-7 filter |

**Post-land Mutation floor =278.75 =5x39.5 +41 +40.25**. This deliberately
reserves the owner's fresh isolated build per baseline/red/restored phase:
21 builds +21 method hosts, 30 result observations. The historical author pack
does not waive formal phase provenance. A 14min cold-build scenario instead of
12 raises Mutation to **320.75min** (+21x2); discovery/repairs are additional.
One control's three formal phases are separate bounded phase runs; expected
full-cycle cost above30min carries the cited build/admission Debug disposition,
and any individual run observed above30min needs a fresh Debug follow-up.

Combined verification/setup reservation **350.5min =24 ordinary +3 compile
+3 bootstrap +41.75 author +278.75 Mutation** (347.5 without bootstrap).
Ordinary Review of the same scope adds24, total **374.5min** before authoring and
admission. Code alone reserves **71.75min** verification/setup plus the Plan's
150-210min authoring, consistent with its **240-330min** Code reservation once
routine admission/analysis is included. This does not promise a 90min Code task.

Acquisitions: ordinary4; author23 (2 builds +21 hosts); native compile1; optional
bootstrap1; formal Mutation42 (21 builds +21 hosts). At the historical mean
46.5s, additional admission scenarios are **3.1,17.825,0.775,0.775,32.55min**,
respectively, **55.025min total** with bootstrap. Worst finite admission bounds
are40,230,10,10,420min across their separate packets, not one loop-wide lease.

Savings: ordinary one build instead of three saves **24min**; author one pack
and one restored build instead of seven pack/restored pairs saves **90min =
6x(12+3)**. Method-scoped author phases retain every variant. Formal Mutation
claims **zero packed-build savings** because the owner's independent phase
provenance remains required. No historical greens are repaid for measurement.

### Binding and handoff

Before handoff: **guards=7, mapped=7, missing=0, duplicate PC maps=0**.
**All PCs must run**, author off/red/restored and formal post-land cycles;
executed here=0, pending=7 controls/10 argument results. Numeric floors: ordinary
Code24min; additional author41.75min; native compile3min; optional setup3min;
Mutation278.75min. Source-bound payloads remain binding required after Code.

Code commits/pushes tests and both metadata identities, produces all30 author
observations and16 ordinary results with exact rosters and clean receipts, then
returns **next: review**, with all7 sourced PCs pending. Final tracked changes
contain no production mutation or selector. Review judges before land; formal
Mutation verifies published L afterwards. Absent evidence is incomplete under
D-2, never an advisory green. Unverifiable seam -> Plan; an actual human choice
-> Decide. R1 metadata first and R3 SAL ownership afterwards are dispatch
dependencies, not requests for new permission for already-authorized R2 work.
