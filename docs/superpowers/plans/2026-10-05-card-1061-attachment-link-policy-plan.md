# CARD-1061: reject hard-linked attachment sources and qualify Windows junction checks

Date: 2026-10-05. Stage: Plan; TestDesign remains a separate dispatch.
Inspected source: `14316228eaac5afd8a404d75fca69d7a9fa94382`.
Card: Antiphon CARD-1061, read through `scripts/card.ps1 get CARD-1061 -Board Antiphon -Json`.

Reject a source file unless its opened handle reports exactly one link. Apply this
to attachment bytes, text and the retained-file hashing path. Qualify the existing
Windows parent-directory reparse checks with real NTFS junctions, including a
junction installed after pathname validation. Deliver two independently committed
30-60 minute slices. This plan changes no production code and claims no runtime
qualification. The brief does not fold TestDesign into Plan; the proposed tests,
controls and checkpoint roster below are its concrete starting point.

## Ground truth

| Card assumption | Code at the inspected source | Consequence |
|---|---|---|
| An in-root hard link can expose bytes also named outside the root. | `server/Infrastructure/Files/ChannelReplyAttachmentReader.cs`: `ValidatePath` checks absolute paths, traversal, root prefix and component attributes. A hard link is an ordinary file and passes these checks. | The premise is supported by code inspection and the card's prior Review disclosure. No fresh reproduction is claimed here. |
| Linux already has enough native protection. | `OpenRegularFile` walks retained directory descriptors with no-follow flags, opens the leaf without blocking, and calls `statx` on the leaf descriptor. Its request mask is only `1`; `FileStat` exposes only `Mode` at offset 28. | Keep that open sequence. Request and inspect link-count metadata from the same handle before constructing the stream. |
| Windows hard-link metadata needs a new API. | `GetFileInformationByHandle` already fills `FileInformation.Links`; the leaf gate checks disk type and reparse/directory/device attributes but ignores `Links`. | Consume the existing observation; no extra pathname query or new native API is needed in production. |
| Only `ReadAttachmentAsync` is affected. | `ReadTextAsync` calls it. Static `HashFileAsync` independently validates the path and calls the same `OpenRegularFile`. `ChannelOutboundFileStore.StageCapturedAsync` and `TryAdoptAsync` hash retained snapshot files through it. | Put enforcement in the shared opener and retain coverage of hashing. Staged files are written as new files, not hard links. |
| A junction test alone proves Windows native checks. | A pre-existing junction is refused by `ValidatePath` before `OpenRegularFile`. Windows then separately opens each parent with `OPEN_REPARSE_POINT`, rejects reparse attributes and pins parents without delete sharing until leaf open. | Prove preflight and native checks separately. A deterministic post-validation junction fixture must reach the latter. |
| Existing reader tests cover the request. | `tests/Antiphon.Tests/Application/ChannelOutboundStorageTests.cs` has four C1059 reader tests for roots, symbolic links, budgets and nonregular files. None creates a hard link. Its link test uses `CreateSymbolicLink`, not a Windows junction. | Add focused cases; preserve the existing narrow regression selection. Windows symbolic-link privilege is not a prerequisite for the new junction proof. |
| A fixture framework must be invented. | `tests/Antiphon.Tests/TestHelpers/DirectoryLink.cs` creates a junction on Windows and removes only the link on disposal. It can return null. `BeforeReadAsync` already exists on the reader, but runs after safe open and length inspection. | Reuse the directory-link helper with a required-success assertion. Add a distinct per-instance before-open barrier at the correct boundary. |
| Platform follows the card automatically. | Card `requiredPlatform` is Any. GET `/api/runner-defaults` returned revision 2 with no kind overrides; GET `/api/session-runners` showed eligible Linux and Windows lanes and an unavailable descriptor. | Require Linux for CP-1/2 and native Windows for CP-3/4. Resolve hosts from the live catalogue; embed no fleet location or runner pin. |

## Decisions

**D-1 — Adopt refusal of multiply linked files.** Require link count exactly one
at safe-open inspection. This is the selected design, not a pending product default.
It covers aliases wholly inside allowed roots as well as aliases to a sibling
outside directory. Reject zero or unavailable counts too: a file unlinked during
open is conservatively refused, and a subsequent attempt can re-evaluate its path.
Raise `InvalidDataException`, matching existing unsafe-file failures. The existing
preparation failure/retry owner handles it; do not silently omit an attachment or
introduce a new transport result.

Accepting hard links on the theory that local writers already possess the bytes
is a defensible alternative, but leaves the reader's link restrictions surprising
and makes its boundary depend on platform permissions. Reject that alternative
here. Also reject discovering every alias and allowing only in-root aliases:
link count does not enumerate names, and a volume-wide scan is neither bounded
nor atomic. Document the compatibility cost: legitimate hard-linked artifacts
must be copied to an independent regular file before attachment.

**D-2 — Inspect the opened file, once, before reading.** On Linux extend the
existing 256-byte explicit `FileStat` with `Mask` at byte 0 and `Links` at byte 16;
retain `Mode` at byte 28. Request `STATX_TYPE | STATX_NLINK` (`0x5`), require both
returned bits, then check regular mode and link count. The kernel explicitly
distinguishes requested metadata from available metadata, including fabricated
fallback values; zero-initialized fields are not proof of support.
([Linux UAPI definition](https://github.com/torvalds/linux/blob/master/include/uapi/linux/stat.h))

On Windows use the already retrieved leaf `FileInformation.Links` after the
existing disk/attribute checks. Its native member is the number of links to the
file. ([Microsoft structure contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information))
Do not apply a single-link rule to parent directories. Preserve handle disposal,
no-follow flags, Windows sharing flags, read budgets and cancellation behavior.
Keep a small internal pure `RequireSingleLinkCount(uint)` policy gate shared by
the two OS branches, and an internal pure Linux metadata validator used by the
actual Linux opener. These permit deterministic missing-metadata/zero-count tests
without replacing the native opener or relying on a special filesystem.

**D-3 — State the security limit precisely.** This is a handle-time link-count
policy and existing path/reparse protection. It proves neither historical inode
origin nor continuous exclusivity while a concurrent local writer runs. A writer
can copy bytes, remove an outside name before inspection, or on Linux change
links/content after the metadata observation. Do not claim that this makes
arbitrary writable allowed roots an inode sandbox. Continuous isolation would
require a different ownership/snapshot model and is outside this card.

**D-4 — Exercise native Windows checks without scheduling races.** Add internal
per-instance `BeforeOpenAsync` immediately after `ValidatePath` and before
`OpenRegularFile` in `ReadAttachmentAsync`; pass its cancellation token and await
it. Text inherits the hook through that method. Keep `BeforeReadAsync` at its
current boundary. No static mutable state, configuration switch or public DI
contract change. In the Windows test, move an ordinary fixture directory aside
and install a real junction at that same name during the hook. Native parent
inspection must refuse it. This proves the existing native check without timing
loops or mocking Win32 attributes. Hashing needs no barrier to prove its shared
hard-link policy.

**D-5 — Use real, owned filesystem fixtures.** Keep allowed and outside sibling
directories under one unique scratch root, on the same volume. A new small
`NativeHardLink` test helper invokes Linux `link` or Windows `CreateHardLinkW`,
checks native success and fails visibly with the native error. It creates no
process. NTFS and same-volume fixture paths are required on Windows.
([Microsoft hard-link contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-createhardlinkw))
Prove setup with link-count/identity observations independent of the reader's
policy validator, plus matching fixture bytes. Never let inability to create a
link become a passing test or an accepted skip. Junction methods require native
Windows, use the existing assembly-local ProcessSpawnLimit for the junction
helper's child process, and dispose the junction before recursive scratch cleanup.
Keep all test-owned child work bounded and joined. Do not probe real user files.

**D-6 — Bound scope to this reader and its immediate consumers.** No new package,
settings, migration, channel binding, broker traffic or retry behavior. Update
the outbound section of `docs/telegram.md` and the interface summary to describe
hard-link refusal and the trust limit. Converted-output sealing and generic Git
cleanup link policies are separate code paths and are not changed or claimed
qualified by this work. Existing CARD-0519 S8/CP-6 obligations remain in force;
this card supplies the specifically missing reader/junction evidence.

## Implementation slices

### S1 — Shared policy, Linux proof and bounded regressions (35-50 minutes)

Files:

- `server/Infrastructure/Files/ChannelReplyAttachmentReader.cs`: metadata layout,
  mask request/validation, shared count gate in both OS branches, before-open hook.
- `server/Application/Interfaces/IChannelReplyAttachmentReader.cs`: contract comment.
- `tests/Antiphon.Tests/TestHelpers/NativeHardLink.cs` (new): owned hard-link fixture.
- `tests/Antiphon.Tests/Application/ChannelReplyAttachmentReaderTests.cs` (new):
  four unparameterized methods listed below, with real native reads on the current OS.
- `docs/telegram.md`: operational behavior and compatibility/trust-boundary wording.

Implement both OS checks together so there is one shared policy change. Commit
and push before CP-1/2. Run those rows on Linux and report the Windows branch as
awaiting S2 qualification. Stage and hash a regular fixture through the normal
reader to ensure refusal did not replace all successful reads. Do not modify
the C1059 tests to weaken existing requirements.

### S2 — Native Windows hard-link and junction qualification (35-50 minutes)

Files:

- `tests/Antiphon.Tests/Application/ChannelReplyAttachmentReaderWindowsTests.cs`
  (new): two unparameterized Windows-only methods below; Integration category and
  ProcessSpawnLimit. Fail explicitly if selected on the wrong OS.
- Reuse `tests/Antiphon.Tests/TestHelpers/DirectoryLink.cs` without widening its
  semantics; a null creation result is a fixture failure in these tests.
- This plan: record any justified roster/name changes before the run. Generated
  evidence remains ignored; receipt facts go in the stored task report.

Run CP-3/4 on native Windows against the committed S2 SHA. CP-3 also executes
S1's real hard-link tests, qualifying the Windows count check. If native proof
finds a production defect, repair it in a new committed slice, update the manifest
to requalify the affected Linux rows at that source, and report the reason for
the added runs. A Linux green does not close S2; unavailable Windows prerequisites
leave that qualification pending.

## TestDesign handoff

TestDesign must add the owner's full `## Verification design` structure, complete
the inspection/guard inventory and confirm these exact method bindings and costs.
Move the single Checkpoints section into it; do not leave duplicate manifests.
No changed asynchronous delivery path is proposed: this is the file-read boundary
feeding the existing preparation pipeline, not new publication or recipient proof.

### Proposed behavior and regression roster

| ID | Test method (new unless identified otherwise) | Decisive observations |
|---|---|---|
| V-1 | `ChannelReplyAttachmentReaderTests.C1061_Byte_and_text_reads_reject_hard_links` | Byte and text entry points reject an in-root hard link to an outside sibling file and two names wholly inside allowed roots. The fixture independently confirms a real two-link file. A separate ordinary file returns exact bytes/text; deleting the second name makes the formerly linked file readable. Each entry point is asserted, not merely invoked. |
| V-2 | `ChannelReplyAttachmentReaderTests.C1061_Hashes_reject_hard_links` | `HashFileAsync` refuses the same outside/inside alias arrangements. A single-link file returns its exact length and independently computed SHA-256. This protects retained-file staging/adoption's use of the shared opener without a database fixture. |
| V-3 | `ChannelReplyAttachmentReaderTests.C1061_Linux_metadata_requires_type_and_link_count` | Through the production Linux metadata validator, regular mode with count one is refused when either requested result-mask bit is missing, including both missing; both present accepts. A fabricated count of one with its bit absent must fail. Real Linux V-1/2 prove the validator is wired to a native handle. |
| V-4 | `ChannelReplyAttachmentReaderTests.C1061_Zero_link_count_is_rejected` | Shared production count validator refuses zero, accepts one, refuses two. This explicitly covers the conservative zero-count policy without a nondeterministic unlink race. Native V-1/2 establish call-site use. |
| V-5 | `ChannelReplyAttachmentReaderWindowsTests.C1061_Preexisting_junction_is_refused_before_open` | Real outward junction in an allowed path, and the same junction used as allowed root, both refuse before the before-open hook is reached. An ordinary-directory companion returns expected bytes and reaches the hook. Check hook count per attempt and reset between cases. |
| V-6 | `ChannelReplyAttachmentReaderWindowsTests.C1061_Junction_after_validation_is_refused_by_native_open` | Start with an ordinary valid path; the before-open hook moves its parent aside and installs a junction to an outside directory with the same leaf name and distinguishable bytes. Assert hook and junction creation occurred, then exact unsafe-directory refusal and no returned bytes. Restore the ordinary path and prove exact original bytes are readable. |
| R-1 | Four existing `ChannelOutboundStorageTests.C1059_Source_*` methods | Roots/traversal, symbolic links, finite length/growth and nonregular rejection retain their current assertions. Linux executes all four. Windows executes all except the symbolic-link method, whose privilege-dependent setup is unnecessary for this junction card. |
| R-2 | Existing `ChannelOutboundStorageTests.Frozen_reply_and_input_bytes_survive_source_mutation` | Frozen reply/input contents remain correct and tampered stored reply is rejected. No new DB, broker or session fixture is required. |

### Proposed positive controls

One control per named behavior/independently bypassable guard. V-1 has separate
native call sites and V-6 has separate open-flag and attribute guards, so each
requires its own control. These are later method-scoped Mutation work, not ordinary
Code runs. Regressions retain their prior cards' control obligations.

| PC | Lane | Compiling mutation | Detecting filter | Required red |
|---|---|---|---|---|
| PC-1 | Linux | Bypass the single-link policy call in the Linux leaf validation path only. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Byte_and_text_reads_reject_hard_links` | Expected unsafe-file exception is absent for the real two-link fixture. |
| PC-2 | Windows | Bypass the single-link policy call in the Windows leaf validation path only. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Byte_and_text_reads_reject_hard_links` | The same hard-link refusal assertion fails on NTFS. |
| PC-3 | Linux | In `HashFileAsync` only, replace the safe opener with an ordinary read-only FileStream, retaining pathname validation. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Hashes_reject_hard_links` | A hard-linked file incorrectly produces a hash instead of refusing. |
| PC-4 | Linux | Remove the required statx result-mask check, leaving mode/count checks intact. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Linux_metadata_requires_type_and_link_count` | Missing-mask inputs with fabricated valid values incorrectly pass. |
| PC-5 | Linux | Weaken the shared link-count gate from count != 1 to count > 1. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Zero_link_count_is_rejected` | Zero incorrectly passes; valid one remains the companion. |
| PC-6 | Windows | Remove ReparsePoint from `ValidatePath`'s component rejection mask, retaining device and directory checks. | `/*/*/ChannelReplyAttachmentReaderWindowsTests/C1061_Preexisting_junction_is_refused_before_open` | The hook is reached for a linked path, even if the native gate later refuses it. |
| PC-7 | Windows | Remove only the parent-handle reparse-attribute predicate. | `/*/*/ChannelReplyAttachmentReaderWindowsTests/C1061_Junction_after_validation_is_refused_by_native_open` | The outside fixture becomes readable; expected refusal is absent. |
| PC-8 | Windows | Remove only OPEN_REPARSE_POINT from Windows parent opens, retaining BACKUP_SEMANTICS and sharing flags. | `/*/*/ChannelReplyAttachmentReaderWindowsTests/C1061_Junction_after_validation_is_refused_by_native_open` | Parent inspection follows the junction and expected refusal is absent. |

Missing fixtures, build errors, timeouts, zero tests or skips never count as PC red.
Run baseline, mutation and restored green with precisely the listed method filter;
retain separate evidence per PC. These controls share the reader file and must
run serially. TestDesign must reject a control if the proposed native fixture
cannot reach its asserted boundary, and resolve the fixture before Code handoff.

### Execution and cost

CP-1/2 are the **Linux lane**; CP-3/4 are the **native Windows lane**, on a local
NTFS scratch volume supporting hard links and junctions. Read GET
`/api/runner-defaults` and GET `/api/session-runners` again before dispatch. Omit
`-Runner`. Use `-Platform Linux` or `-Platform Windows` only for the OS-bound
execution slices; planning/TestDesign need no OS pin. `-Platform Any` unpins an
inherited requirement. A checkpoint group names its lane because the importer
does not support a Lane column or automatically select rows by host OS.

After S1's commit use the checkpoint tool's `run --plan <this-plan> --rows CP-1,CP-2
--expected-source-sha <S1-sha>`. After S2's commit use `--rows CP-3,CP-4` and that
SHA on Windows. Launch the tool through the host build-slot wrapper; checkpoint
drivers acquire their own slots. Await every run and continue `wait` on exit 75
until completion. Never launch the entire table on one OS. Slot timeout exit 4
is not permission to run without a lease. Preserve the emitted CHECKPOINT lines
and source-qualified receipts, including actual counts, skips and failures.

Each slice commits and pushes before its checkpoint group. Keep tracked files
frozen during the run. Verify failures at the base with the same narrow failing
method before labeling them inherited; no assembly rerun. Clean only the exact
owned alternate-output directories after their children finish. Code/Review run
the full-task-range evidence diff guard. TRX, JSON and logs remain gitignored.

Estimated ordinary floor: 8 + 2 + 10 + 2 = **22 minutes** (Linux 10, Windows 12),
plus **48-78 minutes** combined authoring/review across the two slices. Each slice
is estimated at 35-50 minutes including its rows. Eight serial PCs need 24 phase
build/test invocations at an estimated 3 minutes each: **72 minutes** Mutation,
plus about 15 minutes for mutation/restoration work. These are estimates, not
measurements. The brief explicitly excludes whole-Unit execution; four common
tests, two Windows tests and five named existing regression methods bound the
scope. No whole namespace, assembly, E2E or provider run is authorized here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1061-linux/` | linux-link-policy | `/*/*/ChannelReplyAttachmentReaderTests/*` | V-1, V-2, V-3, V-4 | exactly 4 listed methods, 0 failed/skipped | 4 | 8 |
| CP-2 | S1 | CP-1 | linux-reader-regressions | `/*/*/ChannelOutboundStorageTests/(C1059_Source_*)\|(Frozen_reply_and_input_bytes_survive_source_mutation*)` | R-1, R-2 | exactly 5 listed methods, 0 failed/skipped | 5 | 2 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1061-windows/` | windows-native-links | `/*/*/(ChannelReplyAttachmentReaderTests*)\|(ChannelReplyAttachmentReaderWindowsTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6 | exactly 6 listed methods, 0 failed/skipped | 6 | 10 |
| CP-4 | S2 | CP-3 | windows-reader-regressions | `/*/*/ChannelOutboundStorageTests/(C1059_Source_reads_require_captured_roots_without_traversal*)\|(C1059_Source_length_is_checked_before_reading_with_a_finite_budget*)\|(C1059_Source_reads_refuse_nonregular_files_without_blocking*)\|(Frozen_reply_and_input_bytes_survive_source_mutation*)` | R-1 Windows-applicable methods, R-2 | exactly 4 listed methods, 0 failed/skipped | 4 | 2 |
