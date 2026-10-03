using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.Tests.Application;
using Antiphon.TestSupport;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using TUnit.Core.Exceptions;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Reflection;
using Antiphon.PtyHost.Protocol;

namespace Antiphon.Tests.Agents;

/// <summary>FakeGrok through the isolated runner, native PTY, screen and production ready adapter.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerGrokAdapterReadyTestsPty
{
    [Test]
    [Arguments("inbox")]
    [Arguments("modern")]
    public async Task C1011_windows_backends_reach_ready_and_complete_prompt(string backend)
    {
        if (!OperatingSystem.IsWindows())
            throw new SkipTestException("CARD-1011 actual inbox/modern qualification requires Windows");
        var root = Path.Combine(Path.GetTempPath(), "c1011-backend-" + Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"), ptyBackend: backend);
        await using var adapter = new RunnerGrokAdapter(client,
            Options.Create(new AgentRegistrySettings
            {
                GrokReadyMaxWaitMs = 10000, GrokReadyQuietPeriodMs = 200, GrokReadyMinTotalWaitMs = 0,
            }), Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await adapter.StartAsync(new AgentLaunchSpec("fakegrok", AgentKind.Grok,
            TestAppHostPath.Require("fakegrok", AppContext.BaseDirectory),
            ["--session-id", sessionId.ToString("D"), "--cwd", cwd],
            new Dictionary<string, string>
            {
                ["GROK_HOME"] = Path.Combine(root, "grok-home"),
                ["ANTIPHON_FAKE_GROK_LINUX_COMPOSER"] = "0",
            }, cwd, 120, 30, SessionId: sessionId), deadline.Token);
        (await adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
        var hostLog = await ReadHostLogAsync(client, sessionId, deadline.Token);
        if (backend == "inbox")
            hostLog.ShouldContain("pty backend: InboxConhost (requested 'inbox')");
        else
            hostLog.ShouldContain("pty backend: ModernConPty (requested 'modern')");
        var snapshot = await client.GetSnapshotAsync(sessionId, deadline.Token);
        GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
        snapshot.RenderedScreen.Split('\n')[25][4].ShouldBe('>');
        (await client.GetTranscriptAsync(sessionId, deadline.Token)).Entries
            .ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);
        var body = $"C1011 HEAD {Guid.NewGuid():N} TAIL";
        await adapter.SendPromptAsync(body, deadline.Token);
        var transcript = await WaitForPromptAsync(client, sessionId, deadline.Token);
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
        prompts.ShouldHaveSingleItem();
        prompts[0].Text.ShouldBe(body);
        Console.WriteLine($"C1011 backend={backend} session={sessionId:D} evidence={root}");
    }

    [Test]
    [Explicit]
    [NotInParallel("Headed")]
    [Timeout(600_000)]
    public async Task C1011_real_fresh_worktree_ready_and_one_turn(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") != "1")
            throw new SkipTestException("Requires Windows and ANTIPHON_HEADED_TESTS=1 under an explicit WQ-3 commission");
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");
        var root = Path.Combine(Path.GetTempPath(), "c1011-real-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var cwd = Path.Combine(root, "worktree");
        var sessionId = Guid.NewGuid();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(10));
        await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"), ptyBackend: "modern");
        var observer = new C1011GrokQualification.Observer(client);
        await using var adapter = new RunnerGrokAdapter(observer, Options.Create(new AgentRegistrySettings
        {
            GrokStartupCaptureDirectory = Path.Combine(root, "startup"),
        }));
        Process? ownedChild = null;
        var worktreeCreated = false;
        var started = false;
        var releaseConfirmed = false;
        var turnAccepted = false;
        var source = "";
        var version = "";
        var backendLine = "";
        var hostLogSha256 = "";
        var hostLogPath = Path.Combine(Path.GetDirectoryName(client.PtyHostManifestDir)!, "logs", sessionId.ToString("N") + ".log");
        object? buildProvenance = null;
        string? failure = null;
        var phase = "setup";
        var nonce = "C1011-FRESH-" + Guid.NewGuid().ToString("N");
        var body = $"Reply exactly {nonce}. Do not use tools or change files.";
        SessionRunnerTranscriptDto transcript = new(sessionId, [], 0);
        try
        {
            File.Exists(exe).ShouldBeTrue("The real Grok CLI must be installed; missing setup is incomplete qualification");
            source = (await GitAsync(DelegateScriptRunner.RepoRoot, ["rev-parse", "HEAD"], deadline.Token)).Trim();
            await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "add", "--detach", cwd, source], deadline.Token);
            worktreeCreated = true;
            // Immediately before this single launch; retain complete build/channel output.
            version = await RunAsync(exe, ["--version"], DelegateScriptRunner.RepoRoot, deadline.Token);
            version.ShouldNotBeNullOrWhiteSpace("cli-version");
            phase = "launch";
            await adapter.StartAsync(new AgentLaunchSpec("grok", AgentKind.Grok, exe,
                ["--always-approve", "--no-alt-screen", "--model", "grok-4.7", "--session-id", sessionId.ToString("D")],
                new Dictionary<string, string>(), cwd, 120, 30, SessionId: sessionId), deadline.Token);
            started = true;
            adapter.Pid.ShouldNotBeNull();
            ownedChild = Process.GetProcessById(adapter.Pid.Value);
            // Record actual backend before any readiness/prompt assertion can reject the launch.
            var hostLog = await ReadHostLogAsync(client, sessionId, deadline.Token);
            hostLogSha256 = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hostLog)));
            backendLine = hostLog.Split('\n')
                .SingleOrDefault(x => x.Contains("pty backend:", StringComparison.Ordinal))?.TrimEnd('\r') ?? "";
            await WriteObservationsAsync();
            backendLine.ShouldContain("pty backend: ModernConPty (requested 'modern')", customMessage: "backend-line");
            buildProvenance = CaptureBuildProvenance(client, sessionId, ownedChild, hostLogPath, hostLogSha256);
            phase = "startup";
            var adapterReady = await adapter.WaitForReadyAsync(deadline.Token);
            adapterReady.ShouldBeTrue();
            var ready = await observer.GetSnapshotAsync(sessionId, deadline.Token);
            GrokStartupScreen.Classify(ready.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
            ready.RenderedScreen.Split('\n')[25][4].ShouldBe('>');
            GrokTrustPromptDetector.IsVisibleOnScreen(ready.RenderedScreen).ShouldBeFalse();
            var prePrompt = await observer.GetTranscriptAsync(sessionId, deadline.Token);
            prePrompt.Entries.ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);
            C1011GrokQualification.StartupVerdict(adapterReady, ready.RenderedScreen,
                observer.TrustBeforeFirstInput, observer.StartupInputs, prePrompt)
                .ShouldBe(C1011GrokQualification.Verdict.Accepted, "startup-accepted");
            observer.StartupComplete = true;
            phase = "one-paid-turn";
            await adapter.SendPromptAsync(body, deadline.Token);
            transcript = await WaitForPromptAsync(client, sessionId, deadline.Token);
            var prompt = transcript.Entries.Single(x => x.Kind == TranscriptKinds.UserPrompt);
            prompt.Text.ShouldBe(body);
            do
            {
                transcript = await client.GetTranscriptAsync(sessionId, deadline.Token);
                if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.TurnEnd && x.Sequence > prompt.Sequence)) break;
                await Task.Delay(250, deadline.Token);
            } while (true);
            C1011GrokQualification.TurnVerdict(transcript, body, nonce)
                .ShouldBe(C1011GrokQualification.Verdict.Accepted, "turn-rejection");
            transcript.Entries.ShouldContain(x => !string.IsNullOrWhiteSpace(x.Model), "transcript-model");
            turnAccepted = true;
            phase = "release";
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name; // No arbitrary diagnostics or provider screen content.
            throw;
        }
        finally
        {
            try
            {
                if (started)
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await client.KillAsync(sessionId, cleanup.Token);
                    if (ownedChild is not null)
                    {
                        await ownedChild.WaitForExitAsync(cleanup.Token);
                        ownedChild.HasExited.ShouldBeTrue();
                        releaseConfirmed = true;
                    }
                    if (turnAccepted)
                    {
                        // Examine the final post-release transcript, including any late duplicates.
                        transcript = await client.GetTranscriptAsync(sessionId, cleanup.Token);
                        C1011GrokQualification.TurnVerdict(transcript, body, nonce)
                            .ShouldBe(C1011GrokQualification.Verdict.Accepted, "turn-rejection");
                        releaseConfirmed.ShouldBeTrue("release-owner");
                        var measured = new C1011GrokQualification.Measurements(source, version, backendLine,
                            sessionId, cwd, "grok-4.7", body, nonce, buildProvenance!, observer.SessionBannerVersions);
                        await File.WriteAllTextAsync(Path.Combine(root, "receipt.json"),
                            C1011GrokQualification.SerializeReceipt(measured, observer, transcript, releaseConfirmed),
                            CancellationToken.None);
                        phase = "accepted";
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name;
                throw;
            }
            finally
            {
                ownedChild?.Dispose();
                // On setup/launch/startup/input/turn/release failure, retain the measured facts too.
                await WriteObservationsAsync();
                if (worktreeCreated && (!started || releaseConfirmed))
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await GitAsync(DelegateScriptRunner.RepoRoot, ["worktree", "remove", cwd], cleanup.Token);
                }
                Console.WriteLine($"C1011 WQ-3 session={sessionId:D} evidence={root} releaseConfirmed={releaseConfirmed}");
            }
        }

        Task WriteObservationsAsync() => File.WriteAllTextAsync(Path.Combine(root, "observations.json"),
            JsonSerializer.Serialize(new
            {
                source, version, backendLine, hostLogPath, hostLogSha256, sessionId, cwd, cols = 120, rows = 30, requestedModel = "grok-4.7",
                buildProvenance, observer.TrustBeforeFirstInput, observer.StartupInputs, observer.Observations,
                observer.SessionBannerVersions, body, nonce, transcript, releaseConfirmed, phase, failure,
            }), CancellationToken.None);
    }

    private static object CaptureBuildProvenance(DirectSessionRunnerClient client, Guid sessionId, Process child,
        string hostLogPath, string hostLogSha256)
    {
        var manifestPath = PtyHostManifest.PathFor(client.PtyHostManifestDir, sessionId);
        var manifest = PtyHostManifest.TryLoad(manifestPath);
        manifest.ShouldNotBeNull("owned host manifest is mandatory build evidence");
        manifest.SessionId.ShouldBe(sessionId);
        manifest.ChildPid.ShouldBe(child.Id);
        manifest.Cols.ShouldBe(120);
        manifest.Rows.ShouldBe(30);
        using var host = Process.GetProcessById(manifest.HostPid);
        var hostImage = host.MainModule!.FileName;
        var loadedConPty = host.Modules.Cast<ProcessModule>().Single(x =>
            string.Equals(x.ModuleName, ConPtyRedistributable.DllName, StringComparison.OrdinalIgnoreCase)).FileName;
        var consolePath = Path.Combine(Path.GetDirectoryName(loadedConPty)!, ConPtyRedistributable.ConsoleHostName);
        ConPtyRedistributable.VerifyShippedHashes(loadedConPty).Ok.ShouldBeTrue("modern-package-provenance");
        var runnerAssembly = typeof(Antiphon.SessionRunner.SessionRunnerRuntime).Assembly;
        return new
        {
            hostLogPath, hostLogSha256, hostLogHashScope = "UTF-8 launch-log snapshot before readiness",
            manifestPath, manifest.HostPid, manifest.HostStartTimeUtc, manifest.ChildPid, manifest.ChildStartTimeUtc,
            hostImage = Binary(hostImage), hostBuild = Binary(Path.Combine(Path.GetDirectoryName(hostImage)!, "Antiphon.PtyHost.dll")),
            runnerBuild = Binary(runnerAssembly.Location),
            runnerVersion = runnerAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            harnessBuild = Binary(typeof(RunnerGrokAdapterReadyTestsPty).Assembly.Location),
            package = ConPtyRedistributable.PackageId, packageVersion = ConPtyRedistributable.PackageVersion,
            conpty = Binary(loadedConPty), openConsole = Binary(consolePath),
        };
    }

    private static object Binary(string path)
    {
        using var stream = File.OpenRead(path);
        var version = FileVersionInfo.GetVersionInfo(path);
        return new { path, sha256 = Convert.ToHexStringLower(SHA256.HashData(stream)), version.FileVersion, version.ProductVersion };
    }

    private static async Task<string> ReadHostLogAsync(DirectSessionRunnerClient client, Guid sessionId, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetDirectoryName(client.PtyHostManifestDir)!, "logs", sessionId.ToString("N") + ".log");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task<SessionRunnerTranscriptDto> WaitForPromptAsync(ISessionRunnerClient client, Guid sessionId, CancellationToken ct)
    {
        while (true)
        {
            var transcript = await client.GetTranscriptAsync(sessionId, ct);
            if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.UserPrompt)) return transcript;
            await Task.Delay(25, ct);
        }
    }

    private static Task<string> GitAsync(string cwd, string[] args, CancellationToken ct) => RunAsync("git", args, cwd, ct);
    private static async Task<string> RunAsync(string exe, string[] args, string cwd, CancellationToken ct)
    {
        var info = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start owned qualification helper");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
            throw;
        }
        var output = await stdout;
        await stderr; // Drain, but never export arbitrary CLI diagnostics or auth screens.
        process.ExitCode.ShouldBe(0, "The qualification helper must complete successfully");
        return output;
    }


    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Fake_dashboard_marker_reaches_ready_and_complete_first_prompt(bool linuxMarker)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1004-pty-" + Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        await using var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"),
            ptyBackend: OperatingSystem.IsWindows() ? "modern" : null);
        await using var adapter = new RunnerGrokAdapter(client,
            Options.Create(new AgentRegistrySettings
            {
                GrokReadyMaxWaitMs = 10000, GrokReadyQuietPeriodMs = 200,
                GrokReadyMinTotalWaitMs = 0,
            }), Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await adapter.StartAsync(new AgentLaunchSpec("fakegrok", AgentKind.Grok,
            TestAppHostPath.Require("fakegrok", AppContext.BaseDirectory),
            ["--session-id", sessionId.ToString("D"), "--cwd", cwd],
            new Dictionary<string, string>
            {
                ["GROK_HOME"] = Path.Combine(root, "grok-home"),
                ["ANTIPHON_FAKE_GROK_LINUX_COMPOSER"] = linuxMarker ? "1" : "0",
                // The .NET fake's Unix console can turn a typed CR into LF and
                // deliver it in the body's read. Use its existing transport opt-in.
                ["ANTIPHON_FAKE_LF_ENTER"] = OperatingSystem.IsWindows() ? "0" : "1",
            }, cwd, 120, 30, SessionId: sessionId), deadline.Token);
        (await adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
        var snapshot = await client.GetSnapshotAsync(sessionId, deadline.Token);
        snapshot.RenderedScreen.Split('\n')[25][4].ShouldBe(linuxMarker ? '\u276f' : '>');
        GrokStartupScreen.Classify(snapshot.RenderedScreen).Reason.ShouldBe(GrokStartupReason.Ready);
        (await client.GetTranscriptAsync(sessionId, deadline.Token)).Entries
            .ShouldNotContain(x => x.Kind == TranscriptKinds.UserPrompt);

        // A single-line nonce avoids the .NET fake's unqualified Unix multi-line
        // console behavior. Real Grok paste/Enter qualification belongs to the canary.
        const string body = "C1004 complete first prompt HEAD and TAIL";
        await adapter.SendPromptAsync(body, deadline.Token);
        SessionRunnerTranscriptDto transcript;
        do
        {
            transcript = await client.GetTranscriptAsync(sessionId, deadline.Token);
            if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.UserPrompt)) break;
            await Task.Delay(25, deadline.Token);
        } while (true);
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt).ToArray();
        prompts.ShouldHaveSingleItem();
        prompts[0].Text.ShouldBe(body);
        // DirectSessionRunnerClient owns and kills every child on disposal. Retain its
        // unique log root for diagnosis; it never uses the production runner or provider.
    }
}
