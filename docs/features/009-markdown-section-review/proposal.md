# 009 — Markdown section review: hash-anchored marks, collapse, rendered diff

Status: **as built**. Extends the feature-008 home workspace's rendered view.

## 1. Why

Feature 008 made the rendered markdown view the default reading surface, but review state still
only exists per FILE (`FileReviewState`: Viewed/Reviewed anchored to a content hash). Long
documents — specs, plans, reports — are re-read section by section: on a second pass you only care
about what changed, yet the rendered view re-presents everything, and "Reviewed" is all-or-nothing.

What's needed, in the reader's terms:

- Mark a **section** as reviewed and have it stay marked until that section's content changes.
- Reviewed-and-unchanged sections **collapse out of the way**; changed-since-review sections
  surface loudly and re-expand.
- See what changed **in rendered form** — side-by-side or inline — instead of dropping to the
  Monaco text diff.
- Comment on any of it, exactly like the existing line comments.

## 2. Section model

A section is a heading-delimited slice of the document:

- Split at every ATX heading (`#`–`######`). Content before the first heading is a synthetic
  `__intro` section. A section's **direct content** runs from its heading line to the next heading
  of any level (subsections are separate sections, not part of the parent's content).
- **Key** = slugified heading text + `-<n>` occurrence suffix for duplicates (`## Setup` twice →
  `setup`, `setup-2`). Slug keys survive moves and re-ordering; a rename changes the key, but a
  rename changes the content anyway, so the mark was due to go stale regardless. (The workflow
  artifact feature's `ArtifactSectionReview` chose ordinal paths — the opposite trade-off; here
  reordering is common in living docs, so slugs win.)
- **Hash** = FNV-1a 64-bit of the section's direct content (heading line included), hex. Computed
  client-side — the client is the only party that parses markdown; the server stores opaque
  strings.
- Hierarchy is derived from heading levels: a section's **subtree** is every following section
  until one of the same-or-higher level. Collapse and "mark reviewed" both operate on subtrees —
  collapsing an H2 hides its H3s; reviewing an H2 marks its H3s too, because that is what the
  reader means by the gesture.

## 3. Review marks and staleness

`FileSectionReview` (server): `AgentId + Path + SectionKey` → `ContentHash`, `UpdatedAt`. The same
hash-anchored contract as the file-level `FileReviewState`: the mark records what the reader saw.

- **Fresh**: stored hash == current hash → section shows a quiet ✓ and **auto-collapses** (its
  whole subtree must be reviewed-fresh for the parent to auto-collapse).
- **Stale**: stored hash ≠ current hash → "changed since review" badge, auto-expanded, the ✓
  becomes the re-mark affordance.
- Marks are batched per file: `POST /agents/{id}/review/sections { path, sections: [{key, hash |
  null-to-clear}] }` — one round-trip for "mark subtree" and "mark all".
- Manual collapse/expand always overrides the automatic rule (session-local state, not persisted —
  the persistent fact is the review mark, not the twist of the disclosure triangle).

Staleness is computed client-side by comparing hashes; the server never parses markdown. File-level
marks are unchanged and complementary (the file ✓ in the tree still means "whole file signed off").

## 4. Rendered diff — two modes

The rendered view gains a mode toggle when the file differs from the baseline (`head` content at
the selected baseline, which the viewer already fetches):

- **Clean** — the working content, sectioned as above (the default).
- **Inline** — one flow in work-order: unchanged sections render normally (still collapsible);
  changed sections show block-level old/new (removed blocks red-tinted with strikethrough, added/
  new blocks green-tinted); removed sections appear at their old position, red-tinted whole.
- **Side-by-side** — aligned section rows, base render left, work render right, the same
  block-level tinting inside changed sections; added sections have an empty left cell, removed an
  empty right cell.

Alignment: sections matched by key (LCS over the two key sequences, so duplicate keys and moves
resolve sanely); within a changed section, blocks (blank-line groups) are LCS-aligned and each
block renders as its own markdown fragment with add/remove tinting. Block granularity keeps the
diff fully *rendered* (no raw-HTML injection into markdown) while staying much finer than
whole-section.

Review marks stay available in every mode on the work side — reviewing FROM the diff is the point:
read what changed, mark it, move on.

## 5. Comments

No new comment machinery. A section comment is a **line-anchored review thread** at the section's
heading line in the working content (snippet = the heading), so it rides the existing
`ReviewThread` lifecycle: dispatch to the agent, await, resolve. Each section header shows its
thread count (threads whose line falls inside the section's line range); the existing thread cards
below the viewer are unchanged, and the passage-selection → delegate flow (008) keeps working — the
selection wrapper now encloses the sectioned renderer.

## 6. Decisions & rejected alternatives

- **Client-side hashing/splitting.** The server would need a markdown parser and would have to
  agree byte-for-byte with the client's section boundaries forever. Opaque hashes make the client
  the single authority on structure.
- **Slug keys, not ordinals** — see §2.
- **Block-level rendered diff, not word-level.** Word-level `<ins>/<del>` inside rendered markdown
  requires raw-HTML injection (rehype-raw) or a custom renderer, and breaks on formatting spans.
  Block granularity reads well and never corrupts rendering. Revisit if block tinting proves too
  coarse in practice.
- **Subtree semantics for mark + collapse.** Marking a parent but not its children would make
  auto-collapse useless (the parent could never fold). The gesture "this chapter is reviewed"
  means the chapter.
- **No new thread type for sections.** A parallel section-thread system would fork the dispatch/
  resolve lifecycle for no reader-visible gain.

## 7. Verification

- Unit (client): splitting (keys, occurrence suffixes, levels, intro, line ranges), hashing
  stability, subtree ranges, section diff classification, block diff.
- Component: auto-collapse of reviewed-fresh subtrees, stale badge + re-expand, mark subtree posts
  the right batch, diff mode rendering, comment anchor lines.
- Server (TUnit): mark/fetch/clear round-trip, upsert on re-mark, per-agent+path isolation.
- Live: browser-harness walkthrough — mark, edit the file, watch the section go stale, diff modes.
