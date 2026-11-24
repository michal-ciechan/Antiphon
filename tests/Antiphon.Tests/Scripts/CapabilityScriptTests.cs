using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0398 S2. The capability command (Antiphon.Nightly.CapabilityCli, which replaced scripts/capability.ps1 in unit 24 of
/// the 2026-11-22 plan) in process, on a recording transport: it never prints the bearer, writes a DPAPI blob that
/// ProtectedData reads back (what the delegate command's --capability does, through CapabilityCli.UnprotectCurrentUser), resolves a name to the one unrevoked capability, and
/// refuses issue and rotate before any request when it has no protector (off Windows). Nothing leaves the process.
/// </summary>
[Category("Integration")]
[SupportedOSPlatform("windows")]
public sealed class CapabilityScriptTests
{
    private const string Id = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Test]
    public async Task issue_does_not_print_the_token_and_dpapi_round_trips()
    {
        if (!OperatingSystem.IsWindows())
            Skip.Test("CARD-1811: Windows DPAPI (ProtectedData CurrentUser) round trip; required on Windows, excluded from the Linux rc profile.");
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        using var store = new CapabilityTempDir("cap-store");
        using var root = new CapabilityTempDir("cap-root");
        var http = new RecordingOperatorHttp(_ => new(201, Issued(canary, "script-cap", root.Path)));
        var run = Run(http, store.Path, CapabilityCli.ProtectCurrentUser, "issue", "--name", "script-cap", "--roots", root.Path);

        run.Exit.ShouldBe(0, run.All);
        run.All.ShouldNotContain(canary);
        run.Output.ShouldContain("Issued capability 'script-cap' stored at " + Path.Combine(store.Path, "script-cap.dpapi"));
        run.Output.ShouldContain("Roots: " + root.Path);
        var call = http.Calls.Single();
        (call.Method, call.Path).ShouldBe(("POST", "/api/delegation-capabilities"));
        call.Headers.ShouldBeEmpty("the operator's issue sends no task token");
        using (var body = JsonDocument.Parse(call.Body!))
        {
            body.RootElement.GetProperty("name").GetString().ShouldBe("script-cap");
            body.RootElement.GetProperty("roots").EnumerateArray().Select(r => r.GetString()).ShouldBe([root.Path]);
            body.RootElement.TryGetProperty("boardId", out _).ShouldBeFalse();
        }
        var blob = Path.Combine(store.Path, "script-cap.dpapi");
        File.Exists(blob).ShouldBeTrue(blob);
        var plain = Encoding.UTF8.GetString(
            ProtectedData.Unprotect(await File.ReadAllBytesAsync(blob), null, DataProtectionScope.CurrentUser));
        plain.ShouldBe(canary);
    }

    [Test]
    public void error_paths_never_contain_the_token()
    {
        var canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        using var store = new CapabilityTempDir("cap-store");
        using var root = new CapabilityTempDir("cap-root");
        var http = new RecordingOperatorHttp(_ => new(422, """{"detail":"roots invalid"}"""));
        var run = Run(http, store.Path, _ => throw new InvalidOperationException("nothing is sealed on a refusal"),
            "issue", "--name", "err-cap", "--roots", root.Path);

        run.Exit.ShouldBe(1);
        run.All.ShouldNotContain(canary);
        run.All.ShouldNotContain("AllowedRoots");
        run.Error.Trim().ShouldBe("Antiphon POST /api/delegation-capabilities failed: roots invalid");
        Directory.EnumerateFileSystemEntries(store.Path).ShouldBeEmpty();

        // An answer without a token writes nothing and says so; the token field is never echoed.
        var tokenless = new RecordingOperatorHttp(_ => new(201, Issued("", "err-cap", root.Path)));
        var missing = Run(tokenless, store.Path, _ => throw new InvalidOperationException("nothing to seal"), "issue", "--name", "err-cap", "--roots", root.Path);
        missing.Exit.ShouldBe(1);
        missing.Error.Trim().ShouldBe("issue/rotate response did not include a token for 'err-cap'.");
        Directory.EnumerateFileSystemEntries(store.Path).ShouldBeEmpty();
    }

    /// <summary>
    /// CARD-1811: ProtectedData is Windows-only, so the DPAPI case skips off Windows before it runs the capability command and
    /// the Linux rc profile excludes exactly that method. The error-path sibling stays required on every platform.
    /// </summary>
    [Test]
    public async Task C1811_Dpapi_case_is_windows_required_and_linux_excluded()
    {
        var policyPath = Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "test-execution-policy.json");
        using var policy = JsonDocument.Parse(await File.ReadAllTextAsync(policyPath));
        var rows = policy.RootElement.GetProperty("profiles").GetProperty("rc").GetProperty("exclusions")
            .EnumerateArray()
            .Where(row => row.GetProperty("class").GetString() == nameof(CapabilityScriptTests))
            .ToArray();
        rows.Length.ShouldBe(1, "one rc exclusion row for CapabilityScriptTests");
        var row = rows[0];
        row.GetProperty("suite").GetString().ShouldBe("antiphon");
        row.GetProperty("platform").GetString().ShouldBe("linux");
        row.GetProperty("methods").EnumerateArray().Select(method => method.GetString()).ToArray()
            .ShouldBe([nameof(issue_does_not_print_the_token_and_dpapi_round_trips)]);
        row.GetProperty("reason").GetString().ShouldNotStartWith("Linux-only by construction:");
        row.GetProperty("owner").GetString().ShouldBe("CARD-1811");

        if (OperatingSystem.IsWindows())
            return; // The DPAPI case itself is the Windows witness.

        var skip = await Should.ThrowAsync<SkipTestException>(issue_does_not_print_the_token_and_dpapi_round_trips());
        skip.Message.ShouldContain("DPAPI");
    }

    [Test]
    public void Without_a_protector_issue_and_rotate_refuse_before_any_request()
    {
        File.ReadAllBytes(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "CapabilityCli.cs")).ShouldAllBe(b => b < 128);
        using var store = new CapabilityTempDir("cap-store");
        var http = new RecordingOperatorHttp(_ => throw new InvalidOperationException("no request is sent"));
        foreach (var args in new[] { new[] { "issue", "--name", "n", "--roots", "C:/r" }, new[] { "rotate", "--name", "n" } })
        {
            var run = Run(http, store.Path, null, args);
            run.Exit.ShouldBe(1);
            run.Error.Trim().ShouldBe("capability " + args[0] + " writes a DPAPI-protected token and runs on Windows only.");
        }
        http.Calls.ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(store.Path).ShouldBeEmpty();
    }

    [Test]
    public void List_prints_each_capability_with_its_state_and_roots()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """
            [{"id":"1","name":"alpha","roots":["C:\\a","C:\\b"],"revokedAt":null,"rotatedAt":null},
             {"id":"2","name":"beta","roots":["C:\\c"],"revokedAt":null,"rotatedAt":"2026-09-05T00:00:00Z"},
             {"id":"3","name":"gamma","roots":[],"revokedAt":"2026-09-06T00:00:00Z","rotatedAt":"2026-09-05T00:00:00Z"}]
            """));
        var run = Run(http, "unused", null, "list");
        run.Exit.ShouldBe(0, run.Error);
        run.Output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ShouldBe(
        [
            @"alpha  1  active  roots: C:\a, C:\b",
            @"beta  2  rotated  roots: C:\c",
            "gamma  3  revoked  roots: ",
        ]);
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("GET", "/api/delegation-capabilities"));

        var empty = Run(new RecordingOperatorHttp(_ => new(200, "[]")), "unused", null, "list");
        empty.Output.Trim().ShouldBe("No delegation capabilities.");
    }

    [Test]
    public void Rotate_by_name_resolves_the_unrevoked_one_and_rewrites_the_blob()
    {
        using var store = new CapabilityTempDir("cap-store");
        var sealedTokens = new List<string>();
        var http = new RecordingOperatorHttp(call => call.Method == "GET"
            ? new(200, $$"""[{"id":"old","name":"Script-Cap","revokedAt":"2026-09-01T00:00:00Z"},{"id":"{{Id}}","name":"script-cap","revokedAt":null}]""")
            : new(200, """{"id":"x","name":"script-cap","token":"rotated-secret"}"""));
        var run = Run(http, store.Path, bytes =>
        {
            sealedTokens.Add(Encoding.UTF8.GetString(bytes));
            return [9, 8, 7];
        }, "rotate", "--name", "SCRIPT-CAP");

        run.Exit.ShouldBe(0, run.Error);
        http.Calls.Select(c => c.Method + " " + c.Path).ShouldBe(["GET /api/delegation-capabilities", "POST /api/delegation-capabilities/" + Id + "/rotate"]);
        http.Calls[1].Body.ShouldBeNull();
        sealedTokens.ShouldBe(["rotated-secret"]);
        File.ReadAllBytes(Path.Combine(store.Path, "script-cap.dpapi")).ShouldBe(new byte[] { 9, 8, 7 });
        run.Output.Trim().ShouldBe("Rotated capability 'script-cap' stored at " + Path.Combine(store.Path, "script-cap.dpapi"));
        run.All.ShouldNotContain("rotated-secret");
    }

    [Test]
    public void Revoke_by_id_posts_once_and_an_unknown_or_ambiguous_name_is_refused()
    {
        var http = new RecordingOperatorHttp(_ => new(200, """{"id":"x","name":"gone"}"""));
        var revoke = Run(http, "unused", null, "revoke", "--id", Id);
        revoke.Exit.ShouldBe(0, revoke.Error);
        revoke.Output.Trim().ShouldBe("Revoked capability 'gone'");
        (http.Calls.Single().Method, http.Calls.Single().Path).ShouldBe(("POST", "/api/delegation-capabilities/" + Id + "/revoke"));

        var rows = new RecordingOperatorHttp(_ => new(200, """[{"id":"1","name":"twin","revokedAt":null},{"id":"2","name":"twin","revokedAt":null}]"""));
        var ambiguous = Run(rows, "unused", null, "revoke", "--name", "twin");
        ambiguous.Exit.ShouldBe(1);
        ambiguous.Error.Trim().ShouldBe("Capability name 'twin' is ambiguous; pass --id.");
        var unknown = Run(rows, "unused", null, "revoke", "--name", "nobody");
        unknown.Exit.ShouldBe(1);
        unknown.Error.Trim().ShouldBe("No capability named 'nobody'.");
        rows.Calls.ShouldAllBe(c => c.Method == "GET");
    }

    [Test]
    public void A_malformed_command_line_is_a_usage_error()
    {
        Should.Throw<NightlyUsageException>(() => CapabilityCliOptions.Parse([], null, "s")).Message.ShouldContain("issue, rotate, revoke, list");
        Should.Throw<NightlyUsageException>(() => CapabilityCliOptions.Parse(["issue", "--roots", "C:/r"], null, "s")).Message.ShouldBe("issue requires --name");
        Should.Throw<NightlyUsageException>(() => CapabilityCliOptions.Parse(["issue", "--name", "n"], null, "s")).Message.ShouldBe("issue requires --roots");
        Should.Throw<NightlyUsageException>(() => CapabilityCliOptions.Parse(["rotate"], null, "s")).Message.ShouldBe("rotate requires --name or --id");
        Should.Throw<NightlyUsageException>(() => CapabilityCliOptions.Parse(["list", "--name", "n"], null, "s")).Message.ShouldBe("--name does not apply to list");
        Should.Throw<NightlyUsageException>(() => CapabilityCliOptions.Parse(["revoke", "--roots", "x", "--id", Id], null, "s")).Message.ShouldBe("--roots does not apply to revoke");
        var issue = CapabilityCliOptions.Parse(["ISSUE", "--name", "n", "--roots", "C:/a, C:/b", "--board-id", "b"], "http://api.example/", "store");
        (issue.Verb, issue.Api, issue.Store, issue.BoardId).ShouldBe(("issue", "http://api.example", "store", "b"));
        issue.Roots.ShouldBe(["C:/a", "C:/b"]);
    }

    private static BoardCommandRun.Result Run(RecordingOperatorHttp http, string store, Func<byte[], byte[]>? protect, params string[] args) =>
        BoardCommandRun.Run(host => CapabilityCli.Run(CapabilityCliOptions.Parse(args, BoardCommandRun.Api, store), host, new System.IO.Abstractions.FileSystem(), http.Transport, protect));

    private static string Issued(string token, string name, string root) => $$"""
        {"id":"{{Id}}","name":"{{name}}",
         "roots":["{{root.Replace("\\", "\\\\")}}"],"boardId":null,"projectId":null,
         "token":"{{token}}","storePath":"C:\\\\hint\\\\{{name}}.dpapi",
         "createdAt":"2026-09-05T00:00:00Z","lastUsedAt":null,"rotatedAt":null,"revokedAt":null}
        """;

    private sealed class CapabilityTempDir : IDisposable
    {
        public CapabilityTempDir(string prefix) => Path = Directory.CreateTempSubdirectory(prefix).FullName;
        public string Path { get; }
        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch (IOException) { }
        }
    }
}
