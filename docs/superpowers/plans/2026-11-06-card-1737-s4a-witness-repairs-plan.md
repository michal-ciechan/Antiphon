# CARD-1737: S4a preservation witnesses, dispositions and repair slices

Date: 2026-11-06. Plan task `85a330bf`, branch `feat/card-task-85a330bf`.
Source inspected: **10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9**.

**Outcome:** dispose all 48 investigation rows, design independent preservation
witnesses for 13 of them, and assign those repairs to four portable Code slices.
The 13 grouped rows expand to 19 proposed methods/control selectors and 23 test
results. Twenty-seven rows close as redundant or non-preservation bindings;
three need ledger corrections; three remain Windows qualification work; two
need a separately commissioned design/control decision. Closure of a binding
does not remove a production guard or award a mutation kill.

TestDesign is a **separate next stage**, beginning with **R1 and R2 in parallel**.
This dispatch does not fold TestDesign into Plan. The designs and checkpoint
rosters below are its input, not executed evidence. There were no builds, tests,
mutations, production/test edits, desktop commands, tracker writes or activation
in this Plan. The optional measuring run was not needed. Only this plan is to be
committed. No S4a enablement or cleanup is authorized by it.

## Evidence and ground truth

The primary input is the entire investigation at
`e10e6f30073a247c248c413d62a4ea0c977c8d48:docs/investigations/2026-11-06-investigate-s4a-witness-gaps.md`
(task f565efa1). Its P/A1/B1/C1/D1/A2/B2/C2/D2 provenance table remains the
historical ledger; this plan changes the **dispositions**, not those receipts.
The original sweep inventory is
`0037ffbd94ab6d42074d77b31f90015f43ea6a34:docs/investigations/2026-11-06-investigate-s4a-mutation-sweep-plan.md`.
The performance input is
`c318d8edfc97e046bb9a332ec34c76973f9a4f4a:docs/investigations/2026-11-05-debug-s5b-1-performance.md`
(task 5ae98d31), especially P3-P7. Packed-build precedents are B2
`58fd77827bc31f00d6abb540e8b341fbd614c5c3` and C2
`d4fcc1422a3e61f510168320a38af75b9bc6626a`, at the report paths in the investigation.
The format follows [the S5b plan](2026-11-05-card-0824-s5b-ref-only-plan.md).

Owners applied: [project context](../../project-context.md),
[orchestration](../../orchestration-loop.md), [HTTP operations](../../ops-http.md),
[testing/build](../../testing-and-build.md), and
[performance standards](../../test-performance-standards.md). Source anchors
below are at the inspected SHA; `Git/` means `server/Infrastructure/Git/` and
`Services/` means `server/Application/Services/`.

| Card/investigation assumption | What the code/evidence actually establishes | Planning consequence |
|---|---|---|
| Forty-eight gaps mean 48 missing tests or independent mutants. | Several rows group multiple arms; others have existing candidates or no current control. Investigation preserves 15 completed waived kills and 48 gap rows separately. | Keep row identities; expand only designed controls. Do not recalculate historical kill totals from this plan. |
| X-26 can be killed on preservation by a better detached-HEAD fixture. | `Git/LandingGit.cs:1335-1336`: exit 1 first returns `detached_head`, then the general unsuccessful-result guard returns `symbolic_head_error`. `Succeeded` is exit 0. | Narrow X-26 is diagnostic-only. No legal exit-1 fixture satisfies the second guard. |
| A24 still guards deletion with a length seal. | `ContentSeal` is retained in `WorktreeSetAsideRecord`; `CustodyOf` and `RemovalApproval` use identities, names, kinds and hashes. There is no current `ContentSeal` read in server/test source. | Retire the old seal-length binding; retain hash and identity controls. |
| U3 has no failed verification-read witness. | SAL `FailVerifyAfterWrite` is used by `Aged_retry_keeps_its_salvage_ref_for_a_retention_after_completion` and `Unfinished_release_keeps_its_salvage_ref_past_the_retention`. | Mark those candidates present/unrun; repair only successful verification missing a required ref. |
| A replacement directory necessarily differs in content. | `GuardedWorktreeRemoval.CustodyOf` compares directory identity before comparing the entry manifest. The delete's own census does not compare the receipt's old root identity. | An equal-content, different-identity replacement isolates U9 without bypassing content guards. |
| A23 needs a probabilistic writer racing the whole walk. | `WorktreeNoFollowDelete.UnlinkSameNode` accepts a real post-hash stat and compares it with a fresh stat before unlink. `HashAt` is a separate private method. | A test-local reflection adapter can exercise that boundary deterministically with real descriptors and bytes; no production hook is needed. This is a helper-boundary witness. |
| All switch checks are independently destructive fences. | Outer checks precede inner live callbacks at unregister, child start or send. B11's resumed delete has no later live callback; B10's final cursor return can be the last callback before that delete. | Repair B11/B10; close repeated outer checks as independent preservation obligations while retaining them in production. |
| Native sharing retries necessarily re-read Execute. | `DeleteSettled` at `WorktreeNoFollowDelete.cs:65-77` retries after `Pause`; its signature has no authorization callback. `OpenNoFollow` also sleeps/retries. | U8 is a product semantics/design question, not a missing mutant kill. No unmutated runtime failure is claimed here. |
| A null monitor cannot be constructed. | `TaskWorktreeRetirementService` explicitly accepts optional `legacyRelease=null` and documents fail-closed behavior; `ReadLegacySwitch` checks null. Registered DI normally supplies it. | Test the supported optional-constructor boundary with real removal dependencies, without claiming a production DI misconfiguration. |
| Static salvage collisions discriminate atomic write flags. | The enumeration and symbolic probe precede `update-ref`; a collision introduced at the command boundary is not in either earlier observation. | Late direct/dangling-symbolic collisions isolate create-only CAS and no-deref respectively. |
| Status doubles prove the stderr producer. | `LandingGit.ExecuteProcessAsync` produces `WroteStandardError`; the new working-tree in-process path can avoid it. | Run an actual child through the base producer with `AnswerWorkingTreeReadsInProcess=false`; do not manufacture the Boolean. |
| Native receipt/walker categories already have an executable roster. | Existing NF race tests mostly skip on Windows; Native has five methods/eight argument results, and its short-name case can use a synthetic fallback. | Name native methods below, distinguish new from existing, and withhold real 8.3 credit from that fallback. |
| Four ninety-minute tasks will finish everything. | No repair-effort measurement exists. Shared files, 19 portable selectors, native work and U8 remain. Prior two-selector sweeps measured execution, not authoring. | Four portable ownership slices need larger budgets. Five slices including the bounded native authoring follow-up; U8 is additional and unestimated. |

Placement was read through **GET /api/runner-defaults** and
**GET /api/session-runners** on 2026-11-06. Defaults revision 2 had no kind
overrides; its global default resolved to the eligible Linux runner. The catalogue
also exposed an eligible Windows runner (2 seats, 0 occupied), Linux (10 seats,
8 occupied), and an ineligible retired peer. These are transient observations,
not dispatch constants. Refresh both routes and effective concurrency before
dispatch. Portable slices omit `-Runner` and `-Platform`; Linux syscall/permission
rows require `-Platform Linux`; native acceptance requires `-Platform Windows`.
No host is pinned and no fleet location is embedded. **This commission runs
nothing on the desktop**; the Windows rows are queued for the separately
authorized Windows rc run. `-Platform Any` explicitly unpins an inherited OS.

## Decisions

- **D-1 — Preservation is the primary oracle.** Check actual surviving bytes,
  directory identity, Git registration/ref OID or absence of a foreign write
  before checking reason text. A changed reason with the same retained object
  is not a preservation kill. Check fixture activation separately and unwrap
  reflection exceptions; a harness exception is not the desired red.
- **D-2 — Keep production fences intact.** Test-only adapters may enter a smaller
  real boundary, or schedule a real change after an earlier proof. They may not
  fabricate contradictory successful Git outcomes to overcome a second guard,
  disable a database constraint, or remove a second production fence. Reject
  multi-guard bypasses as evidence for an individual guard. Previously defined
  compound controls keep their original scope and history.
- **D-3 — Close only the stated binding.** Category b below means no repair work
  for that independent preservation claim. It is not a proof of whole-product
  equivalence, permission to delete the guard, or erasure of an earlier survivor.
  Liveness/resource/diagnostic coverage is explicitly outside this repair batch.
- **D-4 — Prefer deterministic boundaries over timing loops.** Use existing
  `BeforeCommand`, `AfterCommand`, registration and journal seams; for A23 use a
  test-local reflection adapter over real native handles. Reject large files,
  sleeps, repeated inode allocation and probabilistic inotify scheduling for
  the new final-stat witness. Existing Linux race tests retain their own claims.
- **D-5 — Four portable file owners.** R1 owns PC/NF/GC, R2 then R3 own SAL, R4
  owns SW/LP. Keep helpers in those files. No changes to DesktopLegacyWorld,
  LandingGitFixture, LandingSafetyHarness, shared DI, schema, production source
  or project files are assumed. If one is necessary, stop that slice's scope
  expansion for TestDesign revision and serialize affected consumers.
- **D-6 — Author-side red is explicitly required here.** The brief commissions
  selector-packed, method-scoped witness discrimination in each Code slice.
  Temporary mutation source is restored and never committed. This is additional
  author evidence; it does not replace ordinary Review or post-land SourceLanding
  Mutation. No historical waived sweep becomes SourceLanding evidence.
- **D-7 — Do not use pending choices to block independent repairs.** R1-R4 do
  not depend on resolving U8 or selecting U10. Their dispositions below reserve
  work rather than choose new product behavior under an assumed default. No
  operator answer is needed to begin their separate TestDesign stages.
- **D-8 — No enablement claim.** Ordinary Code, Review, landing and native
  qualification remain separate. Preserve existing effective safety settings;
  this plan performs no settings write and supplies no automatic cleanup gate.

Rejected alternatives: repeating the old masked schedules; removing the second
guard to make the first mutant look destructive; treating exception/timeout/zero
tests as a kill; pooling SAL edits in parallel branches; class-wide positive
controls; modifying shared fixtures without a consumer selection; and declaring
native success from Linux or synthetic 8.3 paths.

## Disposition ledger: all 48 rows

**a** = feasible test-only preservation repair, specified below. **b** = close
this preservation binding as redundant, diagnostic, or outside preservation;
no repair. **c** = correct stale/missing classification, no invented kill.
**d** = separately decide product semantics or define the control. **e** =
Windows-native qualification; queue, with no portable acceptance credit.
Mixed U rows retain their sub-dispositions explicitly. Counts use the primary
category, exactly once per investigation row.

| # | Investigation row | Disposition | Reason and exact follow-through |
|---|---|---|---|
| 01 | U9 deletion-time identity | a / R1 | Equal-content replacement in the pending receipt path satisfies manifest/hash checks. `W-U9` proves the foreign directory is kept. Restore A11 remains separate. |
| 02 | follow-links | b | `WorktreePathLimit` only inspects length. The settled remover refuses links earlier and the no-follow deleter remains later. Direct walker traversal is a read/eligibility defect, not an independently demonstrated delete. Keep its no-follow code; no invented deletion test. |
| 03 | Native receipt/walker: identity, per-entry drift, listing, restore | e / N1 | Use the exact N-ID/N-ENTRY/N-LIST/N-RESTORE roster below. New boundary tests prove changed-byte and unapproved-name preservation at the per-entry check. The earlier listing refusal is b as an independent new-name preservation binding because per-entry rejection remains; no isolated listing kill is claimed. |
| 04 | U8 late native sharing retry | d / O-1 | There is no live read inside the existing retry loops to mutate away. Decide authorization across an already-started native walk, then design any required product fix. Native locked-file resume is not this witness. |
| 05 | PC-68/N-3 newer-guard interaction | e / N1 | Run all three current alias arguments, requiring a real alias for short-name. Record which guard is reached. If canonical/leaf/reparse checks stop the mutant, retain layered/native coverage, not an independent PC-68 kill. |
| 06 | M-7 | a / R1 | Advance the actual branch between the current-OID read and update-ref delete. `W-M7` asserts the newer OID survives an isolated expected-old omission. |
| 07 | U1 HEAD-only and branch-reflog-only sources | a / R2 | Two source-exclusive real reflog worlds; omit only that source's enumeration tuple, including its associated raw-log read. Neither other reflog nor current HEAD reaches the lost commit. |
| 08 | U3 successful verify with missing written ref | a / R2 | Remove one just-created salvage ref before the successful verification enumeration; the source reflog still owns the lost commit. `W-U3` asserts the source tree/registration/ref stay. Failed-read candidates already exist: c correction, not new repair or kill credit. |
| 09 | U4 same-OID, isolated --no-deref and create-only CAS | a / R3 | Two late-collision controls isolate the write flags (`W-U4C`, `W-U4N`). Same-OID direct skip is b: an idempotence/extra-refusal binding, not data loss; include as a green control inside the CAS method, not another mutant. |
| 10 | U5 raw reflog read/parse refusals | a / R2 | Four methods, seven results cover inaccessible, malformed fields/old/new IDs, missing-after-list and empty/all-zero-after-list. Real file faults occur after the earlier Git listing. Preserve uncertain source custody; do not claim a missing file was deleted by the mutant. |
| 11 | U6 prune name/OID binding | a / R3 | Old direct salvage ref with a valid name suffix different from its actual valid OID. Only the name/OID equality is bypassed; real ref deletion is the red. |
| 12 | U6 unreadable/malformed ref time | a / R3 | Separate unsuccessful-read and parse controls, with all other prune predicates satisfied. Require the real ref/OID to remain; parseable stdout on a failed read discriminates success checking. |
| 13 | S10 symbolic-collision binding | b | Static symbolic collision is rejected by the explicit probe before the old compound write revert. Close that masked binding; keep S10 direct pending and S12 whole-probe kill history. Late no-deref control is U4N, not a relabelled S10 kill. |
| 14 | S12 narrow ==0-only variant | b | Exit 0 still fails `ExitCode != 1`. Diagnostic distinction only. Do not remove that second check or relabel the completed whole-loop S12 control. |
| 15 | PC-28 | b | Service remote read and final guarded containment remain fail-closed. If the remote recovers, later genuine containment authorizes only published work. Early policy read remains useful prefiling/diagnostic protection, not an independent deletion kill here. |
| 16 | PC-32r | b | Current own-branch destination read/choice at `RequireUniqueOrdinaryOwnerAsync` is followed by real destination containment in `AuthorityAsync`. Falling back to a target either proves containment there or refuses. Keep the historical missing-branch survivor; do not bypass policy and containment to credit this read alone. |
| 17 | X-26 | b | Explicit final disposition: reason-only, no acceptance kill for deleting only the exit-1 return. General unsuccessful-exit guard must stay. Retain detached-HEAD preservation regression and stored reason-only red. |
| 18 | A23-final-stat | a / R1 | Call the real post-hash/pre-unlink boundary with genuine captured stat, then changed same-inode bytes. `W-A23` isolates the final content stat; it does not close the documented stat-to-unlink/coarse-clock window. |
| 19 | A24-seal-length | c | Remove from active mutation obligations: `ContentSeal` is historical storage, not custody authority. Keep the old anchor and correction; do not transplant its ID onto a hash comparison. |
| 20 | A2 | b | A valid receipt binds evidence bytes both in the pin and entry manifest. Removing source-pin comparison alone leaves manifest/per-entry hashing. An inconsistent hand-made receipt is not an independent real-world loss witness. Keep the diagnostic and manifest guards. |
| 21 | A8 | b | The final nonempty check overlaps the kernel's empty-directory-only rmdir. Removing it cannot recursively delete newly added children. Keep reason/remaining-directory checks; no forced rmdir in the fixture. |
| 22 | skip-unreadable | b | Read-only path-limit census has earlier status/content checks and later actual filesystem opens. A direct inaccessible-walker test would prove refusal/eligibility, not independent byte deletion. No permission trick that silently alters the downstream deleter. |
| 23 | hold-english-phrase | a / R4 | Rebind to the current `ExecuteProcessAsync` stderr producer. A real process emits non-English stderr with empty stdout/exit 0; all other removal proofs are valid. A language-specific producer mutant causes tree deletion (`W-WARN`). |
| 24 | retry-skip-recheck | c | Old request-flag binding no longer exists at the current request constructor. Mark historical control obsolete, retaining the existing compound T-D-4 kill. Any new retry/status control must name a current site; do not call a switch test its replacement. |
| 25 | PC-67/N-2 | e / N1 | Run the three named long-path methods with current path limit intact. The static long-path case now refuses before move/unregister. No PC-67 order kill from that case; qualify actual order separately only with a reachable native fault schedule. |
| 26 | B11-resume | a / R4 | Turn Execute off only after admitted resumed removal's registration await, with the receipt content unchanged. No later live guard precedes `DeleteApproved`. `W-B11` checks the aside's bytes before reason. |
| 27 | B12-branchouter | b | Inner destructive-child callback still checks live authorization before update-ref. Keep B18's completed child-start kill separate; no toggling back on between reads to invent an unauthorized delete. |
| 28 | B14-describe | b | beforeSend/queued-send callback still stops WorkspaceRemove. B19 remains the independent send control. A fake sender that omits its callback would change the system under test. |
| 29 | B16-unregisterouter | b | Inner unregister gate remains after its awaits. Preserve outer early-restoration behavior and B17's separate inner kill; no independent byte-loss obligation for this duplicate read. |
| 30 | B23-ref-admission | b | Initially-off or late-off state is still rejected by the continuation callback. Avoiding description/read work is a separate admission-order property; do not claim mirror loss by bypassing that callback. |
| 31 | B24-mirror-outer | b | Later description and send callbacks retain mirror custody. Close outer independent-send attribution; preserve existing no-send regression. |
| 32 | B6-precommand | b | Avoiding intent consumption is observable bookkeeping/liveness, but later reads still preserve the tree. Outside this preservation repair scope; keep existing intent semantics and historical schedule mismatch. |
| 33 | B7-postintent | b | Recorded 2/2 survivor stays recorded. Pre-move authorization still refuses; this guard's omission alone has not authorized a delete. |
| 34 | B8-preinsert | b | Post-save switch check rolls the transaction back. No extra durable release authority without another fault. B9's distinct post-save binding remains pending. |
| 35 | B10-postcursor | a / R4 | Exercise the final resumed-delete callback, not early admission: disable during its cursor query and leave it disabled. Removing only the final switch return then reaches deletion (`W-B10`). |
| 36 | B21-ref-stale | c | Correct from presumed missing to candidate-present/unvalidated. Preserve SW `Late_switch_on_ref_only_mirror_continuation_sends_no_remove` as the existing candidate. No repair or kill is inferred from inspection. |
| 37 | U7 missing monitor | a / R4 | Explicit optional constructor path with null monitor, valid persisted system release and real remover. A null-branch fail-open mutant permits deletion. This validates API fallback, not a claim that ordinary DI omits the monitor. |
| 38 | M-2 late-path-shared argument | b | Later ownership query refuses `identity_ambiguous`. Close only this argument's independent-loss claim; retain M-2's three destructive argument kills and reason-only argument history. |
| 39 | PC-49 | b | Row lock plus partial unique active-release index still enforce one active row. Constraint/SQL failure is not preservation red. Keep concurrent/idempotent tests; do not drop the index in the fixture. |
| 40 | PC-53 | b | Existing absent-source schedule cannot isolate the later no-move check. Reopening bookkeeping alone is not a delete while receipt/identity guards remain. Close preservation attribution, not recovery/idempotence coverage. |
| 41 | X-3 | b | Removal re-proves HEAD and refuses newer work. Keep downstream source_changed preservation; no control that suppresses both proofs is credited to the reopen check alone. |
| 42 | U2 256-candidate / 64-tip bounds | b | Resource/eligibility bounds. Removing caps can do more salvage work but does not itself discard roots. No combined cap-plus-salvage bypass. |
| 43 | U6 200-ref prune cap | b | Per-pass resource bound; other ownership/age/completion guards still apply. No standalone preservation mutant is owed. |
| 44 | M-1 | b | Backoff identity is liveness. The old varchar(80) failure stays invalid as behavioral red. No repair in this preservation batch. |
| 45 | length-guard-on-linux | b | Enabling Windows length refusal on Linux fails closed. A Linux factory/eligibility test could be useful separately; Windows rc cannot qualify that branch. |
| 46 | refuse-after-slot | b | The missing observation is slot expenditure, not deleted bytes. Preserve ordering in production; close only the deletion-witness claim. |
| 47 | wse-default-true | b | Explicit initializers dominate known consumers and the proposed default over-refuses. No reachable fail-open deletion or missing independent preservation witness is established. |
| 48 | U10 unspecified “one of your own” | d / O-2 | No ID/site/defect/test exists. Operator may commission a concrete additional control or close this placeholder; neither outcome earns a kill. |

Ledger checksum: **13 a + 27 b + 3 c + 2 d + 3 e = 48**. U3's existing
failed-read subcase and U4's same-OID subcase do not add rows to that denominator.
The 63 unselected historical selector rows/67 nominal bindings, A18's three
methods, S8 unfinished, S10 direct, A13 availability, and other existing candidates
are not silently added to this repair scope. Their mutation obligations remain
on the original ledger. G05/G07 stay on their separate cards.

## Test-only witness designs

Every method name below is **proposed**, except where explicitly called existing.
Names begin `C1737_` so TestDesign can validate exact ownership and suffix matches.
No method may be claimed done until its selector-off baseline, intended preservation
red and restored-source green have nonzero exact-method results. Existing tests
continue asserting diagnostic reasons after the preservation oracle.

### R1: directory custody, final bytes and branch CAS

| Witness / proposed method | Real world and deterministic boundary | Sole mutant and required red |
|---|---|---|
| W-U9 / PC `C1737_MatchingContentReplacementIsNotDeleted` | Use `RemovalFixture.AfterUnregister` to leave a genuine pending record after successful unregister. Move the approved aside to an owned rescue sibling; create a second directory at the aside path with exactly the approved relative names/kinds/bytes and retained evidence unchanged. Prove different directory identities and equal approved content. Clear the fault and resume. No old-registration restore path is entered. | Omit only the equality rejection in `CustodyOf` at line 909, keeping unsealed checks, manifests and native walk. Assert replacement directory identity and every byte survive, rescue copy survives and branch remains; the mutant deletes the equal-content foreign directory. A differing-content sentinel would mask this again and is forbidden. |
| W-M7 / GC `C1737_AdvancedBranchSurvivesDeleteCas` | Use existing `RemovalHarness.OtherCommand`/Git `BeforeCommand` only on the exact source ref's `update-ref -d`. After the successful current-OID read, create a real unpublished child commit and advance that ref via a separate fixture Git reader with the hook disabled for the writer. Directory is already removed; all earlier remote/current-source proofs were for the old OID. Assert one hook activation and the intervening OID before resuming delete. | Omit only expected-old SHA from the actual delete argv. Assert `show-ref --verify --hash` still returns the newer OID and its reflog exists before reason/count checks. Mutant deletes the actual newer ref. Never synthesize show-ref output or merely assert argv. |
| W-A23 / NF `C1737_PostHashWriteSurvivesFinalStat` | Linux-only, small owned regular file. A test-local reflection adapter opens the real parent descriptor, captures the real listed stat, invokes real `HashAt` and retains its non-null ending stat. Write different-length bytes through the same inode after the hash returns. Invoke real `UnlinkSameNode` with those exact values. No earlier manifest/hash read runs again; this is the function's legitimate post-hash input state. Dispose every SafeFileHandle in finally. | Omit only `hashed is { } proof && !SameContent(proof, now)` refusal. Assert file exists with the newly written bytes; mutant unlinks it. Check same-node identity, hash completion and changed size before invoking. This proves the final stat check, not call-site wiring or the remaining final-stat/unlink interval. |

R1 retains existing PC replacement/restore regressions and GC argv regression.
Reflection binding failure, inability to obtain the real stat, or an unexpected
inode change is fixture failure, never a kill. Do not add a production seam to
avoid reflection; an unavailable boundary goes back to TestDesign.

### R2: source-exclusive salvage and failed evidence reads

Use SAL-local helpers and actual scratch Git repositories. For source isolation,
create a rewritten commit `L` with a clean pushed current tip `P`. Keep `L` only
in the selected worktree HEAD log or branch log; construct the other log with
valid entries excluding `L` and its descendants. Preflight both raw old/new ID
sets, both `reflog show` results and `rev-list --all`. Do not use `git gc` or delete
object storage. The acceptance claim is surviving **reachability roots**, not
immediate object-file disappearance.

| Witness / proposed SAL method | Boundary and oracle | Sole compiling mutant |
|---|---|---|
| W-U1H / `C1737_HeadReflogOnlyCommitSurvivesRemoval` | Only the worktree HEAD reflog contains `L`; branch reflog/current HEAD/other refs do not. After genuine retirement, exact salvage ref equals `L` and `rev-list --all` reaches `L`. | Omit HEAD reflog enumeration tuple, including its associated `LoggedIdsAsync` call; keep current-HEAD and branch sources. Red: missing salvage root after tree/log removal. |
| W-U1B / `C1737_BranchReflogOnlyCommitSurvivesRemoval` | Twin with `L` exclusively in branch reflog. Same physical ref/reachability oracle. | Omit branch-reflog tuple only. HEAD/current-tip inputs cannot rescue `L`. |
| W-U3 / `C1737_MissingVerifiedSalvageRefKeepsTree` | After successful salvage write and before the final `for-each-ref`, delete that newly created ref using a separate real fixture Git driver; then let the successful enumeration run normally. Source reflog still contains `L`. Assert tree snapshot, registration, source ref and raw source log remain before checking refusal. | Replace only the final `tips.All(...present...)` decision with success. Mutant removes the remaining source custody with the required salvage root absent. Activation must prove exit 0, absent exact ref and a real prior write. |
| W-U5R / `C1737_UnreadableRawReflogKeepsTree` | Linux non-root lane. After real `reflog show` succeeds, at the matching raw-path resolution callback make that owned log mode-000. Keep `L` only as an old ID in that log. Assert unreadability using the same identity, restore permissions in finally, and check original tree/registration/ref/log bytes before reason. | In the raw-read IO/UnauthorizedAccess catch return an empty list instead of null. No other raw source contains `L`; mutant proceeds to removal. Root/capability bypass of mode-000 is not an executed witness. |
| W-U5P / `C1737_MalformedRawReflogKeepsTree` (3 arguments: short-fields, bad-old, bad-new) | After successful earlier listing, put the chosen invalid record containing source-exclusive `L` in the owned raw log **alongside a valid entry for published `P`**. Use `L` as the lone short field, the valid new ID beside bad-old, or the valid old ID beside bad-new. Preserve exact corrupted bytes. Other sources exclude `L`. The valid entry ensures the later nonempty-IDs guard passes if the malformed record is skipped. Assert tree, registration, branch and raw log retained. | Change only malformed-record `return null` to `continue`, leaving valid entry processing untouched. This is one parse-fence selector with three outcomes, not three claimed independent guards. An invalid-only file would remain masked by the empty-IDs check and is not this fixture. |
| W-U5M / `C1737_MissingRawReflogKeepsTree` | Successful listing had entries. At raw-path resolution, move the exact log to an owned evidence sibling, leaving its expected path absent. Assert the still-existing tree/registration/branch stay and the moved log remains readable. | In `!File.Exists(path)` treat listed absence as empty success. Red is unauthorized source-tree removal after unknown history; do not attribute the fixture's log move to the mutant. |
| W-U5E / `C1737_EmptyRawReflogKeepsTree` (2 arguments: empty, all-zero-ids) | Earlier listing had entries. At path resolution put either empty content or a syntactically valid all-zero old/new record in the owned raw log. Other inputs are safe; preserve actual tree/ref/log state on refusal. | Omit only `if (listed && ids.Count == 0) return null`. Mutant removes the retained source tree. |

All faults are installed after the earlier command, not before `reflog show`,
which would otherwise refuse upstream. Restore fixture faults/permissions even
when assertions fail. R2's inaccessible method is explicitly Linux/non-root;
other R2 methods are portable. No unreadability claim comes from a skipped method.

### R3: atomic salvage writes and prune ownership

| Witness / proposed SAL method | World, schedule and primary oracle | Sole mutant |
|---|---|---|
| W-U4C / `C1737_LateDirectCollisionKeepsForeignOid` | Enumeration and symbolic probe see an absent salvage name. At its exact update-ref start, another real fixture writer creates that direct name at different valid OID `F`. Keep the live switch on. Assert name remains `F` and source tree is retained. A separate same-OID initial control verifies idempotent success. | Omit only the expected-zero old OID from the create command. Mutant overwrites `F` with `L`; assert `F` preservation before any refusal text. |
| W-U4N / `C1737_LateDanglingSymbolicCollisionDoesNotCreateTarget` | After both probes, immediately before the write, install a real symbolic salvage name pointing at an **absent** fixture-owned foreign branch. Record that the target is absent. Current `--no-deref` may replace the late symref itself with the owned direct salvage ref; this test does not claim the late symref remains. | Omit only `--no-deref`, keeping expected-zero CAS. A dangling target permits the mutant to create the foreign branch. Assert that foreign target is still absent first. Final verification may subsequently refuse; it cannot undo this already-observed foreign write. A non-dangling target would be masked by CAS. |
| W-U6N / `C1737_PruneKeepsMismatchedNameOid` | Real old direct salvage ref named with OID `A` but holding distinct valid `B`; explicit prunable task scope, old readable timestamp, live switch on and genuine repository lease. Invoke real prune through its supported boundary. Assert exact name still holds `B`. | Remove only name/suffix-to-OID equality; retain two-field/direct/OID/age/live checks and delete CAS. Mutant deletes the real wrongly named root. |
| W-U6R / `C1737_PruneKeepsRefWhenTimeReadFails` | Valid name/OID, old real ref. Inject a failed `reflog show` result containing a parseable old timestamp in stdout; command failure must dominate stdout. Other predicates true. Assert ref/OID unchanged. | Omit only `if (!log.Succeeded) continue`. Parser accepts the old stdout and mutant deletes the ref. Empty stdout would mask this at parsing. |
| W-U6P / `C1737_PruneKeepsRefWhenTimeCannotBeParsed` (2 arguments: no-match, numeric-overflow) | Same eligible direct ref, successful time command with malformed timestamp. Assert ref retained; no exception oracle. | Replace the combined parse-and-continue check with a compiling unconditional `long.TryParse(..., out seconds)` whose Boolean is ignored. Failed parse yields zero, a valid Unix epoch, so remaining age guard passes and actual ref deletion is red. Do not simply remove a declaration and credit a compiler error. |

For direct PruneAsync worlds, retain real lease ownership and an explicitly
completed/prunable scope; do not weaken service completion policy. These tests
isolate the helper's stated predicates, not a new service-completion control.

### R4: final live authorization and actual stderr production

The switch fixtures use existing isolated database/Git graphs. A test-local
`IWorktreeManager` decorator arms callbacks **when TryRemoveAsync receives the
real request**, then forwards it unchanged. This distinguishes remover reads
from earlier service/policy reads. For a pending tree, its next registration
observation is the one immediately before the resumed branch. The decorator is
not a fake successful remover and must not suppress any callback.

| Witness / proposed method | World/schedule and primary oracle | Sole mutant |
|---|---|---|
| W-B11 / SW `C1737_ResumeRegistrationDisableKeepsBytes` | First pass leaves an actual approved set-aside and receipt by throwing after real unregister; do not add an unapproved marker. On retry, service admission succeeds with Execute on. Arm at real remover entry; after its registration observation returns absent, turn Execute off and keep it off. Assert aside identity/approved bytes and source branch remain. | Omit only resumed `LiveAuthorizationAsync` check at `GuardedWorktreeRemoval.cs:108`. Manifest/native deletion checks are satisfied and do not authorize/deny by switch. Mutant deletes aside bytes before any later branch callback. |
| W-B10 / SW `C1737_CursorCompletionDisableKeepsResumedBytes` | Same pending state. Keep Execute on through the final callback's initial `ReadLegacySwitch`. Arm a test-local DB command interceptor only after the resumed registration observation; complete that callback's exact retry-cursor SELECT, then disable Execute before its await returns. No future pace hold. Assert query/flip activation, then actual aside bytes. | Replace only the final `return ReadLegacySwitch()` after the cursor query with null. Earlier admission is genuinely on; no subsequent live check runs before resumed delete. Mutant removes the bytes. Do not flip on an earlier cursor read or in SavingChanges. |
| W-U7 / SW `C1737_NullMonitorKeepsSystemReleaseTree` | Create/persist a valid system release with normal settings. Construct another retirement service using the same scoped real dependencies but explicit `legacyRelease: null`; execute it with actual remover and live callback graph. Assert tree, registration, source ref and no completed removal. | Change only the null-monitor branch to return null (allow), rather than `LegacyReleaseDisabled`. Every subsequent call still takes this same null branch; there is no fabricated monitor and no downstream safety bypass. Simply deleting the null guard and causing NullReferenceException is a different invalid control. |
| W-WARN / LP `C1737_NonEnglishStatusWarningKeepsTree` | Local LandingGit subclass disables in-process working-tree reads and uses its existing process-start seam for a test-owned child returning exit 0, empty stdout and a fixed non-English warning. Base `ExecuteProcessAsync` reads real pipes and creates the result. Forward that real result for the subject's status reads through the fixture's existing hook; all other Git, authority, content and live checks remain genuine and valid. Use a clean pushed tree with a committed sentinel. Assert directory/sentinel/ref/registration remain before reason. | Replace only producer `error.Length != 0` with the historical English-phrase-only predicate. It now produces false for the real non-English warning; the otherwise valid tree is removed. Assert child activation and exact warning shape without manually setting WroteStandardError. This is preservation under an untrusted census, not a claim that the fixture contains an undiscovered dirty file. |

The status child uses the assembly-local process limiter, isolated child-only
environment and finite exit/drain bounds. No global PATH/environment mutation,
live runner, provider or messaging gateway. R4 uses no phone-home fixture for
new repairs; B21 remains a separate existing candidate, not added authoring.

## Slices, ownership and sequencing

File abbreviations are exact:

| Key | Owned path |
|---|---|
| LP | `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleasePolicyTests.cs` |
| SW | `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseSwitchTests.cs` |
| SAL | `tests/Antiphon.Tests/Application/C1708ReflogSalvageTests.cs` |
| NF | `tests/Antiphon.Tests/Infrastructure/WorktreeNoFollowDeleteTests.cs` |
| PC | `tests/Antiphon.Tests/Infrastructure/LandingRemovalPolicyControlTests.cs` |
| GC | `tests/Antiphon.Tests/Infrastructure/WorktreeGuardedCleanupTests.cs` |
| Native | `tests/Antiphon.Tests/Application/C0824DesktopLegacyReleaseNativeTests.cs` |

| Slice | Files; rows | Checkpoints | Risk and bounded exit | Conservative authoring + required author verification |
|---|---|---|---|---|
| R1 custody/CAS | PC, NF, GC; 01, 06, 18 | CP-1..3; 3 new selectors/methods | Highest consequence: foreign directory, new bytes, unpublished branch. Reflection/native handle plumbing is the main uncertainty. Stop if the direct boundary cannot be reached without product edits. | **150-210 min**: 90-130 authoring, 60-80 ordinary/packed cycles/evidence. |
| R2 salvage sources/read failures | SAL; 07, 08, 10 | CP-4..5; 7 selectors/methods, 10 new results | Lost reflog roots; source isolation and actual raw-read timing must be demonstrated. Unprivileged Linux is required for U5R. No new container/host per test. | **240-330 min**: 150-210 authoring, 90-120 verification/evidence. |
| R3 salvage ownership/pruning | SAL; 09, 11, 12 | CP-6..7; 5 selectors/methods, 6 new results | Foreign-ref overwrite/delete; ensure the collision is late and dangling for no-deref. Starts only after R2 is integrated and its file is no longer under Review. | **180-240 min**: 110-150 authoring, 70-90 verification/evidence. |
| R4 switch/status boundaries | SW, LP; 23, 26, 35, 37 | CP-8..9; 4 selectors/methods | Wrong await interception would reproduce masking. One-shot arming must show real remover/cursor/child activation. Changes to common fixtures require reselection. | **210-300 min**: 130-200 authoring, 80-100 verification/evidence. |
| N1 queued native follow-up | NF, Native; existing PC methods read-only; 03, 05, 25 | CP-10..12; native roster below | Real Windows semantics/8.3 prerequisites. No present Linux or desktop execution. Product U8 excluded. | **180-300 min**, provisional, plus Windows availability; freeze exact native budgets in TestDesign. |

**Parallel:** R1 and R2 first. R4 has disjoint named test files and may proceed
alongside them if effective stage/host/build-slot limits allow. This is source
independence, not a recommendation to saturate build slots. **Sequential:** R3
after R2; N1 after R1 because both own NF. Native PC qualification uses the landed
R1 method. Any common-helper change invalidates these independence claims until
its scope is revised. TestDesign itself must use separate artifacts per slice
or serialize edits to this shared plan.

Thus **four portable Code ownership slices**, plus **one native authoring slice**
are designed, rather than four ninety-minute promises. Portable total is
**13-18 hours**, excluding independent Review, post-land Mutation, queue extremes
and repairs. If 90 minutes is a hard dispatch cap, the conservative upper bounds
require **at least 14 sequentially integrated portable dispatch packets**
(R1 3, R2 4, R3 3, R4 4) and 4 native packets; TestDesign must amend the ownership
groups into such packets before Code. Do not truncate a cycle to meet that cap.
No bounded count or cost can yet be assigned to U8's product work.

## Windows rc obligations

Queue these exact methods on the actual landed source in the Windows rc lane.
Use existing rc ownership/custody, platform receipts and isolated fixture paths.
This is a future selection, not permission for this delegate to use the desktop.
Every method/result must appear in a fresh TRX with actual Windows branch
execution; a Linux skip, fixture-precondition failure or synthetic alias gives
no native acceptance credit.

| Native obligation | Exact methods and status | What must be retained / limits |
|---|---|---|
| N-ID: receipt directory identity | PC `C1737_MatchingContentReplacementIsNotDeleted` (new in R1) | Real Windows volume/file identity differs while content matches. Keep both original rescued bytes and replacement. Applies U9 mutant on Windows later; Linux receipt is not its substitute. |
| N-ENTRY: per-entry bytes | NF `C1737_WindowsChangedEntryKeepsBytes` (new in N1); existing `C1717_B3_WindowsOpenWriterRefusesTheDelete` | New method uses a test-local reflection adapter to invoke real private `DeleteWindows` for a non-root approved relative file after its bytes changed, with a valid initial approval listing. This deliberately enters after the initial census. Removing only per-entry `RejectChanged` must delete actual changed bytes. Existing method supplies real deny-write sharing coverage; its initial census may mask a narrower hash-share mutation and is not credited automatically. |
| N-LIST: new name / listing | NF `C1737_WindowsUnapprovedEntryKeepsBytes` (new in N1) | Invoke real `DeleteWindows` for an actual unapproved non-root relative file with the valid earlier listing in RemovalApproval. This isolates the per-entry defense when a name was not admitted by that listing; omit only the per-entry `RejectChanged` call for intended byte-deletion red. It shares N-ENTRY's mutation site but has a separate exact-method cycle. The earlier directory-list refusal is a redundant independent new-name preservation binding, not credited by this test. No contradictory Names/Kinds map or second bypass. |
| N-RESTORE: wrong directory is not restored | PC `C1717_B4_ForeignDirectoryIsNotRestored` (existing) | Windows identity-backed restore refusal, source path not filled with the foreign tree, both original/foreign bytes and record retained. A11's historical Linux kill does not execute Windows identity code. |
| N-1 held handles/cwd | Native `Held_directory_defers_never_forces` (existing: open-handle, cwd) | Both results, owned child joined, registered or recorded custody, successful later retry after release. Not U8 late-disable acceptance. |
| N-2 move/unregister and length | Native `Long_path_never_leaves_an_unregistered_orphan`, `Long_path_untracked_file_holds_the_tree`, `Long_path_ignored_tree_is_held_not_partial` (existing) | Record current refusal boundary, registration/tree/branch/bytes. PC-67's old unregister-first mutant remains masked by length preflight on this schedule. Do not turn off PathLimit. A separate reachable short-path native move-failure schedule would be a new control, not this historical kill. |
| N-3 junctions/aliases | Native `Windows_aliases_and_junctions_are_refused` (existing: junction-leaf, junction-inside, short-name; strengthen native prerequisite in N1) | Preserve actual external target and record guard activation per argument. The short-name argument requires an observed real 8.3 alias resolving to the fixture tree; replace the synthetic CARD-T~1 fallback with explicit unavailable evidence. A skip cannot close native acceptance. Do not change the host's filesystem policy. Removing an outer alias guard while canonical/reparse guards still refuse earns no kill. |
| U8, separately commissioned | Proposed `C1737_WindowsRetryPauseDisableKeepsApprovedBytes` after O-1 design | Not in the runnable checkpoint roster: no existing reauthorization guard supports the proposed contract. Measure real sharing-pause entry, switch change, next native attempt, actual bytes and retained receipt. Cover both whole-walk retry and OpenNoFollow retry if the product contract applies to both. No proof is claimed from a simulated Windows=true rename helper. |

N1 authoring adds **two** methods (N-ENTRY and N-LIST) and strengthens the existing
short-name prerequisite, not an unbounded native suite. Existing native methods and R1's cross-platform receipt
method close the requested receipt/walker roster at their stated boundaries.
They do not establish every handle-exchange interleaving in the native walker.
If Review requires that broader claim, it needs a separately specified control
and schedule, not a broader native-green label.

## Verification handoff and mutation re-verification

TestDesign must write the executable `## Verification design` for each slice,
preserving the row IDs and names here. It must pin mutant snippets/digests,
exact constructor/interceptor wiring, per-argument expectations, activation
markers and the native prerequisites. No additional measuring battery is needed
by this Plan; collect timings from the first required cycles. An infeasible
schedule returns to Plan, never changes a production guard to ease a test.

**Author-side packed proof, explicitly commissioned by this brief:**

1. Commit/push the test-only slice before long verification. The final commit
   has no mutation switches or production diff. Keep original/packed source,
   exact patch and selector manifest in ignored task evidence.
2. Build one temporary logical pack for that slice, in native default bin/obj,
   with one exact allowlisted selector per control. Absent selector means off;
   unknown nonempty selector fails before execution. Exactly **one** selector
   may be active in each test process. No selector inferred from test names.
   Multiple controls in the same production file run **sequentially**, never
   simultaneously; this is not permission to combine bypasses.
3. For each control, run its **one exact method** selector-off, then selected
   red, using that packed build. Parameterized methods use the method prefix
   with trailing `*` and retain every argument result. Group at most three
   method invocations in a supervised execution packet; each still has its own
   lease and TRX. Do not run the ordinary class/group OR as a positive control.
4. Confirm the intended **preservation** assertion failed, after fixture
   activation. Failed builds, zero/skipped cases, query/fixture exceptions,
   timeouts and reason-only red do not qualify. If another guard still keeps
   the object, report a survivor and return the design; do not add a second bypass.
5. Restore every production file byte-for-byte, refresh timestamps, build the
   restored source once, verify producer and staged DLL bindings and absence of
   selector markers, then run each same exact method green. Record source SHA,
   dirty/patch identity, binary hashes, selector, phase, TRX identities/counts,
   assertion and timing. Temporary packed receipts are not clean-source ordinary
   Review certificates. No source edit while a child is running.
6. Run the closed ordinary checkpoint rows on the clean committed slice. Report
   the packed builds/method drivers separately as the explicit additional work
   commissioned here, not hidden unlisted runs. Do not repeat passed greens for
   performance or cosmetic evidence changes.

The mandatory author inventory is **19 selectors / 19 methods / 23 argument
results per phase**, hence **69 result observations** across off/red/restored
phases before any genuine failure reruns. The parse selector U5P has three rows;
U5E and U6P have two each. Some downstream preservation assertions in a red may
not run after the first failure: report only observed assertions. N1 has its own
later native inventory; N-ENTRY and N-LIST share one per-entry mutation site but
require both exact-method cycles. Earlier-listing/alias diagnostic observations
do not create kills.

**Post-land:** normal Code -> independent ordinary Review -> land/confirmed O,L,R
-> SourceLanding Mutation on L. Reverify all 19 controls and every argument on
the landed source, not the author's tip or latest master. Preserve the old waived
history and new author evidence separately. The companion records exact sites,
filters, variants, author report and plan. Sourced snapshots use only their
allowed local inherited execution, external evidence/restoration and no
commit/push. Use the SourceLanding driver's exact-method baseline/red/restored
protocol and one lease per driver; do not assume the author's packed-build waiver
permits relaxing the sourced contract. Recommission native-specific controls on
Windows. A later source fix requires new ordinary verification/Review/land before
another sourced pass. No global S4a clean claim while native/U8/other ledger
obligations remain open.

## Performance budget

Applies [testing-and-build Performance standards](../../testing-and-build.md#performance-standards)
and R1-R9 in [test-performance-standards](../../test-performance-standards.md).
**This Plan itself ran no tests/builds** because it changes only the design.
The following budgets bind future TestDesign/Code; new test timing is provisional,
tracked by CARD-1737, not a newly measured p95.

Measurement provenance:

- Debug 5ae98d31, Linux, 2026-11-05, subject `0626344fbf1739809a4b079237b40c4f6b3028fa`
  and its listed Code SHAs: 11 successful cold builds, p50 **431.6s**, empirical
  p95/max **817.1s**; 39 admissions, p50 **0s**, p95 **255s**, max **405s**.
  Load varied widely; the 30m49s run spent 840s in admission and 817s building.
  Short row executions had maxima 42-85s; these are comparable host costs,
  **not p95s for these new methods**.
- C2's exact source was this plan's baseline. One packed build 660.8s and one
  restored incremental build 119.4s; six method hosts **26.96-44.83s**, body
  samples include S1 11.60s and S11 6.76s. B2 packed/restored builds were about
  403.6s/144.2s. Sample sizes are too small for stable percentiles; they support
  packing/reuse, not a 90-minute repair guarantee.
- No Windows p95 or new A23/reflection/permission/cursor sample is available.
  Native estimates and new body caps remain provisional and are collected at
  the first required baseline, then refined in TestDesign before repeated use.

| Phase / selection | Expected duration | Timeout / rationale |
|---|---|---|
| Admission, each build/test driver | 0-5 min normally; historical max 6m45s | Finite **10 min** per acquisition, about 2x measured 255s rounded conservatively. Report separately; exit 4 means not run, never unleased retry. |
| Cold isolated or packed build | 7-14 min under comparator load | **28 min execution**, rounded 2x 817.1s; replaces the older 20min comparator cap for this plan. CARD-1737 provisional host/load sample. One build at a time/worktree. |
| Restored native incremental build | 2-3 min, small sample | Initial **5 min**, rounded 2x the larger of the two 119-144s observations. Provisional CARD-1737; calibrate from the required restored build, no speculative extra build. |
| New small filesystem/native-boundary body | 0.1-2s; class-group <10s expected | Initial **5s body**, provisional CARD-1737; record actual p95. No slow spin or huge-file race. |
| New Git/DB body | Usually 5-20s; multi-pass ≤30s expected | Initial **40s** simple / **60s** multi-pass body; provisional, collect every argument's first off/red/green timing. Existing 180s SAL/SW class limits are inherited, not evidence-based new budgets; do not widen them. |
| Shared Postgres start/readiness | ≤60s once per assembly/session | Existing bounded TestDbFixture ownership; report actual phase. No new container per test/class. |
| Shared migrations/template seed | ≤30s once per assembly/session | Use existing template/model setup; record separately, not repeatedly in each world. |
| Per-case clone/data and Git scratch | DB ≤5s; Git ≤5s expected | Existing isolated schemas and fixture-owned repositories. Shared expensive seed/template; independent mutable refs/index/tree. Any costly fresh seed is measured and justified in TestDesign. |
| Web/AppHost | Not needed for these new portable witnesses | Do not boot a web host or production runner. DesktopLegacyWorld uses a local service graph; that is not an AppHost startup. Native holder child is separately bounded. |
| Test-host startup / teardown | 10-25s / 5-15s, under load startup observed up to 67s | Count inside row execution; unknown fixture subphases remain unknown. Teardown must join every child/handle; no orphan on timeout. |
| Portable ordinary row | 0.5-3 min execution, depending on roster | CP caps below: **2, 3 or 6 min**, provisionally about 2x expected/nearby observed host+body cost. Recalculate from exact roster baseline, never widen to hide red. |
| Exact-method packed driver | 0.5-1.5 min execution | **3 min** provisional: round 2x the Debug's 85s loaded short-row maximum, with C2's 27-45s hosts as the smaller comparator. Each driver also has 10min admission bound. Reuse the pack, not an active loop-wide lease. |
| Native row | 2-4 min execution expected | Initial **8 min** execution, CARD-1737 provisional because Windows data is absent. Keep existing native 60s test limits unless a measured design justifies a separately reviewed change. |
| External calls | local Git usually <1s, DB <1s; no live HTTP | Fixture Git deadline initially **10s**, DB/interception barrier **5s**, child exit/drain **10s**, teardown **15s**, subject's existing stricter budgets retained. Span every call; release test barriers in finally. |

R2's U5R gets NativeLinuxOnly plus non-root prerequisite; A23 gets NativeLinuxOnly.
New Git/DB and OS/filesystem witnesses belong to **git operations / Integration**;
NF's existing class-level Unit label must not silently misclassify a new native
case. Use explicit method classification/OS policy approved by TestDesign. Native
methods use NativeWindows policy. All process spawns use the assembly-local
ProcessSpawnLimit/GitProcessSpawnLimit as appropriate; no concurrent Pty/FakeClaude
assembly. Separate processes also need DB isolation and fleet admission.

Project/category inventory: tests compile in `Antiphon.Tests`, directly referencing
`server/Antiphon.Server.csproj` (which contains these Git services) and its existing
runner, messaging, resilience and fixture/tool project references. The changes are
test-only with local helpers, so there are **no changed production downstream
consumers**. The closed selection is these exact Git-operation tests, not the
whole runner/messaging/Pty projects built transitively. TestDesign captures the
actual ProjectReference graph at its source; unknown ownership or a shared-source
edit requires revised selection/Full as the owner policy says. CARD-1546 project
extraction is considered but rejected here as unrelated production restructuring.
These rows do not supply scoped rc-gate credit by themselves.

Record `.antiphon/checkpoints/<run-id>/` ordinary receipts/TRX and an ignored
task-owned evidence root for packed cycles. Keep exact Class.Method/arguments,
source/build SHA and hashes, host/OS/load, category, admission per acquisition,
build, container start/readiness, migrations/seed, clone/data, host startup,
test body, teardown, and DB/filesystem/HTTP/CLI spans. Use CARD-1514 recording
with the correct `ANTIPHON_TEST_PHASE_GROUP_ID` per CP. HTTP is not applicable
where none runs; absent measurements are **unknown**, not zero. Report wall,
nested spans and unattributed remainder separately.

**Packing/R1:** no run has more than three rows. The known Debug explains why
three serial rows under burst queueing can exceed 30 minutes even with tiny test
bodies; this is not an exemption. Normal ordinary runs here are expected around
12-22min including admission; a high-contention three-row/native run can be
30-40min. Before scheduling a selection expected above 30min, the caller supplies
a bounded performance Debug disposition for that exact packing, using 5ae98d31's
findings; any observed overrun requires its own recorded Debug follow-up. Do not
pay another identical batch to diagnose it. Prefer one/two-row packets with
supported same-source build reuse when TestDesign freezes the manifests.

One host build-slot lease per build and per method/row driver. Checkpoint tool
takes its own; other drivers use `scripts/build-slot.ps1`. Native direct builds
use no `-o`, `--output` or `OutputPath`, and consistent `UseAppHost=false` for
build/run. Cold checkpoint outputs are the tool-owned exception. Packed cycles
and required clean ordinary certificates are separate; do not mistake an earlier
dirty producer for the clean checkpoint build.

### Checkpoints

One table covers all slices because the importer reads only the first table.
Every row owns one exact filter and an isolated source-bound build, either new
or referenced from an earlier row with the same After. R1 has three rows, R2/R3/R4
two each, N1 three; never run all slices together. `Group` names the lane.
CP-3 and CP-5 require Linux; CP-10..12 require Windows and are queued. Do not run
filters for tests that have not been authored yet. Expected counts below include
parameter arguments, not assertion counts.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | R1 | tests/Antiphon.Tests -> bin-c1737-r1/ | portable-custody | /*/Antiphon.Tests.Infrastructure/LandingRemovalPolicyControlTests/(C1737_MatchingContentReplacementIsNotDeleted*)\|(C1717_D4_RetryKeepsAReplacementDirectory*)\|(C1717_B4_ForeignDirectoryIsNotRestored*) | W-U9; existing replacement and restore | all 3 named results, 0 failed/skipped | 3 | 14 | true |
| CP-2 | R1 | CP-1 | portable-branch-cas | /*/Antiphon.Tests.Infrastructure/WorktreeGuardedCleanupTests/(C1737_AdvancedBranchSurvivesDeleteCas*)\|(C443_BranchDeleteUsesOldSha*) | W-M7; existing argv contract | both named results, 0 failed/skipped | 2 | 3 | true |
| CP-3 | R1 | CP-1 | linux-final-stat | /*/Antiphon.Tests.Infrastructure/WorktreeNoFollowDeleteTests/C1737_PostHashWriteSurvivesFinalStat | W-A23 | exact method, 0 failed/skipped | 1 | 2 | true |
| CP-4 | R2 | tests/Antiphon.Tests -> bin-c1737-r2/ | portable-salvage-sources | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_HeadReflogOnlyCommitSurvivesRemoval*)\|(C1737_BranchReflogOnlyCommitSurvivesRemoval*)\|(C1737_MissingVerifiedSalvageRefKeepsTree*)\|(Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref*) | W-U1H,W-U1B,W-U3; existing happy path | all 4 named results, 0 failed/skipped | 4 | 15 | true |
| CP-5 | R2 | CP-4 | linux-raw-reflog | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_UnreadableRawReflogKeepsTree*)\|(C1737_MalformedRawReflogKeepsTree*)\|(C1737_MissingRawReflogKeepsTree*)\|(C1737_EmptyRawReflogKeepsTree*) | W-U5R,P,M,E | 4 methods / 7 arguments, 0 failed/skipped | 7 | 5 | true |
| CP-6 | R3 | tests/Antiphon.Tests -> bin-c1737-r3/ | portable-salvage-write | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_LateDirectCollisionKeepsForeignOid*)\|(C1737_LateDanglingSymbolicCollisionDoesNotCreateTarget*)\|(Direct_salvage_collision_refuses_and_keeps_the_existing_ref*)\|(Symbolic_salvage_collision_refuses_and_never_moves_another_branch*) | W-U4C,N; existing static collisions | all 4 named results, 0 failed/skipped | 4 | 15 | true |
| CP-7 | R3 | CP-6 | portable-salvage-prune | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_PruneKeepsMismatchedNameOid*)\|(C1737_PruneKeepsRefWhenTimeReadFails*)\|(C1737_PruneKeepsRefWhenTimeCannotBeParsed*)\|(Age_pruning_keeps_young_salvage_refs*) | W-U6N,R,P; existing age behavior | 4 methods / 5 arguments, 0 failed/skipped | 5 | 4 | true |
| CP-8 | R4 | tests/Antiphon.Tests -> bin-c1737-r4/ | portable-live-resume | /*/Antiphon.Tests.Application/C0824DesktopLegacyReleaseSwitchTests/(C1737_ResumeRegistrationDisableKeepsBytes*)\|(C1737_CursorCompletionDisableKeepsResumedBytes*)\|(C1737_NullMonitorKeepsSystemReleaseTree*)\|(Late_switch_after_unregister_preserves_set_aside*) | W-B11,B10,U7; existing B25 | all 4 named results, 0 failed/skipped | 4 | 15 | true |
| CP-9 | R4 | CP-8 | portable-stderr-producer | /*/Antiphon.Tests.Application/C0824DesktopLegacyReleasePolicyTests/C1737_NonEnglishStatusWarningKeepsTree | W-WARN | exact method, 0 failed/skipped | 1 | 3 | true |
| CP-10 | N1 | tests/Antiphon.Tests -> bin-c1737-native/ | windows-native-policy | /*/Antiphon.Tests.Application/C0824DesktopLegacyReleaseNativeTests/* | N-1,N-2,N-3 with real short-name | 5 methods / 8 arguments, 0 failed/skipped; real alias required | 8 | 18 | true |
| CP-11 | N1 | CP-10 | windows-native-custody | /*/Antiphon.Tests.Infrastructure/LandingRemovalPolicyControlTests/(C1737_MatchingContentReplacementIsNotDeleted*)\|(C1717_B4_ForeignDirectoryIsNotRestored*) | N-ID,N-RESTORE | both named results, 0 failed/skipped | 2 | 4 | true |
| CP-12 | N1 | CP-10 | windows-native-walker | /*/Antiphon.Tests.Infrastructure/WorktreeNoFollowDeleteTests/(C1737_WindowsChangedEntryKeepsBytes*)\|(C1737_WindowsUnapprovedEntryKeepsBytes*)\|(C1717_B3_WindowsOpenWriterRefusesTheDelete*) | N-ENTRY,N-LIST | all 3 named results, 0 failed/skipped | 3 | 4 | true |

Roster totals: portable **27 methods / 31 results** (19 new methods / 23 new
results plus 8 existing controls); queued native **10 methods / 13 results**.
Every existing method operand is a unique prefix in its named class at this
baseline; no existing method/class has a `C1737_` prefix. Native currently has
five methods/eight results; the short-name argument must use a real alias.
TestDesign must repeat the suffix-collision audit after implementation and verify
fresh executed TRX membership, including all argument values. Single methods
above are exact; OR operands have their own parentheses and trailing `*`, and
Markdown pipes are escaped. If a suffix begins matching an extra method, split
to exact-method rows with a shared build and revise packing before execution.

Manifest refinements required from TestDesign: per-row execution caps CP-1 **3**,
CP-2 **3**, CP-3 **2**, CP-4 **6**, CP-5 **6**, CP-6 **6**, CP-7 **6**, CP-8 **6**,
CP-9 **3**, native rows **8** minutes; set each row's `timeoutMinutes` explicitly,
`timeouts.rowMinutes: 28` to bound the build under `BuildDeadlineMinutes`, and
`build.slotWaitMinutes: 10`. Charged slice totals are R1 **36**, R2 **40**, R3 **40**, R4 **37**, N1
**52** minutes. Set independent hard-wall caps to these totals plus
`(1 + number of rows) * 10` admission minutes. These are ceilings, not expected
durations. Use the supported `totalTimeoutOverrideReason` referencing this
measured/provisional phase budget when the importer would otherwise derive a
larger total from wall estimates; do not silently accept its 15-minute row floor
or 3x estimate as R5 compliance. No global total for all five slices.

Use the checkpoint tool's `run --plan <artifact> --after <slice>
--expected-source-sha <full committed SHA>`, then `wait` until exit is not 75.
Tool bootstrap, if necessary, is a separately reported slot-gated native build.
Preserve unedited CHECKPOINT lines and validate each ordinary receipt for that
source SHA. No unlisted full Unit/assembly run is commissioned. Code/Review run
`scripts/check-evidence-diff.ps1` over the full assigned base..HEAD history.

### Cost

Table estimates total **76 min portable ordinary execution/wall planning floor**
(R1 19, R2 20, R3 19, R4 18), plus **26 min queued native**, before exceptional
admission or reruns. They are independent of `Min` counts and are not a measured
new-test p95. Ordinary Review budgets the same named scope under its stage policy.

Author packed proof has eight additional builds (one packed and one restored
per portable slice) and 57 exact-method invocations (19 x 3 phases), with 69
argument results. At nearby build/host samples this is roughly **70-120 min
execution plus 0-5 hours admission** spread into bounded packets, already allowed
for provisionally in slice ranges; burst queues can exceed those author budgets
and require a progress/budget correction, not truncated verification. Do not
hold a slot across a loop. No promise that the worst admission bound for every
acquisition fits the ordinary planning estimate.

Formal post-land SourceLanding is budgeted **separately**: 19 controls, 57
exact-method phase observations, with fresh phase build/provenance as required
by that contract. At 7-14min cold build plus 0.5-1.5min host per phase, budget
approximately **7-15 hours execution** before admission and analysis; native is
additional. TestDesign must refine using actual author measurements and the
source driver's allowed build reuse, not assume packed waiver reuse. Ordinary
Code/author proof and SourceLanding costs must both remain visible at dispatch.

## Operator decisions and next handoff

- **O-1 / U8:** decide whether disabling Execute must stop subsequent native
  open/walk retries within a removal that already began, or only stop the next
  externally admitted destructive operation. The source currently has no read
  inside either retry loop. If interruption is required, commission product
  design covering callback placement, retained receipt/partial bytes and both
  retry loops; never relax a fence. Until decided and measured, native
  late-disable acceptance remains open. This choice does not block R1-R4.
- **O-2 / U10:** name a concrete additional defect/site/control and commission
  it, or retire the unspecified placeholder. No extra kill count either way.
- **Scheduling:** accept four portable slices at the stated budgets, or request
  the 14 bounded portable dispatch packets if ninety minutes is mandatory.
  Queue N1/Windows rc separately; never substitute desktop execution in this
  commission or synthetic short-name success. These are scheduling choices,
  not permission requests for the already-authorized Plan.

Next: **test-design**, first **R1 custody/CAS** and **R2 salvage sources** in
parallel, with disjoint TestDesign artifacts. Freeze exact cut wiring, compiling
selector pack definitions, expected rosters, performance manifest overrides and
budgets; then Code those scopes. R4 may follow subject to capacity; R3 follows
R2 integration. Keep O-1/O-2 and the explicit Windows queue on the ledger. The
caller can act on these decisions without another investigation of the same
masked schedules.
