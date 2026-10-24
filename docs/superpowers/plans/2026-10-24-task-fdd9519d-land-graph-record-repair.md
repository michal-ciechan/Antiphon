# Land graph record repair (Code fdd9519d)

Base: `ceb6b8da3b0c11d062594d84c7b98751a681e99c`. Landing owner:
`fdd9519d-676b-4fb3-afbe-6958cd145434`. Caller authorized option (b).

Remove HostCleanup's project-level DefineConstants self-append. Fixed forwarding
types preserve its internal redactor namespace and delegate to the unchanged linked
implementations. No pins, allowlists, timeouts or assertions change.

The real audit `-Check` failed at the base with the precheck's disallowed
DefineConstants diagnostic. Commit source before `-Update`; only the real tool may
write the two records. Commit records before the ordinary checkpoint.

## Verification design

The caller's explicit selection is four existing acceptance tests, one test-project
build and audit `-Check`. No new test is introduced. The existing audit/registry
oracles guard the observed defect; this task adds no deliberate mutation controls.
SourceLanding Mutation remains a caller-owned follow-up, including discovery of
missing controls. No PC IDs are assigned by this brief.

- V-1: real `audit-land-verification-graph.ps1 -Update`, then `-Check` succeeds.
- V-2: `C1385_RegistryMatchesNarrowGraph` binds the embedded and stored registry to HEAD.
- V-3: `C1483_S5_CheckRequiresEvaluationRecord` validates record admission and the real tree.
- R-1: `C1385_UnrelatedScriptChangeStillSkips` preserves the narrow graph exclusion.
- R-2: `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` preserves landing acceptance.
- R-3: evidence history guard over base..HEAD and conflict-free merge-tree against fetched master.

The two build prerequisites are separately accounted utility bootstraps, not extra
test-project builds: the checkpoint tool is needed to run the table; the Release
audit evaluator is needed to regenerate the records before the test build embeds
them. Both use the host build-slot gate and isolated outputs. They execute no tests.
The checkpoint tool's own build may be reused for its subsequent invocations.
No full-assembly test run is selected. Expected cost: utility builds 3 minutes,
audit 2 minutes, one test build plus four narrow acceptance tests 12 minutes,
slot admission additional. No repeats after green. Restart: none.

## Execution finding at source commit 5ff8440646270419e07dd3cf417c3224d5d0289c

The source arrangement and both utility builds succeeded. Each bootstrap received
a build-slot grant with `waited=0s`; checkpoint-tool build held its slot 12 seconds,
Release evaluator build 6 seconds. These were the only builds. No tests executed.

V-1 remains failed: real `-Update` passed the former HostCleanup refusal but then
refused the inherited `tests/Shared/FakeProviderOutput.targets` item type
`_SourceItemsToCopyToOutputDirectory`. That file also declares the SDK items
`_SourceItemsToCopyToOutputDirectoryAlways`,
`_SourceItemsToCopyToOutputDirectoryIfDifferent`, and
`AllItemsFullPathWithTargetPath`, none admitted by the precheck's item inventory.
Its introducing commit is `e9f0259e5fd07dbe9f50365f07c382839af6fdbd`.

A read-only direct evaluator diagnostic (not record approval and not a bypass of
the real wrapper) additionally returned exit 1 with these exact lines:

```text
refused: tests/Shared/FakeProviderOutput.targets Target ExcludeRootFakeProviderAppHosts (missing pin)
refused: tests/Shared/FakeProviderOutput.targets Target RemoveObsoleteRootFakeProviderAppHosts (missing pin)
```

No record was published, no pin was edited, and no precheck or evaluation policy
was relaxed. CP-1, V-2, V-3, R-1 and R-2 are **NOT RUN / pending**: their required
fresh records cannot be produced under the caller's no-pin instruction. The
one test-project build has not been spent on this known incomplete graph.
The caller must commission the inherited target-policy/pin repair with its
required Review, or authorize a separate behavior-preserving removal of those
targets. The HostCleanup repair is independently pushed for that continuation.

## Authorized no-pin replacement feasibility (second continuation)

The caller authorized replacing both targets only if all build guarantees survive
without pins or audit-policy changes. Inspection found no safe replacement in
that admitted surface. This is a structural policy constraint, not a request to
approve a digest or an author-created pin:

1. `TargetPins.Check` (`tools/Antiphon.LandGraphAudit/TargetPins.cs:28-46`)
   unconditionally requires a pin for **every** tracked XML `Target`. Using
   already admitted item/property names does not exempt a replacement target.
   OS-dependent evaluation elements also require pins. Moving the targets to
   another definition or editing an existing pinned target cannot avoid this.
2. In the installed SDK 10.0.402, `Microsoft.NET.Sdk.targets` adds the apphost
   `None` items inside `_ComputeNETCoreBuildOutputFiles`, at target execution
   before `AssignTargetPaths`. Declarative item removals/updates run earlier,
   during project evaluation, and cannot filter those subsequently created items.
3. `Microsoft.Common.CurrentVersion.targets` excludes reference content when
   `ProjectReference.Private=false`, but the same metadata also disables managed
   Copy Local. That loses the required root `fakeclaude.dll`. Disabling producer
   apphosts loses the required staged native executables. Neither is equivalent.
4. The old-root-file recovery guarantee requires target-time filesystem cleanup.
   A declarative item/property replacement cannot perform that deletion. Putting
   these build side effects in unrelated source/tools would bypass the audit's
   target trust boundary and is not a safe alternative.

The original qualification manifest is
`docs/superpowers/plans/2026-10-24-fb7c6043-fake-provider-layout.md`.
Its smoke roster is exactly 15 expanded cases: `CxSessionTestHomeTests` (1),
`StdinClipModelTests` (13), `LandDeliveryFixtureProbeTests` (1). The layout
witness is `scripts/test-fake-provider-layout.ps1 -Phase Green -Project Pty|E2E`
with a fresh isolated OutputPath for each; it checks two default-apphost builds,
nested native executable/DLL/deps/runtimeconfig, and root managed fakeclaude.
Checkpoint-injected `UseAppHost=false` cannot qualify this behavior.

No replacement was applied and no build or test was run in this continuation.
These 15 cases, both clean/repeated layout witnesses, failed-output recovery,
Windows layout qualification, CP-1 and its four acceptance cases remain pending.
The existing source/build outcomes above remain historical. The requested
pinless replacement is unavailable under the closed policy; continuation needs
explicit authorization for the reviewed policy-and-pin repair (the four SDK
item names and both exact target bodies), with matched policy fixtures. Pins,
policy and stored records remain untouched.

### Caller-supplied verification provenance

The later refinement supplied the complete predecessor report at
`/work/worktrees/task-fb7c6043/.antiphon/task-fb7c6043.md` on this host. It was
read in full and confirms these exact smoke filters:

- `/*/*/(CxSessionTestHomeTests*)|(StdinClipModelTests*)/*` (14 expanded cases).
- `/*/*/LandDeliveryFixtureProbeTests/*` (1 expanded case).

It also confirms the two default-apphost layout witnesses and failed-output
recovery requirement above. Its 15 passing smoke results belong to predecessor
source `7a6a2e82279749250e4afb12a568d67fcda30c8e`, not to this task's source;
they are not reused as current acceptance. Its Unit lane ended without final
TRX and remains historical incomplete evidence. No repeat or additional build
is justified by this roster clarification while the no-pin policy blocker
persists. No execution or acceptance status in this document changes.

## Authorized policy-and-pin continuation

The caller now authorizes the four exact SDK-item removals and hand-written exact
pins for both existing targets. No Review approval is represented by these pins;
the caller will commission real Review on the pushed tip before landing.

V-4: one four-argument fixture admits the exact removal for each SDK item, then
refuses Include, altered Remove, altered Condition, metadata, another owner,
another target, evaluation-time use and task-output use. Commit tests before the
policy and run CP-1 red (four assertion failures). The policy remains limited to
the exact file/target/item/attributes; no general SDK item allowance is added.
V-1: derive pins with TargetPins' exact XML normalization, commit them and policy,
then run the real -Update and -Check; never hand-edit records.
R-4: the 15 predecessor smoke cases execute on this source.
R-5: both unchanged layout witnesses pass fresh and second default-apphost builds.
Windows layout remains unchanged because neither target body nor condition changes.

The current requested closed scope is CP-2..CP-6 after the red witness. Utility
bootstraps and the extra red test build are explicitly accounted prerequisites.
Each final TUnit project has one isolated build; layout witnesses necessarily
perform their two default-apphost builds each. No passing selection is repeated.
Estimated final floor: 21 minutes, plus bootstraps, red proof and slot admission.
The prior stop findings and outcomes above remain historical; this authorization
supersedes the no-pin constraint. PCs: no deliberate mutants in Code; all controls
and missing-control discovery remain pending for SourceLanding Mutation.

The test-only source `c311d7b0c5bbb48c784d52c225a1d2c0d648dc55` ran CP-1
as run `20261024-094834-5708`: fresh TRX confirms four intended assertion
failures, each at its exact removal's formerly disallowed item type. No fixture
or build failure substituted for red. Build source was verified and clean;
both build and row slots were granted with zero wait. Build 192.216 seconds,
test row 10.083 seconds. One mistyped abbreviated expected-source argument was
rejected before any run/row/build, then corrected to the full committed SHA.

The narrow policy and exact target pins were committed before regeneration.
Real `-Update` succeeded using the unchanged evaluator staged by CP-1 (no extra
evaluator bootstrap): digest
`8D4CCE85EBDE82978FD648F0F5DDCEA221A3F492C4A28D67DCD1A7A549FCF34B`,
65 definition files. The tool's current census supersedes the brief's older
64-file observation. Both records are real tool output. The two target bodies,
their Windows conditions, provider sources and consumer project files are
byte-unchanged; no new approval record is asserted. Real Review is still owed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | Guard | `tests/Antiphon.Tests -> bin-fdd9519d-red/` | sdk-removal-red | `/*/*/LandGraphEvaluationTests/Fdd9519d_SdkItemsAdmitOnlyReviewedRemoval` | V-4 red witness | expected four assertion failures before policy repair | 4 | 6 | true |
| CP-2 | Final | `tests/Antiphon.Tests -> bin-fdd9519d-final/` | graph-records | `/*/*/(LandNonCodeSkipTests*)\|(LandGraphEvaluationTests*)/(C1385_RegistryMatchesNarrowGraph*)\|(C1483_S5_CheckRequiresEvaluationRecord*)\|(C1385_UnrelatedScriptChangeStillSkips*)\|(C1380_DocsOnlyRebasedLandRecordsNonCodeSkip*)\|(Fdd9519d_SdkItemsAdmitOnlyReviewedRemoval*)` | V-2 V-3 V-4 R-1 R-2 | exactly 8 passed, 0 failed/skipped | 8 | 10 | true |
| CP-3 | Final | `tests/Antiphon.Agents.Pty.Tests -> bin-fdd9519d-pty/` | pty-smoke | `/*/*/(CxSessionTestHomeTests*)\|(StdinClipModelTests*)/*` | R-4 | exactly 14 passed, 0 failed/skipped | 14 | 2 | true |
| CP-4 | Final | `tests/Antiphon.E2E -> bin-fdd9519d-e2e/` | e2e-smoke | `/*/*/LandDeliveryFixtureProbeTests/*` | R-4 | exactly 1 passed, 0 failed/skipped | 1 | 4 | true |
| CP-5 | Final | n/a | pty-layout | `pwsh -NoProfile -File scripts/test-fake-provider-layout.ps1 -Project Pty -Phase Green -OutputPath bin-fdd9519d-layout-pty/` | R-5 | two builds pass and all layout assertions pass | n/a | 2 | true |
| CP-6 | Final | n/a | e2e-layout | `pwsh -NoProfile -File scripts/test-fake-provider-layout.ps1 -Project E2E -Phase Green -OutputPath bin-fdd9519d-layout-e2e/` | R-5 | two builds pass and all layout assertions pass | n/a | 3 | true |
