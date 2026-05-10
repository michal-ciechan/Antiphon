# E12 — DiffReview + GitHub PR open (Review → Done flow)

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** Review column shows diff; inline comments → channel; Done opens PR.

**Covers:** FR-10, FR-11

---

## Stories

- **E12-S01** `[ ]` `DiffReview.tsx` reusing artifact viewer.
  - Work items:
    - Fetch diff `worktree vs base`; render unified. *TDD:* RTL test `DiffReview_renders_added_removed_lines`.
    - Inline comment input. *TDD:* RTL test `DiffReview_post_comment_calls_api`.
- **E12-S02** `[ ]` Comment → channel injection.
  - Work items:
    - `POST /api/cards/{id}/comments` → `AgentChannelHub.SendAsync(activeSessionId, formatted)`. *TDD:* test `CommentApi_post_routes_to_active_session_stdin`.
- **E12-S03** `[ ]` GitHub PR open.
  - Work items:
    - `POST /api/cards/{id}/pr` pushes branch + opens PR via existing GitHub MCP. *TDD:* integration test `CardPrApi_open_pushes_branch_and_creates_pr` with stub GitHub.
    - PR description templated from card + last attempt summary. *TDD:* test `PrDescription_includes_card_title_and_attempt_summary`.
