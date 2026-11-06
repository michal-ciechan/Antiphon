# CARD-1729 P0-G resolver repair: unbound Mutation candidate

Status: UNBOUND, every control/variant pending SourceLanding Mutation. Code ran
ordinary red-first witnesses and ordinary verification, not deliberate mutants.
Bind to the caller's actual landed source SHA, never this candidate's author tip.
Original Code / landing owner: 742b9c5c-9e25-4fac-910e-799803e461c9.
Repair Code: a342b58a-f264-4674-925e-d60245dc9409.
Original frozen design: commit 8c54e76accc25928e503ac2081b2b5d558bd6663,
`docs/superpowers/testdesign/2026-11-05-card-1729-phase0-testdesign.md`.
Repair design: `docs/superpowers/testdesign/2026-11-06-card-1729-p0g-resolver-repair-testdesign.md`.
Engine anchor: `tests/Antiphon.Tests/Layering/LayeringGuardEngine.cs`.
Test anchor: `tests/Antiphon.Tests/Infrastructure/C1729LayeringGuardTests.cs`.

All methods below belong to `C1729LayeringGuardTests`. Use the exact method-scoped
filter `/*/*/C1729LayeringGuardTests/<Method>` per cycle. Every cycle requires an
intended assertion red, byte restoration, rebuild if code changed, then the same
method green. Zero tests, compilation/fixture failures and timeouts are not red.
Never widen a timeout, loosen an assertion, or replace a method with a class run.

| PC / guard | Deliberate compiling defect | Exact method | Intended assertion / status |
|---|---|---|---|
| PC-1 / G-1 | Remove WorkspaceHooks coverage from the Git rule | Map_covers_every_server_file_exactly_once | unmapped-file; pending |
| PC-2 / G-2 | Put the broad Agents rule before Host Job coverage | Layer_of_pinned_files_follows_first_match | ZombieCensusJob Host pin; pending |
| PC-3 / G-3 | Collect identifiers only from using directives | Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode | violation-count=1; pending |
| PC-4 / G-4 | Ignore allowed edges and flag every cross-layer reference | Allowed_edges_listed_rows_comments_and_strings_do_not_fire | findings=0:allowed-edge; pending |
| PC-5 / G-5 | Match an allowlist row by from-file alone | Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode | Other remains unlisted; pending |
| PC-6 / G-6 | Return an empty stale set | Stale_row_and_unmapped_file_fail_in_strict_mode | stale-row identity; pending |
| PC-7 / G-7 | Default an unmapped path to Application | Stale_row_and_unmapped_file_fail_in_strict_mode | unmapped-file in both modes; pending |
| PC-8 / G-8 | Pick the first declaration for duplicate simple names | Ambiguous_simple_names_resolve_by_using_or_are_listed | using resolves to App; pending |
| PC-9 / G-9 | Scan comment trivia as reference candidates | Allowed_edges_listed_rows_comments_and_strings_do_not_fire | findings=0:comment; pending |
| PC-10 / G-10 | Make warn mode ignore parse/map errors | Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors | parse-error-fails-in-warn-mode; pending |
| PC-11 / G-11 | Delete one later-phase real row | Repository_graph_satisfies_the_committed_allowlist | pending, requires strict P0-G2; binding-missing now |
| PC-12 / G-12 | Add one nonexistent violation triple | Repository_graph_satisfies_the_committed_allowlist | pending, requires strict P0-G2; binding-missing now |
| PC-36 / G-36 | Remove only the new member-label syntax exclusion | Allowed_edges_listed_rows_comments_and_strings_do_not_fire | pending: initializer-member, with-member, named-argument, property-pattern, attribute-member all produce false rows |
| PC-37 / G-37 | Restore the unconditional own/declared-name exclusion before qualification and type-context resolution | Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode | pending: all eight shadowing variants lose their required exact row / incorrectly pass strict |

G-36: member labels cannot become type references. G-37: value/own names cannot
hide type-position or qualified references. PC-37 variants: color-color-fires,
parameter-color-color-fires, local-color-color-fires,
creation-with-shadowing-member-fires, qualified-with-shadowing-member-fires,
qualified-expression-with-shadowing-member-fires,
global-qualified-with-shadowing-member-fires, qualified-with-own-nested-type-fires.
PC-36 assertions have the `findings=0:` prefix. Both witnesses aggregate every
variant before failing, so all thirteen must be inspected in their red evidence.

Original phase-wide PC-13..PC-35 remain pending with their own slices; this
candidate does not discharge or redefine them. Mutation owns missing-control
discovery, including any gaps in the census/setup changes that add no new guard.
Do not execute PC-11/12 by pretending the current warn mode is strict.

Execution budget: use the frozen design's method-scoped native incremental build
profile and collect actual phase costs. This Code task measured native baseline
compile 179.56s and shared repository setup 8.968s; neither is a measured mutation
p95. Two new compiled controls each provisionally need two incremental builds
(about 3 minutes each) plus two startup/setup legs (about 20 seconds each), about
14 minutes combined before admissions. Existing PC-1..PC-12 retain the upstream
design's costs; each source edit gets a separate lease/build and its exact method.
No repeats are required after green. Apply the standing three-normal/two-loaded
limit per unchanged proof selection. SourceLanding runs only local inherited
children, keeps restoration/evidence externally, and never commits/pushes snapshots.
