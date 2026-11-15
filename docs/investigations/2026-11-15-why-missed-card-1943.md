# Why CARD-1943 was missed

The g90 round-2 Review (18072e35, document at review commit
cb2210e934b040a69ed4563a4ffa40f4f1b0d91c) identified these follow-ups as advisories
after the operator-abandon path closed the standing-switch blocker. Its outcome
and costs remain historical; this change does not relabel its evidence.

The HTTP test checked only 422 for whitespace. Service validation was already
correct, but status alone could not detect a changed `errors.reason` key or the
loss of the 1000-character limit. The new dedicated HTTP cases pin the media
type, status, code, exact field and exact message. Both passed before production
changes. A deliberate broken-validator control belongs to Mutation, not Code.

Listing tests seeded one unresolved message and asserted its visibility. That
proved discovery beyond recency and session liveness, but provided no cardinality
or continuation pressure. New independent queue/attention variants seed 1003
rows. At the pre-fix committed source, they returned 1003 and 1005 rows rather
than the allowed maximum of 100. The final fixture inserts in reverse sequence,
checks complete ordered traversal without duplicates, checks the queue body-byte
budget and checks that the attention census still counts undisplayed rows.

The Grok cancellation witness ended at terminal-row/no-resurrection assertions.
It did not inspect the operator's next visible action. `Judge` correctly ignores
Canceled rows, and reconciliation retains their refresh keys, so that generation
cannot become Ready: a late prompt or another compact does not acknowledge the
abandoned read. The new witness initially found no attention item. The smaller
repair derives a current, generation-bound attention item directing the operator
to a fresh start (`POST /api/agents/{id}/start` with `fresh=true`). It sends no
input, offers no resend action, and disappears when the generation changes.
Normal policy refresh is not promised as recovery of the abandoned generation.
`PrepareLaunchAsync` binds a launch's new rules generation; the existing Grok
initialization, receipt, transaction, compaction and barrier classes remain whole
ordinary regression selections. Transcript-confirmed UserPrompt remains the
delivery verdict, and abandoned rows keep a null verdict.

The queue and attention retain their existing DTO envelopes. Queue paging uses
the existing pending-note default 50, maximum 100, sequence continuation and
256 KiB body budget after the first row. Attention applies the same row limits
to its parked/unconfirmed/current-abandoned-rules subset, ordered by session,
sequence and id, with a next offset and truncation marker. Kind counts and the
navigation badge retain the full census. Both client panels expose next/first
page controls. Live queue changes and action responses reset to the first page;
attention offset pages describe current state, so an operator can return to the
first page after concurrent changes.

Two additional client cases failed before the controls existed, each naming its
missing Next button. All prior assertions are retained. No migration, input
delivery path, timeout, classification policy, runner setting or service restart
is part of this repair. The server must load the new code after ordinary Review
and the caller's publication/activation procedure; this Code task restarts none.

Plan: `docs/superpowers/plans/2026-11-15-card-1943-operator-abandon.md`.
All positive controls and variants remain pending SourceLanding Mutation.
