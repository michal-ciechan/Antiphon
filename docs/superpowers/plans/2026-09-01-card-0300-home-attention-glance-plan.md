# CARD-0300 — Home is a glance; attention detail lives on `/attention`

**Date:** 2026-09-01 (Plan pass, task c1aaf3ec — design only; no code changed)
**Card:** CARD-0300 "Home Screen: compact stats summary (max 1–3 lines, phone-friendly); move detail to a dedicated screen"
**Diagnosis:** done on the card. This pass verified Home (desktop badge + mobile `NeedsYouBand` full rows), `AttentionKind` 0–23, `groupOf` / `ATTENTION_GROUPS`, `AttentionPanel` on `/orchestrator?tab=attention`, and `/attention/summary` (`open` + `decisions` only).

**Sources (verified this pass):** CARD-0300, `HomePage.tsx` / `MobileHomePage.tsx`, `attentionVisuals.ts` (`groupOf`, `targetOf`, `ATTENTION_VISUALS`), `AttentionPanel.tsx`, `AttentionDtos.cs` (kinds 0–23, last `UnmarkedWaiting`), `client/src/api/attention.ts` (client union in lockstep), `App.tsx` routes, `HomePage.test.tsx` / `MobileHomePage.test.tsx`, mobile spec §D3 (band 1 currently inlines `BlockedReplyRow`).

---

## Decision

Reuse the **existing four groups**. Do not invent a second taxonomy. Home shows **three counts** (omit settled-failure history), one line of badges, linking to a **new `/attention` route** that renders the existing `AttentionPanel` unchanged.

| Home label | `groupOf` | Severity | What the operator does |
|---|---|---|---|
| **Blocked** | `now` | Critical | Answer / decide — nothing else moves it |
| **Broken** | `broken` | Error | Pick up a failure (retry, kill, open session) |
| **Review** | `suspect` | Warning | Look, often leave it |

`RecentFailure` stays in `failures` and is **hidden on Home**, same as today's `NeedsAttentionBadge` and the Orchestrator tab badge.

That is the card's "Critical incidents / Blockers / Needs human review" mapped onto ranks the server already computes. New `AttentionKind` values (append-only) fall into a bucket automatically via severity; `groupOf`'s one special case (`RecentFailure`) stays.

**Do not rebuild the list.** `AttentionPanel`, `BlockedReplyRow`, `ATTENTION_VISUALS`, `targetOf` ship as-is on `/attention`. Orchestrator `?tab=attention` keeps embedding the same panel (bookmark compatibility). Home and `DecisionsBadge` links that today go to `/orchestrator?tab=attention` go to `/attention`.

**Mobile cost, accepted:** band 1 today inlines reply (CARD-0033). Compact Home adds **one tap** (glance → `/attention`) before `BlockedReplyRow`. The dedicated page still has inline reply; we do not put a reply box on the glance.

**Out of this card:** desktop files tree / "To read (N)" / agent rail. The problem paragraph names them; the Wanted section is attention stats + moving the expandable list.

No server DTO change. Counts are `groupOf` over `GET /api/attention` items — Home already fetches that feed.

---

## Kind → Home bucket (enum end = 23)

`groupOf` is severity except `RecentFailure`. Live kinds:

| Kind | Typical severity | Home |
|---|---|---|
| `BlockedQuestion` | Critical | Blocked |
| `CardNeedsDecision` | Critical | Blocked |
| `InboundUnconsumed` | Critical | Blocked |
| `ParkedMessage` | Error | Broken |
| `DeadSession` | Error | Broken |
| `NeverStarted` | Error | Broken |
| `UncorrelatedReport` | Error | Broken |
| `SessionDisagreement` | Error | Broken |
| `RecentCriticalIncident` | Error | Broken |
| `BriefUndelivered` | Error/Warning | Broken or Review |
| `FailureUnacknowledged` | Error | Broken |
| `PastExpectedIdle` | Warning | Review |
| `ChecksSpent` | Warning | Review |
| `Overdue` | Warning | Review |
| `ProgressStalled` | Warning | Review |
| `CardStalled` | Warning | Review |
| `OrchestratorInvestigation` | Warning | Review |
| `CallerNoteUndelivered` | Warning | Review |
| `CardlessDetailsNoPrompt` | Warning | Review |
| `QueuedInputStuck` | Warning | Review |
| `AgentOutlivedTask` | Warning | Review |
| `ReportUnsettled` | Warning | Review |
| `UnmarkedWaiting` | Warning | Review |
| `RecentFailure` | Warning | **hidden** |

Pin: every `AttentionKind` in the client `Record` maps to exactly one of `blocked | broken | review | hidden`. A new kind that only has `ATTENTION_VISUALS` and not the bucket map fails the same test that already enforces visuals totality.

---

## Slices

### S1 — Bucket helper (pure)

`client/src/features/attention/attentionVisuals.ts`:

```ts
export type HomeAttentionBucket = 'blocked' | 'broken' | 'review' | 'hidden'

export function homeBucketOf(item: AttentionItemDto): HomeAttentionBucket {
  const g = groupOf(item)
  if (g === 'failures') return 'hidden'
  if (g === 'now') return 'blocked'
  if (g === 'broken') return 'broken'
  return 'review'
}

export function homeBucketCounts(items: AttentionItemDto[]): {
  blocked: number; broken: number; review: number
}
```

`attentionVisuals.test.ts`: totality over `ATTENTION_VISUALS` keys; the table above as examples; `RecentFailure` hidden.

No server copy of this map.

### S2 — `AttentionGlance` on Home (desktop + mobile)

New `client/src/features/attention/AttentionGlance.tsx`:

- `useAttention()`. Quiet (all three 0, or pending empty): **render nothing** (desktop) / keep `CalmCard` (mobile).
- Else one `Group` of up to three `Badge`s, `wrap`, `size="sm"`, labels `Blocked N` / `Broken N` / `Review N`. Omit a badge at 0 so a single blocker is one chip, not `Blocked 1 · Broken 0 · Review 0`.
- The whole strip is `<Anchor component={Link} to="/attention">`. `data-testid="attention-glance"`.
- Max visual height: one Mantine line on desktop; wrap to two on a 320px-wide phone; never a third row of headlines.

**Desktop:** replace `NeedsAttentionBadge` in `HomePage.tsx` with `AttentionGlance`.

**Mobile:** replace `NeedsYouBand` (the mapped rows + inline `BlockedReplyRow`) with `AttentionGlance` in band 1. When counts are 0, `CalmCard` unchanged. **In motion** and **While you were away** stay. Spec §D3 band order stays: glance, then in motion, then away.

`MobileHomePage.test.tsx`: a BlockedQuestion no longer shows the headline/evidence on `#mobile-home`; it shows `Blocked 1` linking to `/attention`. Calm state still has no glance.

`HomePage.test.tsx`: `Needs attention (2)` becomes glance badges; href `/attention` not `/orchestrator?tab=attention`. Quiet fleet still has no attention chrome.

### S3 — Route `/attention`

- `AttentionPage.tsx`: page chrome title "Needs attention" + `<AttentionPanel />`. No new list implementation.
- `App.tsx`: `<Route path="attention" …>` inside `Layout`.
- `App.test.tsx`: add `{ path: '/attention', content: … }` (mock or real panel with empty attention MSW).
- Orchestrator tab **keeps** `<AttentionPanel />`. Do not delete the tab.
- `DecisionsBadge` can keep `/orchestrator?tab=decisions` (decisions are a different surface). Optional later: also count inside Blocked.

### S4 — Browser check (execute)

User rule: UI work is not done on tests alone.

- Desktop ≥1280: Home header shows at most the three badges; click → `/attention` with the four groups and a BlockedQuestion still offering `BlockedReplyRow`.
- Mobile 375×667: Home band 1 is the glance (or CalmCard); no stacked incident papers; In motion still under it. Tap glance → `/attention` usable (reply box, no horizontal overflow).
- Quiet fixture: Home has no attention strip.

---

## What this card does not do

- Redesigning the files pane, To-read badge, or agent rail.
- New AttentionKind values or server severity changes.
- Extending `GET /api/attention/summary` with bucket counts (client already has the items).
- Removing the Orchestrator attention tab.
- Putting `BlockedReplyRow` on Home.
- Adding Attention to the app nav list (Home is the entry).

---

## Test matrix

| Layer | Test |
|---|---|
| Unit | `homeBucketOf` totality + RecentFailure hidden |
| Home | Desktop glance counts, link `/attention`, quiet = absent |
| Mobile | No inline Needs-you rows; glance or CalmCard; in-motion/away remain |
| App | `/attention` resolves |
| Existing | `AttentionPanel.test.tsx` unchanged (same component) |

```powershell
pwsh -File scripts/test-client.ps1 attentionVisuals
pwsh -File scripts/test-client.ps1 HomePage
pwsh -File scripts/test-client.ps1 MobileHomePage
pwsh -File scripts/test-client.ps1 App.test
pwsh -File scripts/test-client.ps1 AttentionPanel
```

(After CARD-0307, those filters actually scope. If that card is unshipped, expect a full vitest run.)

---

## Sequencing and risks

**Order: S1 helper + tests, S3 route (so glance has a target), S2 Home swap, S4 browser.**

| Risk | Disposition |
|---|---|
| Extra tap to reply on phone | Accepted; reply stays one screen away, still inline on `/attention` |
| Three badges wrap to a wall of chrome | Omit zero buckets; `size="sm"`; wrap; no headlines |
| `BriefUndelivered` severity varies | Bucket follows live `item.severity` via `groupOf`, not a kind table |
| New kind 24+ | Totality test fails until `ATTENTION_VISUALS` + implied `groupOf` |
| Orchestrator tab vs `/attention` drift | Same component instance type; no fork |
| CARD-0307 filter still no-ops | Execute uses the wrapper; if full suite runs, that is CARD-0307, not this |

---

## Execution notes

- Do not "also tidy" the files tree in this PR.
- Do not summarise by inventing labels per kind on Home (`BlockedQuestion` must not appear as its own chip).
- Verify both viewports before calling the card done.
