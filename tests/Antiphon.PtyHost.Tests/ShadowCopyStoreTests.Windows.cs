using System.Diagnostics;
using Antiphon.PtyHost.Client;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

public partial class ShadowCopyStoreTests
{
    [Test]
    public async Task Windows_publication_survives_a_transient_payload_handle()
    {
        RequireWindows();
        var (root, source, store) = CreateFixture();
        try
        {
            // Exercise real Windows rename semantics, including the same .copying path shape
            // as the E2E failure. No filesystem operation or exception is mocked.
            for (var iteration = 0; iteration < 15; iteration++)
            {
                var staging = Path.Combine(store.BinRoot, $"{iteration}.copying");
                var final = Path.Combine(store.BinRoot, $"{iteration}.published");
                Directory.CreateDirectory(staging);
                File.Copy(Path.Combine(source, "Antiphon.PtyHost.exe"), Path.Combine(staging, "Antiphon.PtyHost.exe"));
                using var held = File.OpenRead(Path.Combine(staging, "Antiphon.PtyHost.exe"));

                var denied = Should.Throw<IOException>(() => Directory.Move(staging, final));
                denied.HResult.ShouldBe(unchecked((int)0x80070005));
                var release = Task.Run(async () =>
                {
                    await Task.Delay(150);
                    held.Dispose();
                });
                try
                {
                    ShadowCopyStore.PublishStagingDirectory(staging, final);
                    File.ReadAllText(Path.Combine(final, "Antiphon.PtyHost.exe")).ShouldBe("windows-apphost");
                    Directory.Exists(staging).ShouldBeFalse();
                }
                finally
                {
                    await release;
                }
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public void Windows_publication_preserves_a_persistent_access_denied_failure()
    {
        RequireWindows();
        var (root, _, store) = CreateFixture();
        try
        {
            var staging = Path.Combine(store.BinRoot, "held.copying");
            var final = Path.Combine(store.BinRoot, "published");
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "payload"), "original");
            using var held = File.OpenRead(Path.Combine(staging, "payload"));
            var elapsed = Stopwatch.StartNew();

            var denied = Should.Throw<IOException>(() =>
                ShadowCopyStore.PublishStagingDirectory(staging, final));

            denied.HResult.ShouldBe(unchecked((int)0x80070005));
            elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(3), "a persistent denial must remain bounded");
            Directory.Exists(final).ShouldBeFalse();
            File.ReadAllText(Path.Combine(staging, "payload")).ShouldBe("original");
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Test]
    public void Publication_never_overwrites_an_existing_destination()
    {
        var (root, _, store) = CreateFixture();
        try
        {
            var staging = Path.Combine(store.BinRoot, "loser.copying");
            var final = Path.Combine(store.BinRoot, "winner");
            Directory.CreateDirectory(staging);
            Directory.CreateDirectory(final);
            File.WriteAllText(Path.Combine(staging, "payload"), "loser");
            File.WriteAllText(Path.Combine(final, "payload"), "winner");

            Should.Throw<IOException>(() => ShadowCopyStore.PublishStagingDirectory(staging, final));

            File.ReadAllText(Path.Combine(final, "payload")).ShouldBe("winner");
            File.ReadAllText(Path.Combine(staging, "payload")).ShouldBe("loser");
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("Requires native Windows file sharing semantics.");
    }
}
