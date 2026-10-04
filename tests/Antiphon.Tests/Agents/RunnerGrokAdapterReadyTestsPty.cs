using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>FakeGrok through the isolated runner, native PTY, screen and production ready adapter.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class RunnerGrokAdapterReadyTestsPty
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Fake_dashboard_marker_reaches_ready_and_complete_first_prompt(bool linuxMarker)
    {
        var root = Path.Combine(Path.GetTempPath(), "c1004-pty-" + Guid.NewGuid().ToString("N"));
        var cwd = Path.Combine(root, "cwd");
        Directory.CreateDirectory(cwd);
        var sessionId = Guid.NewGuid();
        var client = new DirectSessionRunnerClient(Path.Combine(root, "logs"),
            ptyBackend: OperatingSystem.IsWindows() ? "modern" : null);
        var adapter = new RunnerGrokAdapter(client,
            Options.Create(new AgentRegistrySettings
            {
                GrokReadyMaxWaitMs = 10000, GrokReadyQuietPeriodMs = 200,
                GrokReadyMinTotalWaitMs = 0,
            }), Options.Create(new SupervisionSettings
            {
                DeliveryVerification = new DeliveryVerificationSettings { Enabled = false },
            }));
        OwnedPtyWitness? witness = null;
        try
        {
                await TestOwnedPtyHost.ScopeAsync(async () =>
                {
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
                witness = OwnedPtyWitness.Capture(client.PtyHostManifestDir, sessionId);
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
            }, async () =>
            {
                    // Adapter releases its connection observation before the client reaps its hosts.
                    await TestOwnedPtyHost.ScopeAsync(() => adapter.DisposeAsync().AsTask(), () => client.DisposeAsync().AsTask());
                });
                witness.ShouldNotBeNull();
                witness.AssertExited();
                witness.Release();
                Directory.Delete(root, true);
                Directory.Exists(root).ShouldBeFalse("private-tree-deletable");
                }
                catch
                {
                    var evidence = Path.Combine(AppContext.BaseDirectory, "c1020-diagnostics", sessionId.ToString("N"));
                    Directory.CreateDirectory(evidence);
                    var logs = Path.Combine(Path.GetDirectoryName(client.PtyHostManifestDir)!, "logs");
                    if (Directory.Exists(logs))
                        foreach (var log in Directory.EnumerateFiles(logs)) File.Copy(log, Path.Combine(evidence, Path.GetFileName(log)), true);
                    Console.WriteLine($"C1020 failure diagnostics={evidence}");
                    throw;
                }
                finally { if (witness is not null) await witness.DisposeAsync(); }
            }
        }
