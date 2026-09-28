using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Antiphon.E2E.Fixtures;
using Serilog;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;

namespace Antiphon.E2E;

[Category("Headed")]
[Category("OptIn")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class ChannelOutboundIsolatedTests
{
    [Test]
    public async Task Static_logger_reaches_owned_host_file_after_restart()
    {
        RequireOptIn();
        var root = Path.Combine(Path.GetTempPath(), "c0784-static-log-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(root, "logs");
        var app = new AntiphonAppFixture
        {
            DiagnosticsDirectory = logPath,
            ConfigureOwnedHost = settings =>
            {
                settings["Delegation:CheckInterpreterEnabled"] = "false";
                settings["Delegation:DiagnoseEnabled"] = "false";
                settings["Delegation:OutputDistillerEnabled"] = "false";
                settings["Hangfire:ServerEnabled"] = "false";
            },
        };
        try
        {
            await app.InitializeAsync();
            await app.RestartOwnedHostAsync();

            var marker = "CARD-0784 static logger after restart " + Guid.NewGuid().ToString("N");
            Log.Information("{Marker}", marker);

            Directory.GetFiles(logPath, "antiphon-*.log")
                .Select(File.ReadAllText)
                .Any(contents => contents.Contains(marker, StringComparison.Ordinal))
                .ShouldBeTrue("the static logger must use the restarted host's file sink");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task FakeGrok_outbound_mode_reads_frozen_request_and_invokes_a_tool()
    {
        RequireOptIn();
        if (!OperatingSystem.IsLinux()) throw new SkipTestException("The shell tool stub is Linux-only.");
        var root = Path.Combine(Path.GetTempPath(), "c0418-fake-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input");
            var output = Path.Combine(root, "output");
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(output);
            var bytes = "# Source\nPolski tekst ✨\n"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Combine(input, "attachment-001.md"), bytes);
            var deliveryId = Guid.NewGuid();
            var requestPath = Path.Combine(root, "request.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new
            {
                version = 1, deliveryId,
                sourceFiles = new[] { new { originalRelativePath = "docs/source.md",
                    localName = "attachment-001.md", length = bytes.Length,
                    sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) } },
            }));
            var tool = Path.Combine(root, "copy-tool");
            var specimen = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(),
                "tests", "Antiphon.Tests", "Fixtures", "Card0418", "combined.pdf");
            await File.WriteAllTextAsync(tool, "#!/bin/sh\n"
                + "test \"$1\" = '--manifest' || exit 11\n"
                + "test \"$3\" = '--output' || exit 12\n"
                + "cp '" + specimen.Replace("'", "'\\''") + "' \"$4\"\n");
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var fake = Path.Combine(AppContext.BaseDirectory, "fakegrok", "fakegrok.dll");
            File.Exists(fake).ShouldBeTrue("FakeGrok must be staged in the E2E output.");
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(fake);
            start.ArgumentList.Add("--cwd");
            start.ArgumentList.Add(root);
            start.ArgumentList.Add("--session-id");
            start.ArgumentList.Add(Guid.NewGuid().ToString("D"));
            start.Environment["GROK_HOME"] = Path.Combine(root, "native");
            start.Environment["ANTIPHON_FAKE_OUTBOUND_TOOL"] = tool;
            start.Environment["ANTIPHON_FAKE_REPORT_LINE"] = "1";
            using var process = Process.Start(start)!;
            try
            {
                await process.StandardInput.WriteAsync("[antiphon-task:12345678] Outbound preparation for delivery "
                    + deliveryId + "\nRead the immutable request JSON at: " + requestPath + "\r");
                await process.StandardInput.FlushAsync();
                using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!File.Exists(Path.Combine(output, "manifest.json")))
                {
                    limit.Token.ThrowIfCancellationRequested();
                    await Task.Delay(50, limit.Token);
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json")));
            manifest.RootElement.GetProperty("deliveryId").GetGuid().ShouldBe(deliveryId);
            var file = manifest.RootElement.GetProperty("files").EnumerateArray().Single();
            file.GetProperty("sha256").GetString().ShouldBe(
                Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(specimen))));
            (await File.ReadAllBytesAsync(Path.Combine(output, "combined.pdf")))
                .ShouldBe(await File.ReadAllBytesAsync(specimen));
            File.Exists(Path.Combine(output, "fakegrok-tool-evidence.json")).ShouldBeTrue();
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1"
            || Environment.GetEnvironmentVariable("ANTIPHON_BROKER_TESTS") != "1"
            || Environment.GetEnvironmentVariable("ANTIPHON_CHANNEL_OUTBOUND_ISOLATED") != "1")
            throw new SkipTestException("F-5 requires all three isolated opt-ins.");
    }
}
