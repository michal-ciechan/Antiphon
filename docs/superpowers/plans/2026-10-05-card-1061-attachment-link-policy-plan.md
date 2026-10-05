# CARD-1061: reject hard-linked attachment sources and qualify Windows junction checks

Date: 2026-10-05. Stage: Plan repair; verification design complete for Code.
Product inspection: `14316228eaac5afd8a404d75fca69d7a9fa94382`.
Repair inspection/base: `84859a041f68b7430b16f1a64b01624063a9734f`, including
the TestDesign amendment; task `54118627`.
Card: Antiphon CARD-1061, read through `scripts/card.ps1 get CARD-1061 -Board Antiphon -Json`.

Reject a source file unless its opened handle reports exactly one link. Apply this
to attachment bytes, text and the retained-file hashing path. Qualify the existing
Windows parent-directory reparse checks with real NTFS junctions, including a
junction installed after pathname validation. Deliver three independently committed
30-60 minute slices. This repair resolves TestDesign's D-5 fixture seam and folds
its fixture verification into the existing verification design. It changes only
this plan and claims no runtime qualification. The single manifest below is the
closed Code scope; Mutation remains post-land work.

## Ground truth

| Card assumption | Code at the inspected source | Consequence |
|---|---|---|
| An in-root hard link can expose bytes also named outside the root. | `server/Infrastructure/Files/ChannelReplyAttachmentReader.cs`: `ValidatePath` checks absolute paths, traversal, root prefix and component attributes. A hard link is an ordinary file and passes these checks. | The premise is supported by code inspection and the card's prior Review disclosure. No fresh reproduction is claimed here. |
| Linux already has enough native protection. | `OpenRegularFile` walks retained directory descriptors with no-follow flags, opens the leaf without blocking, and calls `statx` on the leaf descriptor. Its request mask is only `1`; `FileStat` exposes only `Mode` at offset 28. | Keep that open sequence. Request and inspect link-count metadata from the same handle before constructing the stream. |
| Windows hard-link metadata needs a new API. | `GetFileInformationByHandle` already fills `FileInformation.Links`; the leaf gate checks disk type and reparse/directory/device attributes but ignores `Links`. | Consume the existing observation; no extra pathname query or new native API is needed in production. |
| Only `ReadAttachmentAsync` is affected. | `ReadTextAsync` calls it. Static `HashFileAsync` independently validates the path and calls the same `OpenRegularFile`. `ChannelOutboundFileStore.StageCapturedAsync` and `TryAdoptAsync` hash retained snapshot files through it. | Put enforcement in the shared opener and retain coverage of hashing. Staged files are written as new files, not hard links. |
| A junction test alone proves Windows native checks. | A pre-existing junction is refused by `ValidatePath` before `OpenRegularFile`. Windows then separately opens each parent with `OPEN_REPARSE_POINT`, rejects reparse attributes and pins parents without delete sharing until leaf open. | Prove preflight and native checks separately. A deterministic post-validation junction fixture must reach the latter. |
| Existing reader tests cover the request. | `tests/Antiphon.Tests/Application/ChannelOutboundStorageTests.cs` has four C1059 reader tests for roots, symbolic links, budgets and nonregular files. None creates a hard link. Its link test uses `CreateSymbolicLink`, not a Windows junction. | Add focused cases; preserve the existing narrow regression selection. Windows symbolic-link privilege is not a prerequisite for the new junction proof. |
| Existing junction creation is bounded. | `DirectoryLink.TryCreate` drains stdout synchronously, then stderr, before `WaitForExit(30_000)`. Timeout returns null without terminating/joining the child. `Dispose` only removes the link. | ProcessSpawnLimit and a caller timeout cannot make this safe. Replace that Windows launch path under D-5a before V-5/V-6 or PC-6..8 execute. |
| No reusable process owner exists. | `Scripts/ScriptHarnessProcess.RunAsync` concurrently pumps both streams, uses one execution deadline, and finally terminates/confirms death/drains with a fresh cleanup budget. `WindowsScriptHarnessProcess` owns a private non-breakaway job before resume. `ScriptHarness.RunHarnessCaseAsync` adds unrelated PASS-marker validation. | Use the lower-level process runner through a new junction script adapter; do not call the marker validator, duplicate native custody, or change the shared owner. |
| A read barrier already exists at the needed boundary. | `BeforeReadAsync` runs after safe open and length inspection. | Add the per-instance before-open barrier in D-4. |
| Platform follows the card automatically. | Card `requiredPlatform` is Any. Repair reads of GET `/api/runner-defaults` on 2026-10-05 returned revision 2 with no kind overrides; GET `/api/session-runners` showed available Linux and Windows descriptors and one unavailable descriptor. | Require Linux for CP-1/2 and native Windows for CP-3/4. Resolve hosts from the live catalogue; embed no fleet location or runner pin. Availability is not NTFS/pwsh qualification. |

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

**D-5a — Repair the junction launch by reusing the existing process owner.**
Replace the raw `Process.Start`/`ReadToEnd`/`WaitForExit` block in `DirectoryLink`
with an internal async `DirectoryLinkCommand.RunAsync` adapter around
`ScriptHarnessProcess.RunAsync`. Keep `TryCreate(string, string)` for existing
callers; its Windows branch synchronously joins the complete async operation
(a joined `Task.Run` bridge avoids capturing a caller synchronization context).
Add `TryCreateWindowsAsync` for the new awaited junction tests. Both use the
same adapter and result gate. Linux `CreateSymbolicLink`, `MoveTo` and
nonrecursive link disposal retain their behavior. No global hooks or mutable
defaults. Inject `ScriptHarnessOptions`/owner, script/case and cancellation per
call for the fixture witnesses, never via a static override. The default
TryCreate entry chooses the Windows path only on Windows; the internal async
orchestration and result gate remain callable with fake owners on either host.
Pure cases supply a dummy ExecutablePath with their fake owner so neither PATH
resolution nor native launch is a hidden test prerequisite.

Add `tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1`, accepting the
owner's `-Case`/`-ResultsDirectory` arguments and distinct literal link/target
arguments. Its only normal case calls the system `cmd.exe /d /v:off /c mklink /J`,
with quoted owned paths, inherited stdout/stderr, and returns the actual native
exit code. Preserve spaces and Unicode in paths; fail setup for unsupported
cmd expansion characters instead of executing interpolated commands. No
`Start-Process`, detached work, symlink fallback, WSL or elevation. `/J` creates
a directory junction ([Microsoft command contract](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/mklink)).
The installed real `pwsh.exe` is a fixture prerequisite; resolve it with the
existing installed-PowerShell resolver. The Windows job owns pwsh, cmd and any
descendants before they execute; inherited child membership and job termination
are the [Windows ownership contract](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

Use **30 seconds execution and 10 seconds cleanup** for ordinary junction
creation, supplied explicitly rather than the harness's 300-second default.
The shared runner starts stdout and stderr pumps together and observes root exit
and both EOFs under **one monotonic execution allowance**; root exit does not
restart it. A read fault enters cleanup promptly. Success, nonzero exit, launch
failure, timeout, caller cancellation and pipe fault all go through the same
owner finalization. Cleanup uses one fresh allowance independent of the expired
execution/caller token: terminate the owned job even if the root already exited,
confirm zero active members and retained process handles signaled, join/drain
both pumps, then close/dispose handles. Do not equate `Kill`, root exit or
`Process.Dispose` with joined descendants or EOF. Reuse these inspected owner
operations unchanged; do not add an unbounded fallback wait.

Keep ordinary unsupported/nonzero creation as null **only after successful owner
cleanup**, and require the resulting path's ReparsePoint observation before
returning a `DirectoryLink`. Timeout, cancellation, pipe/launch/cleanup errors
from the owned run must escape the helper's old broad IOException catch. In
particular, unknown cleanup cannot become null, skip or successful setup.
Preserve the primary error and cleanup diagnostics. When cleanup is unconfirmed,
retain owned scratch/evidence, fail the fixture and stop the lane for diagnosis;
do not recursively erase paths under an uncertain writer. Conservatively retain
scratch on any owned-run exception unless the independent observer confirms
cleanup; do not parse diagnostic strings to manufacture cleanup authority.

The caller establishes that its link name was absent, owns the unique scratch
root and uses `finally` to remove only that created reparse entry before restoring
a renamed ordinary directory or deleting scratch. After a failed command with
confirmed process cleanup, inspect/remove a partially created junction even when
no `DirectoryLink` was returned; never remove a pre-existing ordinary directory
or follow a target. V-5/V-6 require nonnull success and independent ReparsePoint
proof. The native witnesses include target survival after move/disposal and
partial-fixture cleanup. This is a test-fixture failure policy, not a change to
the reader's InvalidDataException contract.

Reject simply adding `WaitAsync` around the old helper: its child and pipe reads
would still outlive the timeout. Reject a root-only `Kill(true)` plus dispose:
the root may have exited while a descendant holds a pipe. Reject a new Win32
junction/Job Object implementation: the repository already owns and tests the
required process contract. The small script adapter is additional implementation
work, not an assertion that the existing marker-based ScriptHarness can directly
wrap arbitrary synchronous C# code. Its wiring, failures and cost are F-1..F-7
and PC-14..PC-20 below. Shared-owner repairs, if discovered, return to Plan.

**D-6 — Bound scope to this reader and its immediate consumers.** No new package,
settings, migration, channel binding, broker traffic or retry behavior. Update
the outbound section of `docs/telegram.md` and the interface summary to describe
hard-link refusal and the trust limit. Converted-output sealing and generic Git
cleanup link policies are separate code paths and are not changed or claimed
qualified by this work. Existing CARD-0519 S8/CP-6 obligations remain in force;
this card supplies the specifically missing reader/junction evidence.

## Implementation slices

### S1 — Shared policy, Linux proof and bounded regressions (30-40 minutes)

Files:

- `server/Infrastructure/Files/ChannelReplyAttachmentReader.cs`: metadata layout,
  mask request/validation, both OS count gates, before-open hook.
- `server/Application/Interfaces/IChannelReplyAttachmentReader.cs`: contract comment.
- `tests/Antiphon.Tests/TestHelpers/NativeHardLink.cs` (new): owned hard-link fixture.
- `tests/Antiphon.Tests/Application/ChannelReplyAttachmentReaderTests.cs` (new):
  V-1..V-4, including V-2's regular captured-stage/adoption companion.
- `docs/telegram.md`: refusal, compatibility and trust-limit wording.

Implement both OS checks together. Preserve the C1059 tests. Commit and push this
slice; ordinary source qualification waits for the complete S1-S3 candidate.
There is no whole-Unit or extra S1 checkpoint run.

### S2 — Owned junction command and fixture contract (40-60 minutes)

Files:

- `tests/Antiphon.Tests/TestHelpers/DirectoryLink.cs`: shared async Windows
  path, synchronous compatibility bridge, post-command result gate and exception
  boundaries; Linux creation and MoveTo/Dispose behavior preserved.
- `tests/Antiphon.Tests/TestHelpers/DirectoryLinkCommand.cs` (new): D-5a adapter,
  per-call options, exact execution/cleanup budgets and caller token.
- `tests/Antiphon.Tests/Scripts/Fixtures/directory-link.ps1` (new): foreground
  mklink command, native exit propagation and literal fixture path arguments.
- `tests/Antiphon.Tests/TestHelpers/DirectoryLinkFixtureTests.cs` (new): five
  unparameterized contract methods F-1..F-5, Unit category, no native children.

Call the existing lower-level ScriptHarnessProcess; do not modify its owner or
marker validator. The pure witnesses use fake owners/streams and FakeTimeProvider
through per-call options. They call the actual adapter/result gate, not a copied
implementation. Commit and push before S3. No separate build is added.

### S3 — Native Windows fixture and reader qualification (40-60 minutes)

Files:

- `tests/Antiphon.Tests/Application/ChannelReplyAttachmentReaderWindowsTests.cs`
  (new): V-5/V-6, two unparameterized Windows-only methods.
- `tests/Antiphon.Tests/TestHelpers/DirectoryLinkFixtureWindowsTests.cs` (new):
  two unparameterized native methods F-6/F-7 with independent retained handles,
  per-invocation watchdog and finally cleanup patterned on the existing fixtures.
- This plan only if a justified exact roster change is needed before qualification.

Both native classes carry Integration, Slow where required by the duration owner,
and the assembly-local `ParallelLimiter<ProcessSpawnLimit>`; fail if explicitly
selected on the wrong OS. F-6/F-7 reuse the existing Windows owner hooks,
`ScriptHarnessWindowsProcessFixture`, staged ScriptHarnessHost and process
fixture script via per-call adapter options; no new helper executable or project.
The new test-local wrapper retains and awaits its own invocation; do not edit the
shared fixture just to add a wrapper entry point. Read its cleanup pattern and
reuse its independent observers. Windows qualification needs installed real
pwsh and local NTFS supporting hard links/junctions.

Commit and push S3, then run CP-1/2 in the Linux lane and CP-3/4 in the native
Windows lane at that **same complete candidate SHA**. This slice includes the
30-minute setup/ordinary verification allowance below and 10-30 minutes native
fixture/test authoring. All native scenarios are sequential under the limiter;
Antiphon.Agents.Pty.Tests is not co-scheduled. A new product/shared-owner defect
requires a new committed repair with the manifest and estimates updated before
running affected rows; unavailable native prerequisites leave qualification
pending. Linux success cannot close the Windows obligations.

## Verification design

TestDesign task `c9945af6` inspected
`fd9a5c2bc78c43c090a1c28c621472c69a6c1350` and produced amendment
`84859a041f68b7430b16f1a64b01624063a9734f`. Plan repair task `54118627`
resolves its known D-5 setup seam by D-5a, adds F-1..F-7 and PC-14..PC-20,
and replaces superseded proposed rosters/costs with this single design.
D-1..D-4/D-6 and the thirteen product controls retain their selected policy.

**Disposition: ready for Code.** All product and fixture controls have concrete
setup, observation, restoration and bounded execution designs. This is design
readiness, not executed evidence: no build, native test or mutation has run in
this Plan repair. PC-6..8 use only the repaired owned fixture after S2/S3; they
must never execute against the old unbounded DirectoryLink helper.

### Inspection

Bodies read, rather than inferred from names:

| Bodies read | Boundaries -> verification or exclusion |
|---|---|
| `ChannelReplyAttachmentReader.ReadAttachmentAsync`, `ReadTextAsync`, `ValidatePath`, `HashFileAsync`, `OpenRegularFile`, `OpenAt`, `OpenWindows`, both native layouts/declarations | V-1 through V-6; the byte, text and hash entry points are separate bypass opportunities. Linux metadata is descriptor-based; Windows parent flag and attribute check are independent. |
| `ChannelOutboundStorageTests.C1059_Source_reads_require_captured_roots_without_traversal`, `C1059_Source_reads_reject_file_and_directory_links`, `C1059_Source_length_is_checked_before_reading_with_a_finite_budget`, `C1059_Source_reads_refuse_nonregular_files_without_blocking` | R-1; all four bodies and their inline fixtures read. The budget-growth and device/FIFO arms only execute on Linux. Windows exclusion is limited to the symbolic-link method. |
| `ChannelOutboundStorageTests.Frozen_reply_and_input_bytes_survive_source_mutation` and `Failed_staging_never_exposes_a_partial_snapshot` | R-2 uses the former only. The latter is fixture context, not an added checkpoint. No DB/container fixture is needed for either. |
| `ChannelOutboundFileStore.StageAsync`, `StageCapturedAsync`, `StageCoreAsync`, `TryAdoptAsync`, `ReadReplyAsync` | V-2 covers the shared retained-file hashing primitive. R-2's `StageAsync` does **not** enter `StageCapturedAsync`'s retained-file loop. Add the explicit regular captured-stage/adoption companion described under V-2. |
| `ChannelOutboundDeliveryPump` preparation exception handling and `MaterializeAsync` | Delivery inventory: existing `InvalidDataException` failure owner and existing adoption boundary, no new enqueue/publication protocol. |
| `DirectoryLink.TryCreate`, `MoveTo`, `Dispose`, `IsLink`; `ProcessSpawnLimit.Limit`; `AgentTaskLandHalfResetWindowsTests.C883_CaseAliasCannotChangeRegisteredIdentity` and native host requirement | Nearest fixtures for the new Windows tests and helper. Required-success junction setup and nonrecursive link disposal are reusable; bounded/joined command execution is missing. |
| `Scripts/ScriptHarness.RunHarnessCaseAsync` and `Validate` | This marker-checking entry cannot directly wrap the C# helper. D-5a instead adds an actual foreground script and calls the lower-level process runner. |
| Repair: `ScriptHarnessProcess.RunAsync`, `PumpAsync`, `CreateOwner`, `ResolvePowerShell`; `ScriptHarnessOptions`/`ScriptProcessRequest` | Shared exit/EOF execution deadline, prompt fault path, separate cleanup allowance, unconditional termination/death confirmation, diagnostics/retention; adapter F-1..F-5 exercises these through the actual call. |
| Repair: `WindowsScriptHarnessProcess` constructor, `StartAndWaitForRootAsync`, `TerminateAsync`, `ConfirmDeadAsync`, `RetainTerminationProcesses`; `ScriptHarnessProcessFixture.Invocation` and `ScriptHarnessWindowsProcessFixture` retained observers | Native process job membership before resume, retained process joins, independent emergency cleanup; F-6/F-7 use the existing machinery without changing it. |
| Repair: `ScriptHarnessProcessContractTests` deadline/fault/cancellation/cleanup bodies; `ScriptHarnessProcessTests` timeout/pipe-holder bodies; `ScriptHarnessWindowsOwnershipTests.Nested_job_timeout_kills_owned_descendants_only`; `Scripts/Fixtures/script-harness-process.ps1`; `Antiphon.Tests.csproj` helper-copy target | Existing fake-clock and live native fixture patterns make the bounded adapter witnesses concrete. Existing staged helper and exact scenarios are reusable; no new executable/build project. These read bodies are design evidence, not claimed executed tests. |
| `IChannelReplyAttachmentReader`; outbound section of `docs/telegram.md`; project-context owner; testing/build checkpoint, filter, process, slot, mutation and receipt rules; orchestration stage handoff rules | Contract documentation, OS lanes, exact source evidence and post-land PC execution. |
| `PlanTableImporter.ImportMarkdown`, `ExtractSection`, `SplitRow`, `ExtractPayload` | One nine-column table; escaped OR pipes; reuse only within the same After group. The importer does not choose a host from Group. |

Missing setup is concrete: the two new reader test files, `NativeHardLink`,
`BeforeOpenAsync`, `RequireSingleLinkCount` and the pure Linux validator do not
yet exist. They are S1/S3 implementation work, not existing passing evidence.
Use a validator callable as `ValidateLinuxMetadata(uint mask, ushort mode,
uint links)` for the pure witnesses; it must be called by the real Linux opener.
This is the parameter-level binding of D-2, not a native-I/O substitute.

D-5a resolves the identified seam. The command adapter, junction script and
F-1..F-7 methods are also missing implementation and are fully assigned to S2/S3.
No existing helper is relabeled safe merely because its caller has a timeout.

### Delivery inventory

Changed asynchronous delivery paths: **zero**. Async file reads return to their
caller; they do not create a new queue, destination, durable handoff or recovery
worker. The existing context, which this card does not requalify, is:

| Producer / identity | Destination | Persistence boundary | Recovery | Receipt and limit of this plan |
|---|---|---|---|---|
| Existing preparation/pump, `ChannelOutboundDelivery.Id` | `StageCapturedAsync` then the existing conversion/publication pipeline | `complete.json` and retained files under that delivery's directory, followed by the existing DB transition | `TryAdoptAsync` revalidates the same capture and retained files; existing preparation exception handling persists failure | V-2 observes materialization/adoption bytes and hashes only. R-2 observes a frozen local snapshot only. Neither proves worker input or channel receipt. |

The new refusal is an `InvalidDataException` at the existing file-read boundary;
the inspected pump already treats that exception as terminal preparation failure.
No test here treats a request, queue insert, event, Sent flag, acknowledgement,
file hash or successful staging as delivery. No producer-to-recipient claim is
made. The busy/already-eligible real-queue cases and per-handoff crash/enqueue
recovery are excluded because their paths are unchanged under D-6; existing
CARD-0519 obligations remain, including matching complete `UserPrompt` evidence
for any session input. If Code changes those paths, return to Plan and add the
real queue and recipient witnesses before widening this acceptance claim.

Substitutes: pure metadata/count cases establish decisions, not OS observations;
native scratch files establish reader admission, not provider delivery; the
before-open hook establishes the validation/open cut, not every possible
concurrent rename. Linux success cannot substitute for native Windows proof.

### Proves it works now

All six methods are unparameterized. Internal combinations below contribute one
TUnit execution per method, not one execution per assertion. Assertion labels
are required diagnostic messages so every PC has a decisive red location.

- V-1: bytes and text enforce D-1/D-2 | native filesystem, Linux and Windows |
  `ChannelReplyAttachmentReaderTests.C1061_Byte_and_text_reads_reject_hard_links`
  | Create separate allowed/outside sibling directories in one unique scratch
  root. Cover outside alias plus allowed name, two names inside one allowed root,
  and names in two separately allowed roots. Observe count two and matching
  native file identity independently of the production validator (Linux
  device/inode; Windows volume/file index), and fixture bytes. Assert both
  entry points throw `InvalidDataException`, labeled `Links.BytesRefused` and
  `Links.TextRefused` with the arrangement. Test count-one exact bytes and UTF-8
  text, then remove the second name, independently observe count one and read
  the former linked file successfully. Also install a second name from
  `BeforeOpenAsync` for byte and text attempts: hook count one, same token,
  link creation completed, and refusal. This catches checking only at preflight.
  Close independent observation handles before the reader call on Windows.
- V-2: hashing retains safe-open admission and usable single-link snapshots |
  native filesystem plus local file store, both OSes |
  `ChannelReplyAttachmentReaderTests.C1061_Hashes_reject_hard_links` |
  Repeat V-1's three static alias arrangements with `HashFileAsync`;
  `Links.HashRefused` requires `InvalidDataException`. Independently compute
  SHA-256 from the known fixture bytes and assert returned length/hash for a
  single-link file and after removal of the second name. Within the same method,
  create an ordinary `ChannelReplyPrepared`, call real `StageCapturedAsync` with
  a fresh delivery ID and fixed nonempty capture string, then `TryAdoptAsync`
  for that same ID/string. Assert nonnull adoption, same prompt/revision,
  snapshot path/hash, and exact stored attachment bytes. Use an independently
  computed prompt revision, not a comparison of two defaults. This companion
  reaches both retained-hash call sites; it is not a recovery/delivery test.
- V-3: metadata requires each returned bit and regular mode | pure production
  validator on both hosts; native call-site evidence from V-1/V-2 |
  `ChannelReplyAttachmentReaderTests.C1061_Linux_metadata_requires_type_and_link_count`
  | For regular mode `0x8000` and count one, masks `0`, `1` and `4` refuse;
  `5` and `5 | 0x200` accept. Label missing-type (`mask=4`)
  `Metadata.TypeRequired`, missing-link (`mask=1`) `Metadata.LinksRequired`,
  and both-missing `Metadata.BothRequired`. With mask `5` and count one,
  directory `0x4000`, FIFO `0x1000` and device `0x2000` refuse at
  `Metadata.RegularRequired`. This prevents an extracted validator from
  preserving count checks while dropping the old regular-mode check.
- V-4: exact-one policy covers conservative zero and unsigned extremes | pure
  shared production validator, both hosts |
  `ChannelReplyAttachmentReaderTests.C1061_Zero_link_count_is_rejected` |
  Zero refuses at `Count.ZeroRefused`, one accepts, two and `uint.MaxValue`
  refuse at `Count.MultipleRefused`. Also exercise these counts with mask `5`
  and regular mode through the Linux validator. No unlink scheduling race is
  needed to fabricate a zero result.
- V-5: preflight rejects a junction before opening | native NTFS |
  `ChannelReplyAttachmentReaderWindowsTests.C1061_Preexisting_junction_is_refused_before_open`
  | Require Windows and an NTFS scratch volume; fail, do not skip, if either
  check or `DirectoryLink.TryCreate` fails. Verify ReparsePoint on the actual
  junction. For an outward junction nested under an allowed root and for that
  junction itself supplied as allowed root, capture the exception without
  asserting it first, assert hook count zero at `Junction.PreflightHookZero`,
  then assert the exact `InvalidDataException` message
  `Linked paths and devices are not source files.`. Exercise byte and text
  attempts independently; reset the hook count each time. An ordinary sibling
  returns exact bytes/text and reaches the hook once. Hook ordering prevents
  the later native refusal from hiding the preflight mutant.
- V-6: a junction introduced at the cut reaches native parent refusal | native
  NTFS | `ChannelReplyAttachmentReaderWindowsTests.C1061_Junction_after_validation_is_refused_by_native_open`
  | Use distinct trusted/outside bytes under the same leaf filename. In the
  awaited instance hook, rename the ordinary parent aside and install the real
  outward junction at its old name, then return. Record `hookCount == 1`,
  successful junction setup and ReparsePoint independently. Capture outcome,
  assert setup first, then require `InvalidDataException` with message
  `A source directory is linked or invalid.` at `Junction.NativeRefused`.
  Assert no result bytes escaped. Dispose the junction, restore the ordinary
  parent and verify original bytes; read failures must leave cleanup possible.
  Repeat with the replaced directory as the allowed root, using fresh paths.

V-1/V-2 fixtures must not create a second name by copying. New-file, same-volume
setup plus independent identity/count observations are mandatory. The metadata
matrix partitions type-bit presence, link-bit presence, mode and count with all
other fields valid for each refusal; full Cartesian repetition adds no distinct
guard witness. Counts zero/one/two/max bound the policy. Native zero and missing
mask cases are intentionally supplied to the pure validator because ordinary
scratch files cannot deterministically produce them.

Native Windows PC reachability follows the existing opener: OPEN_REPARSE_POINT
opens the named parent junction itself; removing its attribute rejection then
lets the later pathname-based leaf open traverse that parent. Removing the flag
instead opens the ordinary target directory, whose attributes pass. Both leave
the leaf single-linked. This is a code/API-based expectation, not a measured
Windows result. See [CreateFileW reparse flag and directory contract](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).
The count/identity fixture uses the documented
[handle information members](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/ns-fileapi-by_handle_file_information);
Linux offsets/masks follow the [kernel UAPI](https://github.com/torvalds/linux/blob/master/include/uapi/linux/stat.h).

### Fixture verification

F-1..F-5 are five unparameterized methods in `DirectoryLinkFixtureTests`, selected
on both OS lanes. A small per-test fake owner implements the existing
`IOwnedScriptProcess`; fake streams expose start/EOF/fault barriers and every
created task is released and awaited in `finally`. Use `FakeTimeProvider` to
advance the actual shared runner; no sleep-based timeout assertions. Options
are composed from the adapter's real 30/10-second defaults, replacing only the
owner/clock unless a case explicitly needs the native 5/2-second test allowance.
The result gate used by `TryCreateWindowsAsync` is internal and directly callable
without executing cmd; F-5 supplies real absent/ordinary paths. No fake can prove
Windows termination; F-6/F-7 supply that separate native evidence.

| ID | Exact method | Setup and decisive observations |
|---|---|---|
| F-1 | `DirectoryLinkFixtureTests.C1061_Command_budgets_cover_exit_and_both_pipes` | Inspect the real adapter options at `Fixture.ExecutionBudget` (30 seconds) and `Fixture.CleanupBudget` (10 seconds). Exercise root pending with both EOFs, root exited with only stdout held, and root exited with only stderr held. Both pump-start barriers must arrive before either is released (`Fixture.ConcurrentDrains`). Advance 29 seconds, complete the root where applicable, then one second; each invocation enters cleanup at the original deadline (`Fixture.OneDeadline`), without a fresh allowance for a pipe. Release/await all tasks, and assert termination/death confirmation precede return. |
| F-2 | `DirectoryLinkFixtureTests.C1061_Command_timeout_and_pipe_fault_remain_visible` | Through the async creation path and actual adapter, complete a timeout with clean owner teardown; require TimeoutException at `Fixture.TimeoutVisible`, never null. Separately fault each pipe while root and the opposite pipe remain pending; require the original IOException at `Fixture.PipeFaultVisible` without advancing the execution clock, plus terminate, confirm and drain/close observations. A caller timeout is only an outer test fail-safe. |
| F-3 | `DirectoryLinkFixtureTests.C1061_Command_cancellation_joins_before_return` | Pre-canceled token starts no owner; cancel after both pumps start and require cleanup to receive a fresh uncanceled token, then the original caller token in OperationCanceledException (`Fixture.CallerCancellation`). Record that termination, death confirmation and released pump completion happen before outward completion. Use a bounded fake-clock completion probe; if cancellation is lost, release the fake normally before making the failing assertion, so a mutant leaves no task behind. |
| F-4 | `DirectoryLinkFixtureTests.C1061_Command_cleanup_failure_is_not_null` | Root exit/EOF succeed, but fake death confirmation faults; async creation must throw IOException at `Fixture.CleanupVisible` and retain diagnostic owner paths. Separately hold termination/death/pump tasks and advance the single ten-second cleanup allowance; require failure, retained paths, and no successful result. Release/join fake tasks in finally, then remove only test-owned diagnostics. Also check primary timeout plus cleanup-fault diagnostics, without replacing the primary error. |
| F-5 | `DirectoryLinkFixtureTests.C1061_Command_result_requires_zero_exit_and_link` | Call the same completion gate as TryCreate with exit 37 (`Fixture.NonzeroRefused`), then exit 0 and a real ordinary directory (`Fixture.ReparseRequired`). Both return null. For the nonzero arm a per-call observation delegate records calls and would return true; assert null before asserting zero observations, so bypassing the exit gate fails the intended assertion rather than a fixture error. For zero, use actual attributes. The default observer is the existing IsLink function. F-6 is the positive real-junction companion. |
| F-6 | `DirectoryLinkFixtureWindowsTests.C1061_Junction_fixture_creates_moves_and_disposes_owned_link` | Native Windows/NTFS only; use real default TryCreate (synchronous compatibility path) and TryCreateWindowsAsync on fresh paths with spaces/Unicode. Observe nonnull link, ReparsePoint and target bytes in both. The async companion supplies per-call native owner hooks to observe normal exit, both EOFs and stopped retained handles. Move and dispose the junction; target sentinel and contents must survive. An existing ordinary link-name directory yields visible setup failure/null with its contents intact. Capture an intentional test-body sentinel exception and prove finally removed the junction before restoring/deleting scratch. Also inject a nonzero completion result after real, confirmed-clean creation, so no DirectoryLink is returned: the caller's partial-creation cleanup must remove only its reparse entry and preserve target bytes. No recursive traversal through a junction. |
| F-7 | `DirectoryLinkFixtureWindowsTests.C1061_Junction_command_joins_live_root_and_pipe_holders` | Call the actual DirectoryLinkCommand with per-call script/case/options overrides to run the existing `LiveRoot`, `ExitedStdout`, `ExitedStderr`, `HighVolume` and `Nonzero` fixtures sequentially. Use 5 seconds execution/2 seconds cleanup. The three held cases retain root/child/grandchild handles and cross the nonce/readiness barrier before releasing root exit; HighVolume/Nonzero retain the root only and need no tree-ready barrier. Require bounded TimeoutException for each held case, opposite EOF observed for each pipe-holder case, both output-end markers for HighVolume, and exit 37 plus both markers for Nonzero. All observed handles must signal and job active count reach zero **before** emergency cleanup; an independently owned outer-only sentinel stays live. Capture a root-only completed state while descendants still hold the selected pipe. Finish every invocation/watchdog/sentinel in finally. |

F-7's test-local wrapper follows the existing `Invocation` ownership pattern but
starts the new adapter, so invoking the old harness alone cannot satisfy this
row. Register root handles via `WindowsScriptHarnessHooks.Created` and retain
descendant handles before releasing the existing observed barrier. Use its
30-second independent watchdog per scenario and five-second emergency join;
never abandon an in-flight Task. If the shared runner reports unknown cleanup,
preserve its results/control directory and fail. The emergency observer may stop
only the exact retained fixture handles; its success is not the helper's verdict.
All normal success assertions are taken before that rescue. The generated native
receipts stay ignored. F-6 plus V-5/V-6 prove real mklink integration; F-7's command
substitution proves custody/pipe failure handling, not junction semantics.

The shared owner's independent internal guards are unchanged and retain their
prior-card PCs. New controls below mutate only the adapter/helper's defaults,
token/error propagation and result gates. Unsafe native custody mutants are not
introduced by this card. The fake owners make each new fixture PC safely red
without leaving a deliberately unowned OS child.

### Guards the regression

- R-1: keep the four inspected `ChannelOutboundStorageTests.C1059_Source_*`
  methods unchanged. `C1059_Source_reads_require_captured_roots_without_traversal`
  asserts allowed content, sibling-root refusal and traversal refusal;
  `C1059_Source_reads_reject_file_and_directory_links` asserts file symlink,
  parent symlink and symlink-as-root refusal;
  `C1059_Source_length_is_checked_before_reading_with_a_finite_budget` asserts
  equality-at-budget acceptance, oversize length, Linux growth length nine,
  and sparse-file length refusal before canceled reading;
  `C1059_Source_reads_refuse_nonregular_files_without_blocking` asserts regular
  success, directory refusal and Linux device/FIFO refusal. CP-2 executes all
  four. CP-4 excludes only the symlink method; its native Windows privilege
  prerequisite is not needed for the real junction witnesses.
- R-2: `ChannelOutboundStorageTests.Frozen_reply_and_input_bytes_survive_source_mutation`
  asserts original serialized reply and input bytes after source-array mutation,
  matching delivery ID, no temporary stage directories, and
  `InvalidDataException` after tampering with the stored reply. Both lanes run it.
  It is a compatibility regression, not proof of captured adoption; V-2 adds
  that missing ordinary companion explicitly.

### Guard inventory

This inventory covers CARD-1061's newly introduced or newly qualified product
guards and the new fixture adapter's safety boundaries. Existing R-1/R-2
contracts are retained compatibility tests, not renewed
mutation qualification of every pre-existing path, budget and snapshot guard.
That exclusion does not excuse any changed guard; a change there reopens this
inventory and the owning plan before Code can claim coverage.

| Guard | Plan reference and independently bypassable guard/invariant | Control |
|---|---|---|
| G-1 | D-1/D-2 Linux opened-leaf metadata reaches the count policy | PC-1 |
| G-2 | D-1/D-2 Windows opened-leaf information reaches the count policy | PC-2 |
| G-3 | D-2 hash entry uses the safe opener | PC-3 |
| G-4 | D-2 returned STATX_TYPE bit is required | PC-4 |
| G-5 | D-1 count zero is refused by the shared policy | PC-5 |
| G-6 | D-4 preflight rejects reparse components, including an allowed-root junction | PC-6 |
| G-7 | D-4 native Windows parent inspection rejects reparse attributes | PC-7 |
| G-8 | D-4 native Windows parent opens inspect the reparse object | PC-8 |
| G-9 | D-2 returned STATX_NLINK bit is independently required | PC-9 |
| G-10 | D-1/D-2 byte entry uses the safe opener | PC-10 |
| G-11 | D-1/D-2 text entry inherits guarded byte reading | PC-11 |
| G-12 | D-1 counts above one are refused by the shared policy | PC-12 |
| G-13 | D-2 extracted Linux metadata validator retains regular-mode rejection | PC-13 |
| G-14 | D-5a adapter execution deadline is finite and explicitly 30 seconds, not the harness default | PC-14 |
| G-15 | D-5a cleanup gets a separate finite ten-second total allowance | PC-15 |
| G-16 | D-5a owned-command timeout is observable, not converted into unsupported/null | PC-16 |
| G-17 | D-5a caller cancellation reaches the owned runner | PC-17 |
| G-18 | D-5a owned-command I/O and cleanup uncertainty escape the legacy IOException catch | PC-18 |
| G-19 | D-5a nonzero command exit cannot produce a successful fixture | PC-19 |
| G-20 | D-5a exit zero alone is insufficient without the actual reparse observation | PC-20 |

No row combines independently bypassable type/link availability predicates or
Windows parent flag/attribute guards. None is declared unnecessary. The repair
adds seven adapter guards; concurrent drains, common clock accounting, private
job assignment/termination and native death confirmation belong to the unchanged
shared owner. F-1..F-4/F-7 exercise their use here; their existing guard PCs are
not duplicated or claimed newly mutation-qualified by this card. Any change to
that shared owner invalidates this exclusion and returns to Plan.

### Positive controls

Each row changes only the named product or fixture-adapter guard, leaving the test and its
fixture observations intact. Filters in this table are exact methods; do not
substitute the ordinary checkpoint class filters. The original PC-4 that
removed the entire mask check is replaced by PC-4 and PC-9 so neither bit hides
the absence of the other. All changes below are compiling defects after S1-S3.

| PC / guard / lane | Break by | Exact method filter | Required red assertion |
|---|---|---|---|
| PC-1 / G-1 / Linux | In the Linux native call to the production metadata validator, pass `1u` as the link count instead of `stat.Links`; keep mask and mode unchanged. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Byte_and_text_reads_reject_hard_links` | `Links.BytesRefused` for the outside two-link fixture: expected InvalidDataException absent. |
| PC-2 / G-2 / Windows | Bypass only `RequireSingleLinkCount(info.Links)` in the Windows leaf branch. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Byte_and_text_reads_reject_hard_links` | `Links.BytesRefused` on the real NTFS two-link fixture: expected exception absent. |
| PC-3 / G-3 / Linux | In `HashFileAsync` only, replace `OpenRegularFile(path)` with `new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)`; retain ValidatePath. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Hashes_reject_hard_links` | `Links.HashRefused`: hash incorrectly returned for two links. |
| PC-4 / G-4 / Linux | Weaken the result-mask predicate from requiring `0x5` to requiring only `0x4`; preserve count and mode gates. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Linux_metadata_requires_type_and_link_count` | `Metadata.TypeRequired`: mask 4, regular mode, count one incorrectly accepted. |
| PC-5 / G-5 / Linux | Change the shared rejection from `links != 1` to `links > 1`. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Zero_link_count_is_rejected` | `Count.ZeroRefused`: zero incorrectly accepted. |
| PC-6 / G-6 / Windows | Remove only ReparsePoint from ValidatePath's component rejection mask; retain Device and final Directory checks. | `/*/*/ChannelReplyAttachmentReaderWindowsTests/C1061_Preexisting_junction_is_refused_before_open` | `Junction.PreflightHookZero`: hook count is one despite a later native refusal. |
| PC-7 / G-7 / Windows | Remove only `(info.Attributes & 0x400) != 0` from the native parent predicate. | `/*/*/ChannelReplyAttachmentReaderWindowsTests/C1061_Junction_after_validation_is_refused_by_native_open` | `Junction.NativeRefused`: no unsafe-directory exception, outside bytes returned. |
| PC-8 / G-8 / Windows | Remove only `0x00200000` from parent OpenWindows flags; retain BACKUP_SEMANTICS and sharing flags. | `/*/*/ChannelReplyAttachmentReaderWindowsTests/C1061_Junction_after_validation_is_refused_by_native_open` | `Junction.NativeRefused`: followed target passes attributes and outside bytes returned. |
| PC-9 / G-9 / Linux | Weaken the result-mask predicate from requiring `0x5` to requiring only `0x1`; preserve count and mode gates. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Linux_metadata_requires_type_and_link_count` | `Metadata.LinksRequired`: mask 1 with fabricated count one incorrectly accepted. |
| PC-10 / G-10 / Linux | In `ReadAttachmentAsync` only, replace the safe opener with the ordinary FileStream expression used in PC-3; retain path validation and budgets. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Byte_and_text_reads_reject_hard_links` | `Links.BytesRefused`: native enforcement is bypassed for bytes. |
| PC-11 / G-11 / Linux | Replace `ReadTextAsync`'s body with `return await File.ReadAllTextAsync(path, ct);`. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Byte_and_text_reads_reject_hard_links` | `Links.TextRefused`: bytes still refuse, text incorrectly returns fixture contents. |
| PC-12 / G-12 / Linux | Change the shared rejection from `links != 1` to `links == 0`. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Zero_link_count_is_rejected` | `Count.MultipleRefused`: zero still refuses and one accepts, but two incorrectly accepts. |
| PC-13 / G-13 / Linux | Remove only the regular-mode predicate from the pure Linux metadata validator. | `/*/*/ChannelReplyAttachmentReaderTests/C1061_Linux_metadata_requires_type_and_link_count` | `Metadata.RegularRequired`: mask 5, count one, directory mode incorrectly accepted. |
| PC-14 / G-14 / Linux | Change DirectoryLinkCommand's execution budget from 30 to 300 seconds. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_budgets_cover_exit_and_both_pipes` | `Fixture.ExecutionBudget`: actual options exceed the 30-second contract. This is an assertion failure before any fake/native work, not a watchdog timeout. |
| PC-15 / G-15 / Linux | Change only DirectoryLinkCommand's cleanup budget from 10 to 300 seconds. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_budgets_cover_exit_and_both_pipes` | `Fixture.CleanupBudget`: actual options exceed the ten-second cleanup contract. |
| PC-16 / G-16 / Linux | In TryCreateWindowsAsync, catch TimeoutException from the owned adapter and return null. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_timeout_and_pipe_fault_remain_visible` | `Fixture.TimeoutVisible`: required TimeoutException is absent after the fake owner has been fully joined. |
| PC-17 / G-17 / Linux | Pass CancellationToken.None instead of the caller token to ScriptHarnessProcess.RunAsync in DirectoryLinkCommand. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_cancellation_joins_before_return` | `Fixture.CallerCancellation`: no caller-token OperationCanceledException; bounded test probe then releases/joins the fake before asserting. |
| PC-18 / G-18 / Linux | Widen TryCreateWindowsAsync's legacy IOException catch to include the owned adapter invocation and return null. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_cleanup_failure_is_not_null` | `Fixture.CleanupVisible`: fake death-confirmation failure becomes null instead of an IOException carrying retained-path diagnostics. |
| PC-19 / G-19 / Linux | Remove only the nonzero-exit refusal from DirectoryLink's shared completion gate. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_result_requires_zero_exit_and_link` | `Fixture.NonzeroRefused`: exit 37 plus the would-be-positive observation incorrectly returns a DirectoryLink. |
| PC-20 / G-20 / Linux | Replace only the completion gate's IsLink/observation predicate with true. | `/*/*/DirectoryLinkFixtureTests/C1061_Command_result_requires_zero_exit_and_link` | `Fixture.ReparseRequired`: exit zero on an ordinary directory incorrectly returns a DirectoryLink. |

Mutation runs baseline green, break/red, exact restoration and fresh-build green
**after land**. Code runs V/R; ordinary Review judges this design and ordinary
evidence before land. PC-1..13 share the reader file; PC-14..20 share the helper
and adapter. Keep all twenty serial within each OS lane, no combined mutants.
Each phase uses the listed filter and
`-MinExecuted 1`, its own alternate `bin-` output and fresh results under the
assigned external SourceLanding evidence root. Use the owner's copied
`run-checkpoint.ps1` driver with its slot gate; refresh restored source timestamps.
No snapshot commits, remote executor access or automatic latest-master rerun.
Retain build success, actual method, exact failing assertion and restored clean
source evidence per PC. Wrong exception, setup failure, missing fixture,
timeout, skip, build error or zero tests is not the specified red.

Audit: **guards=20 (13 product + 7 fixture), mapped=20, missing=0,
duplicate PC maps=0**. All **20 controls are design-executable** after their
assigned S1-S3 implementation. PC-6..8 now have the explicit owned-junction
setup and F-6/F-7 proof; PC-14..20 use bounded fake-owner witnesses rather than
unsafe OS custody mutations. None has run. No further product choice or
verification-design dispatch is needed before Code.

### Out of scope

- Continuous link exclusivity, inode provenance, adversarial changes after the
  native metadata observation, and ownership/snapshot redesign: excluded by D-3.
- Exhaustive pre-existing reader defenses (Linux descriptor-walk flags, Windows
  delete sharing/leaf reparse flags, absolute-path/device checks), converted-output
  sealing, and storage/queue guard mutation qualification: unchanged by D-6.
  R-1/R-2 retain their existing assertions and prior-card obligations; this card
  cannot claim their full mutation or race coverage. The regular-mode check
  moved by D-2 is specifically included as G-13.
- Windows symbolic-link privileges and non-NTFS/network filesystems: qualification
  targets real NTFS hard links/junctions. Native fixture failure is visible and
  leaves the Windows lane incomplete; Linux, WSL, a fake attribute or a skip
  cannot substitute for it.
- Real broker/provider/session delivery, DB recovery and busy recipients: no
  changed handoff; local file evidence does not establish delivery.
- Whole Unit, namespace, assembly, provider or E2E runs: excluded by the brief.
  No additional repetition after green without a changed source or new concern.
- Shared ScriptHarnessProcess/WindowsScriptHarnessProcess implementation and
  unrelated DirectoryLinkHelper variants: unchanged. This card repairs the
  selected DirectoryLink Windows entry and tests its integration with the
  existing process owner, not all repository process-launch helpers.

### Checkpoints

This is the sole importable manifest. All S1-S3 slices must be committed/pushed
before the four-row run so Linux and Windows certify the **same final candidate
SHA**. The fixture methods join CP-1/CP-3, preserving exactly four narrow rows.
Running Linux only at S1 would require another Linux qualification after S3;
do not present that earlier result as final-source evidence.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c1061-linux/` | linux-link-policy-and-fixture | `/*/*/(ChannelReplyAttachmentReaderTests*)\|(DirectoryLinkFixtureTests*)/*` | V-1, V-2, V-3, V-4, F-1, F-2, F-3, F-4, F-5 | exactly 9 listed methods, 0 failed/skipped | 9 | 10 |
| CP-2 | S1-S3 | CP-1 | linux-reader-regressions | `/*/*/ChannelOutboundStorageTests/(C1059_Source_*)\|(Frozen_reply_and_input_bytes_survive_source_mutation*)` | R-1, R-2 | exactly 5 listed methods, 0 failed/skipped | 5 | 2 |
| CP-3 | S1-S3 | `tests/Antiphon.Tests -> bin-c1061-windows/` | windows-native-links-and-fixture | `/*/*/(ChannelReplyAttachmentReaderTests*)\|(ChannelReplyAttachmentReaderWindowsTests*)\|(DirectoryLinkFixtureTests*)\|(DirectoryLinkFixtureWindowsTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-6, F-1, F-2, F-3, F-4, F-5, F-6, F-7 | exactly 13 listed methods, 0 failed/skipped | 13 | 14 |
| CP-4 | S1-S3 | CP-3 | windows-reader-regressions | `/*/*/ChannelOutboundStorageTests/(C1059_Source_reads_require_captured_roots_without_traversal*)\|(C1059_Source_length_is_checked_before_reading_with_a_finite_budget*)\|(C1059_Source_reads_refuse_nonregular_files_without_blocking*)\|(Frozen_reply_and_input_bytes_survive_source_mutation*)` | R-1 Windows-applicable methods, R-2 | exactly 4 listed methods, 0 failed/skipped | 4 | 2 |

Min means TUnit executions: Linux 9+5=14, Windows 13+4=17, total **31**, with
**18 distinct methods** (six reader, seven fixture, five regression). F-7's five
sequential scenarios still contribute one TUnit execution. CP-2 reuses CP-1's
one isolated build; CP-4 reuses CP-3's.
Each row has exactly one filter and one identified build. These are counts,
not assertion-loop iterations or minutes. Inspect the actual executed names
and zero skips; the importer derives roster tokens from Filter, not from
the prose `exactly` in Expect.

Run one checkpoint-tool group on Linux with `--rows CP-1,CP-2`, one on native
Windows with `--rows CP-3,CP-4`, both with `--expected-source-sha` set to the
same complete committed candidate SHA. Pass this plan to `run --plan`, keep
the source frozen and await all `wait` exits until not 75. Use the host slot
wrapper for the tool launcher; each driver owns its own slot. Do not nest
`run-checkpoint.ps1` behind another slot wrapper. Never run the whole table
on one OS. Read GET `/api/runner-defaults` and GET `/api/session-runners` when
commissioning the OS lanes. Omit `-Runner`; set `-Platform Linux` for CP-1/2 and
`-Platform Windows` for CP-3/4. Planning/contract authoring needs no OS pin;
`-Platform Any` clears an inherited pin. Group names name lanes; the importer
does not choose hosts or automatically exclude wrong-OS rows. No host dispatch
is done here.

Ordinary Review requires all four source-qualified receipts for that same SHA,
`dirty=0`, stable clean source, verified build provenance, exact roster and
zero failed/skipped. Preserve unedited CHECKPOINT lines in the stored report;
generated logs/TRX/JSON stay ignored. Run the evidence-diff guard over the full
task range. A later source change requires affected rows at the new candidate;
do not relabel receipts. Clean only each run's exact owned output inventory
after its children finish. Slot refusal/timeout never authorizes an unleased
retry. Baseline any suspected inherited failure with its exact failing method.

### Cost

All figures are **estimates**, not measurements. Fixture authoring, ordinary
witnesses and controls are now included, rather than an unpriced setup seam.

- Ordinary V/R floor (Code): **28 minutes** = CP-1 10 + CP-2 2 (Linux filters
  above) + CP-3 14 + CP-4 2 (native Windows filters above). This includes two
  isolated builds at 5 minutes each and 18 minutes of selected execution/receipt
  handling. Of the six-minute increase from TestDesign's 22-minute floor,
  two minutes cover the five contract witnesses on each host (four total), and
  two cover native F-6/F-7. One minute per host for tool setup gives **30 minutes**
  setup/build/ordinary qualification. Fixture timeouts are short intentional
  cases within those allowances, not new long-running suite runs.
- Implementation authoring/self-review: S1 **30-40**, S2 **40-60**, and S3 native
  authoring **10-30** minutes, total **80-130**. S3 also owns the 30-minute
  qualification allowance, so the three slices are **30-40, 40-60, 40-60 minutes**
  respectively. Complete Code commission: **110-160 minutes**. No slice exceeds
  60 minutes by design; report observed overruns rather than dropping coverage.
- PC phase floor (Mutation): **180 minutes** = 20 controls x (3-minute baseline
  build/test + 3-minute mutated build/test + 3-minute restored build/test),
  **60 exact-method invocations**. Linux PC-1,3,4,5,9..20 are sixteen controls
  costing **144** minutes; Windows PC-2,6,7,8 are four costing **36**. Each uses
  its Positive controls table filter with MinExecuted 1. All three phases are
  retained even where two PCs use the same method. Allocate **25 minutes** for
  two-host setup, discovery, edits/restoration and evidence: full Mutation
  commissioning floor **205 minutes**.
- Combined verification floor: **235 minutes** = Code setup 2 + ordinary builds
  10 + ordinary execution/receipts 18 + PC phases 180 + Mutation handling 25.
  Including authoring/self-review gives **315-365 minutes**; separate ordinary
  Review dispatch effort is not estimated here. The seven fixture controls add
  **63 PC minutes**, with five more handling minutes, to the prior 137-minute
  Mutation commission. No blocked or zero-cost controls remain in the floor.
- Savings: two reused builds save **10 estimated minutes** against four separate
  five-minute builds. Final-candidate qualification avoids a second CP-1/2 run
  solely for a later source SHA, saving **12 minutes** under this roster. Shared
  custody reuse avoids implementing another native process owner, but no measured
  time saving is claimed for it. No whole-Unit timing/saving is claimed.
