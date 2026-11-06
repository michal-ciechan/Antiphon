# Slot saved-assignment refusal controls — pending SourceLanding Mutation

Original Code/landing owner: c346b7a6-1aae-4f0d-a3b9-7dfe3cbccfa1.
Repair: 1098f5e9. Review: da7fff71 round 1; next ordinary Review is round 2,
diff from a31cb8ee3bb38deffde89ac176790b466e27f63e.
Plan: docs/superpowers/plans/2026-11-06-c346b7a6-runner-slot-compose-plan.md.

These are **unbound candidates**, not executed PCs. Mutation owns deliberate mutations,
method-scoped binding, intended assertion-red / restore / fresh-build-green and discovery.
Existing PC-1..PC-4 remain pending with their plan variants: passthrough deletion,
default true, preservation omission (standing/temp), invalid/duplicate acceptance.
Do not count the natural pre-fix red witnesses as a deliberate mutation cycle.

The complete-production-phase Bash outcomes live in
`scripts/fixtures/c346-workspace-slot-compose.mjs`. A single outcome can be selected as:
`node scripts/fixtures/c346-workspace-slot-compose.mjs --refusal-case=empty --refusal-action=case_deploy_parent`.
Run the temp variant with `--refusal-action=case_deploy_temp_runner`; wrapper variants
are `compose_host` and `compose_temp`. Unknown selectors and zero cases refuse.
Use a build-slot wrapper for each command. The script checks exact exit 2, exact
RunnerWorkspaceSlotsSettingInvalid, empty stderr, and unchanged complete fixture tree.
There is no docker daemon or live file access. A side-effect boundary records a marker
and exits 91, which cannot satisfy the refusal assertion. Both env files are snapshotted.
No TUnit exact-method binding for these script outcomes is claimed; Mutation must
resolve any required binding/missing-control finding before claiming PC clean.

| Pending PC | Deliberate defect in runner_workspace_slots_enabled | Exact case candidates / variants |
|---|---|---|
| PC-5 | Coerce a present empty/blank saved value to false | empty, space-only, tab-only |
| PC-6 | Normalize malformed key/equal whitespace into accepted syntax, or ignore it | leading-space-key, leading-tab-key, space-before-equals, tab-before-equals |
| PC-7 | Trim saved value or strip CR before validating | space-after-equals, tab-after-equals, trailing-space, trailing-tab, crlf |
| PC-8 | Accept case-insensitive keys or values | lowercase-key, mixed-case-key, uppercase-value, mixed-case-value |
| PC-9 | Accept export syntax, unquote values or strip active inline comments | export-prefix, quoted-value, inline-comment, comment-without-space |
| PC-10 | Ignore an indented/malformed/comment-looking second assignment after a valid one | indented-duplicate, tabbed-duplicate, spaced-duplicate, comment-looking-duplicate, case-duplicate |
| PC-11 | Remove duplicate counting or stop after the first exact assignment | duplicate, same-value-duplicate, later-duplicate |

Every listed case has standing/temp phase and standing/temp wrapper variants. This
adds 108 malformed outcomes to the original 12 invalid/duplicate/shell-shaped outcomes.
Keep the original 16 Compose matrix cases, missing-file default, inactive-comment
acceptance and byte-equivalent default render green. Empty ambient interpolation
remains false; an empty saved assignment is now refused, so that distinction is explicit.

Mutation must not widen timeouts or assertions. All PCs above are pending, including
variants already naturally red before repair. Restart: none for this Code task;
operational activation later recreates the relevant runner container via its named phase.
