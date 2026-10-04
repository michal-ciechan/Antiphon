using System.Diagnostics;
using System.Management;
using Antiphon.PtyHost.Protocol;
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
