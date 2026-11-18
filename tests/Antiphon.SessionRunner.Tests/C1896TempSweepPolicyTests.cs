using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C1677TempSweepTests;

namespace Antiphon.SessionRunner.Tests;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(10_000)]
public sealed class C1896TempSweepPolicyTests
{
    [Test]
    [Arguments("review-clone")]
    [Arguments("session-ansi")]
    public async Task New_families_are_disabled_without_explicit_opt_in(string family, CancellationToken ct)
    {
        using var s = new Scratch();
        var name = family == "review-clone" ? "antiphon-review-1234abcd" : Hex32() + ".ansi.log";
        if (family == "review-clone") s.Entry(name, TimeSpan.FromDays(10));
        else Log(s, name, 240);
        var response = await s.Sweep().SweepAsync(Request(), ct);
        var candidate = Single(response, name);
        candidate.Family.ShouldBe(family);
        candidate.Reason.ShouldBe(TempSweepCodes.FamilyDisabled);
        response.Removed.ShouldBe(0);
        File.Exists(s.Tmp + "/" + name).ShouldBe(family == "session-ansi");
        Directory.Exists(s.Tmp + "/" + name).ShouldBe(family == "review-clone");
    }

    [Test]
    public async Task Unregistered_scratch_ownership_never_authorizes_a_clone_removal(CancellationToken ct)
    {
        using var s = new Scratch();
        const string name = "antiphon-review-1234abcd";
        var path = s.Entry(name, TimeSpan.FromDays(10));
        var response = await s.Sweep().SweepAsync(Enabled("review-clone"), ct);
        KeptIntact(response, name, "scratch_registration_missing", path);
        response.Removed.ShouldBe(0);
    }

    [Test]
    public async Task Scratch_clones_have_a_seven_day_age_floor(CancellationToken ct)
    {
        using var s = new Scratch();
        const string name = "antiphon-review-1234abcd";
        var path = s.Entry(name, TimeSpan.FromHours(100));
        var response = await s.Sweep().SweepAsync(Enabled("review-clone"), ct);
        KeptIntact(response, name, TempSweepCodes.TooYoung, path);
    }

    [Test]
    [Arguments(167, true)]
    [Arguments(169, true)]
    [Arguments(169, false)]
    public async Task Ansi_logs_obey_seven_days_and_preview(int hours, bool execute, CancellationToken ct)
    {
        using var s = new Scratch();
        var name = Hex32() + ".ansi.log";
        Log(s, name, hours);
        var response = await s.Sweep().SweepAsync(Enabled("session-ansi", execute), ct);
        var candidate = Single(response, name);
        candidate.Bytes.ShouldBe(4);
        if (hours < 168)
        {
            candidate.Reason.ShouldBe(TempSweepCodes.TooYoung);
            response.Removed.ShouldBe(0);
        }
        else
        {
            candidate.Verdict.ShouldBe(execute ? TempSweepCodes.Removed : TempSweepCodes.WouldRemove);
            response.Removed.ShouldBe(1);
            response.RemovedBytes.ShouldBe(4);
        }
        File.Exists(s.Tmp + "/" + name).ShouldBe(hours < 168 || !execute);
    }

    [Test]
    public async Task A_held_ansi_log_survives_even_after_seven_days(CancellationToken ct)
    {
        using var s = new Scratch();
        var name = Hex32() + ".ansi.log";
        Log(s, name, 240);
        s.Holders.Next = _ => new HolderScan(true, [new WorkspaceSlotHolder(123, "runner", "fd")]);
        var response = await s.Sweep().SweepAsync(Enabled("session-ansi"), ct);
        Single(response, name).Reason.ShouldBe(TempSweepCodes.LiveProcess);
        File.ReadAllText(s.Tmp + "/" + name).ShouldBe("ansi");
        response.Removed.ShouldBe(0);
    }

    internal static PhoneHomeTempSweepRequest Enabled(string family, bool execute = true) =>
        Request(execute, families: new Dictionary<string, bool> { [family] = true });

    internal static void Log(Scratch s, string name, int hours)
    {
        File.WriteAllText(s.Tmp + "/" + name, "ansi");
        s.Age(s.Tmp + "/" + name, TimeSpan.FromHours(hours));
    }
}
