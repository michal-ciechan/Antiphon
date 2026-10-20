# CARD-1383: union merging for the Slow registry

Date: 2026-10-20

Stage: Plan; TestDesign remains a separate dispatch.

Target: master only; no deployment or rc activation.

Inspected source: `bf9d96db2449bc35dab3c7d1c168c0ce9e78359c`.

## Outcome and scope

Make independent additions to
`tests/Antiphon.Tests/slow-tests-allowlist.txt` merge through the existing landing
rebase, while the existing Unit metadata guard rejects duplicate classes and
structurally detached reasons. Add one exact-path `merge=union` attribute and
strengthen the registry layout validation used by
`TestClassificationGuardTests.Registry_matches_compiled_metadata`.

No new merge driver, server configuration, landing algorithm, sorting requirement,
or registry storage format is needed. Other projects' registries and Markdown
checkpoint tables remain outside this change. The brief's reported six conflict
repairs motivate the work; this plan does not claim to have audited those lands.

## Ground truth

| Card assumption | Inspected code / observed behavior | Design consequence |
|---|---|---|
| Appending two records creates a conflict. | No registry merge attribute exists in `.gitattributes`. A scratch rebase using the production arguments conflicted on two different appended records without the attribute. | Add the exact-path attribute. |
| Registry order may require sorting. | `tests/Shared/TestClassificationMetadata.cs`, `ReadRegistry` and `AssertRegistryMatches`, match names without an ordering constraint. Both `SlowTestTripwire.LoadAllowlist` and `scripts/test-duration-tripwire.ps1` ignore comments and blank lines. | Preserve order as produced by Git; do not sort or regenerate the file. |
| A duplicate-class guard needs adding. | `AssertRegistryMatches` already uses a case-insensitive `seen` set and emits `duplicate <name>`. `TestClassificationPolicyTests.C487_G062("duplicate-fqn")` covers it. | Retain that enforcement and reuse its narrow test; do not add another duplicate checker. |
| A reason must already be immediately above its row. | `ReadRegistry` skips blank lines without clearing `pendingReason`, overwrites it on another comment, and drops it at EOF. The existing reason-content check cannot detect those detachments. | Add structural validation before metadata matching. |
| Union preserves every reason/row block. | Distinct pairs survived the probe. Identical reason lines for different classes collapsed to one reason followed by two rows. Different reasons for the same class collapsed to two reasons followed by one row. Both rebases returned success. | Validate both directions: every row has an adjacent reason and every reason belongs to the immediately following row. |
| The attribute in the source commit is enough during landing. | `AgentTaskLandingProtocol.cs:228` runs `git -c rebase.autoStash=false -c rebase.updateRefs=false rebase <target-sha>` in a prepared land worktree. The target-only attribute case worked; an attribute introduced only by the replayed commit did not resolve that commit's conflict. | Land the rule on master before relying on it for subsequent cards. No promise to rescue this change's own first landing. |
| The server needs a configured union driver. | `LandingGit.cs` launches real Git with an argument list. Git has a built-in union driver. The scratch probe succeeded with system/global Git configuration disabled and no `merge.union.driver`. | Attribute only; no host driver registration. |
| All current comments are record reasons. | At the inspected SHA there are 155 class rows, 158 comment lines and 14 blank lines. Every class has a comment immediately above it. Three comments are file instructions: the opening history line and the instructions preceding the CARD-0701 and CARD-0665 reasons. | Mark those three instructions as header comments and put them together at the top; preserve every class and its reason. |

Relevant owners: [testing and builds](../../testing-and-build.md),
[orchestration](../../orchestration-loop.md), and
[project conventions](../../project-context.md).

## Decisions

**D-1 — Use built-in union for this exact file.** Add:

```gitattributes
tests/Antiphon.Tests/slow-tests-allowlist.txt merge=union
```

Git documents union as a built-in file merge driver which combines conflicting
lines, with result ordering requiring inspection. Custom driver configuration
is a different mechanism. See [Git attributes](https://git-scm.com/docs/gitattributes#_built_in_merge_drivers).
The measured production-shaped rebase supports using the attribute alone.
Reject a custom driver because it would require configuration on every landing
host; reject sorted regeneration because order is immaterial and it would not
remove concurrent-edit conflicts by itself.

**D-2 — Extend the existing metadata guard, narrowly.** Add layout diagnostics in
`TestClassificationMetadata.AssertRegistryMatches` only when `repositoryMode`
is true and the inspected assembly is `Antiphon.Tests`. Its existing
`Registry_matches_compiled_metadata` test then enforces the new contract wherever
that method is selected. Keep the public `ReadRegistry` behavior compatible,
the existing duplicate/name/category checks, and non-repository mode unchanged.
The helper already has an assembly-specific `Antiphon.Tests` lane-XOR rule.

Read one file snapshot for layout and entry parsing; factor a small private
line parser if needed, without expanding the public API. Return diagnostics
through the existing error string rather than throwing on malformed content.
Reject a second independent duplicate guard or a parallel CI-only validation
script: the existing guard is the appropriate enforcement point.

**D-3 — Make headers distinguishable from reasons.** Keep the comment-plus-FQN
format. A `## ` comment is a file header, permitted only in the initial header
region. A single-`#` comment is one nonempty reason and must be immediately
followed by its class row. Move the three existing instructional comments into
the initial `## ` header block. Blank lines may separate complete records or the
header from the first record, but never a reason from its row.

The layout validator must reject:

- A row without a single-`#` reason on the immediately preceding physical line.
- A reason followed by a blank line, another comment, or EOF.
- A header after the record region begins, including a header interrupting a pair.

Keep the existing reason-content validation. Report useful physical line numbers,
the class when available, and stable codes such as `orphan-reason`,
`missing-adjacent-reason`, and `header-after-entry`. Preserve case-insensitive
duplicate detection. LF and CRLF inputs, final newlines and blank lines between
complete pairs remain valid.

Reject inline reasons, JSON, one file per class, or class identifiers embedded in
every reason: those require a larger migration and reader changes. This guard
proves structural attachment, not that arbitrary free-text reasons are
semantically correct for their classes. Exchanging two complete, well-formed
pairs' reasons cannot be inferred from the existing format.

**D-4 — Prove the real Git boundary without changing landing code.** Add a small
Integration test class using fixture-owned repositories and the production
`LandingGit` process implementation. Use the exact rebase arguments above and
the repository's actual `.gitattributes` bytes; do not hard-code the attribute
solely inside a fixture, which would let a missing production rule pass.
Use `LandingGitFixture.FixtureGit` or its established configuration-isolation
pattern so no user/global driver is needed. Put the attribute on the target
before replay; include the case where the source branched before that rule landed.
Do not change `AgentTaskLandingProtocol`, `DelegationWorktreeService`, or host
Git settings.

Git permits a configured rebase backend; see
[Git rebase configuration](https://git-scm.com/docs/git-rebase#Documentation/git-rebase.txt-rebasebackend).
The current checkout has neither an explicit backend nor a union-driver override.
The scratch default and explicit `apply` cases both succeeded for distinct pairs.
This is evidence for the named fixture, not a guarantee about every Git version
or arbitrary host configuration. No backend pin is required for this card.

**D-5 — Keep execution narrow and platform independent.** Ordinary work is the
named Unit metadata methods and the named Integration Git methods. No whole-Unit,
full assembly, live server, PostgreSQL, PTY, browser or deployment checkpoint.
The Integration class has `[ParallelLimiter<ProcessSpawnLimit>]`, bounded owned
Git processes and cleanup. A Slow classification must follow measured cost;
do not add a registry row merely because a fixture launches Git.

`GET /api/runner-defaults` and `GET /api/session-runners` were read on 2026-10-20.
Defaults revision 2 resolves to an eligible Linux runner; eligible Windows
capacity also exists. These are observations, not placement pins. Re-read at
dispatch, omit `-Runner`, and omit `-Platform` for a fresh task. Use
`-Platform Any` only when removing an inherited OS pin. No fleet address belongs
in this plan.

**D-6 — TestDesign is next.** The brief did not fold that stage into Plan.
TestDesign must append `## Verification design`, the exact closed
`### Checkpoints` manifest, nonzero execution floors and cost before Code.
These are implementation decisions; no unresolved product choice or approval
is needed to finish this plan.

## Slices and tests

### S1 — Commit runnable red witnesses

Files:

- `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs`
- New `tests/Antiphon.Tests/Infrastructure/SlowTestRegistryMergeTests.cs`

Add focused tests against the existing `AssertRegistryMatches` API. Proposed
methods are `C1383_Detached_reason_is_rejected` (blank, overwritten, EOF),
`C1383_Header_after_records_is_rejected`, and
`C1383_Valid_reason_pairs_are_accepted` (explicit headers, pair separators,
line-ending variants). Use real compiled Slow class names and assert the exact
layout diagnostic for each damaged fixture. A partial registry legitimately
has unrelated metadata errors: do not call that an expected clean result or
let those unrelated errors satisfy the layout assertion. Include a full-registry
fixture with zero errors for the positive case. Dispose temporary files.

Proposed Integration methods:

- `C1383_Rebase_preserves_distinct_appended_pairs`: copy the real attributes
  into the scratch target; append two different reason/FQN pairs on divergent
  histories; rebase with production arguments; assert success, no unmerged
  index/conflict markers, and each exact pair present once. Cover attribute in
  the common base and attribute added only on the target after the fork.
- `C1383_Union_collapsed_lines_are_rejected`: exercise identical reasons for
  different classes and different reasons for the same class; require successful
  Git rebase followed by the relevant guard diagnostic. Do not treat successful
  Git exit as a valid registry.

Prefer fixture data formed from the complete real registry with two known Slow
entries removed and independently re-added, so valid merged results can also pass
the actual metadata guard. Assert exact pair membership without requiring
left-before-right ordering. Keep every repository and child owned by the test.

Commit and push the runnable tests before fixes. The new rebase success assertion
must fail on the absent repository attribute, and the new malformed-layout
assertions must fail on today's permissive reader. Build/fixture failures or zero
selected tests are not red witnesses. An existing behavior such as duplicate
rejection does not need a deliberately broken implementation in this Code slice.

### S2 — Attribute, layout enforcement and format instructions

Files:

- `.gitattributes` — exact-path attribute and short rationale.
- `tests/Shared/TestClassificationMetadata.cs` — the D-2/D-3 layout validation.
- `tests/Antiphon.Tests/slow-tests-allowlist.txt` — only the three header comments'
  relocation/marking, plus short format guidance; no class deletion, reordering
  or change of the 155 recorded reasons.
- `docs/testing-and-build.md` — document this registry's header/pair rule,
  exact-file union policy, existing guard and first-landing boundary beside the
  Slow-registry guidance.
- S1 test files, only if a fixture needs correction with its outcome explained.

Commit and push this slice, then run the exact narrow green selections. Reuse
`TestClassificationPolicyTests.C487_G062` (two argument results) and
`C487_G064` (one result) for existing duplicate/simple-name and missing-reason
behavior. Run `TestClassificationGuardTests.Registry_matches_compiled_metadata`
against the complete repository, in addition to the new methods. This also
checks that the header-only registry migration lost no compiled Slow class.

TestDesign should add a targeted non-repository-mode control to confirm that
the new strict layout opt-in leaves that existing compatibility mode unchanged.
The shared helper's behavior for other assemblies remains unchanged by its
explicit assembly predicate; no other registry adopts union in this slice.

## Checkpoint scope for TestDesign

This is a selection brief, not an executable checkpoint manifest.

| Intended checkpoint | After | Lane | Required selection / outcome |
|---|---|---|---|
| Red witnesses | S1 commit | Unit metadata + Integration real Git, Any OS | One assertion-level red witness for each newly enforced behavior; method-scoped filters, with the original missing-rule rebase failure distinguished from fixture failures. |
| Narrow green metadata | S2 commit | Unit, Any OS | New C1383 layout methods, existing `C487_G062`, `C487_G064`, compatibility control, and exact `Registry_matches_compiled_metadata`; all selected executions pass. |
| Narrow green merge | Same S2 commit | Integration real Git, Any OS | The two new Integration methods and their explicit argument variants; all pass using the same built output as the green metadata row. |

Keep one red then one green per behavior. Batch independent methods where the
pinned filter syntax permits it; avoid full `TestClassificationPolicyTests/*`,
because unrelated methods compile and run probe assemblies. No full Unit lane.
The final manifest must name exact filters and expanded counts rather than count
internal fixture assertions as tests.

Use the checkpoint tool and host build-slot gate per the owner. Commit each
slice before execution, build into a fresh `bin-<name>/`, reuse it only for the
same committed slice group, await all runs, preserve receipts in ignored output,
and remove owned alternate outputs afterward. Code and Review run
`scripts/check-evidence-diff.ps1` over the complete assigned base-to-tip range.
No behavior is claimed green from this Plan stage.

## Planning evidence and limitations

A foreground, slot-gated scratch experiment ran Git 2.47.3 with temporary
repositories, fixture identity, `GIT_CONFIG_NOSYSTEM=1`,
`GIT_CONFIG_GLOBAL=/dev/null`, and no configured union driver. Both histories
started with a reason/class pair and appended records to the exact target path.
Only the explicit-apply case configured `rebase.backend`. All seven operations
finished; temporary repositories were removed. This was design evidence, not
TUnit/checkpoint acceptance or a production land.

| Probe | Git exit | Observed result |
|---|---:|---|
| No attribute | 1 | Expected append conflict with markers. |
| Attribute in common base | 0 | Both distinct reason/row pairs survived. |
| Attribute only on target | 0 | Both distinct pairs survived, including the old source history. |
| Attribute only in replayed source commit | 1 | Conflict remained; first-landing limitation. |
| Identical reason, different rows | 0 | One reason, then two rows: invalid adjacency. |
| Different reasons, identical row | 0 | Two reasons, then one row: orphaned reason. |
| Common-base attribute, explicit apply backend | 0 | Both distinct pairs survived. |

Duplicate rows already have a compiled-metadata guard, but union can coalesce an
identical row and leave its extra reason. The new bidirectional adjacency rule
is necessary even with that existing duplicate guard.

No server/desktop Git version was remotely executed. Durable regression evidence
comes from the subsequent Code checkpoint, through real `LandingGit`; no full
database-backed land run is justified by this attribute/test-only change.

## Landing and completion

Land the reviewed change to master through the normal land operation. Once the
attribute is present on the target, later source histories can benefit even if
they forked before it landed. The header migration or unrelated concurrent edits
can still make this initial landing conflict; this plan grants no target merge,
task-branch rebase or repair exception.

Union is not an approval for same-class or same-reason concurrent edits: the
metadata/layout guard must pass on the resulting tree when verification runs.
This card does not add an unconditional pre-push hook or change landing's
verification selection policy. It reduces mechanical append conflicts and
adds a registry-quality gate to the existing selected Unit guard.

Completion requires the exact attribute, passing named guards and real-Git
fixtures, preserved registry membership, narrow recorded checkpoints, ordinary
Review and confirmed master publication. No session/input-wait behavior changes,
so CARD-1083's parking/release checklist is not applicable.

## Verification design

TestDesign, 2026-10-20, inspected at
`5328fdc15bbde09b6424a2187743f6031195e68b`. This section appends the executable
verification contract to D-1 through D-6 and S1/S2; it does not replace that fix
design. Target remains master only. No build, TUnit execution or PC execution is
claimed by this design stage.

### Inspection

- `tests/Shared/TestClassificationMetadata.cs`: read `Read`, `ReadRegistry`,
  `AssertRegistryMatches`, `FindRegistryPath`, `Format` and their category/method
  helpers | layout, duplicate, reason-content, assembly/mode boundaries ->
  V-1 through V-6, R-1 through R-6.
- `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs`: read the
  class, particularly `C487_G062`, `C487_G063`, `C487_G064`, `WriteTempRegistry`,
  `RunProbeAsync` and `RepoRoot` | reuse existing duplicate/simple-name and
  missing-reason assertions; new cases go beside them. Exclude the probe-building
  methods: these cases need no child compilation or whole-class selection.
- `tests/Shared/TestClassificationGuardTests.cs`: read
  `Registry_matches_compiled_metadata` | actual repository/compiled-metadata
  wiring -> V-6, R-6. This shared source is linked into Antiphon.Tests.
- `.gitattributes` and `tests/Antiphon.Tests/slow-tests-allowlist.txt`: read the
  actual rule bytes and registry records/instruction comments | exact-path
  scope, complete-registry fixtures and migration -> V-3, V-6 through V-9.
- Nearest fixture for the new Integration file:
  `tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs`, including initialization,
  `RequiredAsync`, `DisposeAsync` and `FixtureGit.ConfigureProcess/RunAsync`;
  read representative fixture consumers in
  `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs` | owned real Git,
  configuration isolation, trace and cleanup -> V-7 through V-9.
- `server/Infrastructure/Git/LandingGit.cs`: read `RunAsync`, `ExecuteAsync`,
  `ExecuteProcessAsync`; `server/Application/Services/AgentTaskLandingProtocol.cs`:
  read the RecoveryPinned/RebaseStarted path and live failure/abort branch |
  real process and exact rebase vector -> V-7 through V-9. Protocol persistence,
  publication and caller notification are unchanged and excluded.
- `docs/testing-and-build.md`: checkpoint schema, combined-method filters,
  checkpoint runner, mutation and source qualification; `docs/project-context.md`
  and `docs/orchestration-loop.md`: applicable ownership/stage rules |
  checkpoint/cost contract below. No runtime settings or deployment change.

### Delivery inventory

New/changed asynchronous delivery paths: **none**. The edits are a Git attribute,
an in-process metadata validator, registry comments, tests and documentation.
There is no new producer, queue destination, persistence handoff, recovery worker
or session input, so no delivery identity or delivery/recovery guard to map.

V-7/V-8 use real Git and inspect the resulting committed registry and index;
their identity is the fixture's base/target/source/result SHAs and exact registry
path. This proves merge output, not publication or recipient delivery. The
fixture substitutes isolated repositories and configuration for the production
land coordinator; it cannot prove database recovery, remote master publication,
busy/eligible caller handling or a complete UserPrompt receipt. Those unchanged
paths require no new queue test here. A later scope expansion into them requires
a new delivery inventory, real-queue producer-to-recipient cases for busy and
already eligible recipients, recovery at each handoff, and matching complete
UserPrompt transcripts; a Git exit, request, queue row, Sent flag or ack will
not substitute for those receipts.

### Proves it works now

Shared recipes (test names below are binding implementation requirements):

- **M, metadata:** use the actual Antiphon.Tests assembly and an owned temporary
  registry, deleted in `finally`/disposal even on assertion failure. For a clean
  fixture, take the complete real registry's parsed entries and emit each
  existing reason/name pair with an initial explicit header block. This removes
  only the three instruction comments from the fixture's record stream, so the
  S1 fixtures can already be structurally valid. Assert selected names resolve
  to compiled Slow classes. Use SmokeTests and WorktreeDefaultAdmissionTests,
  both present in the inspected registry, for small damaged fixtures. Partial
  registries may report unrelated missing classes: require the specified
  diagnostic code, physical line and class when available; nonempty error text
  alone is never the oracle. Full fixtures must return exactly the empty string.
  Retain the source snapshot used for each expected physical line calculation.
- **G, Git:** in `SlowTestRegistryMergeTests` use fixture-owned repos and
  `LandingGitFixture.FixtureGit`, with no injected `BeforeCommand` result.
  Create a common registry from M's complete entries with the two selected
  complete pairs removed. Construct two divergent commits appending at the
  same EOF, one pair on each side. Copy the repository's actual `.gitattributes`
  bytes at the variant's specified point; never synthesize a union line as
  setup. Start a detached, fixture-owned land worktree at the source SHA and run
  `["-c", "rebase.autoStash=false", "-c", "rebase.updateRefs=false", "rebase", targetSha]`
  through the production process implementation. Observe the trace, exit,
  `RebaseHeadSha`, `HEAD`, `ls-files -u` and registry bytes read from that HEAD.
  All setup commands must succeed before reaching the rebase assertion.
  Isolate system/global config, disable hooks/signing/credential helpers by the
  fixture pattern, and assert no configured `merge.union.driver`. Use bounded
  cancellation, await owned processes, abort an expected conflict in `finally`,
  then dispose the owned root. No shell-script dependency or OS skip.

| ID | Behaviour / layer | Test, recipe, variants and classification | Reachability and decisive oracle |
|---|---|---|---|
| V-1 | Detached reasons are rejected / helper | `TestClassificationPolicyTests.C1383_Detached_reason_is_rejected`, M; 3 Arguments: blank, overwritten, eof; Unit | Place a valid CARD reason at line 1, then respectively blank + FQN, another valid reason + FQN, or EOF. Require `orphan-reason` naming line 1. For blank, also require `missing-adjacent-reason` at the FQN. EOF has no invented class name. Today's parser silently accepts the orphan; the specific diagnostic assertion is the red witness. |
| V-2 | Every row has its own adjacent reason; headers stay in the initial region / helper | `TestClassificationPolicyTests.C1383_Row_without_adjacent_reason_is_rejected`, M; 2 Arguments: bare-row, second-row. `TestClassificationPolicyTests.C1383_Header_after_records_is_rejected`, M; 2 Arguments: between-pairs, interrupts-first-pair; Unit | Bare FQN at line 1 and a second FQN at line 3 after one complete pair require `missing-adjacent-reason` at that row. Header at line 3 after a pair requires `header-after-entry`. Reason / header / FQN at lines 1/2/3 first asserts `header-after-entry` line 2, then `orphan-reason` line 1 and `missing-adjacent-reason` line 3. A reason starts the record region even before the first row. Existing missing-reason errors cannot satisfy these assertions. |
| V-3 | Complete valid registry accepts headers and separators / helper | `TestClassificationPolicyTests.C1383_Valid_reason_pairs_are_accepted`, M; 4 Arguments: LF-final, LF-no-final, CRLF-final, CRLF-no-final; Unit | Full compiled Slow membership, multiple initial `## ` headers, blank separators between complete pairs, valid reasons; exact empty error string in all four cases. Assert parsed names/reasons equal the input entry sequence; no sorting or pair loss. Four parameterized executions, not four internal loops. |
| V-4 | Strict layout is opt-in / helper | `TestClassificationPolicyTests.C1383_Layout_opt_in_is_scoped`, M; 2 Arguments: non-repository, other-assembly; Unit | Non-repository: damage a full registry by inserting a blank in a pair and appending an orphan; repositoryMode=false must still give empty errors. Other-assembly: create an in-memory empty assembly with a name other than Antiphon.Tests (no process/build), give it an orphan-only registry and repositoryMode=true; require empty errors. Each payload would trigger layout validation if its independent gate were removed. |
| V-5 | Public reader preserves its legacy behavior / helper | `TestClassificationPolicyTests.C1383_ReadRegistry_retains_legacy_layout_behavior`, M; 1 execution, Unit | A single input includes reason / blank / FQN, two reasons / FQN, and an orphan at EOF. Call public `ReadRegistry` directly. Assert both exact RegistryEntry names, surviving reason strings and physical row numbers, and count 2. No layout exception or invented EOF entry. |
| V-6 | Existing safety checks and migrated actual registry still work / helper + repository | `TestClassificationPolicyTests.C487_G062` (2 Arguments), `TestClassificationPolicyTests.C487_G064` (1), `TestClassificationGuardTests.Registry_matches_compiled_metadata` (1); Unit | Reuse G062's duplicate-fqn and shared-simple rows. Within duplicate-fqn, also repeat with the second spelling case-swapped, asserting its exact duplicate diagnostic; this remains 2 TUnit executions. Within G064, retain row-only and add empty-comment and nonempty unqualified-reason inputs, each requiring the named `missing-reason` error; still 1 execution. Actual repository guard requires zero errors after S2. Review the S2 registry diff against S1: same ordered FQNs and original reason text, with only the three instruction comments moved/marked and format guidance added; 155 is inspected history, not a future hard-coded membership cap. |
| V-7 | Real landing-shaped rebase preserves both distinct records / Integration | `SlowTestRegistryMergeTests.C1383_Rebase_preserves_distinct_appended_pairs`, G; 2 Arguments: common-base, target-only; Integration, ProcessSpawnLimit | Common-base commits actual attributes before either branch forks. Target-only forks with no rule, then commits actual attributes only on target before the conflicting target append; source demonstrably lacks the rule. Require successful rebase, nonnull result SHA equal to HEAD, no unmerged index or conflict markers, each exact reason/FQN pair once, unchanged remaining pairs, and empty metadata errors. Do not require left/right ordering. Check effective merge=union for the exact registry and merge unspecified for a sibling `tests/Antiphon.SessionRunner.Tests/slow-tests-allowlist.txt` and `docs/example.md`; this catches a widened rule. Assert target/base/source attribute placement from committed trees, not a setup boolean. |
| V-8 | Successful union does not excuse damaged layout / Integration | `SlowTestRegistryMergeTests.C1383_Union_collapsed_lines_are_rejected`, G; 2 Arguments: same-reason-different-classes, different-reasons-same-class; Integration, ProcessSpawnLimit | Attribute is committed in the common base. In the first case append the same valid reason with different compiled FQNs on the two sides; assert Git success, one shared reason and two rows, then the second row's `missing-adjacent-reason`. In the second remove one full pair from the common base and re-add its FQN with different valid reasons; assert Git success, two reasons and one row, then `orphan-reason` for the first reason. Confirm the collapsed shape before the guard assertion, using actual physical lines. Duplicate checking alone cannot detect a coalesced FQN. |
| V-9 | First landing is not assumed to benefit from its own replayed rule / Integration | `SlowTestRegistryMergeTests.C1383_Source_only_attribute_does_not_rescue_first_rebase`, G; 1 execution, Integration, ProcessSpawnLimit | Base and target have no attribute; source introduces the actual attribute bytes in the same commit as its conflicting append. Require nonzero rebase and a registry path in the unmerged index; inspect source/target committed attributes before replay and abort the fixture rebase afterward. This is a green characterization of a refusal, not an expected failing TUnit case. Once the rule is on real master, V-7 target-only is the recovery scenario for old branches. |

S1 adds all named new test bodies. Its red checkpoints exercise only newly
enforced behavior: V-1, both V-2 methods, and V-7. Commit/push runnable tests
before running them; each listed argument must fail at its intended assertion,
not setup/build/discovery. V-8 cannot demonstrate a layout red on S1 because
the absent attribute prevents the rebase first; V-1/V-2 supply those ordinary
layout witnesses, and post-land PC-3/PC-5 target the corresponding guards.
V-3/V-4/V-5/V-6/V-9 retain existing behavior and need no manufactured Code red;
their named post-land PCs establish sensitivity.

**First landing handling:** do not rely on the rule until the confirmed target
tree contains it. If this card's header migration conflicts before that point,
retain the normal land refusal and use the existing separately commissioned
source-repair/review path; no manual target merge, task-branch rebase, force push,
host driver or automatic conflict suppression is authorized. Publish to master,
then old source histories use the target-only case. TestDesign does not claim
that a scratch conflict test performs or confirms that publication.

### Guards the regression

- R-1: permissive pending-reason state silently swallows blank, overwritten or
  EOF reasons | V-1's exact `orphan-reason`/line assertions; CP-1 red, CP-5 green.
- R-2: a header or previous row supplies another row's reason | V-2's exact
  row/header/orphan diagnostics; CP-2/CP-3 red, CP-6/CP-7 green.
- R-3: valid headers, pair separators or line endings are rejected | V-3's
  full-registry empty result and exact parsed entries; CP-8.
- R-4: shared helper strictness leaks into compatibility callers | V-4's
  independent gate cases and V-5's direct reader entries; CP-9/CP-10.
- R-5: duplicate, case-folded duplicate, simple-name or missing/empty/unqualified
  reason becomes accepted | reused G062/G064's specific diagnostics; CP-11/CP-12.
- R-6: migrating actual registry comments loses a Slow entry or breaks the guard
  connection | existing repository `Registry_matches_compiled_metadata` empty
  result plus review of the ordered reason/FQN migration diff; CP-13.
- R-7: rule removal, broadening, fixture-only rule, or target-only history stops
  working | V-7's real rebase, committed-tree preconditions, effective attributes
  and exact merged pairs; CP-4 red, CP-14 green.
- R-8: successful Git exit is mistaken for a valid registry | V-8's observed
  collapsed bytes followed by specific guard diagnostics; CP-15.
- R-9: first-landing conflict is hidden by prematurely installing the attribute
  on the target | V-9's target/source tree preconditions and real unmerged index;
  CP-16. Code does not turn this expected refusal into a passing-rebase promise.

### Guard inventory

These are the changed or expressly relied-on safety/compatibility invariants.
Independent bypasses are separate even when the same method detects them.

| Guard | Plan reference and invariant | Positive control |
|---|---|---|
| G-1 | D-1/D-4: repository attribute enables real union merging | PC-1 |
| G-2 | D-1: union is restricted to the exact registry path | PC-2 |
| G-3 | D-3: every row has a physically adjacent reason | PC-3 |
| G-4 | D-3: a blank after a reason is diagnosed as orphaning it | PC-4 |
| G-5 | D-3: another reason cannot silently overwrite a reason | PC-5 |
| G-6 | D-3: a final unconsumed reason is rejected | PC-6 |
| G-7 | D-3: a header after a complete record is rejected | PC-7 |
| G-8 | D-3: a header interrupting even the first pair is rejected | PC-8 |
| G-9 | D-2: repositoryMode gates strict layout independently | PC-9 |
| G-10 | D-2: assembly identity gates strict layout independently | PC-10 |
| G-11 | D-2: existing duplicate-row rejection remains active | PC-11 |
| G-12 | D-2: duplicate identity remains case-insensitive | PC-12 |
| G-13 | D-2: repository names remain fully qualified | PC-13 |
| G-14 | D-2/D-3: missing/empty reason content is rejected | PC-14 |
| G-15 | D-2/D-3: nonempty reasons still require qualifying content | PC-15 |
| G-16 | D-2: public ReadRegistry retains legacy parsing | PC-16 |
| G-17 | D-3: valid initial headers/pairs remain accepted | PC-17 |
| G-18 | S2: actual registry is checked against compiled Slow membership | PC-18 |

No new delivery/recovery guards. Other existing metadata rules (unknown class,
unmarked registration, lane XOR and method-level Slow) remain unmodified and
are not new assertions here; this is not a census of the helper's historical PCs.
The no-rule-first-landing boundary is Git behavior, not an independently edited
production guard; V-9 pins its fixture preconditions, while PC-1 removes the
product rule and exercises the positive target histories.

### Positive controls

Each row is a distinct mutation, never an assertion deletion, fake Git result or
build error. For new code, the described mutation is a **binding-required
payload**: Code must attach the final file/member and concrete replacement
before Mutation, preserving these IDs. If the implementation combines checks,
retain targeted bypass conditions so another diagnostic cannot mask the
specified absent diagnostic. Unbound payloads remain binding required after
Code; they are not executed or waived by being listed here.

| PC | Compiling defect / mutation | Exact detecting method and required red |
|---|---|---|
| PC-1 | Remove only the new exact-path merge=union rule from `.gitattributes` | `SlowTestRegistryMergeTests.C1383_Rebase_preserves_distinct_appended_pairs`: both argument rows reach the rebase-success assertion and fail with real conflicts. Defer effective-attribute assertions until after the rebase-success assertion so this is not merely a text/config check. |
| PC-2 | Replace that rule's path with `tests/**/slow-tests-allowlist.txt`, retaining merge=union | Same exact method as PC-1: rebase succeeds; effective sibling-registry merge attribute assertion fails (expected unspecified, actual union) in both variants. |
| PC-3 | Suppress only `missing-adjacent-reason` emission in the new layout validator | `TestClassificationPolicyTests.C1383_Row_without_adjacent_reason_is_rejected`: both rows fail at the expected code plus physical row assertion even if missing-reason is still emitted. |
| PC-4 | Suppress only the orphan diagnostic when the following physical line is blank | `TestClassificationPolicyTests.C1383_Detached_reason_is_rejected`: blank argument fails at `orphan-reason` for line 1; other argument outcomes recorded. |
| PC-5 | Suppress only the orphan diagnostic when the following line is another single-# reason | Same exact detached-reason method: overwritten argument fails at `orphan-reason` for line 1. |
| PC-6 | Suppress only final pending-reason validation at EOF | Same exact detached-reason method: eof argument fails at `orphan-reason` for line 1. |
| PC-7 | Permit a header after a completed pair when no reason is pending | `TestClassificationPolicyTests.C1383_Header_after_records_is_rejected`: between-pairs argument loses `header-after-entry` at line 3. |
| PC-8 | Permit a header while a reason is pending before the first FQN | Same exact header method: interrupts-first-pair argument loses `header-after-entry` at line 2. Assert that code before secondary pair diagnostics. |
| PC-9 | Remove only the repositoryMode condition around strict layout | `TestClassificationPolicyTests.C1383_Layout_opt_in_is_scoped`: non-repository argument's exact empty-errors assertion fails with the layout diagnostic. |
| PC-10 | Remove only the Antiphon.Tests assembly-name condition around strict layout | Same exact opt-in method: other-assembly argument's empty-errors assertion fails with orphan-reason. |
| PC-11 | Leave `seen.Add` evaluation but suppress duplicate diagnostic emission | `TestClassificationPolicyTests.C487_G062`: duplicate-fqn argument fails at `duplicate Antiphon.Tests.SmokeTests`. |
| PC-12 | Change only the `seen` set comparer from OrdinalIgnoreCase to Ordinal | Same exact G062 method: exact-spelling duplicate still passes its assertion, case-swapped duplicate fails its specific duplicate assertion. |
| PC-13 | Disable the repository simple-name-refused check, leaving name lookup unchanged | Same exact G062 method: shared-simple argument fails at `simple-name-refused SmokeTests`; unknown-class text cannot satisfy it. |
| PC-14 | Exempt whitespace/empty Reason from the existing entire missing-reason predicate | `TestClassificationPolicyTests.C487_G064`: row-only/empty-comment input fails at `missing-reason Antiphon.Tests.SmokeTests`. Keep nonempty qualification checks active. |
| PC-15 | Reduce the existing missing-reason predicate to only IsNullOrWhiteSpace | Same exact G064 method: empty cases pass, nonempty unqualified reason fails its specific missing-reason assertion. |
| PC-16 | Clear pendingReason on blank lines in public ReadRegistry (or its equivalent legacy parse path) | `TestClassificationPolicyTests.C1383_ReadRegistry_retains_legacy_layout_behavior`: first returned entry's exact reason assertion fails. |
| PC-17 | Treat an initial `## ` header as an orphan single-# reason in strict validation | `TestClassificationPolicyTests.C1383_Valid_reason_pairs_are_accepted`: all four line-ending/final-newline variants fail the empty-errors assertion. |
| PC-18 | Delete only the actual SmokeTests reason/FQN pair from the real allowlist | `TestClassificationGuardTests.Registry_matches_compiled_metadata`: existing empty-lines assertion fails with `unregistered-marked Antiphon.Tests.SmokeTests`. Restore exact allowlist bytes. |

Mutation runs **all 18 PCs**, including all argument rows of each selected method;
no sampling or whole-class substitute. For each PC use only
`/*/*/<Class>/<ExactMethod>*` (parameterized method prefix) or the exact
nonparameterized method path above: green baseline, compiling defect, intended
red, exact restore, fresh-build green. Capture build exits, TRX assertion text,
expanded counts and restoration evidence at the landed SHA. A zero count,
timeout or build/fixture error is incomplete evidence, never the required red.
All phases use the host build slot and the owner's SourceLanding driver procedure.
No production/test repair or commit from a SourceLanding snapshot.

Design handoff audit: **guards=18, mapped=18, missing=0,
duplicate PC maps=0**. All 18 are mandatory future runs; execution at TestDesign
is **0/18** by stage order. Code runs the ordinary V/R checkpoints, Review judges
their results and this pending inventory before land, and Mutation runs every
PC after confirmed land. Completion of this design does not claim PC-clean.

### Out of scope

- Full Unit/assembly, database-backed landing, queues, PTY, browser, deployment
  and rc activation: no changed boundary requires them.
- Semantic truth of arbitrary free-text reasons, swapping two complete pairs'
  reasons, or sorted registry order: this format supports structural attachment
  only. V-7 still checks each fixture's known exact reason/FQN association.
- Other assemblies' new strict format or union merging: expressly excluded by
  D-1/D-2; V-4 and V-7 prove the negative boundaries.
- All Git versions, host-specific custom drivers, explicit apply-backend matrix:
  the test binds the production vector and fixture-isolated default backend.
  Record Git version/OS in evidence; the earlier apply probe is historical
  planning evidence, not a new checkpoint or universal backend guarantee.
- Exact one-file-snapshot implementation is reviewed directly in the helper:
  do not add a timing-race test or a new public I/O seam solely to count reads.

### Checkpoints

Code commissioning override, task 3d53859f (2026-10-20): the explicit minimal
selection supersedes the original 16-row manifest and generic whole-Unit profile.
S1 registers only blank and common-base arguments on the two red methods; S2
adds their remaining argument variants. This yields exactly one red witness
per changed behavior, followed by one grouped green selection per area.
CP-2/CP-3 red selections are skipped, with their green cases merged into CP-5.
Original CP-5 through CP-10 and CP-12 are merged into CP-5 (15 executions).
CP-11 existing duplicate/simple-name checks are skipped as explicitly directed;
V-6/R-5's identity checks are not claimed executed. Their planned case-fold
assertion is implemented for future Mutation, without another ordinary duplicate run.
CP-12 retains reason-content regression coverage in the grouped row. CP-13 is retained.
CP-14 through CP-16 are merged into CP-14 (the full new Integration class,
5 executions). There is no whole-Unit or full assembly run. No PC binding or
execution is commissioned; all PC-1 through PC-18 remain pending.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1383-red/` | red-detached | `/*/*/TestClassificationPolicyTests/C1383_Detached_reason_is_rejected*` | V-1, R-1 | 1 intended orphan-reason assertion failure | 1 | 4 |
| CP-4 | S1 | CP-1 | red-real-union | `/*/*/SlowTestRegistryMergeTests/C1383_Rebase_preserves_distinct_appended_pairs*` | V-7, R-7 | 1 intended real rebase-success assertion failure | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1383-green/` | green-layout | `/*/*/TestClassificationPolicyTests/(C1383_*)\|(C487_G064*)` | V-1, V-2, V-3, V-4, V-5, V-6, R-1, R-2, R-3, R-4, R-5 | all 15 variants, 0 failed/skipped | 15 | 4 |
| CP-13 | S2 | CP-5 | green-repository | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata*` | V-6, R-6 | 1 actual-repository guard, 0 failed/skipped | 1 | 1 |
| CP-14 | S2 | CP-5 | green-real-git | `/*/*/SlowTestRegistryMergeTests/C1383_*` | V-7, V-8, V-9, R-7, R-8, R-9 | all 5 variants, 0 failed/skipped | 5 | 1 |

Run one checkpoint-tool invocation per committed group using --serial,
--row-timeout 40m and --expected-source-sha at that group's committed HEAD.
Bootstrap through scripts/build-slot.ps1. Await wait until exit is not 75.
Keep and inspect fresh TRX, source receipts and unedited CHECKPOINT lines.
S1 is expected red; S2 must be green with verified clean source provenance.
Review the registry migration's ordered names and reasons independently.
Run scripts/check-evidence-diff.ps1 over the complete assigned base..HEAD.

### Cost

Commissioned trimmed Code budget: 11 estimated checkpoint minutes (two builds,
five narrow rows), plus authoring and tool bootstrap within the 45-minute brief.
The original TestDesign estimates below are historical, not the active run list.


All numbers below are **estimated**, not measured by TestDesign; slot queue
waits and environment failures are reported separately.

- Ordinary Code V/R floor = sum of CP EstimatedMinutes = **22 minutes**:
  S1 CP-1..CP-4 = 7, S2 CP-5..CP-16 = 15. This includes two 3-minute
  builds (6 total) and 16 minutes of named-filter executions. The importer
  requires positive integer minute estimates; each filter is budgeted 1 minute. Tool bootstrap
  plus evidence/history validation and owned-output cleanup adds **2 minutes**;
  ordinary verification including setup = **24 minutes**, before authoring.
- Mutation PC floor: each PC uses its exact method filter in the PC table,
  three fresh builds (baseline/red/restored-green) at 3 minutes each.
  Every named method filter is budgeted 1 minute per phase, hence
  **12 minutes each** for PC-1 through PC-18. Mutation execution floor =
  `18 * (3 * 3 + 3 * 1)` = **216 minutes**. Add **2 minutes** setup/restoration
  reporting minimum: **218 minutes**, excluding mutation authoring/discovery
  and slot waits. The 18-PC cost must be commissioned explicitly as the
  post-land obligation, not hidden in Code's ordinary floor.
- Combined setup/build/V/R/PC floor =
  `2 + 22 + 2 + 216` = **242 minutes**. Each PC includes its red and
  restore/green; none is silently sampled or charged to ordinary Review.
- Build reuse saves **14 builds, estimated 42 minutes** compared with rebuilding
  independently for all 16 CP rows. Exact filters select 32 ordinary executions
  across red and green, avoiding unrelated probe builds and whole-Unit work.
  No full-suite saving is claimed from the obsolete full-suite timing.
  Method-scoped PC cycles intentionally save zero fresh builds: independent
  source states and restoration provenance are required.
