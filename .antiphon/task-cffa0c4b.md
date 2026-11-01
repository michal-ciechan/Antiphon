# CARD-1545 combined set, Final Review round 2 (task cffa0c4b)

Outcome: **defects found; not ready to merge.** Round-1 D-1, D-2 and D-5..D-7 are closed on the reviewed tree,
and round-1 D-4's eight named survivors now go red. The D-3 fix makes CP-30 green, but only because the census now compares the candidate with
itself (new D-1 below). The sink audit still misses many ordinary sink shapes (new D-2). Every ordinary row
named in the brief ran on `330c600417eccc2fb7da9a4caea813c745f7142f` through the checkpoint tool. The whole
Unit lane ran once on that tree. All failures are inherited or load flakes (Sweep section).

Reviewed on server2/Linux. The subject branches were only read. No live Claude ran. `~/.claude` was not read.
Nothing under `C:\Antiphon` was touched. `-Update` was not run.

Subject: `feat/card-task-f9d4a394` report commit `330c600417eccc2fb7da9a4caea813c745f7142f`. The code tip is
`ae885bfe938e2cec94f674748122a0863b7f4a64`, and the report commit adds only `.antiphon/task-f9d4a394.md`.
`origin/master` = `5aea2708fca4e196ae1915b6101c5287cb8c7cea`, fetched again during this review and unchanged.

## Defects

### D-1 (High; D-3 not fixed) The re-seeded CP-30 census compares the combined tree with itself
- Where: `scripts/lib/live-lane-manifest.ps1:8` (`$script:LiveLanePreS1BaseSha = '2e5b78808...'`),
  `tests/fixtures/live-lanes/full-discovery-before.json` (rewritten in `ae885bfe9`),
  `tests/live-lanes.json:2094` (`additiveContract: []`), and the census engine `scripts/lib/live-lane-manifest.ps1:2005-2009`
  (a LiveClaude addition is *allowed*, never *required*). The seed allow-list at `:10-17` also contains the census engine
  file itself and its driver.
- Failure: the "pre-S1 base" is now `2e5b78808`, the combined branch's own tip. It already contains the S1c chain,
  sanitization, S1a and D-1..D-7. Measured on the reviewed tree:
  - The new fixture equals the candidate exactly. Compared with my own CP-25/CP-1 exports at `330c6004`:
    Antiphon.Tests 20717/20717 and Pty 801/801 UIDs, 0 added, 0 removed, 0 category differences.
  - The six core UIDs already carry `LiveClaude` in the "before" fixture. The previous seed `556a20e65` carried
    0 of them. CP-30 therefore observes **no** LiveClaude addition, while the S1a design requires
    "`valid` asserts full equality plus six observed category additions" (V-4, s1a test design line 220).
  - Compared with the old seed, the fixture gains 158 UIDs (92 methods). 33 of those methods do not exist on master
    `5aea2708f`: they are CARD-1545 additions (`LiveClaude{Delivery,Evidence,Executable,Isolation,Startup}WiringTests`
    20, `LiveLane{Evidence,Executable,Inventory,Isolation}Tests` 12, `PlanCoverageBindingTests` 1). None is declared;
    `additiveContract` was emptied. The plan requires "New contract UIDs require an explicitly enumerated additive
    required set" (s1a design L131-139; plan L441).
  - Result: CP-30 green proves only that nothing changed between `2e5b78808` and the candidate. It says nothing
    about whether S1 left Full unchanged relative to master, which is its only purpose (plan L433-445).
- What still works: drift introduced **after** the seed's parent is still caught. These are my own witnesses, using
  the real census command and my own context and exports, not the in-body hooks:
  - added test UID: `undeclared-new-uid nightly-disposition-changed`
  - dropped UID: `old-membership-changed nightly-disposition-changed`
  - `OptIn` flipped on a required UID: `category-delta nightly-disposition-changed`
  - reader edit (`nightly-policy.ps1` comment): `reader-input-changed`
  - rc exclusion added with the policy hash restated: `policy-hash-changed reader-input-changed`
  - each of these exited 2; the unmodified base was `accepted`, exit 0.
- Can a commit simply re-seed to hide drift? **Yes, and this one does.** The check binds to the
  `-CensusBaseSha` argument the caller passes, plus a pre-S1 constant stored in an allowed seed path. Nothing
  outside the branch pins either value. A seed commit whose parent is any drift commit X, with pre = X, passes every
  seed-range, protected-path and provenance check, because they compare X with itself. The allow-list also lets a
  seed commit edit the census engine (`live-lane-manifest.ps1`) and its driver. The fixture was made from ad-hoc
  builds (`bin-d6`/`bin-d4`), not from receipt-bound CP-27/CP-28 rows (plan L435; s1a design L117-121, "rerun
  CP-27/28 green, then commit the merged fixture"). The fixture's population is reproducible from the tree; the
  assembly hashes are path-dependent and are not.
- Who may re-seed: the plan (L403/L435-437, s1a design L84 "record the actual **master-based** pre-S1 source",
  R-3 "master-based fresh commission") gives that to an S1a-baseline commission at a master-based pre-S1 source.
  It does not allow a fix author to rebase the "pre-S1" base onto the post-S1 tree.
- Fix (owner: S1a landing owner `5dd200c3`, through the combined-fix owner `f9d4a394`):
  - re-seed master-based: pre-S1 = `5aea2708f`, with a seed chain whose parent is that master commit and which
    carries only allowed seed paths, using a fixture captured by CP-27/CP-28;
  - make that seed an ancestor of the candidate;
  - declare the 33 CARD-1545 methods in `additiveContract`;
  - make the engine require exactly the six observed LiveClaude additions;
  - keep the pre-S1 binding and the census engine out of what a seed commit may edit;
  - rerun CP-25, CP-1 and CP-30.

### D-2 (Medium; round-1 D-4 follow-on) The V-6/V-7 sink audit misses common sink shapes
- Where: `tests/Shared/LiveClaude/LiveClaudeSinkAudit.cs` covers these and nothing else:
  - `IsConsole` (`:238`) matches a name only when it starts with `Console.`/`TestContext.`/`Trace.`/`Debug.`
  - `IsFileSync`/`IsFileAsync` (`:259-273`) cover four leaf names only
  - `IsSkipDotTest` (`:252`) covers `Skip.Test` only
  - `Tainted` (`:95`) follows declarator and identifier assignment only
  - `IsShould` takes the receiver only when it is `DirectScreen`
- Failure: a reflection probe against the HEAD Pty build called `LiveClaudeSinkAudit.Walk(WithHelpers(...))` on
  `Control_auto_connects_a_bridge_session_before_the_first_user`, with one statement appended exactly as
  `ProveCallerMutants` does. Baseline counts were all 0. **Survivors** (sink count unchanged):
  - console aliases and method groups: `var err = Console.Error; err.WriteLine(s)`; `TextWriter w = Console.Out; w.Write(s)`; `using static System.Console; WriteLine(s)`; `using Out = System.Console; Out.WriteLine(s)`; `Action<string> log = Console.WriteLine; log(s)`; `new[]{s}.ToList().ForEach(Console.WriteLine)`
  - conditional access: `Console.Out?.WriteLine(s)`; `TestContext.Current?.OutputWriter.WriteLine(s)`
  - other file and path sinks: `File.Create(Path.Combine(tmp, s))`; `Directory.CreateDirectory(...s)`; `File.Move(..., ...s)`; `File.WriteAllLines(p, new[]{s})`; `new StreamWriter(p)` then `.Write(s)`
  - other skips: `Skip.When(true, s)`; `Skip.Unless(false, "x " + s)`
  - taint through mutation: `StringBuilder.Append(s)` then `throw new X(sb.ToString())`; `DefaultInterpolatedStringHandler.AppendFormatted(s)`; `box[0] = s; throw new X(box[0])`; `var (a, b) = (s, 1); throw new X(a)`
  - derived assertion values: `new[]{s}.ShouldBeEmpty()`; `s.Split('\n').ShouldBeEmpty()`, where Shouldly prints the values
  - `Environment.FailFast(s)`; `Debug.Assert(false, s)`

  Here `s` = `runner.SnapshotScreen()`. The same `Walk` serves the C680 file walk in `LiveLaneEvidenceTests`.
  Caught, as the brief asked: a nested `InnerException`, `AggregateException` (both forms), `string.Format` or
  interpolation into an exception or into Console, and `Assert.That`.
- Why: the brief names a Console.Error local alias, a method group and file-name sinks, and they survive. The canary
  methods contain none of these shapes today (grep), so nothing leaks now. The guard, though, can be bypassed by
  ordinary code.
- Fix (owner: sanitization `3a7c132f`/`f6dfc56e`):
  - invert the audit to an allow-list: any tainted value reaching any invocation or creation that is not a
    `LiveClaudeEvidence.*` projection counts (also list `LiveClaudeEvidence` members explicitly);
  - treat `TextWriter`/`Console`-bound locals, using-aliases, method groups and conditional access as sinks;
  - propagate taint through mutating calls on locals, element assignment and deconstruction;
  - count `Skip.*`, every File/Directory/FileInfo/StreamWriter/FileStream argument, `FailFast` and `Debug.Assert`;
  - use `Carries` for Shouldly receivers;
  - add these shapes as in-suite N-controls.

## Round-1 defects: closed in code (verified)
- **D-1 (Compile items).** `linked_shared_sources_are_literal_compile_items` goes red each way in both projects.
  These are source-read mutants run on the HEAD Antiphon.Tests dll inside the mutant tree:
  - add a file: red for `Antiphon.Tests.csproj` (`live should contain ../Shared/LiveClaude/LiveClaudeZzzReviewExtra.cs`)
  - add a file plus its Antiphon.Tests include: red for `Antiphon.Agents.Pty.Tests.csproj`
  - delete a file: red for `Antiphon.Tests.csproj lists ../Shared/LiveClaude/LiveClaudeParityCases.cs`
  - delete a file plus its Antiphon.Tests include: red for `Antiphon.Agents.Pty.Tests.csproj lists ...ParityCases.cs`
  - restored: green.
- **D-2 (setup skips).** `LiveClaudeEvidence.SetupSkip` accepts only the closed set `StartupDefaultAbsent|BridgeNotEstablished|TrustNotCleared|ReadyTimeout`
  and returns the constant `"20 " + setup`. Anything else throws the constant `C1545 evidence/setup`.
  An Add-Type probe of the committed file with planted setup texts gave:
  - inputs: `sk-ant-api03-FAKE`, a JWT `eyJ...`, `/home/app/.claude/.credentials.json`, `Authorization: Bearer abc123`, `ReadyTimeout\nBearer x`
  - every one threw `C1545 evidence/setup`, so no input text reaches the skip message or the TRX
  - `LiveClaudeRefusal` messages are `"20 " + literal reason`.

  The V-6 audit was not weakened. Comparing `0b903dd9d..HEAD`, it now treats every `*Exception` creation and throw
  as a sink, not only `SkipTestException`. It accepts `SetupSkip` as a `LiveClaudeEvidence.*` projection, and
  `LiveClaudeProfileAudit` requires that exact projection (`-projection`). The exit-20 mapping and the chain's
  witnesses still go red: S1, S2, S4, S5, S6 below.
- **D-5 (base64).** Same probe, through `run=`, `case=` and field `cli=`:
  - refused or projected away: `Q2xh...cHF4_1` (62 base64 characters plus one `_`), the same with a middle `_`,
    the OAuth code shape `ac_9FqZ2x...`, a `-` variant, a JWT, a home path and `Bearer`
  - admitted, within the declared 32-character threshold (advisory A-2): 31 hex characters, 31 alphanumeric
    characters, a 41-character value split by `.`, and a contrived closed-id-shaped value (`<31 random>_Abcdef...`).
- **D-4.** N1-N4 and N6-N8 run as in-suite controls (`ProveCallerMutants`), and NC3 does too (`ProveDiagnosticMutants`).
  Each throws when it does not raise, and they ran inside green CP-20 and CP-26. N5 and SM-C1 were also applied
  as real source mutants and went red (Mutants). New shapes still survive: D-2.
- **D-6.** `[NotInParallel]` (no key) is on `LiveLaneIsolationTests`, and a reflection witness requires it.
- **D-7.** CP-23 is Min 4. CP-24 is Min 4, plus the wrapper's CP-24-PARITY row.

## Mutants (each red, then restored and green)
Source-read mutants ran in mutant worktree `wM` against its own HEAD builds. Compiled mutants were built into
fresh `bin-rvcfa-*` outputs in `wN`/`wO`, one build-slot lease per build and one per run. Restored green was the
same witnesses on the unmutated HEAD dll in `wM`.

| Mutant | Witness | Red message |
|---|---|---|
| X6 `var exe="claude"; await runner.StartAsync(exe, launch.Args);` | ClSessionUsesAttestedExecutable | `audit/Control_auto_connects... required consumed launch delegation` |
| N5 `Console.WriteLine(GetFolderPath(UserProfile))` in canary | CanarySinksCannotBypassSanitizer | `V-6/raw-console unprotectedSuccessSinkCount` |
| X2 `HasBareLaunch` returns false | ClSessionUsesAttestedExecutable | `V-1/bare-launch lambda must fail` |
| P3 drop `tokens[3] != source` | CwdInstructionsCannotLeak | `V-8/source-token expected typed settings-unobserved refusal` |
| SM-S4 drop field-value `IsToken` | EvidenceIsSanitized | `V-5/field-token` |
| S2 `premise-absent => ReadyTimeout` | S1cOutcomeFeedsS1bClassifier | `outcome premise-absent emitted 20 ReadyTimeout` |
| P7 `ResolveProjectsRoot` ignores launch env | DedicatedConfigOwnsTrustAndEvidence | `V-13/projects-root reader uses the profile config directory` |
| S1 remove the `premise-absent` arm | S1cOutcomeFeedsS1bClassifier | `outcome premise-absent did not emit a setup skip` |
| P6 `LocateAsync` ignores `_projectsRoot` | DedicatedConfigOwnsTrustAndEvidence | `V-13/projects-root tailer bound /tmp/c1545-tailer-.../decoy/...jsonl` |
| S4 filter `(SetupStatus(..) ?? "ReadyTimeout") is string setup` | S1cOutcomeFeedsS1bClassifier | `profile-audit/Control_auto_connects...-filter` |
| SM-C1 `Console.Out.WriteLine(snapshot.RenderedScreen)` in C680 | C680EvidenceIsSanitized | `V-7/raw-file unprotectedC680SinkCount` |
| S1a SM-A1, A2, A3b, A4, A5, A6, A7b (one batch, separate lines) | CP-30 FullCensusUnchanged | `expected-refusal-missing` seed-nonancestor, producer-provenance, unknown-new-uid, remove-Integration, pending-producer, seed-unlisted-edit, extra-expansion (exit 1); restored `accepted` exit 0 |
| S1a early-final / skipped-receipt / cleanup-removed | -Case ExportPending / ProducerReceipt / ScratchResidue | `finalized-early finalized-on-failure`; `census-skipped-receipt`; `residue-fail x3 dangling-commit-fail ...` (exit 1); restored `accepted` x3 |
| X4 `DelegatedCalls` drops the argument check | ClSessionUsesAttestedExecutable | `V-1/bare-launch lambda must fail` |
| S6 `SetupSkip` returns `"20 ReadyTimeout"` | S1cOutcomeFeedsS1bClassifier | `outcome premise-absent emitted 20 ReadyTimeout` |
| P8 `StartTailerFor` passes `projectsRoot: null` | DedicatedConfigOwnsTrustAndEvidence | `V-13/projects-root tailer bound /tmp/c1545-tailer-.../decoy/...jsonl` |
| S5 extra arm `"provider-error" => "ReadyTimeout"` | S1cOutcomeFeedsS1bClassifier | `outcome provider-error became 20 ReadyTimeout` |

Restored green on the HEAD dlls: Pty ClSessionUsesAttestedExecutable, CwdInstructionsCannotLeak, EvidenceIsSanitized and CanarySinksCannotBypassSanitizer 4/4. Antiphon.Tests S1cOutcomeFeedsS1bClassifier, C680EvidenceIsSanitized and linked_shared_sources_are_literal_compile_items 3/3. S1cOutcomeFeedsS1bClassifier and DedicatedConfigOwnsTrustAndEvidence 2/2, in the same run as the LoadTimeout flake rerun. The batches combined independent mutants in different files, each with its own witness. Every witness failed with its own expected message.
In-suite controls such as SM-S1, SM-S2, SM-S5, SM-S6, SM-C2 and N1-N8 throw when they do not raise. They ran inside green CP-20 and CP-26.
SM-S4, SM-C1 and N5 were also applied as real source mutants (above).

## Sweep (checkpoint tool, tool dll built once; every receipt `source=330c6004 state=clean`)
| Row | Run (worktree) | Result |
|---|---|---|
| CP-25 / CP-1 / CP-30 (S1a group, `-CensusBaseSha ae885bfe9`) | `20261101-005520-36b3` / `-010815-75fd` / `-014214-9e27` (wD) | 1/1, 2/2, exit 0 `accepted`; receipts VALID (exit 0 is vacuous, D-1) |
| CP-18 (isolation wrapper producer) | `20261101-005721-e713` (wE) | 6/6, VALID |
| CP-24 + CP-24-PARITY (wrapper consumer) | `20261101-010851-0901` (wE) | 4/4 + 1/1, VALID, `PROFILE COMPLETE` |
| CP-20 + CP-26 (evidence wrapper) | `20261101-005724-3e92` (wF) | 2/2, 2/2, VALID |
| CP-29 | `20261101-014128-a93d` (wF) | exit 0, VALID |
| CP-2 / CP-16 / CP-23 / COV (one Antiphon.Tests build) | `20261101-005759-874f` (wG) | 3/4 (WrapperOnlyPaidManifest, inherited) / 6/6 / 4/4 / 15/15 |
| CP-17 / CP-3 / CP-19 (one Pty build) | same | 3/3, 5/5, 4/4 |
| Policy witnesses Pty / SessionRunner / PtyHost | same | each 2/3; `Policy_rows_require_pinned_attribute legacy-digest-mismatch 8397984d` (agents-pty, session-runner, pty-host), inherited |

COV is PlanCoverageBindingTests (7), RcRosterRepairTests (3) and C1514LandingSplitBudgetTests (5: C1404, C1577, C1584, C1410, G2865).
Inherited reds were not caused by this set:
- WrapperOnlyPaidManifest `full-inputs contract mismatch`: `tests/test-execution-policy.json` and nightly-common/policy/coverage are byte-identical to master.
- 8397984d: `NativeLinuxPolicyWitness*.cs` and the policy file are byte-identical to master.

**Whole Unit lane** (Antiphon.Tests `[Category=Unit]`). Three lanes, one build each, the same 15-shard partition as round 1. Runs: `20261101-005311-39a2` (uA), `-005316-bef8` (uB), `-005322-08ab` (uC). A source census confirms every class in a file with `Category("Unit")` matches a shard prefix (530 classes); this is not a metadata identity proof.

| Lane | Shards (total/failed/skipped) |
|---|---|
| A (RemoteScriptContractTests) | 217/0/0, 50/0/0, 29/0/0, 83/0/0, 126/1/0 |
| B | 567/0/1, 12/0/0, 78/0/0, 38/0/0 |
| C | 374/0/3, 1250/0/10, 679/0/12, 1269/5/10, 1090/0/8, 501/0/0 |

**TRX union: 6,363 results: 6,313 passed, 44 skipped (platform), 6 failed.** That matches round 1 on `0b903dd9d`; D-1..D-7 added no new test methods.
Failures:
- `RemoteScriptContractTests.Custody_residue_counts_subdirectories_only`: inherited (`scripts/c590-remote.sh` and the test are unchanged from master).
- `NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute` and `NativeLinuxPolicyWitnessMutationTests` `New_exclusion_without_marker_fails` / `Marker_pin_and_origin_are_enforced` / `Lookalike_attribute_identity_is_rejected`: inherited 8397984d.
- `LoadTimeoutBoundWitnessTests.Checkpoint_positive_wait_accepts_a_late_death_and_rejects_a_hook_that_never_dies`: load flake (`wall.Elapsed`); passed alone on the HEAD dll.

uA CP-2 (RSC C111-C119, 50/50 passed) has the row state `roster-miss`. The tool cannot roster a method-level OR filter; this is a tool artifact, not a test result.
Native CP-4..CP-7 and CP-21 stay owed. CP-27/CP-28 are baseline captures, not ordinary rows for this tree.

## Land graph, evidence diff, merge
- `audit-land-verification-graph.ps1 -Check` (LandGraphAudit built Release under build-slot) exited 1:
  `digest=1649974D464009999E5047B1F1DFA1ECA33487D9C14F7F114F54529B74AF1718 count=68`, `manifest does not match the selected graph`.
  The digest is the only difference:
  - the recorded manifest and evaluation line hold `7BDABBA0...`
  - the pins blob `d7513d00` equals `HEAD:` pins
  - the SDK `10.0.403` equals `dotnet --version`
  - the evaluator, run directly (`--repo --solution Antiphon.sln --canary-record`), gave `phase=pass2 approved`, exit 0, and removed its canary.
- `check-evidence-diff.ps1 -Repository . -BaseRef 5aea2708f -HeadRef 330c6004`: commits=102, entries=21, violations=0.
- `git merge-tree --write-tree origin/master HEAD`: tree `06f39ee283a49a532639bfef03f55721083f8aa1`, exit 0, no conflicts.

## Advisories
- A-1 (carried) The CP-30 producer receipts and exports are unauthenticated JSON; node lists are not digested
  (my JSON-edited exports were accepted as candidate evidence).
- A-2 The token filter still admits values within the declared threshold: 31 hex characters, 31 alphanumeric
  characters, `.`-split 20+20 runs.
- A-3 `scripts/test-live-lane-isolation.ps1` still leaves `/tmp/c1545-portable-*` behind, and its committed slot
  wait of 25 minutes and total of 26 minutes are too tight on a loaded server2. I ran gitignored copies with 75 and
  90 minutes; the evidence wrapper copy used 75.
- A-4 `IsProjection` admits any `LiveClaudeEvidence.*` member; an explicit member allow-list would stop a future
  raw passthrough from being counted as safe.
- A-5 PCs stay pending for SourceLanding Mutation. Native CP-4..CP-7 and CP-21 stay owed: no live Claude runs on this host.

## Changed input waits
No session input waits changed. The test-only waits are unchanged: the classifier child (2.5 s, then kill), the
roster child (320 s), and the tailer bind poll (2 s). Parking is disabled, so there is no automatic release deadline (CARD-1083).

## Scope and process notes
- ordinaryScopeCompleted: **Full**. Every row named in the brief ran on `330c6004`: all slice rows, the coverage
  bundle with the census cases, the policy witnesses of the three other suites, and the whole Unit lane once (the
  Antiphon.Tests policy witness is inside the Unit lane). Native CP-4..CP-7 and CP-21 are lane-only, owed by design.
- reviewedSourceClean: **false**.
  - VALID receipts: S1a CP-25/CP-1/CP-30, CP-18, CP-24 with its parity row, CP-20/CP-26, CP-29.
  - INVALID: the Unit lane and sweep receipts. Lanes A and C, and the sweep (wG), are `cleanup_refused`, because a
    red run keeps its outputs. Lane B is `row_failed` because of 1 platform skip. The only reds are the inherited ones listed above.
- These ran without a build slot because they are not dotnet build or test drivers:
  - the census, census-mutant and S1a script-mutant commands, run directly as pwsh scripts;
  - the Add-Type sanitizer probe;
  - the reflection sink-audit probe, which loaded the HEAD Pty test dll into a private AssemblyLoadContext.
  
  Every build and every dll test run had its own build-slot lease, with a fresh `bin-rvcfa-*` output.
- Cleanup:
  - mutant worktrees `task-cffa0c4b-wM/wN/wO`, every `bin-*` output and `/tmp/c1545-portable-yD5Wg4` were removed;
  - the evidence worktrees `/work/worktrees/task-cffa0c4b-{uA,uB,uC,wD,wE,wF,wG}` are kept for their ignored
    receipts, and can be removed after triage;
  - the S1a cleanup-removed mutant may have left one dangling commit object in the shared object store, which
    `git gc` collects; no object was deleted by hand.
