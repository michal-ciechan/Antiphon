using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class C1008HostFixturePortabilityTests
{
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public void C1030_Linux_programs_preserve_legacy_bytes()
    {
        const string root = "/tmp/c1008-host-legacy", repo = "/fixture/repo";
        var source = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-remote.sh"));
        foreach (var dry in new[] { false, true })
        foreach (var extra in new[] { "echo one\necho two\n", "echo one\r\necho two\r\n", "echo no-final-newline" })
            Encoding.UTF8.GetBytes(C1008HostFixture.ComposeProgram(root, repo, source, extra, dry))
                .ShouldBe(Encoding.UTF8.GetBytes(LegacyRun(root, repo, source, extra, dry)), "c1030-linux-run");
        foreach (var fault in new[] { "", "origin-failed", "head", "branch", "tag", "second-remote", "linked", "detached", "bare", "layouts", "dirty", "staged", "untracked" })
            Encoding.UTF8.GetBytes(RemoteScriptContractTests.C1008GitProgram(root, fault))
                .ShouldBe(Encoding.UTF8.GetBytes(LegacyGit(root, fault)), "c1030-linux-git");
        C1008HostFixture.HolderProgram(root).ShouldBe("exec 8>'/tmp/c1008-host-legacy/server/locks/rollout.lock'; flock 8; touch '/tmp/c1008-host-legacy/held'; read -r release", "c1030-linux-holder");
        using var f = new C1008HostFixture(windows: false);
        const string json = "{\"volumes\":{\"v\":{\"Mountpoint\":\"/tmp/c1008-host-legacy/volumes/v/_data\",\"Options\":null}},\"containers\":[{\"Mounts\":[{\"Type\":\"bind\",\"Source\":\"/tmp/c1008-host-legacy/work\",\"RW\":false}]}],\"fault\":\"inspect-error\"}";
        f.AdaptPaths(JsonNode.Parse(json)!.AsObject()).ToJsonString().ShouldBe(json, "c1030-linux-json");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public void C1030_Windows_paths_convert_before_fixture_effects()
    {
        using var f = new C1008HostFixture(windows: true);
        f.ShellRoot.ShouldNotBe(f.Root, "c1030-root-converted: Windows entry must run a converter");
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public void C1030_Windows_transport_ignores_ambient_launchers()
    {
        using var f = new C1008HostFixture(windows: true);
        foreach (var entry in new[] { "run", "git", "holder", "bridge" })
        {
            var start = f.ShellStart(entry, "echo one\r\necho two\r\n", Path.Combine(f.Root, "remote.sh"));
            start.FileName.ShouldBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"), "c1030-" + entry + "-launch");
            start.ArgumentList.ShouldBe(new[] { "-e", "/bin/bash", "-s" });
        }
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public void C1030_Fixture_path_data_preserves_faults_and_round_trips()
    {
        using var f = new C1008HostFixture(windows: true);
        var adapted = f.AdaptPaths(f.Docker);
        adapted["volumes"]!["antiphon-runner_work"]!["Mountpoint"]!.GetValue<string>()
            .ShouldNotBe(f.Docker["volumes"]!["antiphon-runner_work"]!["Mountpoint"]!.GetValue<string>(), "c1030-mountpoint");
    }

    // Independent literals copied before refactoring from the pinned post-C980 source.
    private static string LegacyRun(string Root, string repoRoot, string source, string extra, bool dryRun)
    {
        var injection = $$"""
            C1008_FIXTURE_ROOT='{{Root}}'; export C1008_FIXTURE_ROOT
            SERVER2_ROOT='{{Root}}/server'; ROOT='{{Root}}'; EVIDENCE_ROOT='{{Root}}/evidence'; CASE_DIR="$EVIDENCE_ROOT/$CASE"
            SERVER2_ENV='{{Root}}/main.env'; SERVER2_TEMP_ENV='{{Root}}/temp.env'; mkdir -p "$CASE_DIR"
            RUNNER_GIT_USER_NAME=Fixture; RUNNER_GIT_USER_EMAIL=fixture@example.invalid
            C1008_OPERATION=c100800000000000000000000000000000001; C1008_CONTEXT=default; C1008_PROJECT_ID=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1
            C1008_DRY_RUN={{(dryRun ? "1" : "0")}}; C590_TEMP_RETIRED_AT=2026-10-03T09:30:00Z
            docker() { bash '{{repoRoot}}/scripts/fixtures/c1008-fake-docker.sh' "$@"; }
            compose_host() { docker compose -p "$HOST_PROJECT" "$@"; }
            compose_temp() { docker compose -p "$TEMP_PROJECT" "$@"; }
            detect_lane() { LANE=host; }
            ensure_dirs() { printf 'MUTATION ensure_dirs\n'; }
            ensure_checkout() { printf 'MUTATION ensure_checkout\n'; }
            ensure_runner_boot_files() { printf 'MUTATION boot_files\n'; }
            retire_c590_leftovers() { :; }
            broker_sha12() { echo aaaaaaaaaaaa; }
            build_server2_images() { write_result true '' 0; }
            c849_lock() { :; }
            c849_budget_gate() { :; }
            c849_status_body() { node -e 'process.stdout.write(JSON.stringify(JSON.parse(require("fs").readFileSync(process.argv[1]))[process.argv[2]]))' '{{Root}}/statuses.json' "$1"; }
            c1008_http() { node -e 'const fs=require("fs"),s=JSON.parse(fs.readFileSync(process.argv[1])),p=process.argv[2],u=new URL(p,"http://fixture.invalid"),v=u.searchParams.has("projectId")?s.scopes[u.searchParams.get("projectId")]:s.details[u.pathname.split("/").at(-1)];if(!v)process.exit(2);process.stdout.write(JSON.stringify(v))' '{{Root}}/tasks.json' "$1"; }
            sudo() { [ "$1" = -n ] && shift; if [ "$1" = install ]; then mkdir -p "${@: -1}"; elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nfixture 99999999 1 25000000 1%% /fixture\n'; else "$@"; fi; }
            {{extra}}
            """;
        source = source.Replace("trap 'ec=$?;", injection + "\ntrap 'ec=$?;", StringComparison.Ordinal);
        return source;
    }
    private static string LegacyGit(string root, string fault)
    {
        var script = $$"""
            set -e
            git init -q --bare '{{root}}/origin'
            git init -q -b master '{{root}}/work/repo'
            git -C '{{root}}/work/repo' config user.name Fixture
            git -C '{{root}}/work/repo' config user.email fixture@example.invalid
            git -C '{{root}}/work/repo' remote add origin '{{root}}/origin'
            echo A > '{{root}}/work/repo/file'
            git -C '{{root}}/work/repo' add file
            git -C '{{root}}/work/repo' commit -qm A
            git -C '{{root}}/work/repo' push -q origin master
            """;
        if (fault == "origin-failed") script += $"\ngit -C '{root}/work/repo' remote set-url origin '{root}/missing-origin'\n";
        if (fault is "head" or "branch" or "tag" or "second-remote" or "linked" or "detached" or "bare") script += $$"""

            git -C '{{root}}/work/repo' commit -qm B --allow-empty
            {{(fault == "branch" ? $"git -C '{root}/work/repo' branch unpublished; git -C '{root}/work/repo' checkout -q --detach HEAD~1" : fault == "tag" ? $"git -C '{root}/work/repo' tag unpublished; git -C '{root}/work/repo' reset -q --hard HEAD~1" : "")}}
            """;
        if (fault == "layouts") script += $"\ngit -C '{root}/work/repo' worktree add -q --detach '{root}/work/linked clean' HEAD\ngit clone -q '{root}/origin' '{root}/work/standalone'\ngit clone -q --mirror '{root}/origin' '{root}/work/bare.git'\n";
        if (fault == "second-remote") script += $"\ngit init -q --bare '{root}/second'\ngit -C '{root}/work/repo' remote add other '{root}/second'\ngit -C '{root}/work/repo' push -q other master\n";
        if (fault is "linked" or "detached") script += $"\ngit -C '{root}/work/repo' worktree add -q --detach '{root}/work/unpublished space' HEAD\ngit -C '{root}/work/repo' reset -q --hard HEAD~1\n";
        if (fault == "bare") script += $"\ngit clone -q --mirror '{root}/work/repo' '{root}/work/bare.git'\ngit -C '{root}/work/bare.git' remote set-url origin '{root}/origin'\ngit -C '{root}/work/bare.git' branch unpublished HEAD\ngit -C '{root}/work/bare.git' update-ref refs/heads/master HEAD~1\ngit -C '{root}/work/repo' reset -q --hard HEAD~1\n";
        if (fault is "dirty" or "staged") script += $"\necho B >> '{root}/work/repo/file'\n";
        if (fault == "staged") script += $"git -C '{root}/work/repo' add file\n";
        if (fault == "untracked") script += $"\necho B > '{root}/work/repo/new'\n";
        return script;
    }
}
