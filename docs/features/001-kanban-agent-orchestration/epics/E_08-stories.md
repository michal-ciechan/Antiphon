# E08 — xterm.js terminal pane + Board UI + drag-drop spawn (board-driven mode)

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** board-driven mode end-to-end usable in browser.

**Covers:** FR-01, FR-02, FR-03, FR-04, FR-06, FR-07

---

## Stories

- **E08-S01** `[ ]` `BoardPage.tsx` + dnd-kit columns.
  - Work items:
    - Column grid; drag card between columns calls `PATCH /api/cards/{id}`. *TDD:* RTL test `BoardPage_drag_card_between_columns_invokes_patch`.
    - Optimistic update + invalidate `['boards', id]` on success. *TDD:* RTL test `BoardPage_optimistic_move_reverts_on_api_error`.
- **E08-S02** `[ ]` `CardModal.tsx` + `AgentPicker.tsx`.
  - Work items:
    - Modal shows card detail; spawn button posts `/api/cards/{id}/spawn`. *TDD:* RTL test `CardModal_spawn_calls_api_with_selected_agent`.
    - Picker pulls registry from `GET /api/agents`. *TDD:* RTL test `AgentPicker_renders_options_from_registry`.
- **E08-S03** `[ ]` `SessionTerminal.tsx` (xterm.js).
  - Work items:
    - Mount xterm; pull `/buffer` for backlog; subscribe SignalR `AgentTextDelta` filtered by sessionId. *TDD:* RTL test `SessionTerminal_renders_buffer_then_appends_live_deltas` with mocked hub.
    - Keystroke → `POST /api/sessions/{id}/input`. *TDD:* RTL test `SessionTerminal_sends_keystrokes_to_input_endpoint`.
    - Resize → `POST /api/sessions/{id}/resize` on viewport change. *TDD:* RTL test `SessionTerminal_resize_posts_new_dimensions`.
- **E08-S04** `[ ]` `SessionTabs.tsx` (multi-session per card).
  - Work items:
    - Tab strip; fork button creates new session. *TDD:* RTL test `SessionTabs_fork_button_calls_fork_endpoint_and_adds_tab`.
