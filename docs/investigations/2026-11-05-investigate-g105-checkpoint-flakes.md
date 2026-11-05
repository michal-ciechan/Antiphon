# g105 checkpoint failure investigation

The CARD-1514 investigation is [stored under its card title](2026-11-05-card-1514-investigate-and-speed-up-the-slowest-test-classes-agenttasklandpersistencefailuretests-24-32-min-checkpointmatrix-26-min-publication.md).

Confirmed: receipt-test startup scheduling false failure and a production scheduler readiness/publication race. Both occur on rc and master; 1,040 TUnit cases executed, 1,037 passed, three failed. Investigation only; next stage is Plan.
