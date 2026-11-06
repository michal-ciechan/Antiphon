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
