using System.Diagnostics;
using System.Text;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Git;

public sealed class CardFileRepository(
    GitProcessGate processGate, IOptions<GitSettings> settings,
    ILogger<CardFileRepository> logger) : ICardFileRepository
{
    private readonly GitProcessGate _processGate = processGate;
    private readonly GitSettings _gitSettings = settings.Value;
    private readonly ILogger<CardFileRepository> _logger = logger;

    public void ValidatePath(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw UnsafePath();
        // Include every ancestor: an alias/junction above the configured root is unsafe too.
        for (var current = fullPath; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw UnsafePath();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static ConflictException UnsafePath() => new(
        "Card-file path is not safely contained; repair the target before retrying.", "unsafe_card_file_path");

    private bool IsManagedPath(string root, string directory, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!string.Equals(Path.GetDirectoryName(path), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
            return false;
        ValidatePath(root, path);
        return true;
    }

    public async Task<IReadOnlyList<string>> GetManagedGitPathsAsync(string root, string directory, CancellationToken ct)
    {
        ValidatePath(root, directory);
        var index = await RunGitAsync(root, ct, "ls-files", "-z", "--", directory);
        if (index.ExitCode != 0) throw new IOException(TrimError(index));
        var head = await RunGitAsync(root, ct, "ls-tree", "-r", "--name-only", "-z", "HEAD");
        if (head.ExitCode != 0) throw new IOException(TrimError(head));
        return (index.Stdout + head.Stdout).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => IsManagedPath(root, directory, p)).Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task<CardFileRepositoryState> InspectAsync(string root, string directory, CancellationToken ct)
    {
        ValidatePath(root, directory);
        var working = Directory.Exists(directory) ? Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(p => string.Equals(Path.GetExtension(p), ".md", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray() : [];
        foreach (var path in working) ValidatePath(root, Path.Combine(root, path));
        var indexResult = await RunGitAsync(root, ct, "ls-files", "--stage", "-z");
        var headResult = await RunGitAsync(root, ct, "ls-tree", "-r", "-z", "HEAD");
        if (indexResult.ExitCode != 0 || headResult.ExitCode != 0) throw new IOException("Card-file Git inspection failed.");
        Dictionary<string, string> Parse(string text)
        {
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var tab = entry.IndexOf('\t');
                if (tab < 0) continue;
                var path = entry[(tab + 1)..];
                if (!IsManagedPath(root, directory, path)) continue;
                var metadata = entry[..tab].Split(' ');
                entries[path] = metadata[1] == "blob" ? metadata[2] : metadata[1];
            }
            return entries;
        }
        return new(working, Parse(indexResult.Stdout), Parse(headResult.Stdout));
    }

    public async Task<string> HashAsync(string root, string relativePath, string content, CancellationToken ct)
    {
        var result = await RunGitInputAsync(root, content, ct, "hash-object", "--path", relativePath, "--stdin");
        if (result.ExitCode != 0) throw new IOException("Card-file hashing failed.");
        return result.Stdout.Trim();
    }

    public async Task UnstageAsync(string root, string directory, IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        var state = await InspectAsync(root, directory, ct);
        foreach (var path in paths)
            if (!IsManagedPath(root, directory, path) || state.Head.ContainsKey(path)) throw UnsafePath();
        var result = await RunGitInputAsync(root, string.Concat(paths.Select(p => p + "\0")), ct,
            "restore", "--staged", "--pathspec-from-file=-", "--pathspec-file-nul");
        if (result.ExitCode != 0) throw new IOException("Card-file unstaging failed.");
    }

    public async Task<string?> ReadAsync(string root, string path, CancellationToken ct)
    {
        ValidatePath(root, path);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }

    public void Delete(string root, string path)
    {
        ValidatePath(root, path);
        File.Delete(path);
    }

    public async Task WriteAsync(string root, string path, string content, Guid boardId, CancellationToken ct)
    {
        ValidatePath(root, path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".antiphon-card-files-{boardId:N}-{Guid.NewGuid():N}.tmp");
        ValidatePath(root, temp);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(new UTF8Encoding(false).GetBytes(content), ct);
            await stream.FlushAsync(ct);
        }
        ct.ThrowIfCancellationRequested();
        ValidatePath(root, path);
        File.Move(temp, path, overwrite: true);
    }

    public void RemoveTemporaryFiles(string root, string directory, Guid boardId)
    {
        ValidatePath(root, directory);
        if (!Directory.Exists(directory)) return;
        foreach (var temp in Directory.GetFiles(directory, $".antiphon-card-files-{boardId:N}-*.tmp", SearchOption.TopDirectoryOnly))
            Delete(root, temp);
    }

    public async Task<bool> IsIgnoredAsync(string root, IReadOnlyList<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0) return false;
        var result = await RunGitInputAsync(root, string.Concat(paths.Select(p => p + "\0")), ct,
            "check-ignore", "--no-index", "-z", "--stdin");
        if (result.ExitCode is not (0 or 1)) throw new IOException("Card-file ignore inspection failed.");
        return result.ExitCode == 0;
    }

    public async Task<bool> HasIgnoreProtectionAsync(string root, IReadOnlyList<string> enabledSlugs, CancellationToken ct)
    {
        // Accept the documented deny-default grammar. Unknown broad exceptions cannot prove protection.
        var text = await ReadAsync(root, Path.Combine(root, ".gitignore"), ct) ?? "";
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).ToArray();
        const string begin = "# BEGIN ANTIPHON CARD FILES", end = "# END ANTIPHON CARD FILES";
        if (lines.Count(l => l == begin) != lines.Count(l => l == end) || lines.Count(l => l == begin) > 1) return false;
        if (lines.Contains(begin) && Array.IndexOf(lines, begin) >= Array.IndexOf(lines, end)) return false;
        var hasBlanket = lines.Any(l => l is "/docs/cards/" or "docs/cards/" or "/docs/cards/*" or "docs/cards/*");
        if (!hasBlanket) return false;
        foreach (var line in lines.Where(l => l.StartsWith('!')))
        {
            var prefix = "!/docs/cards/";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                // A negation outside this namespace is harmless only when its first literal component is disjoint.
                var first = line.TrimStart('!', '/').Split('/')[0];
                if (first == "docs" || first.IndexOfAny(['*', '?', '[', '\\']) >= 0 || !line.Contains('/')) return false;
                continue;
            }
            var slug = line[prefix.Length..].TrimEnd('/');
            if (!line.EndsWith('/') || !enabledSlugs.Contains(slug, StringComparer.Ordinal)
                || slug != CardTaskFileRenderer.BoardSlug(slug)) return false;
        }
        var probe = $"docs/cards/c408-unused-{Guid.NewGuid():N}/probe.md";
        return await IsIgnoredAsync(root, [probe], ct);
    }

    public async Task InstallIgnoreAsync(string root, CancellationToken ct)
    {
        ValidatePath(root, root);
        if (await HasIgnoreProtectionAsync(root, [], ct)) return;
        var path = Path.Combine(root, ".gitignore");
        var text = await ReadAsync(root, path, ct) ?? "";
        if (text.Contains("# BEGIN ANTIPHON CARD FILES") || text.Contains("# END ANTIPHON CARD FILES"))
            throw new IOException("Card-file ignore block requires owner repair.");
        var bytes = File.Exists(path) ? await File.ReadAllBytesAsync(path, ct) : [];
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var addition = (bytes.Length > 0 && !text.EndsWith('\n') ? newline : "")
            + "# BEGIN ANTIPHON CARD FILES" + newline + "/docs/cards/" + newline + "# END ANTIPHON CARD FILES" + newline;
        var temp = path + $".antiphon-{Guid.NewGuid():N}.tmp";
        ValidatePath(root, temp);
        try
        {
            await File.WriteAllBytesAsync(temp, bytes.Concat(new UTF8Encoding(false).GetBytes(addition)).ToArray(), ct);
            ValidatePath(root, path);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<CardFileCommitResult> CommitAsync(string root, string directory,
        IReadOnlyDictionary<string, string?> expected, string subject, CancellationToken ct)
    {
        if (expected.Count == 0) return new(null, "nothing_to_commit", null);
        ValidatePath(root, directory);
        foreach (var path in expected.Keys)
            if (!IsManagedPath(root, directory, path)) throw UnsafePath();
        foreach (var (marker, skip) in new[] { ("rebase-merge", "rebase_in_progress"),
            ("rebase-apply", "rebase_in_progress"), ("MERGE_HEAD", "merge_in_progress"),
            ("CHERRY_PICK_HEAD", "cherry_pick_in_progress") })
        {
            var check = await GitPathExistsAsync(root, marker, ct);
            if (check.Error is not null) return new(null, "git_error", check.Error);
            if (check.Exists) return new(null, skip, null);
        }
        if ((await RunGitAsync(root, ct, "symbolic-ref", "-q", "HEAD")).ExitCode != 0)
            return new(null, "detached_head", null);
        var unmerged = await RunGitAsync(root, ct, "diff", "--name-only", "-z", "--diff-filter=U");
        if (unmerged.ExitCode != 0) return GitError(unmerged);
        if (unmerged.Stdout.Split('\0').Any(expected.ContainsKey)) return new(null, "conflicted_paths", null);
        if (!await MatchesAsync(root, expected, ct)) return new(null, "generated_files_changed", null);
        var state = await InspectAsync(root, directory, ct);
        var addPaths = expected.Keys.Where(p => state.Index.ContainsKey(p) || File.Exists(Path.Combine(root, p))).ToArray();
        var input = string.Concat(addPaths.Select(p => p + "\0"));
        var add = addPaths.Length == 0 ? new GitCommandResult(0, "", "") : await RunGitInputAsync(root, input, ct, "add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul");
        if (add.ExitCode != 0) return GitError(add);
        // Rebuild after staging: canceled index-only additions are no longer valid commit paths.
        var changed = await RunGitAsync(root, ct, "diff", "--cached", "--name-only", "-z", "--no-renames", "HEAD");
        if (changed.ExitCode != 0) return GitError(changed);
        var commitPaths = changed.Stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(expected.ContainsKey).ToArray();
        if (commitPaths.Length == 0) return new(null, "nothing_to_commit", null);
        if (!await MatchesAsync(root, expected, ct)) return new(null, "generated_files_changed", null);
        var commit = await RunGitInputAsync(root, string.Concat(commitPaths.Select(p => p + "\0")), ct,
            "commit", "--only", "-m", subject.Replace('\r', ' ').Replace('\n', ' '),
            "--trailer", "antiphon=true", "--pathspec-from-file=-", "--pathspec-file-nul");
        if (commit.ExitCode != 0) return GitError(commit);
        var head = await RunGitAsync(root, ct, "rev-parse", "HEAD");
        return head.ExitCode == 0 ? new(head.Stdout.Trim(), null, null) : GitError(head);
    }

    private async Task<bool> MatchesAsync(string root, IReadOnlyDictionary<string, string?> expected, CancellationToken ct)
    {
        foreach (var (relative, content) in expected)
        {
            var path = Path.Combine(root, relative);
            ValidatePath(root, path);
            if (content is null)
            {
                if (File.Exists(path) || Directory.Exists(path)) return false;
            }
            else if (!File.Exists(path) || Normalize(await File.ReadAllTextAsync(path, ct)) != Normalize(content)) return false;
        }
        return true;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
    private async Task<(bool Exists, string? Error)> GitPathExistsAsync(
        string repoPath, string gitPath, CancellationToken ct)
    {
        var result = await RunGitAsync(repoPath, ct, "rev-parse", "--git-path", gitPath);
        if (result.ExitCode != 0)
            return (false, TrimError(result));

        var path = result.Stdout.Trim();
        if (path.Length == 0)
            return (false, null);

        var full = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(repoPath, path));
        return (Directory.Exists(full) || File.Exists(full), null);
    }

    private async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        CancellationToken ct,
        params string[] arguments) =>
        await RunGitInputAsync(workingDirectory, null, ct, arguments);

    private async Task<GitCommandResult> RunGitInputAsync(
        string workingDirectory, string? input, CancellationToken ct, params string[] arguments)
    {
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _gitSettings.ExecutableName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardInput = input is not null,
                StandardInputEncoding = input is null ? null : new UTF8Encoding(false),
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["GIT_LITERAL_PATHSPECS"] = "1";
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var lease = await _processGate.EnterAsync(ct);
            process = Process.Start(psi);
            if (process is null)
                return new GitCommandResult(-1, "", $"{_gitSettings.ExecutableName} failed to start");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _gitSettings.TimeoutSeconds));
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeoutCts.Token);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(timeoutCts.Token);
            return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (ct.IsCancellationRequested)
                throw;

            _logger.LogWarning("Card-file Git command timed out; child killed");
            return new GitCommandResult(-1, "", "timeout");
        }
        catch (Exception)
        {
            TryKill(process);
            _logger.LogDebug("Card-file Git command failed");
            return new GitCommandResult(-1, "", "Git command failed");
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // A concurrently-exiting child is already the desired state.
        }
    }

    private static CardFileCommitResult GitError(GitCommandResult result) =>
        new(null, "git_error", TrimError(result));

    private static string TrimError(GitCommandResult result) =>
        "Card-file Git operation failed; retry reconciliation.";

    private sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);
}
