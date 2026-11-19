# DW-2 checkpoints

DW-2 of the desktop-win call-home gap plan (that plan's CP-3). Documentation only:
`docs/docker-stack.md` states the split this master implements. One build and one
exact filter. Min is the TUnit executed-test floor. At base
`36d0c758298b85516a29ee05a13225c15e14fc23`, `DockerStackDocumentationTests` has
20 `[Test]` methods and no `[Arguments]`.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | DW-2 | tests/Antiphon.Tests -> bin-dw2/ | dw2 | `/*/*/DockerStackDocumentationTests/*` | pinned docker-stack strings still hold | 0 failed | 20 | 15 |
