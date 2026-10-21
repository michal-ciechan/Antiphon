using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public sealed class GrokRulesRunnerHttpTests
{
    [Test, Timeout(120_000)]
    public async Task Actual_runner_routes_advertise_rules_without_Herdr_and_return_only_receipt_metadata(CancellationToken ct)
    {
        var root = TestSessionLogRoot.Create("card0395-http"); Directory.CreateDirectory(root);
        var temp = Path.Combine(root, "temp"); Directory.CreateDirectory(temp);
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start(); var port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var url = $"http://127.0.0.1:{port}";
        var assembly = typeof(GrokRulesRunnerHttpTests).Assembly.Location;
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("SessionRunner__", StringComparison.OrdinalIgnoreCase)
            || k.StartsWith("ANTIPHON_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)).ToArray()) psi.Environment.Remove(key);
        psi.Environment["TEMP"] = temp; psi.Environment["TMP"] = temp;
        foreach (var arg in new[] { "exec", "--runtimeconfig", Path.ChangeExtension(assembly, ".runtimeconfig.json"), "--depsfile", Path.ChangeExtension(assembly, ".deps.json"),
            typeof(SessionRunnerRuntime).Assembly.Location, "--urls", url, "--SessionRunner:SessionLogPath", root,
            "--SessionRunner:Herdr:Enabled", "false", "--SessionRunner:CpuWatchdogEnabled", "false", "--SessionRunner:PtyBackend", "modern",
            "--SessionRunner:PtyHostLingerHours", "0.02", "--Serilog:LogPath", Path.Combine(root, "logs") }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var client = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(35) };
        var id = Guid.NewGuid(); var created = false;
        try
        {
            var wait = Stopwatch.StartNew();
            while (true)
            {
                if (process.HasExited) throw new InvalidOperationException(await stderr + await stdout);
                try { using var response = await client.GetAsync("/capabilities", ct); if (response.IsSuccessStatusCode) break; }
                catch (HttpRequestException) { }
                wait.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30)); await Task.Delay(100, ct);
            }
            var capability = await client.GetFromJsonAsync<RunnerCapabilitiesDto>("/capabilities", ct);
            capability!.Features.ShouldContain(GrokRulesTransport.Capability);
            capability.SessionBackends.ShouldNotContain(SessionBackends.Herdr);
            var body = "HTTP-PRIVATE-RULE-SENTINEL\r\ncomplete unicode café rules";
            var request = new RunnerLaunchRequest(id, Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/q", "/k", "prompt $G"],
                new Dictionary<string,string> { ["GROK_HOME"] = Path.Combine(root, "home") }, root, 120, 30,
                TranscriptFormat: TranscriptFormats.Grok, GrokRulesPayload: new(body, 1, Guid.NewGuid()));
            using var start = await client.PostAsJsonAsync("/sessions", request, ct); start.EnsureSuccessStatusCode(); created = true;
            var json = await start.Content.ReadAsStringAsync(ct); json.ShouldNotContain("HTTP-PRIVATE-RULE-SENTINEL");
            var launched = JsonSerializer.Deserialize<RunnerSessionDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            var receipt = launched.GrokRulesReceipt.ShouldNotBeNull();
            (await File.ReadAllBytesAsync(receipt.Path, ct)).ShouldBe(Encoding.UTF8.GetBytes(body));
            var fetched = await client.GetFromJsonAsync<RunnerSessionDto>("/sessions/" + id, ct);
            fetched!.GrokRulesReceipt.ShouldBe(receipt);
            (await client.GetStringAsync("/sessions", ct)).ShouldNotContain("HTTP-PRIVATE-RULE-SENTINEL");
            File.ReadAllText(Antiphon.PtyHost.Protocol.PtyHostManifest.PathFor(Path.Combine(root, "pty-hosts", "manifests"), id)).ShouldNotContain("HTTP-PRIVATE-RULE-SENTINEL");
        }
        finally
        {
            try
            {
                if (created) { using var response = await client.PostAsync("/sessions/" + id + "/kill", null, CancellationToken.None); response.EnsureSuccessStatusCode(); }
            }
            finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            await File.WriteAllTextAsync(Path.Combine(root, "runner-stdout.log"), await stdout);
            await File.WriteAllTextAsync(Path.Combine(root, "runner-stderr.log"), await stderr);
        }
    }
}
