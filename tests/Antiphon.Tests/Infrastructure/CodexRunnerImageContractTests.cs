using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

// CARD-0660 S2 (V-1). The phone-home runner image carries the whole native codex-cli platform
// package, verified by npm's published SHA-512 before tar runs; the state initializer seeds one
// runner-owned Codex home without ever replacing an existing file; and the runner service names
// that same home twice (CODEX_HOME for the CLI, PhoneHome__CodexHome for the auth probe). These
// are text contracts over files only a Docker build or a root process on server2 ever reads.
// scripts/verify-card0660-codex-image.ps1 (Q-1/2) executes them.
//
// Amended by operator decision ("put the codex auth file somewhere in server2 and mount it in"):
// the home is a DIRECTORY on the server2 host beside the other identity files, bind-mounted
// read-write into state-init (which seeds it) and the runner (which uses it). A directory and not
// a single auth.json file: Codex rewrites auth.json by rename on token refresh, which a single-file
// bind mount cannot survive.
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
        foreach (var token in new[] { "ENV CODEX_HOME", "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "auth.json", "/state/codex", "/codex-home" })
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
    public void State_initializer_seeds_the_mounted_host_home_without_overwrite()
    {
        var text = Read("docker/stack/init-state.sh");
        text.All(c => c < 128).ShouldBeTrue("init-state.sh stays ASCII");

        // The home is the host directory state-init mounts at /codex-home, not a directory on the
        // runner-state volume: nothing on the volume is named or created for Codex any more.
        text.ShouldContain("codex_home=/codex-home\n");
        text.Contains("/runner-state/codex", StringComparison.Ordinal).ShouldBeFalse("the volume is no longer the Codex home");
        text.Contains("/state/codex", StringComparison.Ordinal).ShouldBeFalse("wrong spelling for the state-init mount");

        // A missing mount refuses rather than seeding a directory that dies with the container.
        var home = Section(text, "codex_home=/codex-home\n", "codex_config=");
        home.ShouldContain("if [ -L \"$codex_home\" ] || [ ! -d \"$codex_home\" ]; then\n");
        home.ShouldContain("echo \"CodexHomeNotMounted path=$codex_home\" >&2\n");

        // Root (a mount Docker had to create itself) is taken over; any other owner refuses. Only
        // the directory itself is chowned and made 0700 -- never recursively, never its contents.
        home.ShouldContain("owner=$(stat -c %u \"$codex_home\")\n");
        home.ShouldContain("if [ \"$owner\" != \"0\" ] && [ \"$owner\" != \"$uid\" ]; then\n");
        home.ShouldContain("echo \"ForeignStateOwner path=$codex_home uid=$owner\" >&2\n");
        text.ShouldContain("chown \"$uid:$gid\" \"$codex_home\"\n");
        text.ShouldContain("chmod 0700 \"$codex_home\"\n");
        Order(home, "ForeignStateOwner path=$codex_home", "chown \"$uid:$gid\" \"$codex_home\"").ShouldBeTrue();
        foreach (var line in text.Split('\n').Where(line => line.Contains("$codex_home", StringComparison.Ordinal)))
            Regex.IsMatch(line, @"\b(chown|chmod)\s+-R\b").ShouldBeFalse("recursive over the Codex home: " + line);
        var recursive = text.Split('\n').Single(line => line.StartsWith("chown -R ", StringComparison.Ordinal));
        recursive.ShouldBe("chown -R \"$uid:$gid\" /state /work /runner-state", "the volume sweep never reaches the host home");

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
        var seed = SeedBlock(text);
        seed.ShouldContain("umask 077\n");
        seed.ShouldContain("chown \"$uid:$gid\" \"$codex_seed\"\n");
        seed.ShouldContain("chmod 0600 \"$codex_seed\"\n");
        seed.ShouldContain("ln \"$codex_seed\" \"$codex_config\"\n");
        seed.ShouldContain("rm -f \"$codex_seed\"\n");
        Order(seed, "chmod 0600 \"$codex_seed\"", "ln \"$codex_seed\" \"$codex_config\"").ShouldBeTrue("never visible at a wider mode");
        seed.Contains("mv ", StringComparison.Ordinal).ShouldBeFalse("mv replaces an existing target");

        // Never touches credentials, sessions or stores; seeded after the volume owner check.
        foreach (var token in new[] { "auth.json", "sessions", "sqlite", ".db" })
            text.Contains(token, StringComparison.Ordinal).ShouldBeFalse("init-state touches " + token);
        Order(text, "ForeignStateOwner path=$d", "codex_home=/codex-home").ShouldBeTrue("a foreign owner refuses before any seed");
        Order(text, "ln \"$codex_seed\" \"$codex_config\"", "chown -R \"$uid:$gid\" /state /work /runner-state").ShouldBeTrue();
    }

    // The Codex section of init-state.sh, run for real against a directory shaped like the host
    // mount deploy-parent leaves: it already exists and may already hold the operator's sign-in.
    // The sentinel is not a credential; the seed must neither read-modify nor re-mode it.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void State_initializer_seeds_an_existing_host_home_and_leaves_its_contents()
    {
        var text = Read("docker/stack/init-state.sh");
        var section = Section(text, "codex_home=/codex-home\n", "chown -R ");
        var script = string.Join('\n',
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            "cd \"$root\"",
            "uid=\"$(id -u)\"; gid=\"$(id -g)\"",
            "seed() {",
            "( set -eu",
            section.Replace("codex_home=/codex-home\n", "codex_home=\"$root/codex\"\n", StringComparison.Ordinal),
            ")",
            "}",
            "mkdir -m 0755 \"$root/codex\"",
            "printf 'c660-sentinel-not-a-credential\\n' > \"$root/codex/auth.json\"",
            "chmod 0640 \"$root/codex/auth.json\"",
            "seed; echo \"existing exit=$?\"",
            "stat -c 'home=%a' \"$root/codex\"",
            "stat -c 'config=%a %F' \"$root/codex/config.toml\"",
            "printf 'entries=%s\\n' \"$(ls -A \"$root/codex\" | tr '\\n' ' ')\"",
            "stat -c 'sentinel=%a' \"$root/codex/auth.json\"",
            "printf 'sentinel-bytes=%s\\n' \"$(cat \"$root/codex/auth.json\")\"",
            "echo '--- config ---'; cat \"$root/codex/config.toml\"; echo '--- end ---'",
            "printf 'model = \"operator\"\\n' > \"$root/codex/config.toml\"",
            "seed; echo \"again exit=$?\"",
            "printf 'kept=%s\\n' \"$(cat \"$root/codex/config.toml\")\"",
            "rm -rf \"$root/codex\"",
            "seed 2>&1; echo \"missing exit=$?\"",
            "[ -e \"$root/codex\" ] || echo missing-not-created",
            "mkdir \"$root/elsewhere\"; ln -s \"$root/elsewhere\" \"$root/codex\"",
            "seed 2>&1; echo \"symlink exit=$?\"",
            "[ -e \"$root/elsewhere/config.toml\" ] || echo symlink-target-untouched",
            "") + "\n";

        var output = RemoteScriptContractTests.LinuxShell(script).Replace("\r\n", "\n");
        output.ShouldContain("existing exit=0\n");
        output.ShouldContain("home=700\n");
        output.ShouldContain("config=600 regular file\n");
        output.ShouldContain("entries=auth.json config.toml \n", customMessage: "no seed scratch is left behind");
        output.ShouldContain("sentinel=640\n", customMessage: "the seed never re-modes what the home already holds");
        output.ShouldContain("sentinel-bytes=c660-sentinel-not-a-credential\n");
        output.ShouldContain("--- config ---\n" + SeededConfig + "--- end ---\n");
        output.ShouldContain("again exit=0\n");
        output.ShouldContain("kept=model = \"operator\"\n", customMessage: "an existing config is never replaced");
        output.ShouldContain("CodexHomeNotMounted path=");
        output.ShouldContain("missing exit=43\n");
        output.ShouldContain("missing-not-created");
        output.ShouldContain("symlink exit=43\n");
        output.ShouldContain("symlink-target-untouched");
    }

    [Test]
    public void Compose_binds_one_host_directory_as_the_codex_home()
    {
        var compose = Read("docker-compose.server2-runner.yml");
        var runner = DockerStackDocuments.Service(compose, "session-runner");
        var init = DockerStackDocuments.Service(compose, "state-init");

        DockerStackDocuments.Env(runner, "CODEX_HOME").ShouldBe("/state/codex");
        DockerStackDocuments.Env(runner, "PhoneHome__CodexHome").ShouldBe("/state/codex", "the auth probe reads the CLI's own store");

        // The same required host directory in both services, read-write (the CLI rewrites
        // auth.json by rename): state-init seeds it where init-state.sh names it, and the runner
        // mounts it exactly at CODEX_HOME.
        var initTarget = HostHomeTarget(init);
        var runnerTarget = HostHomeTarget(runner);
        var seeded = Regex.Match(Read("docker/stack/init-state.sh"), @"codex_home=(\S+)\n");
        seeded.Success.ShouldBeTrue("init-state names the Codex home");
        initTarget.ShouldBe(seeded.Groups[1].Value);
        runnerTarget.ShouldBe(DockerStackDocuments.Env(runner, "CODEX_HOME"));

        // A directory, never a single file: nothing mounts auth.json on its own.
        compose.Contains("auth.json", StringComparison.Ordinal).ShouldBeFalse("a single-file auth.json mount breaks Codex's rename-on-refresh");

        // Subscription-only: no credential name is ever configured on the runner.
        foreach (var name in new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN" })
            compose.Contains(name, StringComparison.OrdinalIgnoreCase).ShouldBeFalse("compose configures " + name);
    }

    // The host path is operator configuration on server2, beside the other identity files; the
    // docs carry the identity-table row and the one-time move of the pre-amendment sign-in.
    [Test]
    public void Stack_env_and_docs_name_the_host_codex_home()
    {
        Read("docker/stack.env.example")
            .ShouldContain("\nRUNNER_CODEX_HOME_DIR=/home/mc/antiphon-server2/secrets/codex\n");

        var docs = Read("docs/docker-stack.md");
        var row = docs.Split('\n').SingleOrDefault(line => line.StartsWith("| `secrets/codex/`", StringComparison.Ordinal));
        row.ShouldNotBeNull("the identity table has a row for the Codex home");
        foreach (var token in new[] { "**Yes**", "0700", "1654", "`deploy-parent`", "`RUNNER_CODEX_HOME_DIR`", "`/state/codex`", "`/codex-home`", "`CODEX_HOME`", "`PhoneHome__CodexHome`", "rename" })
            row.ShouldContain(token);

        // The migration: stream the file between containers with docker cp, never through a
        // terminal or the host filesystem, then fix owner and mode and check presence only.
        foreach (var command in Migration)
            docs.ShouldContain("\n" + command + "\n");
        docs.Contains("cat /state/codex", StringComparison.Ordinal).ShouldBeFalse("the migration never prints the file");
    }

    private static readonly string[] Migration =
    [
        "i=antiphon-runner-state-init-1",
        "c=antiphon-runner-session-runner-1",
        "docker cp \"$i:/runner-state/codex/home/auth.json\" - | docker cp -a - \"$c:/state/codex/\"",
        "docker exec -u 0:0 \"$c\" chown 1654:1654 /state/codex/auth.json",
        "docker exec -u 0:0 \"$c\" chmod 0600 /state/codex/auth.json",
        "docker exec -u 1654:1654 \"$c\" stat -c '%u:%g %a %F' /state/codex/auth.json",
    ];

    // The bind target of the required RUNNER_CODEX_HOME_DIR host directory, read-write.
    private static string HostHomeTarget(string service)
    {
        const string source = "${RUNNER_CODEX_HOME_DIR:?RUNNER_CODEX_HOME_DIR is required}:";
        var entry = DockerStackDocuments.List(service, "volumes")
            .SingleOrDefault(line => line.StartsWith(source, StringComparison.Ordinal));
        entry.ShouldNotBeNull("the service binds the required host Codex home");
        var target = entry[source.Length..];
        target.Contains(':', StringComparison.Ordinal).ShouldBeFalse("the Codex home is mounted read-write: " + entry);
        return target;
    }

    private static string SeedBlock(string text)
    {
        var seed = text[text.IndexOf("if [ ! -e \"$codex_config\" ]", StringComparison.Ordinal)..];
        return seed[..(seed.IndexOf("\nfi\n", StringComparison.Ordinal) + 4)];
    }

    private static string Section(string text, string from, string to)
    {
        var start = text.IndexOf(from, StringComparison.Ordinal);
        start.ShouldBeGreaterThan(-1, "init-state.sh contains " + from.Trim());
        var end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, "init-state.sh contains " + to.Trim() + " after " + from.Trim());
        return text[start..end];
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
