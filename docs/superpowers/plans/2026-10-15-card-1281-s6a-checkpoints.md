# CARD-1281 S6a routing and admission

Commissioned subset of the parent cheap-slice manifest for Code e7ca6378.
The brief waives the whole Unit lane (CARD-1287), excludes B-3 and forbids mapping
before B-1 authorization/transaction/import pass. Component hosts therefore prove
the authorization gate before real Program routing is added. S6b retains closure.
Rows below bind the current uncompiled red-first increment; subsequent S6a rows
must be added before their runs. No pending row is credited as passed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-17 | S6a | `tests/Antiphon.Tests -> bin-c1281-s6a-http-red2/` | caller-authorization | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1Authorization*)\|(C1281_B2Authorization*)` | V-27,V-30,R-7 | 2 executions, zero failed/skipped | 2 | 4 |
| CP-18 | S6a | CP-17 | correction | `/*/*/PipelineSpeedIntegrationTests/(C1281_B1CorrectionTransaction*)\|(C1281_B1ReceiptImport*)` | V-28,V-29,R-6 | 2 executions, zero failed/skipped | 2 | 6 |
| CP-26 | S6a | CP-17 | admission | `/*/*/PipelineSpeedIntegrationTests/C1281_AdmissionHolds*` | V-9,V-11,R-7 | 1 execution, zero failed/skipped | 1 | 3 |
| CP-19 | S6a | CP-17 | caller-http | `/*/*/PipelineSpeedIntegrationTests/(C1281_B2ObservationReadback*)\|(C1281_B2HttpChildRecovery*)` | V-31,V-32,R-7 | 2 executions, zero failed/skipped | 2 | 6 |
| CP-21 | S6a | CP-17 | wired-roundtrip | `/*/*/PipelineSpeedIntegrationTests/C1281_WiredRoundtrip*` | V-36,R-7 | 1 execution, zero failed/skipped; B-3 excluded | 1 | 3 |
| CP-22 | S6a | CP-17 | wired-recovery | `/*/*/PipelineSpeedIntegrationTests/C1281_WiredDeliveryRecovery*` | V-37,R-7 | 9 executions, zero failed/skipped | 9 | 8 |

These are bounded class/method selections; no full assembly or Unit run is authorized.
The additional existing-guard class row required by the brief will be bound after
the final changed-identifier census. All parent PCs and variants remain pending for
SourceLanding Mutation, including missing-control discovery; no mutants run in Code.
