using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.TestSupport;

namespace Antiphon.Tests.Infrastructure;

// CARD-0989: execute the runtime-base Grok pin check. A value that is not exactly
// 64 lowercase hex characters must fail before curl. The checksum must accept only
// the downloaded artifact.
[NativeLinuxOnly("CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly.")]
[Timeout(600_000)]
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokSha256PinTests
{
    internal const string NativeLinuxReason = "CARD-1210: Grok SHA pin contracts require native Linux (/bin/sh and POSIX executable semantics); Windows skips explicitly.";

    private const string ReviewedPin = "9ce03ed23e16ea01072b4496263d6213a27899e1e3e107f008d36edf82e70407";
    private const string EmptySha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private static readonly object Gate = new();

    [Test]
    public async Task Runner_grok_sha_rejects_multiline_pin()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin + "\n" + "extra", "tampered");
        RejectBeforeFetch(result, "multiline-pin-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_trailing_newline()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin + "\n", "tampered");
        RejectBeforeFetch(result, "trailing-newline-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_devnull_second_line()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin + "\n" + EmptySha + "  /dev/null\n", "tampered");
        RejectBeforeFetch(result, "devnull-line-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_embedded_newline()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var pin = ReviewedPin[..32] + "\n" + ReviewedPin[32..63];
        pin.Length.ShouldBe(64);
        var result = await Run(pin, "tampered");
        RejectBeforeFetch(result, "embedded-newline-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_uppercase_pin()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin.ToUpperInvariant(), "tampered");
        RejectBeforeFetch(result, "uppercase-pin-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_short_pin()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin[..63], "tampered");
        RejectBeforeFetch(result, "short-pin-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_long_pin()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin + "0", "tampered");
        RejectBeforeFetch(result, "long-pin-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_empty_pin()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run("", "tampered");
        RejectBeforeFetch(result, "empty-pin-before-fetch");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_tampered_file()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        var result = await Run(ReviewedPin, "tampered");
        RejectAfterFetch(result, "tampered-file-before-install");
    }

    [Test]
    public async Task Runner_grok_sha_rejects_empty_download()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        // The empty-file digest is itself 64 lowercase hex. Length and character checks
        // accept it; only the non-empty-file gate rejects it before install.
        var result = await Run(EmptySha, "");
        RejectAfterFetch(result, "empty-download-before-install");
    }

    [Test]
    public async Task Runner_grok_sha_accepts_matching_file()
    {
        NativeLinuxGate.Require(NativeLinuxReason);
        const string body = "grok-pin-ok";
        var result = await Run(Sha(body), body);
        Console.Error.WriteLine(result.Detail);
        (result.ExitCode == 0 && result.CurlReached).ShouldBeTrue("matching-file-ok");
    }

    private static void RejectBeforeFetch(GrokPinResult result, string label)
    {
        Console.Error.WriteLine(result.Detail);
        (result.ExitCode != 0 && !result.CurlReached).ShouldBeTrue(label);
    }

    private static void RejectAfterFetch(GrokPinResult result, string label)
    {
        Console.Error.WriteLine(result.Detail);
        (result.ExitCode != 0 && result.CurlReached).ShouldBeTrue(label);
    }

    private static async Task<GrokPinResult> Run(string pin, string fixtureText)
    {
        var body = DockerStackDocuments.Stages(
                DockerStackDocuments.Read("docker/session-runner-grok/Dockerfile"))
            .Single(stage => stage.Name == "runtime-base").Body;
        const string startMark = "test -n \"${GROK_SHA256}\"";
        const string endMark = "install -m 0755 /tmp/grok-download/grok";
        var start = body.IndexOf(startMark, StringComparison.Ordinal);
        var end = body.IndexOf(endMark, StringComparison.Ordinal);
        (start >= 0 && end > start).ShouldBeTrue("grok-pin-script-present");
        var script = body[start..end].Trim();
        while (script.EndsWith("&&", StringComparison.Ordinal) || script.EndsWith('\\'))
        {
            script = script.EndsWith("&&", StringComparison.Ordinal)
                ? script[..^2].Trim()
                : script[..^1].Trim();
        }

        script.Contains("sha256sum", StringComparison.Ordinal).ShouldBeTrue("executed-checksum-text");
        var dir = Path.Combine(Path.GetTempPath(), "grok-pin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scriptPath = Path.Combine(dir, "check.sh");
        var fixturePath = Path.Combine(dir, "fixture");
        var markerPath = Path.Combine(dir, "curl-marker");
        await File.WriteAllTextAsync(fixturePath, fixtureText);
        var wrapped = "#!/bin/sh\ncurl() {\n  printf '%s\\n' curl-reached >> \"$GROK_CURL_MARKER\"\n  cp \"$GROK_FIXTURE_FILE\" \"$3\"\n}\n"
            + script + "\n";
        await File.WriteAllTextAsync(scriptPath, wrapped);

        lock (Gate)
        {
            if (Directory.Exists("/tmp/grok-download"))
                Directory.Delete("/tmp/grok-download", recursive: true);
        }

        try
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.Environment["GROK_SHA256"] = pin;
            startInfo.Environment["GROK_VERSION"] = "1.0.41";
            startInfo.Environment["GROK_FIXTURE_FILE"] = fixturePath;
            startInfo.Environment["GROK_CURL_MARKER"] = markerPath;

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("sh did not start");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("grok pin check exceeded 15s");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var curlReached = File.Exists(markerPath) && new FileInfo(markerPath).Length > 0;
            var shown = pin.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
            var detail = "exit=" + process.ExitCode
                + " curl=" + curlReached
                + " pin=" + shown
                + " stdout=" + stdout.Replace("\n", "|", StringComparison.Ordinal)
                + " stderr=" + stderr.Replace("\n", "|", StringComparison.Ordinal);
            return new GrokPinResult(process.ExitCode, curlReached, detail);
        }
        finally
        {
            lock (Gate)
            {
                if (Directory.Exists("/tmp/grok-download"))
                    Directory.Delete("/tmp/grok-download", recursive: true);
            }

            Directory.Delete(dir, recursive: true);
        }
    }

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(text))).ToLowerInvariant();

    private readonly record struct GrokPinResult(int ExitCode, bool CurlReached, string Detail);
}
