# CARD-1836 S5b pending controls

Code owner 1180038b. Unbound candidate inventory: Mutation must bind the actual
landed SHA, discover any missing controls and execute exact method red/restore/green
cycles. Operator waived Code mutation execution; no PC is passed here.

| Pending PC | Exact RcHistoricalAdmissionTests method | Variants to bind |
|---|---|---|
| PC-V6-manifest | FullProvenanceAdmitsAndReverifies | trust stored input records; trust stored combined digest; bypass current Git reconstruction; accept changed case manifest |
| PC-V6-inventory | FullProvenanceAdmitsAndReverifies | skip S5a verification; ignore envelope seal; ignore actual receipt byte digest; skip final bundle rehash |
| PC-V6-provenance | FullProvenanceAdmitsAndReverifies | accept mismatched build output; ignore current contract; ignore stage receipts; skip source/build/discovery/launch join |
| PC-V6-terminal-roster | IncompleteChunkAndCaseAliasNeverCarry | Failed; Timeout; Aborted; partial; missing case; duplicate case; mixed suite; ignore nonzero result count |
| PC-V6-origin-history | ChangedEnvelopeHistoryAndOriginChainRefuse | cycle; missing origin; failed original; unknown cumulative history; wrong source; case alias; depth overflow |
| PC-V6-anchor | EveryIngredientMustMatch | missing; future; exactly seven days old; renew anchor through carry |
| PC-V6-algorithm-coverage | CollisionAndAlgorithmCoverageForceFull | each uncovered required file; each per-file byte change; trust complete minimum list despite incomplete graph; admit resolved S2 collision |
| PC-V6-inert | ImportIsInertAndWritesUnprovenShard | write passed shard; grant gate credit; omit Full fallback |

Pure predicates expose diagnostic eligibility, with gateCredit=false. Read-RcAdmission
adds independently verified bytes/Git joins and the non-overridable incomplete graph
gate. The positive fixture must exercise eligibility while actual admitted remains
false. Missing test/control discovery belongs to Mutation, including TRX method
presence, source receipt corruption and the 64-hop bound. Native Windows evidence
remains pending with the orchestrator; no Linux result discharges it.
