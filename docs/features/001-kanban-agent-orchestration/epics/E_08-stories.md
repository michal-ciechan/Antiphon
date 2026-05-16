# E08 — xterm.js terminal pane + Board UI + drag-drop spawn (board-driven mode)

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** board-driven mode end-to-end usable in browser.

**Covers:** FR-01, FR-02, FR-03, FR-04, FR-06, FR-07

---

## Stories

- **E08-S01** `[x]` `BoardPage.tsx` + dnd-kit columns.
  - Work items:
    - Column grid; drag card between columns calls `PATCH /api/cards/{id}` with required concurrency token. Covered by RTL mutation tests, HTTP integration tests, and Playwright drag/reload E2E.
    - Optimistic update + invalidate `['boards', id]` on success; rollback + refetch on error.
- **E08-S02** `[x]` `CardModal.tsx` + `AgentPicker.tsx`.
  - Work items:
    - Modal shows card detail; spawn button posts `/api/cards/{id}/spawn`.
    - Picker pulls sanitized registry definitions from `GET /api/agents`.
- **E08-S03** `[x]` `SessionTerminal.tsx` (xterm.js).
  - Work items:
    - Mount xterm; join `session-{id}`, pull `/buffer` with `lastSequence`, and append matching live deltas without backlog/live reordering.
    - Keystroke → `POST /api/sessions/{id}/input`.
    - Resize → `POST /api/sessions/{id}/resize` on viewport change.
- **E08-S04** `[x]` `SessionTabs.tsx` (session history per card).
  - Work items:
    - Tab strip for active and historical sessions on a card.
    - Forking is FR-08 and intentionally remains outside E08's FR-01/02/03/04/06/07 slice.
