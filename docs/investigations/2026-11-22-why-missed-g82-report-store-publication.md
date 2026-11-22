# Why missed: a staged report could replace an open canonical report

Windows Full run `c9816b02`, candidate `ac8339c754a84ab53ca07708c091d4e79bd1fb5b`,
group `antiphon-all-group-82`. The 8.8-second case
`AgentReportStoreTests.Publication_exposes_only_a_complete_verified_report`
failed the simultaneous path assertion. The assertion omitted each result's
refusal reason, and the desktop TRX was outside this investigation's access.

## Introducing commit/card

`1ba060b669c60e1e6a55318e51476b0c5dcc5980` (CARD-0419) checks the
canonical path before staging, then overwrites it on publication. Two stores
can both miss the first check. Once one has published, the other can attempt
to replace the same path while the first store is reading it back. The final
path is determined by the full task ID and exact-byte SHA-256. A staging path
never becomes a return value, and a refused attempt returns a null path.

Linux can replace the directory entry while a reader holds the earlier file
open. Windows can refuse the replace when the destination is open, which
fits the reported null-path failure mode. The specific Windows refusal reason
remains unconfirmed. [Windows FileRenameInformation rules](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fsa/87f86c9b-6c2a-4803-84b7-131a74a434fa)

## Approving reviews

- CARD-0419 Review `45358a7f-bdf1-4e1b-af92-715f37e2acbd` recommended
  shipping at `89c037afdc6daa1b28ec7163a0d99d35ef6e2866` with an unrelated
  advisory about duplicate Git ignore rules. It ran `AgentReportStoreTests`
  (26/26) among its 191 passing cases. The legacy stage outcome has no
  Full/Interim scope value.
- CARD-1605 Review `cec313dd-43cf-4728-ab8d-04ff565b9c58` reported Clean
  at Interim scope for `ee2130f52ff91823b63c3a35bbae5e03203a7227`. Its
  CP-2 through CP-7 selections covered report budgets, cancellation, and
  settlement recovery. The controlled open-reader interleaving was absent.

## Outside the selection

The original test was in CARD-0419's Integration selection at
`tests/Antiphon.Tests/Application/AgentReportStoreTests.cs`. Its
`Task.WhenAll` assertion relied on natural scheduling to place both writers
past their initial checks and hold the winner's read handle across the second
move. Equal paths and bytes can still pass on Linux after a second replacement.
The regression now forces that ordering and checks the published file's
identity via a fixed last-write timestamp while a reader holds the file open.

## Cheapest pre-land check

`server/Infrastructure/Files/AgentReportStore.cs` and
`tests/Antiphon.Tests/Application/AgentReportStoreTests.cs` →
`/*/Antiphon.Tests.Application/AgentReportStoreTests/*`

The shortest direct witness is
`/*/Antiphon.Tests.Application/AgentReportStoreTests/Concurrent_publication_preserves_the_verified_winner_with_open_readers`.
The next Windows Full should run both the original method and the witness.

## Exemption

None.

## Linux evidence

Checkpoint run `20261122-094516-b8b0` built
`1ccc9cd1e32dd9195f64836e5fc8a7c14ec3ae99` with the controlled witness
added. The original method and production store were identical to assigned
master `c1e03aac509d8e949f34750a4ead2147b13695d0`. The original method
passed 10/10. The witness failed 10/10 on the timestamp assertion after both
stores had returned the canonical path. The fix rechecks the destination
under a task-ID publication gate immediately before the atomic move. A
contender reuses a verified winner and releases its staged file.
