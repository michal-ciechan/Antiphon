# E10 — External tracker adapters (Linear, GitHub Issues, Jira)

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** Linear / GitHub Issues / Jira poll → cards in board.

**Covers:** FR-12, NFR-07

---

## Stories

- **E10-S01** `[ ]` `IIssueTracker` abstraction.
  - Work items:
    - `FetchCandidates`, `FetchByStates`, `FetchByIds`. *TDD:* contract test `IIssueTracker_contract` runs against fake tracker.
- **E10-S02** `[ ]` `LinearTracker` (GraphQL).
  - Work items:
    - GraphQL query for active states + project filter. *TDD:* test `LinearTracker_fetch_candidates_normalises_response` against recorded fixture.
    - Blocker derivation from inverse `blocks`. *TDD:* test `LinearTracker_blockers_derived_from_inverse_blocks`.
- **E10-S03** `[ ]` `GitHubIssuesTracker`.
  - Work items:
    - REST fetch; map labels lowercase; priority from label convention. *TDD:* test `GitHubIssuesTracker_normalises_priority_from_label_convention`.
- **E10-S04** `[ ]` `JiraTracker` (reuse existing Jira MCP integration as inspiration).
  - Work items:
    - JQL fetch; map status. *TDD:* test `JiraTracker_jql_filters_to_active_states`.
- **E10-S05** `[ ]` Single-fetch-per-tick + in-tick cache (NFR-07).
  - Work items:
    - `TrackerCache` scoped to tick. *TDD:* test `TrackerCache_dedupes_same_id_lookup_within_tick`.
