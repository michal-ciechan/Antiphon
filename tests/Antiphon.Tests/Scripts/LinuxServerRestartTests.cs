using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.LinuxServerRestartWorld;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1745, unit 64 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the restart-linux-server command
/// (Antiphon.Nightly.LinuxServerRestart), which replaced scripts/restart-linux-server.ps1, in process (LinuxServerRestartWorld).
/// The lines, exits, locks, URLs and docker calls are the script's, captured from it before its deletion (163 runs under pwsh with
/// function fakes of Invoke-WebRequest and docker in the session, so nothing reached a network or a daemon). The driver's own
/// cases are LinuxServerRestartGateTests.
/// </summary>
[Category("Unit")]
public sealed class LinuxServerRestartTests
{
    private static readonly string[] Nothing = ["Nothing was restarted."];

    private static string[] Refusal(string message) => ["REFUSED: " + message, "Nothing was restarted."];

    private static void Refused(LinuxServerRestartRun run, string message)
    {
        run.Exit.ShouldBe(3, run.ToString());
        run.Lines.ShouldBe(Refusal(message), run.ToString());
        run.Http.ShouldBeEmpty("a refusal probes nothing");
        run.Docker.ShouldBeEmpty("a refusal calls no docker");
    }

    private static void Verified(LinuxServerRestartRun run, string sha, string mode = "whatif")
    {
        run.Exit.ShouldBe(0, run.ToString());
        run.Lines.ShouldBe(["VERIFIED sha=" + sha + " mode=" + mode], run.ToString());
    }

    // ---- the options ----

    [Test]
    public void Options_take_the_scripts_parameters_in_kebab_case()
    {
        var options = LinuxServerRestartOptions.Parse([]);
        (options.Config, options.ExpectedServerSha, options.Execute, options.WhatIf, options.TimeoutSec, options.LockMaxAgeMinutes)
            .ShouldBe(("", "", false, false, 20, 15));
        options = LinuxServerRestartOptions.Parse(["--config", "c", "--expected-server-sha", "s", "--execute", "--what-if", "--timeout-sec", "0", "--lock-max-age-minutes", "-3"]);
        (options.Config, options.ExpectedServerSha, options.Execute, options.WhatIf, options.TimeoutSec, options.LockMaxAgeMinutes)
            .ShouldBe(("c", "s", true, true, 0, -3));
        // The script's plain param block ignored an unknown -Bogus; a parameter it could not convert ("x" for -TimeoutSec) was a
        // binding error. The CLI's usage error is exit 2 for both.
        Should.Throw<NightlyUsageException>(() => LinuxServerRestartOptions.Parse(["--bogus"])).Message.ShouldBe("unknown flag --bogus");
        Should.Throw<NightlyUsageException>(() => LinuxServerRestartOptions.Parse(["-Execute"])).Message.ShouldBe("unexpected argument '-Execute'");
        Should.Throw<NightlyUsageException>(() => LinuxServerRestartOptions.Parse(["--timeout-sec", "x"])).Message.ShouldBe("--timeout-sec expects an integer, not 'x'");
        Should.Throw<NightlyUsageException>(() => LinuxServerRestartOptions.Parse(["--config"])).Message.ShouldBe("missing value for --config");
        LinuxServerRestartOptions.Usage.ShouldBe("restart-linux-server [--config <file>] [--expected-server-sha <sha>] [--execute | --what-if]"
            + " [--timeout-sec <n>] [--lock-max-age-minutes <n>]");
        File.Exists(Path.Combine(Antiphon.Tests.Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "restart-linux-server.ps1")).ShouldBeFalse();
    }

    // ---- the refusals, in the script's order ----

    [Test]
    public void Config_refusals_are_the_scripts()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve(Version(Sha40));
        // r-env-config: the variable names the config when --config is blank.
        world.Config();
        world.Variables[LinuxServerRestart.ConfigVariable] = world.ConfigPath;
        Verified(world.Run("--expected-server-sha", Sha40), Sha40);
        world.Variables[LinuxServerRestart.ConfigVariable] = "   ";
        Refused(world.Run("--expected-server-sha", Sha40), "Linux server is not configured.");
        world.Variables.Remove(LinuxServerRestart.ConfigVariable);
        // r-both-unconfigured, r-both: the config comes first.
        Refused(world.Run("--execute", "--what-if", "--expected-server-sha", Sha40), "Linux server is not configured.");
        Refused(world.Run("--config", world.ConfigPath, "--execute", "--what-if", "--expected-server-sha", Sha40), "-Execute and -WhatIf were both set.");

        const string missing = "Linux server config is missing baseUrl, container, or lockPath.";
        Refused(world.Run("--config", Path.Combine(Base, "absent.cfg"), "--expected-server-sha", Sha40), missing);
        foreach (var lines in new[]
        {
            Array.Empty<string>(),
            ["baseUrl=" + BaseUrl, "container=antiphon-staging", "lockPath=" + world.Lock, "junk"],
            ["baseUrl=" + BaseUrl, "container=antiphon-staging", "lockPath=" + world.Lock, "=x"],
            ["baseUrl=" + BaseUrl, "container=antiphon-staging"],
        })
            Refused(world.Run("--config", world.ConfigLines(lines), "--expected-server-sha", Sha40), missing);

        // r-config-comments, r-config-keycase, r-config-dup: comments and blanks skipped, keys and values trimmed, keys ignore
        // case, a later key wins.
        foreach (var lines in new[]
        {
            ["# a comment", "", "   ", "  baseUrl = " + BaseUrl + "  ", "   # indented comment", "container= antiphon-staging", "lockPath =" + world.Lock],
            ["BASEURL=" + BaseUrl, "Container=antiphon-staging", "LOCKPATH=" + world.Lock],
            new[] { "baseUrl=http://127.0.0.1:1/", "baseUrl=" + BaseUrl, "container=antiphon-staging", "lockPath=" + world.Lock },
        })
            Verified(world.Run("--config", world.ConfigLines(lines), "--expected-server-sha", Sha40), Sha40);
        // r-config-empty-values: present but empty.
        Refused(world.Run("--config", world.ConfigLines("baseUrl=", "container=", "lockPath="), "--expected-server-sha", Sha40),
            "baseUrl must be an http URL without user info.");
    }

    [Test]
    public void Config_directory_is_a_terminating_error()
    {
        // r-config-dir: Get-Content of a directory stopped the script (pwsh -File exit 1).
        var world = new LinuxServerRestartWorld();
        var dir = Path.Combine(Base, "cfgdir");
        world.Files.AddDirectory(dir);
        var run = world.Run("--config", dir, "--expected-server-sha", Sha40);
        run.Exit.ShouldBe(1, run.ToString());
        run.Lines.ShouldBe([""]);
        run.Error.Trim().ShouldBe("Unable to get content because it is a directory: '" + dir + "'. Please use 'Get-ChildItem' instead.");
    }

    [Test]
    public void Sha_refusals_are_the_scripts()
    {
        const string message = "-ExpectedServerSha is not a full 40- or 64-character SHA.";
        foreach (var sha in new[] { "", "abc", new string('a', 39), new string('a', 41), new string('a', 39) + "g", " " + new string('a', 40), new string('a', 63), new string('a', 65) })
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(Version(Sha40));
            var arguments = sha.Length == 0 ? new[] { "--config", world.Config(), "--execute" } : ["--config", world.Config(), "--execute", "--expected-server-sha", sha];
            Refused(world.Run(arguments), message);
        }

        // r-sha-64-AA: an upper-case SHA is lowered.
        var upper = new LinuxServerRestartWorld();
        upper.Serve(Version(Sha64));
        var run = upper.Run("--config", upper.Config(), "--execute", "--expected-server-sha", Sha64.ToUpperInvariant());
        run.Lines.ShouldBe(["already at expected SHA", "VERIFIED sha=" + Sha64 + " mode=execute"]);

        // r-sha-41 with a final newline: $ matched before it, so the SHA passes and never equals the server's.
        var newline = new LinuxServerRestartWorld();
        newline.Serve(Version(new string('a', 40)));
        var shaNewline = new string('a', 40) + "\n";
        run = newline.Run("--config", newline.Config(), "--execute", "--expected-server-sha", shaNewline);
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.Take(3).ShouldBe(["REFUSED: server build unverified (exit 5).", "  expected SHA: " + new string('a', 40), ""]);
        run.Lines[3].ShouldBe("  observed SHA: " + new string('a', 40));
        newline.LockText.ShouldBe(newline.ExpectedLock("mismatch", shaNewline, new string('a', 40), "execute", "sha256:c1745previous"));
    }

    [Test]
    public void Url_refusals_and_acceptances_are_the_scripts()
    {
        const string http = "baseUrl must be an http URL without user info.";
        const string loopback = "baseUrl must use the host loopback.";
        const string port = "baseUrl must name an explicit port.";
        foreach (var (url, message) in new[]
        {
            ("https://127.0.0.1:17999/", http), ("http://user:pw@127.0.0.1:17999/", http), ("http://user@127.0.0.1:17999/", http), ("http://10.0.0.1:17999/", loopback),
            ("http://127.0.0.1/", port), ("http://127.0.0.1:80/", port), ("http://127.0.0.1:17200/", "baseUrl port 17200 is a desktop AppHost port."),
            ("http://127.0.0.1:17209/", "baseUrl port 17209 is a desktop AppHost port."), ("not a url", http), ("http://[::1]:17999/", loopback), ("127.0.0.1:17999", http),
            ("http://127.0.0.1:0/", port), ("http://127.0.0.2:17999/", loopback), ("ftp://127.0.0.1:17999/", http), ("http://127.0.0.1:65536/", http),
        })
        {
            var world = new LinuxServerRestartWorld();
            Refused(world.Run("--config", world.Config(url), "--expected-server-sha", Sha40), message);
        }

        // Accepted: the host and scheme ignore case, a shorthand loopback normalises, the root's trailing slashes are trimmed.
        foreach (var (url, root) in new[]
        {
            ("http://LOCALHOST:17999/", "http://LOCALHOST:17999"), ("http://localhost:17999", "http://localhost:17999"), ("http://127.0.0.1:17210/", "http://127.0.0.1:17210"),
            ("http://127.0.0.1:17199/", "http://127.0.0.1:17199"), ("HTTP://127.0.0.1:17999/", "HTTP://127.0.0.1:17999"), ("http://127.0.0.1:17999//", "http://127.0.0.1:17999"),
            ("http://0x7f.0.0.1:17999/", "http://0x7f.0.0.1:17999"), ("http://127.1:17999/", "http://127.1:17999"), ("http://127.0.0.1:65535/", "http://127.0.0.1:65535"),
        })
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(Version(Sha40));
            var run = world.Run("--config", world.Config(url), "--expected-server-sha", Sha40);
            Verified(run, Sha40);
            run.Http.ShouldBe([root + "/health", root + "/api/version"]);
        }

        // A path or query in the base is kept: /base/health and /?a=b/health are not the server's routes.
        foreach (var (url, asked) in new[] { ("http://127.0.0.1:17999/base/", "http://127.0.0.1:17999/base/health"), ("http://127.0.0.1:17999/?a=b", "http://127.0.0.1:17999/?a=b/health") })
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(Version(Sha40));
            world.Answers.Remove("/health");
            var run = world.Run("--config", world.Config(url), "--expected-server-sha", Sha40);
            run.Exit.ShouldBe(1, run.ToString());
            run.Lines.ShouldBe(Refusal("/health was not 200."));
            run.Http.ShouldBe([asked]);
        }
    }

    [Test]
    public void Container_refusals_and_acceptances_are_the_scripts()
    {
        foreach (var container in new[] { "", "session-runner", "my-runner", "antiphon-postgres", "Gateway-x", "phone", "fake-gateway", "antiphon-runner", new string('a', 65), "-x",
                     "x y", "_x", "x/y", "RUNNER2", "iPhone" })
        {
            var world = new LinuxServerRestartWorld();
            Refused(world.Run("--config", world.Config(container: container), "--expected-server-sha", Sha40), "container " + container + " is outside this gate.");
        }
        foreach (var container in new[] { new string('a', 64), "ANTIPHON-STAGING", "a.b_c-d" })
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(Version(Sha40));
            Verified(world.Run("--config", world.Config(container: container), "--expected-server-sha", Sha40), Sha40);
        }
        // -notmatch's $ also matched before a final newline (a config value is trimmed, so it never carries one).
        foreach (var name in new[] { "antiphon-staging", "antiphon-server2-server-server-1", "a", "A9._-", "a\n" })
            LinuxServerRestart.ContainerAllowed(name).ShouldBeTrue(name);
        foreach (var name in new[] { "   ", "postgres", "x-POSTGRES", "GATEWAY", "phone-home", "a\n\n", "a b" })
            LinuxServerRestart.ContainerAllowed(name).ShouldBeFalse(name);
    }

    [Test]
    public void Lock_path_and_order_refusals_are_the_scripts()
    {
        var world = new LinuxServerRestartWorld();
        foreach (var lockPath in new[] { "restart.lock", Path.Combine(Base, "LOCKDIR", "..", "restart.lock"), Path.Combine(Base, "LOCKDIR", "a..b.lock") })
            Refused(world.Run("--config", world.ConfigLines("baseUrl=" + BaseUrl, "container=antiphon-staging", "lockPath=" + lockPath), "--expected-server-sha", Sha40),
                "lockPath must be a rooted path.");
        // r-lock-slash: a slash-rooted path is rooted on Windows too; the probe then runs (here no server answers).
        var run = world.Run("--config", world.ConfigLines("baseUrl=" + BaseUrl, "container=antiphon-staging", "lockPath=/u64-nowhere/restart.lock"), "--expected-server-sha", Sha40);
        run.Exit.ShouldBe(1, run.ToString());
        run.Lines.ShouldBe(Refusal("/health was not 200."));

        // r-order-*: the SHA before the URL, the URL before the container, the container before the lock path.
        var bad = world.ConfigLines("baseUrl=https://10.0.0.1/", "container=session-runner", "lockPath=x");
        Refused(world.Run("--config", bad, "--expected-server-sha", "abc"), "-ExpectedServerSha is not a full 40- or 64-character SHA.");
        Refused(world.Run("--config", bad, "--expected-server-sha", Sha40), "baseUrl must be an http URL without user info.");
        Refused(world.Run("--config", world.ConfigLines("baseUrl=" + BaseUrl, "container=session-runner", "lockPath=x"), "--expected-server-sha", Sha40),
            "container session-runner is outside this gate.");
    }

    // ---- the lock ----

    [Test]
    public void Lock_freshness_is_the_scripts()
    {
        foreach (var (minutes, extra, fresh) in new[]
        {
            (0d, Array.Empty<string>(), true), (14d, Array.Empty<string>(), true), (16d, Array.Empty<string>(), false), (20d, new[] { "--lock-max-age-minutes", "30" }, true),
            (2d, new[] { "--lock-max-age-minutes", "0" }, false), (0d, new[] { "--lock-max-age-minutes", "0" }, true), (-60d, Array.Empty<string>(), true),
        })
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(Version(Sha40));
            world.OldLock(minutes);
            var run = world.Run(["--config", world.Config(), "--expected-server-sha", Sha40, .. extra]);
            if (fresh)
            {
                Refused(run, "a restart lock is held at " + world.Lock + ".");
                world.LockText.ShouldBe("reason=old\n");
            }
            else
            {
                // l-stale-verified-removes: a stale lock goes with VERIFIED.
                Verified(run, Sha40);
                world.LockText.ShouldBeNull();
            }
        }

        // l-stale-mismatch-rewrites and l-stale-health-keeps.
        var mismatch = new LinuxServerRestartWorld();
        mismatch.Serve(Version(Other));
        mismatch.OldLock(16);
        mismatch.Run("--config", mismatch.Config(), "--expected-server-sha", Sha40).Exit.ShouldBe(5);
        mismatch.LockText.ShouldBe(mismatch.ExpectedLock("mismatch", Sha40, Other, "whatif"));
        var down = new LinuxServerRestartWorld();
        down.Answers["/health"] = new LinuxServerHttpAnswer(500, "x");
        down.OldLock(16);
        down.Run("--config", down.Config(), "--expected-server-sha", Sha40).Exit.ShouldBe(1);
        down.LockText.ShouldBe("reason=old\n");
        // l-timeout-0: the bound is accepted and clamped; the probe is one request either way.
        var clamped = new LinuxServerRestartWorld();
        clamped.Serve(Version(Sha40));
        Verified(clamped.Run("--config", clamped.Config(), "--expected-server-sha", Sha40, "--timeout-sec", "0"), Sha40);
    }

    [Test]
    public void Lock_directory_and_parent_are_the_scripts()
    {
        // l-dir-fresh: a directory's write time counts.
        var fresh = new LinuxServerRestartWorld();
        fresh.Serve(Version(Sha40));
        fresh.OldLock(0, directory: true);
        Refused(fresh.Run("--config", fresh.Config(), "--expected-server-sha", Sha40), "a restart lock is held at " + fresh.Lock + ".");

        // l-dir-stale-verified and l-dir-stale-mismatch: Remove-Item asked about the entries and Set-Content refused a directory;
        // both stopped the script (exit 1) and the directory stayed.
        var verified = new LinuxServerRestartWorld();
        verified.Serve(Version(Sha40));
        verified.OldLock(20, directory: true);
        var run = verified.Run("--config", verified.Config(), "--expected-server-sha", Sha40);
        run.Exit.ShouldBe(1, run.ToString());
        run.Error.Trim().ShouldBe("The item at " + verified.Lock + " has children and the Recurse parameter was not specified.");
        verified.Files.Directory.Exists(verified.Lock).ShouldBeTrue();
        var mismatch = new LinuxServerRestartWorld();
        mismatch.Serve(Version(Other));
        mismatch.OldLock(20, directory: true);
        run = mismatch.Run("--config", mismatch.Config(), "--expected-server-sha", Sha40);
        run.Exit.ShouldBe(1, run.ToString());
        run.Lines.ShouldBe([""]);
        run.Error.Trim().ShouldBe("Unable to clear content of '" + mismatch.Lock + "' because it is a directory. Clear-Content is only supported on files.");

        // An empty directory is removed with VERIFIED.
        var empty = new LinuxServerRestartWorld();
        empty.Serve(Version(Sha40));
        empty.Files.AddDirectory(empty.Lock);
        empty.Files.Directory.SetLastWriteTimeUtc(empty.Lock, empty.Clock.GetUtcNow().UtcDateTime.AddMinutes(-20));
        Verified(empty.Run("--config", empty.Config(), "--expected-server-sha", Sha40), Sha40);
        empty.Files.Directory.Exists(empty.Lock).ShouldBeFalse();

        // l-nested-parent: the lock's parent directories are created.
        var nested = new LinuxServerRestartWorld();
        nested.Serve(Version(Other));
        var deep = Path.Combine(Base, "deep", "er", "restart.lock");
        run = nested.Run("--config", nested.ConfigLines("baseUrl=" + BaseUrl, "container=antiphon-staging", "lockPath=" + deep), "--expected-server-sha", Sha40);
        run.Exit.ShouldBe(5, run.ToString());
        run.Lines.ShouldContain("  lock: " + deep);
        nested.Files.File.ReadAllText(deep).ShouldStartWith("reason=mismatch" + Environment.NewLine);
    }

    // ---- the probe ----

    [Test]
    public void Probe_answers_are_the_scripts()
    {
        void Case(Action<LinuxServerRestartWorld> serve, string sha, int exit, string[] lines, string[] http, Func<LinuxServerRestartWorld, string?> lockText)
        {
            var world = new LinuxServerRestartWorld();
            serve(world);
            var run = world.Run("--config", world.Config(), "--expected-server-sha", sha);
            run.Exit.ShouldBe(exit, run.ToString());
            run.Lines.ShouldBe(lines, run.ToString());
            run.Http.ShouldBe(http);
            world.LockText.ShouldBe(lockText(world));
        }

        var health = new[] { "http://127.0.0.1:17999/health" };
        var both = new[] { "http://127.0.0.1:17999/health", "http://127.0.0.1:17999/api/version" };
        // p-health-down (no response, status 0) and p-health-302 (no redirect followed).
        Case(_ => { }, Sha40, 1, Refusal("/health was not 200."), health, _ => null);
        Case(w => { w.Serve(Version(Sha40)); w.Answers["/health"] = new LinuxServerHttpAnswer(302, ""); }, Sha40, 1, Refusal("/health was not 200."), health, _ => null);
        // p-version-down and p-version-404.
        string Lock(LinuxServerRestartWorld w, string reason, string expected, string observed) => w.ExpectedLock(reason, expected, observed, "whatif");
        string[] Lines(LinuxServerRestartWorld w, string expected, string observed, string reason) =>
            ["REFUSED: server build unverified (exit 5).", "  expected SHA: " + expected, "  observed SHA: " + observed, "  reason: " + reason, "  lock: " + w.Lock, .. Nothing];
        var probe = new LinuxServerRestartWorld();
        Case(w => w.Answers["/health"] = new LinuxServerHttpAnswer(200, "Healthy"), Sha40, 5, Lines(probe, Sha40, "", "unavailable"), both, w => Lock(w, "unavailable", Sha40, ""));
        Case(w => w.Serve(Version(Sha40), versionCode: 404), Sha40, 5, Lines(probe, Sha40, "", "unavailable"), both, w => Lock(w, "unavailable", Sha40, ""));
        // p-64-match, p-upper-expected, p-64-vs-40.
        Case(w => w.Serve(Version(Sha64)), Sha64, 0, ["VERIFIED sha=" + Sha64 + " mode=whatif"], both, _ => null);
        Case(w => w.Serve(Version(Sha40)), Sha40.ToUpperInvariant(), 0, ["VERIFIED sha=" + Sha40 + " mode=whatif"], both, _ => null);
        Case(w => w.Serve(Version(Sha40)), Sha64, 5, Lines(probe, Sha64, Sha40, "mismatch"), both, w => Lock(w, "mismatch", Sha64, Sha40));
    }

    /// <summary>The version bodies the script was given and what it made of them: ok, or the reason and the observed text.</summary>
    private static readonly (string Name, string Body, string Reason, string Observed)[] Bodies =
    [
        ("blank", "   ", "malformed", ""),
        ("not-json", "not-json", "malformed", ""),
        ("empty-object", "{}", "malformed", ""),
        ("no-version", "{\"sha\":\"" + Sha40 + "\"}", "malformed", ""),
        ("version-key-case", "{\"Version\":\"" + Sha40 + "\"}", "ok", Sha40),
        ("version-both-case", "{\"version\":\"" + Sha40 + "\",\"VERSION\":\"" + Other + "\"}", "malformed", ""),
        ("nested-key-case", "{\"version\":\"" + Sha40 + "\",\"x\":{\"a\":1,\"A\":2}}", "malformed", ""),
        ("empty-key", "{\"\":1,\"version\":\"" + Sha40 + "\"}", "malformed", ""),
        ("version-dup", "{\"version\":\"" + Other + "\",\"version\":\"" + Sha40 + "\"}", "ok", Sha40),
        ("version-short", "{\"version\":\"abc123\"}", "malformed", "abc123"),
        ("version-number", "{\"version\":123}", "malformed", "123"),
        ("version-real", "{\"version\":1.50}", "malformed", "1.5"),
        ("version-big-real", "{\"version\":1e20}", "malformed", "1E+20"),
        ("version-negative-zero", "{\"version\":-0}", "malformed", "0"),
        ("version-bool", "{\"version\":true}", "malformed", "True"),
        ("version-null", "{\"version\":null}", "malformed", ""),
        ("version-array", "{\"version\":[\"" + Sha40 + "\"]}", "ok", Sha40),
        ("version-array2", "{\"version\":[\"a\",\"b\"]}", "malformed", "a b"),
        ("version-array-mixed", "{\"version\":[1,null,\"x\",{\"a\":1},[2,3]]}", "malformed", "1  x  System.Object[]"),
        ("version-object", "{\"version\":{\"a\":1,\"b\":\"x\"}}", "malformed", "@{a=1; b=x}"),
        ("version-object-nested", "{\"version\":{\"a\":[1,2],\"b\":{\"c\":null},\"d\":1.0,\"e\":1e3,\"f\":true}}", "malformed", "@{a=System.Object[]; b=; d=1; e=1000; f=True}"),
        ("version-bignum", "{\"version\":1234567890123456789012345678901234567890}", "mismatch", "1234567890123456789012345678901234567890"),
        ("version-spaced", "{\"version\":\" " + Sha40 + "\"}", "malformed", " " + Sha40),
        ("version-newline", "{\"version\":\"" + Sha40 + "\\n\"}", "mismatch", Sha40 + "\n"),
        ("array-one", "[{\"version\":\"" + Sha40 + "\"}]", "ok", Sha40),
        ("array-two", "[{\"version\":\"" + Sha40 + "\"},{\"version\":\"" + Sha40 + "\"}]", "malformed", ""),
        ("array-nested", "[[{\"version\":\"" + Sha40 + "\"}]]", "malformed", ""),
        ("json-string", "\"" + Sha40 + "\"", "malformed", ""),
        ("json-number", "42", "malformed", ""),
        ("trailing-text", "{\"version\":\"" + Sha40 + "\"} x", "malformed", ""),
        ("two-documents", "{\"version\":\"" + Sha40 + "\"}{}", "malformed", ""),
        ("comment", "{/* c */\"version\":\"" + Sha40 + "\"}", "ok", Sha40),
        ("line-comment", "{\"version\":\"" + Sha40 + "\"} // c", "ok", Sha40),
        ("trailing-comma", "{\"version\":\"" + Sha40 + "\",}", "ok", Sha40),
        ("escaped", "{\"version\":\"\\u0062" + Sha40.Substring(1) + "\"}", "ok", Sha40),
        ("mismatch-upper", "{\"version\":\"" + Other.ToUpperInvariant() + "\"}", "mismatch", Other),
    ];

    [Test]
    public void Version_body_answers_are_the_scripts()
    {
        foreach (var (name, body, reason, observed) in Bodies)
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(body);
            var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40);
            if (reason == "ok")
            {
                Verified(run, observed);
                continue;
            }
            run.Exit.ShouldBe(5, name + "\n" + run);
            run.Out.ShouldContain("  observed SHA: " + observed + "\n  reason: " + reason + "\n", Case.Sensitive, name);
            world.LockText.ShouldBe(world.ExpectedLock(reason, Sha40, observed, "whatif"), name);
        }

        // b-json-null: indexing a null object stopped the script (exit 1, no lock); [] and [null] are the same null.
        foreach (var body in new[] { "null", "[]", "[null]" })
        {
            var world = new LinuxServerRestartWorld();
            world.Serve(body);
            var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40);
            run.Exit.ShouldBe(1, body + "\n" + run);
            run.Error.Trim().ShouldBe("Cannot index into a null array.");
            world.LockText.ShouldBeNull();
        }

        // b-execute-version-number: the same reading under --execute.
        var execute = new LinuxServerRestartWorld();
        execute.Serve("{\"version\":123}");
        var executed = execute.Run("--config", execute.Config(), "--expected-server-sha", Sha40, "--execute");
        executed.Exit.ShouldBe(5);
        executed.Lines[2].ShouldBe("  observed SHA: 123");
        executed.Lines[^1].ShouldBe("recovery: docker start antiphon-staging");
        executed.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Nonstandard_json_reads_as_malformed()
    {
        // Behaviour change (unit 64): ConvertFrom-Json's reader also took single quotes, bare property names, NaN and hex numbers
        // (b-single-quotes and b-unquoted-key verified); the command reads standard JSON (comments and trailing commas allowed), so
        // those bodies are malformed. The server's /api/version is System.Text.Json output. A date-like version string, which the
        // script printed as a date (b-date-version: 11/26/2026 03:00:00), is printed as written; either way it is malformed.
        foreach (var body in new[] { "{'version':'" + Sha40 + "'}", "{version:\"" + Sha40 + "\"}", "{\"version\":NaN}", "{\"version\":0x10}" })
            LinuxServerRestart.Identity(body).ShouldBe(new LinuxServerIdentity("malformed", ""), body);
        LinuxServerRestart.Identity("{\"version\":\"2026-11-26T03:00:00Z\"}").ShouldBe(new LinuxServerIdentity("malformed", "2026-11-26T03:00:00Z"));
    }

    [Test]
    public void Identity_and_sha_answers_are_the_scripts()
    {
        LinuxServerRestart.Identity("{\"version\":\"" + Sha40.ToUpperInvariant() + "\"}").ShouldBe(new LinuxServerIdentity("ok", Sha40));
        LinuxServerRestart.Identity("").ShouldBe(new LinuxServerIdentity("malformed", ""));
        LinuxServerRestart.Identity("{\"version\":\"xyz\"}").ShouldBe(new LinuxServerIdentity("malformed", "xyz"));
        foreach (var sha in new[] { Sha40, Sha64, Sha40.ToUpperInvariant(), Sha40 + "\n" })
            LinuxServerRestart.IsSha(sha).ShouldBeTrue(sha);
        foreach (var sha in new[] { "", "  ", Sha40 + " ", " " + Sha40, Sha40 + "\n\n", Sha40.Substring(1) + "g", Sha40 + Sha40 })
            LinuxServerRestart.IsSha(sha).ShouldBeFalse(sha);
        LinuxServerRestart.ParseConfig(["a=1", "b"]).ShouldBeNull();
        LinuxServerRestart.ParseConfig(["# x", " A = 1 = 2 ", "a=3"])!["a"].ShouldBe("3");
        LinuxServerRestart.ParseConfig(["k= v "])!["K"].ShouldBe("v");
    }

    [Test]
    public void Body_follows_invoke_webrequests_content_type_rule()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"version\":\"x\"}");
        foreach (var type in new[] { "application/json; charset=utf-8", "text/plain", "TEXT/HTML", "application/problem+json", "application/xml", "application/javascript" })
            LinuxServerRestart.Body(type, bytes).ShouldBe("{\"version\":\"x\"}", type);
        // Not text: Invoke-WebRequest's Content was the byte array, whose [string] is its values joined by spaces.
        foreach (var type in new[] { "", "application/octet-stream", "image/png" })
            LinuxServerRestart.Body(type, [123, 34, 10]).ShouldBe("123 34 10", type);
        LinuxServerRestart.Body("text/plain; charset=iso-8859-1", [0xE9]).ShouldBe("\u00e9");
    }

    // ---- --execute and docker ----

    [Test]
    public void Execute_docker_answers_are_the_scripts()
    {
        LinuxServerRestartRun Case(LinuxServerRestartWorld world, string observedBody, string reason, string previousImage, string[] docker, string? tag = null)
        {
            world.Serve(observedBody);
            var run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
            run.Exit.ShouldBe(5, run.ToString());
            run.Out.ShouldContain("  reason: " + reason + "\n");
            if (tag != null) run.Out.ShouldContain("  tag image: " + tag + "\n");
            else run.Out.ShouldNotContain("tag image");
            run.Docker.ShouldBe(docker, run.ToString());
            world.LockText.ShouldBe(world.ExpectedLock(reason, Sha40, Other, "execute", previousImage));
            run.Lines[^1].ShouldBe("recovery: docker start antiphon-staging");
            return run;
        }

        var body = "{\"version\":\"" + Other + "\"}";
        string[] two = ["inspect --format {{.Image}} antiphon-staging", "inspect --format {{.Config.Image}} antiphon-staging"];
        string[] three = [.. two, "image inspect --format {{.Id}} antiphon-server:current"];
        Case(new LinuxServerRestartWorld { Image = (1, ["Error: No such object"]) }, body, "mismatch", "", two);
        Case(new LinuxServerRestartWorld { Ref = (1, []) }, body, "mismatch", "sha256:c1745previous", two);
        Case(new LinuxServerRestartWorld { Image = (0, []) }, body, "mismatch", "", two);
        // Lines are trimmed and the first one counts.
        Case(new LinuxServerRestartWorld { Image = (0, ["  sha256:first  ", "sha256:second"]), Ref = (0, [" antiphon-server:current ", "more"]), TagId = (0, ["  sha256:moved  ", "x"]) },
            body, "image-moved", "sha256:first", three, "sha256:moved");
        Case(new LinuxServerRestartWorld { Ref = (0, ["bad ref"]) }, body, "mismatch", "sha256:c1745previous", two);
        Case(new LinuxServerRestartWorld { Ref = (0, []) }, body, "mismatch", "sha256:c1745previous", two);
        // A digest reference does not match the tag pattern ('@').
        Case(new LinuxServerRestartWorld { Ref = (0, ["reg.example:5000/antiphon/server@sha256:abc"]) }, body, "mismatch", "sha256:c1745previous", two);
        Case(new LinuxServerRestartWorld { TagId = (1, ["sha256:c1745moved"]) }, body, "mismatch", "sha256:c1745previous", three);
        Case(new LinuxServerRestartWorld { TagId = (0, []) }, body, "mismatch", "sha256:c1745previous", three);
        // -eq ignores case: the same id in another case is not a moved tag.
        Case(new LinuxServerRestartWorld { Image = (0, ["sha256:ABC"]), TagId = (0, ["sha256:abc"]) }, body, "mismatch", "sha256:ABC", three);

    }


    [Test]
    public void Docker_override_answers_are_the_scripts()
    {
        var body = "{\"version\":\"" + Other + "\"}";
        // x-docker-env-missing: a path that is absent: no docker, mismatch with no previous image.
        var missing = new LinuxServerRestartWorld();
        missing.Serve(body);
        missing.Variables[LinuxServerRestart.DockerVariable] = Path.Combine(Base, "nowhere", "docker.exe");
        var run = missing.Run("--config", missing.Config(), "--expected-server-sha", Sha40, "--execute");
        run.Exit.ShouldBe(5);
        run.Docker.ShouldBeEmpty();
        missing.LockText.ShouldBe(missing.ExpectedLock("mismatch", Sha40, Other, "execute"));

        // x-docker-env-blank and x-docker-env-docker: docker on PATH (the name ignores case).
        foreach (var value in new[] { "  ", "docker", "DOCKER" })
        {
            var world = new LinuxServerRestartWorld { TagId = (0, ["sha256:c1745moved"]) };
            world.Serve(body);
            world.Variables[LinuxServerRestart.DockerVariable] = value;
            world.Tools["DOCKER"] = world.DockerPath;
            run = world.Run("--config", world.Config(), "--expected-server-sha", Sha40, "--execute");
            run.Out.ShouldContain("  reason: image-moved\n", Case.Sensitive, value);
            run.DockerFiles.ShouldBe([world.DockerPath]);
        }

        // A path that exists is run as it is.
        var file = new LinuxServerRestartWorld { TagId = (0, ["sha256:c1745moved"]) };
        file.Serve(body);
        var fake = Path.Combine(Base, "my-docker");
        file.Files.AddFile(fake, new System.IO.Abstractions.TestingHelpers.MockFileData(""));
        file.Variables[LinuxServerRestart.DockerVariable] = fake;
        run = file.Run("--config", file.Config(), "--expected-server-sha", Sha40, "--execute");
        run.DockerFiles.ShouldBe([fake]);
        run.Out.ShouldContain("  reason: image-moved\n");

        // No docker on PATH: both inspects fail to start (code 1), so the running image is unknown.
        var nameless = new LinuxServerRestartWorld();
        nameless.Serve(body);
        var config = nameless.Config();
        var output = new StringWriter();
        var error = new StringWriter();
        var calls = new List<string>();
        var seams = nameless.Seams(calls);
        var exit = LinuxServerRestart.Run(LinuxServerRestartOptions.Parse(["--config", config, "--expected-server-sha", Sha40, "--execute"]), nameless.Host(output, error),
            nameless.Files, new LinuxServerRestartSeams { Http = seams.Http, Resolve = _ => null });
        exit.ShouldBe(5);
        nameless.Launcher.Launches.ShouldBeEmpty();
        nameless.LockText.ShouldBe(nameless.ExpectedLock("mismatch", Sha40, Other, "execute"));

        // x-whatif-moved-image-not-asked: --what-if never calls docker.
        var whatIf = new LinuxServerRestartWorld { TagId = (0, ["sha256:c1745moved"]) };
        whatIf.Serve(body);
        run = whatIf.Run("--config", whatIf.Config(), "--expected-server-sha", Sha40, "--what-if");
        run.Out.ShouldContain("  reason: mismatch\n");
        run.Docker.ShouldBeEmpty();
    }

    [Test]
    public void Docker_stderr_reaches_the_console()
    {
        var world = new LinuxServerRestartWorld();
        world.Serve("{\"version\":\"" + Other + "\"}");
        var output = new StringWriter();
        var error = new StringWriter();
        var calls = new List<string>();
        var launcher = new RecordingLauncher(_ => new NightlyLaunchResult
        {
            ExitCode = 1,
            StandardOutputText = "",
            StandardErrorText = "Error: No such object: antiphon-staging\n",
        });
        var host = world.Host(output, error);
        host = new NightlyHost
        {
            Clock = host.Clock, Git = host.Git, Launcher = launcher, Processes = host.Processes, Probe = host.Probe, Out = output, Error = error, Trace = host.Trace,
            Variables = host.Variables,
        };
        var seams = world.Seams(calls);
        var exit = LinuxServerRestart.Run(LinuxServerRestartOptions.Parse(["--config", world.Config(), "--expected-server-sha", Sha40, "--execute"]), host, world.Files,
            new LinuxServerRestartSeams { Http = seams.Http, Resolve = _ => world.DockerPath });
        exit.ShouldBe(5);
        error.ToString().Replace("\r\n", "\n").ShouldBe("Error: No such object: antiphon-staging\nError: No such object: antiphon-staging\n");
    }
}
