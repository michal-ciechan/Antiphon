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
