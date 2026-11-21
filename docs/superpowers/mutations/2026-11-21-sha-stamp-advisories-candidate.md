# SHA stamp advisories unbound Mutation candidates

Original Code and landing owner `877ebad8`. Plan
[SHA stamp advisories](../plans/2026-11-21-sha-stamp-advisories-checkpoints.md).
Every variant is pending. SourceLanding Mutation can bind a candidate to the
landed SHA and run its exact detecting method before and after restoration.

| ID and variant | Deliberate defect | Exact detecting filter | Required assertion red |
|---|---|---|---|
| PC-1 / dev-start | Drop the stamp from the server command passed to `Open-Tab`. | `/*/*/SourceRevisionStampContractTests/Deployed_builds_stamp_head` | The AST audit names the legacy launcher build. |
| PC-1 / dev-upgrade-first, dev-upgrade-fallback | Drop the property from one Release build at a time. | `/*/*/SourceRevisionStampContractTests/Deployed_builds_stamp_head` | The AST audit names the affected build invocation. |
| PC-1 / restart-server | Drop the stamp from the build of `$ServerProject`. | `/*/*/SourceRevisionStampContractTests/Deployed_builds_stamp_head` | The AST audit names the server build. |
| PC-1 / AppHost forwarding | Unquote the property passed to the build-slot wrapper in the encoded AppHost command. | `/*/*/SourceRevisionStampContractTests/Deployed_builds_stamp_head` | The AST audit finds the split property instead of one stamp argument. |
| PC-2 / comment, literal, quoted command, detached command, dead splat | Undo the AST decision for one decoy at a time. | `/*/*/SourceRevisionStampContractTests/PowerShell_census_rejects_stamp_decoys` | The selected decoy is missed. |
| PC-3 / host hash | Omit the comparison to the runner output. | `/*/*/C1022BackendLaunchTests/Different_assemblies_with_the_same_unstamped_version_are_rejected` | Equal unknown versions let different binaries pass. |
| PC-3 / expected SHA, loaded SHA | Omit one SHA-presence check at a time in deploy mode. | `/*/*/C1022BackendLaunchTests/Unknown_build_is_rejected_when_deploy_identity_is_required` | The method misses the corresponding SHA-specific assertion. |
| PC-4 / unknown no-build warning | Omit only the warning before the existing executable launch. | `/*/*/C1744G3PhoneHomeSupervisionTests/Phone_home_no_build_warns_and_launches` | The warning row fails while the launch sentinel still executes. |

Use Antiphon.Tests for PC-1, PC-2 and PC-4, and
Antiphon.SessionRunner.Tests for PC-3. Preserve the exact test selectors and
capture fresh failing and restored TRX at the bound source. The Code task's
pre-fix red receipts do not discharge these cycles.
