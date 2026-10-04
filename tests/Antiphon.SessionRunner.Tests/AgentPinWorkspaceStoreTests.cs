using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// S3a.1: real native files, no runner process or production workspace.
[Category("Integration")]
public class AgentPinWorkspaceStoreTests
{
    [Test]
    public async Task V32_InspectPosixPaths()
    {
        OperatingSystem.IsLinux().ShouldBeTrue("CP-4a requires native Linux qualification");
        using var fixture = new Fixture();
        var upper = fixture.Directory("Case");
        var lower = fixture.Directory("case");
        var owner = Guid.Parse("12345678-1111-2222-3333-444444444444");
        var sibling = Guid.Parse("12345678-5555-6666-7777-888888888888");

        // Never-used inspection must not materialize any runtime directory.
        var missing = await Inspect(fixture.Store, owner, upper);
        missing.Status.ShouldBe("MissingFile");
        missing.Path.ShouldBe(fixture.Target(upper, owner));
        Directory.GetFileSystemEntries(upper).ShouldBeEmpty();

        fixture.Write(upper, owner, "UPPER own literal\r\n");
        fixture.Write(lower, owner, "lower own literal\n");
        fixture.Write(upper, sibling, "sibling stays separate");
        foreach (var alias in new[] { upper, upper + "/", upper + "//./", upper + "/../Case" })
        {
            var observed = await Inspect(fixture.Store, owner, alias);
            AssertObserved(observed, fixture.Store, owner, upper, "UPPER own literal\r\n");
        }
        AssertObserved(await Inspect(fixture.Store, owner, lower), fixture.Store, owner, lower, "lower own literal\n");
        AssertObserved(await Inspect(fixture.Store, sibling, upper), fixture.Store, sibling, upper, "sibling stays separate");

        // POSIX backslash is a literal filename character, not a Windows separator.
        var literal = fixture.Directory("native\\name");
        fixture.Write(literal, owner, "");
        AssertObserved(await Inspect(fixture.Store, owner, literal), fixture.Store, owner, literal, "");
        File.Exists(Path.Combine(upper, "antiphon.md")).ShouldBeFalse();
        File.ReadAllText(fixture.Target(upper, sibling)).ShouldBe("sibling stays separate");
    }

    [Test]
    public async Task V13_InspectPathSafety()
    {
        OperatingSystem.IsLinux().ShouldBeTrue("CP-4a requires native Linux qualification");
        using var fixture = new Fixture();
        var owner = Guid.NewGuid();
        var outside = fixture.Directory("outside");
        var canary = Path.Combine(outside, "canary");
        File.WriteAllText(canary, "outside secret must not be advertised");

        foreach (var component in new[] { "cwd", ".antiphon", "pins", owner.ToString("N"), "antiphon.md" })
        {
            var cwd = fixture.Directory("link-" + component);
            var target = fixture.Target(cwd, owner);
            var link = component switch
            {
                "cwd" => cwd,
                ".antiphon" => Path.Combine(cwd, ".antiphon"),
                "pins" => Path.Combine(cwd, ".antiphon", "pins"),
                "antiphon.md" => target,
                _ => Path.GetDirectoryName(target)!
            };
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            if (component == "cwd") Directory.Delete(cwd);
            if (component == "antiphon.md") File.CreateSymbolicLink(link, canary);
            else Directory.CreateSymbolicLink(link, outside);
            var result = await Inspect(fixture.Store, owner, cwd);
            AssertRefused(result, "pin_path_link");
            File.ReadAllText(canary).ShouldBe("outside secret must not be advertised");
        }

        var dangling = fixture.Directory("dangling");
        var danglingTarget = fixture.Target(dangling, owner);
        Directory.CreateDirectory(Path.GetDirectoryName(danglingTarget)!);
        File.CreateSymbolicLink(danglingTarget, Path.Combine(outside, "absent"));
        AssertRefused(await Inspect(fixture.Store, owner, dangling), "pin_path_link");
        File.Exists(Path.Combine(outside, "absent")).ShouldBeFalse();

        // Normalizing '..' must not erase evidence of traversing a symlink.
        var aliasRoot = fixture.Directory("alias");
        Directory.CreateSymbolicLink(Path.Combine(aliasRoot, "link"), outside);
        AssertRefused(await Inspect(fixture.Store, owner, aliasRoot + "/link/.."), "pin_path_link");

        var blocked = fixture.Directory("parent-file");
        File.WriteAllText(Path.Combine(blocked, ".antiphon"), "author bytes");
        AssertRefused(await Inspect(fixture.Store, owner, blocked), "pin_path_not_directory");
        File.ReadAllText(Path.Combine(blocked, ".antiphon")).ShouldBe("author bytes");

        var absent = Path.Combine(fixture.Root, "never-created");
        (await Inspect(fixture.Store, owner, absent)).Status.ShouldBe("MissingCwd");
        Directory.Exists(absent).ShouldBeFalse();

        var swapped = fixture.Directory("swap");
        fixture.Write(swapped, owner, "owned before inspection");
        var swappedTarget = fixture.Target(swapped, owner);
        var swapCalls = 0;
        var swappedResult = await Inspect(fixture.Store, owner, swapped, beforeRead: _ =>
        {
            swapCalls++;
            File.Delete(swappedTarget);
            File.CreateSymbolicLink(swappedTarget, canary);
        });
        swapCalls.ShouldBe(1);
        AssertRefused(swappedResult, "pin_path_link");
        File.ReadAllText(canary).ShouldBe("outside secret must not be advertised");

        var denied = fixture.Directory("denied");
        fixture.Write(denied, owner, "unreadable");
        var deniedResult = await Inspect(fixture.Store, owner, denied,
            beforeRead: _ => throw new UnauthorizedAccessException("test-owned native read denial"));
        deniedResult.Status.ShouldBe("Unavailable");
        deniedResult.Reason.ShouldBe("pin_io_unavailable");
        deniedResult.Sha256.ShouldBeNull();
        deniedResult.Path.ShouldBeNull();
    }

    [Test]
    public async Task V34_InspectProtocolAdmission()
    {
        using var fixture = new Fixture();
        var owner = Guid.NewGuid();
        var cwd = fixture.Directory("request");
        fixture.Write(cwd, owner, "literal inspection bytes");
        var reads = 0;
        Action<string> onRead = _ => reads++;
        AssertRefused(await Inspect(fixture.Store, Guid.Empty, cwd, beforeRead: onRead), "pin_owner_invalid");
        AssertRefused(await Inspect(fixture.Store, owner, cwd, schema: 999, beforeRead: onRead), "pin_schema_unsupported");
        AssertRefused(await Inspect(fixture.Store, owner, cwd, expectedStore: Guid.NewGuid(), beforeRead: onRead), "pin_store_mismatch");
        AssertRefused(await Inspect(fixture.Store, owner, cwd, expectedStore: Guid.Empty, beforeRead: onRead), "pin_store_mismatch");
        foreach (var invalid in new[] { "", "relative/path", "../escape", "C:\\Windows", cwd + '\0' })
            AssertRefused(await Inspect(fixture.Store, owner, invalid, beforeRead: onRead), "pin_cwd_invalid");
        reads.ShouldBe(0);
        AssertObserved(await Inspect(fixture.Store, owner, cwd, beforeRead: onRead), fixture.Store, owner, cwd,
            "literal inspection bytes");
        reads.ShouldBe(1);
        File.ReadAllText(fixture.Target(cwd, owner)).ShouldBe("literal inspection bytes");
    }

    private static void AssertRefused(View result, string reason)
    {
        result.Status.ShouldBe("Refused");
        result.Reason.ShouldBe(reason);
        result.Path.ShouldBeNull();
        result.Sha256.ShouldBeNull();
        result.ByteCount.ShouldBeNull();
    }

    private static void AssertObserved(View result, Guid store, Guid owner, string cwd, string contents)
    {
        result.Status.ShouldBe("Observed");
        result.Reason.ShouldBeNull();
        result.AgentId.ShouldBe(owner);
        result.RunnerStoreId.ShouldBe(store);
        result.Cwd.ShouldBe(cwd);
        result.Path.ShouldBe(Path.Combine(cwd, ".antiphon", "pins", owner.ToString("N"), "antiphon.md"));
        var bytes = Encoding.UTF8.GetBytes(contents);
        result.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        result.ByteCount.ShouldBe(bytes.Length);
    }

    // Reflection lets the red-first commit execute and fail on the absent product contract,
    // rather than failing compilation. Replaced by typed calls with implementation.
    private static async Task<View> Inspect(Guid store, Guid owner, string cwd, int schema = 1,
        Guid? expectedStore = null, Action<string>? beforeRead = null)
    {
        var assembly = typeof(PhoneHomeCommandDispatcher).Assembly;
        var type = assembly.GetType("Antiphon.SessionRunner.AgentPinWorkspaceStore");
        type.ShouldNotBeNull("S3a.1 requires a host-native pin inspection operation");
        var method = type.GetMethod("InspectAsync")!;
        var requestType = method.GetParameters()[0].ParameterType;
        var request = JsonSerializer.Deserialize(JsonSerializer.Serialize(new
        {
            SchemaVersion = schema, AgentId = owner, RunnerStoreId = expectedStore ?? store, Cwd = cwd
        }), requestType)!;
        var instance = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: [store, beforeRead], culture: null)!;
        var task = (Task)method.Invoke(instance, [request, CancellationToken.None])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return JsonSerializer.Deserialize<View>(JsonSerializer.Serialize(result,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }))!;
    }

    private sealed record View(string Status, string? Reason, Guid AgentId, Guid RunnerStoreId,
        string? Cwd, string? Path, string? Sha256, long? ByteCount);

    private sealed class Fixture : IDisposable
    {
        public Guid Store { get; } = Guid.NewGuid();
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c262-inspect-" + Guid.NewGuid().ToString("N"));
        public string Directory(string name) => System.IO.Directory.CreateDirectory(Path.Combine(Root, name)).FullName;
        public string Target(string cwd, Guid owner) => Path.Combine(cwd, ".antiphon", "pins", owner.ToString("N"), "antiphon.md");
        public void Write(string cwd, Guid owner, string bytes)
        {
            var path = Target(cwd, owner);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, bytes, new UTF8Encoding(false));
        }
        public void Dispose() { if (System.IO.Directory.Exists(Root)) System.IO.Directory.Delete(Root, recursive: true); }
    }
}
