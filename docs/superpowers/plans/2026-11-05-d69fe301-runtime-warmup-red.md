# Shared runtime warm-up RED witness (d69fe301)

Pre-repair test-only commit. This records a failing real-EF witness before its fixture repair.
No deliberate mutant is used. The body requires exactly one ContextInitialized diagnostic from
an AppDbContext in the assembly runtime-model phase, outside any TestContext. It then checks
that normal consumers reuse the same model. The pre-repair hook has no such phase or warm-up.

## Performance budget

One owned assembly database startup and one model observation; expected execution under 60 seconds
plus host/container startup. Cold build expected 5-12 minutes on this loaded host, admission
bounded by existing 45-minute broker allowance. The importer enforces a 15-minute row/build floor;
no existing test limit is changed. These are provisional costs, CARD-1514/1404. Retain TRX and trace
with slot admission separate from execution. No whole-assembly test selection.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | red | tests/Antiphon.Tests -> bin-d69fe301-red/ | warm-up-regression-red | /*/*/(TestDbRuntimeWarmupTests*)\|(C0824TransportLimiterTests*)/* | V-1 RED witness | all eight assertions fail before fix | 8 | 2 | true |
