# CARD-1032 S6 unbound Mutation candidate

Original Code/landing owner: f0995c2a. This is a candidate, not a bound pack.
SourceLanding Mutation owns deliberate mutants, assertion-red/restore/green,
per-field/variant binding and missing-control discovery after ordinary Review and
caller landing. No Mutation run was commissioned or performed here.
All prior plan PC IDs and named variants remain pending, including PC-52,
PC-267..270, S5 PC-578..587 and moved S7 PC-60 positive/PC-581.

| PC | Candidate defect | Exact detecting filter | Expected red / variants | Status |
|---|---|---|---|---|
| PC-588 | Omit capability guard or use legacy fallback | `/*/*/PhoneHomeCompletedCardTransportTests/C1032_TransportClientUnsupported*` | no-capability and advertised-but-unsupported inspect/remove; guarded request count and zero legacy removal | pending |
| PC-589 | Bypass dispatcher MutationLock or reply before persisted complete receipt | `/*/*/PhoneHomeCompletedCardTransportTests/C1032_TransportQueueRecipientReplay*` | second request completes while holder remains; fresh journal receipt mismatch; replay/reconnect bytes and replacement preservation | pending; bind queue and durability variants separately |
| PC-590 | Accept a Complete ACK without both receipt components | `/*/*/PhoneHomeCompletedCardTransportTests/C1032_TransportClientUnsupported*` | recipient_receipt_unreadable or false Complete missing | pending; missing receipt/directory/registration variants |
| PC-591 | Omit registered recipient runner/store check | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload*` | runner_store_mismatch missing or downstream Git reached | pending; runner and store independently |
| PC-592 | Read invalid/oversize/linked registration as valid | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload*` | recipient_store_unreadable missing or target/owned bytes changed | pending; empty, garbled, zero Guid, oversized, directory, Linux symlink; FIFO missing-control discovery remains Mutation-owned |
| PC-593 | Compose with a different workspace gate/journal | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportRequiredPayload*` | ArgumentException missing | pending; workspace gate and journal pair |
| PC-594 | Leave legacy workspace removal outside installed runtime gate | `/*/*/CompletedCardWorkspaceRemovalTests/C1032_TransportDispatchReplay*` | legacy removal succeeds under held use lease | pending |

PC-269 additionally binds every missing inspect binding/root member and duplicate,
Force, null/array, unknown and version variant. PC-268 binds caller-supplied local
capability suppression as well as registration/query advertisement. Existing
PC-270 binds new transport RequestIds and recipient reconstruction/reconnection.
No green ordinary run discharges any control. Windows handle/native filters are
carried in the S5 verification artifact and require desktop Windows Final Review.
