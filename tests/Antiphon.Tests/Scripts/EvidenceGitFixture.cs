using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;

namespace Antiphon.Tests.Scripts;

/// <summary>Owned, bounded children and object-only Git plumbing; borrowed objects are read-only.</summary>
internal sealed class EvidenceGitFixture : IAsyncDisposable
{
    internal const string Anchor = "bb5fa774cd56f85ee6f0b1122c198192427e5ddf";
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "c1015-" + Guid.NewGuid().ToString("N"));
    internal string Repo => Path.Combine(Root, "repo");
    internal string Base { get; private set; } = "";
    internal static string Source => DelegateScriptRunner.RepoRoot;
    internal record Result(int Exit, byte[] Bytes, string Error)
    {
        internal string Output => Encoding.UTF8.GetString(Bytes) + Error;
    }
    internal record Entry(string Path, string Mode, string Oid, long Bytes, string Type);
    internal record Inventory(Entry[] Delete, Entry[] Keep)
    {
        internal long Bytes => Delete.Sum(x => x.Bytes);
        internal string Digest => PathHash(Delete);
    }

    internal static async Task<EvidenceGitFixture> CreateAsync(bool anchored = false)
    {
        var f = new EvidenceGitFixture();
        try
        {
            Directory.CreateDirectory(f.Root);
            Directory.CreateDirectory(Path.Combine(f.Root, "home"));
            if (anchored)
                await f.RequiredAsync("git", ["clone", "--shared", "--no-checkout", "--", Source, f.Repo], cwd: f.Root);
            else
            {
                Directory.CreateDirectory(f.Repo);
                await f.GitAsync("init", "-b", "master");
            }
            await f.GitAsync("config", "user.name", "Evidence Fixture");
            await f.GitAsync("config", "user.email", "evidence@example.invalid");
            await f.GitAsync("config", "commit.gpgsign", "false");
            await f.GitAsync("config", "core.hooksPath", Path.Combine(f.Root, "no-hooks"));
            await f.GitAsync("config", "credential.helper", "");
            await f.GitAsync("read-tree", "--empty");
            if (anchored)
            {
                // Only borrow the evidence subtree, without checking out any historical payload.
                var tree = (await f.GitAsync("rev-parse", Anchor + ":.antiphon")).Trim();
                await f.GitAsync("read-tree", "--prefix=.antiphon/", tree);
            }
            await f.PutAsync("outside.txt", Encoding.UTF8.GetBytes("fixture source"));
            f.Base = await f.CommitIndexAsync(anchored ? [Anchor] : [], "fixture base");
            await f.GitAsync("update-ref", "refs/heads/master", f.Base);
            await f.GitAsync("symbolic-ref", "HEAD", "refs/heads/master");
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    internal async Task<Result> RunAsync(string file, IEnumerable<string> arguments, byte[]? input = null, string? cwd = null)
    {
        var start = new ProcessStartInfo(file)
        {
            WorkingDirectory = cwd ?? Repo, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(Root, "home", "empty-config");
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("owned child did not start");
        using var stdout = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            if (input is not null) await process.StandardInput.BaseStream.WriteAsync(input, timeout.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            await copy;
            return new Result(process.ExitCode, stdout.ToArray(), await error);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await copy;
            await error;
            throw;
        }
    }

    internal async Task<string> RequiredAsync(string file, IEnumerable<string> arguments, byte[]? input = null, string? cwd = null)
    {
        var r = await RunAsync(file, arguments, input, cwd);
        r.Exit.ShouldBe(0, "fixture setup: " + r.Output);
        return r.Output;
    }
    internal Task<string> GitAsync(params string[] args) => RequiredAsync("git", args);

    internal async Task<string> PutAsync(string path, byte[] bytes, string mode = "100644")
    {
        var oid = (await RequiredAsync("git", ["hash-object", "-w", "--stdin"], bytes)).Trim();
        await SetAsync(path, oid, mode);
        return oid;
    }
    internal Task SetAsync(string path, string oid, string mode = "100644") =>
        RequiredAsync("git", ["update-index", "-z", "--index-info"], Encoding.UTF8.GetBytes(mode + " " + oid + "\t" + path + "\0"));
    internal Task RemoveAsync(string path) => SetAsync(path, new string('0', 40), "0");
    internal async Task<string> CommitIndexAsync(string[] parents, string message = "fixture commit")
    {
        var tree = (await GitAsync("write-tree")).Trim();
        var args = new List<string> { "commit-tree", tree };
        foreach (var parent in parents) { args.Add("-p"); args.Add(parent); }
        args.Add("-m"); args.Add(message);
        return (await RequiredAsync("git", args)).Trim();
    }
    internal async Task<string> AddCommitAsync(string parent, string path, byte[] bytes, string mode = "100644")
    {
        await GitAsync("read-tree", parent);
        await PutAsync(path, bytes, mode);
        return await CommitIndexAsync([parent]);
    }
    internal async Task<string> DeleteCommitAsync(string parent, params string[] paths)
    {
        await GitAsync("read-tree", parent);
        foreach (var path in paths) await RemoveAsync(path);
        return await CommitIndexAsync([parent]);
    }
    internal static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    internal Task<Result> HistoryAsync(string baseRef, string headRef, string? hook = null) =>
        GuardAsync("check-evidence-diff.ps1", ["-BaseRef", baseRef, "-HeadRef", headRef], hook, "Invoke-EvidenceHistory");
    internal Task<Result> EventAsync(string eventName, object data, string? manualBase = null, string? manualHead = null)
    {
        var path = Path.Combine(Root, "event-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(data));
        return GuardAsync("check-evidence-diff.ps1", ["-EventPath", path, "-EventName", eventName, "-ManualBase", manualBase ?? "", "-ManualHead", manualHead ?? ""]);
    }
    internal static object Push(string head, string before, string branch = "feat/fixture", object? deleted = null) =>
        new { @ref = "refs/heads/" + branch, before, after = head, deleted = deleted ?? false, repository = new { default_branch = "master" } };
    internal async Task<Result> GuardAsync(string script, string[] args, string? hook = null, string? entry = null)
    {
        var path = Path.Combine(Source, "scripts", script);
        File.Exists(path).ShouldBeTrue("c1015-policy-entry-installed");
        if (hook is null)
            return await RunAsync("pwsh", new[] { "-NoProfile", "-NonInteractive", "-File", path, "-Repository", Repo }.Concat(args));
        var body = ". " + Quote(Path.Combine(Source, "scripts", "lib", "evidence-policy.ps1")) + "\n" + hook + "\n" +
            "$code = " + entry + " -Repository " + Quote(Repo) + " " + string.Join(" ", args.Select((v, i) => i % 2 == 0 ? v : Quote(v))) + "\nexit $code\n";
        return await RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(body))]);
    }
    internal async Task<Entry[]> TreeAsync(string commit)
    {
        var r = await RunAsync("git", ["ls-tree", "-r", "-z", "-l", "--full-tree", commit]);
        r.Exit.ShouldBe(0, r.Output);
        return new UTF8Encoding(false, true).GetString(r.Bytes).Split('\0').Where(x => x.Length > 0).Select(x =>
        {
            var tab = x.IndexOf('\t');
            var fields = x[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new Entry(x[(tab + 1)..], fields[0], fields[2], fields[1] == "commit" ? 0 : long.Parse(fields[3]), fields[1]);
        }).ToArray();
    }
    // Independent C# oracle, never sourced from production or candidate diff.
    internal static bool Reject(Entry e)
    {
        var parts = e.Path.Split('/');
        if (!string.Equals(parts[0], ".antiphon", StringComparison.OrdinalIgnoreCase)) return false;
        return parts.Skip(1).SkipLast(1).Any(p => p.Contains("checkpoints", StringComparison.OrdinalIgnoreCase)) ||
            e.Mode is not ("100644" or "100755") ||
            (!parts[^1].EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !ApprovedFixture(parts)) || e.Bytes > 1_048_576;
    }
    private static bool ApprovedFixture(string[] parts)
    {
        const string suffix = ".approved.json";
        if (parts.Length < 3 || parts[0] != ".antiphon" || parts[1] != "fixtures" ||
            !parts[^1].EndsWith(suffix, StringComparison.Ordinal)) return false;
        var names = parts.Skip(2).SkipLast(1).Append(parts[^1][..^suffix.Length]);
        return names.All(name => name.Length > 0 && name.All(c =>
            c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-'));
    }
    internal static bool Scoped(Entry e) => e.Path.Split('/')[0].Equals(".antiphon", StringComparison.OrdinalIgnoreCase);
    internal async Task<Inventory> InventoryAsync(string commit)
    {
        var entries = await TreeAsync(commit);
        return new Inventory(entries.Where(Reject).ToArray(), entries.Where(x => Scoped(x) && !Reject(x)).ToArray());
    }
    internal static string PathHash(IEnumerable<Entry> rows) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(rows.Select(x => x.Path + "\0"))))).ToLowerInvariant();
    internal async Task<string> SnapshotAsync()
    {
        var refs = await GitAsync("show-ref");
        var head = await GitAsync("symbolic-ref", "HEAD");
        var index = File.ReadAllBytes(Path.Combine(Repo, ".git", "index"));
        var files = Directory.EnumerateFiles(Repo, "*", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(Repo, p).StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(Repo, p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        return refs + head + Convert.ToHexString(SHA256.HashData(index)) + string.Join("\n", files);
    }
    internal async Task<string> ExactDeletionAsync(string baseRef, Inventory inv, string? parent = null, string? message = null)
    {
        await GitAsync("read-tree", parent ?? baseRef);
        foreach (var e in inv.Delete) await RemoveAsync(e.Path);
        return await CommitIndexAsync([parent ?? baseRef], message ?? "remove rejected evidence\n\nAntiphon-Evidence-Deletion: CARD-1015");
    }
    internal Task<Result> DeletionAsync(string inventoryRef, string head, Inventory inv, string? count = null, string? digest = null, string? bytes = null, string? hook = null) =>
        GuardAsync("check-evidence-deletion.ps1", ["-InventoryRef", inventoryRef, "-HeadRef", head,
            "-InventoryCount", count ?? inv.Delete.Length.ToString(), "-InventoryPathSha256", digest ?? inv.Digest,
            "-InventoryBytes", bytes ?? inv.Bytes.ToString()], hook, "Invoke-EvidenceDeletion");
    internal Task<Result> InventoryOnlyAsync(string inventoryRef, string? hook = null) =>
        GuardAsync("check-evidence-deletion.ps1", ["-InventoryRef", inventoryRef, "-InventoryOnly"], hook, "Invoke-EvidenceDeletion");
    public ValueTask DisposeAsync()
    {
        StartRefGit.DeleteDirectory(Root);
        Directory.Exists(Root).ShouldBeFalse("fixture-owned root removed; alternate objects untouched");
        return ValueTask.CompletedTask;
    }
}
