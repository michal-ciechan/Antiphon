using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration"), ParallelLimiter<ProcessSpawnLimit>]
public class OwnedPtyFixtureCleanupWindowsTests
{
    [Test, Arguments(true), Arguments(false)]
    public async Task Refusal_names_holder_and_never_stops_it(bool release)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Native Windows checkpoint required");
        var root = Directory.CreateTempSubdirectory("c1515-holder-").FullName;
        var image = Path.Combine(root, "OpenConsole.exe");
        await File.WriteAllTextAsync(image, "owned file");
        try
        {
            await using var holder = await WorktreeLockChild.StartAsync("file", image);
            var cleanup = new OwnedPtyFixtureCleanup(root, [new(image, File.GetAttributes(image))], "independent holder witness");
            var observed = false;
            cleanup.DenialObserved = async () =>
            {
                holder.Exited.ShouldBeFalse("diagnosis never stops the holder");
                cleanup.Diagnostic.ShouldContain($"pid={holder.ProcessId}");
                cleanup.Diagnostic.ShouldContain($"start={holder.StartTicks}");
                cleanup.Diagnostic.ShouldContain(image);
                observed = true;
                if (release) await holder.ReleaseAsync();
            };
            if (release) { await cleanup.DeleteAsync(); Directory.Exists(root).ShouldBeFalse(); }
            else
            {
                var failure = await Should.ThrowAsync<IOException>(() => cleanup.DeleteAsync());
                (failure.HResult & 0xffff).ShouldBeOneOf(5, 32, 33);
                holder.Exited.ShouldBeFalse(); Directory.Exists(root).ShouldBeTrue();
                failure.Message.ShouldContain($"pid={holder.ProcessId}");
            }
            observed.ShouldBeTrue("actual denial and holder snapshot acknowledged");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
