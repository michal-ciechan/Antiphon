# CARD-1483 S5 unbound Mutation candidates

Original Code/landing owner bb5773d2-1779-40a3-8afa-db6701554690.
All IDs and variants below are pending SourceLanding Mutation after ordinary
Review and landing. No deliberate mutant was executed by Code.

| ID | Defect / independent variants | Exact LandGraphEvaluationTests method |
|---|---|---|
| PC-S5-1 | Skip evaluator invocation; ignore refused exit 1 | C1483_S5_UpdateRefusesWhenEvaluationRefuses |
| PC-S5-2 | Accept evaluator exit 2; accept missing DLL; publish records before successful evaluation | C1483_S5_EvaluationFailurePreservesRecords |
| PC-S5-3 | Ignore missing/malformed record; ignore digest, SDK or pins SHA mismatch (each separately) | C1483_S5_CheckRequiresEvaluationRecord |
| PC-S5-4 | Omit disk/HEAD source binding for project or pins changes | C1483_S5_EvaluationFailurePreservesRecords |
| PC-S5-5 | Refuse all updates; fail replacement of existing records | C1483_S5_CheckRequiresEvaluationRecord |

Use `/*/*/LandGraphEvaluationTests/<exact method>` for each red/restore/green cycle.
Missing-control discovery includes source changes during evaluation, publication
interruption between the two record replacements, subprocess timeout/drain failure,
SDK discovery parity and executable provenance. The two-file publication is not a
filesystem transaction: it writes the evaluation record first and manifest last.

All original unnumbered controls (pins comparison, differential, exit-2-as-approval),
S1 item/solution-type/link and re-cut attribute/value controls, PC-S2-1a/1b/1c/2a/2b/3a/3b/3c,
PC-S3-1..7 and every listed variant, and PC-S4-1..6 and every listed variant remain
pending exactly as in their source plans/candidate documents. CARD-1528 remains
scheduled follow-up scope; ordinary S5 green does not discharge those gaps.
