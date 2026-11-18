# CARD-1989 control window and timings

Base: e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4. Code owner: 62cec2de.

The coordinator and live proof assertion share a configurable wall-clock budget.
Use `ANTIPHON_RETIRE_CONTROL_WINDOW_MS`, default 8000, range 1000..8000.
The ceiling preserves the host's existing 10-second response read and 11-second
effect fence. A 15-second default would require widening those independent gates;
that is outside this repair. No proof, ref, nonce, renewal or effect gate changes.

Record proof read, bundle hash, expected parse, ls-remote, live parse, comparison
and cleanup durations, including failures, in the coordinator receipt. Fixed step
names and numeric times only; no URLs, advertisements or raw errors.

S1 commits offline red witnesses before production changes. S2 implements the
window and timings. No deliberate mutants in Code; SourceLanding Mutation owns
PC-1 (remove budget check), PC-2 (discard timings), PC-3 (ignore configuration),
PC-4 (omit failure receipt), all pending; operator has waived mutation execution.

## Performance budget

Script witnesses use one tiny temporary proof, no database, Docker or network.
V-1 deliberately waits 5.5 seconds, V-2 1.2 seconds, others under 2 seconds plus
PowerShell startup. Estimates are provisional for CARD-1989, collect one red and
one green sample. No repeats after green. The first row's 15-minute estimate is
the brief's cold bootstrap budget, not an execution timeout increase. Each row
is serial and capped at 570 seconds. Build the checkpoint driver in native bin/;
command rows need no build, avoiding prohibited custom output directories.

## Verification design

V-1: a live read over the old five seconds passes inside eight seconds.
V-2: a hash that exceeds a one-second configured budget refuses before Git, names
bundle-hash and reports its actual elapsed time.
V-3: all step durations are populated and Git receives only remaining budget.
R-1: withdrawn refs and bundle tampering still refuse.
V-4: absent, empty, valid and invalid configuration states.
V-5: coordinator persists success/failure timing receipts (offline protocol fixture).
R-2: affected class selections, bounded to short rows; no whole slow class.
R-3: pin recomputation, land graph, evidence diff, merge-tree, OS branch audit.
V-6: inspect whether renewals can share a bundle without changing proof semantics.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1-S2 | n/a | window | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case window` | V-1 | 1 PASS, zero failures | n/a | 15 | true |
| CP-2 | S1-S2 | n/a | deadline | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case deadline` | V-2 | 1 PASS, zero failures | n/a | 1 | true |
| CP-3 | S1-S2 | n/a | timings | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case timings` | V-3 | 1 PASS, zero failures | n/a | 1 | true |
| CP-4 | S1-S2 | n/a | guards | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case guards` | R-1 | 1 PASS, zero failures | n/a | 1 | true |
| CP-5 | S1-S2 | n/a | config | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case config` | V-4 | 1 PASS, zero failures | n/a | 1 | true |
| CP-6 | S1-S2 | n/a | receipt | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case receipt` | V-5 | 1 PASS, two protocol exchanges, zero failures | n/a | 1 | true |

Native Windows execution is not claimed. Changed logic is portable PowerShell,
with no new OS branch or production factory. Production script remains ASCII and
uses PowerShell 5.1-compatible syntax. No live retire-temp preview/apply is allowed.
Restart: none.
