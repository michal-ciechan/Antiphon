using System.Diagnostics;
using System.Management;
using Antiphon.PtyHost.Protocol;
using Antiphon.PtyHost.Client;
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

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class DirectSessionRunnerClientDisposalTests
{
    [Test]
    public async Task Lingering_exited_host_is_reaped_before_the_cleanup_returns()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1020-linger-" + Guid.NewGuid().ToString("N"));
        var hostRoot = Path.Combine(root, "pty-hosts");
        var manifestDir = Path.Combine(hostRoot, "manifests");
        var session = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(hostRoot, "logs"));
        var launcher = new PtyHostLauncher(new ShadowCopyStore(Path.Combine(hostRoot, "bin")), AppContext.BaseDirectory);
        OwnedPtyWitness? witness = null;
        PtyHostClient? pipe = null;
        var owned = new TestOwnedPtyHost(hostRoot);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var hostPid = await launcher.LaunchDetachedAsync(session, manifestDir,
            Path.Combine(hostRoot, "logs", session.ToString("N") + ".log"),
            lingerTtl: TimeSpan.FromSeconds(72), ptyBackend: OperatingSystem.IsWindows() ? "modern" : null, ct: deadline.Token);
        // The fallback below owns exactly this launcher-returned process and generation.
        using var emergencyHost = Process.GetProcessById(hostPid);
        try
        {
            pipe = await PtyHostClient.ConnectAsync(PtyHostProtocol.PipeNameFor(session), TimeSpan.FromSeconds(15), deadline.Token);
            await pipe.LaunchAsync(new LaunchMessage(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                OperatingSystem.IsWindows() ? ["/d", "/q"] : [], new Dictionary<string, string>(), root, 120, 30, 0, false,
                Path.Combine(root, "ansi.log")), deadline.Token);
            await pipe.AttachAsync(0, deadline.Token);
            witness = OwnedPtyWitness.Capture(manifestDir, session);
            owned.Capture(session, SessionBackends.PtyHost, false);
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pipe.OnExited += _ => exited.TrySetResult();
            await pipe.KillAsync(TimeSpan.FromSeconds(2), deadline.Token);
            await exited.Task.WaitAsync(deadline.Token);
            await pipe.DisposeAsync();
            pipe = null; // Deliberately no Shutdown: exercise deterministic linger.
            OwnedPtyWitness.Exited(emergencyHost).ShouldBeFalse("linger-live-before-cleanup");
            File.Delete(PtyHostManifest.PathFor(manifestDir, session));
            var started = Stopwatch.StartNew();
            Exception? failure = null;
            try { await owned.DisposeAsync(true, () => { }, _ => Task.CompletedTask, () => Task.CompletedTask); }
            catch (Exception ex) { failure = ex; }
            OwnedPtyWitness.Exited(emergencyHost).ShouldBeTrue("fallback-host-exited");
            witness.AssertExited();
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10), "cleanup-before-ttl");
            failure.ShouldBeNull();
            witness.Release();
            Directory.Delete(root, true);
            Directory.Exists(root).ShouldBeFalse("private-tree-deletable");
        }
        finally
        {
            if (pipe is not null) await pipe.DisposeAsync();
            if (witness is not null) await witness.DisposeAsync();
            if (!OwnedPtyWitness.Exited(emergencyHost))
            {
                emergencyHost.Kill(entireProcessTree: true);
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!OwnedPtyWitness.Exited(emergencyHost)) await Task.Delay(20, cleanup.Token);
            }
        }
    }

    [Test]
    public async Task Dispose_with_kill_disabled_leaves_the_session_adoptable()
    {
        await using var fixture = await OwnedPtyFixture.StartAsync();
        fixture.Client.KillOnDispose = false;
        await fixture.Adapter.DisposeAsync();
        await fixture.Client.DisposeAsync();
        fixture.Witness.AssertAlive();
        await using var adopter = new DirectSessionRunnerClient(Path.Combine(fixture.Root, "logs"),
            ptyBackend: OperatingSystem.IsWindows() ? "modern" : null);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await adopter.AdoptOrphanedHostsAsync(deadline.Token);
            var adopted = await adopter.GetAsync(fixture.SessionId, deadline.Token);
            adopted.HostPid.ShouldBe(fixture.Witness.Entries.Single(x => x.Role == "host").Process.Id);
            await OwnedPtyFixture.AssertPromptAsync(adopter, fixture.SessionId, deadline.Token);
            await adopter.SimulateRunnerRestartAsync(deadline.Token);
            adopter.KillOnDispose.ShouldBeFalse("restart-preserves-optout");
            fixture.Witness.AssertAlive();
            await OwnedPtyFixture.AssertPromptAsync(adopter, fixture.SessionId, deadline.Token);
        }
        finally { adopter.KillOnDispose = true; await adopter.DisposeAsync(); }
        fixture.Witness.AssertExited();
        fixture.DeleteRoot();
    }

    [Test]
    public async Task Dispose_does_not_touch_another_clients_host()
    {
        await using var a = await OwnedPtyFixture.StartAsync();
        await using var b = await OwnedPtyFixture.StartAsync();
        await a.Client.DisposeAsync();
        a.Witness.AssertExited();
        b.Witness.AssertAlive();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await OwnedPtyFixture.AssertPromptAsync(b.Client, b.SessionId, deadline.Token);
        await b.Client.DisposeAsync();
        b.Witness.AssertExited();
        a.DeleteRoot();
        b.DeleteRoot();
    }

    [Test]
    public async Task Dispose_is_idempotent()
    {
        await using var fixture = await OwnedPtyFixture.StartAsync();
        var calls = 0;
        fixture.Client.OnDisposeCore = () => calls++;
        var first = fixture.Client.DisposeAsync().AsTask();
        var second = fixture.Client.DisposeAsync().AsTask();
        ReferenceEquals(first, second).ShouldBeTrue("second call shares completion");
        await Task.WhenAll(first, second);
        calls.ShouldBe(1, "dispose-core-once");
        fixture.Witness.AssertExited();
        fixture.DeleteRoot();
    }

    [Test]
    public async Task Body_failure_still_reaps_the_owned_host()
    {
        await using var fixture = await OwnedPtyFixture.StartAsync();
        var sentinel = new InvalidOperationException("C1020 body sentinel");
        Exception? caught = null;
        try
        {
            await using (fixture.Client) { throw sentinel; }
        }
        catch (Exception ex) { caught = ex; }
        ReferenceEquals(caught, sentinel).ShouldBeTrue("identical body sentinel");
        fixture.Witness.AssertExited();
        fixture.DeleteRoot();
    }
    [Test]
    public async Task Dispose_awaits_exit_of_its_running_host_and_descendants()
    {
        await using var fixture = await OwnedPtyFixture.StartAsync();
        await fixture.Client.DisposeAsync();
        fixture.Witness.AssertExited();
        fixture.DeleteRoot();
    }
}

// These observer handles are separate from the cleanup implementation and survive manifest removal.
internal sealed class OwnedPtyWitness : IAsyncDisposable
{
    internal sealed record Entry(Process Process, DateTime Start, string Image, string Role, int Parent);
    internal readonly List<Entry> Entries = [];
    public static OwnedPtyWitness Capture(string manifestDir, Guid sessionId)
    {
        var witness = new OwnedPtyWitness();
        try
        {
            var manifest = PtyHostManifest.TryLoad(PtyHostManifest.PathFor(manifestDir, sessionId));
            manifest.ShouldNotBeNull("independent manifest witness");
            manifest.SessionId.ShouldBe(sessionId);
            witness.Add(manifest.HostPid, "host", 0);
            manifest.ChildPid.ShouldNotBeNull("nonempty child witness");
            witness.Add(manifest.ChildPid.Value, "child", manifest.HostPid);
            if (OperatingSystem.IsWindows())
            {
                using var query = new ManagementObjectSearcher("SELECT ProcessId,ParentProcessId,Name FROM Win32_Process");
                using var rows = query.Get();
                var processes = rows.Cast<ManagementObject>().Select(x =>
                    (Pid: Convert.ToInt32(x["ProcessId"]), Parent: Convert.ToInt32(x["ParentProcessId"]), Name: (string)x["Name"])).ToArray();
                var parents = new HashSet<int> { manifest.HostPid };
                bool changed;
                do
                {
                    changed = false;
                    foreach (var row in processes.Where(x => parents.Contains(x.Parent)).ToArray())
                    {
                        if (!parents.Add(row.Pid)) continue;
                        changed = true;
                        if (witness.Entries.All(x => x.Process.Id != row.Pid))
                            witness.Add(row.Pid, row.Name.Equals("OpenConsole.exe", StringComparison.OrdinalIgnoreCase) ? "console" : "descendant", row.Parent);
                    }
                } while (changed);
                witness.Entries.ShouldContain(x => x.Role == "console", "nonempty Windows console witness");
                var log = Path.Combine(Path.GetDirectoryName(manifestDir)!, "logs", sessionId.ToString("N") + ".log");
                File.ReadAllText(log).ShouldContain("pty backend: ModernConPty (requested 'modern')");
            }
            TestOwnedPtyHost.ValidateWitnesses(witness.Entries.Select(x => (x.Role, !Exited(x.Process))), OperatingSystem.IsWindows());
            foreach (var entry in witness.Entries)
            {
                entry.Process.HasExited.ShouldBeFalse("alive-before: " + entry.Role);
                Console.WriteLine($"C1020 witness session={sessionId:D} role={entry.Role} pid={entry.Process.Id} start={entry.Start:O} parent={entry.Parent} image={entry.Image} alive=true at={DateTime.UtcNow:O}");
            }
            return witness;
        }
        catch { foreach (var entry in witness.Entries) entry.Process.Dispose(); throw; }
    }

    private void Add(int pid, string role, int parent)
    {
        var process = Process.GetProcessById(pid);
        try { Entries.Add(new(process, process.StartTime.ToUniversalTime(), process.MainModule!.FileName, role, parent)); }
        catch { process.Dispose(); throw; }
    }

    internal static bool Exited(Process process)
    {
        if (process.HasExited) return true;
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var stat = File.ReadAllText($"/proc/{process.Id}/stat");
            return stat[(stat.LastIndexOf(')') + 2)] is 'Z' or 'X';
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
    }

    public void AssertAlive()
    {
        foreach (var entry in Entries)
            Exited(entry.Process).ShouldBeFalse("detach-" + entry.Role + "-alive");
    }

    public void AssertExited()
    {
        foreach (var entry in Entries)
        {
            var exited = Exited(entry.Process);
            Console.WriteLine($"C1020 witness role={entry.Role} pid={entry.Process.Id} exited={exited} at={DateTime.UtcNow:O}");
            exited.ShouldBeTrue(entry.Role + "-exited-at-return");
        }
    }

    public void Release()
    {
        foreach (var entry in Entries) entry.Process.Dispose();
        Entries.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var entry in Entries)
            {
                if (Exited(entry.Process)) continue;
                using var current = Process.GetProcessById(entry.Process.Id);
                current.StartTime.ToUniversalTime().ShouldBe(entry.Start, "emergency generation must match");
                current.Kill(entireProcessTree: true);
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Entries.Any(x => !Exited(x.Process))) await Task.Delay(20, deadline.Token);
        }
        finally { Release(); }
    }
}

internal sealed class OwnedPtyFixture : IAsyncDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "c1020-pty-" + Guid.NewGuid().ToString("N"));
    public Guid SessionId { get; } = Guid.NewGuid();
    public DirectSessionRunnerClient Client { get; private set; } = null!;
    public RunnerGrokAdapter Adapter { get; private set; } = null!;
    public OwnedPtyWitness Witness { get; private set; } = null!;

    public static async Task<OwnedPtyFixture> StartAsync()
    {
        var fixture = new OwnedPtyFixture();
        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.Root, "cwd"));
            fixture.Client = new(Path.Combine(fixture.Root, "logs"), ptyBackend: OperatingSystem.IsWindows() ? "modern" : null);
            fixture.Adapter = CreateAdapter(fixture.Client);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await fixture.Adapter.StartAsync(new AgentLaunchSpec("fakegrok", AgentKind.Grok,
                TestAppHostPath.Require("fakegrok", AppContext.BaseDirectory),
                ["--session-id", fixture.SessionId.ToString("D"), "--cwd", Path.Combine(fixture.Root, "cwd")],
                new Dictionary<string, string>
                {
                    ["GROK_HOME"] = Path.Combine(fixture.Root, "grok-home"),
                    ["ANTIPHON_FAKE_GROK_LINUX_COMPOSER"] = "0",
                    ["ANTIPHON_FAKE_LF_ENTER"] = OperatingSystem.IsWindows() ? "0" : "1",
                }, Path.Combine(fixture.Root, "cwd"), 120, 30, SessionId: fixture.SessionId), deadline.Token);
            (await fixture.Adapter.WaitForReadyAsync(deadline.Token)).ShouldBeTrue();
            fixture.Witness = OwnedPtyWitness.Capture(fixture.Client.PtyHostManifestDir, fixture.SessionId);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    internal static RunnerGrokAdapter CreateAdapter(DirectSessionRunnerClient client) => new(client,
        Options.Create(new AgentRegistrySettings { GrokReadyMaxWaitMs = 10000, GrokReadyQuietPeriodMs = 200, GrokReadyMinTotalWaitMs = 0 }),
        Options.Create(new SupervisionSettings { DeliveryVerification = new DeliveryVerificationSettings { Enabled = false } }));

    internal static async Task AssertPromptAsync(DirectSessionRunnerClient client, Guid session, CancellationToken ct)
    {
        var baseline = (await client.GetTranscriptAsync(session, ct)).LastSequence;
        var body = "C1020 HEAD " + Guid.NewGuid().ToString("N") + " TAIL";
        await client.SendInputAsync(session, "\u001b[200~" + body + "\u001b[201~", ct);
        await client.SendInputAsync(session, "\r", ct);
        SessionRunnerTranscriptDto transcript;
        do
        {
            transcript = await client.GetTranscriptAsync(session, ct);
            if (transcript.Entries.Any(x => x.Kind == TranscriptKinds.UserPrompt && x.Sequence > baseline)) break;
            await Task.Delay(25, ct);
        } while (true);
        var prompts = transcript.Entries.Where(x => x.Kind == TranscriptKinds.UserPrompt && x.Sequence > baseline).ToArray();
        prompts.ShouldHaveSingleItem();
        prompts[0].Text.ShouldBe(body, "complete unique UserPrompt after baseline");
    }
    public void DeleteRoot()
    {
        Witness.Release();
        Directory.Delete(Root, true);
        Directory.Exists(Root).ShouldBeFalse("private-tree-deletable");
    }

    public async ValueTask DisposeAsync()
    {
        try { if (Adapter is not null) await Adapter.DisposeAsync(); }
        finally
        {
            try { if (Client is not null) await Client.DisposeAsync(); }
            finally { if (Witness is not null) await Witness.DisposeAsync(); }
        }
    }
}
