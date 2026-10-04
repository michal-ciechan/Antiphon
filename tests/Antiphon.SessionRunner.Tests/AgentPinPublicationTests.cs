using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// The red-first commit used reflection before the dormant primitive existed.
// The implementation now uses its typed boundary; oracles inspect independent bytes.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentPinPublicationTests
{
    [Test]
    public async Task V34_PublicationAdmission()
    {
        using var f = new Fixture();
        foreach (var (field, value, reason) in new (string, object, string)[]
        {
            ("SchemaVersion", 99, "pin_schema_unsupported"),
            ("AgentId", Guid.Empty, "pin_owner_invalid"),
            ("TargetOwnerId", Guid.NewGuid(), "pin_owner_mismatch"),
            ("RunnerStoreId", Guid.NewGuid(), "pin_store_mismatch"),
            ("Action", 99, "pin_action_unsupported"),
            ("Sha256", new string('0', 64), "pin_digest_mismatch"),
            ("Fence", 0, "pin_operation_invalid"),
            ("OperationId", Guid.Empty, "pin_operation_invalid"),
            ("LocationGeneration", Guid.Empty, "pin_operation_invalid"),
            ("Cwd", "../escape", "pin_cwd_invalid")
        })
        {
            var request = f.Request();
            request[field] = JsonSerializer.SerializeToNode(value);
            var guard = field switch
            {
                "Sha256" => "c262-g190", "TargetOwnerId" => "c262-g200", "SchemaVersion" => "c262-g201",
                "RunnerStoreId" => "c262-g202", "Action" => "c262-g203", _ => field
            };
            Refused(await f.Apply(request), reason, guard);
            Directory.GetFileSystemEntries(f.Cwd).ShouldBeEmpty(guard + ": reject before write");
            Directory.GetFileSystemEntries(f.Journal).ShouldBeEmpty();
        }
        var missing = f.Request();
        missing["Cwd"] = Path.Combine(f.Root, "absent");
        Refused(await f.Apply(missing), "pin_cwd_missing");
        Directory.Exists(Path.Combine(f.Root, "absent")).ShouldBeFalse();
        // Until native Git excludes land, Git workspaces fail closed, including ancestors.
        File.WriteAllText(Path.Combine(f.Root, ".git"), "gitdir: elsewhere");
        Refused(await f.Apply(f.Request()), "pin_git_not_qualified");
        File.Delete(Path.Combine(f.Root, ".git"));
        var large = f.Request(content: new string('x', AgentPinWorkspaceStore.MaxInspectionBytes + 1));
        Refused(await f.Apply(large), "pin_content_invalid");
        var invalidCleanup = f.Request();
        invalidCleanup["Action"] = 1;
        Refused(await f.Apply(invalidCleanup), "pin_content_invalid");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => new AgentPinWorkspacePublisher(f.Store, f.Journal)
            .ApplyAsync(f.Request().Deserialize<AgentPinPublicationRequest>()!, canceled.Token));
        Directory.GetFileSystemEntries(f.Journal).ShouldBeEmpty();
        var legal = f.Request();
        Receipt(await f.Apply(legal), legal, f, "first pins\n");
        File.ReadAllText(f.Target).ShouldBe("first pins\n", "c262-g002: actual publication");
    }

    [Test]
    public async Task V14_PublicationReplay()
    {
        foreach (var cut in new[] { "intent", "before-compare", "published" })
        {
            using var f = new Fixture();
            var request = f.Request();
            var result = await f.Apply(request, phase => phase == cut
                ? Task.FromException(new IOException("injected crash " + cut)) : Task.CompletedTask);
            result["Status"]!.GetValue<int>().ShouldBe(2);
            if (cut == "published") File.ReadAllText(f.Target).ShouldBe("first pins\n");
            else File.Exists(f.Target).ShouldBeFalse();
            // Each call constructs a new host object reading the durable journal.
            Receipt(await f.Apply(request), request, f, "first pins\n");
            var time = File.GetLastWriteTimeUtc(f.Target);
            Receipt(await f.Apply(request), request, f, "first pins\n");
            File.GetLastWriteTimeUtc(f.Target).ShouldBe(time, "replay must not rewrite bytes");
            Directory.GetFiles(Path.GetDirectoryName(f.Target)!, "*.tmp").ShouldBeEmpty();
            File.WriteAllText(f.Target, "editor after receipt");
            Refused(await f.Apply(request), "pin_bytes_conflict");
            File.ReadAllText(f.Target).ShouldBe("editor after receipt", "c262-g151: receipts recheck disk");
        }
        using var replacement = new Fixture();
        var initial = replacement.Request();
        Receipt(await replacement.Apply(initial), initial, replacement, "first pins\n");
        var next = replacement.Request(2, "complete replacement\n", Hash("first pins\n"));
        var failed = await replacement.Apply(next, phase =>
        {
            if (phase != "before-compare") return Task.CompletedTask;
            File.ReadAllText(replacement.Target).ShouldBe("first pins\n", "c262-g043: old complete bytes before rename");
            Directory.GetFiles(Path.GetDirectoryName(replacement.Target)!, "*.tmp").Length.ShouldBe(1);
            return Task.FromException(new IOException("interrupted before rename"));
        });
        failed["Status"]!.GetValue<int>().ShouldBe(2);
        Receipt(await replacement.Apply(next), next, replacement, "complete replacement\n");

        var sameBytes = replacement.Request(3, "complete replacement\n", Hash("complete replacement\n"));
        var modified = File.GetLastWriteTimeUtc(replacement.Target);
        Receipt(await replacement.Apply(sameBytes), sameBytes, replacement, "complete replacement\n");
        File.GetLastWriteTimeUtc(replacement.Target).ShouldBe(modified, "equal-byte operation preserves target mtime");
    }

    [Test]
    public async Task V14_PublicationFences()
    {
        using var f = new Fixture();
        var first = f.Request();
        Receipt(await f.Apply(first), first, f, "first pins\n");
        var next = f.Request(2, "new pins\n", Hash("first pins\n"));
        Receipt(await f.Apply(next), next, f, "new pins\n");
        Refused(await f.Apply(first), "pin_fence_stale"); // c262-g149
        var collision = next.DeepClone().AsObject();
        collision["OperationId"] = Guid.NewGuid();
        Refused(await f.Apply(collision), "pin_operation_conflict");
        var cleanup = f.Request(3, null, Hash("new pins\n"));
        Receipt(await f.Apply(cleanup), cleanup, f, null);
        Refused(await f.Apply(next), "pin_fence_stale");
        File.Exists(f.Target).ShouldBeFalse("c262-g150: cleanup tombstone fences delayed publication");
        Receipt(await f.Apply(cleanup), cleanup, f, null);
        var reuse = f.Request(4, "repinned\n");
        reuse["LocationGeneration"] = Guid.NewGuid();
        Receipt(await f.Apply(reuse), reuse, f, "repinned\n");
        File.ReadAllText(f.Target).ShouldBe("repinned\n");
        Refused(await f.Apply(cleanup), "pin_fence_stale");
        var alias = reuse.DeepClone().AsObject();
        alias["Cwd"] = f.Cwd + "/./";
        Receipt(await f.Apply(alias), alias, f, "repinned\n");
        // Higher fences can supersede interrupted old intents, with either their
        // verified old or published bytes as expected custody.
        foreach (var cut in new[] { "intent", "published" })
        {
            using var interrupted = new Fixture();
            var old = interrupted.Request();
            (await interrupted.Apply(old, phase => phase == cut
                ? Task.FromException(new IOException("interrupted")) : Task.CompletedTask))["Status"]!.GetValue<int>().ShouldBe(2);
            var newest = interrupted.Request(2, "latest", cut == "published" ? Hash("first pins\n") : null);
            Receipt(await interrupted.Apply(newest), newest, interrupted, "latest");
            Refused(await interrupted.Apply(old), "pin_fence_stale");
            File.ReadAllText(interrupted.Target).ShouldBe("latest", "c262-g149: old RPC cannot regress a superseded intent");
        }
    }

    [Test]
    public async Task V13_PublicationCustody()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(f.Target)!);
        File.WriteAllText(f.Target, "<!-- forged owned marker -->\nfirst pins\n");
        Refused(await f.Apply(f.Request(expected: Hash(File.ReadAllText(f.Target)))), "pin_custody_missing");
        File.ReadAllText(f.Target).ShouldStartWith("<!-- forged");
        File.Delete(f.Target);
        var first = f.Request();
        Receipt(await f.Apply(first), first, f, "first pins\n");

        var next = f.Request(2, "new pins\n", Hash("first pins\n"));
        Refused(await f.Apply(next, phase =>
        {
            if (phase == "before-compare") File.WriteAllText(f.Target, "author edit");
            return Task.CompletedTask;
        }), "pin_bytes_conflict");
        File.ReadAllText(f.Target).ShouldBe("author edit", "final comparison preserves editor bytes");

        using var link = new Fixture();
        var outside = Path.Combine(link.Root, "canary");
        File.WriteAllText(outside, "outside");
        Refused(await link.Apply(link.Request(), phase =>
        {
            if (phase == "before-compare") File.CreateSymbolicLink(link.Target, outside);
            return Task.CompletedTask;
        }), "pin_path_link");
        File.ReadAllText(outside).ShouldBe("outside", "c262-g191: final native component check");

        using var corrupt = new Fixture();
        var owned = corrupt.Request();
        Receipt(await corrupt.Apply(owned), owned, corrupt, "first pins\n");
        var journalFile = Directory.GetFiles(corrupt.Journal, "*.json").Single();
        var saved = File.ReadAllText(journalFile);
        File.WriteAllText(journalFile, "{}");
        Refused(await corrupt.Apply(owned), "pin_journal_invalid");
        File.WriteAllText(journalFile, "broken json");
        (await corrupt.Apply(owned))["Status"]!.GetValue<int>().ShouldBe(2);
        File.Delete(journalFile);
        Refused(await corrupt.Apply(owned), "pin_custody_missing");
        File.WriteAllText(journalFile, saved);
        Receipt(await corrupt.Apply(owned), owned, corrupt, "first pins\n");
    }

    [Test]
    public async Task V15_PublicationCleanup()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.GetDirectoryName(f.Target)!);
        File.WriteAllText(f.Target, "foreign");
        Refused(await f.Apply(f.Request(1, null, Hash("foreign"))), "pin_custody_missing");
        File.ReadAllText(f.Target).ShouldBe("foreign", "c262-g154: no cleanup from path/hash alone");
        File.Delete(f.Target);
        var first = f.Request();
        Receipt(await f.Apply(first), first, f, "first pins\n");
        var sibling = Path.Combine(Path.GetDirectoryName(f.Target)!, "author.txt");
        File.WriteAllText(sibling, "keep sibling");
        var empty = f.Request(2, "", Hash("first pins\n"));
        Receipt(await f.Apply(empty), empty, f, "");
        File.Exists(f.Target).ShouldBeTrue("last revoke is an explicit file, not delete");
        var cleanup = f.Request(3, null, Hash(""));
        Refused(await f.Apply(cleanup, phase =>
        {
            if (phase == "before-compare") File.WriteAllText(f.Target, "edited before delete");
            return Task.CompletedTask;
        }), "pin_bytes_conflict");
        File.ReadAllText(f.Target).ShouldBe("edited before delete");
        File.WriteAllText(f.Target, "");
        Receipt(await f.Apply(cleanup), cleanup, f, null);
        File.ReadAllText(sibling).ShouldBe("keep sibling");
        Directory.Exists(Path.GetDirectoryName(f.Target)).ShouldBeTrue();
        File.Delete(sibling);
        var reuse = f.Request(4, "again");
        Receipt(await f.Apply(reuse), reuse, f, "again");
        var remove = f.Request(5, null, Hash("again"));
        Receipt(await f.Apply(remove), remove, f, null);
        Directory.Exists(Path.GetDirectoryName(f.Target)).ShouldBeFalse("only empty owner leaf is removed");
        Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(f.Target)!)).ShouldBeTrue();
        using var crash = new Fixture();
        var create = crash.Request();
        Receipt(await crash.Apply(create), create, crash, "first pins\n");
        var retire = crash.Request(2, null, Hash("first pins\n"));
        (await crash.Apply(retire, phase => phase == "published"
            ? Task.FromException(new IOException("crash after delete")) : Task.CompletedTask))["Status"]!.GetValue<int>().ShouldBe(2);
        Directory.Exists(Path.GetDirectoryName(crash.Target)).ShouldBeTrue();
        Receipt(await crash.Apply(retire), retire, crash, null);
        Directory.Exists(Path.GetDirectoryName(crash.Target)).ShouldBeFalse("replay must finish empty-leaf retirement");
        Directory.Delete(Path.Combine(crash.Cwd, ".antiphon"), recursive: true);
        Receipt(await crash.Apply(retire), retire, crash, null);
        Directory.GetFileSystemEntries(crash.Cwd).ShouldBeEmpty("cleanup replay must not recreate a retired subtree");

        using var absent = new Fixture();
        var interrupted = absent.Request();
        (await absent.Apply(interrupted, phase => phase == "intent"
            ? Task.FromException(new IOException("crash before first workspace I/O")) : Task.CompletedTask))["Status"]!.GetValue<int>().ShouldBe(2);
        Directory.GetFileSystemEntries(absent.Cwd).ShouldBeEmpty();
        var retireAbsent = absent.Request(2, null);
        Receipt(await absent.Apply(retireAbsent), retireAbsent, absent, null);
        Directory.GetFileSystemEntries(absent.Cwd).ShouldBeEmpty("retiring an unpublished intent must not create directories");
        Refused(await absent.Apply(interrupted), "pin_fence_stale");
    }

    [Test]
    public async Task V14_PublicationSerialization()
    {
        using var f = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = f.Request();
        var writing = f.Apply(first, async phase =>
        {
            if (phase == "intent") { entered.SetResult(); await release.Task; }
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var competing = await f.Apply(first);
            competing["Status"]!.GetValue<int>().ShouldBe(2, "second store cannot enter a held file lock");
            competing["Reason"]!.GetValue<string>().ShouldBe("pin_io_unavailable");
        }
        finally { release.TrySetResult(); await writing; }
        Receipt(await writing, first, f, "first pins\n");
        Receipt(await f.Apply(first), first, f, "first pins\n");
        // A separate inherited native child holds the same kernel lock. This is
        // deliberately not a second test runner or a production host process.
        var key = Hash(f.Target);
        var start = new ProcessStartInfo("flock")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in new[] { "-x", Path.Combine(f.Journal, key + ".lock"), "sh", "-c", "echo locked; read release" })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        try
        {
            (await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe("locked");
            (await f.Apply(first))["Status"]!.GetValue<int>().ShouldBe(2, "cross-process lock excludes publication");
            await child.StandardInput.WriteLineAsync("release");
            await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            child.ExitCode.ShouldBe(0);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
        }
        Receipt(await f.Apply(first), first, f, "first pins\n");
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void Refused(JsonObject result, string reason, string? guard = null)
    {
        result["Status"]!.GetValue<int>().ShouldBe(1, guard ?? reason);
        result["Reason"]!.GetValue<string>().ShouldBe(reason, guard ?? reason);
        result["Receipt"].ShouldBeNull();
    }

    private static void Receipt(JsonObject result, JsonObject request, Fixture f, string? content)
    {
        result["Status"]!.GetValue<int>().ShouldBe(0, result.ToJsonString());
        var receipt = result["Receipt"]!.AsObject();
        foreach (var field in new[] { "AgentId", "RunnerStoreId", "LocationGeneration", "OperationId", "Fence", "Revision", "SchemaVersion", "Action", "Sha256" })
            JsonNode.DeepEquals(receipt[field], request[field]).ShouldBeTrue("receipt identity " + field);
        receipt["Path"]!.GetValue<string>().ShouldBe(f.Target);
        receipt["ByteCount"]!.GetValue<long>().ShouldBe(content is null ? 0 : Encoding.UTF8.GetByteCount(content));
        if (content is null) File.Exists(f.Target).ShouldBeFalse();
        else File.ReadAllBytes(f.Target).ShouldBe(Encoding.UTF8.GetBytes(content));
    }

    private sealed class Fixture : IDisposable
    {
        public Guid Store { get; } = Guid.NewGuid();
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Generation { get; } = Guid.NewGuid();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c262-publish-" + Guid.NewGuid().ToString("N"));
        public string Cwd { get; }
        public string Journal { get; }
        public string Target => Path.Combine(Cwd, ".antiphon", "pins", Owner.ToString("N"), "antiphon.md");
        public Fixture()
        {
            Cwd = Directory.CreateDirectory(Path.Combine(Root, "workspace")).FullName;
            Journal = Directory.CreateDirectory(Path.Combine(Root, "runner-state")).FullName;
        }
        public JsonObject Request(long fence = 1, string? content = "first pins\n", string? expected = null) =>
            JsonSerializer.SerializeToNode(new
            {
                SchemaVersion = 1, AgentId = Owner, TargetOwnerId = Owner, RunnerStoreId = Store,
                Cwd, LocationGeneration = Generation, OperationId = Guid.NewGuid(), Fence = fence,
                Revision = fence, Action = content is null ? 1 : 0,
                Content = content is null ? null : Encoding.UTF8.GetBytes(content),
                Sha256 = content is null ? null : Hash(content), ExpectedSha256 = expected
            })!.AsObject();

        public async Task<JsonObject> Apply(JsonObject request, Func<string, Task>? boundary = null)
        {
            var instance = new AgentPinWorkspacePublisher(Store, Journal, boundary);
            var result = await instance.ApplyAsync(request.Deserialize<AgentPinPublicationRequest>()!, CancellationToken.None);
            return JsonSerializer.SerializeToNode(result)!.AsObject();
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
