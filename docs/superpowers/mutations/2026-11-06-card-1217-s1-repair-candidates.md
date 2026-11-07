# CARD-1217 S1 repair controls: binding pending

Repair task `4c959a75`; original Code/landing owner
`36bb3715-4e5e-4038-9a81-5ec024e60a99`. These are unbound candidates for
SourceLanding Mutation, additional to the frozen TestDesign's PC-1..PC-102.
No mutant was applied or executed by Code. Mutation owns binding, deliberate
mutants, red/restore/green and missing-control discovery. These candidates do
not discharge any existing PC or claim mechanical eligibility.

| Variant | Candidate production defect | Exact method filter | Required observation |
|---|---|---|---|
| B-1/TRX | In `StrictTrxReport.Parse`, replace `ReceiptFile.ReadStrictText` with legacy `ReadText`. | `/*/*/C1217ConditionalSkipContractTests/StrictTrxRefusesMalformedEncoding` | Valid Unicode/BOM and missing-file controls hold; invalid byte FF in otherwise valid DebugTrace is wrongly accepted, failing `preserved:B-1:malformed-encoding-refused`. |
| B-1/C | In `Program.Validate`, replace strict receipt decoding with `ReceiptFile.ReadText`. | `/*/*/CheckpointSourceExecutionTests/validators_refuse_malformed_receipt_encoding` | Six valid BOM/Unicode reports qualify; malformed UTF-8 inside a branch string wrongly returns 0, failing `preserved:B-1:C:malformed-utf8`. |
| B-1/P | In `validate-checkpoint-receipt.ps1`, replace `Read-CheckpointStrictText` with `Read-CheckpointText` for the envelope. | `/*/*/CheckpointSourceExecutionTests/validators_refuse_malformed_receipt_encoding` | Same literal corpus; real PowerShell validator wrongly returns 0 for malformed UTF-8, failing `preserved:B-1:P:malformed-utf8`. |
| B-2/C | Set the strict XML reader's `Normalization` to `true`. | `/*/*/C1217ConditionalSkipContractTests/StrictTrxPreservesLiteralLineEndings` | LF positive remains accepted; literal CRLF/CR must survive in both SkipReason and DebugTrace. The mutant loses CR at `preserved:B-2:literal-reason`. |
| B-4/C | Omit `passed` from the non-repeat count-token equality loop. | `/*/*/CheckpointSourceExecutionTests/validators_refuse_changed_count_tokens` | Valid report qualifies in both entry points; only the receipt's passed token changes. PowerShell refuses, C# wrongly returns 0, failing `preserved:B-4:passed:passed=99:C:verdict`. |

Bind exact landed source SHA, compiling before/after bytes, patch digest,
consumed-site observer, literal corpus identity and expected assertion before
execution. Each cycle uses only its exact method, with a nonzero execution
count, its own build-slot lease per build/test command, verified source/build
provenance and byte-for-byte restoration evidence. Build, fixture, zero-count
and timeout failures are not red. Never widen a timeout or loosen an assertion.

The PowerShell conditional TRX projection and new-envelope qualification remain
S2 work: S1 has no such projection to mutate. Both legacy validators retain
their blanket skip refusal. The future PowerShell reason controls in the frozen
102-PC plan remain pending; no surrogate XML probe is credited for them.

## R2-1 repair candidates (task 8711a7d0)

All four variants remain unbound/pending SourceLanding Mutation. Code's committed
old-source red is ordinary regression evidence, not a deliberate mutation cycle.

| Variant | Candidate defect | Exact method filter | Required observation |
|---|---|---|---|
| R2-1/no-validation | Remove the strict validation read before literal projection. | `/*/*/C1217ConditionalSkipContractTests/StrictTrxRefusesIllegalCharacterReferences` | `preserved:R2-1:text:&#0;` fails because an illegal reference is accepted. |
| R2-1/decimal-only | In the validation read only, escape hex numeric references so they are not validated; retain the original text for projection. | `/*/*/C1217ConditionalSkipContractTests/StrictTrxRefusesIllegalCharacterReferences` | Valid decimal controls hold; an illegal hex reference fails its independently expected refusal. |
| R2-1/hex-only | In the validation read only, escape decimal numeric references so they are not validated; retain the original text for projection. | `/*/*/C1217ConditionalSkipContractTests/StrictTrxRefusesIllegalCharacterReferences` | Valid hex controls hold; an illegal decimal reference fails its independently expected refusal. |
| R2-1/normalize-CRLF | Set the projection reader's `Normalization` to `true`. | `/*/*/C1217ConditionalSkipContractTests/StrictTrxPreservesLiteralLineEndings` | LF control holds; literal CRLF/CR reason fails `preserved:B-2:literal-reason`. |

The integration observer is
`/*/*/CheckpointSourceExecutionTests/validators_agree_on_xml_character_references`.
It drives the same 99-reference corpus through strict text/file projection and
both real validator entry points using otherwise valid native-repeat receipts.
It does not discharge S2 conditional eligibility or future PowerShell strict-skip
projection controls. Mutation owns exact landed-source binding, compiling patches,
consumed-site/expected-assertion proof, method-scoped red/restore/green, and discovery.

The ordinary parity witness additionally found an inherited PowerShell repeat
reader acceptance of `&#0;`; it is now guarded by the same corpus and a strict
validation read before the unchanged XML cast. These additional controls remain
unbound/pending Mutation at
`/*/*/CheckpointSourceExecutionTests/validators_agree_on_xml_character_references`:
R2-1/P-no-validation (omit strict read), R2-1/P-decimal-only (escape hex references
only for validation), R2-1/P-hex-only (escape decimal references only for validation).
Expect the corresponding `:P:` refusal assertion to fail with actual exit 0;
ordinary and valid-reference controls must still qualify. S2 PowerShell skip
projection/reason controls remain pending; no deliberate mutant ran in Code.
