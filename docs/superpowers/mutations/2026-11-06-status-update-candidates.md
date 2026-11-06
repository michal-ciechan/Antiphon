# Status snapshot: unbound Mutation candidates

Original Code/landing owner: `23abede2-8099-4671-a46b-b0fcbd40dc27`.
Plan: [status update skill](../plans/2026-11-06-status-update-skill-plan.md).
Implementation and ordinary affected-test source:
`e237b4dd71ba19fa5d64b6361e5b4fa517c0bee9`.

These are candidates, not a bound pack or execution receipts. Every control and
variant is **pending**. Mutation owns binding to the landed source, missing-control
discovery, deliberate mutants, assertion-red/restore/green and restoration records.
Neither the ordinary red-first test nor ordinary/nightly green discharges a PC.

All three candidates use the same exact method-scoped filter in `Antiphon.Tests`:

```text
/*/*/StatusSnapshotScriptTests/Snapshot_reads_scoped_tasks_counts_remote_master_and_rc_without_writes_in_text_and_json
```

| ID / variant | Candidate defect in scripts/status-snapshot.ps1 | Discriminating outcome | State |
|---|---|---|---|
| PC-1 / omit-queued | Omit accumulation of the queued rows, leaving the count at zero. | The stub supplies two queued rows; parsed JSON must report 2. | Pending Mutation |
| PC-2 / substitute-master | Substitute a different SHA for the remote master tip. | Text and parsed JSON must contain the fixture's actual remote master SHA. | Pending Mutation |
| PC-3 / persistent-write | Create or overwrite a file beneath the supplied Repository during snapshot execution. | The fixture compares its complete file census and bytes before and after both invocations. | Pending Mutation |

The test runs the actual PowerShell script with an authenticated HTTP stub, a
private real Git remote and private rc files. It checks text and JSON, explicit
project selection and unique-project discovery, counts, master, rc progress and
heartbeat age. Its first green method duration was 5.020159 seconds on shared
Linux at the implementation SHA; one ambient-load sample is not a p95 or a bound
Mutation budget. Binding must account separately for build, admission and execution.

Code ran no deliberate mutants. Final ordinary Unit qualification remains
incomplete: the first full Unit attempt reached its existing 45-minute execution
limit without TRX. Its two console failures were individually reproduced at base
`df1fc145181a3bedf444f9113f822f30d3b136f6`. Required ordinary verification and
Review precede landing; the caller commissions SourceLanding Mutation afterwards.
Restart impact: none.
