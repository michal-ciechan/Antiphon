# CARD-1032 S6 verification — Code owner f0995c2a

Base: 51fb2fc753e24993f2b3a2ff9b39bedb9ee9a02a.
S6 adds fixture-only typed transport. S7 admission/map production and S8 coordinator
recovery remain deferred. Production composition stays absent. TestDesign CP-11/12
supersede the proposed plan numbering. Wildcards bind the actual MTP methods.

Tests first: CP-11 runs on unchanged S5 production. The four methods assert
explicit runtime/transport composition, dispatcher validation and physical receipt
outcomes. Missing S6 type/composition is assertion-red rather than compiler-red.
PC-52/267/268/269/270 remain pending SourceLanding Mutation with all named variants.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-11 | S6 | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-s6-dispatch/` | guarded-dispatch | `/*/*/CompletedCardWorkspaceRemovalTests/(C1017_GuardedOperationIsDistinct*)\|(C1032_Transport*)` | V-2, V-5, R-2, R-3 | all 4 listed; 0 failed/skipped after implementation | 4 | 5 | true |
| CP-12 | S6 | CP-11 | runner-legacy | `/*/*/(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)/(Mirror_*)\|(Publish_*)\|(Remove_*)\|(Workspace_*)` | R-1 | all 28 listed; 0 failed/skipped | 28 | 6 | true |
| CP-31 | S6 | `tests/Antiphon.Tests -> bin-c1032-s6-join/` | transport-join | `/*/*/PhoneHomeCompletedCardTransportTests/C1032_Transport*` | V-2, V-5, R-2, R-3 | both methods; Linux 0 failed/skipped | 2 | 5 | true |

Cost: one isolated runner build and bounded scratch Git/dispatch tests; 11 estimated
minutes including baseline and failure-driven rerun, excluding slot wait.
V-1/3/4 remain carried from earlier slices; V-6 coordinator/endpoints belongs to S7/S8.
Windows native qualification remains pending for the S5 late file identity/content/root
filters named in 2026-10-13-card-1032-s5-verification.md. Restart: none.

### Queue supplement checkpoints

The following row is part of the S6 closed group. It extends S6's delivery claim
through PhoneHomeLiveConnection and the actual runner receive/MutationLock queue,
without the S7 coordinator. Two exact methods: unsupported client refusal and
Linux ready inspection/busy removal/queued exact replay/reconnection. Additional
cost: one isolated server build and two bounded loopback cases (5 minutes).

The S6 added parser/registration vectors live inside C1032_TransportRequiredPayload:
missing root and binding members, null/array/duplicate/Force/version/unknown bodies,
foreign runner/store/full task, empty/garbled/oversize/directory/linked registration,
and a mismatched composition gate. C1032_TransportDispatchReplay holds a real use
lease before typed and legacy removal, releases it, and checks complete replay
against the separately re-read journal and replacement bytes.

C1032_TransportClientUnsupported covers absent capability, advertised-but-unsupported
inspect/remove and a bare completion ACK without a receipt. The latter is protocol
refusal evidence; the real queue method owns physical removal/reconnect proof.
New response DTOs carry existing recipient facts; no server endpoint is marked Complete.

Deletion/removal paths touched by S6:
- CompletedCardWorkspaceRemove dispatch -> capability + strict command parser +
  current registered runner/store -> explicit transport -> existing S5 remover,
  TryRetireRemovalAsync journal-before-effect and held-handle delete gates.
- WorkspaceRemove -> existing RunnerWorkspaceService.RemoveAsync; explicit
  composition now supplies the same optional gate before remove/prune.
- VerificationWorkspaceRemove -> existing workspace service and its optional gate.
  Verification authority and legacy payloads are unchanged by the S6 parser.
- Transport/client/parser/registration reader contain no deletion or pruning calls.
- Test fixture teardown removes only its owned scratch root after connection/runtime
  shutdown; scripted peers close their owned socket. No production Program registration,
  scheduler, route, setting activation, deployment or restart is added.
