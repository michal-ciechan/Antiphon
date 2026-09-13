using System.Diagnostics;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;

namespace Antiphon.Server.Infrastructure.Git;

public class LandingGit : ILandingGit
{
    protected virtual void ConfigureProcess(ProcessStartInfo start) { }

    public virtual async Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        => await ExecuteAsync(repository, arguments, null, ct);

    public virtual async Task<LandingGitResult> RunOwnedAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct)
        => await ExecuteAsync(repository, arguments, started, ct);

    public Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(processId);
            return Task.FromResult<bool?>(!process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks);
        }
        catch (ArgumentException) { return Task.FromResult<bool?>(false); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return Task.FromResult<bool?>(null); }
    }

    private async Task<LandingGitResult> ExecuteAsync(string repository, IReadOnlyList<string> arguments,
        Func<int, long, CancellationToken, Task>? started, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMinutes(5));
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        ConfigureProcess(start);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var mutating = arguments.Any(a => a is "rebase" or "merge" or "push" or "fetch" or "update-ref"
            or "add" or "remove" or "commit" or "checkout" or "checkout-index" or "restore" or "reset");
        var journal = mutating ? await RepositoryChildJournal.BeginAsync(repository, ct) : null;
        using var process = Process.Start(start) ?? throw new IOException("git_start_failed");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (journal is not null) await journal.StartedAsync(process, ct);
            if (started is not null) await started(process.Id, process.StartTime.ToUniversalTime().Ticks, ct);
            await process.WaitForExitAsync(budget.Token);
        }
        catch
        {
            // The Process handle, not a subsequently looked-up PID, identifies this child.
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, error);
            journal?.Exited(process);
            if (!ct.IsCancellationRequested && budget.IsCancellationRequested) throw new TimeoutException("git_timeout");
            throw;
        }
        // Descendants may retain redirected handles after the root exits. Keep the standing
        // journal until both streams drain; worker death in that interval must still fence admission.
        await Task.WhenAll(output, error); // Never expose Git stderr (endpoints/hooks may contain secrets).
        journal?.Exited(process);
        string? rebaseHead = null;
        if (process.ExitCode == 0 && arguments.Contains("rebase") && !arguments.Contains("--abort"))
        {
            var head = await ExecuteAsync(repository, ["rev-parse", "--verify", "HEAD^{commit}"], null, ct);
            if (head.Succeeded) rebaseHead = head.Output.Trim();
        }
        return new(process.ExitCode, await output,
            process.ExitCode == 0 ? "" : $"git_exit_{process.ExitCode}") { RebaseHeadSha = rebaseHead };
    }

    public Task<string> CanonicalDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (!info.Exists) throw new IOException("path_missing_or_inaccessible");
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("path_alias_unresolved");
        }
        return Task.FromResult(Path.TrimEndingDirectorySeparator(Path.GetFullPath(current)));
    }

    public async Task<string> CommonDirectoryAsync(string repository, CancellationToken ct)
    {
        var result = await RequiredAsync(repository, ["rev-parse", "--path-format=absolute", "--git-common-dir"], ct);
        return await CanonicalDirectoryAsync(result.Trim(), ct);
    }

    public async Task<IReadOnlyList<LandingRegistration>> RegistrationsAsync(string repository, CancellationToken ct)
        => ParseRegistrations(await RequiredAsync(repository, ["worktree", "list", "--porcelain", "-z"], ct));

    public async Task<bool> HasActiveSequencerAsync(string repository, CancellationToken ct)
        => HasSequencerAt(await CanonicalDirectoryAsync((await RequiredAsync(repository,
            ["rev-parse", "--absolute-git-dir"], ct)).Trim(), ct));

    private static bool HasSequencerAt(string admin)
    {
        foreach (var name in new[] { "rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "sequencer" })
        {
            try { _ = File.GetAttributes(Path.Combine(admin, name)); return true; }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return false;
    }

    public async Task<LandSourceInspection> InspectAsync(LandSourceCoordinates coordinates, CancellationToken ct)
    {
        try
        {
            if (coordinates.SourceFullRef == coordinates.TargetFullRef) return new(null, "source_equals_target");
            var before = await IdentityAsync(coordinates, ct);
            if (before.Reason is not null) return before;
            await ValidateBranchAsync(coordinates.RepositoryPath, coordinates.SourceFullRef, ct);
            await ValidateBranchAsync(coordinates.RepositoryPath, coordinates.TargetFullRef, ct);
            var snapshot = before.Snapshot!;
            if (HasSequencerAt(snapshot.GitDirectory)) return new(null, "active_sequencer");
            var statusArgs = new[] { "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none" };
            var status = await RunAsync(snapshot.RegisteredPath, statusArgs, ct);
            if (!status.Succeeded)
                return new(null, "status_error", LandFailureDiagnostic.FromCommand(statusArgs, status.ExitCode));
            var ignoredArgs = new[] { "ls-files", "--others", "--ignored", "--exclude-standard", "-z" };
            var ignored = await RunAsync(snapshot.RegisteredPath, ignoredArgs, ct);
            if (!ignored.Succeeded)
                return new(null, "ignored_status_error", LandFailureDiagnostic.FromCommand(ignoredArgs, ignored.ExitCode));
            var after = await IdentityAsync(coordinates, ct);
            if (after.Reason is not null || after.Snapshot != snapshot) return new(null, "source_changed");
            if (status.Output.Length != 0) return new(null, "source_dirty");
            return new(snapshot with { Status = status.Output,
                IgnoredPaths = ignored.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray() }, null);
        }
        catch (LandingGitCommandException ex)
        {
            return new(null, "identity_io_error", LandFailureDiagnostic.FromCommandException(ex));
        }
        catch (UnauthorizedAccessException ex)
        {
            return new(null, "identity_inaccessible", LandFailureDiagnostic.FromIo(ex));
        }
        catch (IOException ex)
        {
            return new(null, "identity_io_error", LandFailureDiagnostic.FromIo(ex));
        }
        catch (ArgumentException) { return new(null, "invalid_identity"); }
    }

    private async Task<LandSourceInspection> IdentityAsync(LandSourceCoordinates coordinates, CancellationToken ct)
    {
        var common = await CommonDirectoryAsync(coordinates.RepositoryPath, ct);
        var path = await CanonicalDirectoryAsync(coordinates.WorktreePath, ct);
        if (!PathsEqual(common, await CommonDirectoryAsync(path, ct))) return new(null, "wrong_repository");
        var registrations = ParseRegistrations(await RequiredAsync(coordinates.RepositoryPath,
            ["worktree", "list", "--porcelain", "-z"], ct));
        var matching = new List<LandingRegistration>();
        foreach (var entry in registrations)
        {
            // Missing unrelated registrations are retained, never silently adopted.
            if (PathsEqual(entry.Path, path)) matching.Add(entry);
            else if (Directory.Exists(entry.Path)
                     && PathsEqual(await CanonicalDirectoryAsync(entry.Path, ct), path)) matching.Add(entry);
        }
        if (matching.Count != 1) return new(null, "registration_mismatch");
        var registered = matching[0];
        if (registered.Locked || registered.Prunable) return new(null, "registration_unavailable");
        var symbolic = await RunAsync(path, ["symbolic-ref", "-q", "HEAD"], ct);
        if (symbolic.ExitCode == 1) return new(null, "detached_head");
        if (!symbolic.Succeeded) return new(null, "symbolic_head_error");
        if (symbolic.Output.Trim() != coordinates.SourceFullRef || registered.Branch != coordinates.SourceFullRef)
            return new(null, "source_branch_mismatch");
        var exists = await RunAsync(path, ["show-ref", "--exists", coordinates.SourceFullRef], ct);
        if (!exists.Succeeded) return new(null, exists.ExitCode == 2 ? "source_ref_missing" : "source_ref_error");
        var branch = await RunAsync(path, ["show-ref", "--verify", "--hash", coordinates.SourceFullRef], ct);
        if (!branch.Succeeded) return new(null, "source_ref_error");
        var head = await CommitAsync(path, "HEAD", ct);
        var branchSha = await CommitAsync(path, coordinates.SourceFullRef, ct);
        if (head != branchSha || head != registered.Head || head != branch.Output.Trim()) return new(null, "source_changed");
        var gitDirectory = await CanonicalDirectoryAsync((await RequiredAsync(path,
            ["rev-parse", "--absolute-git-dir"], ct)).Trim(), ct);
        return new(new(coordinates, common, path, gitDirectory, symbolic.Output.Trim(), head, branchSha, "", ImmutableArray<string>.Empty), null);
    }

    public async Task<LandingDestination> DestinationAsync(string repository, string targetFullRef, CancellationToken ct)
    {
        await ValidateBranchAsync(repository, targetFullRef, ct);
        var symbolic = await RunAsync(repository, ["symbolic-ref", "-q", targetFullRef], ct);
        if (symbolic.ExitCode != 1) throw new IOException("symbolic_destination");
        var endpoint = await EndpointAsync(repository, ct);
        return new("origin", targetFullRef, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint))));
    }

    private async Task<string> EndpointAsync(string repository, CancellationToken ct)
    {
        var result = await RequiredAsync(repository, ["remote", "get-url", "--push", "--all", "origin"], ct);
        var endpoints = result.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (endpoints.Length != 1 || endpoints[0].StartsWith('-')) throw new IOException("ambiguous_push_endpoint");
        return endpoints[0];
    }

    public async Task<LandingRemoteObservation> ObserveAsync(string repository, LandingDestination destination,
        string sourceSha, string observationRef, CancellationToken ct)
    {
        if (!IsOid(sourceSha) || !observationRef.StartsWith("refs/antiphon/land/", StringComparison.Ordinal))
            return new(null, false, "invalid_observation_identity");
        var check = await RunAsync(repository, ["check-ref-format", observationRef], ct);
        if (!check.Succeeded) return new(null, false, "invalid_observation_ref");
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await DestinationAsync(repository, destination.FullRef, ct) != destination)
                return new(null, false, "remote_configuration_changed");
            var endpoint = await EndpointAsync(repository, ct);
            var read = await RunAsync(repository, ["ls-remote", "--refs", "--exit-code", endpoint, destination.FullRef], ct);
            if (!read.Succeeded) return new(null, false, "remote_read_failed");
            var lines = read.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var fields = lines.Length == 1 ? lines[0].TrimEnd('\r').Split('\t') : [];
            if (fields.Length != 2 || fields[1] != destination.FullRef || !IsOid(fields[0]))
                return new(null, false, "remote_response_invalid");
            // A unique immutable observation ref avoids FETCH_HEAD/shared tracking state.
            var pin = $"{observationRef}/{Guid.NewGuid():N}";
            var fetch = await RunAsync(repository,
                ["fetch", "--no-tags", "--no-write-fetch-head", endpoint, $"{destination.FullRef}:{pin}"], ct);
            if (!fetch.Succeeded) return new(null, false, "remote_fetch_failed");
            var observed = await CommitAsync(repository, pin, ct);
            if (observed != fields[0]) continue;
            if (await DestinationAsync(repository, destination.FullRef, ct) != destination)
                return new(null, false, "remote_configuration_changed");
            var ancestry = await RunAsync(repository, ["merge-base", "--is-ancestor", sourceSha, observed], ct);
            return ancestry.ExitCode switch
            {
                0 => new(observed, true, null),
                1 => new(observed, false, null),
                _ => new(null, false, "remote_ancestry_error"),
            };
        }
        return new(null, false, "remote_changed_during_confirmation");
    }

    public async Task<LandingSourceObservation> ObserveSourceAsync(string repository, string sourceFullRef,
        string observationPrefix, CancellationToken ct)
    {
        if (!sourceFullRef.StartsWith("refs/heads/", StringComparison.Ordinal)
            || !observationPrefix.StartsWith("refs/antiphon/land/", StringComparison.Ordinal))
            return new(null, null, null, "invalid_observation_identity");
        var check = await RunAsync(repository, ["check-ref-format", observationPrefix + "/probe"], ct);
        if (!check.Succeeded) return new(null, null, null, "invalid_observation_ref");
        string? fingerprint;
        try { fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(await EndpointAsync(repository, ct)))); }
        catch (IOException) { return new(null, null, null, "source_remote_endpoint_ambiguous"); }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            string endpoint;
            try { endpoint = await EndpointAsync(repository, ct); }
            catch (IOException) { return new(null, null, null, "source_remote_endpoint_ambiguous"); }
            var currentFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
            if (currentFingerprint != fingerprint)
                return new(null, null, null, "source_remote_endpoint_changed");

            var read = await RunAsync(repository, ["ls-remote", "--refs", "--exit-code", endpoint, sourceFullRef], ct);
            if (read.ExitCode == 2) return new(null, null, fingerprint, "source_remote_missing");
            if (!read.Succeeded) return new(null, null, fingerprint, "source_remote_unreadable");
            var lines = read.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var fields = lines.Length == 1 ? lines[0].TrimEnd('\r').Split('\t') : [];
            if (fields.Length != 2 || fields[1] != sourceFullRef || !IsOid(fields[0]))
                return new(null, null, fingerprint, "source_remote_response_invalid");

            var pin = $"{observationPrefix}/{Guid.NewGuid():N}";
            var pinCheck = await RunAsync(repository, ["check-ref-format", pin], ct);
            if (!pinCheck.Succeeded) return new(null, null, fingerprint, "invalid_observation_ref");
            var fetch = await RunAsync(repository,
                ["fetch", "--no-tags", "--no-write-fetch-head", endpoint, $"{sourceFullRef}:{pin}"], ct);
            if (!fetch.Succeeded) return new(null, null, fingerprint, "source_remote_fetch_failed");
            string observed;
            try { observed = await CommitAsync(repository, pin, ct); }
            catch (IOException) { return new(null, null, fingerprint, "source_remote_commit_invalid"); }
            if (observed != fields[0]) continue;
            try
            {
                var after = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(await EndpointAsync(repository, ct))));
                if (after != fingerprint) return new(null, null, fingerprint, "source_remote_endpoint_changed");
            }
            catch (IOException) { return new(null, null, fingerprint, "source_remote_endpoint_ambiguous"); }
            return new(observed, pin, fingerprint, null);
        }

        return new(null, null, fingerprint, "source_remote_changed_during_confirmation");
    }

    public async Task<LandingGitResult> PinAsync(string repository, string recoveryRef, string sha, CancellationToken ct)
    {
        if (!IsOid(sha) || !recoveryRef.StartsWith("refs/antiphon/land/", StringComparison.Ordinal))
            return new(1, "", "invalid_recovery_identity");
        var valid = await RunAsync(repository, ["check-ref-format", recoveryRef], ct);
        if (!valid.Succeeded) return new(1, "", "invalid_recovery_ref");
        var existing = await RunAsync(repository, ["show-ref", "--verify", "--hash", recoveryRef], ct);
        if (existing.Succeeded)
            return existing.Output.Trim() == sha ? new(0, "", "") : new(1, "", "recovery_ref_collision");
        // show-ref --verify uses 128 for a missing named ref, so use --exists (Git >= 2.46)
        // to distinguish absence (2) from a lookup error (1) before expected-old creation.
        var existence = await RunAsync(repository, ["show-ref", "--exists", recoveryRef], ct);
        if (existence.ExitCode != 2) return new(1, "", "recovery_ref_query_error");
        if (await CommitAsync(repository, sha, ct) != sha) return new(1, "", "invalid_recovery_commit");
        return await RunAsync(repository, ["update-ref", recoveryRef, sha, new string('0', sha.Length)], ct);
    }

    public async Task<LandingGitResult> PushAsync(string repository, LandingDestination destination, string sha, CancellationToken ct)
    {
        if (!IsOid(sha)) return new(1, "", "invalid_push_commit");
        if (await DestinationAsync(repository, destination.FullRef, ct) != destination)
            return new(1, "", "remote_configuration_changed");
        var endpoint = await EndpointAsync(repository, ct);
        return await RunAsync(repository, ["push", endpoint, $"{sha}:{destination.FullRef}"], ct);
    }

    public async Task<LandingGitResult> PushOwnedAsync(string repository, LandingDestination destination, string sha,
        Func<int, long, CancellationToken, Task> started, CancellationToken ct)
    {
        if (!IsOid(sha)) return new(1, "", "invalid_push_commit");
        if (await DestinationAsync(repository, destination.FullRef, ct) != destination)
            return new(1, "", "remote_configuration_changed");
        var endpoint = await EndpointAsync(repository, ct);
        return await RunOwnedAsync(repository, ["push", endpoint, $"{sha}:{destination.FullRef}"], started, ct);
    }

    internal async Task<string> CommitAsync(string repository, string revision, CancellationToken ct)
    {
        var sha = (await RequiredAsync(repository, ["rev-parse", "--verify", $"{revision}^{{commit}}"], ct)).Trim();
        if (!IsOid(sha)) throw new IOException("invalid_commit");
        return sha;
    }

    private async Task ValidateBranchAsync(string repository, string fullRef, CancellationToken ct)
    {
        if (!fullRef.StartsWith("refs/heads/", StringComparison.Ordinal) || fullRef[11..].StartsWith('-'))
            throw new ArgumentException("invalid_branch");
        var valid = await RunAsync(repository, ["check-ref-format", fullRef], ct);
        if (!valid.Succeeded) throw new ArgumentException("invalid_branch");
    }

    private async Task<string> RequiredAsync(string repository, IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await RunAsync(repository, args, ct);
        if (!result.Succeeded)
        {
            var template = LandFailureDiagnostic.CommandTemplate(args);
            if (string.IsNullOrEmpty(template)) throw new IOException(result.Diagnostic);
            throw new LandingGitCommandException(template, result.ExitCode, $"git_exit_{result.ExitCode}");
        }
        return result.Output;
    }

    internal static bool IsOid(string value) => value.Length is 40 or 64
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool PathsEqual(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    internal static IReadOnlyList<LandingRegistration> ParseRegistrations(string text)
    {
        var rows = new List<LandingRegistration>();
        string? path = null, branch = null, head = null;
        var locked = false;
        var prunable = false;
        var fieldsSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in text.Split('\0'))
        {
            if (field.Length == 0)
            {
                if (fieldsSeen.Count != 0)
                {
                    if (string.IsNullOrEmpty(path) || (fieldsSeen.Contains("bare")
                        ? head is not null || branch is not null || fieldsSeen.Contains("detached")
                        : head is null || !IsOid(head) || (branch is null) == !fieldsSeen.Contains("detached")))
                        throw new IOException("registration_parse_error");
                    rows.Add(new(path, branch, head, locked, prunable));
                }
                path = branch = head = null;
                locked = prunable = false;
                fieldsSeen.Clear();
                continue;
            }
            var key = field.Split(' ', 2)[0];
            if (!fieldsSeen.Add(key) || (key != "worktree" && path is null))
                throw new IOException("registration_parse_error");
            if (field.StartsWith("worktree ", StringComparison.Ordinal)) path = field[9..];
            else if (field.StartsWith("branch ", StringComparison.Ordinal)) branch = field[7..];
            else if (field.StartsWith("HEAD ", StringComparison.Ordinal)) head = field[5..];
            else if (field == "locked" || field.StartsWith("locked ", StringComparison.Ordinal)) locked = true;
            else if (field == "prunable" || field.StartsWith("prunable ", StringComparison.Ordinal)) prunable = true;
            else if (field is not ("detached" or "bare")) throw new IOException("registration_parse_error");
        }
        if (path is not null) throw new IOException("registration_truncated");
        if (rows.Count == 0) throw new IOException("registration_empty");
        return rows;
    }
}
