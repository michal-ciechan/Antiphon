using System.Diagnostics;
using Antiphon.Server.Infrastructure.Git;

namespace Antiphon.Tests.TestHelpers;

/// <summary>Independent safety token and release barrier. Rescue is never the cleanup under test.</summary>
internal sealed class OwnedProcessContainmentFixture : IDisposable
{
    private readonly DescendantLeakScope _safety = DescendantLeakScope.Create();
    private readonly List<Process> _strangers = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    internal string DirectoryPath { get; } = Path.Combine(
        FindRoot(), ".antiphon", "containment-fixtures", Guid.NewGuid().ToString("N"));
    internal int SurvivorPid { get; private set; }
    internal LinuxProcIdentity? Survivor { get; private set; }
    internal int LatePid { get; private set; }
    internal Process[] Strangers => _strangers.ToArray();
    internal int IntermediatePid { get; private set; }

    internal OwnedProcessContainmentFixture()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath, "fixture.sh"),
            "case \"$1\" in\n" +
            "root) /bin/sh \"$0\" intermediate \"$2\" \"$3\"; read line ;;\n" +
            "intermediate) echo $$ > \"$2/intermediate.pid\"; setsid /bin/sh \"$0\" survivor \"$2\" \"$3\" & ;;\n" +
            "survivor)\n" +
            " if [ \"$3\" = closed ]; then exec </dev/null >/dev/null 2>&1; fi\n" +
            " echo $$ > \"$2/survivor.pid\"\n" +
            " while [ ! -f \"$2/release\" ]; do\n" +
            "  if [ -f \"$2/spawn\" ] && [ ! -f \"$2/late.pid\" ]; then /bin/sleep 120 & echo $! > \"$2/late.pid\"; fi\n" +
            "  /bin/sleep 0.02\n" +
            " done ;;\nesac\n");
    }

    internal ProcessStartInfo StartInfo(bool closed)
    {
        var start = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(DirectoryPath, "fixture.sh"));
        start.ArgumentList.Add("root");
        start.ArgumentList.Add(DirectoryPath);
        start.ArgumentList.Add(closed ? "closed" : "inherited");
        _safety.Apply(start);
        return start;
    }

    internal async Task ReadyAsync(CancellationToken ct)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "wait");
        await UntilAsync(() => TryPid("survivor.pid", out _) && TryPid("intermediate.pid", out _), ct);
        TryPid("survivor.pid", out var survivor);
        TryPid("intermediate.pid", out var intermediate);
        SurvivorPid = survivor;
        IntermediatePid = intermediate;
        await UntilAsync(() => !IsAlive(IntermediatePid)
            && LinuxProcOperations.ReadIdentityFile(SurvivorPid) is { Alive: true, ParentPid: var parent }
            && parent != IntermediatePid, ct);
        Survivor = LinuxProcOperations.ReadIdentityFile(SurvivorPid);
    }

    internal void StartStrangers()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessSpawn, "spawn");
        foreach (var marked in new[] { false, true })
        {
            var start = new ProcessStartInfo("/bin/sleep") { UseShellExecute = false };
            start.ArgumentList.Add("120");
            _safety.Apply(start);
            start.Environment.Remove(OwnedGitProcess.MarkerVariable);
            if (marked) start.Environment[OwnedGitProcess.MarkerVariable] = Guid.NewGuid().ToString("N");
            _strangers.Add(Process.Start(start) ?? throw new InvalidOperationException("fixture_start_failed"));
        }
    }

    internal async ValueTask ReleaseLateForkAsync(CancellationToken ct)
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "child");
        File.WriteAllText(Path.Combine(DirectoryPath, "spawn"), "go");
        await UntilAsync(() => TryPid("late.pid", out _), ct);
        TryPid("late.pid", out var late);
        LatePid = late;
    }

    internal static bool IsAlive(int pid) => LinuxProcOperations.ReadIdentityFile(pid) is { Alive: true };
    private bool TryPid(string name, out int pid)
    {
        pid = 0;
        var path = Path.Combine(DirectoryPath, name);
        return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out pid) && pid > 1;
    }
    private async Task UntilAsync(Func<bool> ready, CancellationToken ct)
    {
        while (!ready())
        {
            ct.ThrowIfCancellationRequested();
            if (_clock.Elapsed >= TimeSpan.FromSeconds(10)) throw new TimeoutException("containment_fixture_readiness");
            await Task.Delay(10, ct);
        }
    }
    public void Dispose()
    {
        using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "cleanup");
        File.WriteAllText(Path.Combine(DirectoryPath, "release"), "release");
        _safety.KillAll();
        foreach (var stranger in _strangers)
        {
            stranger.WaitForExit(2000);
            stranger.Dispose();
        }
        _safety.Dispose();
        // Retain fixture files with ignored evidence. No recursive cleanup or inferred ownership.
    }
    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Antiphon.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("fixture_repository_missing");
    }
}
