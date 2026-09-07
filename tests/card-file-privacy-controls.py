"""CARD-0408 reversible mutation controls. Run ONLY in an isolated, idle checkout.

Usage: python tests/card-file-privacy-controls.py PC-1 PC-2 ...
Each production mutation is restored in finally, then the identical test is run green.
Compilation failures/empty runs are invalid controls, never counted as killed.
Artifacts stay in ignored .antiphon/c408-controls. No live APIs or shared stack.
"""
from pathlib import Path
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / '.antiphon/c408-controls'
CONTROLS = {}

def add(key, file, before, after, cls, method='*', client=False):
    CONTROLS[key] = (file, before, after, cls, method, client)

S = 'server/Application/Services/'
G = 'server/Infrastructure/Git/CardFileRepository.cs'
SYNC = 'CardFilePrivacySyncTests'
API = 'CardPrivateNotesApiTests'
POLICY = 'CardFilePolicyTests'
GIT = 'CardFilePrivacyGitTests'
OWN = 'CardFilePrivacyOwnershipTests'
add('PC-1', 'server/Domain/Entities/Board.cs', 'SyncCardFiles { get; set; } = false', 'SyncCardFiles { get; set; } = true', POLICY, 'Defaults_are_board_off_card_Inherit_notes_empty_repository_Unknown')
add('PC-1b', 'server/Migrations/20260906223252_CardFilePrivacy.cs', 'defaultValue: false', 'defaultValue: true', 'CardFilePrivacyMigrationTests')
add('PC-2', S+'CardFilePolicyService.cs', 'if (!syncCardFiles) return "board_not_opted_in";', '', SYNC, 'Board_off_never_writes_even_for_explicit_Public_card')
add('PC-3', S+'CardFilePolicyService.cs', 'repositoryVisibility is not (RepositoryVisibility.Private or RepositoryVisibility.Public)', 'false', SYNC, 'Unknown_repository_visibility_with_remote_present_BLOCKS_publication')
add('PC-4', S+'CardTaskFilePolicy.cs', '(c.CardFileVisibility == CardFileVisibility.Inherit || c.CardFileVisibility == CardFileVisibility.Public)', 'true', SYNC, 'Private_card_contributes_no_filename_index_count_or_metadata')
add('PC-5', S+'CardFilePublicCard.cs', '        c.Description,', '        c.Description + c.PrivateNotes,', SYNC, 'Private_card_contributes_no_filename_index_count_or_metadata')
add('PC-6', 'server/Api/Endpoints/CardEndpoints.cs', 'return Results.Ok(await service.GetByIdAsync(cardId, cancellationToken));', 'return Results.Ok(await service.GetPrivateNotesAsync(cardId, null, cancellationToken));', API, 'Create_and_atomic_public_to_private_edit_preserve_notes_and_history')
add('PC-7', S+'CardRevisionLog.cs', 'PrivateNotes = card.PrivateNotes,', 'PrivateNotes = null,', API, 'Create_and_atomic_public_to_private_edit_preserve_notes_and_history')
add('PC-8', S+'CardService.cs', 'if (request.PrivateNotes is not null) card.PrivateNotes = request.PrivateNotes;', 'card.PrivateNotes = request.PrivateNotes ?? "";', API, 'Omitted_null_empty_and_limit_inputs_have_distinct_semantics')
add('PC-9', S+'CardService.cs', 'if (request.ConcurrencyToken != card.ConcurrencyToken)\n            throw new ConflictException($"Card \'{card.Identifier}\' was modified by another operation.");', 'if (false) throw new ConflictException("mutated");', API, 'Create_and_atomic_public_to_private_edit_preserve_notes_and_history')
add('PC-10', S+'CardService.cs', 'RequireWithinLimit(errors, "privateNotes", request.PrivateNotes, MaxPrivateNotesLength);', '', API, 'Omitted_null_empty_and_limit_inputs_have_distinct_semantics')
add('PC-11', S+'CardService.cs', '&& r.RevisionNumber == revisionNumber && r.Kind == CardRevisionKind.ContentEdit)', '&& r.RevisionNumber == revisionNumber)', API, 'Historical_note_read_rejects_move_revision_and_distinguishes_unknown_snapshot')
add('PC-12', S+'CardTaskFileService.cs', 'foreach (var path in inspection.Removals.Order(StringComparer.Ordinal))', 'foreach (var path in Array.Empty<string>())', SYNC, 'Revocation_removes_previously_exported_files_and_last_public_index')
add('PC-13', S+'CardTaskFileService.cs', 'var ids = await _db.Boards.AsNoTracking().OrderBy', 'var ids = await _db.Boards.AsNoTracking().Where(b => b.ArchivedAt == null && b.Project.ArchivedAt == null).OrderBy', 'CardTaskFileServiceTests', 'SyncAllAsync_syncs_the_pathed_project_reports_pathless_and_skips_archived')
add('PC-14', S+'CardTaskFileService.cs', 'if (!_syncSettings.Enabled) throw new ConflictException("Card file sync is disabled.", "card_file_sync_disabled");', '', SYNC, 'Enabled_false_freezes_IO_and_does_not_claim_erasure')
add('PC-16', S+'CardTaskFilePolicy.cs', 'if (root is not null) roots.Add(Path.GetFullPath(root));', 'if (root is not null) roots.Add(Path.GetFullPath(path));', OWN, 'Subdirectory_projects_use_canonical_git_root_gate')
add('PC-18a', G, 'var input = string.Concat(addPaths.Select(p => p + "\\0"));', 'var input = Path.GetRelativePath(root, directory).Replace(\'\\\\\', \'/\') + "\\0";', GIT, 'AutoCommit_stages_and_commits_only_exact_generated_paths_never_directory')
add('PC-18b', G, 'string.Concat(commitPaths.Select(p => p + "\\0"))', 'Path.GetRelativePath(root, directory).Replace(\'\\\\\', \'/\') + "\\0"', GIT, 'AutoCommit_stages_and_commits_only_exact_generated_paths_never_directory')
add('PC-19', G, 'if (!await MatchesAsync(root, expected, ct)) return new(null, "generated_files_changed", null);', '', GIT, 'Changed_generated_bytes_before_commit_refuse_with_generated_files_changed')
add('PC-20a', S+'CardTaskFilePolicy.cs', 'state.WorkingPaths.Concat(state.Index.Keys).Concat(state.Head.Keys)', 'state.WorkingPaths', SYNC, 'Index_only_private_addition_is_unstaged_even_with_AutoCommit_false')
add('PC-20b', G, 'changed.Stdout.Split(\'\\0\', StringSplitOptions.RemoveEmptyEntries).Where(expected.ContainsKey).ToArray()', 'expected.Keys.ToArray()', GIT, 'Index_only_private_addition_is_unstaged_without_nonexistent_commit_path')
add('PC-22', S+'ProjectService.cs', 'if (pathChanged && _cardFiles is not null) await _cardFiles.EnsureDrainedAsync(id, null, cancellationToken);', '', OWN, 'Project_path_change_refuses_residue_then_releases_old_pin_and_resets_visibility')
add('PC-23', S+'CardTaskFileService.cs', 'if (!dryRun && (board.CardFilesDirectorySlug is null || board.CardFilesRepositoryPath is null))', 'if (board.CardFilesDirectorySlug is null || board.CardFilesRepositoryPath is null)', SYNC, 'Dry_run_changes_no_database_file_ignore_index_HEAD_or_warning_state')
add('PC-23b', S+'CardTaskFileService.cs', 'if (!dryRun) _repository.Delete(root, Path.Combine(root, path));', '_repository.Delete(root, Path.Combine(root, path));', SYNC, 'Dry_run_revocation_keeps_legacy_private_files_and_does_not_consume_warning')
add('PC-24', G, '"check-ignore", "--no-index", "-z", "--stdin"', '"check-ignore", "-z", "--stdin"', SYNC, 'Effective_ignore_blocks_new_writes_including_tracked_paths_and_allows_cleanup')
add('PC-27', S+'CardTaskFilePolicy.cs', 'if (board.SyncCardFiles != expected) throw new ConflictException("Card-file policy changed; refresh and retry.", "card_file_policy_changed");', '', OWN, 'Settings_compare_after_waiting_for_lease_and_only_broadcast_IDs')
add('PC-29', 'scripts/card.ps1', 'card files  NOT WRITTEN:', 'card files  WRITTEN:', 'CardFilePrivacyScriptTests', 'New_reports_eligibility_target_and_visibility_without_claiming_written')
add('PC-30', 'client/src/features/board/CardEditModal.tsx', '...(notesDraft === undefined ? {} : { privateNotes: notesDraft }),', '...(notes.isError ? { privateNotes: "" } : notesDraft === undefined ? {} : { privateNotes: notesDraft }),', 'CardEditModal', 'failed note read', True)
add('PC-31', 'client/src/features/board/CardFilePrivacy.tsx', "<Text component=\"pre\" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{notes.data.privateNotes === null ? 'Private-note history unknown' : notes.data.privateNotes === '' ? 'No private notes' : notes.data.privateNotes}</Text>", '<div dangerouslySetInnerHTML={{ __html: notes.data.privateNotes ?? "" }} />', 'CardFilePrivacy', 'loads notes only', True)
add('PC-32', 'client/src/api/cardFiles.ts', "export function usePrivateNotes(cardId: string, enabled: boolean, revisionNumber?: number) {\n  return useQuery({\n    queryKey: ['private-notes', cardId, revisionNumber ?? 'current'],\n    queryFn: () => apiGet<CardPrivateNotes>(`/cards/${cardId}/private-notes${revisionNumber === undefined ? '' : `?revisionNumber=${revisionNumber}`}`),\n    enabled, gcTime: 0, meta: { persist: false },\n  })\n}\n", "export function usePrivateNotes(cardId: string, enabled: boolean, revisionNumber?: number) {\n  const cache = useQueryClient()\n  return useQuery({\n    queryKey: ['private-notes', cardId, revisionNumber ?? 'current'],\n    queryFn: () => apiGet<CardPrivateNotes>(`/cards/${cardId}/private-notes${revisionNumber === undefined ? '' : `?revisionNumber=${revisionNumber}`}`).then((data) => { cache.setQueryData(['boards', cardId], data); return data }),\n    enabled, gcTime: 0, meta: { persist: false },\n  })\n}\n", 'CardFilePrivacy', 'loads notes only', True)
add('PC-33', S+'CardTaskFileSyncGate.cs', 'var key = $"{boardId:N}|{target}";', 'var key = $"{target}";', POLICY, 'Warning_dedup_is_per_board_target_reason')
add('PC-34', G, 'else psi.Environment["GIT_LITERAL_PATHSPECS"] = "1";', 'else psi.Environment.Remove("GIT_LITERAL_PATHSPECS");', GIT, 'Literal_NUL_pathspec_batch_exceeds_Windows_argument_limit_without_overmatching')
add('PC-35', 'server/Application/Settings/CardFileSyncSettings.cs', 'AutoCommit { get; set; } = false', 'AutoCommit { get; set; } = true', POLICY, 'Defaults_are_board_off_card_Inherit_notes_empty_repository_Unknown')
add('PC-36', S+'CardService.cs', 'await SaveCardWriteAsync(card, ct);\n\n        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);\n        fileLease?.Dispose();', 'await SaveCardWriteAsync(card, ct);\n        _logger?.LogInformation("Mutated notes {PrivateNotes}", card.PrivateNotes);\n        await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);\n        fileLease?.Dispose();', 'CardPrivateNotesBoundaryTests')
add('PC-37', S+'ProjectService.cs', 'else if (targetChanged) project.RepositoryVisibility = RepositoryVisibility.Unknown;', '', OWN, 'URL_change_without_visibility_resets_Unknown_but_explicit_value_is_retained')
add('PC-38', S+'CardTaskFileService.cs', '"antiphon: remove unpublished card files", ct);', '$"antiphon: remove unpublished card files ({board.Name})", ct);', SYNC, 'Revocation_removes_previously_exported_files_and_last_public_index')
add('PC-39a', G, 'if (check.Exists) return new(null, skip, null);', 'if (check.Exists && !marker.StartsWith("rebase")) return new(null, skip, null);', GIT, 'Git_operation_guards_keep_index_and_HEAD_unchanged')
add('PC-39b', G, 'if (check.Exists) return new(null, skip, null);', 'if (check.Exists && marker != "MERGE_HEAD") return new(null, skip, null);', GIT, 'Git_operation_guards_keep_index_and_HEAD_unchanged')
add('PC-39c', G, 'if (check.Exists) return new(null, skip, null);', 'if (check.Exists && marker != "CHERRY_PICK_HEAD") return new(null, skip, null);', GIT, 'Git_operation_guards_keep_index_and_HEAD_unchanged')
add('PC-39d', G, 'if ((await RunGitAsync(root, ct, "symbolic-ref", "-q", "HEAD")).ExitCode != 0)', 'if (false)', GIT, 'Git_operation_guards_keep_index_and_HEAD_unchanged')
add('PC-40', 'server/Api/Endpoints/CardEndpoints.cs', 'http.Response.Headers.CacheControl = "no-store";', '', API, 'Create_and_atomic_public_to_private_edit_preserve_notes_and_history')

add('PC-15', 'server/Application/Services/CardTaskFileService.cs', '        using var lease = await EnterBoardAsync(boardId, false, ct);\n        // Authoritative reads occur only after both the project and Git-root leases are owned.\n        var board = await ReadBoardAsync(boardId, ct);', '        var board = await ReadBoardAsync(boardId, ct);\n        using var lease = await EnterBoardAsync(boardId, false, ct);', 'CardFilePrivacyRecoveryTests', 'Authoritative_policy_load_after_acquiring_repository_lease_observes_revocation')
add('PC-17', 'server/Application/Services/CardTaskFileService.cs', '                operationWarnings.Add(phase);', '                operationWarnings.Add(phase);\n                foreach (var (relative, body) in inspection.Desired) await _repository.WriteAsync(root, Path.Combine(root, relative), body, board.Id, ct);', 'CardFilePrivacyRecoveryTests', 'Failed_cleanup_blocks_public_additions_and_fresh_service_recovers')
add('PC-21', 'server/Infrastructure/Git/CardFileRepository.cs', 'if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)', 'if (false)', 'CardFilePrivacyGitTests', 'Junction_ancestors_refuse_root_and_managed_file_aliases')
add('PC-25', 'server/Application/Services/CardTaskFilePolicy.cs', 'await _repository.InstallIgnoreAsync(root, ct);', 'await Task.CompletedTask;', 'CardFileIgnoreTests', 'Fresh_project_create_installs_deny_default_without_publication')
add('PC-26', 'server/Application/Services/CardTaskFilePolicy.cs', 'var gitPending = state.Index.Keys.Concat(state.Head.Keys).Any(removals.Contains);', 'var gitPending = !await _repository.IsIgnoredAsync(root, removals.ToArray(), ct) && state.Index.Keys.Concat(state.Head.Keys).Any(removals.Contains);', 'CardFilePrivacySyncTests', 'Effective_ignore_blocks_new_writes_including_tracked_paths_and_allows_cleanup')
add('PC-28', 'server/Application/Services/CardService.cs', '        catch (Exception)\n        {\n            return dto with { CardFileStatus = new CardFileCardStatusDto', '        catch (Exception) when (false)\n        {\n            return dto with { CardFileStatus = new CardFileCardStatusDto', 'CardPrivateNotesBoundaryTests', 'Failed_policy_probe_after_save_retains_created_card_and_reports_status_unavailable')
add('PC-39e', 'server/Infrastructure/Git/CardFileRepository.cs', 'if (unmerged.Stdout.Split(\'\\0\').Any(paths.Contains)) return new(null, "conflicted_paths", null);', '', 'CardFilePrivacyGitTests', 'Unmerged_managed_path_has_named_guard_and_preserves_conflict_index')

def run_test(key, phase, cls, method, client):
    if client:
        cmd = ['pwsh', '-NoProfile', '-File', 'scripts/test-client.ps1', cls, '-t', method]
    else:
        cmd = ['dotnet', 'run', '--project', 'tests/Antiphon.Tests', '--property:OutputPath=bin-c408/', '--', '--treenode-filter', f'/*/*/{cls}/{method}', '--report-trx', '--report-trx-filename', f'{key}-{phase}.trx']
    log = OUT / f'{key}-{phase}.log'
    with log.open('w', encoding='utf-8') as stream:
        result = subprocess.run(cmd, cwd=ROOT, stdout=stream, stderr=subprocess.STDOUT)
    text = log.read_text(encoding='utf-8')
    failed = re.search(r'failed:\s+(\d+)', text)
    total = re.search(r'total:\s+(\d+)', text)
    if client:
        tested = 'Test Files' in text and 'Tests ' in text
        return {'exit': result.returncode, 'assertion_failure': tested and result.returncode != 0, 'tested': tested, 'log': str(log.relative_to(ROOT))}
    return {'exit': result.returncode, 'assertion_failure': bool(failed and int(failed[1]) > 0), 'tested': bool(total and int(total[1]) > 0), 'log': str(log.relative_to(ROOT))}

if __name__ == '__main__':
    if len(sys.argv) == 1:
        raise SystemExit('Name explicit PC controls; this script mutates production files temporarily.')
    gitdir = subprocess.check_output(['git', 'rev-parse', '--git-dir'], cwd=ROOT, text=True).strip()
    if gitdir == '.git':
        raise SystemExit('Refusing the canonical checkout. Use an isolated worktree with no concurrent builds.')
    OUT.mkdir(parents=True, exist_ok=True)
    summary = []
    for key in sys.argv[1:]:
        file, before, after, cls, method, client = CONTROLS[key]
        path = ROOT / file
        original = path.read_bytes()
        (OUT / f'{key}-source.backup').write_bytes(original)
        (OUT / f'{key}-source.json').write_text(json.dumps({'path': file}), encoding='utf-8')
        text = original.decode('utf-8-sig').replace('\r\n', '\n')
        if before not in text:
            summary.append({'control': key, 'outcome': 'INVALID: mutation did not match'})
            print(summary[-1], flush=True)
            continue
        try:
            path.write_text(text.replace(before, after), encoding='utf-8')
            red = run_test(key, 'red', cls, method, client)
        finally:
            path.write_bytes(original)
        green = run_test(key, 'green', cls, method, client)
        killed = red['assertion_failure'] and green['tested'] and green['exit'] == 0
        row = {'control': key, 'outcome': 'KILLED' if killed else 'UNQUALIFIED', 'red': red, 'green': green}
        summary.append(row)
        (OUT / f'{key}.json').write_text(json.dumps(row, indent=2), encoding='utf-8')
        print(json.dumps(row), flush=True)
        if green['exit'] != 0:
            print('Stopping: restored baseline is not green.', flush=True)
            break
    raise SystemExit(0 if all(s['outcome'] == 'KILLED' for s in summary) else 1)
