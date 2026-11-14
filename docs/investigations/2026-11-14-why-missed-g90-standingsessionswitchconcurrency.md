# Why missed: StandingSessionSwitchConcurrencyTests

`Reservation_and_real_queue_flush_serialize_in_both_orders(True)` failed at
clean master `8cec1e74e75819ef20756c228add987ce4ae4170`: 24 executed, 23 passed,
1 failed, 0 skipped. It expected `standing_resume_delivery_pending`, but
history selection succeeded. Its contention witness passed first: the queue
semaphore still serialized delivery and selection. CARD-1892 changed the
IOException outcome from Pending to Sent/null-verdict, so the Pending-only
reservation guard no longer saw the unresolved source attempt.

The caller explicitly chose a product repair rather than replacing the
in-flight transport error with a definitive refusal. Product commit
`dcb068f89c33caebaa543f0413fd254e501b1261` widens the guard. Test commit
`91fbecb735a3eb2c61ddd8f746f720068edd80ac` adds confirmed-source,
other-session and same-conversation-resume controls. The shared note records
the full caller/recovery and stuck-row audit.

The recorded CARD-1892 Code/Review roster covered queue interruption and
launch/resume recovery, but not this consumer's reservation after a real
queue flush. Its successful Pending-only recheck sibling could not detect a
Sent/null-verdict escape. The old Merge uncertainty row watched only the
exception file and omitted this class; neither queue-service catch edits nor
AgentControlService guard edits matched it. Original Merge execution receipts
were not supplied. The syntax-derived AgentControlService index was capped,
which is insufficient authority to omit this explicit invariant.

The cheapest pre-land selection is the shared
[`input-delivery-uncertainty` row](2026-11-14-why-missed-g90-three-classes.md#cheapest-pre-land-selection)
watching both service paths and explicitly naming this whole class.
Local evidence: `.antiphon/evidence/g90/red-switch/`.
