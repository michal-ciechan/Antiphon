# CARD-2076 Mutation candidates

All 57 controls and 87 variants below are pending SourceLanding Mutation. The Code owner is `99dce9ae-60d4-4e46-b66f-2b93da16fe2a`. The witness, exact filter, defect recipe and expected red assertion for each variant are in the [verification design](../plans/2026-11-26-card-2076-proven-subtraction-test-design.md#positive-controls).

The locations below are candidate source anchors. The binding needs the reviewed and landed source, executable edit and restoration digests, and the assigned evidence directory. Bind the controls whose necessary product path and fixture are present. Keep the outstanding controls open. SourceLanding Mutation runs the exact method filter for each baseline, deliberate defect and restoration.

| Control | Variants | Filter | Assertion cases | Candidate anchor or outstanding prerequisite |
|---|---|---|---|---|
| PC-1 | Single | CP-1 | I / `I.authority` | `ReleaseCompilerInputs.Bind` checks the independently supplied digest before deserialization. |
| PC-2 | Single | CP-1 | I / `I.context` | `ReleaseCompilerInputs.Bind` validates the endpoint and context on every call. |
| PC-3 | (a), (b) | CP-1 | I / `I.generated`, `I.reference` | `ReleaseCompilerInputs.Bind` reads generated and reference payloads from the pinned pack. |
| PC-4 | (a), (b) | CP-1 | I / `I.off`, `I.v1` | The index driver's `ProofOptions` defaults off. `ReleaseCompilerInputs.Bind` validates the schema. |
| PC-5 | Single | CP-2 | T / `Q.ambiguous` | `ReleaseImpactTypeResolver.Resolve` retains candidates when the compilation is unavailable. |
| PC-6 | Single | CP-2 | T / `AL.duplicate`, `AL.failed` | `Resolve` adds alias RHS candidates and unions them with existing lexical origins. |
| PC-7 | Single | CP-2 | T / `EX.unbound` | `DeclarationPaths` checks the resolved assembly and the pinned reference project. |
| PC-8 | (a), (b) | CP-2 | T / `P.parts`, `P.unbound` | `DeclarationPaths` retains every declaration. `Bind` identifies an unavailable generated input. |
| PC-9 | (a), (b) | CP-2 | T / `GU.ambiguous`, `GU.owners` | `Resolve` evaluates each owner and unions occurrence results. |
| PC-10 | (a), (b) | `/*/*/ReleaseImpactRelationTests/SdkGeneratedUsingsRetainOnlyPinnedBindings` | T / `IU.ambiguous`, `IU.missing` | `Bind` and `Resolve` use actual SDK-emitted, frozen global usings and preserve candidates on refusal. |
| PC-11 | Single | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `N.ambiguous` | `ResolveSite` binds the nested type and keeps the declaring metadata name. |
| PC-12 | (a), (b) | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `AR.ambiguous`, `AR.argument` | `ResolveSite` uses the original definition and treats argument and constraint occurrences independently. |
| PC-13 | (a), (b), (c) | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `EXT.ambiguous`, `EXT.dynamic`, `EXT.receiver` | `ResolveSite` requires the reduced extension provider and resolved receiver. The unsupported path retains candidates. |
| PC-14 | (a), (b) | CP-2 | T / `FW.missing`, `FW.chain` | `ReadForwarding` checks the pinned Library return TypeRef, Facade export, and Destination definition. `Resolve` retains the facade file independently. |
| PC-15 | Single | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `SH.unsupported` | `ResolveSite` accepts a supported named type and retains candidates for shadowing symbols. |
| PC-16 | (a), (b) | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `CC.missing`, `CC.union` | `Bind` verifies the frozen configuration inventory. `Resolve` unions every reported evaluation. |
| PC-17 | (a), (b) | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `ER.compilation`, `ER.lookup` | `Bind` refuses a compilation with errors. `Resolve` retains the lexical origins without a model. |
| PC-18 | (a), (b) | CP-2 | T / `OT.ambiguous`, `OT.attribute` | `ReadDeclarations` and `Candidates` collect the declaration kinds and attribute spellings. |
| PC-19 | Single | CP-2 | T / `OT.winner` | `ResolveSite` adds winner paths before considering alternative removal. |
| PC-20 | (a), (b) | CP-2 | T / `Q.mixed`, `P.consumer`, `Q.kinds` | `Resolve` unions surviving sites and preserves independent graph kinds. |
| PC-21 | (a), (b) | CP-2 | T / `ER.bounded`, `Q.limit` | `ResolveSite` retains the full candidate bucket at the limit. The bounded downstream widening witness remains outstanding. |
| PC-22 | Single | `/*/*/ReleaseImpactRelationTests/ScopedBindingsPreserveEveryAmbiguousCandidate` | T / `ER.unbounded` | The release relation keeps the Full fallback. The affected-universe witness remains outstanding. |
| PC-23 | (a), (b) | (a) CP-3, (b) `/*/*/RcProjectSelectionTests/EndpointProofsPreserveIndependentReadersAndAddedConsumers` | C / `C.sides` | `RcChunkSelection.Select` visits B and A for index hits and retains the side-qualified project and declaration reasons. |
| PC-24 | (a), (b) | CP-3 | C / `C.legacy`, `C.roster` | `RcTypeProofComparison.DropIndexHit` checks the legacy name. `RosterBound` checks full case membership and recomputes the method-bound content digest. |
| PC-25 | Single | CP-3 | C / `C.owed` | `RcTypeProofComparison.Independent` and `RcChunkSelection.Select` retain reader, hand, closure and graph obligations. The companion `EndpointProofsPreserveIndependentReadersAndAddedConsumers` has separate hand and script cases. |
| PC-26 | (a), (b), (c) | (a) CP-3, (b), (c) CP-4 | C / `C.origin`, `C.debt`, `C.trust` | `RcChunkSelection.Select` keeps `RcChunkReceipt.Match`. The replay covers an earlier executed failure and clean and failing own-test outcomes under the same held prediction. |
| PC-27 | (a), (b) | (a) `/*/*/RcProjectSelectionTests/EndpointProofsPreserveOwnedAndUnownedCaps`, (b) CP-3 | C / `C.cap`, `C.index` | `RcChunkSelection.Select` preserves cap and changed-index rules. |
| PC-28 | Single | CP-3 | C / `C.reseal` | The independent CARD-2069 verifier and its recomputation witness remain outstanding. |
| PC-29 | (a), (b) | CP-3 | C / `C.identity` | `RcAdmission.RequiredAlgorithmPaths` and `RcTypeProofComparison.Read` bind the algorithm and policy identity. |
| PC-30 | Single | CP-3 | C / `C.delta` | `RcChunkSelection.Select` applies `DropIndexHit` before receipt matching. |
| PC-31 | Single | CP-4 | P / `P.cohort` | `RcSelectionReplay.BindCohort` checks the ordered inventory and predecessor, distinct run attempts, and sealed roster contents. |
| PC-32 | Single | CP-4 | P / `P.incident` | `RcSelectionReplay.Replay` calls the shared selector with the pinned pair. |
| PC-33 | Single | CP-4 | P / `P.blind` | `Replay` writes the initial seal before opening current-run outcomes. |
| PC-34 | (a), (b) | CP-4 | P / `P.reveal`, `P.resolved` | `Replay` limits gate disclosure to the owned roster and binds the resolved digest. The proof replay exercises pass, fail, and direct-hit gate cases. |
| PC-35 | Single | CP-4 | P / `P.chain` | `Replay` advances the chained ledger only from selected executions. |
| PC-36 | Single | CP-4 | P / `P.roster` | `Plan` rejects duplicate roster keys. `ReadOutcomes` binds case identity to the expected chunk. |
| PC-37 | (a), (b) | CP-4 | P / `P.nonclean` | `Replay` records every unsafe carry and prioritizes the hard-red verdict. |
| PC-38 | Single | CP-4 | P / `P.opportunity` | `Replay` requires a proof-dependent carry for the diagnostic criterion. |
| PC-39 | (a), (b), (c) | CP-4 | P / `P.records`, `P.unknown`, `P.evidence` | `RecordMisses` maps each failed case or the typed unknown. `RcSelectionMiss.ReadDiff` reads both rename ends. |
| PC-40 | (a), (b) | (a) CP-4, (b) `/*/*/IncrementalSelectionSoundnessTests/ArchivedWrongAliasWinnerUsesTheReplayScorerAndLeavesAnOpenRepair` | P / `P.join`, `P.attribution` | `ReadOutcomes` and `RecordMisses` bind the observed row to its attempt contents. The replay compares the historical wrong alias binding and repaired disposition. Complete per-use-site attribution remains outstanding. |
| PC-41 | (a), (b) | CP-4 | P / `P.atomic`, `P.conflict` | `RcSelectionMiss.Persist` creates the record atomically and verifies occupied IDs. |
| PC-42 | Single | CP-4 | P / `P.revision` | `RcSelectionMiss.AppendDisposition` appends a separate open revision. |
| PC-43 | Single | CP-4 | P / `P.criterion` | `Replay` writes the criterion with `enables=false`. |
| PC-44 | Single | CP-5 | W / `W.shadows` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-45 | Single | CP-5 | W / `W.fresh` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-46 | Single | CP-5 | W / `W.sweep` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-47 | Single | CP-5 | W / `W.resume` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-48 | (a), (b) | CP-5 | W / `W.first`, `W.close` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-49 | Single | CP-5 | W / `W.once` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-50 | (a), (b) | CP-5 | W / `W.ready`, `W.busy` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-51 | (a), (b) | CP-5 | W / `W.crash`, `W.enqueue` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-52 | (a), (b) | CP-5 | W / `W.pinned`, `W.coverage` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-53 | Single | CP-5 | W / `W.taint` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-54 | Single | CP-5 | W / `W.publisher` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-55 | Single | CP-5 | W / `W.repair` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-56 | Single | CP-5 | W / `W.durableMiss` | Pending CARD-2069 and CARD-1836 production integration. |
| PC-57 | Single | CP-5 | W / `W.authority` | Pending CARD-2069 and CARD-1836 production integration. |
