using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Antiphon.PtyHost.Client;
using Shouldly;
using TUnit.Core;

namespace Antiphon.PtyHost.Tests;

public partial class ShadowCopyStoreTests
{
    [Test]
    public async Task Windows_publication_survives_a_transient_directory_handle()
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
                using var held = HoldDirectoryWithoutDeleteSharing(staging);

                var denied = Should.Throw<UnauthorizedAccessException>(() => Directory.Move(staging, final));
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
            using var held = HoldDirectoryWithoutDeleteSharing(staging);
            var elapsed = Stopwatch.StartNew();

            var denied = Should.Throw<UnauthorizedAccessException>(() =>
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
            throw new TUnit.Core.Exceptions.SkipTestException("Requires native Windows directory sharing semantics.");
    }

    private static SafeFileHandle HoldDirectoryWithoutDeleteSharing(string path)
    {
        // OPEN_EXISTING + FILE_FLAG_BACKUP_SEMANTICS opens the directory itself. Read/write
        // sharing lets the copy finish; withholding FILE_SHARE_DELETE prevents its rename.
        var handle = CreateFileW(path, 0, 3, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new System.ComponentModel.Win32Exception(error);
        }
        return handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
