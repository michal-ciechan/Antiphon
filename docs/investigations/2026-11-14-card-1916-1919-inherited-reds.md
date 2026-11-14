# CARD-1916..1919 inherited-red investigation

Debug task 55b46f7e. Base/current master at start: ca8737e78170c9a441e216408da5fab636c34ca0. Linux x64; time box 120 minutes. Work order: 1919, 1917, 1918, 1916. The original eight reds and timings are preserved in Debug 71aaddba's roster report; this task independently verifies relevant methods before repairs.

Verification scope: exact failing methods for diagnosis/bisection, then the three affected classes whole and the caller's seven-class guard union on the final tree. Builds and runs use build-slot.ps1, native default outputs and UseAppHost=false. Rows run serially, bounded below ten minutes. Per-run load, source, wall duration, slot output and TRX/phase evidence are retained under `.antiphon/evidence/debug-1916-1919/`. Unknown phases remain unknown. No new test timeouts are authorized.

Investigation in progress; this document will record first-bad commits, SQL differences, product/test attribution, omitted review selections, and final results. No green verdict yet.

## CARD-1919: stale selector expectation

Test wrong; selector closure correct. `f09330720303fce092d0803a69062bae1ce9c484` added C1514SamplerTickCpuTests; `673ebd43a434aeee5638f2c27b9e3f8beb378498` regenerated the derived index and added that class to the sampler dependencies. The why-missed expected SessionRunner filter was not updated. Preserve the new CPU witness and add it to the exact tuple expectation.

Independent baseline at 33e2322e3a1476d2bc48058999483a620c5df629: 24 executed, 16 passed, the same eight assertion failures, zero skipped. Wall 159.695s, slot wait 75s, held 83s; load 23.81/28.96/25.16 -> 17.63/24.31/24.01. Build: zero errors, 342.981s wall, 121s admission, 220s held; load 34.47/30.50/23.39 -> 24.41/29.16/25.21. TRX and all three complete SQL rosters retained in baseline-methods.
