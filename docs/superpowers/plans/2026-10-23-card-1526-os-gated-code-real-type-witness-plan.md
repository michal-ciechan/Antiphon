# CARD-1526: OS branch witnesses and AppHost restart rehearsal

Date: 2026-10-23. Stage: Plan; verification design is a separate TestDesign dispatch.
Owner: CARD-1526 on Antiphon. Incident/fix owner: CARD-1525; original change: CARD-1506.
Inspected source: `72c2464cfe572a4b4a5881c09ab1f0265b43ba51`.
Plan owner: task `661a0adc-33d1-4711-87e6-3f475ad8e12e`, branch `feat/card-task-661a0adc`.

The change makes OS-dependent verification identify the actual runtime object,
the selected production branch and the input that distinguishes it from a test
double. Changes to the AppHost startup path additionally need a native Windows
probe before an rc pick and a restore rehearsal at the published rc SHA before
activation. A successful mock, static lint or stop preview supplies none of those
execution claims.

The Plan owner publishes this document as documentation through its own landing
owner. This dispatch does not implement the checklist, change the live rc line,
restart anything, or declare CARD-1525 fixed. No operator question is required.

## Ground truth

Read CARD-1526 and CARD-1525 in full through `scripts/card.ps1 get ... -Board
Antiphon`. The outage times and recovery below are incident facts from CARD-1525,
not a new live-host investigation. Read owners: `docs/orchestration-loop.md`
(standing rules, stage contracts, rc ports, D-2), `docs/testing-and-build.md`
(checkpoint manifest, slots and coverage lint), `docs/release-gates.md` (release
line, rc integration, cut/test/publish and rollback), `docs/apphost-runbook.md`,
`docs/project-context.md` and `server/Bundles/README.md`.

| Card assumption | Inspected implementation / evidence | Planning consequence |
|---|---|---|
| Review passed a Windows-shaped dictionary seam instead of the real type. | `scripts/test-build-driver-environment.ps1` already constructs real `ProcessStartInfo` for both its six wrapper scenarios and four direct cases. Every scenario assigns the opt-out key, including `''`; none removes it. | Correct the example: a real-type witness alone is insufficient. Require the meaningful absent/default/boundary input and assert its setup. Do not rewrite the incident as entirely a mock-type problem. |
| The fault needs native Windows to reproduce. | `scripts/lib/BuildDriverEnvironment.cs:GetOverrides` reads the optional key with an indexer when `windows` is true. A leased Linux diagnostic at the inspected SHA, forcing that parameter, reproduced `KeyNotFoundException` on the real `StringDictionaryWrapper` with the key absent. | Portable real-type witnesses are useful and required where the branch can be forced. Native startup probing remains a separate integration obligation. |
| A normal dictionary is equivalent. | In the same diagnostic, a plain `StringDictionary` accepted absent, empty, `0` and `1`; the real wrapper failed only absent. Both accepted the other three inputs. | Name the object obtained from the production factory/property, not merely its declared base/interface type. Never replace it with a copied dictionary for the witness. |
| `restart-apphost.ps1 -WhatIf` can rehearse restart. | The script rejects `-WhatIf` without `-StopOnly` with exit 3. `-StopOnly -WhatIf` returns before restore/build/launch. `dev-aspire.ps1` restores via `build-slot.ps1 -Label apphost-restore -- dotnet restore $appHostDir`. | Use the actual restore substep. Do not run `dev-aspire.ps1` just to obtain a rehearsal: its other operations are real startup operations. |
| A successful native version command proves restart. | `build-slot.ps1` enters the Windows environment policy before starting its child, so `dotnet --version` traverses the failed boundary cheaply. It does not exercise restore, build, health or version equality. | Record probe, restore rehearsal and actual activation as three different outcomes. |
| A recovery one-liner already exists. | The AppHost runbook documents lock ownership, exit codes and canonical restart, but no rc-pick-specific source compensation recipe. CARD-1525 records temporary process and USER opt-out as incident first aid. | Add a forward revert recipe and link the existing restart/lock rules. Do not turn persistent opt-out into ordinary recovery or silently disable the policy. |
| Code and Review have no relevant checklist surfaces. | `stage-code.md` is 2,399 bytes; `stage-review.md` is 2,476 bytes. `TaskPlatformGuidanceTests` pins Review instructions and the 2,480-character cap; `CheckpointRepeatDocumentationTests` requires Code below 2,410 bytes and ASCII. | Add compact owner pointers by deliberate compression, retaining every existing obligation and budget. Update only the affected literal pins, never raise a cap. |
| A new lint must prove execution or control Review verdicts. | Existing plan coverage is syntax-only. Standing rule 4 defines `found`; D-2 separates acceptance, required evidence and advisory defects. | New lint checks declared coverage and provenance structure. Review still judges actual runtime identity/reachability, and the existing verdict policy is reused. |
| Fleet location can be copied into future briefs. | GET `/api/runner-defaults` and GET `/api/session-runners` succeeded: defaults revision 2; an eligible Windows lane and an eligible Linux lane were available, plus an unavailable draining entry. | Re-read placement at execution. Portable work stays unpinned; only native work uses `-Platform Windows`. Do not hard-code a runner or an rc branch date. |

Diagnostic provenance: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label
card1526-ground-truth -- pwsh -NoProfile -File .antiphon/card1526-probe.ps1`, lease
`c2724d69-7f51-4a45-b1e5-df8ed42df54d`, waited 0 seconds, held 4 seconds, exit 0;
eight observations, one expected exception and seven successful calls. This was
a bounded premise check, not a passing regression checkpoint. Two earlier
diagnostic attempts failed in fixture setup because PowerShell enumerated the
dictionary; neither is defect-red evidence. Generated diagnostic files stay ignored.

## Decisions

- **D-1 — Scope the new obligation to changed behavior.** Every changed OS-selected
  production behavior needs a witness using the real platform object and its
  discriminating input. Audit changed helpers and their OS-gated callers, even
  if the OS check itself was not edited. Do not retroactively require witnesses
  for every unchanged branch in the repository. Rejected: a repository-wide
  coverage campaign, or checking only newly added `if` statements.
- **D-2 — Preserve the observed distinction.** For this incident, absent, empty,
  `0` and `1` are separate inputs; absent setup explicitly removes the key and
  asserts absence before calling production. CARD-1525 owns the repaired witness
  and runtime fix. CARD-1526 links its final file/method and reviewed SHA when
  available; it neither duplicates that fix nor credits the diagnostic as green.
- **D-3 — Use portable execution first, native execution where necessary.** Force
  an existing OS parameter while retaining the real type when possible. For a
  native-only API, place the witness on that OS and record the exact reason.
  Do not add a production bypass solely to make a native API execute elsewhere.
  A native-only witness that has not run remains pending, never a passed skip.
- **D-4 — Add a bounded evidence lint, not a semantic compiler.** A small
  PowerShell checker validates the changed-source inventory and structured
  witness records described below. It does not execute user-provided commands,
  attest runtime object identity, prove all OS dependencies, change server land
  admission, or replace the existing checkpoint receipts. Rejected: new Roslyn
  dataflow analysis, a new server evidence schema and mandatory whole suites.
- **D-5 — Keep the native probe an operational command.** Use existing
  `build-slot.ps1` and `dotnet --version`, with opt-out absent in the probe
  process, before publication. After publication, repeat at the rc SHA and run
  the existing restore command. No new restart/probe service or `-WhatIf`
  behavior is introduced. Rejected: stop-only preview as build proof, Windows
  simulation as native credit, or a permanent USER opt-out.
- **D-6 — Roll back by new history.** A failed pick is compensated by a reviewed
  revert on the current rc line, then ordinary publication and canonical
  activation. Never move rc backwards, reset a shared checkout, force-push or
  mutate a release tag. If the failure is infrastructure-only, retain the
  failure and use its existing Debug/recovery path rather than reverting
  unrelated source on the strength of a timeout alone.
- **D-7 — Reuse Review policy verbatim.** The owner remains
  `docs/orchestration-loop.md#standing-operating-rules`, rule 4:
  "`found` is for a regression, a new reachable fail-open or a test that cannot
  fail." Keep the rest of that rule and the D-2 advisory policy unchanged.
  Missing required execution is verification incomplete under D-2, not a new
  category of product regression. Do not manufacture Clean/Full or request a
  Review solely to repair advisory prose.
- **D-8 — Centralize the instructions.** Put the full witness contract in
  `docs/testing-and-build.md#os-branch-witnesses`; Code/Review bundles carry a
  short mandatory pointer. Put rc sequencing in `docs/release-gates.md` and
  recovery in `docs/apphost-runbook.md`; orchestration links those owners.
  These are executable instructions/policy and take ordinary Review; D-2's
  narrative-only exemption does not apply.
- **D-9 — No new input-waiting session.** This change adds no agent session,
  question, parking policy, timer or automatic release. Native work is a
  separately scoped execution task when needed, not a Plan seat waiting for an
  operator. Existing parking-off semantics remain unchanged.

## Witness contract and lint boundary

The Code/Review checklist must say: identify each changed OS branch and its real
runtime factory/property; inspect a witness that drives that object through the
selected production branch; assert the input setup and the observable result;
name its exact checkpoint and native obligation; check startup-path impact before
an rc pick. A mock/seam may supplement this witness but cannot replace it.

Add `scripts/check-os-witnesses.ps1 -BaseRef <full-sha> -HeadRef <full-sha>
-EvidencePath <ignored-json> -Plan <plan.md>`. Operate on pinned Git objects, not
on unstaged source, and report the two resolved SHAs. Reject unresolved refs and
malformed input. Keep generated JSON under ignored evidence paths; document its
shape in the testing owner rather than committing actual receipts.

Version 1 input contains `version`, `baseSha`, `headSha`, `inspectedPaths`,
`witnesses` and `restartImpact`. Every changed executable source/test path gets
one inventory entry with a disposition (`os-gated`, `portable`, `test-only`,
`removed`) and a reason. Recognize at least C#, PowerShell, shell and JS/TS source
extensions; an unrecognized changed non-document path is reported for explicit
classification, not silently ignored. Renames retain both identities. Deleted
code can be classified removed; new callers of existing OS-gated helpers must
name those helpers as additional inspected paths.

A witness entry has a stable id, source path and branch anchor, target OS,
production type/factory, test path and exact method or script case, setup/input,
asserted outcome, portable-forced or native execution mode, and checkpoint id.
For optional configuration reads its input record distinguishes `absent`, `empty`
and `value`, names the key and supplies a setup assertion; an empty value cannot
be described as absent. A native obligation records its target OS, exact source,
command and receipt reference or an explicit pending state. Fixture/forced-mode
records cannot fill that native slot. These are structural consistency checks,
not validation that the referenced run actually happened.
The linter verifies required nonblank fields, inventory coverage, existing source
and test paths at the pinned head, and a checkpoint id present in the plan. A
removed path is checked against the base instead. Duplicate or contradictory
records fail. Runtime receipts are referenced separately; strings such as
`realType` are not proof that a test used it.

Scan complete changed files at both revisions for a bounded documented set of
OS-check spellings (`OperatingSystem.Is*`, `RuntimeInformation.IsOSPlatform`,
`Environment.OSVersion.Platform`, PowerShell OS variables, OS preprocessor guards
and shell/Node platform checks). Emit candidates with path/line; require a
witness or a reasoned non-behavior disposition for each candidate. A review of
the diff/callers remains mandatory: a regex cannot discover indirect behavior,
aliases or every language. Preserve diagnostic candidates even when dispositioned.
Do not label exit 0 "OS coverage proved". Exit 0 is complete syntax/inventory,
1 is a missing/contradictory obligation, 2 is unreadable/invalid input.

`restartImpact` records whether the delta touches `dev-aspire.ps1`,
`scripts/restart-apphost.ps1`, `scripts/build-slot.ps1`, `scripts/lib/**`,
`scripts/apphost-common.ps1` or `Antiphon.AppHost/**`, plus any startup dependency
identified by review. Match either side of a rename/delete. A matching executable
delta requires native probe evidence before an rc pick; doc/test-only changes
can say not applicable with their actual changed-path census. The lint requires
the native obligation to be named, not executed during a portable lint run.

## Native probe, rc rehearsal and recovery

Add the following procedure to the existing rc integration text, with an owner
link from standing rule 9 and the Code/Review brief guidance. It supplements the
reviewed pick procedure; it does not change merge/cherry-pick authority.

1. **Before rc publication:** record current rc ref/tip, the last healthy running
   SHA, the reviewed source SHA and the prerequisite chain. Prepare the reviewed
   integration candidate. On native Windows at that exact candidate, run the
   cheap probe in a fresh process with the opt-out genuinely absent. An older
   master-source receipt does not prove a changed integration candidate. Keep
   the native receipt in Review evidence or an exact-candidate verification
   attachment before the pick is authorized; do not relabel the original Review.
2. **Probe command from that checkout:**

   ```powershell
   pwsh -NoProfile -Command {
       $ErrorActionPreference = 'Stop'
       if (-not $IsWindows) { throw 'native Windows required' }
       foreach ($name in @('C589_SLOT_SHIM', 'C589_COMMAND_SHIM', 'ANTIPHON_APPHOST_TEST_SEAMS')) {
           if ([Environment]::GetEnvironmentVariable($name, 'Process')) { throw "test seam present: $name" }
       }
       [Environment]::SetEnvironmentVariable('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT', [NullString]::Value, 'Process')
       if ([Environment]::GetEnvironmentVariables('Process').Contains('ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT')) { throw 'opt-out must be absent' }
       $dotnet = Get-Command dotnet -CommandType Application -ErrorAction Stop
       & ./scripts/build-slot.ps1 -Label rc-native-probe -- $dotnet.Source --version
       exit $LASTEXITCODE
   }
   ```

   This is one native PowerShell command; the block runs in the fresh child.
   The process-local removal leaves USER/MACHINE settings unchanged. The absence
   assertion uses key presence, not an empty-string comparison. Refuse inherited
   test shims rather than silently accepting fixture evidence.
   Check native OS, resolved real `dotnet`, PowerShell version, clean checkout
   and expected SHA before execution. Record sanitized output, UTC time, source
   SHA, OS/runtime, opt-out state, slot grant/release and child exit code. Do not
   dump the environment. One wrapper owns the slot: do not wrap this command in
   a checkpoint row that takes another slot. An unavailable/timeout/unleased
   probe is not qualified native evidence; do not retry without the gate.
3. **After pushing rc:** read the remote tip back and advance the clean canonical
   checkout through the existing rc procedure. Verify it equals the published
   SHA. Repeat the native probe there, then run the restore-only substep with
   opt-out absent using the same process-local wrapper pattern:

   ```powershell
   pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-restart-rehearsal -- dotnet restore Antiphon.AppHost
   ```

   Use the same fresh-process block from step 2, changing only the wrapper label
   and replacing `--version` with `restore Antiphon.AppHost`. The bare restore
   line above names that underlying substep. This does not stop AppHost, run npm,
   create a second launcher or waive the existing canonical-checkout guard.
   `restart-apphost.ps1 -StopOnly -WhatIf` may describe a stop, but cannot satisfy
   rehearsal. Do not add a fake `-WhatIf` pass to the evidence.
4. **Activate only after rehearsal passes:** follow the current AppHost runbook
   from an independent owning shell, honor both locks and the Job Object caveat,
   then require restart exit 0, health and `/api/version` equal to the intended
   rc SHA. Publication, rehearsal and activation remain separate recorded states.
   A healthy old process is not completion of this pick.
5. **Failed startup path:** preserve the error and logs, mark the pick unactivated
   and commission the existing Debug path. If attributable to the pick, prepare
   its forward revert. For a single ordinary pick the source recovery line is:

   ```powershell
   git revert --no-edit <exact-bad-pick-sha>
   ```

   This runs in a separately assigned recovery task based on the current rc tip,
   with explicit publication authority, not in the running product checkout.
   For a multi-commit bundle, enumerate its exact commits newest first; for a
   merge, review its parent and use the explicitly selected mainline. Never
   blindly revert a SHA range containing intervening work. Review and publish
   the resulting revert by a normal fast-forward to the named rc ref; if the
   target moved, stop for ordinary integration, never force. Update the canonical
   checkout, repeat probe/restore and restart with the new revert SHA. This
   one-line source compensation does not claim that service recovery is already
   complete. Retain failed receipts and confirm the recovered `/api/version`.

The temporary USER opt-out recorded by CARD-1525 is a separate incident cleanup
obligation. Its repair owner removes it only after the repaired rc source and
required service restarts are verified. Do not change that setting from this
process-policy implementation, or count a probe with opt-out `1` as coverage.

## Implementation slices

| Slice | Files and change | Tests / finish condition |
|---|---|---|
| S1 — Pin the missing policy and checker behavior | New `scripts/test-os-witnesses.ps1`; fixtures are generated in owned scratch repositories. Add only necessary literal-pin updates to `tests/Antiphon.Tests/Application/TaskPlatformGuidanceTests.cs` when S3 changes bundle wording. | Commit a policy-surface case before owner/bundle changes; it must fail on the missing OS-witness requirement, not missing tooling or fixture setup. The harness also specifies the lint cases below. Record that one pre-fix diagnostic separately from final ordinary receipts. |
| S2 — Evidence lint | New `scripts/check-os-witnesses.ps1`; complete the S1 harness against the pinned Git/evidence contract. No product runtime, CI release predicate or server admission changes. | Portable lint cases: complete forced real-type declaration; native-only pending obligation; missing witness; absent-vs-empty setup declaration; candidate/disposition mismatch; helper inventory; restart-path rename/delete; invalid SHA/path/checkpoint; fixture-only receipt rejected as native; documentation-only not-applicable. All are checker tests, not native execution claims. |
| S3 — Owners and delivered pointers | `docs/testing-and-build.md`, `docs/orchestration-loop.md`, `docs/release-gates.md`, `docs/apphost-runbook.md`, `server/Bundles/stage-code.md`, `server/Bundles/stage-review.md`, and the affected literal pins in `TaskPlatformGuidanceTests.cs`. | Full recipe, record format, failure handling and corrected incident example; policy-surface test green. Preserve the Review evidence block, D-2/standing verdict rules, repeat budget, platform rules, input-wait rule and bundle caps. Existing checkpoint-documentation tests stay green. |

CARD-1525 is a dependency for the worked runtime witness and any actual Windows
startup qualification of that repaired source. It is not a dependency for writing
or reviewing the generic checklist/lint. Re-read its final implementation before
binding a method name; no speculative method names or historical CP counts become
acceptance receipts. Do not edit its policy implementation or regression script in
this card. Avoid parallel Code edits to the owner documents or stage bundles.

## TestDesign handoff

This brief did not fold TestDesign into Plan. The next stage adds `## Verification
design` with the exact harness case census, V/R obligations, guard inventory,
method-scoped PC design and cost, then freezes the two ordinary rows below. It
must qualify the linter's limits and evidence semantics, including invalid inputs
and the false claim that empty means absent. A checker pass cannot prove the real
runtime object or branch was executed; only the referenced test and fresh receipt
can do that. No human choice or new operator questionnaire is pending.

The pre-fix red is the S1 policy-surface script case, executed once through
`build-slot.ps1`; it is expressly budgeted diagnostic evidence, not a successful
ordinary checkpoint. No production mutation is authorized during Code. Final
ordinary verification runs after all three slices are committed, so it requires
one isolated .NET build in total. Lint harnesses exercise local fixtures only.

## Verification design

Code brief task f93ec86b explicitly skips a separate TestDesign dispatch and
freezes this bounded design. Final ordinary scope is CP-1/CP-2 only; no Unit lane,
whole suite or live Windows operation. All new checker tests invoke the actual
checker against privately committed Git fixtures and assert both exit code and
specific diagnostic. `-Case <name>` selects a single script case for later
Mutation binding. `-PolicyOnly` is the separately budgeted pre-fix red witness.
No command in a witness record is executed by the checker.

| ID | Named cases / invariant | Ordinary row |
|---|---|---|
| V-1 | `forced-real-type`, `seam-only`: complete real-type plus absent/empty/value declaration passes; seam alone fails | CP-1 |
| V-2 | `native-pending`, `fixture-native`: explicit native pending remains pending; forced receipt cannot occupy native slot | CP-1 |
| V-3 | `missing-witness`: changed OS behavior needs a witness | CP-1 |
| V-4 | `empty-is-not-absent`, `absent-case-required`: presence/absence setup and all input states | CP-1 |
| V-5 | `candidate-mismatch`, `candidate-contradiction`, `candidate-reason`: complete-file candidates persist and need consistent dispositions | CP-1 |
| V-6 | `helper-missing`, `helper-complete`: caller's named helper inventory at both revisions | CP-1 |
| V-7 | `restart-rename-missing`, `restart-rename-pending`, `restart-delete`: old path triggers native obligation | CP-1 |
| V-8 | `sha-mismatch`, `unresolved-ref`, `missing-path`, `missing-checkpoint`, `duplicate-inventory`, `malformed-record`, `unsafe-path`, `pinned-source`: identity, structure and pinned-object validation | CP-1 |
| V-9 | `unknown-classification`: unknown non-document paths need classification | CP-1 |
| V-10 | `documentation-only`: correct not-applicable census passes | CP-1 |
| V-11 | Repair: `test-seam-only`, `test-always-assigned`, `test-comment-only`, `test-case-missing`, `test-comment-strings`, `test-line-comment-strings`, `test-hash-comment-strings`, `test-factory-missing`, `test-name-call-only`, `test-name-prefix`, `real-absent-test`, `pinned-test-ignores-worktree`, `pinned-test-rejects-worktree-repair`: require pinned test declarations, code tokens and setup assertions | CP-1 |
| R-1 | `policy-surface`: mandatory owner/bundle pointers, corrected incident, native absent-key recipe, restore and forward recovery (26 assertions) | CP-1 |
| R-2 | Five TaskPlatformGuidanceTests, eight CheckpointManifestDocumentationTests, one CheckpointRepeatDocumentationTests; existing caps, platform, checkpoint and repeat obligations preserved | CP-2 |
| R-3 | Manual diff: standing rule 4/D-2 unchanged; no runtime policy repair, desktop/release operations or persistent opt-out change; clean merge-tree and sequential rc cherry-pick simulation | Manual |

CP-1 preserves the original 25 named checker cases (50 exit/diagnostic assertions)
and 26 policy assertions, adding 13 repair cases (26 assertions): 102 total,
all required, no skipped group. Each checker process has a
15-second kill/join deadline. The ordinary command row is capped at two minutes;
CP-2 at eight minutes, with one isolated build. Normal green is run once; only
failed rows are repaired/rerun. Pre-fix policy red was committed before the fix.

Unbound Mutation candidate: `docs/superpowers/mutations/2026-10-23-card-1526-os-witnesses.md`.
PC-1 real-type guard (`seam-only`), PC-2 absence guard (`empty-is-not-absent`),
PC-3 candidate coverage (`candidate-mismatch`), PC-4 native mode guard
(`fixture-native`), PC-5 startup old-path census (`restart-rename-missing`) remain
pending for SourceLanding Mutation, including deliberate red/restore/green and
missing-control discovery. Code does not run those mutants. Native Windows
probe/restore is future affected-pick work, not deferred ordinary work for this
portable policy card. CARD-1525's reviewed runtime witness remains externally
owned and unbound while its repair is InProgress.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | n/a | portable-os-witness-lint | `pwsh -NoProfile -File scripts/test-os-witnesses.ps1` | Lint contract, pinned test code, policy pointers, native recipe/rollback document checks | All 38 checker cases (76 assertions) plus 26 policy assertions pass (102 total); exit 0; no skipped group. | n/a | 2 |
| CP-2 | S1-S3 | `tests/Antiphon.Tests -> bin-c1526/` | portable-bundle-contracts | `/*/*/(TaskPlatformGuidanceTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)/*` | Existing bundle caps, preserved instructions, placement, checkpoint and repeat-policy obligations | All 14 existing methods pass, 0 failed/skipped; no sibling `RunnerDefaultGuidanceTests` selected | 14 | 8 |

Both rows name the portable lane and stay unpinned. Execute them in one
checkpoint-tool run with exact committed source, await completion, retain the
unedited lines and validate receipts. Command rows already own a slot; their
harness must not acquire a second production slot. If a harness exercises a slot
wrapper, use its isolated fixture broker only. Remove owned `bin-c1526/` outputs
after all runs finish. Run `scripts/check-evidence-diff.ps1` over task base..HEAD.

The native pre-pick and post-push restore commands are future **Windows rc
operations**, not hidden extra ordinary rows for this doc/lint implementation.
This card changes none of the startup executable paths. Its Code/Review claims
portable policy/lint verification only; a doc test does not claim a native smoke
ran. When those commands are required for an affected product pick, commission
only that Windows piece with its exact SHA/filter/command and budget; leave the
remaining work unpinned and record the native results in the Review/pick record.

### Cost and completion

Ordinary verification estimate: 10 minutes plus one pre-fix script run of at most
2 minutes and actual slot wait. Authoring/TestDesign is additional. No whole Unit,
namespace, full assembly, E2E or rc roster run belongs to this card's ordinary
scope. Failed selected checks are fixed and rerun narrowly; inherited failures
are confirmed only with the same selection at the base. Never label current
CARD-1525 red as introduced by this process change.

Completion needs the committed policy/lint, both ordinary checkpoints, an
independent Final/Full Review and normal land. Bundle activation follows the
existing reviewed rc delivery procedure when commissioned; a master land alone
does not activate instructions embedded in the running server. Preserve the
separate source/activation and pending-native facts in the report. Mutation and
its companion follow the existing pipeline even if TestDesign records zero PCs.

--- next stage ---
next: test-design
handoff: Add verification design for CARD-1526's bounded witness lint and policy/rc procedure changes; freeze the two portable checkpoints, preserve the real-type plus absent-input distinction, bind CARD-1525's final witness separately, and add no broad suites or operator questions.
artifact: docs/superpowers/plans/2026-10-23-card-1526-os-gated-code-real-type-witness-plan.md
