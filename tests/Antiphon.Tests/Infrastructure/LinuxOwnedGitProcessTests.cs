using System.Diagnostics;
using System.Text;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LinuxOwnedGitProcessTests
{
    [Test]
    [Timeout(10_000)]
    [NativeLinuxOnly("Linux managed process pipes; Windows transport belongs to S3.")]
    public async Task Disposal_closes_all_used_streams(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) { Skip.Test("Linux managed pipes"); return; }
        using var adapter = new LinuxOwnedGitProcess(new EmptyMembers());
        using var owner = new OwnedGitProcess(adapter);
        owner.Start(new ProcessStartInfo("/bin/cat")
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        }).State.ShouldBe(OwnedGitLaunchState.Started);
        var input = owner.StandardInput;
        var output = owner.StandardOutput;
        var error = owner.StandardError;
        try
        {
            await input.WriteLineAsync("roundtrip".AsMemory(), ct);
            await input.FlushAsync(ct);
            (await output.ReadLineAsync(ct)).ShouldBe("roundtrip");
            input.Close();
            (await owner.WaitForRootExitAsync(ct)).ShouldBeTrue();
            owner.Dispose();
            output.BaseStream.CanRead.ShouldBeFalse("Dispose must close used stdout");
            error.BaseStream.CanRead.ShouldBeFalse("Dispose must close used stderr");
        }
        finally { input.Dispose(); output.Dispose(); error.Dispose(); }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Timeout(10_000)]
    [NativeLinuxOnly("Real Linux double fork, setsid, legacy kill and independent safety marker; Windows is S3.")]
    public async Task Termination_kills_reparented_member_before_rescue(bool closed, bool rootExited, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) { Skip.Test("Linux orphan topology"); return; }
        using var fixture = new OwnedProcessContainmentFixture();
        await using var owner = new OwnedGitProcess(new LinuxOwnedGitProcess());
        using (TestTiming.BeginPhase(TestPhaseKind.ProcessSpawn, "spawn"))
            owner.Start(fixture.StartInfo(closed)).State.ShouldBe(OwnedGitLaunchState.Started);
        await fixture.ReadyAsync(ct);
        fixture.Survivor.ShouldNotBeNull();
        fixture.Survivor!.ParentPid.ShouldNotBe(fixture.IntermediatePid);
        OwnedProcessContainmentFixture.IsAlive(fixture.IntermediatePid).ShouldBeFalse();
        fixture.StartStrangers();
        if (rootExited)
        {
            owner.StandardInput.Close();
            (await owner.WaitForRootExitAsync(ct)).ShouldBeTrue();
        }
        OwnedGitCleanup result;
        using (TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "cleanup"))
            result = await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(6));
        result.Joined.ShouldBeTrue();
        result.Members.State.ShouldBe(OwnedGitMembershipState.Empty);
        OwnedProcessContainmentFixture.IsAlive(fixture.SurvivorPid).ShouldBeFalse("before independent rescue/release");
        foreach (var stranger in fixture.Strangers) stranger.HasExited.ShouldBeFalse();
        (await owner.StandardOutput.ReadToEndAsync(ct)).ShouldBeEmpty();
        (await owner.StandardError.ReadToEndAsync(ct)).ShouldBeEmpty();
    }

    [Test]
    [Timeout(10_000)]
    [NativeLinuxOnly("A real Linux fork is released after the census and before the first signal.")]
    public async Task A_child_born_during_signaling_is_joined_by_a_later_pass(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) { Skip.Test("Linux late fork"); return; }
        using var fixture = new OwnedProcessContainmentFixture();
        var io = new LateForkOperations(fixture);
        await using var owner = new OwnedGitProcess(new LinuxOwnedGitProcess(new LinuxOwnedGitMembers(io)));
        owner.Start(fixture.StartInfo(true)).State.ShouldBe(OwnedGitLaunchState.Started);
        await fixture.ReadyAsync(ct);
        var result = await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(6));
        fixture.LatePid.ShouldBeGreaterThan(1);
        result.Joined.ShouldBeTrue();
        OwnedProcessContainmentFixture.IsAlive(fixture.LatePid).ShouldBeFalse("late member must die before rescue");
        OwnedProcessContainmentFixture.IsAlive(fixture.SurvivorPid).ShouldBeFalse();
        io.SignalPasses.ShouldBeGreaterThan(1);
    }

    [Test]
    [Arguments("ANTIPHON_REPOSITORY_CHILD=mine\0", true)]
    [Arguments("OTHER=x\0ANTIPHON_REPOSITORY_CHILD=mine\0NEXT=y\0", true)]
    [Arguments("XANTIPHON_REPOSITORY_CHILD=mine\0", false)]
    [Arguments("OTHER=ANTIPHON_REPOSITORY_CHILD=mine\0", false)]
    [Arguments("ANTIPHON_REPOSITORY_CHILD=mine-more\0", false)]
    [Arguments("ANTIPHON_REPOSITORY_CHILD=other\0", false)]
    public async Task Only_exact_nul_delimited_marker_fields_are_members(string environment, bool live)
    {
        var io = new FakeProc { EnvironmentBytes = Encoding.UTF8.GetBytes(environment) };
        using var members = Census(io);
        var result = await members.ProbeAsync(null, Deadline(), default);
        result.State.ShouldBe(live ? OwnedGitMembershipState.Live : OwnedGitMembershipState.Empty);
        await members.SignalAsync(null, Deadline(), default);
        io.Signals.Count.ShouldBe(live ? 1 : 0);
    }

    [Test]
    [Arguments("hidden")]
    [Arguments("listing")]
    [Arguments("stat")]
    [Arguments("environment")]
    [Arguments("truncated")]
    [Arguments("unterminated")]
    [Arguments("entries")]
    [Arguments("members")]
    [Arguments("deadline")]
    public async Task Incomplete_observation_is_unknown_never_empty(string fault)
    {
        var io = new FakeProc();
        var clock = new TestClock();
        if (fault == "hidden") io.Hidden = true;
        if (fault == "listing") io.ListFailure = true;
        if (fault == "stat") io.Stat = _ => throw new UnauthorizedAccessException();
        if (fault == "environment") io.EnvironmentRead = _ => throw new UnauthorizedAccessException();
        if (fault == "truncated") io.EnvironmentBytes = new byte[LinuxOwnedGitMembers.MaxEnvironmentBytes + 1];
        if (fault == "unterminated") io.EnvironmentBytes = Encoding.UTF8.GetBytes("ANTIPHON_REPOSITORY_CHILD=mine");
        if (fault == "entries") io.Entries = Enumerable.Range(100, LinuxOwnedGitMembers.MaxEntries + 1).Select(x => x.ToString());
        if (fault == "members") io.Entries = Enumerable.Range(100, LinuxOwnedGitMembers.MaxMembers + 1).Select(x => x.ToString());
        if (fault == "deadline") io.EnvironmentRead = _ => { clock.Advance(); return io.EnvironmentBytes; };
        using var members = Census(io);
        var result = await members.ProbeAsync(null, new(clock, TimeSpan.FromSeconds(1)), default);
        result.State.ShouldBe(OwnedGitMembershipState.Unknown);
        if (fault == "members") result.Survivors.ShouldBe(LinuxOwnedGitMembers.MaxMembers);
        io.StatReads.ShouldBeLessThanOrEqualTo(LinuxOwnedGitMembers.MaxEntries * 2);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Replacement_at_each_identity_read_prevents_a_signal(int boundary)
    {
        var io = new FakeProc();
        // First observation must establish the expected identity; boundary 1 replaces
        // it immediately after that read, boundary 2 after the marker, 3/4 before kill.
        if (boundary == 1)
            io.EnvironmentRead = _ => { io.Stat = pid => new(pid, 1, 999, 'S'); return io.EnvironmentBytes; };
        else
            io.Stat = pid => new(pid, 1, io.StatReads >= boundary ? 999 : 100, 'S');
        using var members = Census(io);
        await members.SignalAsync(null, Deadline(), default);
        io.Signals.ShouldBeEmpty();
    }

    [Test]
    [Arguments("marker")]
    [Arguments("gone")]
    [Arguments("unreadable")]
    [Arguments("truncated")]
    [Arguments("zombie")]
    public async Task Changed_marker_or_unavailable_identity_is_never_signaled(string change)
    {
        var io = new FakeProc();
        io.EnvironmentRead = read => read == 1 ? io.EnvironmentBytes : change switch
        {
            "gone" => throw new FileNotFoundException(),
            "unreadable" => throw new UnauthorizedAccessException(),
            "truncated" => new byte[LinuxOwnedGitMembers.MaxEnvironmentBytes + 1],
            _ => Encoding.UTF8.GetBytes("ANTIPHON_REPOSITORY_CHILD=someone-else\0")
        };
        if (change == "zombie") io.Stat = pid => new(pid, 1, 100, io.StatReads >= 3 ? 'Z' : 'S');
        using var members = Census(io);
        await members.SignalAsync(null, Deadline(), default);
        io.Signals.ShouldBeEmpty();
    }

    [Test]
    public async Task Gone_zombie_older_and_invalid_pids_cannot_be_writers()
    {
        var io = new FakeProc
        {
            Entries = ["1", "0", "-1", "bad", Environment.ProcessId.ToString(), "100", "101", "102"],
            Stat = pid => pid == 100 ? null : new(pid, 1, 99, pid == 101 ? 'Z' : 'S'),
            EnvironmentRead = _ => throw new UnauthorizedAccessException()
        };
        using var members = Census(io);
        (await members.ProbeAsync(new(40, null, 100), Deadline(), default)).State.ShouldBe(OwnedGitMembershipState.Empty);
        await members.SignalAsync(new(40, null, 100), Deadline(), default);
        io.Signals.ShouldBeEmpty();
        io.EnvironmentReads.ShouldBe(0);
    }

    [Test]
    public async Task Unknown_root_lower_bound_cannot_exempt_an_unreadable_process()
    {
        var io = new FakeProc { EnvironmentRead = _ => throw new UnauthorizedAccessException() };
        using var members = Census(io);
        (await members.ProbeAsync(new(40, null, null), Deadline(), default)).State.ShouldBe(OwnedGitMembershipState.Unknown);
        members.CaptureRoot(100, null).LinuxStartTicks.ShouldBe(100L);
        io.Stat = _ => throw new UnauthorizedAccessException();
        members.CaptureRoot(100, null).LinuxStartTicks.ShouldBeNull();
    }

    [Test]
    public async Task Ancestry_backstop_revalidates_unmarked_children_and_every_parent()
    {
        var io = new FakeProc { Entries = ["100", "101"], EnvironmentBytes = [] };
        io.Stat = pid => new(pid, pid == 100 ? 1 : 100, pid, 'S');
        using var members = Census(io);
        await members.SignalAsync(new(100, null, 100), Deadline(), default);
        io.Signals.ShouldBe([100, 101]);
        io.Signals.Clear();
        io.StatReads = 0;
        io.Stat = pid => new(pid, pid == 100 ? 1 : 100, pid == 100 && io.StatReads > 2 ? 900 : pid, 'S');
        await members.SignalAsync(new(100, null, 100), Deadline(), default);
        io.Signals.ShouldBeEmpty();
    }

    [Test]
    public async Task Signal_then_fresh_complete_empty_probe_is_required()
    {
        var io = new FakeProc();
        using var members = Census(io);
        (await members.ProbeAsync(null, Deadline(), default)).State.ShouldBe(OwnedGitMembershipState.Live);
        (await members.SignalAsync(null, Deadline(), default)).ShouldBe(1);
        // A successful kill request is not proof; the process is still observable.
        (await members.ProbeAsync(null, Deadline(), default)).State.ShouldBe(OwnedGitMembershipState.Live);
        io.Stat = _ => null;
        (await members.ProbeAsync(null, Deadline(), default)).State.ShouldBe(OwnedGitMembershipState.Empty);
    }

    private static LinuxOwnedGitMembers Census(FakeProc io)
    {
        var members = new LinuxOwnedGitMembers(io);
        members.SetMarker("mine");
        return members;
    }
    private static OwnedGitDeadline Deadline() => new(TimeProvider.System, TimeSpan.FromSeconds(6));
    private sealed class TestClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance() => _ticks += TimeSpan.FromSeconds(2).Ticks;
    }
    private sealed class FakeProc : ILinuxProcOperations
    {
        internal bool Hidden, ListFailure;
        internal int StatReads, EnvironmentReads;
        internal IEnumerable<string> Entries = ["100"];
        internal byte[] EnvironmentBytes = Encoding.UTF8.GetBytes("ANTIPHON_REPOSITORY_CHILD=mine\0");
        internal Func<int, LinuxProcIdentity?> Stat = pid => new(pid, 1, 100, 'S');
        internal Func<int, byte[]>? EnvironmentRead;
        internal readonly List<int> Signals = [];
        public bool HidesProcesses() => Hidden;
        public IEnumerable<string> EnumerateEntries() => ListFailure ? throw new IOException() : Entries;
        public LinuxProcIdentity? ReadIdentity(int pid) { StatReads++; return Stat(pid); }
        public ValueTask<byte[]> ReadEnvironmentAsync(int pid, CancellationToken token) =>
            ValueTask.FromResult(EnvironmentRead?.Invoke(++EnvironmentReads) ?? EnvironmentBytes);
        public ValueTask SignalAsync(int pid, CancellationToken token) { Signals.Add(pid); return ValueTask.CompletedTask; }
    }
    private sealed class LateForkOperations(OwnedProcessContainmentFixture fixture) : ILinuxProcOperations
    {
        private readonly LinuxProcOperations _native = new();
        internal int SignalPasses;
        public bool HidesProcesses() => _native.HidesProcesses();
        public IEnumerable<string> EnumerateEntries() => _native.EnumerateEntries();
        public LinuxProcIdentity? ReadIdentity(int pid) => _native.ReadIdentity(pid);
        public ValueTask<byte[]> ReadEnvironmentAsync(int pid, CancellationToken token) => _native.ReadEnvironmentAsync(pid, token);
        public async ValueTask SignalAsync(int pid, CancellationToken token)
        {
            if (SignalPasses++ == 0) await fixture.ReleaseLateForkAsync(token);
            await _native.SignalAsync(pid, token);
        }
    }

    private sealed class EmptyMembers : ILinuxOwnedGitMembers
    {
        public OwnedGitRoot CaptureRoot(int pid, DateTime? startUtc) => new(pid, startUtc, null);
        public void SetMarker(string marker) { }
        public ValueTask<OwnedGitMembers> ProbeAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
            CancellationToken cancellationToken) => ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
        public ValueTask<int> SignalAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
            CancellationToken cancellationToken) => ValueTask.FromResult(0);
        public void Dispose() { }
    }
}
