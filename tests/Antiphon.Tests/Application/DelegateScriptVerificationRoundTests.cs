using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0544 V-2 / G-10. The real <c>scripts/delegate.ps1</c> and <c>scripts/card.ps1</c> under pwsh
/// against loopback stubs: the exact camelCase wire body, full GUIDs, the selection file's JSON
/// object (never its path), local refusal before any POST, and server refusals surviving to a
/// nonzero exit. The stubs prove the CLI contract, not server admission.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DelegateScriptVerificationRoundTests
{
    private static readonly Guid Subject = Guid.Parse("c5440000-1111-2222-3333-4444444444aa");
    private static readonly Guid Baseline = Guid.Parse("c5440000-1111-2222-3333-4444444444bb");
    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Test]
    public async Task C544_RequestRoundTrip()
    {
        using var temp = new TempDir();
        var selectionFile = Path.Combine(temp.Path, "c544-selection.json");
        await File.WriteAllTextAsync(selectionFile,
            $$"""{"artifactPath":"docs/plans/c544.md","artifactCommitSha":"{{Sha}}","section":"Round selection"}""");
        foreach (var (row, roleArgs) in new[]
                 {
                     ("code-worktree", new[] { "-Role", "Code", "-Worktree", "-RepairSource", Subject.ToString("D") }),
                     ("review-readonly", new[] { "-Role", "Review", "-ReadOnly" }),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                [.. roleArgs, "-Goal", "repair round", "-VerificationRound", "Interim",
                    "-VerificationSubject", Subject.ToString("D").ToUpperInvariant(),
                    "-VerificationBaselineOutcome", Baseline.ToString("D"),
                    "-VerificationSelectionFile", selectionFile]);
            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            server.RequestCount.ShouldBe(1, row);
            var body = server.LastBody.ShouldNotBeNull(row).RootElement;
            body.GetProperty("verificationRound").GetString().ShouldBe("Interim", row);
            body.GetProperty("verificationSubjectTaskId").GetString().ShouldBe(Subject.ToString("D"), row);
            body.GetProperty("verificationBaselineOutcomeId").GetString().ShouldBe(Baseline.ToString("D"), row);
            var selection = body.GetProperty("verificationSelection");
            selection.ValueKind.ShouldBe(JsonValueKind.Object, row);
            selection.EnumerateObject().Select(p => p.Name).OrderBy(n => n)
                .ToArray().ShouldBe(new[] { "artifactCommitSha", "artifactPath", "section" }, customMessage: row);
            selection.GetProperty("artifactPath").GetString().ShouldBe("docs/plans/c544.md", row);
            selection.GetProperty("artifactCommitSha").GetString().ShouldBe(Sha, row);
            selection.GetProperty("section").GetString().ShouldBe("Round selection", row);
            var raw = body.GetRawText();
            raw.ShouldNotContain("c544-selection.json", Case.Insensitive, row + ": the local filename never reaches the API");
            raw.ShouldNotContain("verificationSelectionFile", Case.Insensitive, row);
        }
    }

    [Test]
    public async Task C544_OmittedRound()
    {
        foreach (var (row, args, expected) in new (string, string[], string?)[]
                 {
                     ("code-omitted", ["-Role", "Code", "-Worktree"], null),
                     ("review-omitted", ["-Role", "Review", "-ReadOnly"], null),
                     ("code-explicit-final", ["-Role", "Code", "-Worktree", "-VerificationRound", "Final"], "Final"),
                 })
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, [.. args, "-Goal", "full round"]);
            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            var body = server.LastBody.ShouldNotBeNull(row).RootElement;
            if (expected is null)
                body.TryGetProperty("verificationRound", out _).ShouldBeFalse(row + ": omitted stays omitted (server resolves Final)");
            else
                body.GetProperty("verificationRound").GetString().ShouldBe(expected, row);
            foreach (var name in new[] { "verificationSubjectTaskId", "verificationBaselineOutcomeId", "verificationSelection" })
                body.TryGetProperty(name, out _).ShouldBeFalse($"{row}: {name}");
        }
    }

    [Test]
    public async Task C544_InvalidArguments()
    {
        using var temp = new TempDir();
        var good = Path.Combine(temp.Path, "good.json");
        await File.WriteAllTextAsync(good, $$"""{"artifactPath":"docs/plans/c544.md","artifactCommitSha":"{{Sha}}","section":"S"}""");
        var malformed = Path.Combine(temp.Path, "malformed.json");
        await File.WriteAllTextAsync(malformed, "{\"artifactPath\": \"docs/plans/c544.md\",");
        var array = Path.Combine(temp.Path, "array.json");
        await File.WriteAllTextAsync(array, "[\"docs/plans/c544.md\"]");
        string[] Interim(string subject, string baseline, string file) =>
            ["-VerificationRound", "Interim", "-VerificationSubject", subject, "-VerificationBaselineOutcome", baseline,
                "-VerificationSelectionFile", file];
        var full = Subject.ToString("D");
        var rows = new (string Row, string[] Args, string Needle)[]
        {
            ("invalid-role", ["-Role", "Docs", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("orchestrator", ["-Role", "Code", "-Orchestrator", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("mutation-final", ["-Role", "Mutation", "-Worktree", "-VerificationRound", "Final"], "verification_round_role"),
            ("code-without-worktree", ["-Role", "Code", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("review-worktree", ["-Role", "Review", "-Worktree", .. Interim(full, Baseline.ToString("D"), good)], "verification_round_role"),
            ("short-subject", ["-Role", "Review", "-ReadOnly", .. Interim(full[..8], Baseline.ToString("D"), good)], "verification_baseline_invalid"),
            ("short-baseline", ["-Role", "Review", "-ReadOnly", .. Interim(full, Baseline.ToString("N"), good)], "verification_baseline_invalid"),
            ("malformed-json", ["-Role", "Review", "-ReadOnly", .. Interim(full, Baseline.ToString("D"), malformed)], "verification_selection_invalid"),
            ("array-json", ["-Role", "Review", "-ReadOnly", .. Interim(full, Baseline.ToString("D"), array)], "verification_selection_invalid"),
            ("missing-file", ["-Role", "Review", "-ReadOnly", .. Interim(full, Baseline.ToString("D"), Path.Combine(temp.Path, "absent.json"))], "verification_selection_invalid"),
            ("fields-without-interim", ["-Role", "Review", "-ReadOnly", "-VerificationSubject", full], "verification_round_role"),
            ("bad-enum", ["-Role", "Review", "-ReadOnly", "-VerificationRound", "Partial"], "VerificationRound"),
        };
        foreach (var (row, args, needle) in rows)
        {
            using var server = new DelegateCreateStubApi();
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl, [.. args, "-Goal", "invalid"]);
            run.ExitCode.ShouldNotBe(0, $"{row}: {run.Output}");
            run.Output.ShouldContain(needle, Case.Insensitive, row);
            server.RequestCount.ShouldBe(0, row + ": no POST and no server-side admission");
        }
    }

    [Test]
    public async Task C544_ServerRefusal()
    {
        using var temp = new TempDir();
        var file = Path.Combine(temp.Path, "selection.json");
        await File.WriteAllTextAsync(file, $$"""{"artifactPath":"docs/plans/c544.md","artifactCommitSha":"{{Sha}}","section":"S"}""");
        foreach (var code in new[] { "verification_interim_disallowed", "verification_backstop_unready" })
        {
            using var server = new DelegateCreateStubApi($$"""{"code":"{{code}}","title":"Conflict","detail":"refused {{code}}"}""", 409);
            var run = await DelegateScriptRunner.RunAsync(server.BaseUrl,
                "-Role", "Review", "-ReadOnly", "-Goal", "interim", "-VerificationRound", "Interim",
                "-VerificationSubject", Subject.ToString("D"), "-VerificationBaselineOutcome", Baseline.ToString("D"),
                "-VerificationSelectionFile", file);
            run.ExitCode.ShouldNotBe(0, code);
            run.Output.ShouldContain(code, Case.Sensitive, code);
            server.RequestCount.ShouldBe(1, code + ": exactly one attempt, no silent Final resubmission");
            server.LastBody.ShouldNotBeNull(code).RootElement.GetProperty("verificationRound").GetString().ShouldBe("Interim", code);
        }
    }

    [Test]
    public async Task C544_CardPolicyPatch()
    {
        foreach (var (row, args, expectCode, expectReview) in new (string, string[], string?, string?)[]
                 {
                     ("code-only", ["-CodeVerificationPolicy", "AllowInterim"], "AllowInterim", null),
                     ("review-only", ["-ReviewVerificationPolicy", "FullOnly"], null, "FullOnly"),
                     ("both", ["-CodeVerificationPolicy", "FullOnly", "-ReviewVerificationPolicy", "AllowInterim"], "FullOnly", "AllowInterim"),
                 })
        {
            using var stub = new CardStubApi();
            var run = await RunCardAsync(stub.BaseUrl, ["edit", "CARD-0544", .. args, "-Reason", $"C544 {row}"]);
            run.ExitCode.ShouldBe(0, $"{row}: {run.Output}");
            var patch = stub.LastPatch.ShouldNotBeNull(row).RootElement;
            patch.GetProperty("concurrencyToken").GetString().ShouldBe(CardStubApi.Token, row + ": fresh token");
            patch.GetProperty("reason").GetString().ShouldBe($"C544 {row}", row);
            AssertOptional(patch, "codeVerificationPolicy", expectCode, row);
            AssertOptional(patch, "reviewVerificationPolicy", expectReview, row);
        }

        using var refused = new CardStubApi();
        var bad = await RunCardAsync(refused.BaseUrl, ["edit", "CARD-0544", "-CodeVerificationPolicy", "Sometimes", "-Reason", "bad"]);
        bad.ExitCode.ShouldNotBe(0, "bad-enum");
        refused.LastPatch.ShouldBeNull("bad-enum: no PATCH");
    }

    private static void AssertOptional(JsonElement body, string name, string? expected, string row)
    {
        if (expected is null)
            body.TryGetProperty(name, out _).ShouldBeFalse($"{row}: omitted {name} preserves the stored value");
        else
            body.GetProperty(name).GetString().ShouldBe(expected, $"{row}: {name}");
    }

    private static async Task<(int ExitCode, string Output)> RunCardAsync(string apiBaseUrl, string[] args)
    {
        var startInfo = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "card.ps1"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = apiBaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>Loopback card API: GET limits, GET card (token), PATCH content. Never the production board.</summary>
    private sealed class CardStubApi : IDisposable
    {
        public const string Token = "c5440000-aaaa-bbbb-cccc-000000000001";
        private const string CardId = "c5440000-aaaa-bbbb-cccc-0000000000cc";
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _pump;

        public CardStubApi()
        {
            BaseUrl = EphemeralHttpListener.BindLoopback(_listener);
            _pump = Task.Run(PumpAsync);
        }

        public string BaseUrl { get; }
        public JsonDocument? LastPatch { get; private set; }

        private static string CardJson => $$"""
            {"id":"{{CardId}}","boardId":"c5440000-aaaa-bbbb-cccc-0000000000bb","identifier":"CARD-0544","title":"fixture",
             "status":"Backlog","importance":"Normal","urgency":"Normal","rank":1,"importanceProvenance":"Auto","labels":[],
             "concurrencyToken":"{{Token}}","revisionCount":3,"codeVerificationPolicy":"FullOnly","reviewVerificationPolicy":"FullOnly"}
            """;

        private async Task PumpAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }
                var path = context.Request.Url!.AbsolutePath;
                string response;
                var status = 200;
                if (context.Request.HttpMethod == "GET" && path == "/api/cards/limits")
                    response = """{"maxTitleLength":300,"maxDescriptionLength":20000,"maxPrivateNotesLength":20000,"maxReasonLength":4000,"maxActorLength":200,"maxAliasLength":64,"maxAliasWords":6,"importanceValues":[],"urgencyValues":[]}""";
                else if (context.Request.HttpMethod == "GET" && path.StartsWith("/api/cards/", StringComparison.Ordinal))
                    response = CardJson;
                else if (context.Request.HttpMethod == "PATCH" && path == $"/api/cards/{CardId}/content")
                {
                    using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                    LastPatch = JsonDocument.Parse(await reader.ReadToEndAsync());
                    response = CardJson;
                }
                else
                {
                    status = 404;
                    response = """{"detail":"not stubbed"}""";
                }
                var bytes = Encoding.UTF8.GetBytes(response);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch (Exception) { }
            try { _pump.Wait(TimeSpan.FromSeconds(5)); } catch (Exception) { }
            _listener.Close();
            LastPatch?.Dispose();
            _cts.Dispose();
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-c544-cli").FullName;
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }
}
