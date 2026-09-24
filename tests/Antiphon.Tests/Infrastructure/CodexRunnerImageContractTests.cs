using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0660 S2 (V-1). The phone-home runner image carries the whole native codex-cli platform
// package, verified by npm's published SHA-512 before tar runs; the state initializer seeds one
// runner-owned Codex home on the runner-state volume without ever replacing an existing file; and
// the runner service names that same home twice (CODEX_HOME for the CLI, PhoneHome__CodexHome
// for the auth probe). These are text contracts over files only a Docker build or a root process
// on server2 ever reads. scripts/verify-card0660-codex-image.ps1 (Q-1/2) executes them.
[Category("Unit")]
public sealed class CodexRunnerImageContractTests
{
    private const string Version = "0.156.1";

    // npm's dist.integrity for @openai/codex@0.156.1-linux-x64, read from the registry during Plan.
    private const string NpmIntegrity =
        "sha512-2ePo0wgOcnONKsuzp8vBjOmNY+IdsKaouaDDIdiKq9HOWNuV/GI22OeXft2A/1GaoL71ictO0/pLAsCEnQ6wew==";

    private const string PackageRoot = "/opt/codex/${CODEX_VERSION}/package";
    private const string NativeBinary = PackageRoot + "/vendor/x86_64-unknown-linux-musl/bin/codex";

    private const string SeededConfig =
        "check_for_update_on_startup = false\n"
        + "cli_auth_credentials_store = \"file\"\n"
        + "forced_login_method = \"chatgpt\"\n"
        + "\n"
        + "[projects.\"/work/repos/antiphon\"]\n"
        + "trust_level = \"trusted\"\n";

    [Test]
    public void Pin_integrity_and_install_are_in_runtime_base()
    {
        var dockerfile = Read("docker/session-runner-grok/Dockerfile");
        var stages = DockerStackDocuments.Stages(dockerfile);
        var body = stages.Single(stage => stage.Name == "runtime-base").Body.Replace("\r\n", "\n");

        // A reviewed pair: the version and the hex form of npm's published integrity.
        body.ShouldContain("ARG CODEX_VERSION=" + Version + "\n");
        var digest = Regex.Match(body, @"ARG CODEX_SHA512=([0-9a-f]{128})\n");
        digest.Success.ShouldBeTrue("the platform tarball is pinned by a 128-hex SHA-512");
        var published = Convert.ToHexStringLower(Convert.FromBase64String(NpmIntegrity["sha512-".Length..]));
        digest.Groups[1].Value.ShouldBe(published, "CODEX_SHA512 is npm's dist.integrity for the pinned tarball");

        var install = Run(body, "codex-${CODEX_VERSION}-linux-x64.tgz");
        install.ShouldContain("https://registry.npmjs.org/@openai/codex/-/codex-${CODEX_VERSION}-linux-x64.tgz");
        install.ShouldContain("| sha512sum -c -");
        Order(install, "sha512sum -c -", "tar ").ShouldBeTrue("the archive is hashed before tar runs");
        install.ShouldContain("tar --no-same-owner -xzf /opt/codex-download/codex.tgz -C /opt/codex/${CODEX_VERSION}");
        install.Contains("--strip-components", StringComparison.Ordinal)
            .ShouldBeFalse("the whole npm package keeps its package/ root and relative layout");

        // The whole platform package, root-owned and not writable by the app uid.
        install.ShouldContain("chown -R 0:0 /opt/codex");
        install.ShouldContain("chmod -R go-w /opt/codex");
        foreach (var helper in new[] { "bin/codex-code-mode-host", "codex-path/rg", "codex-resources/bwrap", "codex-package.json" })
            install.ShouldContain(PackageRoot + "/vendor/x86_64-unknown-linux-musl/" + helper);
        install.ShouldContain("ln -s " + NativeBinary + " /usr/local/bin/codex");

        // Exact version output from an isolated, non-/tmp home that is gone with the scratch tree.
        install.ShouldContain(
            "test \"$(env HOME=/opt/codex-probe CODEX_HOME=/opt/codex-probe/home /usr/local/bin/codex --version)\" = \"codex-cli ${CODEX_VERSION}\"");
        Order(install, "ln -s " + NativeBinary, "/usr/local/bin/codex --version").ShouldBeTrue();
        Order(install, "/usr/local/bin/codex --version", "rm -rf /opt/codex-download /opt/codex-probe")
            .ShouldBeTrue("download and probe scratch are removed in the same layer, last");

        // No npm shim, no unpinned install, no binary-only copy.
        dockerfile.Contains("npm install", StringComparison.Ordinal).ShouldBeFalse();
        dockerfile.Contains("@openai/codex@", StringComparison.Ordinal).ShouldBeFalse("no moving npm tag");
        install.Contains("install -m 0755", StringComparison.Ordinal).ShouldBeFalse("no binary-only copy");

        // No baked home, credential or credential name.
        foreach (var token in new[] { "ENV CODEX_HOME", "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "auth.json", "/state/codex" })
            dockerfile.Contains(token, StringComparison.Ordinal).ShouldBeFalse("the image bakes " + token);

        // Both deployed targets inherit it, and nothing after runtime-base replaces the link.
        foreach (var target in new[] { stages[^1].Name, "session-testing" })
        {
            var closure = DockerStackDocuments.Closure(stages, target).Replace("\r\n", "\n");
            closure.Contains("ln -s " + NativeBinary + " /usr/local/bin/codex", StringComparison.Ordinal)
                .ShouldBeTrue(target + " inherits the Codex install");
        }

        foreach (var stage in stages.Where(stage => stage.Name is "receipt-probe" or "session-testing" or "runtime"))
            stage.Body.Contains("codex", StringComparison.OrdinalIgnoreCase)
                .ShouldBeFalse(stage.Name + " must not re-install or shadow codex");
    }

    [Test]
    public void State_initializer_seeds_runner_mount_without_overwrite()
    {
        var text = Read("docker/stack/init-state.sh").Replace("\r\n", "\n");
        text.All(c => c < 128).ShouldBeTrue("init-state.sh stays ASCII");

        // Created on the runner-state volume as state-init mounts it (/runner-state), which the
        // runner sees as /state. /state/codex does not exist in the state-init container.
        var loop = text[text.IndexOf("for d in \\", StringComparison.Ordinal)..text.IndexOf("\ndo\n", StringComparison.Ordinal)];
        loop.Split([' ', '\n', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .ShouldContain("/runner-state/codex", "the mkdir loop names the Codex home");
        text.ShouldContain("codex_home=/runner-state/codex\n");
        text.Contains("/state/codex", StringComparison.Ordinal).ShouldBeFalse("wrong spelling for the state-init mount");
        text.ShouldContain("chmod 0700 \"$codex_home\"\n");

        // Only when absent: neither a regular file nor a dangling link is ever replaced.
        text.ShouldContain("codex_config=\"$codex_home/config.toml\"\n");
        text.ShouldContain("if [ ! -e \"$codex_config\" ] && [ ! -L \"$codex_config\" ]; then\n");

        // Exact bytes, from a quoted heredoc so nothing expands.
        var start = text.IndexOf("<<'CODEX_CONFIG'\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, "the seeded config is a quoted heredoc");
        start += "<<'CODEX_CONFIG'\n".Length;
        var end = text.IndexOf("\nCODEX_CONFIG\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start);
        (text[start..end] + "\n").ShouldBe(SeededConfig);

        // Written to a scratch name at 0600 as the app uid, then linked in: ln refuses an existing
        // target, so a config that appears meanwhile is never clobbered.
        var seed = text[text.IndexOf("if [ ! -e \"$codex_config\" ]", StringComparison.Ordinal)..];
        seed = seed[..(seed.IndexOf("\nfi\n", StringComparison.Ordinal) + 4)];
        seed.ShouldContain("umask 077\n");
        seed.ShouldContain("chown \"$uid:$gid\" \"$codex_seed\"\n");
        seed.ShouldContain("chmod 0600 \"$codex_seed\"\n");
        seed.ShouldContain("ln \"$codex_seed\" \"$codex_config\"\n");
        seed.ShouldContain("rm -f \"$codex_seed\"\n");
        Order(seed, "chmod 0600 \"$codex_seed\"", "ln \"$codex_seed\" \"$codex_config\"").ShouldBeTrue("never visible at a wider mode");
        seed.Contains("mv ", StringComparison.Ordinal).ShouldBeFalse("mv replaces an existing target");

        // Never touches credentials, sessions or stores; seeded after the owner check.
        foreach (var token in new[] { "auth.json", "sessions", "sqlite", ".db" })
            text.Contains(token, StringComparison.Ordinal).ShouldBeFalse("init-state touches " + token);
        Order(text, "ForeignStateOwner", "codex_home=/runner-state/codex").ShouldBeTrue("a foreign owner refuses before any seed");
        Order(text, "ln \"$codex_seed\" \"$codex_config\"", "chown -R \"$uid:$gid\" /state /work /runner-state").ShouldBeTrue();
    }

    [Test]
    public void Compose_home_and_probe_home_agree()
    {
        var compose = Read("docker-compose.server2-runner.yml");
        var runner = DockerStackDocuments.Service(compose, "session-runner");
        var init = DockerStackDocuments.Service(compose, "state-init");

        DockerStackDocuments.Env(runner, "CODEX_HOME").ShouldBe("/state/codex");
        DockerStackDocuments.Env(runner, "PhoneHome__CodexHome").ShouldBe("/state/codex", "the auth probe reads the CLI's own store");

        // The directory state-init seeds is that same path once the runner mounts the volume.
        var initMount = Mount(init, "runner-state");
        var runnerMount = Mount(runner, "runner-state");
        var seeded = Regex.Match(Read("docker/stack/init-state.sh"), @"codex_home=(\S+)\r?\n");
        seeded.Success.ShouldBeTrue("init-state names the Codex home");
        seeded.Groups[1].Value.StartsWith(initMount + "/", StringComparison.Ordinal).ShouldBeTrue();
        (runnerMount + seeded.Groups[1].Value[initMount.Length..]).ShouldBe(DockerStackDocuments.Env(runner, "CODEX_HOME"));

        // Subscription-only: no credential name is ever configured on the runner.
        foreach (var name in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN" })
            compose.Contains(name, StringComparison.OrdinalIgnoreCase).ShouldBeFalse("compose configures " + name);
    }

    private static string Mount(string service, string volume)
    {
        var entry = DockerStackDocuments.List(service, "volumes")
            .SingleOrDefault(line => line.StartsWith(volume + ":", StringComparison.Ordinal));
        entry.ShouldNotBeNull("the service mounts the " + volume + " volume");
        return entry[(volume.Length + 1)..].Split(':')[0];
    }

    // The RUN instruction (with its continuation lines) that mentions the marker.
    private static string Run(string body, string marker)
    {
        var at = body.IndexOf(marker, StringComparison.Ordinal);
        at.ShouldBeGreaterThan(-1, "runtime-base mentions " + marker);
        var start = body.LastIndexOf("\nRUN ", at, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1);
        var lines = body[(start + 1)..].Split('\n');
        var run = new List<string>();
        foreach (var line in lines)
        {
            run.Add(line);
            if (!line.TrimEnd().EndsWith('\\'))
                break;
        }

        return string.Join("\n", run);
    }

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b >= 0 && a < b;
    }

    private static string Read(string relative) => DockerStackDocuments.Read(relative).Replace("\r\n", "\n");
}
