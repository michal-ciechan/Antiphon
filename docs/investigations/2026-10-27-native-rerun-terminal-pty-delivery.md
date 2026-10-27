# Native Windows rerun: terminal, PTY and delivery

Task: `32365cdf`. Investigation in progress; no green claim or confirmed root cause yet.

## Provenance

- Requested rc: `origin/rc/2026-10-27`, fetched at `d1d090f766451797a3bd9b30c9d2966f2f8c034c`.
- Assigned branch: `feat/card-task-32365cdf`. It was unpublished and clean, initially based at `718bd29f4a1504c64c491d607d31403fcb87dae2`. Repositioned the unpublished branch onto the requested rc with no task commits replayed or published history rewritten.
- Host: Windows 10 Pro `10.0.19045`, .NET SDK `10.0.300`; preflight available memory 9,424 MiB; no matching Antiphon test hosts, FakeClaude or FakeGrok processes; native qualification lock absent.
- Historical results (read-only): `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`.
- Current generated evidence: `C:\Antiphon\worktrees\card-task-32365cdf\logs\native-32365cdf` (gitignored).
- One native build of the three required project graphs completed at report commit `2a13310b17aac1874eac018fbd07c04ccb5a0402` (same product/test source as rc): exit 0, 1,240 warnings, 0 errors, 9m04s compiler time; lease granted immediately and held 546s. Product and test source are unchanged. The warnings include nullable/analyzer diagnostics and an ASP.NET analyzer exception; a successful build is not a warning-free build.

## Results

Pending the native runs. Each class will retain its exact filter, pass/fail/skip counts, errors and stack in generated evidence. The historical digest is corroboration; raw TRX rows own the before counts.

First completed class: `SessionMessageQueuePtyIntegrationTests`, historical 3 passed / 5 failed; current 6 passed / 2 failed in 115.35s including gate/launch. Its fresh TRX is `logs/native-32365cdf/SessionMessageQueuePtyIntegrationTests-1/results.trx`.

## Evidence established while reruns continue

1. **Confirmed stale batch expectation (fixture).** The failed `Batched_multiline_body_passes_composer_delivery_verification_and_submits_once` reached the child transcript. Expected lines were `[T] batched alpha` and `[T] batched omega`; actual complete lines include `[antiphon-channel:308db271a0214ea2818c76e0223eb4e0]` and `[antiphon-channel:f13bab6df0e646d4ae948c4aaad578a6]`. The assertion constructs unmarked text at `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs:393` and compares the recipient file at `:395`. Production marks each initial channel batch member at `server/Application/Services/SessionMessageQueueService.cs:2627`; `ChannelPromptCorrelation.cs:76` defines the first-attempt eligibility and `:15` the marker. This is not evidence of a lost or partial prompt.
2. **Confirmed Windows filesystem assumptions (fixture), independent witness.** `logs/native-32365cdf/file-witness.log` records a newly created Git worktree `.git` as `Hidden, Archive`. `File.WriteAllBytes` throws `UnauthorizedAccessException`; opening that same file with `FileMode.Open`, `FileAccess.Write` succeeds (`CanWrite=True`). Thus the failure does not establish absent write permission. `CompletedCardWorkspaceInspectionRepairTests.cs:139` overwrites that hidden file with `File.WriteAllText`; `CompletedCardWorkspaceS5Tests.cs:96` uses `WriteAllBytes`. The same witness creates `Twin` then `twin`, obtaining exactly one file with content `second`; `CompletedCardWorkspaceInspectionRepairTests.cs:144` expects two. Native class rerun is pending.
3. **Cleanup race under investigation.** In the first class's `Launch_args_reach_the_child_process(inbox)`, the C1020 log captures PID 469860 at `05:17:39.3141574Z`, begins disposal at `.5199320Z`, rejects the same host at `.5585811Z`, then records its exit at `.5614359Z`. The test kills before disposal (`SessionMessageQueuePtyIntegrationTests.cs:641`). Disposal recaptures session/request manifests (`DirectSessionRunnerClient.cs:417`), and `TestOwnedPtyHost.cs:62` requires Alive before checking for an already captured generation (`:76`). Rejection becomes an accumulated error (`:107`, `:116`, `:163`) even when later exit is observed. Further repetitions will distinguish frequency; the recorded error does not imply a survivor.

## Remaining uncertainties

All native outcomes and root causes are still open. CARD-0680 and CRLF/ConPTY are hypotheses from the brief, not findings.

## Not done, noted

No fix designed or implemented; this is an evidence-only Investigate task.

--- next stage ---
next: investigate
handoff: Complete individual native class reruns and reconstruct any remaining failures from their exact error and source evidence.
artifact: docs/investigations/2026-10-27-native-rerun-terminal-pty-delivery.md
