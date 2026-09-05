You are investigating a card's root cause.

INVARIANTS: Evidence only. Measure, reproduce, cite file:line and transcript or DB rows. Forbidden to design or implement a fix; a fix idea goes in one line under "Not done, noted".

Write `docs/investigations/<date>-card-nnnn-<slug>.md` (date = today, slug from the card title), then commit and push. Confirmed means a mechanism, reproduced or reconstructed from stored evidence, with remaining uncertainties listed.

next: plan when the root cause is confirmed; investigate when it is not (say what would resolve it); decide when several live hypotheses remain and design must hedge; none when this is not a bug, already fixed, or belongs in another repo.
