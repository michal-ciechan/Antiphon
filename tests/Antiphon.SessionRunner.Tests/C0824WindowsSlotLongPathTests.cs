using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;
using static Antiphon.SessionRunner.Tests.C0824SlotScratch;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0824 S3 CP-3 (D-9). Every slot git call passes <c>-c core.longpaths=true</c>.
/// A flag-off <c>task-&lt;id&gt;</c> mirror stays byte-identical.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(60_000)]
public sealed class C0824WindowsSlotLongPathTests
{
    [Test]
    public async Task Slot_checkout_passes_longpaths(CancellationToken ct)
    {
        using var scratch = Create();
        var pool = scratch.Pool();
        await pool.LeaseAsync(scratch.RequestA(), ct);
        string joined;
        lock (scratch.Argv)
        {
            var checkout = scratch.Argv.Where(argv => argv.Contains("checkout")).ToArray();
            checkout.ShouldNotBeEmpty();
            joined = string.Join("\n", checkout.Select(argv => string.Join(" ", argv.Skip(1))));
        }

        joined.ShouldContain("-c core.longpaths=true");
    }

    [Test]
    public async Task Legacy_mirror_omits_longpaths(CancellationToken ct)
    {
        using var scratch = Create();
        var service = scratch.Service();
        await service.MirrorAsync(new PhoneHomeWorkspaceMirrorRequest(BranchA, scratch.ShaA, "task-aaaaaaaa"), ct);
        lock (scratch.Argv)
        {
            var add = scratch.Argv.Single(argv => argv.Skip(1).SequenceEqual(
                new[] { "worktree", "add", "-B", BranchA, scratch.Worktrees + "/task-aaaaaaaa", scratch.ShaA }));
            add.ShouldNotContain("-c");
            add.ShouldNotContain("core.longpaths=true");
        }
    }
}
