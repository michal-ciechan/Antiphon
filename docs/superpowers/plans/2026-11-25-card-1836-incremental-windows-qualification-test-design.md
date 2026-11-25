# CARD-1836 incremental Windows qualification: test design

This is the TestDesign sibling of the [landed plan](2026-11-25-card-1836-incremental-windows-qualification-plan.md) (master `bdca854ff5ecb92400f45c453b751211f6e192c4`). It adds verification structure and leaves the fix design unchanged. Board card: CARD-1836. TestDesign task: fd6634bf. It defines the six exact-method fixtures, the five-run replay oracle and the custody copy that the oracle reads on server2.

**Full stays the only executing mode** until the replay verdict passes and the plan's two prospective Full shadows pass. No fixture, command or verdict on this page enables Incremental. A passing verdict (`exit 0`) is only an input to the later gate (G-20, G-24).

## Verification design

### Inspection

- Bodies read: the landed plan (all of it); `NightlyChunkRunnerTests` (world, native row, `Group4NativeTrxReader`); `RcProjectSelectionTests` (whole file, including `Repo` fixture); `RcForcedFullTests` (whole file); `RcDirtyOrderCSharpTests` (whole file) and `RcDirtyOrder.Launch` (outcome fold, carry refusal); `IncrementalSelectionSoundnessTests` (header, owner-glob method); `ReleaseCutWorld` (Fake/Real worlds, host seams, publisher seam); `HistoricalEvidence.Export/Verify` (freeze, inventory, read-only source); `NightlyCaseRows.ShouldAllPass(expected, required)`; `ReleaseSelectionAttempt.Save` (attempt layout `candidates/<cid>/attempts/<runId>/`); `NightlyRun.Core` (state root `last-run.json`, `last-complete-green.json`, log root); `NightlyPaths.TimingRoot` (`<logRoot>/timing-<runId>`); `tests/derived-impact-index.json` keys; both regression investigations ([owner map](../../investigations/2026-11-23-why-missed-rc-input-owners-map.md), [deleted drivers](../../investigations/2026-11-24-why-missed-live-lane-callers-deleted-drivers.md)); [group 136 native evidence](../../investigations/2026-11-24-group-136-native-evidence.md).
- Boundaries → IDs: row writer/admission → V-1, R-1, G-1..G-4, G-25, G-26; B/A diff, two index blobs and two project graphs → V-2, R-2, R-3, G-5..G-7; trust gate → V-3, G-8..G-11; launch queue → V-4, G-12, G-13; replay oracle → V-5, R-4, R-5, G-14..G-19; cut/run/tests hop and publisher → V-6, G-20..G-24.
- Method names rechecked at `bdca854ff`. None of the six names exists in `tests/`, and each class named by the plan exists at the path given. `scripts/lib/rc-trusted-projects.json` does not exist yet; S3 creates it empty.

### Delivery inventory

The change adds no async delivery path: no queue, no session input, no event or ack. Receipts are files on one host. Their only persistence boundary is the atomic create/rename of the sealed chunk inventory (D-2), which G-25 guards. The replay command writes only into its own new output directory. No producer-to-recipient queue test is needed and no substitute is declared.

### Fixture contract (all six)

Every fixture follows this contract:

- **Recipe:** a fresh `NightlyFixture` scratch root per invocation. Git fixtures use the existing `Repo`/`NightlyRcChain.Git` helpers (`core.autocrlf=false`, no hooks, no signing). Nothing touches the database, AppHost, network or containers.
- **Case:** one `[Test]` method per row. Each case is a `NightlyCaseRows.Check(ok, "<label>", detail)` row with the label shown below. The method ends with `rows.ShouldAllPass(<N>, <every label>)`, so a missing row is red, and so is a renamed one.
- **Oracle:** each row compares observable output against an expectation computed independently in the test: serialized row/selection/decision/verdict JSON, recorded fake-launcher launches or recorded publisher calls. Nothing is read back from the code under test to build an expectation.
- **Reachability:** each fixture enters through the production seam the slice changes, named per fixture below. Code may rename a seam only if the same production path stays in use. A compile error, unknown command, parser error or zero executed tests is never accepted as the red.
- **Red direction:** each fixture lists the naive implementation it must catch. The decisive rows are the ones where naive code **carries or skips a chunk that must run**. Rows only about over-selection are secondary.

Input receipt matrix. ✓ = the fixture builds this receipt, — = not used.

| Fixture | per-chunk `sha` + `contentDigest` | baseline (B receipt/attempt) | B..A diff | index edges old + new | trusted-project marker |
|---|---|---|---|---|---|
| CP-1 S1 | ✓ produced and admitted | ✓ sealed RC attempt, published release, later partial FAIL | — | — | — |
| CP-2 S2 | ✓ prior clean receipts at B for every chunk | ✓ B commit | ✓ real git | ✓ two blobs | — |
| CP-3 S3 | ✓ own-test rows at A | ✓ policy digest at B | ✓ as changed-path list | — | ✓ one entry, plus variants |
| CP-4 S4 | ✓ obligations with tiers and costs | ✓ attempt history | — (tiers given) | — | ✓ one gate and two held consumers |
| CP-5 S5 | ✓ in the five-run input root | ✓ predecessor P | ✓ real git P→R1..R5 | ✓ two blobs per pair | ✓ one gate in R5 |
| CP-6 S6 | ✓ sealed v2 inventory from a prior attempt | ✓ admitted RC attempt | ✓ real git | ✓ committed inputs | — (empty policy) |

### Fixtures

**CP-1 `NightlyChunkRunnerTests.Sealed_rows_distinguish_clean_execution_from_unproven_work`** (S1, V-1)
- Recipe: `NightlyChunkWorld` with its fake launcher. A second fake chunk writes a TRX that has per-case `UnitTestResult` rows. A sealed v2 prior attempt at commit B is plain JSON written by the fixture. Candidate A is a fixed 40-hex string, because the world has no git.
- Seam: `NightlyChunkRunner.Run` → row; S1's admission predicate (suggested name `RcChunkReceipt.Reusable`); `RcBaseline.Propose` for the D-1 cases.
- Rows (14): `s1-row-sha` (the row has `sha`=A and `contentDigest` equal to the content-digest v1 the test computes from the expanded roster with `RcBoundary.AddString` encoding; `summary.chunkReceiptVersion=2`) · `s1-green-sibling-of-failed` (in one run, Y passes and X fails: Y is reusable and X is owed) · `s1-skip-under-pass` (`result=pass`, exit 0, TRX `notExecuted=1` → not reusable, reason names the skip; the group 137 shape) · `s1-tolerated-exit` (pass label, exit 1 tolerated → owed) · `s1-missing-row` (a scheduled chunk without a row → disposition `missing`, `sha` null, owed) · `s1-renumber` (same members as group-4 and group-5 → equal `contentDigest`, different chunk id) · `s1-split` (one member moved → new digest, the B receipt does not match, owed) · `s1-carried-origin` (B receipt carried onto A: summary `sha`=A, row `sha`=B, `carried`, origin digest bound) · `s1-legacy-unproven` (a v1 row without the fields → strict admission `unproven`, and the v1 reader still parses it) · `s1-case-count` (a TRX that lacks one required case → unproven) · `s1-duplicate-or-torn` (two rows for one content, or a truncated inventory → no credit) · `s1-baseline-sealed-rc` (no published release → the last sealed RC attempt on the same line is chosen, and its kind, digest and path are recorded) · `s1-later-fail-stays-open` (an older release pass plus a later partial attempt with FAIL → the chunk is owed) · `s1-moved-ref-full` (moved ref or conflicting receipts → Full with a reason).
- Naive red: an admission rule of `result=="pass" && exitCode==0` carries `s1-skip-under-pass`, `s1-tolerated-exit` and `s1-missing-row`. Matching by chunk id carries `s1-split`. Taking the newest release as baseline closes `s1-later-fail-stays-open`.
- Classification: Integration, portable, `NotInParallel("QualificationTimingTests")` (class level). Add method-level `[Timeout(60_000)]`.

**CP-2 `RcProjectSelectionTests.Removed_harness_edges_select_both_graphs_and_keep_the_unrelated_chunk`** (S2, V-2, R-2, R-3)
- Recipe: the existing `Repo` (six projects, catalog, three batches). It adds `tests/derived-impact-index.json` at B and at A, a chunk roster (`ServerHarnessTests`, `RunnerLinkedTests`, `OtherTests`, `NewEdgeTests`, two `Ambiguous` classes in two projects) and clean B receipts for every chunk. At B, `tests/Runner/Runner.csproj` links `../Server/Harness.cs`, and the B index has `tests/Server/Harness.cs → [ServerHarnessTests]`. Commit A deletes `Harness.cs` and the `<Compile Include>` together, and drops the edge from the A index.
- Seam: new `RcChunkSelection` over `RcProjectSelection.Select(root, B, A)`, with index blobs read by `git rev-parse <sha>:tests/derived-impact-index.json`.
- Rows (10): `s2-old-only-edge` (the B-only edge selects the `ServerHarnessTests` chunk) · `s2-deleted-linked-reader` (the B graph's linked Compile selects `RunnerLinkedTests`; the A graph has no edge) · `s2-unrelated-carry` (`OtherTests` is carried with its B origin; the carry set is nonempty) · `s2-new-only-hit` (an A-only edge selects `NewEdgeTests`) · `s2-ambiguous-name` (both `Ambiguous` chunks are selected) · `s2-cap` (a path in `cappedPaths` on either side widens to its project closure, or forces Full with `index-capped:<path>:<side>` when the closure is unknown) · `s2-index-unbound` (no index blob at B → Full `index-unbound:B`) · `s2-index-binding` (the selection records both blob ids and SHA-256 values, equal to git) · `s2-runtime-reader` (R-2: deleting every file under a directory that a reader-census entry names selects the reader chunk) · `s2-assembled-name-reader` (R-3: deleting `scripts/test-x-startup.ps1`, with no index edge on either side, selects the census reader when its glob covers the path, and forces Full `unknown-path` when no census entry covers it).
- Naive red: an A-only index or graph carries `ServerHarnessTests` and `RunnerLinkedTests`. "No edge ⇒ independent" carries the R-3 reader.
- Classification: Integration, portable, class `ParallelLimiter<ProcessSpawnLimit>` (git). Add method `[Timeout(60_000)]`.

**CP-3 `RcForcedFullTests.Trust_requires_executed_own_tests_and_equal_contracts`** (S3, V-3)
- Recipe: in-memory JSON only, no git. Contract hashes enter as receipts (`{project, side, extractorVersion, contractHash}`). The extractor gets its own seam test in S3 Code; it is outside this row. The trust policy is a fixture document with one entry for `src/Messaging/Messaging.csproj`, with Windows configurations, owned tests `MessagingTests` (complete case roster) and allowed downstream edges `Server` and `Runner`. The policy digest is given for B and for A.
- Seam: new `RcTrustedProjectGate.Decide(policyB, policyA, changedPaths, ownResults, contracts)`, plus a source read of the committed `scripts/lib/rc-trusted-projects.json`.
- Rows (14): `s3-hold-only-dependents` (a private change in Messaging → own tests `execute`, Server/Runner consumers `held`, Other `carry`) · `s3-release` (own tests executed clean, zero skip, plus equal B/A hashes → holds become carries with `trust-gate-passed:<entry>`) · `s3-own-red`, `s3-own-skip`, `s3-own-timeout`, `s3-own-missing` (each expands held consumers and their downstream closure into the attempt) · `s3-hash-drift`, `s3-hash-absent` (either side missing) → expand · `s3-policy-edit` (policy digest differs B/A → trust ignored, `trust-policy-changed`) · `s3-self-authorize` (an entry present only at A → untrusted) · `s3-package-sdk` (package or SDK change → closure or Full regardless of trust) · `s3-policy-refusals` (wildcard, missing tests, package or SDK entry → refused) · `s3-empty-shipped-policy` (the committed file has zero entries) · `s3-direct-hit-retained` (a consumer with its own direct hit executes even when the gate passes).
- Naive red: treating "not run" or a skip as gate-pass, or comparing hashes with null equal to null, carries Server and Runner. Reading policy at A lets A authorize itself.
- Classification: Unit, portable, class `[Timeout(60_000)]` already present.

**CP-4 `RcDirtyOrderCSharpTests.Dispatch_orders_ready_chunks_by_impact_then_cost`** (S4, V-4)
- Recipe: `RcPortCorpus.WithScratchRepo` for containment checks. Obligations: F1 and F2 are unresolved failures (tier 0, prior cost 90 s and 30 s); P is a tier-1 direct product hit; G is the trusted gate's own tests (tier 2, 10 s); D1 and D2 are tier 2 with tied cost 40 s, so the stable key decides; T is tier 3 (50 s); H1 and H2 are held tier-3 consumers of G (20 s and 60 s); M is a missing row (tier 4, 20 s); U is tier 4 with unknown cost; C is a carry. The fake dispatcher has one slot and completes jobs in order, so re-evaluation after G is deterministic. Prior-failure history uses the CARD-1823 shape.
- Seam: `RcDirtyOrder.Launch` extended by S4, or its new successor called by `NightlyDispatch`. The assertion is on the launch sequence, not on internal bands.
- Rows (7): `s4-exact-sequence` (gate green: the literal order `F2,F1,P,G,D1,D2,T,M,U`; ties break on cost then key, and unknown cost goes last) · `s4-expansion-launches-held` (gate red: H1 and H2 join the same attempt and the queue is re-evaluated; the literal order is `F2,F1,P,G,D1,D2,H1,T,H2,M,U`, each once) · `s4-gate-pass-carries-held` (gate green with equal contracts → H1 and H2 omitted as carries with origin) · `s4-no-duplicate-no-missing` (multiset of launches ∪ carries = obligations, exactly) · `s4-open-failure-not-carried` (a Carry disposition over F1 → refusal `carry-with-open-failure`) · `s4-same-tip-pass-no-discharge` (an unrelated pass at the same tip leaves F1 open) · `s4-cost-source-recorded` (each launch records rank and cost source: `prior-wall`, `member-sum` or `unknown`).
- Naive red: an order computed before expansion never launches H1 and H2. Dropping the carry refusal carries F1.
- Classification: Integration, portable, class `ParallelLimiter<ProcessSpawnLimit>`. Add method `[Timeout(60_000)]`.

**CP-5 `IncrementalSelectionSoundnessTests.Five_run_replay_exposes_a_missed_failure_before_activation`** (S5, V-5, R-4, R-5)
- Recipe: a scratch git repo with commits P, R1..R5, each carrying both index blobs. An **input root laid out exactly like the server2 custody copy** below (`MANIFEST.sha256`, `custody.json`, `runs/<runId>/{logs,timing,attempt,candidate}`, `state/`), written by the fixture with synthesized per-case TRX. Chunk ordinals shift in R4. The cohort:
  - R1: clean Full.
  - R2: the diff hits chunk A, and A fails. Predicted run; the failure is caught.
  - R3: no input change for chunk K, but K FAILS (the group 136 intermittent shape). This is the deliberate miss.
  - R4: an old-only edge selects E, and E fails; ordinals are renumbered.
  - R5: gate G with an own-test failure expands its holds, and one predicted-carry chunk has no TRX (missing oracle).
  - A sixth cohort of five all-Full fallbacks (zero carries).
- Seam: `RcSelectionReplay.Run` behind `scripts/antiphon replay-rc-selection --input-root <dir> --output <new-dir> --as-of <utc>`. The test invokes the command entry in process, not a subprocess.
- Rows (11): `s5-missed-failure-listed` (exit 1, `activation=blocked`, `missedFailures` is exactly R3/K with suite, contentDigest, case, sha, baseline, reason and TRX path) · `s5-unknown-is-unsafe` (the R5 missing oracle is in `unsafeCarries` as unknown) · `s5-caught-failures` (R2/A and R4/E are counted as caught, and recall is reported) · `s5-content-not-ordinal` (R4's failure is attributed by content digest after renumbering) · `s5-blind-perturbation` (rerun with flipped or removed outcomes of every unseen chunk: the bytes of `predictions/*.json` are identical) · `s5-chain-hides-skipped-results` (chained mode: K's origin at R4 is its R2 receipt, never R3) · `s5-teacher-forced-separate` (the teacher-forced diagnosis is written to its own file and never changes the verdict) · `s5-zero-carry-insufficient` (the all-Full cohort → exit 3 `insufficient:no-carry-opportunity`) · `s5-clean-cohort-admits` (the same cohort with K passing and the TRX present → exit 0 `criterion-met`, `enables=false`, and nothing written outside `--output`) · `s5-input-read-only` (input tree hash is equal before and after, and an `--output` inside `--input-root` is refused with exit 2) · `s5-reconciles-universe` (for each run, predicted run + carry + held = the discovery universe count, with no overlap).
- Naive red: an oracle that scores only executed rows reports no miss. One that treats a missing TRX as pass passes R5. One that reads current outcomes before freezing makes R3/K "run". All three fail at the named rows.
- Classification: Unit, portable, class `ParallelLimiter<ProcessSpawnLimit>` (git). Add method `[Timeout(60_000)]`.

**CP-6 `ReleaseCutTests.An_incremental_cut_reconciles_the_roster_and_withholds_publication`** (S6, V-6)
- Recipe: `ReleaseCutWorld.Real` (git checkout, bare origin). Its `Run` drives `NightlyRun.Core` → `NightlyTests` with an **independent fake chunk launcher** that records each launch and writes TRX, and the recording publisher (`PublisherGit`, `GitHub`). A prior attempt's sealed v2 inventory and an approval document (replay verdict digest, selector hash, trust-policy hash, two shadow run ids) are fixture files under the release root.
- Seam: `ReleaseCut.Run(Options(o => o.SelectionMode = "Incremental"), Host())`.
- Rows (10): `s6-gate-closed-full` (no approval → effective Full, every roster chunk launched, `incremental-not-admitted`) · `s6-gate-admitted-owed-only` (approval bound → launches equal the owed set exactly, every carry resolves to an origin receipt, and the ledger reconciles to discovery) · `s6-no-publication` (a green selective cut → zero publisher git or GitHub calls, and `coverageScope=Incremental`) · `s6-explicit-full-publishes` (explicit Full keeps today's single publisher call) · `s6-default-full` (options without the flag and each entry script default → Full) · `s6-stale-plan-digest` (plan digest changed between hops → refusal or Full) · `s6-hop-mismatch` (mode differs cut→run→tests → refusal) · `s6-age-boundary` (anchor at exactly seven days → Full) · `s6-crash-resume` (interrupt after the sealed partition → resume launches only unlaunched owed chunks, none twice) · `s6-rollback` (withdrawn approval → next attempt Full on the same SHA).
- Naive red: honouring the requested mode without admission carries chunks while the gate is closed. A publisher gate on `exitCode==0` publishes a composite ledger.
- Classification: Integration, portable, method `ParallelLimiter<ProcessSpawnLimit>` with `[Timeout(60_000)]`.

### Five-run replay oracle

**Command.** `scripts/antiphon replay-rc-selection --input-root <dir> --output <new-dir> --as-of <yyyy-MM-ddTHH:mm:ssZ> [--repository-root <git>]`. It uses the same selector, cost model and gate transitions as dispatch (S5 links them, not copies).

**Inputs.** Everything is read from `--input-root`, and nothing outside it except Git objects in `--repository-root`. The input root is refused (exit 2) when:
- `MANIFEST.sha256` is absent, or the tree has an extra or missing file, a link or a digest mismatch;
- `--output` exists or lies inside the input root or the checkout;
- a candidate SHA is missing from Git and `git/<runId>.bundle` cannot supply it.

The oracle then freezes the root with `HistoricalEvidence.Export` into `<output>/frozen/` and binds the inventory digest.

**Phases.** Each phase is timed in `<output>/phases.json`: `freeze`, `git` (B..A NUL diff, both index blobs, both graphs), `predict`, `seal-predictions`, `outcomes`, `score`, `verdict`.

**Blinding.**
- `predict` gets a pre-run view built only from that run's prelaunch files (`dispatch-schedule.json`, `groups-planned*.json`, `*.discovery.json`, `attempt/`) and from the simulated chain ledger.
- The outcome reader for run k needs the sealed `predictions/<k>.json` digest to open run k's TRX and `summary.json`.
- Predictions for all five runs, and both branches of each trust gate, are sealed before any of their outcomes are opened. Only own-gate rows are revealed to resolve the gates, and the resolved partition is sealed again.

**Two columns.** `strict` uses only v2 receipts the runtime would admit; for legacy input it is often zero. `counterfactual` reconstructs content from the pinned source and prelaunch roster, and every reconstruction is labelled.

**Scoring.**
- `missedFailures` = predicted carry ∩ actual FAIL.
- `unsafeCarries` = predicted carry ∩ (FAIL ∪ other nonclean ∪ unknown).
- Each run reports a run/carry × clean/FAIL/other/unknown matrix. Timeouts, skips and incomplete evidence get their own columns.
- Totals are reconciled with discovery.

**Outputs.**
- `verdict.json` (`antiphon-rc-replay-verdict-v1`): selector hash, algorithm revision, trust-policy hash, inventory digest, prediction digests, as-of time, per-run matrices, the missed table, cost (carried seconds, unpriced count, projected wall time, time to first failure) and `enables:false`.
- Per-run `matrix.json`, `trace.jsonl` and `teacher-forced.json`.

**Exit codes.**
- **1, hard red:** any missed failure or unsafe carry. This blocks activation, and any intermittent miss counts.
- **3, insufficient:** fewer than five reconciled comparisons, zero exercised carries, or an unscorable predicted skip.
- **2:** input refusal.
- **0:** the switch criterion of the plan's step 6 is met. Activation still needs the two prospective shadows.

**Calibration.** Record phases for each of the five real inputs. Plan targets are ≤5 min per run, ≤25 min total and ≤256 MiB. They stay provisional until those five samples exist; then set timeouts at about 2× the nearest-rank p95.

### Custody copy for server2 (orchestrator action)

The desktop's `C:\Antiphon\releases` is not mounted on server2. The evidence custodian copies these files once. The copy is a read and a file copy, not a build or test, so it does not touch the live run b8cbfb24's build slot. Everything below is relative to `C:\Antiphon\releases`.

1. **Fix `asOf` (UTC) before reading any `summary.json`.** List `candidates\*\logs\<yyyy-MM-dd-HHmm>-<run8>\` directories. Order them by the directory stamp, with the ordinal run id as tie-break, and take the last five whose stamp is before `asOf`. Exclude a run still in flight at `asOf`, such as b8cbfb24 if it has not finished; list it as `in-flight-at-asOf`. Two are already known: `rc-2026-12-04-6ed4cb58cd75\logs\2026-11-24-0922-acca11ba` and `rc-2026-12-03-ecc9bfaa5bfa\logs\2026-11-22-2052-557cdb63`. Also take the **predecessor** of the oldest (the sixth newest), which is the comparison input for the oldest. Also take the newest run before the oldest that has a `last-complete-green`/Full anchor, if that is a different run. List any missing run explicitly in `custody.json`.
2. **For each chosen run** (`<cid>` candidate, `<run8>` id, `<runId>` full id from the attempt directory), copy these to `runs\<run8>\`:
   - `logs\` ← `candidates\<cid>\logs\<stamp>-<run8>\`. Depth-1 files only: `*.json`, `*.jsonl`, `*.trx`, `*.txt`. That covers `summary.json`, `dispatch-schedule.json`, `groups-planned*.json`, `*.discovery.json`, `*.timing.json`, `expected-platform-skips.json`, client and scripts JSON, and failure markers. **Do not copy** `*-tests.log`, `*-execution\` or `*.diag`.
   - `timing\` ← `candidates\<cid>\logs\timing-<run8>\`. Depth-1 `*.json` only, except `owner-*.json`, so `setup-phases.json` is included. Do not copy the `chunk-*\` subdirectories.
   - `attempt\` ← `candidates\<cid>\attempts\<runId>\`. All files (`selection-attempt.json`, `selection.json`, revisions).
   - `candidate\` ← `candidates\<cid>\`. Depth-1 `*.json` (`candidate.json`, `release-manifest.json`, `publish.json` when present).
   - Record `git -C checkout-<run8> rev-parse HEAD` in `custody.json`. If that SHA is not on origin, add `git\<run8>.bundle` from `git -C checkout-<run8> bundle create ... <sha>`.
3. **Once:** `state\<id>\last-run.json` and `last-complete-green.json` from every `state-<id>\` directory, as they are at `asOf`.
4. **Integrity.** Hash every source file with `Get-FileHash -Algorithm SHA256` before and after the copy, and abort if any hash changes. Write `MANIFEST.sha256` (lines of `<sha256>  <relative/forward/slash/path>`, sorted ordinally) and `custody.json`:
   - `asOf` and the run list with stamp, cid, runId and sha;
   - the predecessor and anchor;
   - missing runs;
   - file count and bytes;
   - source and after hashes.

   Stage this outside `C:\Antiphon\releases`, for example `C:\Antiphon\replay-stage\<asOf>\`, and `tar -cf` it.
5. **Destination on server2:** the runner-visible, read-only directory `/work/evidence/card-1836-replay-input/<asOf yyyyMMddTHHmmssZ>/`. Route: `scp` the tar to `mc@server2:/var/tmp/card-1836-replay/`. Then place it on the work volume by the orchestrator's usual volume route. From a server2 delegate, extract it, run `sha256sum -c MANIFEST.sha256` (it must pass on every line), then `chmod -R a-w`. The oracle runs with `--input-root` set to that directory and `--output /work/evidence/card-1836-replay-output/<asOf>-<selectorHash12>/`.

The digests of this payload are **unbound until the copy exists**. The first replay run records the inventory digest in its verdict. Binding is required after Code and before scoring.

### Proves it works now

- V-1: sealed rows carry execution `sha` and `contentDigest` and separate clean from unproven work | S1 receipt/admission | CP-1 | 14 rows pass
- V-2: both graphs and both index blobs select, and the unrelated chunk carries | S2 selection | CP-2 | 10 rows pass
- V-3: trust holds only dependents and releases only on executed clean own tests plus equal contracts | S3 gate | CP-3 | 14 rows pass
- V-4: an exact deterministic impact/cost order with same-attempt expansion | S4 launch | CP-4 | 7 rows pass
- V-5: the replay exposes the hidden miss and blocks activation; a clean cohort admits without enabling | S5 oracle | CP-5 | 11 rows pass
- V-6: the public cut runs Full when the gate is closed, the owed roster only when admitted, and never publishes a selective cut | S6 hop | CP-6 | 10 rows pass

### Guards the regression

- R-1: a skip hidden under a pass label (group 137 observed skips) is carried | CP-1 `s1-skip-under-pass`
- R-2: a runtime reader of a deleted directory is missed (owner-map incident `9cabf2f08`) | CP-2 `s2-runtime-reader`
- R-3: an assembled-filename reader of deleted drivers is missed (`f47da556e`, group 112) | CP-2 `s2-assembled-name-reader`
- R-4: an intermittent failure behind a predicted carry (group 136) goes unreported | CP-5 `s5-missed-failure-listed`
- R-5: an ordinal renumber attributes a failure to the wrong chunk | CP-5 `s5-content-not-ordinal`

### Guard inventory

- G-1: D-3.1 pass predicate, zero skipped/NotExecuted | PC-1
- G-2: D-2 a missing or never-launched row has null `sha` and is never carried | PC-2
- G-3: D-2 content digest is over the expanded roster, not the label or ordinal | PC-3
- G-4: D-2 a carried row keeps its origin execution `sha` | PC-4
- G-5: D-3.2/3 old-index edges are unioned with new | PC-5
- G-6: D-3.4 B-graph reachability for a deleted harness or linked reader | PC-6
- G-7: D-3.5 capped, unbound or unknown edges widen or force Full | PC-7
- G-8: D-4 release requires executed, clean, complete own tests | PC-8
- G-9: D-4 B/A contract hashes are both present and equal | PC-9
- G-10: D-4 trust policy cannot self-authorize, and an edit invalidates | PC-10
- G-11: D-4 the shipped allowlist is empty | PC-11
- G-12: D-5/CARD-1823 an open failure is never carried or discharged by an unrelated pass | PC-12
- G-13: D-4/D-5 held consumers are expanded into the same attempt, exactly once | PC-13
- G-14: replay step 4, carry ∩ FAIL is listed and is a hard red | PC-14
- G-15: replay step 4, unknown actual on a carry is unsafe | PC-15
- G-16: replay step 3, predictions are frozen before outcomes (blinding) | PC-16
- G-17: replay step 6, zero exercised carries cannot pass | PC-17
- G-18: replay inputs are read-only and output stays outside them | PC-18
- G-19: replay step 3, the chained ledger is never seeded with hidden results | PC-19
- G-20: D-6 gate closed ⇒ Full executes (Full stays active) | PC-20
- G-21: D-6 a selective cut never reaches the publisher | PC-21
- G-22: D-6 the seven-day age is enforced at admission | PC-22
- G-23: D-3/rollout 1, a plan digest mismatch between hops forces Full or refuses | PC-23
- G-24: D-6 Full is the default at every public entry | PC-24
- G-25: D-2 duplicate rows or a torn inventory never grant credit | PC-25
- G-26: D-1 a later FAIL or partial attempt is not erased by an older pass | PC-26

### Positive controls

Each PC applies a compiling defect to the slice's production code, then runs the exact method red, restores, and runs green. "Red at" names the `FAIL <label>` line in the `ShouldAllPass` message.

- PC-1: break G-1 by ignoring `notExecuted`/`skipped` in the reuse predicate; expect `NightlyChunkRunnerTests.Sealed_rows_distinguish_clean_execution_from_unproven_work` red at `s1-skip-under-pass`.
- PC-2: break G-2 by treating a missing row as its last prior pass; same method red at `s1-missing-row`.
- PC-3: break G-3 by hashing the chunk id instead of the roster; red at `s1-split`.
- PC-4: break G-4 by writing the candidate SHA into a carried row; red at `s1-carried-origin`.
- PC-25: break G-25 by keeping the first of duplicate rows instead of refusing; red at `s1-duplicate-or-torn`.
- PC-26: break G-26 by preferring the newest published release over later attempts; red at `s1-later-fail-stays-open`.
- PC-5: break G-5 by skipping the B index read; `RcProjectSelectionTests.Removed_harness_edges_select_both_graphs_and_keep_the_unrelated_chunk` red at `s2-old-only-edge`.
- PC-6: break G-6 by computing reachability at A only; same method red at `s2-deleted-linked-reader`.
- PC-7: break G-7 by treating `cappedPaths` as no hit; red at `s2-cap`.
- PC-8: break G-8 by accepting `not-run`/`skipped` own results as passed; `RcForcedFullTests.Trust_requires_executed_own_tests_and_equal_contracts` red at `s3-own-missing`.
- PC-9: break G-9 by comparing contract hashes with null equal to null; red at `s3-hash-absent`.
- PC-10: break G-10 by reading the trust policy at A; red at `s3-self-authorize`.
- PC-11: break G-11 by adding one entry to `scripts/lib/rc-trusted-projects.json`; red at `s3-empty-shipped-policy`.
- PC-12: break G-12 by removing the `carry-with-open-failure` refusal; `RcDirtyOrderCSharpTests.Dispatch_orders_ready_chunks_by_impact_then_cost` red at `s4-open-failure-not-carried`.
- PC-13: break G-13 by skipping held-consumer expansion on a red gate; red at `s4-expansion-launches-held`.
- PC-14: break G-14 by scoring only executed rows; `IncrementalSelectionSoundnessTests.Five_run_replay_exposes_a_missed_failure_before_activation` red at `s5-missed-failure-listed`.
- PC-15: break G-15 by mapping an absent outcome to clean; red at `s5-unknown-is-unsafe`.
- PC-16: break G-16 by giving the predictor the current run's `summary.json`; red at `s5-blind-perturbation`.
- PC-17: break G-17 by treating zero carries as vacuously safe (exit 0); red at `s5-zero-carry-insufficient`.
- PC-18: break G-18 by dropping the output-inside-input refusal; red at `s5-input-read-only`.
- PC-19: break G-19 by advancing the chain ledger with all observed outcomes; red at `s5-chain-hides-skipped-results`.
- PC-20: break G-20 by honouring the requested Incremental without approval; `ReleaseCutTests.An_incremental_cut_reconciles_the_roster_and_withholds_publication` red at `s6-gate-closed-full`.
- PC-21: break G-21 by gating the publisher on exit code only; red at `s6-no-publication`.
- PC-22: break G-22 by changing the age comparison from `>=` to `>`; red at `s6-age-boundary`.
- PC-23: break G-23 by skipping the plan-digest comparison at the nightly-tests hop; red at `s6-stale-plan-digest`.
- PC-24: break G-24 by defaulting `NightlyRunOptions` selection mode to Incremental; red at `s6-default-full`.

Mutation runs break, red, restore and green after land. Code runs V/R. Review judges these PCs before land.

### Out of scope

- The contract extractor's own fidelity (reference metadata → logical entries). It needs built assemblies, so S3 Code gives it its own seam test outside this table. CP-3 consumes hash receipts.
- Native Windows execution, the real five-run scoring, the two prospective shadows and any pilot. These are measurements after implementation, under separate commissions.
- Re-running the existing affected classes as checkpoint rows. Operator standing rule 2026-11-25: short tests only. Review chooses re-runs and rc qualification owns them.
- `RcShadowSample` audit-sample statistics (unchanged; covered by the existing `RcIncrementalShadowTests`).

### Existing tests affected

No migration, public HTTP DTO/route, operation label, remote script function or statement budget. Compatibility surfaces are on-disk receipts and CLI modes:
- receipt and legacy readers: `NightlySummaryWitnessTests`, `RcRunEvidenceTests`, `RcIncrementalLedgerTests`, `RcHistoricalAdmissionTests`, `RcReleaseBaselineTests`, `RcBoundaryPackTests`, `RcChunkInputDigestTests`;
- graph and policy: `DerivedImpactIndexTests`, `RcInputManifestTests`, `RcInputOwnersMapTests`, `RcProjectSelectionTests`, `RcForcedFullTests`;
- dispatch and CLI: `RcDirtyLaunchOrderTests`, `RcShadowDirtyDifferentialTests`, `RcFailedFirstExecutionTests`, `ReleaseSelectionAttemptTests`, `ReleaseSelectionHopSeedTests`, `ReleaseCutTests`, `RcTagBackingTests`;
- CARD-1782: `LiveLaneRunnerTests` and `NightlyCoreGoldenTests` for `nightly-live-lane`.

S5 adds a CLI command, so it affects `antiphon-scripts` command registration and parity rosters. S3 adds `scripts/lib/rc-trusted-projects.json`, so it affects `RcInputOwnersMapTests` owner coverage of `scripts/lib/**`. Re-evaluate both against the actual diff at Review.

### Checkpoints

Unchanged from the landed plan except for the row counts. Each slice is its own committed revision and its own single-row run: one red, then the same row green. Per run, `total = max(30, 2*12+10) = 34` min, with no override. Union = the six new methods, which is the whole ordinary scope.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1836-receipt/` | portable-receipt | `/*/*/NightlyChunkRunnerTests/Sealed_rows_distinguish_clean_execution_from_unproven_work` | V-1, R-1, G-1..G-4, G-25, G-26 | 1 executed, 0 failed, 0 skipped; 14 rows | 1 | 12 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1836-graph/` | portable-graph | `/*/*/RcProjectSelectionTests/Removed_harness_edges_select_both_graphs_and_keep_the_unrelated_chunk` | V-2, R-2, R-3, G-5..G-7 | 1 executed, 0 failed, 0 skipped; 10 rows | 1 | 12 |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1836-trust/` | portable-trust | `/*/*/RcForcedFullTests/Trust_requires_executed_own_tests_and_equal_contracts` | V-3, G-8..G-11 | 1 executed, 0 failed, 0 skipped; 14 rows | 1 | 12 |
| CP-4 | S4 | `tests/Antiphon.Tests -> bin-c1836-order/` | portable-order | `/*/*/RcDirtyOrderCSharpTests/Dispatch_orders_ready_chunks_by_impact_then_cost` | V-4, G-12, G-13 | 1 executed, 0 failed, 0 skipped; 7 rows | 1 | 12 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1836-replay/` | portable-replay | `/*/*/IncrementalSelectionSoundnessTests/Five_run_replay_exposes_a_missed_failure_before_activation` | V-5, R-4, R-5, G-14..G-19 | 1 executed, 0 failed, 0 skipped; 11 rows | 1 | 12 |
| CP-6 | S6 | `tests/Antiphon.Tests -> bin-c1836-cut/` | portable-cut | `/*/*/ReleaseCutTests/An_incremental_cut_reconciles_the_roster_and_withholds_publication` | V-6, G-20..G-24 | 1 executed, 0 failed, 0 skipped; 10 rows | 1 | 12 |

**Runtime budget** ([performance standards](../../test-performance-standards.md)):
- Comparable measured bodies, from retained server2 TRX on 2026-11-25: `C1545_Closure` 0.17–0.35 s (n=7); `C1545_ConsumerSafety` max 1.70 s (n=7, the nearest-rank p95); `C1545_HistoricalEdges` max 1.28 s; `Project_closure_misses_match_the_recorded_known_unsound_set` max 0.87 s (n=9); `The_rc_run_gets_the_candidate_its_private_roots_and_the_sealed_selection` max 1.20 s (n=7); `Only_a_green_run_reaches_the_publisher_and_it_is_asked_once_for_the_candidate` max 1.55 s (n=8).
- Targets: setup ≤2 s, body ≤5 s for CP-1..CP-4 and ≤15 s for CP-5/CP-6, teardown ≤2 s.
- Method timeout is a provisional 60 s for all six (a ≤30 s ceiling, doubled). After five samples per method, re-set it to about 2× the measured p95, under CARD-1836.
- A same-day reused-build row (task 6ea7f02f CP-27) measured 255 s slot wait, 32 s startup and 5.7 s teardown. Build time stays at the plan's 12-minute estimate; report it separately.

### Cost

- **Ordinary V/R floor (Code):** 6 rows × 12 min = **72 min** for greens. Including each slice's red run on the same row: **144 min** (estimated). Slot wait is extra; 255 s was measured once today.
- **PC floor (Mutation):** 26 PCs. Estimate per PC: an incremental rebuild of about 4 min plus a method run of about 1 min.
  - Serial: 26 × 5 + 6 restore greens × 5 = **160 min** (estimated).
  - Batched under git-common (independent mutations in different files and methods, at most one per CP method per batch; S1 has six PCs, so six batches): 6 batch builds × 4 + 26 method-scoped red runs × 1 + 1 restore build × 4 + 6 greens × 1 ≈ **60 min** (estimated).
  - Saving: about **100 min**.
- **Replay measurement (later commission):** ≤25 min provisional for five runs, plus the custody copy, which is unmeasured.
- **Total for this card's test work:** 144 + 60 = **204 min**, estimated, excluding slot wait.

Before handoff: guards=26, mapped=26, missing=0, duplicate PC maps=0. All PCs run in Mutation after land. Cost is numeric above.
