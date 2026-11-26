# CARD-2083 unbound Mutation candidate

Original Code and landing owner `99ffacae-49a9-433f-aea8-2f5e6f9db5c6`.
All controls are pending for method-scoped SourceLanding Mutation.
[Checkpoint plan](../plans/2026-11-26-card-2083-custody-release-retry.md).

| ID / variant | Deliberate defect | Exact method filter | Expected red |
| --- | --- | --- | --- |
| PC-1 / drop retry | In RunnerSlotService.ReconcilePendingReleasesAsync, skip the custody intent without calling RetryCustodyReleaseAsync. | `/*/*/VerificationCleanupServiceTests/C2083_Late_host_exit_is_recovered_without_another_cleanup` | No due custody RPC reaches the original runner and the intent cannot settle after host exit. |
| PC-2 / append per tick | In RunnerSlotService.RetryCustodyReleaseAsync, append a fresh pending custody intent on each nonterminal attempt while advancing the original intent's retry marker. | `/*/*/VerificationCleanupServiceTests/C2083_Live_host_keeps_one_intent_until_operator_close` | The persistent host creates duplicate pending rows or attracts extra scheduled custody calls. |
| PC-3 / route through ordinary seal | Dispatch a recovery-only custody request to the general ReadCustodyAsync with seal enabled. Run on a qualified native Linux witness host. | `/*/*/LinuxRunnerCustodyLifecycleTests/C2083_Recovery_only_requires_a_prior_seal_and_waits_for_the_host_to_exit` | The recovery request creates a seal before authorization or acts through the still-live producer. |
