using System.Text.Json.Nodes;
using Antiphon.LiveLane;
using Antiphon.Nightly;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S21: the cases of the deleted scripts/test-live-lane-run.ps1 and scripts/test-live-lane-outcome.ps1 run in
/// process. Each case id is checked once and reported as "C1545 &lt;method&gt;/&lt;id&gt;", as the C487 harness reported it.
/// </summary>
internal sealed class C1545Cases(string method)
{
    private readonly List<string> _seen = [];
    private readonly List<string> _failed = [];

    public void Check(string id, bool ok, string detail = "")
    {
        _seen.Add(id);
        if (!ok) _failed.Add($"C1545 {method}/{id} " + (detail.Length > 0 ? detail : "contract mismatch (synthetic inputs only)"));
    }

    /// <summary>Every case passed, and exactly the expected ids were checked, in order.</summary>
    public void Complete(params string[] expected)
    {
        _failed.ShouldBeEmpty(string.Join(Environment.NewLine, _failed));
        _seen.ShouldBe(expected);
    }
}

/// <summary>
/// The live lane's native-lock seam bound to the nightly core's shared native-run lock (ReleaseGateNativeLock), as the
/// deleted live-lane-run.ps1 used it: the real lock file under the fixture's coordination root, this machine's process
/// table, and a fake clock for acquiredAt.
/// </summary>
internal sealed class LiveLaneNativeLockSeam : ILiveLaneNativeLock
{
    internal static readonly DateTimeOffset FixedNow = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly NightlyProcessTable _processes = new();
    private readonly ReleaseGateNativeLock _lock;

    public LiveLaneNativeLockSeam() => _lock = new ReleaseGateNativeLock(new FakeTimeProvider(FixedNow), _processes);

    /// <summary>A second holder of the same lock (another lane in this process).</summary>
    public static ReleaseGateNativeLock Foreign() => new(new FakeTimeProvider(FixedNow), new NightlyProcessTable());

    public static string LockPath(string coordinationRoot) => ReleaseGateNativeLock.LockPath(coordinationRoot);

    public LiveLaneLockEntry Enter(string coordinationRoot, string runId, string lane)
    {
        var entry = _lock.Enter(coordinationRoot, runId, lane);
        return new LiveLaneLockEntry(entry.Ok, entry.Reason, Owner(entry.Record));
    }

    public LiveLaneLockOwner? Owner(string coordinationRoot) => Owner(NightlyLockRecord.Read(LockPath(coordinationRoot)));

    public bool IsAlive(int pid) => _processes.IsAlive(pid);

    public void Exit(string coordinationRoot, string runId) => _lock.Exit(coordinationRoot, runId);

    private static LiveLaneLockOwner? Owner(NightlyLockRecord? record) =>
        record == null ? null : new LiveLaneLockOwner(record.Pid, record.ProcessStartedAt, record.RunId, record.Text("lane"));
}

/// <summary>A scratch root for one test, removed afterwards (no repository is ever created in it).</summary>
internal sealed class C1545Scratch : IDisposable
{
    public C1545Scratch(string prefix) => Root = Directory.CreateTempSubdirectory(prefix).FullName;

    public string Root { get; }

    public string NewDirectory()
    {
        var path = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>JSON builders for the fixtures' hashtables.</summary>
internal static class LiveLaneJsonFixtures
{
    public static JsonArray Array(params JsonNode?[] items)
    {
        var array = new JsonArray();
        foreach (var item in items) array.Add(item);
        return array;
    }

    public static JsonObject? Object(JsonNode? node) => node as JsonObject;
}
