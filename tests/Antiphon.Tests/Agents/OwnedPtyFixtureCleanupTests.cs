using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Integration")]
public class OwnedPtyFixtureCleanupTests
{
    [Test, Arguments("transient"), Arguments("persistent"), Arguments("nonretryable"), Arguments("body-and-cleanup")]
    public async Task Delete_observes_release_and_preserves_failures(string scenario)
    {
        var root = Directory.CreateTempSubdirectory("c1515-policy-").FullName;
        var image = Path.Combine(root, "OpenConsole.exe");
        await File.WriteAllTextAsync(image, "owned image");
        var elapsed = TimeSpan.Zero;
        var attempts = 0;
        var diagnostics = 0;
        var denied = new IOException("captured image refused", unchecked((int)0x80070020));
        var unrelated = new IOException("unrelated failure", unchecked((int)0x80070070));
        var cleanup = new OwnedPtyFixtureCleanup(root, [new(image, File.GetAttributes(image))], "pid=41 start=42 image=owned",
            deleteTree: path =>
            {
                attempts++;
                if (scenario == "nonretryable") throw unrelated;
                if (scenario != "transient" || attempts < 3) throw denied;
                Directory.Delete(path, true);
            }, deleteFile: _ => throw denied,
            diagnose: paths => { paths.ShouldBe(new[] { image }); diagnostics++; return Task.FromResult("holder pid=51 start=52 application=fixture"); },
            elapsed: () => elapsed, delay: (span, _) => { elapsed += span; return Task.CompletedTask; }, windows: true);
        try
        {
            if (scenario == "transient")
            {
                Exception? failure = null;
                try { await cleanup.DeleteAsync(); } catch (Exception ex) { failure = ex; }
                failure.ShouldBeNull("transient refusal must wait for actual private-tree deletion");
                Directory.Exists(root).ShouldBeFalse();
                attempts.ShouldBe(3); diagnostics.ShouldBe(1);
                cleanup.Diagnostic.ShouldContain("holder pid=51 start=52");
            }
            else if (scenario == "persistent")
            {
                var failure = await Should.ThrowAsync<IOException>(() => cleanup.DeleteAsync());
                failure.HResult.ShouldBe(denied.HResult);
                elapsed.ShouldBe(TimeSpan.FromSeconds(5), "one bounded retry budget");
                attempts.ShouldBe(101);
                diagnostics.ShouldBe(1);
                failure.Message.ShouldContain(image);
                failure.Message.ShouldContain("holder pid=51 start=52");
                failure.InnerException.ShouldBeSameAs(denied);
                Directory.Exists(root).ShouldBeTrue();
                var second = await Should.ThrowAsync<IOException>(() => cleanup.DeleteAsync());
                second.ShouldBeSameAs(failure); attempts.ShouldBe(101);
            }
            else if (scenario == "nonretryable")
            {
                (await Should.ThrowAsync<IOException>(() => cleanup.DeleteAsync())).ShouldBeSameAs(unrelated);
                elapsed.ShouldBe(TimeSpan.Zero); attempts.ShouldBe(1); diagnostics.ShouldBe(0);
                // A plausible native code alone cannot authorize retries or attribute a foreign file.
                foreach (var variant in new[] { "readonly", "changed", "unattributed", "unix" })
                {
                    var calls = 0;
                    var observedAttributes = File.GetAttributes(image);
                    var policy = new OwnedPtyFixtureCleanup(root, [new(image, observedAttributes)], variant,
                        deleteTree: _ => { calls++; throw denied; },
                        deleteFile: _ => { if (variant != "unattributed") throw denied; },
                        attributes: _ => variant == "readonly" ? observedAttributes | FileAttributes.ReadOnly
                            : variant == "changed" ? observedAttributes ^ FileAttributes.Hidden : observedAttributes,
                        diagnose: _ => throw new InvalidOperationException("ineligible denial reached diagnosis"),
                        elapsed: () => elapsed, delay: (_, _) => throw new InvalidOperationException("ineligible denial retried"),
                        windows: variant != "unix");
                    (await Should.ThrowAsync<IOException>(() => policy.DeleteAsync())).ShouldBeSameAs(denied);
                    calls.ShouldBe(1, variant); Directory.Exists(root).ShouldBeTrue();
                }
                Should.Throw<ArgumentException>(() => new OwnedPtyFixtureCleanup(root,
                    [new(Path.Combine(Path.GetDirectoryName(root)!, "OpenConsole.exe"), FileAttributes.Normal)], "foreign"));
                using var canceled = new CancellationTokenSource();
                canceled.Cancel();
                var noDelete = new OwnedPtyFixtureCleanup(root, [], "canceled", deleteTree: _ => throw new InvalidOperationException("canceled deletion ran"));
                await Should.ThrowAsync<OperationCanceledException>(() => noDelete.DeleteAsync(canceled.Token));
            }
            else
            {
                var body = new InvalidOperationException("body sentinel");
                var aggregate = await Should.ThrowAsync<AggregateException>(() => TestOwnedPtyHost.ScopeAsync(
                    () => Task.FromException(body), () => cleanup.DeleteAsync()));
                aggregate.InnerExceptions[0].ShouldBeSameAs(body);
                var failure = aggregate.InnerExceptions[1];
                (await Should.ThrowAsync<IOException>(() => cleanup.DeleteAsync())).ShouldBeSameAs(failure);
                failure.HResult.ShouldBe(denied.HResult);
                elapsed.ShouldBe(TimeSpan.FromSeconds(5), "final cleanup shares its failure and budget");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
