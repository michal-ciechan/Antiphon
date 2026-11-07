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

## R3-1 repair candidates (task fd3883d6)

Every variant below is unbound/pending SourceLanding Mutation. Ordinary red-first
tests at `668b70c93b9bea9b29abd50e529aa4d34e9136a0` exposed eight real
validator disagreements and four additional XML-reader failures against unchanged
production from `82216aa85fedf8e0c62cc5de9a77f6404f36680e`. They are not PCs.
Mutation owns exact landed-source binding, compiling defects, red/restore/green
and missing-control discovery. Use exact method filters, never whole classes.

| Variant | Candidate defect | Exact method filter | Required observation |
|---|---|---|---|
| R3-1/allow-DTD | Change shared `SecureXml.Settings` to `DtdProcessing.Parse`. | `/*/*/CheckpointSourceExecutionTests/validators_agree_on_xml_character_references` | Accepted ordinary control; C# wrongly qualifies bare DTD and entity declarations, failing `preserved:R3-1:real-validator-parity`. |
| R3-1/diagnostic-bypass | Replace `TrxReport` shared parse with `XDocument.Parse`. | `/*/*/XmlInputSecurityTests/diagnostic_reader_refuses_declarations_and_retains_literal_controls` | Bare DTD wrongly returns 0 instead of 2 at `R3-1:diagnostic:review`. |
| R3-1/strict-bypass | Replace strict shared parse with `XDocument.Parse`. | `/*/*/CheckpointSourceExecutionTests/validators_agree_on_xml_character_references` | Strict text/file refusal fails for a DTD; retain valid references and input bytes. |
| R3-1/repeat-project-bypass | Replace `RepeatRequest` shared parse with `XDocument.Parse`. | `/*/*/XmlInputSecurityTests/project_readers_refuse_declarations_after_an_accepted_control` | `R3-1:repeat-project` fails because DTD no longer throws after the accepted control. |
| R3-1/project-closure-bypass | Replace `ProjectWriteRoots` shared parse with `XDocument.Parse`. | `/*/*/XmlInputSecurityTests/project_readers_refuse_declarations_after_an_accepted_control` | Repeat refusal still holds; `R3-1:closure` fails because the closure accepts the declaration. |
| R3-1/coverage-project-bypass | Replace `CoverageCommand` shared parse with `XDocument.Parse`. | `/*/*/XmlInputSecurityTests/coverage_project_reader_refuses_declarations_after_an_accepted_control` | Accepted ordinary coverage control; `R3-1:coverage-project` wrongly returns 0 instead of 2. |
| R3-1/document-limit | Set the shared `MaxCharactersInDocument` to 0. | `/*/*/XmlInputSecurityTests/diagnostic_reader_bounds_the_document_without_requiring_strict_structure` | Small structurally tolerant diagnostic control holds; over-limit document wrongly qualifies at `R3-1:diagnostic-document-limit`. |
| R3-1/resolver-enabled | Enable an external resolver in the shared settings and separately the literal projection. | `/*/*/CheckpointSourceExecutionTests/validators_agree_on_xml_character_references` | Pending binding/discovery: DTD prohibition dominates resolution, so enabling a resolver alone may be equivalent. Do not credit a survivor or a combined allow-DTD failure as independent resolver detection. Mutation must classify this defense and discover any missing real consumer witness. |

The corpus retains the exact four Review 38ee4b12 declarations, used internal and
external entities, parameter entity, billion-laughs shape, undefined entities,
bare DOCTYPE, valid comment/CDATA/escaped-attribute literals and all five predefined
entities in text/attribute/reason positions. All original 298 numeric/control cases
remain; total parity inputs are 326. The 30-second PowerShell child bound is unchanged.
