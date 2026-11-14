# CARD-1916..1919 inherited-red investigation

Debug task 55b46f7e. Base/current master at start: ca8737e78170c9a441e216408da5fab636c34ca0. Linux x64; time box 120 minutes. Work order: 1919, 1917, 1918, 1916. The original eight reds and timings are preserved in Debug 71aaddba's roster report; this task independently verifies relevant methods before repairs.

Verification scope: exact failing methods for diagnosis/bisection, then the three affected classes whole and the caller's seven-class guard union on the final tree. Builds and runs use build-slot.ps1, native default outputs and UseAppHost=false. Rows run serially, bounded below ten minutes. Per-run load, source, wall duration, slot output and TRX/phase evidence are retained under `.antiphon/evidence/debug-1916-1919/`. Unknown phases remain unknown. No new test timeouts are authorized.

Investigation in progress; this document will record first-bad commits, SQL differences, product/test attribution, omitted review selections, and final results. No green verdict yet.
