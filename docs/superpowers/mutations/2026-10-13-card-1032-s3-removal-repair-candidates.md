# CARD-1032 S3 repair: unbound Mutation candidates

Source Code task / landing owner: `f6e00270-2149-45b2-b065-5c0ef10d1f17`.
Prior Code task: `c533d6c6-3a11-4b45-b851-f27c39ed0b92`.

These are unbound candidates for caller-commissioned SourceLanding Mutation
after ordinary Final Review and landing. No deliberate mutant has run.
The exact wildcard method filters and red witnesses are in the round-2 repair
PC table in `docs/superpowers/plans/2026-10-12-card-1032-test-design.md`.
Mutation owns binding, missing-control discovery and all red/restore/green work.

| PC | Pending variants / production target |
|---|---|
| PC-551 | Present RemoveAsync: omit outer admission |
| PC-552 | Absent RemoveAsync: move admission after PruneRepositoriesAsync |
| PC-553 | Lease disposal: omit release; require actual post-use removal |
| PC-554 | Removal/creation ordering: release before Git; also creation-first removal refusal |
| PC-555 | Unconfigured present and absent removal compatibility |
| PC-556 | Removal lease release: exception and cancellation separately |
| PC-557 | Retained owner: same-path removal and absent-sibling repository prune separately |
| PC-558 | RemoveVerificationAsync: present and absent branches separately |
| PC-559 | CreateVerificationAsync: omit admission before Git |
| PC-560 | AcquireRemovalAsync holds: retiring intent, torn journal, incomplete census separately |

All original 292 PCs and these ten new IDs/each listed variant are pending.
Use only method-scoped cycles on a bound pack. Never loosen assertions or
widen timeouts. Ordinary pre-fix CP-25 red demonstrates the reported defect;
it is not a SourceLanding Mutation cycle or a claim that any PC passed.
