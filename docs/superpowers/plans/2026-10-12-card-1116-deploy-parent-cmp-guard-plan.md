# CARD-1116: deploy-parent Compose comparison before recycling

Plan, 2026-10-12. Task `96f951b3-05e8-4e65-a973-bf1391ca42f1`.
Inspected source and fetched `origin/master`:
`a91ba68a72185d57d39e87b218e459c5df2451de`. All source line references below
refer to that commit. Card read through
`pwsh -NoProfile -File scripts/card.ps1 get CARD-1116 -Board Antiphon`.

## Outcome and stage disposition

The requested production change and the requested regression tests already exist.
CARD-1105 absorbed this follow-up: landed commit
`c88411f0f6ad7da341475fb94c0aafd4e5ba350e` adds the early comparison and changes
the late refusal; `a0ca0b49908f99badd082cbe13b6be3ad2005170` adds the mismatch
test and ordering assertions. Both are ancestors of the inspected checkout and
fetched master. The original repair report explicitly says "CARD-1116 is
implemented in this slice" and leaves card closure to its caller
([repair report](../../../.antiphon/task-a2122818.md), line 22).

This is a reconciliation and qualification plan, not a second implementation.
The brief requested `next: test-design`; the stage-plan rule requires
`next: investigate` when the card's premise is wrong. Investigate must establish
whether the existing change and its evidence close the card, or identify a
specific remaining observable gap before TestDesign. No production or test
edit is justified just to recreate the already-landed patch.

This dispatch performs design and source inspection only: no builds, tests,
mutation controls, deployments, drain changes, SSH host commands or live recycle
operations. Post-land Mutation is **NOT run (paused)**. Historical reports below
are provenance, not qualification of this plan's source SHA.

## Ground truth

| Card assumption or requirement | What this checkout does, with file:line | Consequence |
|---|---|---|
| The comparison runs only after destructive recycling. | `scripts/c590-remote.sh:5944` materializes the target model; `:5946` limits the comparison to the host project and apply; `:5947` compares the target file with `$SERVER2_COMPOSE`; `:5948` calls `c1008_refuse RecycleComposeMismatch`. `c1008_lock` follows at `:5976`. | The premise is stale. The requested early guard is already present, including before the resume path can acquire its lock. |
| The late guard must remain and must use the established refusal path. | `scripts/c590-remote.sh:6169` gates the late comparison on `C1008_ACTIVE=1`; `:6170` compares the files; `:6171` calls `c1008_refuse`; `:6174` runs `compose_host up -d --no-build`. | Both requested changes are implemented. Keep the late check for drift after preflight. |
| Checkout and boot-file setup must precede recycle. | `scripts/c590-remote.sh:6129` calls `ensure_checkout`, `:6130` calls `ensure_runner_boot_files`, then `:6132` recycles. `c1008_compose_source` at `:3955` materializes both Compose files from Git objects. | The early check has both inputs available. No setup reorder is needed. |
| Early mismatch should create no journal and stop/remove nothing. | The guard at `:5947` precedes even `C1008_JOURNAL`/`C1008_RECORD` initialization (`:5952`). `c1008_refuse` only saves a journal when active (`:3903`); activation occurs later at `:5998` or `:6052`. | Fresh early refusal must be exit 2 with the diagnosis and no recycle journal. Do not create a journal to make early refusal look like a late partial failure. On resume, an existing journal must remain unchanged. |
| Active mismatch must preserve the real diagnosis in its receipt. | `scripts/c590-remote.sh:3905` sets `.diagnosis` and chooses `partial` when removed-volume count is positive, otherwise `refused`; `:3907` saves; `:3908` calls the summary; `:3910` emits the result and exits 2. | Use the existing refusal semantics, including `RecycleReceiptUnavailable` if saving fails. Do not unconditionally force `partial`. |
| Switching to `c1008_refuse` guarantees a newly printed failure summary. | `c1008_summary` at `scripts/c590-remote.sh:3886` is one-shot. Fresh recycle already calls it at `:6124`, before the late guard. The late refusal can update the journal without emitting a second summary. | Do not claim a fresh `outcome=partial` summary was proved. Investigate the actual stdout/receipt contract on fresh apply versus resume before proposing a summary-policy change. |
| Dry run remains read-only with respect to deployment state. | The early guard excludes dry run (`scripts/c590-remote.sh:5946`). The preview intercept at `:6871` invokes recycle before generic checkout/boot setup. Preview exits at `:5974` before the normal lock. Evidence-file materialization is still allowed. | Do not compare the live checkout in preview or add deployment-state writes. |
| Add a fixture mismatch test and extend the ordering test. | `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:2112` already asserts early comparison before lock/stop and late use of `c1008_refuse`. `:2145` injects a differing file through `c1008_compose_source` and requires exit 2, diagnosis, empty `Removed`, zero stop/rm/volume-rm calls, and no journal. | The exact requested tests already exist. Reuse them. |
| The mismatch test proves the late receipt behavior as well. | `C1008HostFixture.Run` in `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:1000` defaults `build_server2_images` to a successful early exit. The mismatch test at `RemoteScriptContractTests.cs:2145` targets early refusal. The late call has a source-text pin at `:2123`. | Existing focused coverage proves the early boundary and the late call site, not a runtime late-mismatch receipt. This is a coverage distinction, not proof of another production defect. |
| CARD-1105 S3 must land first. | The CARD-1105 plan already lists the folded CARD-1116/CARD-1117 controls at `docs/superpowers/plans/2026-10-06-card-1105-compose-mount-generation-plan.md:293` and CP-10 at `:457`. Its subsequent report records CP-10 at `.antiphon/task-68308978.md:136`. | The source dependency is satisfied; do not queue a duplicate fix behind S3. |
| The desktop wrapper is the place to fix guard order. | `scripts/deploy-server2.ps1:1045` invokes host case `deploy-parent` after wrapper admission checks. The guard and journal behavior are in the remote shell. | No wrapper, API, Compose-file or runner changes are indicated. |

Historical CP-10 in `.antiphon/task-68308978.md:136` records four executed,
four passed, zero failed/skipped at source
`db249df5b35274c11cf421d10f1aa29083822378`, including
`C1105_Compose_cmp_refuses_before_removal`. That is a report of a previous run;
this dispatch has not rerun it or independently revalidated its raw receipts.
The repair report uses pre-landing commit IDs; the landed IDs above come from
current Git blame/history and ancestry checks.

## Decisions

### D-1: reconcile the landed fix before commissioning Code

Investigate is the next stage because the central missing-code premise is false.
It must compare card acceptance with the landed source and existing evidence,
then report closure readiness or a concrete residual requirement. TestDesign
remains separate if more verification work is needed.

Rejected: another copy of the guard/test, a cosmetic rename to `C1116_*`, or a
Code dispatch that must manufacture a source commit. These add churn without
changing the requested behavior. This decision does not close or move the card.

### D-2: retain both comparisons and their narrow scope

Keep the early check immediately after successful target model materialization,
before journal initialization and lock acquisition, for host-project apply only.
Keep the active late check immediately before `up`, using `c1008_refuse`.

Rejected: deleting the late comparison (loses protection against later drift),
moving the early comparison under the lock (changes the requested boundary), or
applying the host comparison to temp/preview (uses an irrelevant or stale file).
The contract concerns avoiding stop/removal on early mismatch; checkout and
boot-file setup already happen first, so do not describe it as zero filesystem
activity anywhere on the host.

### D-3: distinguish early refusal from active-operation refusal

Early refusal has no new journal. Active refusal retains the actual diagnosis in
the existing journal and copied receipt, and derives `partial` from actual
removals. Keep the existing summary suppression until its caller-facing effect
has been measured. A failed receipt save must remain an explicit refusal.

Rejected: creating a preflight journal only for this guard, forcing a partial
outcome with no removals, changing all `write_result` calls, or asserting that
every refusal emits a new summary. Those are broader behavioral changes.

### D-4: qualify with the existing native Linux fixture

Use the named source contract and fake-Docker tests below. They exercise the
remote Bash with local fixture files, not the live rollout. The fixture requires
native Linux (`RollingVolumeRecycleScriptTests.cs:787`), Bash, jq, flock and Node;
its fake Docker seam is at `:985`. Keep the process-spawn limiter and its current
120-second per-invocation deadline (`:1023`).

Rejected: a real fleet rollout as a regression test, WSL as proof of native
execution, an entire Unit suite, or silently increasing timeouts to hide red.
Use exact failures at the base SHA to establish inherited red if needed.

### D-5: resolve placement from current settings

At 2026-10-12 approximately 07:08 UTC, both required GETs succeeded using the
existing task token: `/api/runner-defaults` returned revision 2, one global
default and no per-kind overrides; `/api/session-runners` advertised an eligible
accepting native Linux runner and an eligible accepting Windows runner. The
other catalogued runner was unavailable. The token was present and never printed.

These observations do not pin a host. Re-read both routes at dispatch. Omit
`-Runner`; use `-Platform Linux` only for the native fixture execution. Plan and
TestDesign need no OS pin; omit `-Platform`, or use `-Platform Any` to clear an
inherited pin. Checkpoint group names below name the native Linux lane.

### D-6: keep this dispatch and future evidence bounded

Only this Markdown plan is assigned for editing now. Builds/tests remain NOT run.
Future qualification binds to the owning task when `ANTIPHON_TASK_TOKEN` is
present and reports its absence otherwise, without displaying credentials.
Post-land Mutation remains NOT run (paused); historical red/green claims are
not substitutes for an authorized future mutation pass.

Rejected: doing the card's historical red-first exercise in this design dispatch
or silently resuming Mutation. No session waiting for input is introduced or
changed, so the session-release checklist does not apply.

## Bounded slices and file ownership

| Slice | Owner and files | Work and completion condition | Tests / checkpoint relationship |
|---|---|---|---|
| S1: reconcile premise | Investigate; read `scripts/c590-remote.sh`, `RemoteScriptContractTests.cs`, `RollingVolumeRecycleScriptTests.cs`, the CARD-1105 plan and the two cited reports. Any report uses the task's evidence path. | Confirm the landed commit/test mapping; separate historical proof from current qualification. Measure what remains under the questions below. No production changes. | Inspect the five methods named in CP-1/CP-2. Source inspection alone is not a green execution result. |
| S2: design any remaining qualification | TestDesign, conditional on S1; owns this plan's later `## Verification design` and finalized checkpoint table. | Preserve completed work. Specify only missing observations, positive controls and cost; decide whether existing evidence suffices or a bounded Review run is needed. Confirmation-only execution belongs to Review. | Proposed CP-1/CP-2 below; no whole-class or whole-Unit selection. |
| S3: repair only a demonstrated residual gap | Separate commissioned Code/Docs task only after S1/S2 establish one. Potential source owner is `scripts/c590-remote.sh`; test owner is `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`; the shared fixture in `RollingVolumeRecycleScriptTests.cs` changes only if its existing injection seam cannot express the observation. | Amend this plan with the actual defect, specific edits and verification before implementation. There is no currently authorized second production patch. | New focused late-receipt coverage, if required, must be designed first and added to the manifest before running it. |

Preserve `scripts/deploy-server2.ps1`, `scripts/c590-real.ps1`, Compose files,
runtime settings and live rollout state. Do not edit generated `docs/cards/`.

## What Investigate must measure

1. Map every CARD-1116 acceptance sentence to the landed patch and retained
   evidence. Establish whether the original red-first mismatch exercise has a
   retained assertion failure, rather than inferring it from a commit message.
   If none is available, record that limit while Mutation is paused.
2. Distinguish the existing early mismatch proof from late mismatch observability.
   If runtime late-path qualification is necessary, have TestDesign specify an
   offline injection after a matching early comparison. Observe exit code,
   `DIAGNOSIS`, actual `up` calls, journal `.diagnosis`/`.outcome`, copied receipt,
   and the count/content of `C1008_RECYCLE` lines, on fresh apply and resume.
   Account for the default fixture's early build exit before choosing the seam;
   `C1105DeployTail` (`RemoteScriptContractTests.cs:2555`) is an existing example
   of exercising the deploy tail. Never override the guard/refusal being proved.
3. Report whether one-shot summary behavior is acceptable under the established
   wrapper contract or a separately scoped defect. Do not expand CARD-1116 into
   a generic journal/summary redesign based on static suspicion alone.

No live rollout or mutation control is required to settle the source-premise
mismatch. Any execution beyond the following proposal needs its own dispatch
scope and finalized verification design.

## Proposed qualification scope for TestDesign

This is a proposal, not the folded TestDesign deliverable. No `## Verification
design` section has been completed. Provisional coverage IDs are:

- V-1: existing early/late ordering and refusal-call contract.
- V-2: early mismatched materialized Compose refuses before destructive work or
  a new journal.
- R-1: matching target still accepts the previous generation and verifies the
  replacement generation.
- R-2: preview remains read-only and apply rechecks changed facts.
- R-3: temp retirement retains its independent absence/retirement gates.

Five existing non-parameterized methods execute across two rows. Method-name
wildcards are deliberately limited to those exact names; TestDesign must confirm
the discovered roster at its pinned SHA. The proposed ordinary floor is 14
minutes, including one isolated build per row, plus observed slot wait. No
current run duration or green result is claimed. The late runtime receipt
observation is not covered by these rows and must not be reported as covered.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1116-guard/` | compose-guard-native-linux | `/*/*/RemoteScriptContractTests/(C1105_Deploy_parent_orders_checkout_and_boot_files_before_recycle*)\|(C1105_Compose_cmp_refuses_before_removal*)` | V-1, V-2 | exact 2 methods, 0 failed/skipped, native Linux | 2 | 6 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1116-regression/` | compose-regression-native-linux | `/*/*/RemoteScriptContractTests/(C1105_Redeploy_accepts_previous_generation_and_requires_new_bind*)\|(C1105_Dry_run_previews_a_generation_change_without_moving_the_checkout*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)` | R-1, R-2, R-3 | exact 3 methods, 0 failed/skipped, native Linux | 3 | 8 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |

Once finalized and commissioned, run the rows through `Antiphon.Checkpoints`
with the full expected source SHA and this committed plan. Bootstrap any build
driver through `scripts/build-slot.ps1`; checkpoint row drivers acquire their
own slots. Keep foreground ownership and wait until completion (exit 75 means
continue waiting); slot timeout is not permission for an unleased run. Preserve
unaltered CHECKPOINT lines, counts and provenance in the report; keep generated
TRX/JSON/logs ignored and remove owned alternate outputs when done. These are
future execution requirements, not commands run by this Plan dispatch.
