# CARD-1591 S1 round 2 repairs

Original Code owner 73c027ae-8ab4-4e6d-a145-50d426f513ea; repair task 2ede815a.
Round 2 review diff starts f6f380c08e3468a5b98415e14f72380377fc6f6e.
Original S1 V-1..V-5 and R-1 remain required. S2 V-6..V-11 and R-2 are deferred
outside this slice. Whole Unit is prohibited here and owned by CARD-1720.

Invariant: historical withholds cannot page while a recipient works or has a durable
hold, or after another hold incident for that inbound. An incident and its stamp
commit together under nonblocking queue/inbound row locks, fencing queue disposition.
No scan path writes session input or waits for the session semaphore. A pass caps
its inspected queue heads and incidents at 64, and SQL at a short statement/lock deadline, using a partial aged
withhold index. Non-shutdown cancellation logs a failed pass and retries.

The original no-index prescription is superseded for B4. Add only the aged pending
channel withhold index. Existing nullable stamp migration remains additive.

Budget: prior Review measured cold build 412s; CP-1 46s (6 cases), CP-3 230s,
CP-4 155s, CP-5 146s. Expanded CP-1 estimate provisional 4m. One red witness
selection and one green complete selection; rerun only failing rows after repairs.
No full assembly selection. Maximum execution per run 28m, row/build 12m;
slot allowance 45m, hard wall 73m. Shared fixture clones one migrated template.
Keep host/startup/body/teardown and per-test durations; finer phases unknown if absent.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1591-repair/` | withheld-incident | `/*/*/(ChannelInputWithheldIncidentTests*)\|(DialogDeliveryAdmissionTests*)/*` | V-1, V-2, V-3, V-4, V-5, R-1, B2-B6 | every case, 0 failed/skipped at fixed source | 24 | 11 | true | `TEMP=/tmp` |
| CP-3 | S1 | CP-1 | channel-regressions | `/*/*/(ChannelInbound*)\|(ChannelBridge*)/*` | inbound acceptance, wake recovery, queue receipt, busy recipients | every case, 0 failed/skipped | 88 | 6 | true | `TEMP=/tmp` |
| CP-4 | S1 | CP-1 | migration-model | `/*/*/(AppDbContextModelTests*)\|(TestSchemaSnapshotTests*)\|(TestMigrationFingerprintTests*)\|(ChannelOutboundMigrationTests*)/*` | model/snapshot parity, schema clone, migration contracts | every case, 0 failed/skipped | 10 | 3 | true | `TEMP=/tmp` |
| CP-5 | S1 | CP-1 | classification-guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)/*` | B1, registry integrity | every case, 0 failed/skipped | 47 | 3 | true | `TEMP=/tmp` |

All M1..M5 and review-proposed controls remain pending for method-scoped
SourceLanding Mutation. Ordinary pre-fix witnesses are not Mutation certification.

Pre-fix native witnesses at ef3dea0c2252dd2307a0cacd5317946981b4bff7: 70 executed,
46 passed, 24 failed. Nine registry failures; 13 B2/B3/B4/B6 behavior failures;
one invalid Attention reason-text premise and one frozen-clock/queue-reentry delivered
fixture timeout. The latter two are witness repairs, not product-defect proof.
The first cold checkpoint hit its unchanged 12m build deadline with zero tests.
A separately leased native build passed in 356s and its test run took 231s.
