# Task 5b21d0be: CARD-1545 S2b receipt/state/use record + S2a-2 advisories A-1..A-6 (Code, Final)

**Outcome:** S2b is implemented red-first then green, shadow-only (no state carries gate credit;
AlwaysRerun is the default). The CARD-1651 advisories A-1..A-5 are fixed with red witnesses, and
A-6 has a separating case. All checkpoint rows were run through the checkpoint tool at the tested SHA
and are GREEN: CP-9 6/6, CP-32 7/7, CP-33 6/6, CP-22 6/6, CP-31 7/7, CP-8 7/7. Receipts validate
with `buildSource=verified`. No landing was done.

- Branch `feat/card-task-5b21d0be`, worktree `/work/worktrees/task-5b21d0be` (server2/Linux), base
  `740e2a6a2391fec61d52f143f9086d73f2f9782c` (the S2a-2 tip). Original Code task and landing owner: 5b21d0be.
- Test design: `docs/superpowers/plans/2026-11-02-card-1545-S2a-S2b-test-design.md`, unchanged. The
  imported manifest is ignored: `.antiphon/s2b-checkpoints/manifest.yaml`.
- Commits, in order:
  - `ffac519386d9fb88a4dd82b2bc4497fc520679a7`: advisory witnesses (red), with a signature-only `-Roster` stub.
  - `6e8da524e153374123aa58c1049db1f7e3913bb7`: advisory fixes.
  - `8f212505848394513d09cc2a4d7470d51b06d3b7`: S2b witnesses, harness, fail-open stub and status options
    (red). This commit also carries the attestation-fixture fix, see Deviations 1.
  - `e22a2063cd7ead2a7d42572cd2ceef64d48eb637`: harness fix so every S2b red is an assertion row.
  - `5fd6ec3f2160535be8398c63332cdb8e38655b89`: S2b green. **This is the tested SHA.**
  - This report commit changes nothing else.

## Advisory fixes (CARD-1651)

| Id | Fix | Witness, and what it showed at the A1 libraries |
|---|---|---|
| A-1 | A missing or empty nonce, host or account is unknown and is never treated as equal. It is reported as `candidate-binding-missing`, `executed-binding-missing` or `probe-binding-missing:<kind>`. `-Candidate $null` no longer throws. | `ExternalEpochIsNotInferred/binding-missing`: red. It includes the review's repro (empty candidate, epoch-only executed, bare probes). |
| A-2 | `Read-LiveLaneJsonDocument` refuses `attestation-link` when the file or its directory is a reparse point. It refuses `attestation-too-large` above 1 MiB, and reads at most cap+1 bytes. Receipts, use records and context reuse this reader. | `UnknownSettingsRefuseReuse/linked-document` (directory link: a junction on Windows, a symlink elsewhere; plus a file symlink off Windows; a copied file stays complete) and `size-cap` (cap+1 refused, exactly the cap accepted): both red. |
| A-3 | `Get-LiveLaneEnvironmentAttestation -Roster` binds the rows to the lane roster. A missing roster uid is `roster-missing:<uid>`. An out-of-roster uid gives `roster-unexpected`, and its row is named `row-<n>` so the uid is never echoed. With no roster the result is `roster-unbound`, which is unknown. `Get-LiveLaneFingerprint -Attestation` passes the manifest roster (`Get-LiveLaneManifestRoster` / `ConvertTo-LiveLaneRoster`: namespace.class.method(arguments)). The real manifest's roster equals the six `LiveClaudeCore.All` uids. | `UnknownSettingsRefuseReuse/roster-bound` (short document, sentinel uid not echoed, unbound): red. |
| A-4 | A malformed or null probe `atUtc` is `probe-time-invalid:<kind>` and its value is not echoed. A bad `NowUtc`/`ExecutedAtUtc` throws `time-invalid`. | `ExternalEpochIsNotInferred/probe-time-token`: red. |
| A-5 | The credential-name scan is case-insensitive (`-contains`). | `ConfigurationInvalidates/credential-case` (camel, upper and list-nested variants all give `credential-field-present`; no sentinel in the error): red. |
| A-6 | No source change was needed. New separating case: candidate and probes equal, executed host or account differs. | `ExternalEpochIsNotInferred/executed-host-account`: green at A1, because the guard already existed. Mutant run (drop both `-cne $Executed` halves, restored afterwards): red on exactly this row (13/14). |

Advisory red record, run against the e22a2063 build with the attestation and fingerprint libraries
reverted to ffac5193 (lease `c1545-advisory-red-run`, waited 0 s):
- 3 of 6 methods failed.
- 6 FAIL rows, exactly the 6 new advisory rows. Every other row passed, and there were no harness crashes.

## What S2b delivers

New files:
- `scripts/lib/live-lane-receipt.ps1`
- `scripts/test-live-lane-receipt.ps1`
- `tests/Antiphon.Tests/Scripts/LiveLaneReceiptTests.cs` (Integration, `ParallelLimiter<ProcessSpawnLimit>`, 6 methods, 54 rows)

Changed files:
- `scripts/live-lane-status.ps1`
- `scripts/lib/live-lane-fingerprint.ps1`: `Get-LiveLaneFingerprintDigest` factored out unchanged, the roster reader added, and the no-history reason renamed to `no-history`.
- `scripts/lib/live-lane-attestation.ps1`: the advisory fixes.
- The attestation harness and test: 7 new rows.

Byte-identical to `10c8c2471` (checked with `git diff --exit-code`): `rc-project-selection.ps1`,
`test-selection.ps1`, `tests/live-lanes.json`, `tests/Shared/LiveClaude/*`, the S1 `live-lane-*`
libraries, `run-live-lane.ps1`, `tests/test-execution-policy.json`, and every csproj/props/targets file.

**`Test-LiveLaneReceipt -Receipt -Manifest|-Roster`** checks, and reports:
- The exact key set of `kind='live-lane-execution'`, schemaVersion 1. An unknown field is `field:<name>`, and only a plain identifier is echoed as the name.
- Types: `type:<f>`, `required:<f>`, `negative:<f>`.
- The roster as an exact multiset: `roster-empty`, `-duplicate`, `-unexpected`, `-incomplete`. It also checks `planned==roster`, `executed==planned`, `missing==0`.
- That counts equal the row outcomes: `counts-mismatch`.
- The closed outcome enum: `outcome-enum`.
- `finalExit==classifierVerdict` (`classifier-mismatch`).
- The fingerprint digest, recomputed from its components and lane: `fingerprint-tamper`.
- `rosterDigest` against the candidate roster: `roster-changed`.
- `inputBefore==inputAfter`: `input-drift`.
- Time order: `time-order`.
- Artifact SHA-256 beside the receipt: `artifact-digest:<name>`.
- Authority: only `diagnostic` is accepted (`issuer-unqualified`).

The outcome is the worse of the rows and the classifier verdict, so a skip is INCONCLUSIVE (20) and never GREEN.

**`Get-LiveLaneState`** works in this order:
1. An unknown fingerprint or environment is DUE before any receipt (`unknown-input`, `environment-unknown`).
2. Every history record is validated. Any invalid record makes the result DUE `history-invalid`, which fails closed. One run id with two different receipts throws `contradictory-receipts`.
3. A receipt from another lane is excluded and gives `foreign-lane`.
4. The newest valid clean attempt for this fingerprint decides, ordered by `endedUtc` then `sequence` then digest. A newer RED, INCONCLUSIVE or INVALID attempt gives `newer-*`.
5. A dirty receipt is never selected (`source-dirty`). A use record is never a source (`no-execution-receipt`). With no match the result is `fingerprint-changed:<component>`.
6. A revocation record (`live-lane-revocation`) gives `revoked`.
7. Repository, rc ref, host and account are compared, and real git ancestry is checked (`foreign-repository`, `foreign-ref`, `host-mismatch`, `account-mismatch`, `not-ancestor`, `source-missing`). All of this happens before freshness, and the state reports `freshness=$null`.
8. D-6 freshness is checked last, using the receipt's `endedUtc`.

The state reports `localCompatibility`, `reuseEligible` and `lastGreen` (the newest GREEN only). Every
state is `gateCredit=$false`, `effectiveMode='Full'`, `activation='shadow-only'`.

**`New-LiveLaneUseRecord`** writes `kind='live-lane-use'` with `CreateNew` (`use-record-exists`) and
only from a SATISFIED state. The record has `executedNow=0`, zero counts, `reused=$true` and
`gateCredit=$false`. The receipt is never touched. `Test-LiveLaneUseRecord` refuses a forged
`executedNow` as `use-executed`.

**Status:** `live-lane-status.ps1 -History <dir> -Context <json> [-ReusePolicy AlwaysRerun|Bounded24h] [-UseRecord <path>]`.
- The context is strict: `repository`, `ref`, `host`, `account`, `nonce`, `nowUtc`, `probes`, `revocations`.
- The default policy is AlwaysRerun, and in that case no use record is written.
- Without `-History`, `-Context` and `-UseRecord` refuse as `history-required:<name>`.

## Checkpoints (closed list; checkpoint tool, unedited lines)

Run `20261102-131928-df1a` (`--after S2b --serial --total-timeout 30m --hard-wall-timeout 35m`) finished GREEN, exit 0:

```text
CHECKPOINT CP-9 commit=5fd6ec3f2160535be8398c63332cdb8e38655b89 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneReceiptTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-5b21d0be/.antiphon/checkpoints/20261102-131928-df1a/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=5fd6ec3f2160535be8398c63332cdb8e38655b89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=101.513s wall=101.570s
PHASES CP-9 slotWait=90s build=309.7367585s startup=5.1719886s testsWall=95.1464998s teardown=0.4838903s hostWall=100.8025459s
CHECKPOINT CP-32 commit=5fd6ec3f2160535be8398c63332cdb8e38655b89 build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5b21d0be/.antiphon/checkpoints/20261102-131928-df1a/rows/CP-32/run.trx slot=granted waited=45s dirty=0 source=5fd6ec3f2160535be8398c63332cdb8e38655b89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-32 waited=45s executed=200.918s wall=245.966s
CHECKPOINT CP-33 commit=5fd6ec3f2160535be8398c63332cdb8e38655b89 build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-5b21d0be/.antiphon/checkpoints/20261102-131928-df1a/rows/CP-33/run.trx slot=granted waited=15s dirty=0 source=5fd6ec3f2160535be8398c63332cdb8e38655b89 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-33 waited=15s executed=31.733s wall=46.780s
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

Run `20261102-133531-04ff` (`--after S2a-2`, 30m/35m) finished GREEN, exit 0:

```text
CHECKPOINT CP-22 commit=5fd6ec3f2160535be8398c63332cdb8e38655b89 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneAttestationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-5b21d0be/.antiphon/checkpoints/20261102-133531-04ff/rows/CP-22/run.trx slot=granted waited=15s dirty=0 source=5fd6ec3f2160535be8398c63332cdb8e38655b89 sourceState=clean buildSource=verified
CHECKPOINT CP-31 commit=5fd6ec3f2160535be8398c63332cdb8e38655b89 build=reused filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5b21d0be/.antiphon/checkpoints/20261102-133531-04ff/rows/CP-31/run.trx slot=granted waited=0s dirty=0 source=5fd6ec3f2160535be8398c63332cdb8e38655b89 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

Run `20261102-135149-397d` (`--after S2a-1`, 25m/30m) finished GREEN, exit 0:

```text
CHECKPOINT CP-8 commit=5fd6ec3f2160535be8398c63332cdb8e38655b89 build=ok filter=/*/Antiphon.Tests.Scripts/LiveLaneFingerprintTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-5b21d0be/.antiphon/checkpoints/20261102-135149-397d/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=5fd6ec3f2160535be8398c63332cdb8e38655b89 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
```

`validate --evidence <report.json> --expected-source-sha 5fd6ec3f…` returned `CHECKPOINT SOURCE VALID`
for all three runs (rows=3, 2 and 1). Each fresh TRX names exactly the intended methods, all
`Passed`: the 6 receipt methods, the 6 attestation methods, and the 7 fingerprint methods in each of
CP-8, CP-31 and CP-32.

Timing:
- Builds took 5m10s to 8m40s.
- The receipt class took 95 s; the slowest method was about 20 s, against a 60 s budget.
- The fingerprint class took 193 to 237 s (StableCanonicalInputs has a 120 s budget).

YAML pins (ignored file): `slotWaitMinutes 15`, `rowMinutes 16`, `maxRows 1`. Row timeouts are
CP-9/22/33 8 min and CP-8/31/32 10 min, as in S2a-2. The design's 4 min would cancel the fingerprint
rows on this host.

## Other runs (explained unlisted; one lease each)

1. Gated native build `c1545-s2b-red-build` at `8f212505`: waited 0 s, held 154 s. This one dll
   served every probe below, because only PowerShell changed after it.
2. **S2b red** at `e22a2063` (`c1545-s2b-red-run-2`), run against the stub:
   - 6/6 methods failed.
   - 54 rows: 52 FAIL and 2 PASS. The 2 PASS rows are the stub-green `ExactRosterRequired/exact` and `TypesAndCountsFailClosed/valid`.
   - All failures were assertion rows.
   - The first red run (`c1545-s2b-red-run`, at 8f212505) exposed a harness crash in ReuseIsNotExecution; it was fixed in e22a2063.
3. Advisory red (see above).
4. Green probe `c1545-s2b-green-probe` with the real library before commit: 12/12 (receipt plus attestation).
5. A-6 mutant (see above), restored and checked with `cmp`.
6. Plain `pwsh` harness runs of five receipt methods (no dotnet) while authoring.
7. Land graph `scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll`
   (gated `c1545-s2b-landgraph`, 57 s): exit 0, **approved**,
   `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`. This is the
   expected value; `-Update` was not run.
8. `check-evidence-diff -Repository . -BaseRef 740e2a6a… -HeadRef HEAD`: commits=5, violations=0.
9. Merge checks:
   - `git merge-tree --write-tree` onto `origin/rc/2026-11-01` `10a826b9…` is clean (tree `22a9d199…`).
   - Onto `origin/master` `43ae7371…`, the plain merge-tree gives **add/add on the S2a files**. The cause is that master now carries S2a-1/S2a-2 rebased (`e18a1177d`, `e5d111209`), byte-identical to this base (`git diff 740e2a6 origin/master` over those paths is empty).
   - `git merge-tree --write-tree --merge-base 740e2a6 origin/master HEAD` is clean (tree `7b1e416b…`). So a rebase land (drop the carried S2a commits, replay ffac5193..5fd6ec3f) applies cleanly.
   - Nothing was merged to resolve this.
10. Unit lane: **not run; deferred to final, not passed.** The change is PowerShell plus one new
    Integration class. No production C#, csproj, policy or shared helper changed, and the land-graph
    pin is unchanged. The whole lane is about 67 min serial on server2, which is outside the
    150-minute brief.

## V / R outcomes

- V-14..V-19 (54 named rows: 8, 13, 10, 8, 7, 8), R-7 and R-8, and the S2b parts of R-3 and R-6: passed in CP-9.
- V-8..V-13 plus the 7 advisory rows (now 51 rows: 5, 4, 5, 11, 12, 14): passed in CP-22 and CP-33.
- V-1..V-7 and R-1..R-3: passed in CP-8, CP-31 and CP-32.
- All five scoped-rc rules have their witnesses green:
  - Shadow-first: V-18 `gate-credit` over 7 states, including two status documents.
  - Stale or foreign receipt: V-16 all, V-19 `stale`, V-13.
  - D-6: V-18 `policy-default` through the status front door, plus the Bounded24h control (SATISFIED, use record written).
  - Fingerprint completeness: V-1..V-7, V-12.
  - Never skipping an affected project: V-2..V-7.

## Deviations (none weakens a witness)

1. The advisory witnesses were added as rows of the existing CP-22 methods, with matching harness
   counts: ConfigurationInvalidates 10->11, UnknownSettingsRefuseReuse 9->12, ExternalEpochIsNotInferred 11->14.
   The fixture manifest now lists the six roster rows.
   The A2 commit `6e8da524` is not green on its own: the A1 fixture's `Split-Uid` unrolled a
   one-element argument list, so the new roster read refused the fixture manifest. The fixture fix
   is in `8f212505`. Every gate ran at `5fd6ec3f`.
2. API choices the design left open:
   - `Get-LiveLaneState` takes `-Roster`; `Test-LiveLaneReceipt` takes `-Manifest` or `-Roster`.
   - The status binding facts come from a strict `-Context` JSON.
   - Revocations come either as history records (`live-lane-revocation`) or through `-Revocations` (incident epoch or receipt).
   - An invalid history record makes the state DUE (`history-invalid`); this fails closed.
   - The lane id is part of the fingerprint domain, so `foreign-lane` excludes other-lane receipts before fingerprint matching.
3. Several rows are stronger than the design's stub-green marking, so they are red on the stub:
   - V-16 `same-line-ancestor`, V-17 `red-then-green` and `other-fingerprint-red`, and V-19 `equal-all`: they also assert the selected receipt digest or `localCompatibility`.
   - V-18 `receipt-immutable`: it also asserts that the use record names the unchanged digest.
4. With no history, the status reason is `no-history`; it was `no-receipt-validator`.
   The fingerprint tests do not pin this reason.

## Pending for Mutation (SourceLanding, method-scoped)

- PC-83..PC-130 (S2b; every row is binding-missing until the binder runs at the landed source).
- PC-1..PC-82 (S2a-1/S2a-2) remain pending.
- **Unbound candidate PCs for the advisory guards** (not in the design's 130; a mutations amendment
  under `docs/superpowers/mutations/` should bind them):

| Advisory | Mutant | Expected red row |
|---|---|---|
| A-1 | Drop the candidate/executed binding checks | `ExternalEpochIsNotInferred/binding-missing` |
| A-1 | Drop the probe binding check | `binding-missing` (part c) |
| A-2 | Drop the reparse check | `UnknownSettingsRefuseReuse/linked-document` |
| A-2 | Drop the size cap | `size-cap` |
| A-3 | Drop roster-missing | `roster-bound` |
| A-3 | Echo the uid instead of `row-<n>` | `roster-bound` |
| A-3 | Accept an unbound roster | `roster-bound` |
| A-4 | Cast the probe time unguarded | `probe-time-token` |
| A-5 | `-ccontains` | `ConfigurationInvalidates/credential-case` |
| A-6 | Drop the executed halves | `executed-host-account` (shown red in this task) |

## Open questions

- The master plain merge-tree is add/add because S2a was landed rebased. The landing must rebase
  (drop the carried S2a commits); `--merge-base` proves the result is clean. Alternatively the
  branch can be re-cut onto master. That is the caller's choice.
- A history directory that contains an invalid or foreign record keeps the lane DUE until the
  operator removes that record. This is fail-closed by design; tell me if a quarantine rule is
  wanted instead.
