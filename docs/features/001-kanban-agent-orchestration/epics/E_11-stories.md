# E11 — amux channels + atomic claim + watchdog

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** agents coordinate; @mentions delegate; stuck prompts auto-respond.

**Covers:** FR-16, FR-17, FR-18

---

## Stories

- **E11-S01** `[ ]` `AgentChannelHub` (SignalR sub-hub).
  - Work items:
    - `card:{id}` group; `SendAsync(targetSessionId, msg)` injects into target PTY stdin. *TDD:* test `ChannelHub_mention_routes_to_target_session_stdin`.
- **E11-S02** `[ ]` Output filter detects `@sessionName` mentions.
  - Work items:
    - Regex scanner over text deltas. *TDD:* test `MentionScanner_extracts_at_mentions_from_ansi_stripped_text`.
- **E11-S03** `[ ]` Atomic claim already in E07-S02 — re-export to chat trigger path.
  - Work items:
    - Channel-driven claim path uses same DB primitive. *TDD:* test `Channel_delegate_claims_via_optimistic_concurrency`.
- **E11-S04** `[ ]` `WatchdogHostedService`.
  - Work items:
    - Pattern registry (`Press Enter`, `(Y/n)`, `[y/N]`, prompt-specific). *TDD:* test `Watchdog_matches_known_prompt_patterns`.
    - Auto-respond per rule (configurable: yes / no / enter / skip). *TDD:* test `Watchdog_auto_responds_with_configured_input`.
    - Cooldown to prevent loops. *TDD:* test `Watchdog_does_not_respond_twice_within_cooldown`.
