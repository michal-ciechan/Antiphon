# CARD-1823: cumulative failure ledger for rc seeds

Date: 2026-11-09. Base: `263b985a8d708169c3312db6d4e207c28be7e1a8`. Branch: `feat/card-task-f56af59b`.
Card: CARD-1823, platform Any. Lane for every checkpoint: Integration, from the `[Category("Integration")]` already on `RcStoppedRunSeedTests`. Checkpoints do not pin a runner and do not pass `-Platform`.
`GET /api/runner-defaults` and `GET /api/session-runners` both returned 200. No host, runner id, or reason is copied into this plan.
This plan adds no session that waits for input, so the CARD-1083 parking checklist does not apply.
The brief path is this file (no `-plan` suffix).

Operator 2026-11-09: add something that picks up previous failures. The stopped-run exporter records only the run it was pointed at. A method that failed in an earlier run and was never reached by the stopped run is absent from that seed, so the next launch does not lead with it.

## Intended behaviour

### Before

`scripts/export-nightly-stopped-run-seed.ps1` calls `Export-NightlyStoppedRunSeed` (`scripts/lib/nightly-stopped-run-seed.ps1:163`). The envelope is `format=antiphon-nightly-stopped-seed-v1`, `seedKind=operator-seed-from-stopped-run`. The payload (`:430`) is `sourceObservedAtUtc`, `chunks`, `failedTests`, `evidence`. A failed row (`:374`) is `suite`, `chunk`, bare `className`, TRX `method` (parameter suffix kept), `outcome`. `createdAtUtc` is `UtcNow` (`:431`). An existing output path refuses.

`Resolve-NightlyStoppedSeedOrder` (`:451-597`) places chunks in five bands and returns `entries`, `reason` (prefix `operator-seed-order:`), and `lanes` (`:595`). Band 0 is a chunk that contains a failed method, or a chunk with no node list whose class failed (`:553-558`). `Get-NightlySeedMethodName` (`:442-449`) strips a `(` suffix, so one unproven arm places the whole method. Chunk ids are not binding.

`scripts/lib/nightly-dispatch.ps1:152` writes `dispatch-schedule.json` entries with exactly `suite`, `chunk`, `canonicalOrdinal`, `dispatchOrdinal`, `priorFailure`. `priorFailure` is the band-0 mark (`:69-110`). `scripts/test-nightly-failed-first.ps1:847-860` (`Test-FFSchemaKeys`) refuses any other key set.

`Initialize-NightlyDispatchHistory` (`scripts/lib/nightly-failure-history.ps1:727-777`) accepts a seed only when `-AcceptCrossCandidateSeed` is set, `SeedSourceCandidateId` equals `sourceCandidateId`, the file digest matches, and both `createdAtUtc` and `sourceObservedAtUtc` sit in the 14-day window (`:741-744`). `scripts/nightly-run.ps1:104-107` forwards `PriorFailuresPath`, `PriorFailuresDigest`, `AcceptCrossCandidateSeed`, and `SeedSourceCandidateId`. It does not merge seeds.

Watch scripts do not print band names. `scripts/orchestration/watch-qual.ps1:11-19` has no schedule parameter. `scripts/qualification/observe.ps1` reports supervision, not chunk order.

The 2026-11-09 restarts combined seeds by hand. There is no cumulative memory.

### After slice 1 (usable at the next restart)

A new script, `scripts/merge-nightly-seeds.ps1`, reads N seed files plus an optional decision list and writes one seed plus a deterministic report. The merged `failedTests` array is every identity that is not yet proven. The existing orderer then places those chunks first. Launch defaults stay byte-identical: the operator passes the merged file to the four switches that already exist, with the merged file's digest and the newest seed's `sourceCandidateId`.

```
pwsh -NoProfile -File scripts/merge-nightly-seeds.ps1 -Seed <older> -Seed <newer> -Decision decisions.txt -Repo <rc checkout> -OutputPath <merged.json> -ReportPath <report.json>
```

Then the existing launch flags: `-PriorFailuresPath <merged.json> -PriorFailuresDigest <Get-NightlyHistoryDigest of that file> -AcceptCrossCandidateSeed -SeedSourceCandidateId <newest sourceCandidateId>`.

### After the later slices

Slice 2 adds an operator-supplied ledger file. Slice 3 lets the exporter append into that ledger when asked. Slice 4 adds band labels beside the sealed schedule. Slice 5 witnesses that history init accepts the merged file and refuses a bad digest. Slice 6 witnesses the shared identity with CARD-1820. None of those slices change the default launch path.

### Unchanged by slice 1

- `scripts/nightly-run.ps1:82-160`, including the four parameters at `:104-107` and the splat at `:155-158`.
- `scripts/lib/nightly-stopped-run-seed.ps1:12-61` (hash, schema, read), `:163-440` (export), `:442-597` (method name and orderer).
- `scripts/lib/nightly-failure-history.ps1:293-296` (file digest), `:315-318` (class pattern), `:727-777` (history init).
- `scripts/lib/nightly-dispatch.ps1` prior-failure partition and the five-key projection at `:152`.
- `scripts/lib/nightly-coverage.ps1:730-742` (`Get-NightlyBareClassIdentity`).
- `scripts/export-nightly-stopped-run-seed.ps1` (whole file, 19 lines).
- `scripts/orchestration/watch-qual.ps1`, `scripts/qualification/observe.ps1`, `scripts/rc-qualification-status.ps1`.
- `scripts/test-nightly-failed-first.ps1:847-860`.
- `tests/impacted-classes.json`.

Slice 1 dot-sources the hash and the reader. It does not reimplement them.

## Ground truth

| The card assumes | What the code does |
|---|---|
| The seed remembers every earlier failure | `failedTests` is only the run passed to `Export-NightlyStoppedRunSeed` (`nightly-stopped-run-seed.ps1:370-390`) |
| A later green on a candidate that contains the fix clears the identity | No merge exists. A green is "class listed, every such chunk completed, method absent from `failedTests`". Passed methods are not stored |
| The fix commit is an ancestor of the rc tip | rc cuts are cherry-picks. `git merge-base --is-ancestor` of each why-missed fix SHA below is false for `5fef2c23b89414b8818aaf40db9ad738bce9524c`, `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`, and `7bee43614495293afbae1b0f4add019d11dacb9c`. The rc commit body contains `(cherry picked from commit <40hex>)` |
| `653f1145` descends from `5fef2c23` | `653f1145de5b72c2617a018cd606e0e8ba3cfe6e` is not a descendant of `5fef2c23b89414b8818aaf40db9ad738bce9524c`. It is an ancestor of `7bee43614495293afbae1b0f4add019d11dacb9c` |
| Band names are already printed | The orderer returns priorities inside `reason` text. The schedule flag is boolean `priorFailure` only |
| CARD-1820 is the rc-failure ledger | CARD-1820 is Selector v2, InProgress. Its item (5) asks for a ledger and a machine-readable why-missed block. That block is not in the tree. Why-missed notes stay prose |

## Decisions

D-1. Slice 1 is a new script `scripts/merge-nightly-seeds.ps1`. Band 0 is produced by writing unproven identities into `failedTests` of a seed the existing orderer already places first (`priorFailure=true`). Launch defaults stay as they are.
Rejected: an exporter `-MergeWith` switch, and teaching `Resolve-NightlyStoppedSeedOrder` to read N seeds. Either one changes the path the next restart already uses.

D-2. The output remains a valid `operator-seed-from-stopped-run` envelope. Chunk rows, including `completed`, `started`, and `unfinishedReason`, are copied in order from the newest seed. Only `failedTests` is replaced. Evidence is copied from the newest seed only. The payload keeps the four fields the exporter writes. The reader ignores unknown fields; slice 1 does not rely on that.
Rejected: a new seed kind. History init and the orderer accept this kind only.

D-3. An identity is proven only when a strictly later seed shows it green and that seed's `sourceSha` contains a decision-list fix, or `--Proven` names that later run. Contains means `git merge-base --is-ancestor <fix> <sourceSha>` is true, or a commit reachable from `sourceSha` has a body line that is exactly `(cherry picked from commit <fix>)`. A prose mention of the SHA does not count. The report records both the decision-list SHA and the containing commit (the fix SHA itself when it is an ancestor, otherwise the cherry-pick commit). `--Repo` is the checkout used for the check. Tests use a tiny temp repo. If `--Repo` is omitted, git proof is unavailable and only `--Proven` can clear an identity. If the decision file is omitted, nothing is proven except via `--Proven`.
Rejected: ancestor-only (it never matches these rc cherry-picks). Rejected: any later green. Rejected: `git log --grep` of the bare SHA (docs commits mention SHAs). Rejected: stripping the parameter suffix in the ledger. The orderer still strips it, so one unproven arm leads the chunk; the report stays arm-specific.

D-4. Green, the strongest signal the schema allows: in a strictly later seed, at least one chunk lists the class, every chunk that lists the class is `completed=true`, and no `failedTests` row matches the identity. A class that is absent does not prove. An incomplete sibling chunk of the same class blocks green. An earlier green never clears a later failure. Identity is the ordinal string `suite|className|method` with the parameter suffix kept, so `Method(canceled)` is a different row from `Method(other)`.
Rejected: inventing passed-method rows the schema does not have.
Limit, stated here so a proof is not over-read: a method that moved onto a chunk the later seed does not list can look green while the old chunk still lists the class and is completed.

D-5. Status is `open`, `fixed-pending-proof`, or `proven`. `placed` is a boolean, not a status. `open`: failed, and no strictly later seed both contains a decision fix and avoids showing this identity failed again. `fixed-pending-proof`: a strictly later seed's `sourceSha` contains a decision fix, that seed does not show the identity failed, and that seed does not show green. `proven`: strictly later green plus containment or `--Proven`, with the proving run id and sha. A later failure moves `lastFailedRun` forward, so a fix that was present and still failed stays `open`. A green whose `sourceSha` does not contain the named fix stays `open`.
A class on no chunk of the newest seed is omitted from `failedTests`, reported with `placed=false`, and the default exit is 0. `-Strict` exits 3 when `unplacedCount>0`, or when an identity has no decision row and was not proven. Schema-invalid input exits 2 and writes nothing.
Rejected: failing the merge by default when one class was dropped from the newest plan.

D-6. Digest stability. The script never calls `UtcNow`. `createdAtUtc` is the newest seed's stamp, passed through `ConvertTo-NightlySeedUtc` and `ToString('o')`, which is what `Get-NightlyStoppedSeedHash` (`nightly-stopped-run-seed.ps1:12-21`) hashes. `failedTests` are sorted by `suite`, `className`, `method` (ordinal). Chunk order is the newest seed's order. The payload string is `ConvertTo-Json -Depth 30 -Compress`. The outer file is `ConvertTo-Json -Depth 35` with UTF-8 and no BOM, matching the exporter (`:431-435`). The hash is the dot-sourced function. The report format is `antiphon-cumulative-failure-report-v1` and has no wall-clock field. Two runs of the same inputs on the same PowerShell engine produce identical file bytes. Stability across Windows PowerShell 5.1 and pwsh 7 is not claimed. The desktop launch uses pwsh, and the Linux checkpoints use pwsh.
Rejected: committing a timestamp of the merge.

D-7. The ledger home is slice 2, an operator path passed as `-Ledger`, outside git. Slice 1 writes the one-shot report and does not write a ledger. Slice 3's exporter `-Ledger` defaults off. Slice 5 does not change `nightly-run.ps1`.
Rejected: committing the live ledger. Rejected: a release-gates paragraph. `docs/release-gates.md` does not own seed launch; the hop witness is the pin.

D-8. Shared identity with CARD-1820, slice 6 only. Ledger key is `suite|Namespace.Class|Method(args)`. A selector row looks like `C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget`. Join on the class's final segment plus the method. If two ledger rows match, the witness reports both and does not pick one. Suite is what disambiguates. Slice 6 is a parser witness. It does not implement selector v2 and it does not edit `tests/impacted-classes.json`. Why-missed notes stay prose until CARD-1820 adds its JSON block.
Rejected: using the short class name alone as the ledger key. The same short name exists in more than one suite (`ProcessSpawnLimitTests` is `Antiphon.Tests`, `Antiphon.SessionRunner.Tests`, and `Antiphon.Agents.Pty.Tests`).

D-9. Slice 4 band labels are a parallel `bands` array on the orderer result (`P0-failed`, `P1-interrupted`, `P1-untouched`, `P2-sibling`, `P3-passed`, same order as `entries`) and an optional `-SchedulePath` on `watch-qual.ps1`. Omitting `-SchedulePath` leaves today's stdout unchanged. The sealed five-key schedule entry is not given a new field. `observe.ps1` is not used for this. Slice 1 does not touch these files.
Rejected: adding `band` to `dispatch-schedule.json` entries. `Test-FFSchemaKeys` would fail.

## Seed schema and merge algorithm

### Inputs

- `-Seed` is repeatable. N is at least 1. Each path is a stopped-run envelope.
- `-Decision` is optional. UTF-8, ASCII. `#` comments and blank lines are ignored. A row is `suite|Full.Class.Name|Method(optional-args)<TAB>fixSha[,moreSha]`. Each SHA matches `^[a-f0-9]{40}$`.
- `-Proven` is repeatable, `suite|class|method:runId`. It names a seed in this invocation. The named run must be strictly later than `lastFailedRun` and must show that identity green. An unknown run, a run that is not later, or a run that is not green refuses with exit 2 and writes nothing (`proven-run-unknown` or `proven-not-green`).
- `-Repo` is optional. When set, it must be a git work tree that can resolve every decision SHA and every seed `sourceSha`. When omitted, git proof is off.
- `-OutputPath` and `-ReportPath` are required and must not exist (`seed-output-exists`).
- `-Strict` is the exit-3 switch from D-5. Slice 2 adds `-Ledger`.

The script dot-sources `scripts/lib/nightly-failure-history.ps1` and `scripts/lib/nightly-stopped-run-seed.ps1`. `$PSScriptRoot` is resolved in the body, not in a `param()` default, so Windows PowerShell 5.1 `-File` can load it. The file is ASCII (no byte above 127). The synopsis contains this sentence, which V-7 searches for:

`A later green proves an identity only when that seed's source SHA contains the fix commit, or --Proven names that run.`

### Read

Read each file with `Read-NightlyStoppedRunSeed`. That already checks the envelope, the content hash, the schema (`Assert-NightlyStoppedSeedContent`, `:23-39`), and the class pattern (`Assert-NightlyHistoryClass`, `nightly-failure-history.ps1:315-318`). Refuse a duplicate `sourceRunId` (exit 2, write nothing). Refuse a `sourceSha` that is not `^[a-f0-9]{40}$` (the reader already does).

Newest seed: maximum `sourceObservedAtUtc`. A tie goes to the seed listed later on the command line. The merged envelope copies `sourceRunId`, `sourceCandidateId`, `sourceSha`, `createdAtUtc`, and `sourceObservedAtUtc` from that newest seed, so `-SeedSourceCandidateId` and the 14-day gate keep working. The merger itself does not apply the 14-day gate. An identity from an older seed still enters the union. If the newest seed is stale, the existing launch gate still refuses it.

### Union and status

Collect every `failedTests` row. The key is `suite|className|method`, ordinal, suffix kept. `firstFailedRun` is the oldest seed that contains the key (observed time, then list order). `lastFailedRun` is the newest seed that contains it. `outcome` is the outcome on that last-failed row.

A seed is strictly later than `lastFailedRun` when its `sourceObservedAtUtc` is greater, or the times are equal and it was listed later.

For each identity, look at seeds strictly later than `lastFailedRun`:

1. `proven` when one of those seeds shows green and either its `sourceSha` contains any decision SHA for this identity, or `--Proven` names this identity and that seed's `sourceRunId`.
2. `fixed-pending-proof` when one of those seeds contains a decision SHA and does not show green (and does not show the identity failed; a failure would have moved `lastFailedRun`).
3. `open` otherwise.

Git containment, cached per pair `(sourceSha, fixSha)`: ancestor check first; otherwise `git log` with an extended regular expression that matches only a line which is exactly `(cherry picked from commit <fix>)`. Do not search for the bare SHA. The failure set is tens of identities, not thousands.

### Placement

For `open` and `fixed-pending-proof`:

- If the newest seed's `failedTests` already lists this method, keep that chunk.
- Otherwise use the first chunk, in the newest seed's array order, whose `classes` contain the class.
- If the class is on no chunk, omit the identity from `failedTests` and set `placed=false`.

`proven` rows are omitted from `failedTests` and reported with `placed=false`. Rows copied into `failedTests` have `placed=true`. Sort `failedTests` by `suite`, `className`, `method`. Copy chunk rows unchanged from the newest seed.

### Report

One JSON object, no wall-clock field. `format` is `antiphon-cumulative-failure-report-v1`. `newestRun` is the newest `sourceRunId`. `unplacedCount` is the number of rows with `placed=false` and a status other than `proven`. `identities` is sorted by `suite`, `className`, `method`. Each identity has `suite`, `className`, `method`, `firstFailedRun`, `lastFailedRun`, `status`, `placed`, `outcome`, `provingRun`, `provingSha`, `decisionShas`, `containingCommit`. The last four are null or empty when they do not apply.

### Exit

- 0: both files written, schema-valid seed.
- 2: bad input, duplicate run, existing output, bad `--Proven`, or a set `-Repo` that cannot resolve a SHA. Write nothing.
- 3: `-Strict`, and either `unplacedCount>0` or some identity has an empty `decisionShas` and a status other than `proven`. The files are still written.

### Synthetic walk

Seed A, older: chunk `antiphon/g1` lists `Fixture.Alpha`, completed. Failed method `M1`. Seed B, newer: the same chunk completed, `M1` absent, `M2(canceled)` failed. No decision file. The merged `failedTests` contains `M1` and `M2(canceled)`. Chunk rows are B's rows. `Resolve-NightlyStoppedSeedOrder` places `g1` first because both arms are unproven; the report keeps the arms apart.
Add a decision row for `M1` whose fix is an ancestor of B's `sourceSha`, and B shows `M1` green: `M1` is `proven` and leaves `failedTests`; `M2(canceled)` stays. The same green with a `sourceSha` that does not contain the fix leaves `M1` in band 0. A third identity whose class is absent from B is reported `placed=false` and is not in `failedTests`.

### This session's runs

The desktop seed files (`C:\Users\lndco\antiphon-seeds`) are not on this runner. The table below is read from the run-start notes and the why-missed notes, not from seed JSON. Where a note says a P0 chunk ran and the stopped run continued, and the later seed's failure list does not include that method, this plan treats that as a green inference. The seed file is the authority when the operator runs the script. If that inference is wrong, rows 1 and 2 stay in band 0 too. `dce78ee0` may have failed more chunks than these notes record; its export, once the run is stopped, is the authority for identities after the retry-2 note. The example assumes the decision list contains the fix SHAs named in the why-missed notes and nothing else. Introducing commits are not fixes.

| Run | Full id | Candidate | sourceSha | What the notes record |
|---|---|---|---|---|
| a6410818 | `a6410818876948f7ac9d7a46bc592900` | `rc-2026-11-12-5fef2c23b894` | `5fef2c23b89414b8818aaf40db9ad738bce9524c` | Stopped 2026-11-09T04:34:00Z. Seed sha256 `119f4ad38601f2645dea3201c3f31781c83305fdf3ebd2b95306a9e5e421d6a3`. 10 failed tests in 7 chunks. The 8 classes seeded from `4e0d1159` re-ran in P0 and passed; they are not band 0 |
| 231f573c | `231f573c834141c2acab4fe0f0ef8847` | `rc-2026-11-13-653f1145de5b` | `653f1145de5b72c2617a018cd606e0e8ba3cfe6e` | Launched from the a6410818 seed only. Stopped 2026-11-09T12:48:13Z on `e2e/all-split-6`. Seed sha256 `c937fc1ed65770753a094132f99799df9c6171eb63afb99de08b720c009a188b`. Exactly 5 failed tests. The a6410818 seven chunks were P0 ordinals 0-6 and the run later reached generation 785, so this plan infers those methods are absent from this seed's `failedTests` |
| dce78ee0 | `dce78ee0a35a4b08be30d8e0e8ef0295` | `rc-2026-11-14-7bee43614495` | `7bee43614495293afbae1b0f4add019d11dacb9c` | Same 231f573c seed, same acknowledgement, `SeedSourceCandidateId=rc-2026-11-13-653f1145de5b`. Started 13:06:17Z. P0 is five antiphon chunks (group-2, -4, -71, -96, -117). The run later reached group-130. No export of this run is in the notes |

Not in the ledger: attempt `f8c8904b` (CS2012 `LandGraphAudit` double compile, no test chunk, no seed; why-missed `docs/investigations/2026-11-09-why-missed-landgraphaudit-double-build.md`; introducing `19e6c73957bc01e36f2b0b85229e6f0c0ed5e76c` is not a test-identity fix). Profile chunks whose TRX outcome is Failed and whose only result is NotExecuted (exit 8) are completed skips, not identities. Linux-only notes that are not failures of these three runs are out of the example.

Containment checked on this worktree: `653f1145de5b72c2617a018cd606e0e8ba3cfe6e` is an ancestor of `7bee43614495293afbae1b0f4add019d11dacb9c`. `5fef2c23b89414b8818aaf40db9ad738bce9524c` is not an ancestor of `653f`. Cherry-picks, each verified with `git log -1` and `merge-base --is-ancestor`:

| Rc commit | Trailer source (the decision-list SHA) | Ancestor of 653f | Ancestor of 7bee |
|---|---|---|---|
| `2a8e5bc51adf89bfa3137888e8cc2adc29298738` | `a350f1c0385ac8d13ab3ff8b805f0f62989672ca` | yes | yes |
| `77c05347de5cbdab5ef39ad143af5f70abb824b7` | `680fecc47464aacbf307de71b8f212ebc99e27f9` | yes | yes |
| `16c3b4e0a5d44c9ea110374330cce285417e83dd` | `58e11030b989620c2a30177c5eac5f8c158462e9` | no | yes |
| `4cbef3867537687115bc5d154b1df7d955b39454` | `4b9ca585529a4da727beb202c9ef4ecb2800c61b` | no | yes |
| `68df3753c00f9f2003d28a586ec167ae3b60f926` | `eba2444ac3f4fa5ab47d134b9535e8673507096f` | no | yes |
| `c7053d36b76c10db5b5ca0e177c54a587c2ef389` | `efe450127d57be1c9701f387688ad297e30b0b9e` | no | yes |
| `0ae681065ccc0bfb182bf93ac707234d3227ca34` | `8ec342fd891a264e451383fd3d8c9f4d05c3d8d1` | no | yes |
| `1fa8f562223ffa429f6611de330284db69424ec0` | `dd711ab7ea26846cbd84646d212c0dc68d9a0486` | no | yes |

`1fa8f56222` is a later wait fix for the same `C467_V32(busy)` arm. It does not reopen a proven identity unless a later seed shows that method failed. The notes do not say it failed again. The Windows E2E was not re-run after the Linux probe fix; the proof for row 2 is the 231f573c green inference plus the `77c05347` trailer.

### Band 0 at the next restart

If the 231f573c green inference holds and `dce78ee0` is exported without those two methods failing again, two identities drop out and 14 stay in band 0. Exact method strings are in the notes for rows 1-8 and 16. Rows 9-15 are partial: the seed file is the authority, and this plan does not invent the missing method names.

Drop out (proven), contingent on the green inference:

1. `session-runner|Antiphon.SessionRunner.Tests.C1677TempFamilyRegistryTests|Pty_test_root_alternation_equals_known_prefixes`. First failed a6410818. Fix `a350f1c0`, contained in 653f via `2a8e5bc5`.
2. `e2e|Antiphon.E2E.AgentTaskLandDeliveryE2ETests|C467_V32_StatusPollingCannotDischargeUnreceivedOutcome(busy)`. Fix `680fecc4`, contained in 653f via `77c05347`.

Band 0, `fixed-pending-proof`, once a seed whose `sourceSha` is `7bee43614495` is in the merge. The fixes are on 7bee only, not on 653f. These were dce78ee0 P0 ordinals 0-4. Reaching group-130 is not a green seed. Until that export exists, a merge of only a6410818 and 231f573c leaves these `open`.

3. `antiphon|Antiphon.Tests.ProcessSpawnLimitTests|Checkpoint_tool_classes_carry_their_own_limiter`. Fix `58e11030` via `16c3b4e0`. Failed on 231f573c group-2. This is the `Antiphon.Tests` class, not the SessionRunner or Pty copies.
4. `antiphon|Antiphon.Tests.Agents.Card1277PtyHostFixtureTests|Claude_two_write_submit_is_a_normalizer_turn`. Passed on a6410818 (0.454s), failed on 231f573c group-4. The earlier green must not prove the later failure. Fix `4b9ca585` via `4cbef386`.
5. `antiphon|Antiphon.Tests.Application.PreDispatchFailureDeliveryTests|Pre_dispatch_recovery_retains_authoritative_row(canceled)`. Keep the `(canceled)` suffix. Other arms passed. Fix `eba2444a` via `68df3753`. Introducing `ae5b545d9ac9091f42847734b88bf8e202cf2814` is not the fix.
6. `antiphon|Antiphon.Tests.Application.Card1773TranscriptPromptStampMigrationTests|Up_renames_an_invalid_index_drops_it_and_creates_if_missing`. Windows-only newline. Fix `efe45012` via `c7053d36`. group-96.
7. `antiphon|Antiphon.Tests.Infrastructure.RepositoryMutationLeaseTests|C448_C24_KilledWorkerLeavesLiveGitChildFenced`. a6410818 wrote no group-117 TRX, so this identity was not reached there. Fix `8ec342fd` via `0ae68106`. Introducing `dff78584d636fa49eca37652f2c3f14a48002a92` was already on rc/2026-11-12 and is not the fix.

Band 0, `open` (no fix SHA in the notes, so a 231f573c green does not prove):

8. `e2e|Antiphon.E2E.AgentTaskLandDeliveryE2ETests|C467_V24_BlockedWriterThenReleaseDeliversBothNotes`. Class taken from the note's failing class. No why-missed note on this branch.
9. `antiphon|Antiphon.Tests.Checkpoints.OutputCleanupTests|windows_set_clock_and_foreign_dacl_are_not_adopted`. The note says `OutputCleanupTests windows_set_clock`. That fragment matches one method in `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs`. The seed's method string is still the authority; this full name is a tree match, not a seed reading. a6410818 profile-1543, moved to profile-1563 on the next cut.
10. `antiphon|Antiphon.Tests.Infrastructure.ServerImagePushCredentialTests|<method>`. Two methods. Names are only in `a6410818.json`. Namespace confirmed. group-118.
11. The second `ServerImagePushCredentialTests` method. Same limit.
12. `antiphon|Antiphon.Tests.Infrastructure.ServerGitProcessTests|<one C1432_ method>`. The note does not say which. Many methods start with `C1432_` in that file and in `LandingGitProfileTests.cs`. Do not guess. group-118 on a6410818, group-119 on the next cut.
13. `antiphon|Antiphon.Tests.Scripts.ServerRepositoryMirrorScriptTests|<method>`. Three methods, names only in the seed. group-131 on a6410818, group-132 on the next cut.
14. The second mirror-script method.
15. The third mirror-script method.

Band 0, `open`, from dce78ee0:

16. `antiphon|Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests|C1584_E2EExpandedBudget`. Chunk `antiphon-all-group-130`, Windows 3.09s. Note `docs/investigations/2026-11-09-why-missed-landing-split-budget.md`. The roster was already stale on 653f; 231f573c did not dispatch group-130. Introducing `9f009f299fb524ab241d4be8d82b66ab1b39e505` (CARD-1795), `5aee4e8e813b21415601c49c4560cf016b5e383c` (CARD-1797), and `906533773a18b5b06d21fa43a3127dcb9839fdf7` (probe) are not fixes. The proposed impacted-classes id `e2e-source-roster` was not applied. Do not edit the table.

Count if the green inference holds and the dce78ee0 export adds row 16 without clearing rows 3-7: 14 identities in band 0 (8 of the original 10, plus the 5 from 231f573c, plus `C1584`). `-Strict` on that real merge exits 3 because rows 8-16 have no decision SHA. The next restart uses the default exit 0.

## Tests

All new methods go on `tests/Antiphon.Tests/Scripts/RcStoppedRunSeedTests.cs`. The cheap ones use the existing `Run` helper (`:11-14`): harness `scripts/test-nightly-stopped-seed.ps1`, token `SS <Case>.contract` via `Check` (`:26`), inner budget 90s, `[Timeout(120_000)]`. Add a `switch ($Case)` arm (`:180`) per method. Build tiny seeds in memory. Do not extend the 3300-file scale fixture. Do not add these methods to `RcFailedFirstPlannerTests`.

Suffix check against the class as it stands: `BuilderReadOnly`, `FailedFirstMapping`, `AcknowledgementRequired`, `HashMustVerify`, `TimeoutIsFailure`, `ScheduleConservesAllChunks`, `PublicHopsForwardAcknowledgement`, `InvalidSeedFallsBack`, `SiblingTimingFromSummary`, `StoppedTimingRootAndRefusals`, `ChunkUnfinishedTolerance`, `LinkCacheRefusesReplacement`, `DigestGuardLeavesSeedOrder`, `RealPackerRegroupKeepsFailedFirst`, `FailedMethodBands`, `RealPackerFailedMethodFollows`, `RealShapeFailedMethodBand`, `SummaryPrefixTamper`, `UnfinishedRunLaneResume`, `SkipRowsCountAsCompleted`, `WindowsAncestorSwapRefused`, `ScaleExportBound`. None of the new names is a prefix of these or of each other. A rename that creates a prefix collision has to split the checkpoint row.

Red-first for slice 1: commit the failing harness arms and the C# methods before `scripts/merge-nightly-seeds.ps1` exists, show CP-1 red, then add the script. One mutant per witness. Restoring the mutant is Code's job; this plan does not run the tests.

| Witness | Asserts | Mutant that must fail it |
|---|---|---|
| `CumulativeMergeUnion` | Older identity absent from the newer failed list stays in the merged `failedTests`. Newest chunk rows are unchanged, not a union of chunk rows. `(canceled)` stays its own report row and its chunk still leads. A failed row whose class is not on the chunk exits 2 and writes nothing. A class missing from the newest seed is `placed=false`, omitted from `failedTests`, exit 0; the same inputs with `-Strict` exit 3 | Dropping older failures, or replacing newest chunk rows with a union of chunk rows |
| `CumulativeMergeProvenDrop` | Later green, and a temp repo where the fix commit is an ancestor of that `sourceSha`, removes the identity and marks `proven` with that run and sha | Removing the ancestor check, so a green alone is not enough for this fixture and the row stays failed |
| `CumulativeMergeUnfixedGreen` | Later green whose `sourceSha` does not contain the named fix stays in `failedTests` with status `open` | Treating any later green as proof |
| `CumulativeMergeCherryPick` | A reachable commit whose body has the exact trailer line, plus a later green, marks `proven` and records that cherry-pick SHA as `containingCommit`. A reachable commit that only mentions the SHA in prose does not prove | Dropping the trailer check, or treating any mention as containment |
| `CumulativeMergeExplicitProven` | `--Proven suite\|class\|method:runId` on a later green seed proves with no `-Repo` and no decision file | Ignoring the switch leaves the identity failed |
| `CumulativeMergeDigestStable` | Two runs of the same inputs produce identical file bytes and the same `contentHash`. `createdAtUtc` equals the newest seed's normalized stamp. `Read-NightlyStoppedRunSeed` accepts the file | Stamping `UtcNow` changes the second digest |
| `CumulativeMergeLaunchDefaults` | The merge synopsis contains the D-3 sentence. The script is ASCII. `scripts/nightly-run.ps1` still declares the four parameters and its text does not call `merge-nightly-seeds.ps1` | Deleting the sentence |
| `CumulativeLedgerRoundTrip` | `-Ledger` creates `antiphon-cumulative-failure-ledger-v1`. A second run with the same inputs writes identical ledger bytes and the same seed digest. No wall-clock field | A timestamp in the ledger changes the second write |
| `CumulativeLedgerRejectsRewrite` | An extra `open` identity already in the ledger, absent from this invocation's seeds, is still present after the write. A ledger whose `format` is wrong exits 2 and leaves the file bytes unchanged | Replacing the ledger with only this run's identities |
| `ExporterAppendOffByDefault` | `export-nightly-stopped-run-seed.ps1` and `Export-NightlyStoppedRunSeed` do not create or touch a ledger when `-Ledger` is omitted | An unconditional append |
| `ExporterAppendWritesLedger` | Passing `-Ledger` after a successful export upserts that seed's failed identities as `open` | The switch is parsed and then ignored |
| `ScheduleBandLabels` | The orderer result includes `bands` in entry order with the five labels. A schedule written through `nightly-dispatch.ps1:152` still has exactly the five keys | Adding a field on the schedule entry, or dropping `bands` |
| `WatchQualPrintsBand` | `watch-qual.ps1 -Once` without `-SchedulePath` does not print `P0-failed`. With `-SchedulePath` the same fixture prints the band prefix for that chunk | The new switch changes the omitted-switch stdout |
| `MergedSeedHopAccepts` | `Initialize-NightlyDispatchHistory` on the merged file, with the digest and the newest `sourceCandidateId`, returns an empty `FallbackReason` and the union failure is in `failedTests` | Rejecting a cross-candidate seed even when the three switches match |
| `MergedSeedHopRefusesDigest` | The same call with a wrong digest returns a non-empty `FallbackReason` and a null history | Accepting a mismatched digest |
| `FailureIdentityJoinsSelectorRow` | A fixture token `C1514LandingSplitBudgetTests/C1584_E2EExpandedBudget` joins `antiphon\|Antiphon.Tests.Scripts.C1514LandingSplitBudgetTests\|C1584_E2EExpandedBudget` and does not join a second suite's row that shares the short class name and a different method. Two matches are reported as ambiguous | Joining on the short class name alone |

`RcQualificationWatcherScriptTests.Offline_watchers_and_trx_extraction` runs 39 `Smoke` cases. That method is not in any checkpoint.

## Migrations

None. Scripts and tests only. No database, no public DTO, no route, no operation label, no remote statement-budget script. The merge script is new and local.

## Slices

Code of the first slice runs CP-1 and CP-2 only. A later code dispatch runs the rows whose `After` is that slice. The checkpoints table is the closed list for the whole card. Each slice is one commit series of about 60-120 minutes.

S1, about 90 minutes. Add `scripts/merge-nightly-seeds.ps1` and the seven `CumulativeMerge*` arms in `scripts/test-nightly-stopped-seed.ps1` plus the seven methods on `RcStoppedRunSeedTests`. Dot-source the existing hash and reader. No edit to `nightly-run.ps1`, the exporter's default path, the orderer, or the watch scripts. Red harness commit first.

S2, about 75 minutes. `-Ledger` on the merger. Format `antiphon-cumulative-failure-ledger-v1`, same identity fields as the report, upsert only, no deletion of identities this invocation did not prove. Operator path, not a git commit. Witnesses `CumulativeLedgerRoundTrip`, `CumulativeLedgerRejectsRewrite`.

S3, about 75 minutes. Optional `-Ledger` on `scripts/export-nightly-stopped-run-seed.ps1` and `Export-NightlyStoppedRunSeed` (`nightly-stopped-run-seed.ps1:163`). Default off. Witnesses `ExporterAppendOffByDefault`, `ExporterAppendWritesLedger`.

S4, about 90 minutes. Additive `bands` on the orderer return (`nightly-stopped-run-seed.ps1:595`). Optional `-SchedulePath` on `scripts/orchestration/watch-qual.ps1` (parameters today are `:11-19`). Do not edit `nightly-dispatch.ps1:152` or `observe.ps1`. Witnesses `ScheduleBandLabels`, `WatchQualPrintsBand`.

S5, about 75 minutes. Witness `Initialize-NightlyDispatchHistory` (`nightly-failure-history.ps1:727`) against a merged file. No default change. Witnesses `MergedSeedHopAccepts`, `MergedSeedHopRefusesDigest`.

S6, about 60 minutes. One parser witness, `FailureIdentityJoinsSelectorRow`. No product selector and no `tests/impacted-classes.json` edit.

## Existing tests affected

CARD-1782 product-class selection: none. Do not edit `tests/impacted-classes.json`.
Neighbour tests that must stay green: every existing `RcStoppedRunSeedTests` method, because slice 1 does not change the orderer or the launch defaults. CP-2 samples `FailedMethodBands` and `AcknowledgementRequired` for that. `Test-FFSchemaKeys` (`scripts/test-nightly-failed-first.ps1:847-860`) must stay green; slice 4 is forbidden to add a schedule-entry field. Remote script statement budgets: none.

## Performance budget

Owner: `docs/testing-and-build.md` (plan-format performance budget) and `docs/test-performance-standards.md`.

The selection is the methods in the checkpoints table, category Integration, on `tests/Antiphon.Tests`. No database, no container, no AppHost, no shared Postgres. Setup is temp files plus, where the witness says so, one tiny git repo per case. Phases are process start, fixture write, script body, teardown. The harness already writes `performance.json` with wall seconds (`scripts/test-nightly-stopped-seed.ps1` near the end of the file); new arms inherit that. Record the wall seconds from the harness PASS lines. Build time and slot wait are reported by the checkpoint tool and sit outside the row.

No measured p95 exists yet. The body is expected to be a few seconds. The `[Timeout(120_000)]` and the 90s `ScriptHarness` inner budget are the sibling SS budget (`RcStoppedRunSeedTests.cs:12-14`) and are about 2x that expectation, provisional until Code measures a case. Do not widen either number to hide a red. No selection in this plan is over 30 minutes. `EstimatedMinutes` is at most 8 so `3 * estimate` stays under the 45-minute importer advisory. Derived row timeout is `max(15, 3 * EstimatedMinutes)`: CP-1's estimate of 8 yields a 24-minute row timeout.

Direct builds use the worktree's native default output, one at a time, with no `-o`, `--output`, or `OutputPath`. The checkpoint rows use the isolated `bin-c1823-*` outputs in the table. On Linux the checkpoint tool builds with `UseAppHost=false`. Do not wrap `scripts/run-checkpoint.ps1` in `scripts/build-slot.ps1`; the script takes its own slot. Do not set `totalTimeoutMinutes` on this table.

## Verification design

V-1 `CumulativeMergeUnion`. V-2 `CumulativeMergeProvenDrop`. V-3 `CumulativeMergeUnfixedGreen`. V-4 `CumulativeMergeCherryPick`. V-5 `CumulativeMergeExplicitProven`. V-6 `CumulativeMergeDigestStable`. V-7 `CumulativeMergeLaunchDefaults` (doc-contract pin plus the launch-default source pin). V-8 `CumulativeLedgerRoundTrip`. V-9 `CumulativeLedgerRejectsRewrite`. V-10 `ExporterAppendOffByDefault`. V-11 `ExporterAppendWritesLedger`. V-12 `ScheduleBandLabels` and `WatchQualPrintsBand`. V-13 `MergedSeedHopAccepts`. V-14 `MergedSeedHopRefusesDigest`. V-15 `FailureIdentityJoinsSelectorRow` (exact method).

R-1. `FailedMethodBands` and `AcknowledgementRequired` on the same class, same isolated build as CP-1. This is the regression sample. It is not a rerun of `GlobalReplayPriority`, `FullBytesAndCoverageConserved`, `ScaleExportBound`, or `RealShapeFailedMethodBand`. The 39-case watcher `Smoke` is not in the checkpoint.

Code of S1 runs CP-1 and CP-2 and stops. Later slices run their own `After` rows. Ordinary verification runs the V/R rows. Mutation owns any later positive-control cycle; the mutant column above is the contract for those cycles, and this plan does not schedule them.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1823-s1/` | merge | `/*/*/RcStoppedRunSeedTests/(CumulativeMergeUnion*)\|(CumulativeMergeProvenDrop*)\|(CumulativeMergeUnfixedGreen*)\|(CumulativeMergeCherryPick*)\|(CumulativeMergeExplicitProven*)\|(CumulativeMergeDigestStable*)\|(CumulativeMergeLaunchDefaults*)` | V-1 V-2 V-3 V-4 V-5 V-6 V-7 | all listed, 0 failed | 7 | 8 |
| CP-2 | S1 | `CP-1` | regression | `/*/*/RcStoppedRunSeedTests/(FailedMethodBands*)\|(AcknowledgementRequired*)` | R-1 | all listed, 0 failed | 2 | 4 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1823-s2/` | ledger | `/*/*/RcStoppedRunSeedTests/(CumulativeLedgerRoundTrip*)\|(CumulativeLedgerRejectsRewrite*)` | V-8 V-9 | all listed, 0 failed | 2 | 6 |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1823-s3/` | exporter | `/*/*/RcStoppedRunSeedTests/(ExporterAppendOffByDefault*)\|(ExporterAppendWritesLedger*)` | V-10 V-11 | all listed, 0 failed | 2 | 6 |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c1823-s4/` | bands | `/*/*/RcStoppedRunSeedTests/(ScheduleBandLabels*)\|(WatchQualPrintsBand*)` | V-12 | all listed, 0 failed | 2 | 6 |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c1823-s5/` | hop | `/*/*/RcStoppedRunSeedTests/(MergedSeedHopAccepts*)\|(MergedSeedHopRefusesDigest*)` | V-13 V-14 | all listed, 0 failed | 2 | 6 |
| CP-7 | S6 | `tests/Antiphon.Tests -> bin-c1823-s6/` | identity | `/*/*/RcStoppedRunSeedTests/FailureIdentityJoinsSelectorRow` | V-15 | all listed, 0 failed | 1 | 4 |

## Risks, operator steps, Windows owed

The green inference for rows 1 and 2 is from the notes (P0 chunk ran, the run continued, the method is not in the five-failure export). A seed that still lists those methods keeps them in band 0. Rows 9-15 are incomplete until the operator copies the method string from the seed. `dce78ee0` has no export until that run is stopped. A method that moved off a completed chunk can look green (D-4). A squash that dropped the cherry-pick trailer is not contained; the operator passes `--Proven`. `-Strict` exits 3 on this history because several identities have no decision SHA; the restart uses exit 0. Digest equality is per PowerShell engine.

Operator steps, on the desktop, after the stopped run is exported. Nothing in this card runs on the desktop from the server2 code task.

1. Write the decision file from why-missed fix SHAs. Do not list introducing commits. Copy method strings, including `(canceled)`, from the seed.
2. Run the merge with `-Repo` pointed at the rc checkout that contains the candidate `sourceSha`.
3. Launch with the existing four switches, the merged file digest, and the newest seed's `sourceCandidateId`.
4. Keep the ledger, once slice 2 exists, outside git.

Owed natively on Windows, on a private tree, not on the desktop production runner: the same CP-1 through CP-7 filters under Windows PowerShell 5.1 as well as pwsh. The 5.1 proof is the ASCII script and the `$PSScriptRoot` body resolution. Linux pwsh is the checkpoint lane for Code on server2.
