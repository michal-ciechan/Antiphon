# CARD-0927 pinned jq implementation and verification

Original Code/landing owner: dd036af2. Base: f0fadc61635e3d977f045876d65e1e09ef06b17a.
No separate landed plan was supplied; this manifest makes the brief's scope explicit.

S0 commits regression tests while production remains at the start ref, and records red.
S1 adds only a new runtime-base install block, jq qualification row, wrapper registration,
and the image-dependencies sentence. Existing Dockerfile bytes and CARD-1008 files stay intact.
S2 runs isolated nested-Docker acceptance on the same ASP.NET base, and downloads the verified
static binary into ignored task scratch for final tests. No standing runner changes.

## Verification design

V-1: JqRunnerImageContractTests pins official URL/version/digest, hash-before-install,
root owner/mode, exact build version, cleanup, target inheritance and no apt jq.
V-2: JqRunnerImageContractTests executes the verifier row with correct/wrong/empty versions,
unsuccessful exit and stderr; qualification wrapper must register the row for both targets.
V-3: Manual isolated nested-Docker layer build through c927-jq-layer build slot; exact version,
root ownership/mode and real jq-version row as uid 1654, network none, no ports/socket/privilege.
V-4: Full RemoteScriptContractTests with scratch jq first on PATH; inspect C849/C912/C973
and every RequireLinuxJq case in fresh TRX, zero jq skips.
The current source has 66 methods expanded to 84 cases, confirmed independently from
attributes and the first full-class TRX (84/84). CP-3's initial floor of 100 was an authoring
error; it is corrected to this roster count without changing any test assertion or timeout.
The first Final run passed 3945 Unit cases with 33 Windows-only skips. Only CP-3 is rerun
after this manifest-only correction; implementation, tests and dependencies documentation
remain byte-identical to the source verified by the other completed rows.
V-5: Whole Unit category for the Final profile. This is required even though no shared helper
or registry changes. Unit does not prove production rollout/delivery/landing/lease/persistence.
R-1: Full DockerStackContractTests, CodexRunnerImageContractTests and GrokRunnerImageContractTests
preserve existing pins and anchors. No assertion is weakened.

PC-1 pending SourceLanding Mutation: bypass checksum gate; V-1 must fail.
PC-2 pending SourceLanding Mutation: accept nonexact jq version; V-2 wrong-version cases must fail.
The brief requests Code scratch mutants, but the standing stage contract assigns deliberate
mutants to Mutation. Ordinary red-first S0 remains Code evidence; PCs are not discharged here.

### Cost

Closed-list ordinary floor: 17 minutes plus authoring and manual layer build (~3 minutes).
No full-assembly run: impact is bounded to image/qualification contracts and shell consumers.
One invocation per unchanged proof selection; no loaded repetitions planned.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S0 | `tests/Antiphon.Tests -> bin-c927-red/` | red-first | `/*/*/JqRunnerImageContractTests/*` | V-1, V-2 | 14 executed, expected red before S1 | 14 | 3 | true |
| CP-2 | S1-S2 | `tests/Antiphon.Tests -> bin-c927-contract/` | image-contracts | `/*/*/(JqRunnerImageContractTests*)\|(DockerStackContractTests*)\|(CodexRunnerImageContractTests*)\|(GrokRunnerImageContractTests*)/*` | V-1, V-2, R-1 | all four full classes, zero failed/skipped | 163 | 3 | true |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c927-shell/` | real-jq-shell | `/*/*/RemoteScriptContractTests/*` | V-4 | all 84 cases, zero failed or jq skips | 84 | 3 | true |
| CP-4 | S1-S2 | `tests/Antiphon.Tests -> bin-c927-unit/` | final-unit | `/*/*/*/*[Category=Unit]` | V-5 | whole Unit lane, zero failed; platform skips individually accounted | 2000 | 8 | true |
