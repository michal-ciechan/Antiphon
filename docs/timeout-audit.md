# Timeout audit (CARD-1563)

A timeout detects a hang; it is not an execution estimate. R5 in
[test-performance-standards.md](test-performance-standards.md) owns the policy.
Use the read-only `tools/Antiphon.TimeoutAudit` lint before tightening a test or row:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label timeout-audit -- dotnet build tools/Antiphon.TimeoutAudit --property:OutputPath=bin-timeout-audit/
dotnet tools/Antiphon.TimeoutAudit/bin-timeout-audit/Antiphon.TimeoutAudit.dll --repo . --receipts .antiphon/checkpoints --output .antiphon/timeout-audit.json --check
```

`--receipts` may occur repeatedly, naming explicit retained checkpoint roots from other
worktrees. Audit input is read-only; its report belongs outside tracked source. Exit 0
means no measured excess or input warning; it **does not** mean provisional budgets have
been qualified. Exit 1 under `--check` means measured excess or an input warning; exit 2
means invalid options/input. Without `--check`, candidates are reported without failing.
This is an opt-in lint; nightly/release gate semantics and watchdog execution are unchanged.

The inventory includes C# Timeout attributes (effective method/class values), helper
wait/delay/cancellation and timeout assignments, script/client/tool deadline expressions,
policy watchdogs, and checkpoint Markdown rows. Expressions that cannot be statically
resolved remain in the inventory with null seconds. It scans tests, scripts, tools, client
and the plan directory, skipping build outputs, dependencies and links. It is a syntactic
inventory, not a claim that every dynamic/generated or inherited deadline is resolved.
A class default remains separately visible when partial/inherited tests cannot be expanded.
Checkpoint rows expose the current importer's `max(15, 3*EstimatedMinutes)` watchdog;
this implementation fact is not endorsed as R5 policy. The CLI does not edit plans,
timeout declarations, checkpoint tooling, costs, policy hashes or release evidence.

TRX final durations provide per-case samples; checkpoint `testHostWallSeconds` provides
per-row execution wall. Exact filter membership, expanded case display identity, source
SHA, OS, host, load observation and concurrency separate comparable series. Independent
run/row identities deduplicate copied receipts; conflicting copies warn. Failed cases,
failed rows and retry attempts remain observations but do not estimate a successful p95.
Missing/corrupt TRX, unknown attribution and unavailable execution wall warn. An orphan
TRX cannot qualify a cross-host estimate. Summed case durations, admission wait and build
are never substituted for execution wall. Slot wait is retained separately.

Each budget reports sample count, median, nearest-rank p95, maximum and the recommendation
`ceil(2*p95)` seconds, with a one-second resolution floor. Under 20 ordinary successful
comparable samples, or missing host/load/concurrency metadata, it is **provisional**.
These are observed percentiles, not claims that a small sample establishes a measured p95.
Missing history has null statistics/recommendation. Existing nightly-class-costs.json
rows retain their seconds and basis as cost inventory; a serial class total or a partial
`pace` bound is never presented as a p95. Every provisional row carries CARD-1563 and the
bounded collection plan: 20 comparable runs within 14 days, including ordinary shared-host
load/slot observations, then re-audit before tightening. Use routine future runs, not
repetition solely to fill the audit. Existing repeat-proof limits still apply.

For sufficient comparable history, a timeout above the recommendation is a `candidate`
and makes `--check` fail. A deadline at/below it is `already-tight`. An exception file is
an array of exact declarations (the report supplies stable path/line/kind/subject IDs):

```json
[
  {
    "declarationId": "tests/Example.cs:10:test:Example.Tests.Method",
    "justification": "Explain the measured operation needing more time",
    "measurement": "Exact retained receipt/run reference",
    "card": "CARD-1563"
  }
]
```

Pass it with `--exceptions <path>`; blank reason, measurement or card does not exempt a
candidate. Exceptions are declaration-specific; a test exception does not exempt its row.
Keep exceptions reviewed and tied to current source locations. The audit does not establish
that a generous deadline hides a hang: inspect its preserved failed/incomplete observations
and commission Debug with run identity and load evidence before making that claim. It does
not infer a blanket load multiplier from slot wait. Tighten in a later measured slice,
first using loaded-host observations in the same context; unknown load stays provisional.

Windows log paths and live Windows timing inputs are not reachable from a Linux runner.
Copy/export the retained receipts through the ordinary evidence path before making a
Windows budget claim; do not relabel Linux timings as Windows evidence.

The six audit tests are shared source: their small standalone project is a declared child
checkpoint host, and Antiphon.Tests compiles the same file and references the audit tool.
The shipped execution-policy census retains seven primary suites and now declares seven
child hosts. This registration changes the policy hash, without changing any watchdog,
suite, exclusion or release gate behavior.
