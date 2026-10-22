# CARD-1483 S3 verification amendment

Code/landing owner: 0ca57dc9-7d5c-459f-8bc2-bf201f51e528. Assigned base
`c5da1d2387c08adf86e301a3874a5cbcfd4d6a98` predates S2; the six landed S2
commits through `8ef0baca812807bf83c7c34578173ac61a36eed6` are cherry-picked
as prerequisites without merging master. This amends S3 of the original plan.

Scope is D-5 only. V-1..V-7 and S1 repair V-1a/V-1b/V-3a remain required.
V-8 has six cases: both card metadata witnesses, CARD-1504 task Properties,
in-target property value, TargetFramework, and imported-props target parameters.
V-9 covers exact pins, formatting, significant attribute/property whitespace,
changed/stale/missing/duplicate/malformed pins, duplicate targets, wrong owner,
non-built and unusually named definitions, untracked pin files and continued
evaluated-item refusal after a valid pin. Whitespace-only properties have both
an acceptance case and a changed-value refusal.
V-10 has three OS condition spellings, each missing/exact/changed/moved.
The complete affected LandGraphEvaluationTests class is 20 results (10 inherited,
six V-8, one V-9, three V-10). No ordinary R-n rows.

Whitespace normalization removes only layout between element children, retaining
leaf text (including all-whitespace property values), attributes and comments.
Pin selectors are Target names, or absolute ordinal element paths for OS elements.
Duplicate or malformed identities refuse. The scanner reads all tracked MSBuild
XML definitions, including those outside the evaluated solution closure.

The brief explicitly defers Unit (known CARD-1466 failures), all whole suites,
V-11..V-16 (S4/S5), and CARD-1504 evaluation-time file probes to S4. The original
CARD-1498 imported-props evaluation witness remains S5 as assigned by S2.
No production land wiring changes until S5; restart: none.

Unlisted execution: build the checkpoint tool through the slot gate, then run
CP-3 on the committed red tests before the fix. Run the final CP-3 once on the
committed implementation/report tip. Generate the manifest only with the audit
script's -Update. Retain final checkpoint outputs for one slot-gated manual
real-tree tool invocation, proving all twelve reviewed pins match and S2 still
approves the repository; then remove those owned outputs. This manual invocation
is bounded to one solution evaluation (120 seconds), not a suite or build.
All deliberate mutants remain SourceLanding Mutation work.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c1483-s3/` | c1483-s3 | `/*/*/LandGraphEvaluationTests/(C1483_S1_*)\|(C1483_S2_*)\|(C1483_S3_*)` | V-1..V-10, V-1a V-1b V-3a | exactly 20 executed, 0 failed/skipped | 20 | 15 |
