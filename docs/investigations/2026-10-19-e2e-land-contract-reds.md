# Windows E2E land contract reds - 2026-10-19

Investigation in progress for task `463014fb`. Product/test source and the isolated
E2E build are pinned to `a0f3c4f3e728655933da1782981a88fb2b7b3a56`.
This preliminary report is committed before the long test runs; only this report
changes after that source revision. No product or test changes are authorized.

The C488 fixture's generated Review report omits `reviewedSourceClean`
(`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.Review.cs:41-51`). The parser
returns null for an absent declaration (`server/Application/Services/ReviewEvidence.cs:83-88`)
and the land gate requires true before checking SHA identity
(`server/Application/Services/LandApproval.cs:76-78`, `:107-110`). The requirement
is explicit in `docs/orchestration-loop.md:2282-2289` (CARD-0835).

Representative execution and separate investigation of completion-scan and C540
dispatch warnings are pending. Raw local evidence is gitignored under
`.antiphon/task-463014fb/` and `.antiphon/acceptance/card-0467/`.
