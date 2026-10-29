# CARD-1605 Final Review (Interim scope): task cec313dd

**Verdict: clean.** I found no defects. Subject task 245c48d2, tip
`ee2130f52ff91823b63c3a35bbae5e03203a7227` (red witnesses `d51349ec9`, base `3f2a6834e`).
The product code implements the reviewed plan exactly. Every new witness I mutated went red for
the reason it exists to catch.

Disclosure: the brief commissioned Interim scope, so `ordinaryScopeCompleted: Interim`. I did not
run the whole Unit lane. C1210 skips on Linux (Windows-only placement), so it was not run green
here.

## Product review (fail-closed)

1. **Plan conformance.** `AgentReportStore.StoreAsync` uses two sequential linked CTSs.
   - Preparation: 30 s default, linked to the caller. It is armed before `root` and checked after
     `ResolveRootAsync`, after `EnsureIgnoredAsync` on both branches and after `HasReparseAncestor`.
     It is disposed at the boundary.
   - I/O: `CreateLinkedTokenSource(ct)` with `CancelAfter(IoBudget)`, where IoBudget is still 2 s.
     It is armed after preparation, immediately before `existing-read`. One token runs through
     write, publish and readback.
   - Final token checks: before the reuse return, before `File.Move` and after readback.
   - Decision order: caller → rethrow; preparation owner while `!ioStarted` →
     `preparation-budget-expired`; I/O owner → `io-budget-expired`; anything else → `canceled`
     (non-throwing).
   - Owned-temp cleanup stays in `finally`.
   - Diagnostics: TaskId, phase, outcome, phases list, Total/Preparation/Io/Cleanup ms and reason.
     No bytes or paths are logged; V-9/V-11 assert this with sentinels.
   - Regeneration (`AgentTaskLandNotificationService.cs:406-429`): a 5 s linked allowance
     surrounds only StoreAsync. The preliminary `IsUsableAsync` stays on the caller token. The
     caller is checked first, then the allowance. The local catch is
     `OCE when (!ct.IsCancellationRequested)`, returning `completion_report_unavailable`, so
     `ReconcileAsync` saves RetryPending, attempts+1 and NextAttemptAt.
   - No interface or schema change. The settlement, cleanup and dispatcher caller code is
     unchanged.
2. **CARD-1032 window** (`AgentReportStore.cs:72-80`). The I/O CTS is created from the original
   `ct` only after preparation finishes. A slow preparation cannot shorten the 2 s window, and a
   fast one cannot lengthen it. V-3 is green at the tip: it measured ≥1.9 s from I/O start to
   cancellation, and the 2.5 s write stall gave null Path, `io-budget-expired`, no file and no
   temp file.
3. **Preparation ceiling.**
   - `GitProcessGate.EnterAsync` waits with `SemaphoreSlim.WaitAsync(prep)`.
   - On cancellation, `RunGit` (`GitWorkspaceService.cs:1178-1182`) kills the child's whole
     process tree and rethrows because the token is canceled. The lease is released by `using`.
   - Expiry returns `preparation-budget-expired`. No temp file exists before the boundary, so
     nothing can be half-published.
   - V-5 proves the waiter is gone and the gate can be re-acquired.
4. **Cancellation outcomes.** Caller cancel, preparation expiry, I/O expiry and unrelated OCE give
   distinct outcomes (V-6/V-5/V-3,V-4/V-11). The store throws only on caller cancellation, as it
   did before. A regeneration allowance expiry is handled locally. V-13 shows row 1 RetryPending
   and row 2 AwaitingReceipt with one queue row in the same pass, with the gate still held.
5. **Red witnesses.** See the mutation table below; every new witness was red.
6. **Roster.**
   - Recounted from the JSON rows: base rows were 699/504/195/7044/4663, against a published
     698/503/195/7038/4657 (pre-existing drift). Tip rows are 701/504/197/7057/4663, matching the
     committed summary.
   - SHA-256 of the three sources matches the census: e599830c…, 0c2dfc34…, ce6bbeba….
   - `[Test]` counts are 11/1/21, and the helper closures match.
   - The allowlist pairs are present, and CP-6 is green.
7. **Callers.** The settlement 32 s, cleanup 32 s per endpoint, maintenance 32·k s and
   regeneration 5·m s worst cases are the plan-accepted ones. No code changed at those sites.
8. **Names and census.** Nothing else changed. No secrets are logged.

## Mutation and pre-fix results (build-slot, UseAppHost=false, dotnet exec)

| Product | Witness | Result |
|---|---|---|
| pre-fix d51349ec9 | V-1, V-2 | red: `result.Path` null, Reason `io-budget-expired` (CP-1 confirmed) |
| pre-fix | V-3 | red: `ioStart` null (no existing-read hook; the plan says it is not a CP-1 witness) |
| pre-fix | V-13 | red: `FirstCanceled` false (2 s refusal, not a 5 s cancel) |
| M12 I/O timer +1 s | V-3 | red: path published |
| M5 no reuse check | V-7 existing-read | red: did not throw |
| M4 no final readback check | V-7 readback | red: did not throw |
| M6 rethrow unrelated OCE | V-11 root, V-11 write | red: OCE escaped |
| M2 no preparation timer | V-5 | hung; killed by my 60 s timeout (exit 124) |
| M7 no regeneration allowance | V-13 | red: 10 s pass watchdog TimeoutException |
| M1 preparation = IoBudget | V-1, V-2, V-8 queued | red: Path null / `artifact_unpreserved` |
| M3 fresh I/O token at publish | V-4 publish, V-4 readback | red: token identity |
| M10 swallow caller cancel | V-6 ×4 (and V-7 ×2) | red: did not throw |
| M11 preparation reason → io | V-5, V-9 preparation-expired | red: reason mismatch |
| M8 alone, no local catch | V-13 | red: row 1 `Queued`, not RetryPending |

In batch B, M10 masked M8 (the swallowed cancel avoided the OCE). I re-ran M8 in its own build
(the last row). I did not mutate V-10, which is timer-independent by design, or V-12, the opt-in
sampler.

## Green reruns at ee2130f52 (clean tree; built assembly stamped with the full SHA)

| Row | Counts |
|---|---|
| CP-2 | 49/49 passed |
| CP-3 | 8/8 passed |
| CP-4 | 1/1 passed |
| CP-5 | 1/1 passed, with `ANTIPHON_REPORT_STORE_MEASUREMENTS=1` |
| CP-6 | 2/2 passed |
| CP-7 | 1/1 passed |

That is 62 green cases, with notExecuted=0 on every CP row.

CP-5 ran at loadavg 14.7/24.2/19.8. All buckets were 20/20 successes with no refusals; queuePeak
was 16 and gatePeak 8.

| Bucket | Preparation p95 / max (ms) |
|---|---|
| normal-4k | 32 / 101 |
| normal-64k | 15 / 15 |
| loaded-4k | 55 / 57 |
| loaded-64k | 56 / 83 |

These are far inside the ≤15 s / <30 s bounds.

Extra classes:

| Class | Result |
|---|---|
| RcRosterRepairTests + C1514LandingSplitBudgetTests (C1404, C1577, C1584, C1410, G2865) | 8/8 passed |
| VerificationRoundDeliveryTests + CompletionWarningDeliveryTests | 32/32 passed |
| CompletedCardWorktreeRemovalTests (artifact preservation consumer) | 37/37 passed |
| WindowsUnitPlatformAdmissionTests.C1210 | not executed: Windows-only skip |

C1210's child filter does not include any CARD-1605 class.

Other checks:
- `check-evidence-diff.ps1`, base f8bddb6e..ee2130f5: 11 commits, 0 violations.
- `git merge-tree --write-tree origin/master` (master e6534313) gave tree 8110a71d with exit 0.

## Advisories (non-blocking)

- **A-1.** The author's report says the plan heading is `## Checkpoints` and that the importer
  cannot read it. That is wrong. Plan line 372 is `### Checkpoints`, and
  `Antiphon.Checkpoints import --plan` imports all 7 rows with exit 0. Code therefore skipped the
  checkpoint tool on a false premise and left no tool receipts. This review's independent reruns
  cover CP-2..CP-7. No plan change is needed.
- **A-2.** V-5 and V-9's preparation-expired case have no in-test deadline. A lost preparation
  bound hangs (M2) until the row or process timeout, instead of failing within the plan's 10 s
  per-case bound. The plan accepted "would hang".
- **A-3.** V-4 asserts cancellation within 2600 ms of I/O start. It passed here under load, but
  the margin is tight on a busy host.
- **A-4.** `ReportBudgetWorkspace.TemplateAsync` deletes and recreates the per-checkout template
  on each host's first use. Two concurrent test hosts in one checkout could race. The CP rows are
  Serial, so this is fine as planned.
- **A-5.** The fixture puts its cases under the checkout's `.antiphon/test-output`. The store
  refuses roots under `$TMPDIR`, so a checkout under `/tmp` fails every Budget case with
  `no-durable-root`. My scratch copies needed `TMPDIR` overridden.
- **A-6.** Regeneration discards a verified store if the allowance fires after StoreAsync returns.
  The plan mandates this, and the next pass's `IsUsableAsync` accepts the file.

The worktree `bin-c1605rv` outputs and scratch copies have been removed. TRX files stay in session
scratch.
