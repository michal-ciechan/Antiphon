using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0660 D-6 (V-2). The runner Codex probe is metadata-only presence of
/// <c>CODEX_HOME/auth.json</c>: a regular file is signed in, a missing file or home is signed out,
/// and an unconfigured or unreadable store is "cannot tell". It never opens the file, so neither
/// its bytes nor a secret in it can reach the DTO or the log.
/// </summary>
[Category("Unit")]
public class CodexAuthProbeTests
{
    private const string Sentinel = "codex-secret-sentinel-not-for-logs";
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Present_file_returns_presence_without_reading()
    {
        var root = NewHome();
        var log = new List<string>();
        try
        {
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = Posix(root) },
                new ListLogger<CodexAuthProbe>(log),
                clock: new FakeTimeProvider(Now));
            var authPath = Path.Combine(root, "auth.json");
            // Unparseable bytes: a probe that parsed the file could not call this signed in.
            await File.WriteAllBytesAsync(authPath, [0xFF, 0x00, .. System.Text.Encoding.UTF8.GetBytes(Sentinel), 0xFE]);

            RunnerProviderAuthDto dto;
            // Held exclusively: a probe that opened the file would fail here and answer "unreadable".
            await using (new FileStream(authPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                dto = await probe.ProbeAsync("codex", CancellationToken.None);

            dto.Provider.ShouldBe("codex");
            dto.LoggedIn.ShouldBe(true, "a regular auth.json is presence");
            dto.AuthMethod.ShouldBe("auth_file");
            dto.SubscriptionType.ShouldBeNull("presence says nothing about the plan");
            dto.CheckedAtUtc.ShouldBe(Now);
            dto.Error.ShouldBeNull();
            System.Text.Json.JsonSerializer.Serialize(dto).ShouldNotContain(Sentinel);
            string.Join('\n', log).ShouldNotContain(Sentinel);
            log.ShouldNotBeEmpty("the probe logs its answer");
        }
        finally
        {
            DeleteHome(root);
        }
    }

    [Test]
    public async Task Missing_file_is_signed_out()
    {
        var root = NewHome();
        try
        {
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = Posix(root) + "/" }, clock: new FakeTimeProvider(Now));

            var missing = await probe.ProbeAsync("codex", CancellationToken.None);
            missing.Provider.ShouldBe("codex");
            missing.LoggedIn.ShouldBe(false, "no auth.json in an existing home is signed out");
            missing.AuthMethod.ShouldBeNull();
            missing.Error.ShouldBeNull();
            missing.CheckedAtUtc.ShouldBe(Now);

            // A directory named auth.json is not a credential file.
            Directory.CreateDirectory(Path.Combine(root, "auth.json"));
            var directory = await probe.ProbeAsync("codex", CancellationToken.None);
            directory.LoggedIn.ShouldBe(false, "only a regular file counts as presence");
            directory.AuthMethod.ShouldBeNull();
        }
        finally
        {
            DeleteHome(root);
        }
    }

    [Test]
    public async Task Symlinked_auth_file_is_not_presence_but_a_linked_home_is_judged_by_its_file()
    {
        // Review of ff170389: only a regular, non-link auth.json is presence. A link to a real
        // credential file, a dangling link and a link to a directory are all signed out. The
        // home itself being a link (as /state/codex is a bind mount) does not matter: only the
        // final component is judged, with lstat semantics.
        var root = NewHome();
        try
        {
            var home = Path.Combine(root, "home");
            var elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(elsewhere);
            var target = Path.Combine(elsewhere, "real-auth.json");
            await File.WriteAllTextAsync(target, "{}");
            var authPath = Path.Combine(home, "auth.json");
            var linkedHome = Path.Combine(root, "linked-home");
            if (!TryCreateSymlink(authPath, target, directory: false)
                || !TryCreateSymlink(linkedHome, home, directory: true))
            {
                Skip.Test(
                    "This host denied symlink creation (Windows without Developer Mode). The symlinked auth.json "
                    + "refusal runs on the Linux runner, where CreateSymbolicLink needs no privilege.");
                return;
            }

            var probe = new CodexAuthProbe(new PhoneHomeSettings { CodexHome = Posix(home) }, clock: new FakeTimeProvider(Now));

            var toFile = await probe.ProbeAsync("codex", CancellationToken.None);
            toFile.LoggedIn.ShouldBe(false, "a symlinked auth.json is not a regular file, even when its target is");
            toFile.AuthMethod.ShouldBeNull();
            toFile.Error.ShouldBeNull();

            File.Delete(target);
            var dangling = await probe.ProbeAsync("codex", CancellationToken.None);
            dangling.LoggedIn.ShouldBe(false, "a dangling auth.json link is signed out");
            dangling.AuthMethod.ShouldBeNull();

            File.Delete(authPath);
            TryCreateSymlink(authPath, elsewhere, directory: true).ShouldBeTrue();
            var toDirectory = await probe.ProbeAsync("codex", CancellationToken.None);
            toDirectory.LoggedIn.ShouldBe(false, "a link to a directory is signed out");

            // The positive control: a regular auth.json in the same home, reached directly or
            // through a linked home, is presence.
            File.Delete(authPath);
            await File.WriteAllTextAsync(authPath, "{}");
            var regular = await probe.ProbeAsync("codex", CancellationToken.None);
            regular.LoggedIn.ShouldBe(true, "a regular auth.json is presence");
            regular.AuthMethod.ShouldBe("auth_file");

            var viaLinkedHome = await new CodexAuthProbe(
                    new PhoneHomeSettings { CodexHome = Posix(linkedHome) }, clock: new FakeTimeProvider(Now))
                .ProbeAsync("codex", CancellationToken.None);
            viaLinkedHome.LoggedIn.ShouldBe(true, "a linked or mounted home does not disqualify a regular file in it");
        }
        finally
        {
            DeleteHome(root);
        }
    }

    [Test]
    public async Task Link_attributes_from_the_seam_are_signed_out()
    {
        // The same rule through the metadata seam, so it holds on every host: ReparsePoint (a
        // symlink on Unix; a symlink or junction on Windows) is not a regular file.
        foreach (var attributes in new[]
                 {
                     FileAttributes.ReparsePoint,
                     FileAttributes.ReparsePoint | FileAttributes.Normal,
                     FileAttributes.ReparsePoint | FileAttributes.Directory,
                 })
        {
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = "/state/codex" }, getAttributes: _ => attributes, clock: new FakeTimeProvider(Now));
            var dto = await probe.ProbeAsync("codex", CancellationToken.None);
            dto.LoggedIn.ShouldBe(false, attributes.ToString());
            dto.AuthMethod.ShouldBeNull(attributes.ToString());
            dto.Error.ShouldBeNull(attributes.ToString());
        }

        foreach (var attributes in new[] { FileAttributes.Normal, FileAttributes.ReadOnly, FileAttributes.Archive })
        {
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = "/state/codex" }, getAttributes: _ => attributes, clock: new FakeTimeProvider(Now));
            var dto = await probe.ProbeAsync("codex", CancellationToken.None);
            dto.LoggedIn.ShouldBe(true, attributes + " is a regular file");
        }
    }

    [Test]
    public async Task Missing_directory_is_signed_out()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-auth-probe-absent-" + Guid.NewGuid().ToString("N"));
        Directory.Exists(root).ShouldBeFalse();
        var probe = new CodexAuthProbe(new PhoneHomeSettings { CodexHome = Posix(root) }, clock: new FakeTimeProvider(Now));

        var dto = await probe.ProbeAsync("codex", CancellationToken.None);

        dto.LoggedIn.ShouldBe(false, "a CODEX_HOME that does not exist has no login");
        dto.AuthMethod.ShouldBeNull();
        dto.Error.ShouldBeNull();
        probe.AuthPath.ShouldBe(Posix(root) + "/auth.json");
    }

    [Test]
    public async Task Inaccessible_store_is_unknown()
    {
        foreach (var fault in new Func<Exception>[]
                 {
                     () => new IOException("device error"),
                     () => new UnauthorizedAccessException("denied"),
                 })
        {
            var asked = new List<string>();
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = "/state/codex" },
                getAttributes: path =>
                {
                    asked.Add(path);
                    throw fault();
                },
                clock: new FakeTimeProvider(Now));

            var dto = await probe.ProbeAsync("codex", CancellationToken.None);

            var kind = fault().GetType().Name;
            dto.LoggedIn.ShouldBeNull(kind + " cannot tell, and an unknown answer admits");
            dto.Error.ShouldBe("unreadable", kind);
            dto.AuthMethod.ShouldBeNull(kind);
            asked.ShouldBe(["/state/codex/auth.json"], kind);
        }

        // Not-found faults from the same seam are a definite "signed out", not "cannot tell".
        foreach (var fault in new Func<Exception>[]
                 {
                     () => new FileNotFoundException("gone"),
                     () => new DirectoryNotFoundException("gone"),
                 })
        {
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = "/state/codex" }, getAttributes: _ => throw fault());
            var dto = await probe.ProbeAsync("codex", CancellationToken.None);
            dto.LoggedIn.ShouldBe(false, fault().GetType().Name);
            dto.Error.ShouldBeNull(fault().GetType().Name);
        }
    }

    [Test]
    public async Task Unconfigured_store_is_unknown()
    {
        foreach (var home in new[] { "", "   " })
        {
            var asked = 0;
            var probe = new CodexAuthProbe(
                new PhoneHomeSettings { CodexHome = home },
                getAttributes: _ =>
                {
                    asked++;
                    return FileAttributes.Normal;
                },
                clock: new FakeTimeProvider(Now));

            var dto = await probe.ProbeAsync("codex", CancellationToken.None);

            dto.Provider.ShouldBe("codex");
            dto.LoggedIn.ShouldBeNull($"'{home}' is no store to measure");
            dto.Error.ShouldBe("unconfigured");
            dto.CheckedAtUtc.ShouldBe(Now);
            asked.ShouldBe(0, "an unconfigured home must not be resolved against the process cwd");
        }
    }

    [Test]
    public async Task Other_provider_is_refused()
    {
        var asked = 0;
        var probe = new CodexAuthProbe(
            new PhoneHomeSettings { CodexHome = "/state/codex" },
            getAttributes: _ =>
            {
                asked++;
                return FileAttributes.Normal;
            });

        foreach (var provider in new[] { "grok", "claude", "opencode", "codex-cli", "" })
        {
            var ex = await Should.ThrowAsync<PhoneHomeAdmissionException>(() => probe.ProbeAsync(provider, CancellationToken.None));
            ex.StatusCode.ShouldBe(400, provider);
            ex.Code.ShouldBe(PhoneHomeProblemTypes.UnsupportedTarget, provider);
        }

        asked.ShouldBe(0);

        // The provider name is case-insensitive; the answer always names it canonically.
        var mixed = await probe.ProbeAsync("Codex", CancellationToken.None);
        mixed.Provider.ShouldBe("codex");
        mixed.LoggedIn.ShouldBe(true);
        asked.ShouldBe(1);
    }

    [Test]
    public void Enabled_settings_require_a_posix_codex_home()
    {
        static PhoneHomeSettings Enabled(string codexHome) => new()
        {
            Enabled = true,
            ServerOrigin = "https://antiphon.example",
            CodexHome = codexHome,
        };

        Should.NotThrow(() => Enabled("/state/codex").Validate());
        foreach (var home in new[] { "", " ", "state/codex", "C:\\Users\\me\\.codex", "~/.codex" })
        {
            var ex = Should.Throw<InvalidOperationException>(() => Enabled(home).Validate(), home);
            ex.Message.ShouldContain("PhoneHome:CodexHome", customMessage: home);
        }

        // A disabled runner validates nothing, as for every other PhoneHome setting.
        Should.NotThrow(() => new PhoneHomeSettings { Enabled = false, CodexHome = "" }.Validate());
    }

    private static string NewHome()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-auth-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string Posix(string path) => path.Replace('\\', '/');

    private static bool TryCreateSymlink(string link, string target, bool directory)
    {
        try
        {
            if (directory)
                Directory.CreateSymbolicLink(link, target);
            else
                File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static void DeleteHome(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }
}
