using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0408 V-30: bootstrap-check's card-file probe (Antiphon.Nightly BootstrapCheck.CardFiles; scripts/bootstrap-check.ps1 until
/// unit 38 of the 2026-11-22 plan, when this class read the script's probe out of its AST and ran it in pwsh) reads the projects
/// from a loopback stub, never the shared stack, through the production platform's HTTP read. It only GETs /api/projects and warns
/// without repair: the checkout's ignore bytes stay as they were and no card folder appears.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CardFileBootstrapCheckTests
{
    [Test]
    [Arguments("[{\"cardFileWarnings\":[\"card_files_ignore_missing\"]}]", "WARN", "card_files_ignore_missing")]
    [Arguments("[{}]", "WARN", "Publication status unavailable")]
    [Arguments("[{\"cardFileWarnings\":[]}]", "PASS", "explicit opt-in")]
    public async Task Actual_bootstrap_privacy_probe_only_GETs_stub_and_warns_without_repair(string json, string level, string expected)
    {
        var root = OperatingSystem.IsWindows() ? @"C:\c408-bootstrap" : "/c408-bootstrap";
        var ignore = Path.Combine(root, ".gitignore");
        var files = new MockFileSystem();
        files.AddFile(ignore, new MockFileData("# owner bytes\n"));
        var url = EphemeralHttpListener.BindLoopback(out var bound); using var listener = bound;
        var requests = new List<string>();
        var pump = Task.Run(async () => {
            var request = await listener.GetContextAsync();
            requests.Add(request.Request.HttpMethod + " " + request.Request.Url!.AbsolutePath);
            request.Response.ContentType = "application/json"; await request.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(json)); request.Response.Close();
        });
        try
        {
            var platform = new BootstrapCheckOs(NightlyHost.Production(TextWriter.Null, TextWriter.Null), files,
                _ => throw new InvalidOperationException("the probe never runs the stack check"));
            var line = await Task.Run(() => BootstrapCheck.CardFiles(url.TrimEnd('/'), platform)).WaitAsync(TimeSpan.FromSeconds(30));
            await pump.WaitAsync(TimeSpan.FromSeconds(30));

            requests.ShouldBe(["GET /api/projects"]);
            line.Status.ShouldBe(level);
            (line.Detail + "|" + line.Remedy).ShouldContain(expected);
            files.File.ReadAllText(ignore).ShouldBe("# owner bytes\n");
            files.AllFiles.ShouldBe([ignore]);
            files.Directory.Exists(Path.Combine(root, "docs", "cards")).ShouldBeFalse();
        }
        finally { listener.Close(); }
    }
}
