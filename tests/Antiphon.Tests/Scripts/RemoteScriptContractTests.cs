using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;

namespace Antiphon.Tests.Scripts;

// CARD-0604 S3. Text guards over the remote script's two lanes. The script only ever executes on
// server2 (host lane) or inside the persistent runner (nested lane), so these are the only guards
// that run on Windows at all; CP-5 through CP-13 are the live rows.
[Category("Unit")]
public sealed class RemoteScriptContractTests
{
    [Test]
    public void C944_All_cache_loop_variables_are_local()
    {
        var remote = Remote();
        CacheLoopLeaks(remote).ShouldBeEmpty("every cache helper owns its loop variables");
        var mutant = remote.Replace("local context=\"${1:-full-required}\" marker image name role item",
            "local context=\"${1:-full-required}\" marker image name role", StringComparison.Ordinal);
        CacheLoopLeaks(mutant).ShouldContain("c849_require_ready: item");
    }

    private static List<string> CacheLoopLeaks(string source)
    {
        var leaks = new List<string>();
        foreach (Match function in Regex.Matches(source, @"(?m)^(c849_\w+)\(\) \{"))
        {
            var name = function.Groups[1].Value;
            var body = Block(source, name);
            var locals = Regex.Matches(body, @"(?m)^\s*local\s+([^\n]+)")
                .SelectMany(line => Regex.Matches(line.Groups[1].Value,
                    @"(?:^|\s)([A-Za-z_]\w*)(?==|\s|$)").Select(v => v.Groups[1].Value)).ToHashSet();
            // A loop inside a quoted docker sh -c program belongs to that child shell.
            var executable = Regex.Replace(body, @"'(?:[^']*)'", "''", RegexOptions.Singleline);
            foreach (Match loop in Regex.Matches(executable, @"(?m)^\s*for\s+(?:\(\(\s*)?([A-Za-z_]\w*)\s*(?:in\b|=)"))
                if (!locals.Contains(loop.Groups[1].Value)) leaks.Add(name + ": " + loop.Groups[1].Value);
        }
        return leaks;
    }

    [Test]
    [Arguments("observe", "symlink")]
    [Arguments("prune", "symlink")]
    [Arguments("observe", "sudo-refused")]
    [Arguments("prune", "sudo-refused")]
    [Arguments("observe", "sudo-test-refused")]
    [Arguments("prune", "sudo-test-refused")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C951_Cache_paths_refuse_symlinked_mountpoints_and_refusing_sudo(string reader, string fault)
    {
        CheckCachePath(reader, fault, Remote());
    }

    private void CheckCachePath(string reader, string fault, string remote)
    {
        var output = LinuxShell("set -u\n" + $"reader='{reader}'; fault='{fault}'\n" + """
            root="$(mktemp -d /tmp/c951-path-XXXXXXXX)"
            printf 'C951_ROOT=%s\n' "$root"
            trap '[[ "$root" == /tmp/c951-path-???????? && -d "$root" ]] && rm -rf -- "$root"' EXIT
            C849_PACKAGES=antiphon-runner-cache-nuget-packages; C849_SCRATCH=scratch; C849_NPM=npm
            CASE=runner-cache-inventory
            canonical="$root/volumes/$C849_PACKAGES/_data"
            mkdir -p "$canonical"
            path="$canonical"
            if [ "$fault" = symlink ]; then ln -s "$canonical" "$root/link"; path="$root/link"; fi
            docker() {
                case "$1:${2:-}:${4:-}" in
                    info:*) printf '%s\n' "$root" ;;
                    volume:inspect:*)
                        case "${4:-}" in
                            *'.Driver'*) echo local ;; *'.Options'*) echo '{}' ;;
                            *'io.antiphon.owner'*) echo server2-runner ;;
                            *'io.antiphon.cache-schema'*) echo 1 ;;
                            *'io.antiphon.cache-role'*) echo nuget-packages ;;
                            *'.Mountpoint'*) printf '%s\n' "$path" ;;
                        esac ;;
                    *) return 2 ;;
                esac
            }
            sudo() {
                [ "$1" = -n ] && shift
                [ "$fault" != sudo-refused ] || return 77
                if [ "$1" = test ] && [ "$2" = -L ] && [ "$fault" = sudo-test-refused ]; then return 77; fi
                if [ "$1" = stat ]; then echo 1654:1654:700; else "$@"; fi
            }
            write_result() { printf 'DIAGNOSIS=%s\n' "$2"; exit "$3"; }
            """ + "\n" + Block(remote, "c849_observe_volume") + "\n" + Block(remote, "c849_prune_validate_tree") + "\n" + """
            if [ "$reader" = observe ]; then
                ( c849_observe_volume "$C849_PACKAGES" nuget-packages 999999 ) > "$root/out" 2>&1
            else
                ( c849_prune_validate_tree "$path" "$path" ) > "$root/out" 2>&1
            fi
            printf 'READER_EXIT=%s\n' "$?"; cat "$root/out"
            """);
        output.Contains("READER_EXIT=2", StringComparison.Ordinal).ShouldBeTrue(reader + ": " + fault + " must fail closed");
        output.ShouldContain("DIAGNOSIS=CacheTargetInvalid");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C976_Missing_helper_image_preserves_final_receipt_diagnosis()
    {
        CheckHelperReceipt(Remote());
    }

    private void CheckHelperReceipt(string remote)
    {
        var trap = remote.Split('\n').Single(line => line.StartsWith("trap 'ec=$?;", StringComparison.Ordinal));
        var output = LinuxShell("set -u\n" + """
            root="$(mktemp -d /tmp/c976-receipt-XXXXXXXX)"
            printf 'C976_ROOT=%s\n' "$root"
            mkdir -p "$root/case"
            """ + "\n" + Block(remote, "json_escape") + "\n" + Block(remote, "scrub_file") + "\n" +
            Block(remote, "write_result") + "\n" + Block(remote, "c849_image") + "\n" + Block(remote, "c849_prepare") + "\n" + """
            docker() { return 1; }; compose_host() { :; }; c849_optional_donor() { :; }
            c849_volume() { echo UNEXPECTED_VOLUME; }; c849_lock() { :; }
            CASE_DIR="$root/case"; C849_READY="$root/missing"; CASE=runner-cache-seed
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; HOST_PROJECT=main; WROTE=0
            """ + "\n( set -e; " + trap + "\nc849_prepare yes\n) > \"$root/out\" 2>&1\n" + """
            printf 'PREPARE_EXIT=%s\n' "$?"
            cat "$root/case/c590-result.json"
            cat "$root/out"
            [[ "$root" == /tmp/c976-receipt-???????? && -d "$root" ]] && rm -rf -- "$root"
            """);
        output.ShouldContain("PREPARE_EXIT=2");
        output.ShouldContain("\"diagnosis\":\"CacheHelperImageMissing\"");
        output.ShouldNotContain("UnhandledExit");
        output.ShouldNotContain("UNEXPECTED_VOLUME");
    }

    [Test]
    public void C946_Rolling_harness_cleans_only_its_owned_success_root()
    {
        var script = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-deploy-server2.ps1"));
        script.ShouldContain("[switch]$KeepTemp");
        script.ShouldContain("finally {");
        script.ShouldContain("Remove-Item -LiteralPath $ownedRoot");
        script.ShouldContain("^c849-rolling-[0-9a-f]{32}$");
        script.ShouldContain("C849_TEMP kept=");
    }

    [Test]
    [Arguments("loop")]
    [Arguments("observe")]
    [Arguments("receipt")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C957_Scratch_mutation_controls_go_red_and_restore(string control)
    {
        var original = Remote();
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "c957-spots-" + Guid.NewGuid().ToString("N")));
        var path = Path.Combine(directory.FullName, "remote.sh");
        var bytes = Encoding.UTF8.GetBytes(original);
        File.WriteAllBytes(path, bytes);
        try
        {
            string mutant;
            if (control == "loop")
                mutant = original.Replace("local context=\"${1:-full-required}\" marker image name role item",
                    "local context=\"${1:-full-required}\" marker image name role", StringComparison.Ordinal);
            else if (control == "observe")
                mutant = original.Replace("[ \"$symlink_status\" = 1 ] || write_result false CacheTargetInvalid 2", ": # scratch mutation drops status refusal", StringComparison.Ordinal);
            else
            {
                var image = Block(original, "c849_image");
                mutant = original.Replace(image, image.Replace("return 10", "write_result false CacheHelperImageMissing 2", StringComparison.Ordinal), StringComparison.Ordinal);
                var prepare = Block(mutant, "c849_prepare");
                mutant = mutant.Replace(prepare, Regex.Replace(prepare, @"image=""\$\(c849_image\)"" \|\| \{.*?\n    \}", "image=\"$(c849_image)\"", RegexOptions.Singleline), StringComparison.Ordinal);
            }
            mutant.ShouldNotBe(original, "control must change scratch source");
            File.WriteAllText(path, mutant);
            if (control == "loop") CacheLoopLeaks(File.ReadAllText(path)).ShouldContain("c849_require_ready: item");
            else if (control == "observe") Should.Throw<ShouldAssertException>(() => CheckCachePath("observe", "sudo-test-refused", File.ReadAllText(path)));
            else Should.Throw<ShouldAssertException>(() => CheckHelperReceipt(File.ReadAllText(path)));
            File.WriteAllBytes(path, bytes);
            if (control == "loop") CacheLoopLeaks(File.ReadAllText(path)).ShouldBeEmpty();
            else if (control == "observe") CheckCachePath("observe", "sudo-test-refused", File.ReadAllText(path));
            else CheckHelperReceipt(File.ReadAllText(path));
            File.ReadAllBytes(path).SequenceEqual(bytes).ShouldBeTrue("exact scratch byte restoration");
            Remote().ShouldBe(original, "tracked source unchanged");
        }
        finally { File.WriteAllBytes(path, bytes); directory.Delete(true); }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C946_Green_harness_removes_root_and_failed_keep_run_retains_evidence()
    {
        RequireLinuxPwsh();
        var repo = DelegateScriptRunner.RepoRoot.Replace("\\", "/", StringComparison.Ordinal);
        if (OperatingSystem.IsWindows()) repo = LinuxShell("wslpath -u '" + repo + "'").Trim();
        var output = LinuxShell("repo='" + repo + "'\n" + """
            set -u
            root="$(mktemp -d /tmp/c946-cleanup-XXXXXXXX)"
            printf 'C946_ROOT=%s\n' "$root"
            trap '[[ "$root" == /tmp/c946-cleanup-???????? && -d "$root" ]] && rm -rf -- "$root"' EXIT
            pwsh -NoProfile -File "$repo/scripts/test-deploy-server2.ps1" -Only retired-start > "$root/green" 2>&1
            printf 'GREEN_EXIT=%s\n' "$?"
            cat "$root/green"
            removed="$(sed -n 's/^C849_TEMP removed=//p' "$root/green" | tr -d '\r')"
            if [[ "$removed" == "$repo/.antiphon/"c849-rolling-* && ! -e "$removed" ]]; then echo GREEN_ROOT_REMOVED; fi
            pwsh -NoProfile -File "$repo/scripts/test-deploy-server2.ps1" -Only cleanup-failure -KeepTemp > "$root/fail" 2>&1
            printf 'FAILED_KEEP_EXIT=%s\n' "$?"
            kept="$(sed -n 's/^C849_TEMP kept=//p' "$root/fail" | tr -d '\r')"
            if [[ "$kept" == "$repo/.antiphon/"c849-rolling-* && -d "$kept" ]]; then
                echo FAILED_KEEP_ROOT_RETAINED
                # Only the root created and printed by this invocation is removed.
                rm -rf -- "$kept"
            fi
            """);
        output.ShouldContain("GREEN_EXIT=0");
        output.ShouldContain("GREEN_ROOT_REMOVED");
        output.ShouldContain("FAILED_KEEP_EXIT=1");
        output.ShouldContain("FAILED_KEEP_ROOT_RETAINED");
    }

    private const string NoLinuxPwshReason = "CARD-0905: pwsh is not on the Linux shell PATH (WSL has no pwsh); install pwsh in WSL to run C849 script-block tests.";
    private const string NoLinuxJqReason = "CARD-0912: jq is not on the Linux shell PATH; install jq in the runner or WSL to run C912 cold-seed tests.";
    private static readonly AsyncLocal<bool> ForceNoLinuxPwsh = new();
    private static readonly AsyncLocal<bool?> ForceLinuxJqAvailability = new();
    private static readonly Lazy<bool> LinuxPwshAvailable = new(() =>
        LinuxShell("command -v pwsh >/dev/null 2>&1 && printf 'C849_PWSH_AVAILABLE\\n'\n")
            .Contains("C849_PWSH_AVAILABLE", StringComparison.Ordinal));

    private static bool HasLinuxPwsh() =>
        !ForceNoLinuxPwsh.Value && LinuxPwshAvailable.Value;

    private static readonly Lazy<bool> LinuxJqAvailable = new(() =>
        LinuxShell("command -v jq >/dev/null 2>&1 && printf 'C912_JQ_AVAILABLE\\n'\n")
            .Contains("C912_JQ_AVAILABLE", StringComparison.Ordinal));

    private static bool HasLinuxJq() => ForceLinuxJqAvailability.Value ?? LinuxJqAvailable.Value;

    private static void RequireLinuxJq()
    {
        if (!HasLinuxJq())
            throw new SkipTestException(NoLinuxJqReason);
    }

    private static void RequireLinuxPwsh()
    {
        if (!HasLinuxPwsh())
            throw new SkipTestException(NoLinuxPwshReason);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C905_Missing_linux_pwsh_skips_all_five_cases_before_a_script_block()
    {
        var previous = ForceNoLinuxPwsh.Value;
        ForceNoLinuxPwsh.Value = true;
        try
        {
            var cases = new Action[]
            {
                C849_Cache_cases_use_only_the_validated_host_lane,
                C849_Saved_donor_archive_is_checked_and_imported_without_a_container,
                C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters,
                C849_Saved_donor_rejects_declared_size_bomb_before_writing,
                C849_Seed_publishes_complete_payloads_before_its_marker
            };
            foreach (var run in cases)
            {
                SkipTestException? skip = null;
                try { run(); }
                catch (SkipTestException exception) { skip = exception; }
                skip.ShouldNotBeNull($"{run.Method.Name} must skip before running a Linux script block");
                skip.Message.ShouldBe(NoLinuxPwshReason);
            }
        }
        finally
        {
            ForceNoLinuxPwsh.Value = previous;
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C905_Linux_shell_finds_pwsh_when_installed()
    {
        if (OperatingSystem.IsWindows()) return; // Linux is the positive probe lane.
        HasLinuxPwsh().ShouldBeTrue("the real shell probe must find pwsh on this Linux host");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_front_door_passes_every_full_case_name_to_the_invoker()
    {
        var repo = DelegateScriptRunner.RepoRoot;
        var front = Path.Combine(repo, "scripts/verify-card0849-caches.ps1");
        var source = File.ReadAllText(front);
        var mapBody = Regex.Match(source, @"(?s)\$map\s*=\s*@\{(?<body>.*?)\r?\n\}")
            .Groups["body"].Value;
        var entries = Regex.Matches(mapBody, @"(?m)^\s*(?<name>\w+)\s*=\s*'(?<remote>[^']+)'\s*$")
            .Select(match => new { Name = match.Groups["name"].Value, Remote = match.Groups["remote"].Value })
            .ToArray();
        entries.Length.ShouldBeGreaterThan(0, "the front door case map is found");
        var accepted = Regex.Matches(Regex.Match(source, @"\[ValidateSet\((?<values>[^)]*)\)\]")
                .Groups["values"].Value, @"'([^']+)'")
            .Select(match => match.Groups[1].Value)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        accepted.ShouldBe(entries.Select(entry => entry.Name).Append("Both")
            .OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            "every accepted case has an invoker assertion");

        var scratch = Path.Combine(repo, ".antiphon", "c849-front-door-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var mapPath = Path.Combine(scratch, "cases.json");
            var harnessPath = Path.Combine(scratch, "probe.ps1");
            File.WriteAllText(mapPath, JsonSerializer.Serialize(entries) + "\n");
            File.WriteAllText(harnessPath, """
                param([string]$Front, [string]$MapPath, [string]$Scratch)
                $ErrorActionPreference = 'Stop'
                $repo = Split-Path -Parent (Split-Path -Parent $Front)
                $sha = (& git -C $repo rev-parse HEAD).Trim()
                $entries = @(Get-Content -Raw -LiteralPath $MapPath | ConvertFrom-Json)
                $global:ownedEvidence = @()
                function global:C849WriteLfFixture([string]$Path, [string]$Value) {
                    [IO.File]::WriteAllText($Path, $Value + "`n", [Text.Encoding]::ASCII)
                }
                function global:C849AssertLfFixture([string]$Path) {
                    if ([IO.File]::ReadAllBytes($Path) -contains [byte]13) {
                        throw "FAIL C849 fixture LF-only $([IO.Path]::GetFileName($Path))"
                    }
                }
                function global:pwsh {
                    param([switch]$NoProfile, [string]$File, [string]$Case, [string]$Manifest)
                    $global:seen += $Case
                    $m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
                    $evidence = Split-Path -Parent $m.evidenceRoot
                    $global:ownedEvidence += $evidence
                    if ($global:requested -ne 'Both') { throw 'C849_STUB_STOP' }
                    $caseDir = Join-Path $m.evidenceRoot $Case
                    New-Item -ItemType Directory -Path $caseDir -Force | Out-Null
                    C849WriteLfFixture (Join-Path $caseDir 'c590-result.json') '{"accepted":true,"exit":0}'
                    C849WriteLfFixture (Join-Path $caseDir 'status.json') (
                        @{ sessions=0; runnerSessions=0; queuedTasks=0; buildVersion=$sha; dispatchEligible=$true; acceptingNewWork=$true; draining=$false } | ConvertTo-Json -Compress)
                    C849WriteLfFixture (Join-Path $caseDir 'smoke-summary.txt') 'uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK'
                    $private = if ($m.runnerId -eq 'server2') { 'antiphon-runner_runner-tmp' } else { 'antiphon-runner-temp_runner-tmp' }
                    C849WriteLfFixture (Join-Path $caseDir 'runner-mounts.txt') (
                        "antiphon-runner-cache-nuget-packages antiphon-runner-cache-nuget-scratch antiphon-runner-cache-npm-content`n$private /tmp true")
                    C849WriteLfFixture (Join-Path $caseDir 'seed-hash.txt') ('a' * 64)
                    C849WriteLfFixture (Join-Path $caseDir 'seed-kind.txt') 'full'
                    foreach ($name in @('c590-result.json', 'status.json', 'smoke-summary.txt', 'runner-mounts.txt', 'seed-hash.txt', 'seed-kind.txt')) {
                        C849AssertLfFixture (Join-Path $caseDir $name)
                    }
                }
                try {
                    foreach ($entry in $entries) {
                        $global:requested = $entry.Name
                        $global:seen = @()
                        $arguments = @{ Case = $entry.Name; Sha = $sha }
                        if ($entry.Name -eq 'Prune') {
                            $previewDir = Join-Path $Scratch 'runner-cache-prune-preview'
                            New-Item -ItemType Directory -Path $previewDir -Force | Out-Null
                            $preview = Join-Path $previewDir 'preview.txt'
                            C849WriteLfFixture $preview "run=c849$('a' * 16)0`nsource-sha=$sha"
                            C849AssertLfFixture $preview
                            $arguments.Preview = $preview
                        }
                        try { & $Front @arguments | Out-Null }
                        catch {
                            if ($_.Exception.Message -ne 'C849_STUB_STOP') { throw }
                        }
                        if ($global:seen.Count -ne 1 -or $global:seen[0] -cne $entry.Remote) {
                            throw "FAIL $($entry.Name) full remote case: expected $($entry.Remote), got $($global:seen -join ',')"
                        }
                        Write-Output "PASS $($entry.Name) full remote case"
                    }
                    $global:requested = 'Both'
                    $global:seen = @()
                    & $Front -Case Both -Sha $sha | Out-Null
                    if (($global:seen -join ',') -cne 'verify-runner-caches,verify-runner-caches') {
                        throw "FAIL Both ordered remote cases: got $($global:seen -join ',')"
                    }
                    Write-Output 'PASS Both ordered remote cases'
                }
                finally {
                    foreach ($path in ($global:ownedEvidence | Select-Object -Unique)) {
                        if ((Split-Path -Leaf $path) -match '^c849-c849[0-9a-f]{16}$' -and
                            (Split-Path -Parent $path) -eq (Join-Path $repo '.antiphon')) {
                            Remove-Item -LiteralPath $path -Recurse -Force
                        }
                    }
                    Remove-Item Function:\pwsh -ErrorAction SilentlyContinue
                    Remove-Item Function:\C849WriteLfFixture -ErrorAction SilentlyContinue
                    Remove-Item Function:\C849AssertLfFixture -ErrorAction SilentlyContinue
                }
                """.ReplaceLineEndings("\n") + "\n");

            foreach (var path in new[] { mapPath, harnessPath })
                File.ReadAllBytes(path).ShouldNotContain((byte)'\r', $"FAIL C849 fixture LF-only {Path.GetFileName(path)}");

            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = repo,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in new[] { "-NoProfile", "-File", harnessPath, "-Front", front,
                         "-MapPath", mapPath, "-Scratch", scratch })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("C849 front door probe timed out");
            }
            var output = stdout.Result + stderr.Result;
            process.ExitCode.ShouldBe(0, output);
            foreach (var entry in entries)
                output.ShouldContain("PASS " + entry.Name + " full remote case");
            output.ShouldContain("PASS Both ordered remote cases");
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public void Nested_lane_never_uses_sudo_or_python()
    {
        var text = Remote();

        // The runner image has no python3 at all: every JSON write, every count and every context
        // check is shell. One `python3` reintroduced anywhere kills the whole nested lane.
        var nestedText = text.Replace(Block(text, "c849_saved_copy"), "", StringComparison.Ordinal);
        Executable(nestedText, "python3").ShouldBeEmpty("nested lane invokes python3");
        Executable(nestedText, "python ").ShouldBeEmpty("nested lane invokes python");

        // sudo is the host lane's alone: the nested lane runs as uid 1654, whose ONLY sudo grant
        // is the two custody helpers (CARD-0604 D-17), so a general sudo invocation from nested
        // shell would fail anyway and a new one is an unconditional failure here. Merely naming
        // the path (an assertion that the runtime image does NOT carry sudo) is the opposite of
        // an invocation and must not trip this.
        //
        // Two places may invoke it. `ensure_dirs`' host branch elevates on the server2 HOST to
        // create its own directories. `case_custody_containment` is a host-lane case whose sudo
        // is inside a `docker exec -u 1654` payload -- it is executed by the container as the app
        // uid, against the allow-listed grant, which is the very thing that case measures.
        var sudoLines = Executable(text, "sudo")
            .Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"(^|[;&|(]\s*)sudo\s"))
            .ToList();
        sudoLines.ShouldNotBeEmpty("the host lane still elevates to create its own directories");
        var containment = Block(text, "case_custody_containment");
        var tempDeploy = Block(text, "case_deploy_temp_runner");
        // CARD-0660 (amended): the third is `ensure_runner_codex_home`, which creates and owns the
        // host Codex home for uid 1654 -- a chown the host user mc cannot do. It refuses off the
        // host lane before any of its sudo lines.
        var codexHome = Block(text, "ensure_runner_codex_home");
        var cacheLock = Block(text, "c849_lock");
        var cacheEvidence = Block(text, "c849_evidence_dir");
        var cacheSeed = Block(text, "c849_seed");
        var cacheColdSeed = Block(text, "c849_cold_seed");
        var cacheColdFacts = Block(text, "c849_cold_volume_facts");
        var cacheColdProof = Block(text, "c849_cold_proof");
        var cacheColdProbe = Block(text, "c849_cold_probe");
        var cacheReady = Block(text, "c849_require_ready");
        cacheColdSeed.ShouldContain("require_lane host");
        var cacheObserve = Block(text, "c849_observe_volume");
        var cachePreview = Block(text, "c849_preview");
        var cachePruneTree = Block(text, "c849_prune_validate_tree");
        var cacheFixture = Block(text, "c849_fixture");
        cacheFixture.ShouldContain("require_lane host");
        var fixtureRestore = Block(text, "c849_fixture_nuget_race");
        var fixtureApphost = Block(text, "c849_fixture_apphost");
        var fixtureNpm = Block(text, "c849_fixture_npm");
        foreach (var line in sudoLines)
            (EnsureDirsBody(text).Contains(line, StringComparison.Ordinal)
                || containment.Contains(line, StringComparison.Ordinal)
                || tempDeploy.Contains(line, StringComparison.Ordinal)
                || codexHome.Contains(line, StringComparison.Ordinal)
                || cacheLock.Contains(line, StringComparison.Ordinal)
                || cacheEvidence.Contains(line, StringComparison.Ordinal)
                || cacheSeed.Contains(line, StringComparison.Ordinal)
                || cacheColdSeed.Contains(line, StringComparison.Ordinal)
                || cacheColdFacts.Contains(line, StringComparison.Ordinal)
                || cacheColdProof.Contains(line, StringComparison.Ordinal)
                || cacheColdProbe.Contains(line, StringComparison.Ordinal)
                || cacheReady.Contains(line, StringComparison.Ordinal)
                || cacheObserve.Contains(line, StringComparison.Ordinal)
                || cachePreview.Contains(line, StringComparison.Ordinal)
                || cachePruneTree.Contains(line, StringComparison.Ordinal)
                || fixtureRestore.Contains(line, StringComparison.Ordinal)
                || fixtureApphost.Contains(line, StringComparison.Ordinal)
                || fixtureNpm.Contains(line, StringComparison.Ordinal))
                .ShouldBeTrue("sudo outside a declared host-lane case or helper: " + line);
        EnsureDirsBody(text).ShouldContain("if [ \"$LANE\" = \"host\" ]; then");
        var codexCommands = Commands(codexHome);
        codexCommands[1].ShouldBe("if [ \"$LANE\" != \"host\" ]; then", "the Codex home refuses off the host lane first");
        codexCommands[2].ShouldBe("write_result false CodexHomeHostLaneOnly 2");

        // And the containment case's sudo only READS the grant; it never runs a helper directly
        // (the probe does that, as uid 1654) and never elevates anything else.
        foreach (var line in sudoLines.Where(l => containment.Contains(l, StringComparison.Ordinal)))
            line.ShouldContain("sudo -n -l");
        containment.ShouldContain("docker exec -u 1654");
        tempDeploy.ShouldContain("require_lane host");
        foreach (var line in sudoLines.Where(l => tempDeploy.Contains(l, StringComparison.Ordinal)))
            (line.Contains("sudo -n test -d \"$grok_dir\"", StringComparison.Ordinal)
                || line.Contains("sudo -n df -Pk \"$mount\"", StringComparison.Ordinal))
                .ShouldBeTrue("temp deploy only inspects the host volume filesystem: " + line);
    }

    // CARD-0604 S12 / R-5, G-40 (Cut B). The fence inverts with the cut: the session-testing
    // image IS now a supported producer, so the payload case must prove the custody mechanism is
    // present and root-owned -- and must still refuse any trace of the WINDOWS backend, which on
    // a Linux image could only ever be a fabricated claim. The runtime and receipt-probe targets
    // keep carrying none of it.
    [Test]
    public void Testing_payload_expects_linux_backend_never_windows()
    {
        var payload = Block(Remote(), "case_testing_payload");

        payload.ShouldContain("test -x /usr/local/bin/antiphon-custody-enter");
        payload.ShouldContain("test -x /usr/local/bin/antiphon-custody-kill");
        payload.ShouldContain("test -f /etc/sudoers.d/antiphon-custody");

        // Root-owned and 0755/0440, checked on the real image rather than trusted from the
        // Dockerfile: a COPY whose ownership is wrong hands uid 1654 the shim itself.
        payload.ShouldContain("stat -c %U:%G:%a /usr/local/bin/antiphon-custody-enter)\" = \"root:root:755");
        payload.ShouldContain("stat -c %U:%G:%a /usr/local/bin/antiphon-custody-kill)\" = \"root:root:755");
        payload.ShouldContain("stat -c %U:%G:%a /etc/sudoers.d/antiphon-custody)\" = \"root:root:440");

        // The Windows backend must not appear anywhere the runner would read it.
        payload.ShouldContain("! grep -a -q windows-job-v1 /etc/sudoers.d/antiphon-custody");

        // And the Cut A claim that this image advertises NO custody at all is gone: leaving it
        // standing would fail the moment the cut it is guarding actually lands.
        payload.Contains("! grep -a -q VerificationCustodyV1", StringComparison.Ordinal)
            .ShouldBeFalse("the no-custody assertion is superseded by Cut B");
    }

    // CARD-0604 V-28 (cgroup v1 half). The measurement is a case, not a comment: it runs the
    // same probe the Docker Desktop harness runs on v2, as uid 1654 through docker exec, and
    // refuses if it reads v2 -- which would mean the freezer path is still unmeasured.
    [Test]
    public void Custody_containment_case_measures_the_v1_path_as_the_app_uid()
    {
        var block = Block(Remote(), "case_custody_containment");

        block.ShouldContain("docker exec -u 1654");
        block.ShouldContain("antiphon-custody-containment-probe");
        block.ShouldContain("containment=ok");
        block.ShouldContain("cgroup_version=v1");
        block.ShouldContain("ExpectedCgroupV1");
        block.ShouldContain("CustodyNotAdvertised");
        block.ShouldContain("WindowsBackendAdvertisedOnLinux");
        block.ShouldContain("CustodyHelpersNotRootOwned");
        block.ShouldContain("CustodyResidue");
        // Never as root: running the probe as root would measure a mechanism nobody uses.
        block.Contains("docker exec -u 0", StringComparison.Ordinal).ShouldBeFalse();
        block.Contains("docker exec \"$container\" /usr/local/bin/antiphon-custody-containment-probe",
            StringComparison.Ordinal).ShouldBeFalse("the probe must be run as uid 1654");
    }

    [Test]
    public void Custody_residue_counts_subdirectories_only()
    {
        var block = Block(Remote(), "case_custody_containment");
        foreach (var controller in new[] { "pids", "freezer" })
        {
            var root = "/sys/fs/cgroup/" + controller + "/antiphon-custody";
            block.ShouldContain("find " + root + " -mindepth 1 -maxdepth 1 -type d");
            block.ShouldNotContain("ls -1 " + root);
        }
    }

    // CP-15 dispatches `verify-docker-stack.ps1 -Case custody-containment`, which reaches the
    // remote ONLY if the case is in c590-real.ps1's live roster. Omitted, it falls through to the
    // default arm and reports "RealCasePending" -- a red checkpoint for a routing reason, with
    // the v1 freezer path never measured and nothing on server2 touched.
    [Test]
    public void Custody_containment_is_routed_to_the_remote_and_not_left_pending()
    {
        var live = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "c590-real.ps1"));
        var stack = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "verify-docker-stack.ps1"));

        live.ShouldContain("'custody-containment'");
        // The stub boundary exists too, and refuses the same things the remote refuses first.
        stack.ShouldContain("'custody-containment' {");
        stack.ShouldContain("ExpectedCgroupV1");
        stack.ShouldContain("CustodyHelpersNotRootOwned");
        stack.ShouldContain("CustodyResidue");
        // Every host-lane case the remote script implements must be routed, or the row is a stub.
        var remote = Remote();
        foreach (var hostCase in new[] { "custody-containment", "deploy-parent", "nested-residue" })
            live.Contains("'" + hostCase + "'", StringComparison.Ordinal)
                .ShouldBeTrue(hostCase + " is implemented in c590-remote.sh but never routed there");
        remote.ShouldContain("custody-containment) case_custody_containment");
    }

    // V-29 / R-5 on the live runner: the linux-custody case REQUIRES the Linux backend and a
    // store id, and names the Windows backend only to refuse it.
    [Test]
    public void Linux_custody_case_requires_the_linux_backend()
    {
        var text = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Infrastructure.DockerStackDocuments.RepoRoot, "scripts", "verify-docker-stack.ps1"));

        text.ShouldContain("'linux-custody'");
        text.ShouldContain("CustodyNotAdvertised");
        text.ShouldContain("WindowsBackendAdvertisedOnLinux");
        text.ShouldContain("RunnerStoreIdMissing");
        text.ShouldContain("-ne 'linux-cgroup-v1'");
    }

    [Test]
    public void Scrub_covers_github_token_prefixes()
    {
        var scrub = Block(Remote(), "scrub_file");
        // Every GitHub token prefix, not only the gho_ the CARD-0590 pattern covered.
        foreach (var prefix in new[] { "ghp_", "gho_", "ghu_", "ghs_", "ghr_" })
            Matches(scrub, prefix).ShouldBeTrue("scrub pattern does not cover " + prefix);
        Matches(scrub, "github_pat_11ABCDEFG0aBcDeFgHiJkL").ShouldBeTrue("scrub pattern does not cover github_pat_");
        scrub.ShouldContain("PRIVATE KEY");
    }

    [Test]
    public void Smoke_deletes_its_branch()
    {
        var smoke = Block(Remote(), "case_git_smoke");
        smoke.ShouldContain("throwaway/c604-credential-smoke-$RUN");
        smoke.ShouldContain("git -C \"$work\" push origin --delete \"$branch\"");
        smoke.ShouldContain("SmokeBranchNotDeleted");
        // Deletion is verified against origin, not assumed from the push's exit code.
        smoke.ShouldContain("ls-remote --exit-code --heads origin \"$branch\"");
        smoke.ShouldContain("SmokeBranchSurvived");
        Order(smoke, "git push origin \"HEAD:$branch\"", "push origin --delete").ShouldBeTrue();
        // The credential is the deploy key on its tmpfs, never a desktop-sourced token.
        smoke.ShouldContain("/run/antiphon/deploy-key");
        smoke.ShouldNotContain("gh-token");
        smoke.ShouldNotContain("GIT_ASKPASS");
    }

    [Test]
    public void Child_project_is_run_scoped_and_distinct()
    {
        var text = Remote();
        text.ShouldContain("HOST_PROJECT=\"antiphon-runner\"");
        text.ShouldContain("CHILD_PROJECT=\"c604${RUN}\"");
        // The nested child never composes the persistent project, and the host lane never
        // composes the run-scoped one: a collision would take the standing runner down.
        Block(text, "compose_child").ShouldContain("COMPOSE_PROJECT_NAME=\"$CHILD_PROJECT\"");
        Block(text, "compose_child").ShouldNotContain("HOST_PROJECT");
        Block(text, "compose_host").ShouldContain("-p \"$HOST_PROJECT\"");
        Block(text, "compose_host").ShouldNotContain("CHILD_PROJECT");
        // `down -v` is only ever aimed at the child or the temp project, never at the persistent project.
        Block(text, "compose_temp").ShouldContain("-p \"$TEMP_PROJECT\"");
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            // Comments explain the rule; only executable lines are bound by it.
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            if (!trimmed.Contains("down -v", StringComparison.Ordinal))
                continue;
            trimmed.ShouldNotContain("-p \"$HOST_PROJECT\"");
            trimmed.ShouldNotContain("-p \"antiphon-runner\"");
            (trimmed.StartsWith("compose_child ", StringComparison.Ordinal)
                || trimmed.StartsWith("compose_temp ", StringComparison.Ordinal)
                || trimmed.Contains("-p \"$project\"", StringComparison.Ordinal)
                || trimmed.Contains("-p \"$main\"", StringComparison.Ordinal)
                || trimmed.Contains("-p \"$temp\"", StringComparison.Ordinal)
                || trimmed.Contains("-p \"c849${RUN}", StringComparison.Ordinal))
                .ShouldBeTrue("down -v aimed at something other than the child, temp or a retired c590 project: " + trimmed);
        }
    }

    [Test]
    public void Host_compose_uses_the_deployed_tag_not_this_runs_sha()
    {
        var text = Remote();
        // A case that only restarts or inspects the standing runner must compose the image that is
        // actually deployed. Deriving the tag from this run's sha made `up -d --no-build` look for
        // a tag that was never built, try to PULL it, and leave the runner stopped.
        Block(text, "compose_host").ShouldContain("sha12=\"$(deployed_sha12)\"");
        Block(text, "compose_host").ShouldNotContain("${SHA:0:12}");
        Block(text, "deployed_sha12").ShouldContain("SERVER2_ENV");

        // deploy-parent writes that file before it composes, so it deploys its own sha.
        var deploy = Block(text, "case_deploy_parent");
        Order(deploy, "SOURCE_SHA12=${SHA:0:12}", "compose_host up -d")
            .ShouldBeTrue("the deploy pins the env before it composes");

        // The restart case owns bringing the runner back before it refuses.
        var restart = Block(text, "case_persistent_restart");
        Order(restart, "compose_host stop", "RestartFailed").ShouldBeTrue();
        // Two bring-ups: the one that failed, and the recovery that runs before the refusal.
        System.Text.RegularExpressions.Regex
            .Matches(restart, @"compose_host up -d --no-build").Count
            .ShouldBe(2, "the failure path brings the runner back before refusing");
        var recovery = restart.IndexOf("compose_host up -d --no-build >> \"$CASE_DIR/command.log\" 2>&1 || true", StringComparison.Ordinal);
        recovery.ShouldBeGreaterThan(0, "the recovery bring-up tolerates its own failure");
        recovery.ShouldBeLessThan(restart.IndexOf("RestartFailed", StringComparison.Ordinal));
    }

    [Test]
    public void Retirement_is_anchored_on_the_run_scoped_prefix()
    {
        // D-9: the host daemon is shared with am-service, traefik, windmill and schoolrevision-*.
        // Every retirement pattern is anchored so it can only ever name a c590 run's own leftovers.
        var retire = Block(Remote(), "retire_c590_leftovers");
        retire.ShouldContain("c590[0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]");
        retire.ShouldContain("'^antiphon-c590-'");
        retire.ShouldContain("'^c590[0-9a-f]{12}_'");
        // The inventory is written before anything is removed: an untraceable retirement is worse
        // than a leftover.
        Order(retire, "inventory-containers.txt", "down -v").ShouldBeTrue();
        Order(retire, "inventory-volumes.txt", "volume rm").ShouldBeTrue();
        retire.ShouldNotContain("prune");
    }

    [Test]
    public void Host_daemon_is_never_pruned()
    {
        // Cache-specific prune helpers may clear selected entries under the maintenance
        // lock. A daemon-wide prune would also erase unrelated services' resources.
        foreach (var line in Remote().Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
                continue;
            foreach (var forbidden in new[] { "docker system prune", "docker volume prune", "docker image prune", "docker container prune" })
                trimmed.Contains(forbidden, StringComparison.Ordinal)
                    .ShouldBeFalse("remote script prunes a daemon: " + trimmed);
        }
    }

    [Test]
    public void Lane_detection_separates_a_bare_host_from_the_runner()
    {
        var detect = Block(Remote(), "detect_lane");

        // A bare host's daemon reports the HOST's own hostname, exactly as the nested daemon
        // reports the runner container's - so "Name equals hostname" alone says nothing about
        // which lane this is, and CP-5 self-identified as nested on server2's shell because of it.
        detect.ShouldContain("/.dockerenv");
        Order(detect, "!= \"$own\"", "/.dockerenv")
            .ShouldBeTrue("a sibling daemon is ruled out before the container check");

        // All four outcomes are named; a sibling is neither lane, not silently one of them.
        foreach (var lane in new[] { "none", "sibling", "nested", "host" })
            detect.ShouldContain("LANE=\"" + lane + "\"");
    }

    [Test]
    public void Every_case_declares_a_lane()
    {
        var text = Remote();
        text.ShouldContain("detect_lane > /dev/null");
        text.ShouldContain("WrongLane want=$want lane=$LANE");
        // The three cases that change standing state are host-lane, and the nested roster is not.
        foreach (var name in new[] { "case_deploy_parent", "case_nested_residue", "case_persistent_restart",
                     "case_handoff", "case_custody_containment" })
            Block(text, name).ShouldContain("require_lane host");
        foreach (var name in new[] { "case_throwaway", "case_deployment_state", "case_git_smoke", "case_dotnet", "case_client_tests_body" })
            Block(text, name).ShouldContain("require_lane nested");
    }

    private static bool Matches(string pattern, string sample)
    {
        // The scrub is a sed -E expression; check the sample against the same alternation.
        foreach (var expression in new[]
                 {
                     @"gh[pousr]_[A-Za-z0-9_]+",
                     @"github_pat_[A-Za-z0-9_]+",
                 })
        {
            if (!pattern.Contains(expression, StringComparison.Ordinal))
                continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(sample + "XXXXXXXX", expression))
                return true;
        }

        return false;
    }

    // CARD-0604 D-2. deploy-parent accepted a runner that had never once registered: /health and
    // `docker info` were the whole verdict, and the standing runner sat green through 304
    // consecutive phone-home failures. Both new probes must refuse, by name.
    [Test]
    public void Deploy_parent_probes_the_phone_home_secret_as_the_app_uid()
    {
        var deploy = Block(Remote(), "case_deploy_parent");

        // Read as uid 1654, at whatever path the runner is actually configured to read.
        deploy.ShouldContain("printenv PhoneHome__SecretPath");
        deploy.ShouldContain("docker exec -u 1654:1654 \"$container\" head -c 1 \"$phone_home_secret_path\"");
        deploy.ShouldContain("write_result false PhoneHomeSecretUnreadable 2");
        deploy.ShouldContain("write_result false PhoneHomeSecretPathUnset 2");

        // The probe's one byte goes to /dev/null: the secret is never captured into evidence.
        Executable(deploy, "head -c 1 \"$phone_home_secret_path\"")
            .ShouldAllBe(line => line.Contains("> /dev/null", StringComparison.Ordinal));

        // It happens while the case can still refuse, not after the accept.
        Order(deploy, "PhoneHomeSecretUnreadable", "write_result true").ShouldBeTrue();
    }

    [Test]
    public void Deploy_parent_phone_home_grep_matches_the_runners_registration_failure_template()
    {
        var service = File.ReadAllText(Path.Combine(
            DelegateScriptRunner.RepoRoot, "src", "Antiphon.SessionRunner", "PhoneHomeConnectionService.cs"));
        var deploy = Block(Remote(), "case_deploy_parent");

        // The runner's message template must still start with the text the deploy gate greps for.
        service.ShouldContain("\"Phone-home registration failed: reason={Reason}");
        var grep = deploy.Replace("\r\n", "\n").Split('\n')
            .Single(line => line.Contains("phone_home_failures=\"$(grep -cE", StringComparison.Ordinal));
        grep.ShouldContain("Phone-home registration failed");
    }

    [Test]
    public void Deploy_parent_refuses_a_runner_whose_phone_home_keeps_failing()
    {
        var deploy = Block(Remote(), "case_deploy_parent");

        // The window opens after the health wait (i.e. past the compose start_period), so a
        // single cold-start reconnect is not a verdict, and it is long enough for the 15s
        // backoff (capped at 5s) to leave more than one mark if the loop can only fail.
        deploy.ShouldContain("phone_home_since=\"$(date -u +%Y-%m-%dT%H:%M:%S)\"");
        deploy.ShouldContain("docker logs --since \"$phone_home_since\" \"$container\"");
        // The current runner logs a rejected/unreachable registration as "registration failed";
        // the old line is kept so a pre-S4 image is still gated.
        deploy.ShouldContain("Phone-home registration failed");
        deploy.ShouldContain("Phone-home connection ended; reconnecting");
        deploy.ShouldContain("UnauthorizedAccessException");
        deploy.ShouldContain("write_result false PhoneHomeUnreachable 2");

        // "Repeated", not "any": one is a reconnect, two in the window is a loop that cannot win.
        deploy.ShouldContain("-ge 2 ]");

        // CP-6a is what enables phone-home on the production server, and it runs AFTER this case.
        // A server still answering phone_home_disabled is therefore recorded, not blamed on the
        // deployment -- otherwise this refusal would make the plan's own order unsatisfiable.
        // Every other cause (a permission fault, a rejected secret, any other conflict) still
        // refuses, which is the entire reason the probe exists.
        deploy.ShouldContain("/api/session-runners/register");
        deploy.ShouldContain("phone_home_disabled");
        deploy.ShouldContain("phone-home-server-state.txt\")\" != \"disabled\" ]");
        // The probe carries no secret: it separates "disabled" from "enabled" and nothing else.
        Block(Remote(), "case_deploy_parent")
            .Split('\n')
            .Where(l => l.Contains("register-probe", StringComparison.Ordinal))
            .ShouldAllBe(l => !l.Contains("SecretHeader", StringComparison.Ordinal)
                && !l.Contains("PHONE_HOME_SECRET", StringComparison.Ordinal));

        // Runner output can carry a token; the window is scrubbed like every other evidence file.
        deploy.ShouldContain("scrub_file \"$CASE_DIR/phone-home-window.log\"");
        Order(deploy, "write_result false PhoneHomeUnreachable 2", "write_result true").ShouldBeTrue();
    }

    // CARD-0604 D-5. deploy-parent builds a ~2.2 GB image pair every round and used to leave every
    // superseded pair on a SHARED host daemon. Its own build products must be retired, and only
    // its own: prune stays forbidden and no foreign repository may be reachable by the pattern.
    [Test]
    public void Deploy_parent_retires_its_own_superseded_images()
    {
        var text = Remote();
        var retire = Block(text, "retire_superseded_server2_images");
        retire.ShouldContain("^antiphon-server2/(server|session-testing):");
        retire.ShouldContain("docker image rm \"$image\"");
        // The tag being deployed is kept; everything older goes.
        retire.ShouldContain("antiphon-server2/server:$keep");
        retire.ShouldContain("antiphon-server2/session-testing:$keep");
        retire.ShouldNotContain("prune");

        var deploy = Block(text, "case_deploy_parent");
        deploy.ShouldContain("retire_superseded_server2_images \"${SHA:0:12}\"");
        // Only once the new deployment is proven: an image still in use cannot be removed, and a
        // failed deploy must leave the pair that is actually running alone.
        Order(deploy, "compose_host up -d", "retire_superseded_server2_images").ShouldBeTrue();
        Order(deploy, "PhoneHomeUnreachable", "retire_superseded_server2_images").ShouldBeTrue();
        deploy.ShouldContain("inventory-images-after.txt");
    }

    [Test]
    public void Temp_deploy_uses_its_own_project_and_the_inspected_grok_store()
    {
        var text = Remote();
        var deploy = Block(text, "case_deploy_temp_runner");
        deploy.ShouldContain("docker volume inspect -f '{{.Mountpoint}}' antiphon-runner_runner-state");
        deploy.ShouldContain("NestedStoreDiskLow");
        deploy.ShouldContain("RUNNER_GROK_STORE_DIR=\"$grok_dir\"");
        deploy.ShouldContain("cat > \"$SERVER2_TEMP_ENV\"");
        deploy.ShouldContain("seed_runner_checkout \"$TEMP_PROJECT\"");
        deploy.ShouldContain("compose_temp up -d --no-build");
        deploy.ShouldContain("temp-container.txt");
        deploy.ShouldContain("bridge-nf.txt");
        deploy.ShouldNotContain("--remove-orphans");
        Block(text, "compose_temp").ShouldContain("-p \"$TEMP_PROJECT\"");
        var live = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-real.ps1"));
        live.ShouldContain("'deploy-temp-runner'");
    }

    [Test]
    public void Temp_retire_requires_the_retired_receipt_and_downs_only_temp_volumes()
    {
        var text = Remote();
        var retire = Block(text, "case_retire_temp_runner");
        retire.ShouldContain("C590_TEMP_RETIRED_AT");
        retire.ShouldContain("TempRunnerNotRetired");
        retire.ShouldContain("compose_temp down -v");
        retire.ShouldContain("temp-down.txt");
        retire.ShouldNotContain("docker image rm");
        Block(text, "compose_temp").ShouldContain("-p \"$TEMP_PROJECT\"");
        var keep = Block(text, "retire_superseded_server2_images");
        keep.ShouldContain("temp_keep");
        keep.ShouldContain("broker_keep");
        keep.ShouldContain("live_temp_image");
        keep.ShouldContain("live_broker_image");
        Block(text, "case_deploy_parent").ShouldNotContain("--remove-orphans");
    }

    // CARD-0631 D-9 (amended). The runner's git identity is a file on server2 beside the deploy
    // key and the Claude token. deploy-parent creates it when missing, from stack.env's values or
    // the defaults, and never rewrites one that exists: the operator may have edited it.
    [Test]
    public void Deploy_parent_creates_the_git_identity_file_without_overwriting()
    {
        var text = Remote();
        text.ShouldContain("GIT_IDENTITY_PATH=\"$SERVER2_ROOT/secrets/gitconfig\"");
        text.ShouldContain("GIT_IDENTITY_DEFAULT_NAME=\"antiphon-server2-runner\"");
        text.ShouldContain("GIT_IDENTITY_DEFAULT_EMAIL=\"antiphon-server2-runner@users.noreply.github.com\"");
        text.ShouldContain("GIT_IDENTITY_MOUNT=\"/run/antiphon/gitconfig\"");
        Block(text, "compose_host").ShouldContain("RUNNER_GIT_IDENTITY_FILE=\"$GIT_IDENTITY_PATH\" \\");

        var ensure = Block(text, "ensure_runner_git_identity");
        ensure.ShouldContain("name=\"$(stack_env_value RUNNER_GIT_USER_NAME)\"");
        ensure.ShouldContain("email=\"$(stack_env_value RUNNER_GIT_USER_EMAIL)\"");
        ensure.ShouldContain("RUNNER_GIT_USER_NAME=\"${name:-$GIT_IDENTITY_DEFAULT_NAME}\"");
        ensure.ShouldContain("RUNNER_GIT_USER_EMAIL=\"${email:-$GIT_IDENTITY_DEFAULT_EMAIL}\"");

        // Create only when missing: every write to the path sits inside the `! -e` branch, and the
        // final move cannot clobber a file that appeared meanwhile.
        const string create = "if [ ! -e \"$GIT_IDENTITY_PATH\" ]; then";
        var lines = Commands(ensure);
        var open = lines.FindIndex(line => line == create);
        open.ShouldBeGreaterThanOrEqualTo(0, "the file is created only when it does not exist");
        var close = lines.FindIndex(open + 1, line => line == "else" || line == "fi");
        close.ShouldBeGreaterThan(open);
        var writes = lines
            .Select((line, index) => (line, index))
            .Where(item => item.line.Contains("$GIT_IDENTITY_PATH", StringComparison.Ordinal))
            .Where(item => !item.line.Contains("--get", StringComparison.Ordinal)
                && !item.line.StartsWith("if [ -L ", StringComparison.Ordinal)
                && !item.line.StartsWith("if [ -d ", StringComparison.Ordinal)
                && !item.line.StartsWith("rmdir ", StringComparison.Ordinal)
                && item.line != create)
            .ToList();
        writes.ShouldNotBeEmpty();
        writes.ShouldAllBe(item => item.index > open && item.index < close,
            "an existing identity file is never rewritten");
        ensure.ShouldContain("mv -n \"$GIT_IDENTITY_PATH.tmp\" \"$GIT_IDENTITY_PATH\"");
        ensure.ShouldContain("git config --file \"$GIT_IDENTITY_PATH.tmp\" user.name \"$RUNNER_GIT_USER_NAME\"");
        ensure.ShouldContain("git config --file \"$GIT_IDENTITY_PATH.tmp\" user.email \"$RUNNER_GIT_USER_EMAIL\"");
        // uid 1654 reads the bind mount itself, so a NEW file is made readable -- on the temporary
        // file, never the destination; an incomplete file refuses rather than booting blind.
        ensure.ShouldContain("&& chmod 0644 \"$GIT_IDENTITY_PATH.tmp\" \\");
        Executable(ensure, "chmod").ShouldAllBe(line => line.Contains("\"$GIT_IDENTITY_PATH.tmp\"", StringComparison.Ordinal));
        ensure.ShouldContain("write_result false GitIdentityIncomplete 2");
        // A directory a premature bind mount left behind is removed only when empty.
        ensure.ShouldContain("rmdir \"$GIT_IDENTITY_PATH\"");
        ensure.ShouldNotContain("rm -rf");

        var deploy = Block(text, "case_deploy_parent");
        // Before compose binds it, and before stack.env (its seed values' source) is rewritten.
        var bootFiles = Block(text, "ensure_runner_boot_files");
        bootFiles.ShouldContain("ensure_runner_git_identity");
        Order(deploy, "ensure_runner_boot_files", "cat > \"$SERVER2_ENV\"").ShouldBeTrue();
        Order(deploy, "ensure_runner_boot_files", "compose_host up -d").ShouldBeTrue();
        deploy.ShouldContain("RUNNER_GIT_IDENTITY_FILE=$GIT_IDENTITY_PATH\n");
        deploy.ShouldContain("RUNNER_GIT_USER_NAME=$RUNNER_GIT_USER_NAME\n");
        deploy.ShouldContain("RUNNER_GIT_USER_EMAIL=$RUNNER_GIT_USER_EMAIL\n");

        // And the mounted file is what uid 1654 resolves, before anything is retired or accepted.
        var effective = Block(text, "verify_runner_git_identity");
        effective.ShouldContain("docker exec -u 1654:1654 \"$container\" git -C \"$where\" config --show-origin --get user.email");
        effective.ShouldContain("file:%s\\t%s' \"$GIT_IDENTITY_MOUNT\"");
        effective.ShouldContain("write_result false GitIdentityNotEffective 2");
        deploy.ShouldContain("verify_runner_git_identity \"$container\" /");
        Order(deploy, "verify_runner_git_identity", "retire_superseded_server2_images").ShouldBeTrue();
        Order(deploy, "verify_runner_git_identity", "write_result true").ShouldBeTrue();
    }

    // CARD-0631 V-7, D-10. deploy-parent accepted a runner whose repository did not exist, so the
    // first mirror crashed with Win32Exception(2). It now verifies the checkout the runner is
    // configured with, as uid 1654, INSIDE the container, and every failure refuses by name before
    // acceptance. (The fresh-volume seed that precedes it is guarded separately below.)
    [Test]
    public void Deploy_parent_seeds_or_verifies_runner_checkout()
    {
        var text = Remote();
        var verify = Block(text, "verify_runner_checkout");
        var commands = Commands(verify);

        // The runner's own repository setting, with the runner's own default and clone source.
        verify.ShouldContain("${PhoneHome__RunnerRepository:-}");
        verify.ShouldContain("repo=\"${repo:-$RUNNER_CHECKOUT_DEFAULT}\"");
        text.ShouldContain("RUNNER_CHECKOUT_DEFAULT=\"" + new global::Antiphon.SessionRunner.PhoneHomeSettings().RunnerRepository + "\"");
        text.ShouldContain("RUNNER_CHECKOUT_ORIGIN=\"" + global::Antiphon.SessionRunner.RunnerWorkspaceService.DefaultCloneSource + "\"");

        // Every probe of the checkout runs in the container as the runner uid.
        var probes = commands.Where(line => line.Contains("\"$repo", StringComparison.Ordinal)
            && line.Contains("docker exec", StringComparison.Ordinal)).ToList();
        probes.Count.ShouldBeGreaterThanOrEqualTo(5);
        probes.ShouldAllBe(line => line.Contains("docker exec -u 1654:1654 ", StringComparison.Ordinal));
        // Never the host's identically named checkout, never a child project or volume.
        verify.ShouldNotContain("$CHECKOUT");
        verify.ShouldNotContain("CHILD_PROJECT");
        verify.ShouldNotContain("docker volume");
        commands.Where(line => line.Contains("git -C", StringComparison.Ordinal))
            .ShouldAllBe(line => line.Contains("docker exec -u 1654:1654 ", StringComparison.Ordinal));

        // Missing, invalid, foreign origin and a failed anonymous fetch each refuse by name.
        Refuses(commands, "test -e \"$repo/.git\"", "RunnerCheckoutMissing");
        Refuses(commands, "rev-parse --show-toplevel", "RunnerCheckoutInvalid");
        verify.ShouldContain("if [ \"$top\" != \"$repo\" ]; then");
        Refuses(commands, "remote get-url origin", "RunnerCheckoutOriginMismatch");
        verify.ShouldContain("if [ \"$origin\" != \"$RUNNER_CHECKOUT_ORIGIN\" ]; then");
        var fetch = commands.Single(line => line.Contains(" fetch ", StringComparison.Ordinal));
        fetch.ShouldContain("-e GIT_TERMINAL_PROMPT=0");
        fetch.ShouldContain("timeout --kill-after=5s 120s git -C \"$repo\" fetch --no-tags origin \"$BRANCH\"");
        fetch.ShouldContain("|| write_result false RunnerCheckoutFetchFailed 2");
        Refuses(commands, "rev-parse FETCH_HEAD", "RunnerCheckoutFetchFailed");
        verify.ShouldNotContain("|| true");
        Executable(verify, "write_result").ShouldAllBe(line => line.Contains("write_result false ", StringComparison.Ordinal));

        // A repository-local identity (a stopgap) would outrank the mounted file: it is removed,
        // and the mount is then proven effective inside the checkout itself.
        verify.ShouldContain("config --local --unset-all \"$k\"");
        verify.ShouldContain("write_result false GitIdentityOverrideNotRemoved 2");
        verify.ShouldContain("verify_runner_git_identity \"$container\" \"$repo\"");

        // The receipt: the verified path and FETCH_HEAD, no credentials.
        verify.ShouldContain("fetch_head=%s");
        verify.ShouldContain("runner-checkout.txt");

        var deploy = Block(text, "case_deploy_parent");
        deploy.ShouldContain("verify_runner_checkout \"$container\"");
        Order(deploy, "RunnerUnhealthy", "verify_runner_checkout").ShouldBeTrue("the runner is up before it is probed");
        Order(deploy, "verify_runner_checkout", "retire_superseded_server2_images").ShouldBeTrue();
        Order(deploy, "verify_runner_checkout", "write_result true").ShouldBeTrue();
    }

    // CARD-0631 Review 012e6357 (1). A first deploy on an empty work volume always refused
    // RunnerCheckoutMissing: phone-home may still be disabled on the server at that gate, so the
    // runner's lazy clone never runs. The checkout is now seeded before the runner starts (no
    // mirror can be in flight), as uid 1654, anonymously, with RunnerWorkspaceService's command,
    // and only into an absent or empty destination; the named verification still follows.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Deploy_parent_seeds_a_fresh_runner_checkout_before_starting_the_runner()
    {
        var text = Remote();
        var seed = Block(text, "seed_runner_checkout");
        var commands = Commands(seed);

        // state-init owns the fresh volume for uid 1654 first, then a one-off of the runner image.
        var init = commands.Single(line => line.Contains("run --rm --no-deps -T state-init", StringComparison.Ordinal));
        init.ShouldContain("|| write_result false StateInitFailed 2");
        var oneOff = commands.Single(line => line.StartsWith("\"$compose\" run --rm --no-deps -T --user ", StringComparison.Ordinal));
        oneOff.ShouldBe("\"$compose\" run --rm --no-deps -T --user 1654:1654 -e GIT_TERMINAL_PROMPT=0 --entrypoint /bin/sh session-runner -c '");
        commands.IndexOf(init).ShouldBeLessThan(commands.IndexOf(oneOff));
        Order(seed, "--user 1654:1654", "git clone").ShouldBeTrue("the clone runs inside the uid-1654 one-off");
        seed.ShouldContain("git clone --filter=blob:none --no-checkout \"$2\" \"$repo\"");
        seed.ShouldContain("antiphon-seed \"$RUNNER_CHECKOUT_DEFAULT\" \"$RUNNER_CHECKOUT_ORIGIN\"");
        seed.ShouldContain("|| write_result false RunnerCheckoutSeedFailed 2");
        // Never the host's checkout, never a credential.
        seed.ShouldNotContain("$CHECKOUT");
        seed.ShouldNotContain("ssh");
        seed.ShouldNotContain("|| true");

        // Before the runner exists, after both images exist, and still verified by name.
        var deploy = Block(text, "case_deploy_parent");
        deploy.ShouldContain("\n    seed_runner_checkout\n");
        Block(text, "build_server2_images").ShouldContain("StateInitBuildFailed");
        Order(deploy, "build_server2_images", "seed_runner_checkout").ShouldBeTrue("the images are built first");
        Order(deploy, "seed_runner_checkout", "compose_host up -d").ShouldBeTrue("seeded before the runner starts");
        Order(deploy, "compose_host up -d", "verify_runner_checkout \"$container\"").ShouldBeTrue();

        // The in-container body, run for real: an empty volume is seeded from the given origin and
        // an existing or occupied destination is never touched.
        var body = System.Text.RegularExpressions.Regex.Match(seed, "-c '(?<body>[^']*)'").Groups["body"].Value;
        body.ShouldContain("repo=\"${PhoneHome__RunnerRepository:-$1}\"");
        var output = LinuxShell("SEED='" + body + "'\n" + """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            cd "$root"
            git init -q "$root/origin"
            git -C "$root/origin" -c user.name=t -c user.email=t@t commit -q --allow-empty -m init
            seed() { sh -c "$SEED" antiphon-seed "$1" "$root/origin" 2>/dev/null; echo "exit=$?"; }
            unset PhoneHome__RunnerRepository
            printf 'fresh %s\n' "$(seed "$root/work/repos/antiphon" | tr '\n' ' ')"
            printf 'origin=%s\n' "$(git -C "$root/work/repos/antiphon" remote get-url origin | sed "s#^$root#ROOT#")"
            printf 'again %s\n' "$(seed "$root/work/repos/antiphon" | tr '\n' ' ')"
            mkdir -p "$root/occupied" && touch "$root/occupied/x"
            printf 'occupied %s\n' "$(seed "$root/occupied" | tr '\n' ' ')"
            [ -e "$root/occupied/.git" ] || echo occupied-untouched
            export PhoneHome__RunnerRepository="$root/configured"
            printf 'configured %s\n' "$(seed "$root/ignored" | sed "s#$root#ROOT#" | tr '\n' ' ')"
            [ -e "$root/configured/.git" ] && [ ! -e "$root/ignored" ] && echo configured-used
            """);
        output.ShouldContain("fresh seeded path=");
        output.ShouldContain("origin=ROOT/origin\n");
        output.ShouldContain("again present path=");
        output.ShouldContain("occupied occupied path=");
        output.ShouldContain("occupied-untouched");
        output.ShouldContain("configured seeded path=ROOT/configured exit=0");
        output.ShouldContain("configured-used");
    }

    // CARD-0631 Review 012e6357 (2). persistent-restart stopped a runner deployed before the
    // identity mount existed; the start and the recovery then both failed on the new required
    // mount and the standing runner stayed down. The file is ensured while the runner is still up.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Persistent_restart_ensures_the_identity_file_before_stopping_an_older_runner()
    {
        var text = Remote();
        var restart = Block(text, "case_persistent_restart");
        Order(restart, "ensure_runner_git_identity", "compose_host stop").ShouldBeTrue();

        // The migration case, run for real: an older stack.env with no identity values and no
        // file. The compose stub refuses `up` exactly as the required bind mount would.
        var output = LinuxShell(IdentityHarness(text, "case_persistent_restart", "ensure_runner_codex_home") + CodexHomeLines(text) + """
            C604_SERVER_ORIGIN=http://127.0.0.1:9
            printf 'SOURCE_SHA12=0123456789ab\n' > "$SERVER2_ENV"
            require_lane() { :; }
            runner_container() { echo antiphon-runner-session-runner-1; }
            sleep() { :; }
            curl() { return 0; }
            docker() {
                case "$*" in
                    *runner-store-id*) echo store-1 ;;
                    *"images -q"*) echo image-1 ;;
                esac
                return 0
            }
            compose_host() {
                case "$1" in
                    stop)
                        if [ -f "$GIT_IDENTITY_PATH" ]; then echo "stop identity=present"; else echo "stop identity=missing"; fi >> "$CASE_DIR/calls.txt"
                        if [ -d "$CODEX_HOME_PATH" ] && [ "$(stat -c %a "$CODEX_HOME_PATH")" = 700 ]; then echo "stop codex-home=present"; else echo "stop codex-home=missing"; fi >> "$CASE_DIR/calls.txt"
                        ;;
                    up)
                        if [ ! -f "$GIT_IDENTITY_PATH" ] || [ -L "$GIT_IDENTITY_PATH" ]; then echo "up mount-missing" >> "$CASE_DIR/calls.txt"; return 1; fi
                        echo "up ok" >> "$CASE_DIR/calls.txt"
                        ;;
                esac
            }
            ( set -euo pipefail; case_persistent_restart )
            echo "exit=$?"
            cat "$CASE_DIR/calls.txt"
            stat -c 'identity=%a %F' "$GIT_IDENTITY_PATH"
            git config --file "$GIT_IDENTITY_PATH" --get user.name
            """);
        output.ShouldContain("stop identity=present");
        output.ShouldContain("stop codex-home=present", customMessage: "the Codex home is ensured before the older runner stops");
        output.ShouldNotContain("up mount-missing");
        output.ShouldContain("up ok");
        output.ShouldContain("RESULT accepted=true diagnosis=\n");
        output.ShouldContain("exit=0");
        output.ShouldContain("identity=644 regular file");
        output.ShouldContain("antiphon-server2-runner\n");
    }

    // CARD-0631 Review 012e6357 (3). `chmod 0644` on the identity path followed an existing
    // symlink and made its target -- possibly an adjacent 0600 secret -- world-readable. A symlink
    // now refuses before anything touches it, and 0644 is applied only to a newly created file.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Deploy_parent_refuses_a_git_identity_symlink_and_leaves_its_target_mode()
    {
        var text = Remote();
        var ensure = Commands(Block(text, "ensure_runner_git_identity"));
        ensure[2].ShouldBe("if [ -L \"$GIT_IDENTITY_PATH\" ]; then", "a symlink refuses before the path is touched");
        ensure[3].ShouldBe("write_result false GitIdentityPathIsSymlink 2");

        var output = LinuxShell(IdentityHarness(text) + """
            secret="$SERVER2_ROOT/secrets/phone-home"
            printf 'secret-bytes\n' > "$secret"
            chmod 0600 "$secret"
            ln -s phone-home "$GIT_IDENTITY_PATH"
            ( set -euo pipefail; ensure_runner_git_identity )
            echo "symlink exit=$?"
            stat -c 'target=%a' "$secret"
            printf 'target-content=%s\n' "$(cat "$secret")"
            [ -L "$GIT_IDENTITY_PATH" ] && echo link-left

            rm "$GIT_IDENTITY_PATH"
            umask 077
            ( set -euo pipefail; ensure_runner_git_identity )
            echo "fresh exit=$?"
            stat -c 'created=%a %F' "$GIT_IDENTITY_PATH"

            chmod 0600 "$GIT_IDENTITY_PATH"
            ( set -euo pipefail; ensure_runner_git_identity )
            echo "existing exit=$?"
            stat -c 'kept=%a' "$GIT_IDENTITY_PATH"
            """);
        output.ShouldContain("RESULT accepted=false diagnosis=GitIdentityPathIsSymlink\nsymlink exit=2");
        output.ShouldContain("target=600\n");
        output.ShouldContain("target-content=secret-bytes\n");
        output.ShouldContain("link-left");
        output.ShouldContain("fresh exit=0");
        output.ShouldContain("created=644 regular file");
        output.ShouldContain("existing exit=0");
        output.ShouldContain("kept=600\n", customMessage: "an operator's existing file keeps its mode");
    }

    // CARD-0660 (amended). The runner's Codex home is a DIRECTORY on server2 beside the identity
    // files, bind-mounted read-write as CODEX_HOME. deploy-parent creates it for uid 1654 at 0700
    // before anything binds it, and never reads, lists or copies what it holds; the evidence is
    // presence only. persistent-restart ensures it while an older runner is still up.
    [Test]
    public void Deploy_parent_creates_the_codex_home_directory_without_reading_it()
    {
        var text = Remote();
        text.ShouldContain("CODEX_HOME_PATH=\"$SERVER2_ROOT/secrets/codex\"\n");
        text.ShouldContain("CODEX_HOME_OWNER=\"1654:1654\"\n");
        Block(text, "compose_host").ShouldContain("RUNNER_CODEX_HOME_DIR=\"$CODEX_HOME_PATH\" \\");

        var ensure = Block(text, "ensure_runner_codex_home");
        var lines = Commands(ensure);
        // A symlink refuses before anything touches the path (an install, chown or chmod through
        // it would land on its target), and so does anything that is not a directory.
        var symlink = lines.IndexOf("if [ -L \"$CODEX_HOME_PATH\" ]; then");
        symlink.ShouldBeGreaterThan(0);
        lines[symlink + 1].ShouldBe("write_result false CodexHomePathIsSymlink 2");
        ensure.ShouldContain("write_result false CodexHomePathIsNotDirectory 2");
        var firstTouch = lines.FindIndex(line => line.StartsWith("sudo -n install ", StringComparison.Ordinal)
            || line.StartsWith("sudo -n chown ", StringComparison.Ordinal) || line.StartsWith("sudo -n chmod ", StringComparison.Ordinal));
        firstTouch.ShouldBeGreaterThan(symlink);

        // Created only when missing, owned by the runner uid at 0700 in one step.
        var create = lines.IndexOf("if [ ! -e \"$CODEX_HOME_PATH\" ]; then");
        create.ShouldBeGreaterThan(symlink);
        lines[create + 1].ShouldBe(
            "sudo -n install -d -o \"${CODEX_HOME_OWNER%:*}\" -g \"${CODEX_HOME_OWNER#*:}\" -m 0700 \"$CODEX_HOME_PATH\" 2>> \"$CASE_DIR/command.log\" || write_result false CodexHomeCreateFailed 2");
        // Owner and mode are re-asserted on the directory itself only: never recursive, never a
        // symlink's target.
        ensure.ShouldContain("sudo -n chown -h \"$CODEX_HOME_OWNER\" \"$CODEX_HOME_PATH\"");
        ensure.ShouldContain("sudo -n chmod 0700 \"$CODEX_HOME_PATH\"");
        ensure.ShouldNotContain(" -R ");
        ensure.ShouldNotContain("rm ");

        // Presence only, as evidence and as a warning. Nothing in the script reads the home.
        ensure.ShouldContain("printf 'true\\n' > \"$CASE_DIR/codex-home-present.txt\"");
        ensure.ShouldContain("if sudo -n test -e \"$CODEX_HOME_PATH/auth.json\"; then");
        ensure.ShouldContain("codex-auth-present.txt");
        ensure.ShouldContain("WARN CodexAuthAbsent");
        foreach (var line in Commands(text).Where(line => line.Contains("$CODEX_HOME_PATH", StringComparison.Ordinal)))
            System.Text.RegularExpressions.Regex.IsMatch(line,
                    @"(^|[\s;|&(])(cat|cp|mv|ls|head|tail|less|more|grep|sed|awk|tar|rsync|sha256sum|md5sum|base64|xxd|od|strings|scp)\s|<\s*""\$CODEX_HOME_PATH")
                .ShouldBeFalse("the Codex home is never read: " + line);

        // Before state-init binds it (seed_runner_checkout runs state-init) and before compose up;
        // stack.env names it for every later compose call.
        var deploy = Block(text, "case_deploy_parent");
        Block(text, "ensure_runner_boot_files").ShouldContain("ensure_runner_codex_home");
        Order(deploy, "ensure_runner_boot_files", "seed_runner_checkout").ShouldBeTrue();
        Order(deploy, "ensure_runner_boot_files", "compose_host up -d").ShouldBeTrue();
        deploy.ShouldContain("RUNNER_CODEX_HOME_DIR=$CODEX_HOME_PATH\n");

        var restart = Block(text, "case_persistent_restart");
        Order(restart, "ensure_runner_codex_home", "compose_host stop").ShouldBeTrue();
    }

    // The same function, run for real over a throwaway server2 root with sudo reduced to a plain
    // call and the owner reduced to the current uid. The sentinel is not a credential.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Deploy_parent_codex_home_refuses_a_symlink_or_file_and_keeps_contents()
    {
        var text = Remote();
        var output = LinuxShell(CodexHomeHarness(text) + """
            ( set -euo pipefail; ensure_runner_codex_home ) 2>&1
            echo "fresh exit=$?"
            [ "$(stat -c '%u:%g' "$CODEX_HOME_PATH")" = "$CODEX_HOME_OWNER" ] && echo fresh-owner-ok
            stat -c 'fresh=%a %F' "$CODEX_HOME_PATH"
            printf 'fresh-state=%s auth=%s\n' "$(cat "$CASE_DIR/codex-home-state.txt")" "$(cat "$CASE_DIR/codex-auth-present.txt")"

            printf 'c660-sentinel-not-a-credential\n' > "$CODEX_HOME_PATH/auth.json"
            chmod 0640 "$CODEX_HOME_PATH/auth.json"
            chmod 0755 "$CODEX_HOME_PATH"
            ( set -euo pipefail; ensure_runner_codex_home )
            echo "existing exit=$?"
            stat -c 'existing=%a' "$CODEX_HOME_PATH"
            stat -c 'sentinel=%a' "$CODEX_HOME_PATH/auth.json"
            printf 'sentinel-bytes=%s\n' "$(cat "$CODEX_HOME_PATH/auth.json")"
            printf 'existing-state=%s home=%s auth=%s\n' "$(cat "$CASE_DIR/codex-home-state.txt")" \
                "$(cat "$CASE_DIR/codex-home-present.txt")" "$(cat "$CASE_DIR/codex-auth-present.txt")"
            grep -rq c660-sentinel "$CASE_DIR" || echo evidence-holds-no-contents

            mv "$CODEX_HOME_PATH" "$root/elsewhere"
            chmod 0755 "$root/elsewhere"
            chmod 0755 "$root/elsewhere"
            ln -s "$root/elsewhere" "$CODEX_HOME_PATH"
            ( set -euo pipefail; ensure_runner_codex_home )
            echo "symlink exit=$?"
            stat -c 'target=%a' "$root/elsewhere"
            [ -L "$CODEX_HOME_PATH" ] && echo link-left

            rm "$CODEX_HOME_PATH"
            printf 'not-a-directory\n' > "$CODEX_HOME_PATH"
            chmod 0644 "$CODEX_HOME_PATH"
            ( set -euo pipefail; ensure_runner_codex_home )
            echo "file exit=$?"
            stat -c 'file=%a %F' "$CODEX_HOME_PATH"

            rm "$CODEX_HOME_PATH"
            ( set -euo pipefail; LANE=nested; ensure_runner_codex_home )
            echo "nested exit=$?"
            [ -e "$CODEX_HOME_PATH" ] || echo nested-created-nothing
            """);
        output.ShouldContain("fresh exit=0");
        output.ShouldContain("fresh-owner-ok");
        output.ShouldContain("fresh=700 directory\n");
        output.ShouldContain("fresh-state=created auth=false\n");
        output.ShouldContain("WARN CodexAuthAbsent");
        output.ShouldContain("existing exit=0");
        output.ShouldContain("existing=700\n");
        output.ShouldContain("sentinel=640\n", customMessage: "the home's contents are never re-moded");
        output.ShouldContain("sentinel-bytes=c660-sentinel-not-a-credential\n");
        output.ShouldContain("existing-state=kept home=true auth=true\n");
        output.ShouldContain("evidence-holds-no-contents");
        output.ShouldContain("RESULT accepted=false diagnosis=CodexHomePathIsSymlink\nsymlink exit=2");
        output.ShouldContain("target=755\n", customMessage: "a symlink's target is never chowned or re-moded");
        output.ShouldContain("link-left");
        output.ShouldContain("RESULT accepted=false diagnosis=CodexHomePathIsNotDirectory\nfile exit=2");
        output.ShouldContain("file=644 regular file\n");
        output.ShouldContain("RESULT accepted=false diagnosis=CodexHomeHostLaneOnly\nnested exit=2");
        output.ShouldContain("nested-created-nothing");
    }

    // ensure_dirs resets the host-lane trees to mc on every host case. Recursing into the Codex home
    // would hand the runner's live sign-in to mc (and read every entry under it): the reset now
    // prunes it, run for real with chown swapped for a print of what it would visit.
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void Host_lane_ownership_reset_never_enters_the_codex_home()
    {
        var text = Remote();
        var ensure = Commands(EnsureDirsBody(text));
        ensure.ShouldNotContain(line => line.Contains("chown -R", StringComparison.Ordinal)
            && line.Contains("$SERVER2_ROOT", StringComparison.Ordinal), "a recursive chown over the server2 root");
        var reset = ensure.Single(line => line.Contains("-prune", StringComparison.Ordinal));
        reset.ShouldBe("sudo -n find \"$SERVER2_ROOT\" \\( -path \"$CODEX_HOME_PATH\" -o -path \"$SERVER2_ROOT/cache\" \\) -prune -o -exec chown -h mc:mc {} +");

        var visit = reset.Replace("sudo -n ", "", StringComparison.Ordinal)
            .Replace("-exec chown -h mc:mc {} +", "-exec printf 'visit %s\\n' {} +", StringComparison.Ordinal);
        var output = LinuxShell(string.Join('\n',
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            "SERVER2_ROOT=\"$root/s2\"",
            text.Replace("\r\n", "\n").Split('\n').Single(line => line.StartsWith("CODEX_HOME_PATH=", StringComparison.Ordinal)),
            "mkdir -p \"$CODEX_HOME_PATH/sessions\"",
            "touch \"$SERVER2_ROOT/secrets/gitconfig\" \"$CODEX_HOME_PATH/auth.json\"",
            visit,
            "") + "\n").Replace("\r\n", "\n");
        output.ShouldContain("/s2\n");
        output.ShouldContain("/s2/secrets\n");
        output.ShouldContain("/s2/secrets/gitconfig\n");
        output.ShouldNotContain("/s2/secrets/codex");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Cache_cases_use_only_the_validated_host_lane()
    {
        RequireLinuxPwsh();
        var remote = Remote();
        var bridge = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/c590-real.ps1"));
        var front = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/verify-card0849-caches.ps1"));
        foreach (var name in new[] { "runner-cache-inventory", "runner-cache-fixture", "runner-cache-seed", "runner-cache-reset",
                     "verify-runner-caches", "verify-runner-caches-retired", "runner-cache-prune-preview", "runner-cache-prune" })
        {
            bridge.ShouldContain("'" + name + "'");
            remote.ShouldContain(name + ")");
        }
        remote.ShouldContain("if [ \"$LANE\" != host ]; then printf 'DIAGNOSIS=WrongLane");
        remote.ShouldContain("c849_evidence_dir");
        bridge.ShouldContain("CacheEvidencePathInvalid");
        bridge.ShouldContain("CachePreviewInvalid");
        front.ShouldContain("C849_DEPLOY_SHA");
        front.ShouldContain("CachePreviewInvalid");
        var result = LinuxShell("root='" + DelegateScriptRunner.RepoRoot.Replace("'", "'\\''") + "'\n" +
            "pwsh -NoProfile -File \"$root/scripts/verify-card0849-caches.ps1\" -Case Inventory -Sha bad 2>&1 || true\n");
        result.ShouldContain("C849_DEPLOY_SHA must be the reviewed full lowercase SHA");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Cache_prepare_is_idempotent_and_preserves_payloads()
    {
        var output = LinuxShell(CachePrepareHarness() + """
            set -e
            c849_prepare yes
            for n in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
                printf 'sentinel-%s\n' "$n" > "$root/volumes/$n/payload"
                chmod 0700 "$root/volumes/$n/payload"
            done
            printf 'sibling\n' > "$root/sibling"
            mkdir -p "$(dirname "$C849_READY")"; printf 'accepted\n' > "$C849_READY"
            c849_prepare yes
            for n in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
                [ "$(cat "$root/volumes/$n/payload")" = "sentinel-$n" ]
                [ "$(stat -c %a "$root/volumes/$n/payload")" = 700 ]
                [ -z "$(find "$root/volumes/$n" -name '.c849-probe-*' -print -quit)" ]
                echo "preserved $n"
            done
            [ "$(cat "$root/sibling")" = sibling ]
            echo sibling-preserved
            """);
        foreach (var role in new[] { "nuget-packages", "nuget-scratch", "npm-content" })
            output.ShouldContain("preserved antiphon-runner-cache-" + role);
        output.ShouldContain("sibling-preserved");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Cache_prepare_refuses_foreign_or_unsafe_roots()
    {
        var output = LinuxShell(CachePrepareHarness() + """
            for fault in driver options owner schema role mode symlink file unmarked name; do
                rm -rf "$root/volumes"; mkdir -p "$root/volumes"
                FAULT="$fault"
                if [ "$fault" = unmarked ]; then
                    mkdir -p "$root/volumes/$C849_PACKAGES"
                    printf 'keep\n' > "$root/volumes/$C849_PACKAGES/payload"
                fi
                target="$C849_PACKAGES"; [ "$fault" = name ] && target=foreign-cache
                ( set -e; c849_volume "$target" nuget-packages yes image ) 2>&1
                echo "$fault exit=$?"
                if [ "$fault" = unmarked ]; then
                    [ "$(cat "$root/volumes/$C849_PACKAGES/payload")" = keep ] && echo unmarked-preserved
                fi
            done
            """);
        foreach (var fault in new[] { "driver", "options", "owner", "schema", "role", "mode", "symlink", "file", "unmarked", "name" })
            output.ShouldContain(fault + " exit=2");
        output.ShouldContain("unmarked-preserved");
        var lookup = LinuxShell(Block(Remote(), "c849_empty_volume") + "\n" + """
            docker() { return 1; }
            c849_empty_volume cache image
            printf 'empty-on-docker-error=%s\n' "$?"
            """);
        lookup.ShouldContain("empty-on-docker-error=1");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Prune_refuses_stale_or_busy_authority()
    {
        // The second target is an escaping symlink. The first target has a prune
        // candidate, so this catches a delete-before-whole-set-validation bug.
        var output = LinuxShell(CachePruneHarness() + """
            mkdir -p "$root/volumes/packages" "$root/volumes/scratch" "$root/volumes/npm"
            printf 'untouched\n' > "$root/volumes/packages/sentinel"
            mkdir -p "$root/outside"
            ln -s "$root/outside" "$root/volumes/scratch-link"
            make_preview "$root/volumes/scratch-link"
            ( c849_prune ) > "$root/verdict" 2>&1
            code=$?
            printf 'exit=%s\n' "$code"
            cat "$root/verdict"
            [ "$(cat "$root/volumes/packages/sentinel")" = untouched ] && echo package-retained
            [ -z "$(grep -E '^(run|volume create)' "$root/docker-trace")" ] && echo no-docker-mutation
            [ ! -e "$root/outside/sentinel" ] && echo sibling-retained
            """);
        output.ShouldContain("exit=2");
        output.ShouldContain("CacheTargetInvalid");
        output.ShouldContain("package-retained");
        output.ShouldContain("no-docker-mutation");
        output.ShouldContain("sibling-retained");
        var reset = LinuxShell(CacheStatusHarness() + "\n" +
            Block(Remote(), "c849_prune_idle") + "\n" + Block(Remote(), "c849_reset") + "\n" + """
            SERVER2_ROOT="$root/server2"; mkdir -p "$SERVER2_ROOT/cache" "$root/volumes"
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            C849_PACKAGES=packages; C849_SCRATCH=scratch; C849_NPM=npm
            RUN=red; LANE=host; HOST_PROJECT=main; TEMP_PROJECT=temp
            for name in packages scratch npm; do
                mkdir -m 700 "$root/volumes/$name"
                printf 'partial\n' > "$root/volumes/$name/payload"
            done
            printf 'keep\n' > "$root/sibling"
            require_lane() { :; }; c849_lock() { :; }; c849_image() { echo image; }
            c849_volume() { [ "$LABELS_OK" = yes ] || write_result false CacheVolumeForeign 2; }
            c849_empty_volume() { [ -z "$(find "$root/volumes/$1" -mindepth 1 -print -quit)" ]; }
            compose_host() { echo broker; }
            write_result() { printf 'reset-result=%s:%s\n' "$1" "$2"; exit "$3"; }
            docker() {
                local name
                case "$1" in
                    ps)
                        [ "$PS_FAIL" = yes ] && return 1
                        if [[ "$*" == *volume=* ]] && [ "$IN_USE" = yes ]; then echo consumer
                        elif [[ "$*" == *volume=* ]] && [ "$2" = -aq ] && [ "$STOPPED_ONLY" = yes ]; then echo consumer
                        elif [[ "$*" == *'project=main'* ]]; then echo main; fi ;;
                    exec)
                        if [ "$2" = main ]; then echo 0
                        elif [ "$BROKER_BUSY" = yes ]; then echo busy
                        else echo zero; fi ;;
                    run)
                        if [[ "$*" =~ source=([^,]+),target=/cache ]] && [[ "$*" == *'find /cache -mindepth 1 -maxdepth 1 -exec rm -rf'* ]]; then
                            name="${BASH_REMATCH[1]}"
                            rm -f "$root/volumes/$name/payload"
                            return 0
                        fi
                        return 1 ;;
                esac
                return 0
            }
            for fault in sessions null unknown broker inuse stopped lookup foreign marker success; do
                STATUS=zero; BROKER_BUSY=no; IN_USE=no; STOPPED_ONLY=no; LABELS_OK=yes; PS_FAIL=no
                case "$fault" in
                    sessions|null|unknown) STATUS="$fault" ;;
                    broker) BROKER_BUSY=yes ;;
                    inuse) IN_USE=yes ;;
                    stopped) STOPPED_ONLY=yes ;;
                    lookup) PS_FAIL=yes ;;
                    foreign) LABELS_OK=no ;;
                    marker) printf 'ready\n' > "$C849_READY" ;;
                esac
                ( c849_reset ) > "$root/result" 2>&1
                printf '%s verdict=%s\n' "$fault" "$(cat "$root/result")"
                if [ "$fault" != success ]; then
                    [ -f "$root/volumes/packages/payload" ] || echo unsafe-clear
                fi
                rm -f "$C849_READY"
            done
            [ ! -e "$root/volumes/packages/payload" ] && echo cleared
            [ "$(stat -c %a "$root/volumes/packages")" = 700 ] && echo root-retained
            [ "$(cat "$root/sibling")" = keep ] && echo reset-sibling-retained
            """);
        foreach (var (fault, diagnosis) in new[] {
            ("sessions", "CacheConsumersBusy"), ("null", "CacheConsumersBusy"),
            ("unknown", "CacheConsumersBusy"), ("broker", "CacheBuildSlotsBusy"),
            ("inuse", "CacheConsumersBusy"), ("stopped", "CacheResetInUse"), ("lookup", "CacheConsumerUnknown"),
            ("foreign", "CacheVolumeForeign"),
            ("marker", "CacheSeedAlreadyReady") })
            reset.ShouldContain(fault + " verdict=reset-result=false:" + diagnosis);
        reset.ShouldContain("success verdict=reset-result=true:");
        reset.ShouldContain("cleared");
        reset.ShouldContain("root-retained");
        reset.ShouldContain("reset-sibling-retained");
        reset.ShouldNotContain("unsafe-clear");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Seed_publishes_complete_payloads_before_its_marker()
    {
        RequireLinuxPwsh();
        var output = LinuxShell(CacheSeedTreeHarness() + """
            before="$(sha256sum "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)"
            mkdir -p "$tree/packages/unrelated/1.0.0"
            printf 'partial\n' > "$tree/packages/unrelated/1.0.0/partial"
            c849_validate_seed_tree "$tree"
            after="$(sha256sum "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)"
            [ "$before" = "$after" ] && echo payload-hash-preserved
            [ "$(stat -c %a "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost")" = 755 ] && echo executable-preserved
            [ ! -e "$tree/packages/unrelated/1.0.0" ] && echo incomplete-excluded
            [ -s "$tree/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" ] && echo reference-complete
            """);
        output.ShouldContain("payload-hash-preserved");
        output.ShouldContain("executable-preserved");
        output.ShouldContain("incomplete-excluded");
        output.ShouldContain("reference-complete");
        var seed = Block(Remote(), "c849_seed");
        Order(seed, "docker stop \"$donor\"", "docker cp \"$donor:/home/app/.nuget/packages/.\"").ShouldBeTrue();
        Order(seed, "c849_smoke \"$helper\" seed", "mv \"$stage\" \"$recovery\"").ShouldBeTrue();
        Order(seed, "docker start \"$donor\"", "mv \"$C849_READY.tmp-$RUN\" \"$C849_READY\"").ShouldBeTrue();
        var status = LinuxShell(CacheStatusHarness() + "\n" + """
            for STATUS in sessions runnerSessions queuedTasks null garbage unknown; do
                c849_status_zero server2-temp reconnected
                printf '%s reconnect=%s\n' "$STATUS" "$?"
            done
            STATUS=zero
            c849_status_zero server2-temp reconnected
            printf 'zero reconnect=%s\n' "$?"
            DISPATCH=bad
            jq() {
                local opt="$1" expression="$2" input
                input="$(cat)"
                if [[ "$expression" == *dispatchEligible* ]]; then return 1; fi
                [ "$input" = zero ]
            }
            c849_status_zero server2-temp reconnected
            printf 'dispatch-unknown reconnect=%s\n' "$?"
            """);
        foreach (var fault in new[] { "sessions", "runnerSessions", "queuedTasks", "null", "garbage", "unknown" })
            status.ShouldContain(fault + " reconnect=1");
        status.ShouldContain("zero reconnect=0");
        status.ShouldContain("dispatch-unknown reconnect=1");
        var ready = LinuxShell(CacheStatusHarness() + "\n" + seed + "\n" + """
            SERVER2_ROOT="$root/server2"; mkdir -p "$SERVER2_ROOT/cache"
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"; printf 'ready\n' > "$C849_READY"
            RUN=red; LANE=host
            require_lane() { :; }
            write_result() { printf 'seed-result=%s:%s\n' "$1" "$2"; exit "$3"; }
            c849_prepare() { :; }; c849_image() { echo image; }
            c849_optional_donor() { :; }; c849_no_temp_containers() { :; }; c849_require_ready() { :; }
            docker() { echo unsafe-docker; return 1; }
            ( c849_seed )
            """);
        ready.ShouldContain("seed-result=true:");
        ready.ShouldNotContain("unsafe-docker");
        var wrapper = LinuxShell("repo='" + DelegateScriptRunner.RepoRoot.Replace("'", "'\\''") + "'\n" + """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            export ANTIPHON_OPERATOR_TOKEN_FILE="$root/operator-token"
            printf 'synthetic-test-token' > "$ANTIPHON_OPERATOR_TOKEN_FILE"
            export C727_TEST_HTTP_STUB="$repo/scripts/fixtures/c727-fake-http.ps1"
            export C727_TEST_VERIFY_STUB="$repo/scripts/fixtures/c727-fake-verify.ps1"
            export C727_TEST_WAIT_MS=100 C727_TEST_POLL_MS=5
            export C727_TEST_STATE="$root/state.json" C727_TEST_TRACE="$root/trace.jsonl"
            sha=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            for container in false true; do
                printf '{"scenario":"retired","sha":"%s","tempDeployed":false,"oldDeployed":false,"oldDraining":false,"tempDraining":true,"tempRedirectTo":"server2","tempRetireWhenIdle":true,"tempRetiredAt":"2026-09-27T10:00:00Z","tempContainer":%s,"tempOffline":true,"faultRunner":"","faultField":"","faultKind":"","faultValue":null,"failVerify":""}\n' "$sha" "$container" > "$C727_TEST_STATE"
                : > "$C727_TEST_TRACE"
                pwsh -NoProfile -File "$repo/scripts/deploy-server2.ps1" -Rolling -Sha "$sha" -Phase deploy-temp > "$root/out" 2>&1
                code=$?
                printf 'retired-container=%s exit=%s cases=%s\n' "$container" "$code" "$(sed -n 's/.*"kind":"case","name":"\([^"]*\)".*/\1/p' "$C727_TEST_TRACE" | paste -sd, -)"
            done
            """);
        wrapper.ShouldContain("retired-container=false exit=0 cases=runner-cache-seed,deploy-temp-runner,verify-runner-caches");
        wrapper.ShouldContain("retired-container=true exit=2 cases=\n");
        wrapper.ShouldNotContain("retired-container=true exit=2 cases=runner-cache-seed");
        var noContainer = LinuxShell(CacheStatusHarness() + "\n" + Block(Remote(), "c849_no_temp_containers") + "\n" + seed + "\n" + """
            SERVER2_ROOT="$root/server2"; mkdir -p "$SERVER2_ROOT/cache"
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"; printf 'ready\n' > "$C849_READY"
            RUN=red; LANE=host
            TEMP_PROJECT=antiphon-runner-temp
            require_lane() { :; }
            c849_prepare() { :; }; c849_image() { echo image; }
            c849_optional_donor() { :; }; c849_require_ready() { :; }
            write_result() { printf 'host-refusal=%s\n' "$2"; exit "$3"; }
            docker() {
                [ "$1" = ps ] || return 1
                [ "$TEMP_CONTAINER" = yes ] && printf 'leftover-container\n'
                return 0
            }
            TEMP_CONTAINER=no
            ( c849_seed ); printf 'host-empty=%s\n' "$?"
            TEMP_CONTAINER=yes
            ( c849_seed ); printf 'host-present=%s\n' "$?"
            """);
        noContainer.ShouldContain("host-empty=0");
        noContainer.ShouldContain("host-refusal=CacheTempContainerExists");
        noContainer.ShouldContain("host-present=2");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Seed_refuses_invalid_donors_and_partial_payloads()
    {
        var output = LinuxShell(CacheSeedTreeHarness() + """
            for fault in host metadata reference symlink hardlink special; do
                copy="$root/$fault"; cp -a "$tree" "$copy"
                case "$fault" in
                    host) rm "$copy/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" ;;
                    metadata) : > "$copy/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ;;
                    reference) : > "$copy/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" ;;
                    symlink) ln -s "$root/outside" "$copy/packages/escape" ;;
                    hardlink) ln "$copy/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata" "$copy/packages/escape" ;;
                    special) mkfifo "$copy/packages/escape" ;;
                esac
                diagnosis="$(c849_validate_seed_tree "$copy")"; code=$?
                printf '%s code=%s diagnosis=%s\n' "$fault" "$code" "$diagnosis"
            done
            for path in '../escape' '/tmp/escape'; do
                diagnosis="$(c849_validate_seed_relative "$path")"; code=$?
                printf 'path code=%s diagnosis=%s\n' "$code" "$diagnosis"
            done
            [ "$(cat "$root/sibling")" = keep ] && echo sibling-preserved
            """);
        foreach (var expected in new[] {
            "host code=2 diagnosis=AppHostDonorMissing", "metadata code=2 diagnosis=AppHostDonorMetadataMissing",
            "reference code=2 diagnosis=Net9ReferenceDonorMissing", "symlink code=2 diagnosis=CacheDonorUnsafeEntry",
            "hardlink code=2 diagnosis=CacheDonorUnsafeEntry", "special code=2 diagnosis=CacheDonorUnsafeEntry",
            "path code=2 diagnosis=CacheDonorUnsafePath", "sibling-preserved" })
            output.ShouldContain(expected);
        var gate = LinuxShell(CacheStatusHarness() + "\n" + Block(Remote(), "c849_seed") + "\n" + """
            c849_prepare() { :; }
            c849_image() { echo image; }
            c849_optional_donor() { echo donor; }
            c849_empty_volume() { :; }
            docker() { printf 'docker %s\n' "$*" >> "$root/docker-trace"; return 1; }
            c849_seed_failure() { write_result false "$2" 2; }
            sudo() { :; }
            SERVER2_ROOT="$root/server2"; mkdir -p "$SERVER2_ROOT/cache"
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            C849_PACKAGES=packages; C849_SCRATCH=scratch; C849_NPM=npm
            RUN=red; LANE=host
            require_lane() { :; }
            write_result() { printf 'seed-result=%s:%s\n' "$1" "$2"; exit "$3"; }
            for STATUS in sessions runnerSessions queuedTasks null garbage unknown; do
                : > "$root/docker-trace"
                ( c849_seed ) > "$root/result" 2>&1
                printf '%s verdict=%s\n' "$STATUS" "$(cat "$root/result")"
                if grep -q '^docker stop' "$root/docker-trace"; then echo unsafe-stop; fi
            done
            """);
        foreach (var fault in new[] { "sessions", "runnerSessions", "queuedTasks", "null", "garbage", "unknown" })
            gate.ShouldContain(fault + " verdict=seed-result=false:CacheDonorNotIdleDrained");
        gate.ShouldNotContain("unsafe-stop");
        var donorLookup = LinuxShell(Block(Remote(), "c849_optional_donor") + "\n" + """
            TEMP_PROJECT=temp
            write_result() { printf 'lookup-result=%s\n' "$2"; exit "$3"; }
            docker() {
                [ "$1" = ps ] || return 1
                case "$LOOKUP" in
                    error) return 1 ;;
                    duplicate) printf 'one\ntwo\n' ;;
                    absent) : ;;
                esac
            }
            for LOOKUP in absent error duplicate; do
                ( c849_optional_donor ) > "$LOOKUP.out" 2>&1
                printf '%s code=%s verdict=%s\n' "$LOOKUP" "$?" "$(cat "$LOOKUP.out")"
                rm -f "$LOOKUP.out"
            done
            """);
        donorLookup.ShouldContain("absent code=0 verdict=");
        donorLookup.ShouldContain("error code=2 verdict=lookup-result=CacheDonorLookupFailed");
        donorLookup.ShouldContain("duplicate code=2 verdict=lookup-result=CacheDonorIdentityInvalid");
        var cleanup = LinuxShell("""
            root="$(mktemp -d)"; trap 'rm -rf "$root"' EXIT
            SERVER2_ROOT="$root/server2"; RUN=red
            mkdir -p "$SERVER2_ROOT/cache"
            stage="$(mktemp -d "$SERVER2_ROOT/cache/stage-$RUN-XXXXXXXX")"
            printf 'partial\n' > "$stage/payload"
            printf 'keep\n' > "$root/sibling"
            write_result() { printf 'seed-failure=%s\n' "$2"; exit "$3"; }
            """ + "\n" + Block(Remote(), "c849_seed_failure") + "\n" + """
            ( c849_seed_failure '' Interrupted )
            [ -e "$stage" ] && echo stage-left || echo stage-cleaned
            [ "$(cat "$root/sibling")" = keep ] && echo cleanup-sibling-retained
            """);
        cleanup.ShouldContain("seed-failure=Interrupted");
        cleanup.ShouldContain("stage-cleaned");
        cleanup.ShouldContain("cleanup-sibling-retained");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Saved_donor_archive_is_checked_and_imported_without_a_container()
    {
        RequireLinuxPwsh();
        var remote = Remote();
        var output = LinuxShell("repo='" + DelegateScriptRunner.RepoRoot.Replace("'", "'\\''") + "'\n" +
            CacheSeedTreeHarness() + "\n" + Block(remote, "c849_saved_copy") + "\n" +
            Block(remote, "c849_no_cache_attachments") + "\n" +
            Block(remote, "c849_seed_failure") + "\n" + Block(remote, "c849_seed") + "\n" +
            Block(remote, "case_deploy_parent") + "\n" + Block(remote, "case_deploy_temp_runner") + "\n" + """
            SERVER2_ROOT="$root/server2"; CASE_DIR="$root/case"
            mkdir -p "$SERVER2_ROOT/cache" "$CASE_DIR" "$root/volumes/packages" "$root/volumes/npm"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            C849_PACKAGES=packages; C849_SCRATCH=scratch; C849_NPM=npm
            RUN=red; LANE=host; TEMP_PROJECT=temp
            mkdir -p "$tree/packages/incomplete/1.0"
            printf 'unfinished\n' > "$tree/packages/incomplete/1.0/payload"
            chmod 4755 "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
            tar -cf "$root/donor.tar" -C "$tree" .
            C590_SAVED_DONOR="$root/donor.tar"
            c849_prepare() { :; }; c849_image() { echo image; }
            c849_optional_donor() { :; }; c849_no_temp_containers() { :; }
            c849_prune_idle() { printf 'idle\n' >> "$root/trace"; }
            c849_empty_volume() { :; }
            c849_smoke() { printf 'smoke\n' >> "$root/trace"; }
            c849_status_body() { printf '{"sessions":0,"runnerSessions":null,"queuedTasks":0,"draining":true,"retireWhenIdle":true,"redirectTo":"server2","dispatchEligible":false,"acceptingNewWork":false}\n'; }
            jq() { cat; }
            sudo() { mkdir -p "${@: -1}"; }
            docker() {
                case "$1:$2" in
                    image:inspect) printf 'sha256:%064d\n' 0; return 0 ;;
                    volume:inspect) echo "$root/state"; return 0 ;;
                    ps:*) return 0 ;;
                    run:*)
                        if [[ "$*" == *'--entrypoint pwsh'* ]]; then
                            local expected="$(id -u):$(id -g)"
                            [[ "$*" == *"--user $expected"* ]] || { echo CacheImportOwnerInvalid; return 2; }
                            pwsh -NoProfile -File "$repo/scripts/c849-import-saved-donor.ps1" -Source "$C590_SAVED_DONOR" -Stage "$stage"
                            return $?
                        fi
                        if [[ "$*" == *'cache verify'* ]]; then return 0; fi
                        if [[ "$*" == *'cp -a /seed/.'* ]]; then
                            local source='' target='' arg
                            for arg in "$@"; do
                                case "$arg" in
                                    type=bind,source=*) source="${arg#*source=}"; source="${source%%,*}" ;;
                                    type=volume,source=*) target="${arg#*source=}"; target="${target%%,*}" ;;
                                esac
                            done
                            cp -a "$source/." "$root/volumes/$target/"
                            if [[ "$*" == *'chown -R 1654:1654 /cache'* ]]; then
                                printf '1654\n' > "$root/volumes/$target/.owner"
                            else
                                printf '0\n' > "$root/volumes/$target/.owner"
                            fi
                            return 0
                        fi
                        if [[ "$*" == *'--entrypoint sleep'* ]]; then echo helper; return 0; fi
                        return 0 ;;
                    rm:*) return 0 ;;
                esac
                return 1
            }
            require_lane() { :; }
            write_result() { printf 'seed-result=%s:%s\n' "$1" "$2"; exit "$3"; }
            ( c849_seed ) > "$root/result" 2>&1
            printf 'success=%s %s\n' "$?" "$(cat "$root/result")"
            test -s "$C849_READY" && echo marker-written
            grep -q '^donor=saved$' "$C849_READY" && echo saved-identity
            test -s "$root/volumes/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" && echo payload-imported
            test -d "$SERVER2_ROOT/cache/recovery-$RUN" && echo recovery-retained
            test "$(stat -c %u "$SERVER2_ROOT/cache/recovery-$RUN/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost")" = "$(id -u)" && echo recovery-host-owned
            test "$(stat -c %a "$SERVER2_ROOT/cache/recovery-$RUN/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost")" = 755 && echo unsafe-mode-masked
            test ! -e "$SERVER2_ROOT/cache/recovery-$RUN/packages/incomplete/1.0" && echo incomplete-pruned
            grep -q '^1654$' "$root/volumes/packages/.owner" && grep -q '^1654$' "$root/volumes/npm/.owner" && echo live-cache-owned-by-1654
            printf 'idle-count=%s smoke-count=%s\n' "$(grep -c '^idle$' "$root/trace")" "$(grep -c '^smoke$' "$root/trace")"
            mv "$C849_READY" "$root/accepted-marker"
            RUN=smoke
            c849_smoke() { return 1; }
            ( c849_seed ) > "$root/smoke-result" 2>&1
            printf 'smoke-exit=%s diagnosis=%s\n' "$?" "$(cat "$root/smoke-result")"
            test ! -e "$C849_READY" && echo smoke-no-ready-marker
            if compgen -G "$SERVER2_ROOT/cache/stage-$RUN-*" > /dev/null; then echo smoke-stage-left; else echo smoke-stage-cleaned; fi
            mv "$root/accepted-marker" "$C849_READY"
            mkdir -p "$root/directory-stage/packages" "$root/directory-stage/npm"
            C590_SAVED_DONOR="$tree"
            c849_saved_copy "$tree/" "$root/directory-stage" image
            test -s "$root/directory-stage/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" && echo directory-imported
            SERVER2_ENV="$root/main.env"; printf 'ready\n' > "$SERVER2_ENV"
            mkdir -p "$root/state/grok"
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; HOST_PROJECT=main
            C604_SERVER_ORIGIN=https://example.invalid
            DEPLOY_KEY=x; PHONE_HOME_SECRET=x; CLAUDE_OAUTH_TOKEN_PATH=x
            GIT_IDENTITY_PATH=x; CODEX_HOME_PATH=x
            RUNNER_GIT_USER_NAME=test; RUNNER_GIT_USER_EMAIL=test@example.invalid
            ensure_checkout() { :; }; ensure_runner_boot_files() { :; }
            retire_c590_leftovers() { :; }; broker_sha12() { echo aaaaaaaaaaaa; }
            c849_no_temp_containers() { :; }
            build_server2_images() { :; }
            c849_require_ready() { test -s "$C849_READY" || write_result false CacheSeedRequired 2; }
            c849_budget_gate() { write_result false PastSeedGate 2; }
            sudo() {
                [ "$1" = -n ] && shift
                if [ "$1" = test ]; then shift; test "$@"
                elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nstate 30000000 1 25000000 1%% /state\n'
                else mkdir -p "${@: -1}"; fi
            }
            for target in parent temp; do
                ( case_deploy_$([ "$target" = parent ] && echo parent || echo temp_runner) ) > "$root/deploy" 2>&1
                printf 'deploy-%s=%s\n' "$target" "$(cat "$root/deploy")"
            done
            """);
        foreach (var expected in new[] { "success=0 seed-result=true:", "marker-written", "saved-identity",
            "payload-imported", "recovery-retained", "recovery-host-owned", "unsafe-mode-masked",
            "incomplete-pruned", "live-cache-owned-by-1654", "idle-count=3 smoke-count=1",
            "smoke-exit=2 diagnosis=seed-result=false:CacheSeedSmokeFailed", "smoke-no-ready-marker", "smoke-stage-cleaned", "directory-imported",
            "deploy-parent=seed-result=false:PastSeedGate", "deploy-temp=seed-result=false:PastSeedGate" })
            output.ShouldContain(expected);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Saved_donor_rejects_unsafe_archives_missing_pack_and_busy_counters()
    {
        RequireLinuxPwsh();
        var remote = Remote();
        var output = LinuxShell("repo='" + DelegateScriptRunner.RepoRoot.Replace("'", "'\\''") + "'\n" +
            CacheSeedTreeHarness() + "\n" + Block(remote, "c849_saved_copy") + "\n" +
            Block(remote, "c849_prune_idle") + "\n" +
            Block(remote, "c849_no_cache_attachments") + "\n" + """
            SERVER2_ROOT="$root/server2"; mkdir -p "$SERVER2_ROOT/cache"
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"
            RUN=red; CASE=runner-cache-seed; HOST_PROJECT=main; TEMP_PROJECT=temp
            C849_PACKAGES=packages; C849_SCRATCH=scratch; C849_NPM=npm
            write_result() { printf 'refusal=%s\n' "$2"; exit "$3"; }
            compose_host() { echo broker; }
            c849_status_body() { if [ "$1" = server2-temp ]; then printf 'temp-retired'; else printf '%s' "$STATUS"; fi; }
            jq() {
                local body; body="$(cat)"
                case "$body" in
                    main-zero|broker-idle) return 0 ;;
                    temp-retired)
                        [[ "$*" == *'.runnerSessions != null'* ]] && return 1
                        return 0 ;;
                    *) return 1 ;;
                esac
            }
            docker() {
                if [ "$1" = run ] && [[ "$*" == *'--entrypoint pwsh'* ]]; then
                    pwsh -NoProfile -File "$repo/scripts/c849-import-saved-donor.ps1" -Source "$SOURCE" -Stage "$STAGE"
                    return $?
                fi
                if [ "$1" = ps ]; then
                    [ "$PS_ERROR" = yes ] && return 1
                    if [[ "$*" == *volume=* ]] && [ "$IN_USE" = yes ]; then echo consumer; return 0; fi
                    [[ "$*" == *'project=main'* ]] && echo main
                    return 0
                fi
                if [ "$1" = exec ]; then
                    [ "$2" = main ] && echo 0 || echo broker-idle
                    return 0
                fi
                return 1
            }
            tar -cf "$root/good.tar" -C "$tree" .
            mkdir -p "$root/bad"; printf 'bad\n' > "$root/bad/escape"
            tar -cf "$root/traversal.tar" -C "$root/bad" --transform='s|escape|../escape|' escape
            tar -cf "$root/nested-traversal.tar" -C "$root/bad" --transform='s|escape|packages/../../escape|' escape
            ln -s "$root/outside" "$tree/packages/link"
            tar -cf "$root/symlink.tar" -C "$tree" .
            rm "$tree/packages/link"
            rm "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
            tar -cf "$root/missing.tar" -C "$tree" .
            for fault in traversal nested-traversal symlink missing; do
                stage="$root/stage-$fault"; mkdir -p "$stage/packages" "$stage/npm"
                SOURCE="$root/$fault.tar" STAGE="$stage"
                diagnosis="$(c849_saved_copy "$SOURCE" "$STAGE" image)"; code=$?
                if [ "$code" = 0 ]; then diagnosis="$(c849_validate_seed_tree "$stage")"; code=$?; fi
                printf '%s code=%s diagnosis=%s\n' "$fault" "$code" "$diagnosis"
            done
            test ! -e "$root/escape" && echo nested-traversal-no-escape
            mkfifo "$tree/packages/fifo"
            mkdir -p "$root/fifo-stage/packages" "$root/fifo-stage/npm"
            SOURCE="$tree" STAGE="$root/fifo-stage"
            diagnosis="$(timeout 5s bash -c 'pwsh -NoProfile -File "$1/scripts/c849-import-saved-donor.ps1" -Source "$2" -Stage "$3"' _ "$repo" "$SOURCE" "$STAGE")"; code=$?
            printf 'directory-fifo code=%s diagnosis=%s\n' "$code" "$diagnosis"
            test ! -e "$root/fifo-stage/packages/fifo" && echo directory-fifo-not-copied
            for STATUS in main-zero main-busy main-unknown; do
                ( c849_prune_idle ) > "$root/verdict" 2>&1
                printf '%s code=%s verdict=%s\n' "$STATUS" "$?" "$(cat "$root/verdict")"
            done
            STATUS=main-zero; PS_ERROR=yes
            ( c849_prune_idle ) > "$root/verdict" 2>&1
            printf 'ps-error code=%s verdict=%s\n' "$?" "$(cat "$root/verdict")"
            STATUS=main-zero; PS_ERROR=no
            ( c849_prune_idle ) > "$root/verdict" 2>&1
            printf 'retired code=%s verdict=%s\n' "$?" "$(cat "$root/verdict")"
            IN_USE=yes
            ( c849_no_cache_attachments ) > "$root/verdict" 2>&1
            printf 'attached code=%s verdict=%s\n' "$?" "$(cat "$root/verdict")"
            IN_USE=no; PS_ERROR=yes
            ( c849_no_cache_attachments ) > "$root/verdict" 2>&1
            printf 'attachment-unknown code=%s verdict=%s\n' "$?" "$(cat "$root/verdict")"
            """);
        output.ShouldContain("traversal code=2 diagnosis=CacheDonorUnsafePath");
        output.ShouldContain("nested-traversal code=2 diagnosis=CacheDonorUnsafePath");
        output.ShouldContain("nested-traversal-no-escape");
        output.ShouldContain("directory-fifo code=2 diagnosis=CacheDonorUnsafeEntry");
        output.ShouldContain("directory-fifo-not-copied");
        output.ShouldContain("symlink code=2 diagnosis=CacheDonorUnsafeEntry");
        output.ShouldContain("missing code=2 diagnosis=AppHostDonorMissing");
        output.ShouldContain("main-busy code=2 verdict=refusal=CacheConsumersBusy");
        output.ShouldContain("main-unknown code=2 verdict=refusal=CacheConsumersBusy");
        output.ShouldContain("ps-error code=2 verdict=refusal=CacheConsumerUnknown");
        output.ShouldContain("retired code=0 verdict=");
        output.ShouldContain("attached code=2 verdict=refusal=CacheConsumersBusy");
        output.ShouldContain("attachment-unknown code=2 verdict=refusal=CacheConsumerUnknown");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Saved_donor_rejects_declared_size_bomb_before_writing()
    {
        RequireLinuxPwsh();
        var output = LinuxShell("repo='" + DelegateScriptRunner.RepoRoot.Replace("'", "'\\''") + "'\n" + """
            root="$(mktemp -d)"; trap 'rm -rf "$root"' EXIT
            mkdir -p "$root/stage/packages" "$root/stage/npm"
            perl -e '
                my $h = "\0" x 512;
                substr($h, 0, 13) = "packages/bomb";
                substr($h, 100, 8) = "0000644\0";
                substr($h, 108, 8) = "0000000\0";
                substr($h, 116, 8) = "0000000\0";
                substr($h, 124, 12) = sprintf("%011o\0", 12 * 1024**3);
                substr($h, 136, 12) = "00000000000\0";
                substr($h, 148, 8) = " " x 8;
                substr($h, 156, 1) = "0";
                substr($h, 257, 6) = "ustar\0";
                substr($h, 263, 2) = "00";
                substr($h, 148, 8) = sprintf("%06o\0 ", unpack("%32C*", $h));
                open my $out, ">", $ARGV[0] or die $!;
                print $out $h;
            ' "$root/bomb.tar"
            diagnosis="$(pwsh -NoProfile -File "$repo/scripts/c849-import-saved-donor.ps1" -Source "$root/bomb.tar" -Stage "$root/stage")"; code=$?
            printf 'size-bomb code=%s diagnosis=%s\n' "$code" "$diagnosis"
            test ! -e "$root/stage/packages/bomb" && echo size-bomb-not-written
            """);
        output.ShouldContain("size-bomb code=2 diagnosis=CacheBudgetExceeded");
        output.ShouldContain("size-bomb-not-written");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Retired_guard_refuses_docker_ps_errors()
    {
        var output = LinuxShell(Block(Remote(), "c849_no_temp_containers") + "\n" + """
            TEMP_PROJECT=temp
            write_result() { printf 'guard=%s\n' "$2"; exit "$3"; }
            docker() { [ "$1" = ps ] && return 1; return 0; }
            ( c849_no_temp_containers ) > /tmp/c849-guard-$$ 2>&1
            printf 'exit=%s verdict=%s\n' "$?" "$(cat /tmp/c849-guard-$$)"
            rm -f /tmp/c849-guard-$$
            """);
        output.ShouldContain("exit=2 verdict=guard=CacheDonorLookupFailed");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Deploy_temp_names_saved_import_when_seed_is_missing()
    {
        var remote = Remote();
        var output = LinuxShell(Block(remote, "c849_require_ready") + "\n" +
            Block(remote, "case_deploy_temp_runner") + "\n" + """
            root="$(mktemp -d)"; trap 'rm -rf "$root"' EXIT
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR" "$root/state/grok"
            SERVER2_ROOT="$root/server2"; C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            SERVER2_ENV="$root/main.env"; printf 'ready\n' > "$SERVER2_ENV"
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; RUN=red; LANE=host
            require_lane() { :; }; ensure_checkout() { :; }; ensure_runner_boot_files() { :; }
            c849_no_temp_containers() { :; }; build_server2_images() { :; }; c849_prepare() { :; }
            write_result() { printf 'deploy=%s\n' "$2"; exit "$3"; }
            docker() { [ "$1:$2" = volume:inspect ] && echo "$root/state"; }
            sudo() {
                [ "$1" = -n ] && shift
                if [ "$1" = test ]; then shift; test "$@"
                elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nstate 30000000 1 25000000 1%% /state\n'; fi
            }
            ( case_deploy_temp_runner ) > "$root/out" 2>&1
            printf 'exit=%s verdict=%s\n' "$?" "$(cat "$root/out")"
            """);
        output.ShouldContain("exit=2 verdict=deploy=CacheSeedRequired:");
        output.ShouldContain("scripts/verify-card0849-caches.ps1 -Case Seed -SavedDonor /home/mc/runner-cache-donor/temp-runner-cache.tar");
    }

    [Test]
    public void C849_Deploy_prepares_and_verifies_before_acceptance()
    {
        var remote = Remote();
        foreach (var name in new[] { "case_deploy_parent", "case_deploy_temp_runner" })
        {
            var body = Block(remote, name);
            Order(body, "c849_prepare yes", "seed_runner_checkout").ShouldBeTrue(name);
            Order(body, "c849_require_ready", "seed_runner_checkout").ShouldBeTrue(name);
            Order(body, "c849_assert_mounts", "c849_smoke").ShouldBeTrue(name);
        }
        Order(Block(remote, "case_deploy_parent"), "c849_smoke", "retire_superseded_server2_images").ShouldBeTrue();
        var retire = Block(remote, "case_retire_temp_runner");
        Order(retire, "c849_status_zero server2-temp", "compose_temp down -v").ShouldBeTrue();
        retire.ShouldContain("c849_require_ready");
        retire.ShouldContain("c849_budget_gate");
        var output = LinuxShell(CacheStatusHarness() + "\n" + retire + "\n" + """
            C590_TEMP_RETIRED_AT=2026-09-30T00:00:00Z
            SERVER2_TEMP_ENV="$root/temp.env"; printf 'RUNNER_GROK_STORE_DIR=/x\n' > "$SERVER2_TEMP_ENV"
            SERVER2_ROOT="$root/server2"; mkdir -p "$SERVER2_ROOT/cache"
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR"
            RUN=red; LANE=host
            require_lane() { :; }
            write_result() { printf 'retire-result=%s:%s\n' "$1" "$2"; exit "$3"; }
            c849_prepare() { :; }; c849_require_ready() { :; }; c849_budget_gate() { :; }
            c849_image() { echo image; }
            c849_volume() {
                [ "$VOLUME_FAIL" != yes ] || return 1
                [ -s "$root/volumes/$1/sentinel" ]
            }
            compose_temp() {
                printf 'compose %s\n' "$*" >> "$root/trace"
                rm -rf "$root/temp-private"
            }
            for STATUS in sessions runnerSessions queuedTasks null garbage unknown; do
                : > "$root/trace"
                ( case_retire_temp_runner ) > "$root/result" 2>&1
                printf '%s verdict=%s\n' "$STATUS" "$(cat "$root/result")"
                if grep -q 'down -v' "$root/trace"; then echo unsafe-down; fi
            done
            STATUS=zero; VOLUME_FAIL=yes
            ( case_retire_temp_runner ) > "$root/result" 2>&1
            printf 'lost-volume verdict=%s\n' "$(cat "$root/result")"
            mkdir -p "$root/temp-private"
            for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
                mkdir -p "$root/volumes/$name"
                printf 'keep\n' > "$root/volumes/$name/sentinel"
            done
            VOLUME_FAIL=no
            ( case_retire_temp_runner ) > "$root/result" 2>&1
            printf 'retained verdict=%s\n' "$(cat "$root/result")"
            [ ! -e "$root/temp-private" ] && echo private-removed
            for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
                [ -s "$root/volumes/$name/sentinel" ] || echo cache-lost
            done
            """);
        foreach (var fault in new[] { "sessions", "runnerSessions", "queuedTasks", "null", "garbage", "unknown" })
            output.ShouldContain(fault + " verdict=retire-result=false:TempRunnerNotIdle");
        output.ShouldNotContain("unsafe-down");
        output.ShouldContain("lost-volume verdict=retire-result=false:");
        output.ShouldContain("retained verdict=retire-result=true:");
        output.ShouldContain("private-removed");
        output.ShouldNotContain("cache-lost");
        var deployments = LinuxShell(Block(remote, "case_deploy_parent") + "\n" +
            Block(remote, "case_deploy_temp_runner") + "\n" + """
            root="$(mktemp -d)"; trap 'rm -rf "$root"' EXIT
            CASE_DIR="$root/case"; mkdir -p "$CASE_DIR" "$root/state/grok"
            SERVER2_ENV="$root/main.env"; SERVER2_TEMP_ENV="$root/temp.env"
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            C604_SERVER_ORIGIN=https://example.invalid
            HOST_PROJECT=main; TEMP_PROJECT=temp; LANE=host
            DEPLOY_KEY=x; PHONE_HOME_SECRET=x; CLAUDE_OAUTH_TOKEN_PATH=x
            GIT_IDENTITY_PATH=x; CODEX_HOME_PATH=x
            RUNNER_GIT_USER_NAME=test; RUNNER_GIT_USER_EMAIL=test@example.invalid
            require_lane() { :; }; ensure_checkout() { :; }
            ensure_runner_boot_files() { :; }; retire_c590_leftovers() { :; }
            broker_sha12() { echo aaaaaaaaaaaa; }
            build_server2_images() { printf 'build\n' >> "$root/trace"; }
            c849_prepare() {
                if [ "$MODE" = prepare ]; then write_result false CacheRootNotWritable 2; fi
                printf 'prepared\n' >> "$root/trace"
            }
            c849_require_ready() { write_result false CacheSeedRequired 2; }
            c849_budget_gate() { echo unsafe-budget >> "$root/trace"; }
            ensure_build_slots_broker() { echo unsafe-broker >> "$root/trace"; }
            compose_host() { echo unsafe-compose >> "$root/trace"; }
            compose_temp() { echo unsafe-compose >> "$root/trace"; }
            docker() {
                if [ "$1" = volume ] && [ "$2" = inspect ]; then echo "$root/state"; return 0; fi
                echo unsafe-docker >> "$root/trace"; return 1
            }
            sudo() {
                [ "$1" = -n ] && shift
                if [ "$1" = test ]; then shift; test "$@"
                elif [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\nstate 30000000 1 25000000 1%% /state\n'
                else return 1; fi
            }
            write_result() { printf 'deploy-result=%s:%s\n' "$1" "$2"; exit "$3"; }
            for MODE in prepare ready; do
                for target in parent temp; do
                    printf 'seed\n' > "$SERVER2_ENV"
                    : > "$root/trace"
                    if [ "$target" = parent ]; then
                        ( case_deploy_parent ) > "$root/result" 2>&1
                    else
                        ( case_deploy_temp_runner ) > "$root/result" 2>&1
                    fi
                    printf '%s %s verdict=%s\n' "$MODE" "$target" "$(cat "$root/result")"
                    if grep -q '^unsafe-' "$root/trace"; then echo unsafe-deploy; fi
                done
            done
            """);
        foreach (var target in new[] { "parent", "temp" })
        {
            deployments.ShouldContain("prepare " + target + " verdict=deploy-result=false:CacheRootNotWritable");
            deployments.ShouldContain("ready " + target + " verdict=deploy-result=false:CacheSeedRequired");
        }
        deployments.ShouldNotContain("unsafe-deploy");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Deploy_temp_observes_inaccessible_docker_mountpoints_with_sudo()
    {
        var remote = Remote();
        var output = LinuxShell("set -u\n" + """
            root="$(mktemp -d /tmp/c849-privilege-XXXXXXXX)"
            printf 'C849_PRIVILEGE_ROOT=%s\n' "$root"
            trap '[[ "$root" == /tmp/c849-privilege-???????? && -d "$root" ]] && rm -rf -- "$root"' EXIT
            mkdir -p "$root/docker/volumes" "$root/case" "$root/server2" "$root/state/grok"
            CASE_DIR="$root/case"; SERVER2_ROOT="$root/server2"
            SERVER2_ENV="$root/main.env"; SERVER2_TEMP_ENV="$root/temp.env"
            printf 'parent\n' > "$SERVER2_ENV"
            C849_PACKAGES=antiphon-runner-cache-nuget-packages
            C849_SCRATCH=antiphon-runner-cache-nuget-scratch
            C849_NPM=antiphon-runner-cache-npm-content
            for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
                mkdir -p "$root/docker/volumes/$name/_data"
            done
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            HOST_PROJECT=main; TEMP_PROJECT=temp; LANE=host; CASE=deploy-temp-runner
            C604_SERVER_ORIGIN=https://example.invalid
            DEPLOY_KEY=x; PHONE_HOME_SECRET=x; CLAUDE_OAUTH_TOKEN_PATH=x
            GIT_IDENTITY_PATH=x; CODEX_HOME_PATH=x
            require_lane() { [ "$1" = host ]; }
            write_result() { printf 'DIAGNOSIS=%s\n' "$2"; exit "$3"; }
            realpath() {
                if [[ "$*" == *"$root/docker/"* && "${PRIVILEGED:-0}" != 1 ]]; then
                    printf 'DENIED unprivileged realpath\n' >> "$root/access"
                    return 13
                fi
                command realpath "$@"
            }
            sudo() {
                [ "$1" = -n ] && shift
                printf '%s %s\n' "$1" "${*: -1}" >> "$root/elevated"
                if [ "$1" = df ]; then printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\ndocker 30000000 1 29999999 1%% /docker\n'; return 0; fi
                if [ "$1" = stat ]; then echo 1654:1654:700; return 0; fi
                PRIVILEGED=1 "$@"
            }
            docker() {
                local name="${@: -1}" format=''
                case "$1:$2" in
                    info:*) printf '%s\n' "$root/docker" ;;
                    volume:inspect)
                        if [ "${3:-}" = -f ]; then format="$4"; fi
                        if [ "$name" = antiphon-runner_runner-state ]; then
                            printf '%s\n' "$root/state"; return 0
                        fi
                        case "$format" in
                            *'.Driver'*) echo local ;;
                            *'.Options'*) echo '{}' ;;
                            *'io.antiphon.owner'*) echo server2-runner ;;
                            *'io.antiphon.cache-schema'*) echo 1 ;;
                            *'io.antiphon.cache-role'*)
                                case "$name" in
                                    "$C849_PACKAGES") echo nuget-packages ;;
                                    "$C849_SCRATCH") echo nuget-scratch ;;
                                    "$C849_NPM") echo npm-content ;;
                                esac ;;
                            *'.Mountpoint'*) printf '%s\n' "$root/docker/volumes/$name/_data" ;;
                        esac ;;
                    *) return 2 ;;
                esac
            }
            ensure_checkout() { :; }; ensure_runner_boot_files() { :; }
            c849_no_temp_containers() { :; }; build_server2_images() { :; }; c849_prepare() { :; }; c849_require_ready() { :; }
            ensure_build_slots_broker() { echo DEPLOY_REACHED_BROKER; exit 0; }
            """ + "\n" + Block(remote, "c849_observe_volume") + "\n" +
            Block(remote, "c849_budget_gate") + "\n" + Block(remote, "c849_prune_validate_tree") + "\n" +
            Block(remote, "case_deploy_temp_runner") + "\n" + """
            ( c849_observe_volume "$C849_PACKAGES" nuget-packages 10737418240 ) > "$root/observe" 2>&1
            printf 'OBSERVE_EXIT=%s\n' "$?"
            cat "$root/observe"
            mountpoint="$root/docker/volumes/$C849_PACKAGES/_data"
            ( c849_prune_validate_tree "$mountpoint" "$mountpoint" ) > "$root/prune" 2>&1
            printf 'PRUNE_EXIT=%s\n' "$?"
            cat "$root/prune"
            ( case_deploy_temp_runner ) > "$root/deploy" 2>&1
            printf 'DEPLOY_EXIT=%s\n' "$?"
            cat "$root/deploy"
            printf 'ELEVATED_REALPATH=%s\n' "$(grep -c '^realpath .*docker/volumes/' "$root/elevated" 2>/dev/null || true)"
            printf 'ELEVATED_SYMLINK=%s\n' "$(grep -c '^test .*docker/volumes/' "$root/elevated" 2>/dev/null || true)"
            """);
        output.Contains("OBSERVE_EXIT=0", StringComparison.Ordinal)
            .ShouldBeTrue("CacheTargetInvalid: observe must traverse the Docker volume through sudo");
        output.Contains("PRUNE_EXIT=0", StringComparison.Ordinal)
            .ShouldBeTrue("CacheTargetInvalid: prune must validate the Docker volume through sudo");
        output.Contains("DEPLOY_EXIT=0", StringComparison.Ordinal)
            .ShouldBeTrue("CacheTargetInvalid: deploy-temp must pass its volume observation through sudo");
        output.ShouldContain("DEPLOY_REACHED_BROKER");
        output.ShouldNotContain("DENIED unprivileged realpath");
        Regex.Match(output, @"ELEVATED_REALPATH=(\d+)").Groups[1].Value.ShouldNotBe("0");
        Regex.Match(output, @"ELEVATED_SYMLINK=(\d+)").Groups[1].Value.ShouldNotBe("0");
    }

    [Test]
    public void C849_Docker_volume_paths_have_no_bare_host_reads()
    {
        var remote = Remote();
        BareDockerRootReads(remote).ShouldBeEmpty();
        var scratch = remote.Replace("c849_observe_volume() {", "c849_observe_volume() {\n    realpath -e -- \"$mountpoint\"", StringComparison.Ordinal);
        BareDockerRootReads(scratch).ShouldContain("c849_observe_volume: realpath -e -- \"$mountpoint\"");
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh")).ShouldBe(remote);
    }

    private static List<string> BareDockerRootReads(string source)
    {
        var violations = new List<string>();
        foreach (var name in new[] { "c849_observe_volume", "c849_cold_volume_facts", "c849_cold_proof",
                     "c849_cold_probe", "c849_prune_validate_tree", "c849_preview", "c849_budget_gate",
                     "case_deploy_temp_runner" })
        {
            foreach (var line in Block(source, name).Split('\n').Select(x => x.Trim()))
            {
                if (!Regex.IsMatch(line, @"\$(mountpoint|path|resolved|docker_root|C849_COLD_DOCKER_ROOT)(\b|[}""/])")) continue;
                if (Regex.IsMatch(line, @"(?<!sudo -n )\b(realpath|stat|readlink|ls|test)\b[^;&|]*\$(?:mountpoint|path|resolved|docker_root|C849_COLD_DOCKER_ROOT)\b")
                    || Regex.IsMatch(line, @"\[\s*!?\s*-[Lde]\s+""?\$(?:mountpoint|path|resolved|docker_root|C849_COLD_DOCKER_ROOT)\b"))
                    violations.Add(name + ": " + line);
            }
        }
        return violations;
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Prune_preview_is_read_only_and_bounded()
    {
        var text = Remote();
        var output = LinuxShell(Block(text, "c849_budget_state") + "\n" + Block(text, "c849_headroom_state") + "\n" + """
            for n in 79 80 99 100 101; do printf '%s=%s\n' "$n" "$(c849_budget_state "$n" 100)"; done
            printf 'below=%s\n' "$(c849_headroom_state 21474836479)"
            printf 'at=%s\n' "$(c849_headroom_state 21474836480)"
            """);
        foreach (var value in new[] { "79=OK", "80=WARN", "99=WARN", "100=OVER", "101=OVER", "below=LOW", "at=OK" })
            output.ShouldContain(value);
        var preview = Block(text, "c849_preview");
        preview.ShouldNotContain("c849_prepare");
        preview.ShouldNotContain("docker stop");
        preview.ShouldNotContain("docker volume create");
        preview.ShouldNotContain("chmod ");
        preview.ShouldContain("c849_observe_volume");
        preview.ShouldContain("c849_budget_state");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Prune_and_rollback_retain_roots_and_recovery()
    {
        var output = LinuxShell(CachePruneHarness() + """
            CASE=runner-cache-fixture
            mkdir -p "$root/volumes/packages" "$root/volumes/scratch" "$root/volumes/npm"
            chmod 0700 "$root/volumes/packages" "$root/volumes/scratch" "$root/volumes/npm"
            mkdir -p "$root/volumes/packages/unrelated/1.0.0"
            printf 'remove\n' > "$root/volumes/packages/unrelated/1.0.0/payload"
            printf 'remove\n' > "$root/volumes/scratch/lock"
            printf 'remove\n' > "$root/volumes/npm/content"
            make_preview "$root/volumes/scratch"
            docker() {
                local args="$*" target
                if [ "$1" = image ]; then return 0; fi
                [ "$1" = run ] || return 2
                case "$args" in
                    *"source=$C849_PACKAGES,target=/cache"*) target="$root/volumes/packages" ;;
                    *"source=$C849_SCRATCH,target=/cache"*) target="$root/volumes/scratch" ;;
                    *"source=$C849_NPM,target=/cache"*) target="$root/volumes/npm" ;;
                    *) return 2 ;;
                esac
                if [[ "$args" == *'find /cache -mindepth'* ]]; then
                    if [ "$target" = "$root/volumes/packages" ]; then rm -rf "$target/unrelated"; else rm -f "$target"/*; fi
                    printf 'clear %s\n' "$target" >> "$root/docker-trace"
                elif [[ "$args" == *'cp -a'* ]]; then
                    mkdir -p "$target/microsoft.netcore.app.host.linux-x64" "$target/microsoft.netcore.app.ref"
                    cp -a "$root/recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20" "$target/microsoft.netcore.app.host.linux-x64/"
                    cp -a "$root/recovery/packages/microsoft.netcore.app.ref/9.0.20" "$target/microsoft.netcore.app.ref/"
                    printf 'refill %s\n' "$target" >> "$root/docker-trace"
                fi
            }
            c849_fixture_refill() {
                [ -s "$root/volumes/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" ] || return 2
                printf 'smoke-after-refill\n' >> "$root/docker-trace"
            }
            c849_budget_gate() { :; }
            ( c849_prune ) > "$root/verdict" 2>&1
            code=$?
            printf 'exit=%s\n' "$code"
            [ -d "$root/volumes/packages" ] && [ "$(stat -c %a "$root/volumes/packages")" = 700 ] && echo root-retained
            [ ! -e "$root/volumes/packages/unrelated" ] && [ ! -e "$root/volumes/scratch/lock" ] && [ ! -e "$root/volumes/npm/content" ] && echo selected-cleared
            [ -s "$root/recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata" ] && echo recovery-retained
            cat "$root/docker-trace"
            """);
        output.ShouldContain("exit=0");
        output.ShouldContain("root-retained");
        output.ShouldContain("selected-cleared");
        output.ShouldContain("recovery-retained");
        output.ShouldContain("smoke-after-refill");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C849_Cache_receipts_exclude_credentials_and_payloads()
    {
        var output = LinuxShell(Block(Remote(), "c849_fixture_receipt") + "\n" + """
            root="$(mktemp -d)"; trap 'rm -rf "$root"' EXIT
            printf 'source-sha=%040d\nimage-id=sha256:%064d\ninventories=2\n' 0 0 > "$root/valid"
            c849_fixture_receipt "$root/valid" "$root/receipt"
            grep -Fxq inventories=2 "$root/receipt" && echo aggregates-retained
            printf 'credential=TOKEN_SENTINEL_C849_CONTENT\n' > "$root/toxic"
            diagnosis="$(c849_fixture_receipt "$root/toxic" "$root/rejected")"; code=$?
            printf 'toxic-code=%s diagnosis=%s\n' "$code" "$diagnosis"
            [ ! -s "$root/rejected" ] && echo toxic-not-exported
            """);
        output.ShouldContain("aggregates-retained");
        output.ShouldContain("toxic-code=2 diagnosis=EvidenceNotAllowListed");
        output.ShouldContain("toxic-not-exported");
    }

    private static string ColdSeedHarness()
    {
        var source = Remote();
        return "set -u\n" + """
            root="$(mktemp -d /tmp/c912-cold-XXXXXXXX)"
            printf 'C912_TEST_ROOT=%s\n' "$root"
            trap '[[ "$root" == /tmp/c912-cold-???????? && -d "$root" ]] && rm -rf -- "$root"' EXIT
            mkdir -p "$root/volumes" "$root/case" "$root/server2"
            CASE_DIR="$root/case"; SERVER2_ROOT="$root/server2"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            C849_PACKAGES=antiphon-runner-cache-nuget-packages
            C849_SCRATCH=antiphon-runner-cache-nuget-scratch
            C849_NPM=antiphon-runner-cache-npm-content
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            RUN=c912cold
            HOST_PROJECT=antiphon-runner; TEMP_PROJECT=antiphon-runner-temp
            MAIN_ID=1111111111111111111111111111111111111111111111111111111111111111
            IMAGE_ID=sha256:0000000000000000000000000000000000000000000000000000000000000000
            FAULT=''; PHASE=''; C849_COLD_MAIN_ID=''; UNRELATED_BIND=0
            : > "$root/effects"
            write_result() { printf 'RESULT accepted=%s diagnosis=%s\n' "$1" "$2"; exit "$3"; }
            require_lane() { [ "$1" = host ]; }
            c849_lock() { :; }
            sudo() {
                [ "$1" = -n ] && shift
                if [ "$1" = stat ]; then
                    local stat_path="${@: -1}" volume_name
                    volume_name="$(basename "$(dirname "$stat_path")")"
                    if [ "$FAULT" = wrong-mode ] || [ -f "$root/uninitialized/$volume_name" ]; then
                        printf '0:0:755\n'
                    else
                        printf '1654:1654:700\n'
                    fi
                    return 0
                fi
                if [ "$1" = install ]; then mkdir -p "${@: -1}"; return 0; fi
                if [ "$1" = find ] && [ "$FAULT" = vanish-before-init ] && [ "$2" = "$root/volumes/$C849_PACKAGES/_data" ]; then
                    find "${@:2}" || return 2
                    rmdir "$root/volumes/$C849_PACKAGES/_data" "$root/volumes/$C849_PACKAGES" || return 2
                    return 0
                fi
                "$@"
            }
            c849_status_body() {
                if [ "$1" = server2 ]; then
                    printf '{"sessions":3,"runnerSessions":3,"queuedTasks":1,"acceptingNewWork":true,"draining":false,"dispatchEligible":true}'
                else
                    if [ "$FAULT" = temp-counter-omitted ]; then
                        printf '{"retiredAt":"2026-10-02T00:00:00Z","available":false,"dispatchEligible":false,"acceptingNewWork":false,"draining":true,"retireWhenIdle":true,"redirectTo":"server2","sessions":0,"queuedTasks":0}'
                        return 0
                    fi
                    if [ "$FAULT" = temp-unretired ]; then
                        printf '{"retiredAt":null,"available":true,"dispatchEligible":true,"acceptingNewWork":true,"draining":false,"retireWhenIdle":false,"redirectTo":null,"sessions":0,"runnerSessions":0,"queuedTasks":0}'
                        return 0
                    fi
                    printf '{"retiredAt":"2026-10-02T00:00:00Z","available":false,"dispatchEligible":false,"acceptingNewWork":false,"draining":true,"retireWhenIdle":true,"redirectTo":"server2","sessions":0,"runnerSessions":null,"queuedTasks":0}'
                fi
            }
            c849_no_temp_containers() { [ "$FAULT" != temp-container ] || write_result false CacheTempContainerExists 2; }
            docker() {
                local verb="$1" sub="${2:-}" name='' format='' code='' mount='' arg i path role
                case "$verb:$sub" in
                    info:*) printf '%s\n' "$root"; return 0 ;;
                    image:inspect) [ "$FAULT" != missing-image ]; return $? ;;
                    ps:*)
                        case "$*" in
                            *'name=^/'*) return 0 ;;
                            *'com.docker.compose.project=antiphon-runner-temp'*) return 0 ;;
                        esac
                        printf '%s\n' "$MAIN_ID"
                        if [ "$sub" = -aq ] && { [ "$FAULT" = other-mount ] || [ "$FAULT" = stopped-mount ] || [ "$UNRELATED_BIND" = 1 ]; }; then
                            printf '%064d\n' 2
                        fi
                        return 0 ;;
                    inspect:*)
                        [ "$FAULT" = inspect-error ] && return 2
                        if [ "$FAULT" = change-after-init ] && [ "$PHASE" = changed ]; then
                            printf '[{"Id":"%064d","Image":"%s","State":{"Running":true},"Config":{"Labels":{"com.docker.compose.project":"antiphon-runner","com.docker.compose.service":"session-runner"}},"Mounts":[]}]\n' 3 "$IMAGE_ID"
                            return 0
                        fi
                        if [ "$2" = "$(printf '%064d' 2)" ]; then
                            if [ "$UNRELATED_BIND" = 1 ]; then
                                printf '[{"Id":"%064d","Image":"%s","State":{"Running":false},"Config":{"Labels":{}},"Mounts":[{"Type":"bind","Source":"%s/unrelated-bind","Destination":"/unrelated","RW":true}]}]\n' 2 "$IMAGE_ID" "$root"
                                return 0
                            fi
                            printf '[{"Id":"%064d","Image":"%s","State":{"Running":false},"Config":{"Labels":{}},"Mounts":[{"Type":"volume","Name":"%s","Source":"%s/volumes/%s/_data","Destination":"/cache","RW":true}]}]\n' 2 "$IMAGE_ID" "$C849_PACKAGES" "$root" "$C849_PACKAGES"
                            return 0
                        fi
                        if [ "$FAULT" = main-mount ]; then
                            printf '[{"Id":"%s","Image":"%s","State":{"Running":true},"Config":{"Labels":{"com.docker.compose.project":"antiphon-runner","com.docker.compose.service":"session-runner"}},"Mounts":[{"Type":"volume","Name":"%s","Source":"%s/volumes/%s/_data","Destination":"/home/app/.nuget/packages","RW":true}]}]\n' "$MAIN_ID" "$IMAGE_ID" "$C849_PACKAGES" "$root" "$C849_PACKAGES"
                            return 0
                        fi
                        printf '[{"Id":"%s","Image":"%s","State":{"Running":true},"Config":{"Labels":{"com.docker.compose.project":"antiphon-runner","com.docker.compose.service":"session-runner"}},"Mounts":[]}]\n' "$MAIN_ID" "$IMAGE_ID"
                        return 0 ;;
                    volume:ls)
                        [ "$FAULT" = census-error ] && return 2
                        find "$root/volumes" -mindepth 1 -maxdepth 1 -printf '%f\n'; return 0 ;;
                    volume:create)
                        name="${@: -1}"; printf 'create %s\n' "$name" >> "$root/effects"
                        [ "$FAULT" = create-error ] && return 2
                        [[ "$*" == *'--label io.antiphon.owner=server2-runner'* && "$*" == *'--label io.antiphon.cache-schema=1'* && "$*" == *'--label io.antiphon.cache-role='* ]] || return 2
                        mkdir -p "$root/volumes/$name/_data" "$root/volume-labels" "$root/uninitialized"
                        printf '%s\n' "$*" > "$root/volume-labels/$name"
                        : > "$root/uninitialized/$name"
                        printf '%s\n' "$name"; return 0 ;;
                    volume:inspect)
                        name="${@: -1}"; [ -d "$root/volumes/$name" ] || return 1
                        role=nuget-packages
                        [ "$name" = "$C849_SCRATCH" ] && role=nuget-scratch
                        [ "$name" = "$C849_NPM" ] && role=npm-content
                        local owner=server2-runner
                        [ "$FAULT" = foreign-owner ] && owner=foreign
                        printf '[{"Name":"%s","Driver":"local","Options":{},"Mountpoint":"%s/volumes/%s/_data","Labels":{"io.antiphon.owner":"%s","io.antiphon.cache-schema":"1","io.antiphon.cache-role":"%s"}}]\n' "$name" "$root" "$name" "$owner" "$role"
                        return 0 ;;
                    run:*)
                        for ((i=1;i<=$#;i++)); do
                            arg="${!i}"
                            [ "$arg" = --mount ] && { i=$((i+1)); mount="${!i}"; }
                            [ "$arg" = -c ] && { i=$((i+1)); code="${!i}"; }
                        done
                        name="${mount#*source=}"; name="${name%%,*}"
                        if [ ! -d "$root/volumes/$name" ]; then
                            mkdir -p "$root/volumes/$name/_data" "$root/unlabelled-volume" "$root/uninitialized"
                            : > "$root/unlabelled-volume/$name"
                            : > "$root/uninitialized/$name"
                        fi
                        path="$root/volumes/$name/_data"
                        if [[ "$code" == *chown* ]]; then
                            printf 'init %s\n' "$name" >> "$root/effects"
                            rm -f "$root/uninitialized/$name"
                            [ "$FAULT" = change-after-init ] && PHASE=changed
                            [ "$FAULT" = init-error ] && return 2
                            return 0
                        fi
                        printf 'probe %s %s\n' "$name" "$*" >> "$root/effects"
                        code="${code//\/cache/$path}"
                        [ "$FAULT" = probe-create-fail ] && code="set -e; false; $code"
                        [ "$FAULT" = probe-rename-fail ] && code="mv() { return 2; }; $code"
                        [ "$FAULT" = probe-delete-fail ] && code="rm() { return 2; }; $code"
                        bash -c "$code" sh "$RUN"; return $? ;;
                    rm:*) printf 'remove %s\n' "${@: -1}" >> "$root/effects"; [ "$FAULT" != cleanup-fail ]; return $? ;;
                esac
                printf 'UNEXPECTED-DOCKER %s\n' "$*" >> "$root/effects"
                return 2
            }
            timeout() {
                [ "$1" = --kill-after=2s ] && [ "$2" = 10s ] || return 2
                [ "$FAULT" = probe-timeout ] && return 124
                shift 2; "$@"
            }
            """ + "\n" +
            Block(source, "c849_cold_volume_facts") + "\n" +
            Block(source, "c849_cold_proof") + "\n" +
            Block(source, "c849_cold_probe") + "\n" +
            Block(source, "c849_cold_seed") + "\n";
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_seed_with_unrelated_bind_initializes_only_three_labelled_roots()
    {
        RequireLinuxJq();
        var output = LinuxShell(ColdSeedHarness() + """
            mkdir -p "$SERVER2_ROOT/cache"
            UNRELATED_BIND=1
            (c849_cold_seed)
            [ -f "$C849_READY" ] && echo cold-seed-accepted
            expected="$(printf 'init %s\ninit %s\ninit %s' "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM")"
            [ "$(grep '^init ' "$root/effects")" = "$expected" ] && echo init-targets-packages-scratch-npm-in-order
            [ "$(find "$root/volumes" -mindepth 1 -maxdepth 1 -type d | wc -l)" = 3 ] &&
                [ ! -d "$root/unlabelled-volume" ] && echo no-unlabelled-volume
            for name in "$C849_PACKAGES" "$C849_SCRATCH" "$C849_NPM"; do
                role=nuget-packages
                [ "$name" = "$C849_SCRATCH" ] && role=nuget-scratch
                [ "$name" = "$C849_NPM" ] && role=npm-content
                [ -f "$root/volume-labels/$name" ] &&
                    grep -Fq -- '--label io.antiphon.owner=server2-runner' "$root/volume-labels/$name" &&
                    grep -Fq -- '--label io.antiphon.cache-schema=1' "$root/volume-labels/$name" &&
                    grep -Fq -- "--label io.antiphon.cache-role=$role" "$root/volume-labels/$name" &&
                    [ "$(sudo -n stat -c '%u:%g:%a' -- "$root/volumes/$name/_data")" = 1654:1654:700 ] || exit 2
            done
            echo all-three-labelled-owned-0700
            """);
        output.Contains("cold-seed-accepted").ShouldBeTrue("cold seed with unrelated bind must be accepted: " + output);
        output.Contains("init-targets-packages-scratch-npm-in-order").ShouldBeTrue("init helper must target packages, scratch, npm in order: " + output);
        output.Contains("no-unlabelled-volume").ShouldBeTrue("docker run must not auto-create an unlabelled volume: " + output);
        output.Contains("all-three-labelled-owned-0700").ShouldBeTrue("all three volumes require three labels and uid 1654 mode 0700: " + output);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_seed_refuses_when_created_volume_disappears_before_init()
    {
        RequireLinuxJq();
        var output = LinuxShell(ColdSeedHarness() + """
            mkdir -p "$SERVER2_ROOT/cache"
            FAULT=vanish-before-init
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && echo no-accepted-marker
            [ ! -d "$root/unlabelled-volume" ] && echo no-unlabelled-auto-created-volume
            ! grep -q '^init ' "$root/effects" && echo no-init-against-absent-volume
            """);
        output.Contains("RESULT accepted=false diagnosis=CacheFirstSeedPreconditionUnknown").ShouldBeTrue("absent volume must refuse before init: " + output);
        output.Contains("no-accepted-marker").ShouldBeTrue("absent volume must not write a marker: " + output);
        output.Contains("no-unlabelled-auto-created-volume").ShouldBeTrue("init must not auto-create an unlabelled volume: " + output);
        output.Contains("no-init-against-absent-volume").ShouldBeTrue("init must not target an absent volume: " + output);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_volumes_seed_accepts_busy_main_without_packages()
    {
        RequireLinuxJq();
        for (var mask = 0; mask < 8; mask++)
        {
            var existing = new[] { "$C849_PACKAGES", "$C849_SCRATCH", "$C849_NPM" };
            var setup = string.Concat(existing.Where((_, index) => (mask & (1 << index)) != 0)
                .Select(name => "mkdir -p \"$root/volumes/" + name + "/_data\"\n"));
            var expectedCreates = 3 - Convert.ToString(mask, 2).Count(bit => bit == '1');
            var output = LinuxShell(ColdSeedHarness() + setup + "\n" + """
                mkdir -p "$SERVER2_ROOT/cache"
                (c849_cold_seed)
                [ -f "$C849_READY" ] && grep -Fxq kind=cold "$C849_READY" &&
                    [ "$(wc -l < "$C849_READY")" = 8 ] &&
                    ! grep -Eq '^(payload|reference|recovery|apphost)' "$C849_READY" && echo cold-ready
                """ + "\n" +
                "[ \"$(grep -c '^create ' \"$root/effects\")\" = " + expectedCreates + " ] && echo only-absent-created\n" + """
                grep -Fxq 'ready=true kind=cold writable=3' "$CASE_DIR/seed.txt" && echo cold-receipt
                [ -z "$(find "$root/volumes" -type f -print -quit)" ] && echo empty-roots
                [ -z "$(grep -Ei '^UNEXPECTED|dotnet restore|npm ci|build-slot|--network (bridge|host)' "$root/effects")" ] && echo no-package-network-or-slot-call
                [ -z "$(grep -E '^(stop|post|deploy) ' "$root/effects")" ] &&
                    [ "$(c849_status_body server2 | jq -r .acceptingNewWork)" = true ] && echo main-unchanged
                """);
            output.Contains("cold-ready").ShouldBeTrue("cold-ready mask=" + mask);
            output.Contains("only-absent-created").ShouldBeTrue("only-absent-created mask=" + mask);
            output.Contains("cold-receipt").ShouldBeTrue("cold-receipt mask=" + mask);
            output.Contains("empty-roots").ShouldBeTrue("empty-roots mask=" + mask);
            output.Contains("main-unchanged").ShouldBeTrue("main-unchanged mask=" + mask);
            output.Contains("no-package-network-or-slot-call").ShouldBeTrue("no-package-network-or-slot-call mask=" + mask);
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets()
    {
        RequireLinuxJq();
        var output = LinuxShell(ColdSeedHarness() + """
            mkdir -p "$SERVER2_ROOT/cache"
            FAULT=census-error
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && [ ! -s "$root/effects" ] && echo preflight-no-write-census-error
            FAULT=inspect-error
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && [ ! -s "$root/effects" ] && echo preflight-no-write-inspect-error
            for fault in main-mount other-mount stopped-mount temp-unretired temp-counter-omitted; do
                FAULT="$fault"
                (c849_cold_seed)
                [ ! -e "$C849_READY" ] && [ ! -s "$root/effects" ] && echo "preflight-no-write-$fault"
            done
            FAULT=''
            mkdir -p "$root/volumes/$C849_PACKAGES/_data"
            FAULT=foreign-owner
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && [ ! -s "$root/effects" ] && echo preflight-no-write-foreign-owner
            FAULT=wrong-mode
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && [ ! -s "$root/effects" ] && echo preflight-no-write-wrong-mode
            FAULT=''
            printf x > "$root/volumes/$C849_PACKAGES/_data/.hidden"
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && [ ! -s "$root/effects" ] && echo preflight-no-write-hidden
            """);
        output.Contains("RESULT accepted=false diagnosis=CacheFirstSeedPreconditionUnknown").ShouldBeTrue("refusal-CacheFirstSeedPreconditionUnknown");
        output.Contains("preflight-no-write-census-error").ShouldBeTrue("preflight-no-write-census-error");
        output.Contains("preflight-no-write-inspect-error").ShouldBeTrue("preflight-no-write-inspect-error");
        foreach (var fault in new[] { "main-mount", "other-mount", "stopped-mount", "temp-unretired", "temp-counter-omitted", "foreign-owner", "wrong-mode" })
            output.Contains("preflight-no-write-" + fault).ShouldBeTrue("preflight-no-write-" + fault);
        output.Contains("RESULT accepted=false diagnosis=CacheFirstSeedVolumeInUse").ShouldBeTrue("refusal-CacheFirstSeedVolumeInUse");
        output.Contains("RESULT accepted=false diagnosis=CacheFirstSeedTempNotRetired").ShouldBeTrue("refusal-CacheFirstSeedTempNotRetired");
        output.Contains("RESULT accepted=false diagnosis=CacheVolumeForeign").ShouldBeTrue("refusal-CacheVolumeForeign");
        output.Contains("RESULT accepted=false diagnosis=CacheRootOwnershipInvalid").ShouldBeTrue("refusal-CacheRootOwnershipInvalid");
        output.Contains("RESULT accepted=false diagnosis=CacheUnmarkedContent").ShouldBeTrue("refusal-CacheUnmarkedContent-hidden");
        output.Contains("preflight-no-write-hidden").ShouldBeTrue("preflight-no-write-hidden");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries()
    {
        RequireLinuxJq();
        var output = LinuxShell(ColdSeedHarness() + """
            mkdir -p "$SERVER2_ROOT/cache"
            (c849_cold_seed)
            for p in P0 P1 P2 P3 P4 P5 P6; do
                grep -q "phase=$p " "$CASE_DIR/cold-proof.txt" && echo "phase-proof-recorded-$p"
            done
            [ "$(grep -c '^phase=' "$CASE_DIR/cold-proof.txt")" -ge 11 ] && echo repeated-proof
            """);
        foreach (var phase in new[] { "P0", "P1", "P2", "P3", "P4", "P5", "P6" })
            output.Contains("phase-proof-recorded-" + phase).ShouldBeTrue("phase-proof-recorded-" + phase);
        output.Contains("repeated-proof").ShouldBeTrue("repeated-proof");
        var changed = LinuxShell(ColdSeedHarness() + """
            mkdir -p "$SERVER2_ROOT/cache"
            FAULT=change-after-init
            (c849_cold_seed)
            [ ! -e "$C849_READY" ] && [ "$(grep -c '^create ' "$root/effects")" = 1 ] &&
                [ "$(grep -c '^probe ' "$root/effects")" = 0 ] && echo recheck-refused-P1
            """);
        changed.Contains("RESULT accepted=false diagnosis=CacheFirstSeedMainMountChanged").ShouldBeTrue("refusal-CacheFirstSeedMainMountChanged-id");
        changed.Contains("recheck-refused-P1").ShouldBeTrue("recheck-refused-P1");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit()
    {
        RequireLinuxJq();
        var output = LinuxShell(ColdSeedHarness() + """
            mkdir -p "$SERVER2_ROOT/cache"
            (c849_cold_seed)
            [ "$(grep -c '^probe ' "$root/effects")" = 3 ] && echo probe-writable
            grep -q -- '--user 1654:1654' "$root/effects" && echo probe-uid-1654
            [ "$(grep -c '^remove ' "$root/effects")" = 3 ] && echo probe-owned-helper-stopped
            """);
        output.Contains("probe-writable").ShouldBeTrue("probe-writable");
        output.Contains("probe-uid-1654").ShouldBeTrue("probe-uid-1654");
        output.Contains("probe-owned-helper-stopped").ShouldBeTrue("probe-owned-helper-stopped");
        foreach (var variant in new[] {
                     (Fault: "missing-image", Code: "CacheHelperImageMissing"),
                     (Fault: "create-error", Code: "CacheVolumeCreateFailed"),
                     (Fault: "init-error", Code: "CacheVolumeInitFailed"),
                     (Fault: "probe-create-fail", Code: "CacheRootNotWritable"),
                     (Fault: "probe-rename-fail", Code: "CacheRootNotWritable"),
                     (Fault: "probe-delete-fail", Code: "CacheRootNotWritable"),
                     (Fault: "probe-timeout", Code: "CacheColdProbeTimeout"),
                     (Fault: "cleanup-fail", Code: "CacheSeedProbeCleanupFailed") })
        {
            var refused = LinuxShell(ColdSeedHarness() + "\nFAULT=" + variant.Fault + "\n" + """
                mkdir -p "$SERVER2_ROOT/cache"
                printf keep > "$root/outside"
                (c849_cold_seed)
                [ ! -e "$C849_READY" ] && [ "$(cat "$root/outside")" = keep ] && echo probe-refused-no-marker-or-outside-write
                grep -q '^remove c849-cold-' "$root/effects" && echo probe-owned-helper-stopped
                """);
            refused.Contains("RESULT accepted=false diagnosis=" + variant.Code).ShouldBeTrue("probe-refused-" + variant.Fault);
            refused.Contains("probe-refused-no-marker-or-outside-write").ShouldBeTrue("probe-refused-no-marker-or-outside-write " + variant.Fault);
            if (variant.Fault == "probe-timeout")
                refused.Contains("probe-owned-helper-stopped").ShouldBeTrue("probe-owned-helper-stopped");
        }
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_marker_has_distinct_validation_and_full_context_refusal()
    {
        RequireLinuxJq();
        var source = Remote();
        var output = LinuxShell(ColdSeedHarness() + "\n" + Block(source, "c849_require_ready") + "\n" + """
            mkdir -p "$SERVER2_ROOT/cache"
            (c849_cold_seed)
            c849_require_ready allow-cold
            [ "$C849_KIND" = cold ] && echo cold-marker-valid
            printf payload > "$root/volumes/$C849_PACKAGES/_data/ordinary-build-package"
            before="$(grep -c '^probe ' "$root/effects")"
            c849_require_ready allow-cold
            [ "$(grep -c '^probe ' "$root/effects")" = "$before" ] && echo marker-reuse-no-probe-or-restore
            SHA=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
            for context in deploy-temp verify retire; do
                (c849_require_ready allow-cold) && echo "cross-sha-$context-accepted"
            done
            for context in reset prune saved-donor; do
                refusal="$( (c849_require_ready) )"
                [ "$refusal" = 'RESULT accepted=false diagnosis=CacheFullSeedRequired' ] && echo "cross-sha-$context-full-required"
            done
            cp "$C849_READY" "$root/valid-marker"
            (c849_require_ready)
            printf 'payload-sha256=bad\n' >> "$C849_READY"
            (c849_require_ready allow-cold)
            cp "$root/valid-marker" "$C849_READY"
            printf 'kind=cold\n' >> "$C849_READY"
            (c849_require_ready allow-cold)
            cp "$root/valid-marker" "$C849_READY"
            rm "$C849_READY"; ln -s "$root/valid-marker" "$C849_READY"
            (c849_require_ready allow-cold)
            """);
        output.Contains("cold-marker-valid").ShouldBeTrue("cold-marker-valid");
        output.Contains("marker-reuse-no-probe-or-restore").ShouldBeTrue("marker-reuse-no-probe-or-restore");
        foreach (var context in new[] { "deploy-temp", "verify", "retire" })
            output.Contains($"cross-sha-{context}-accepted").ShouldBeTrue($"cross-sha-{context}-accepted");
        foreach (var context in new[] { "reset", "prune", "saved-donor" })
            output.Contains($"cross-sha-{context}-full-required").ShouldBeTrue($"cross-sha-{context}-full-required");
        output.Contains("RESULT accepted=false diagnosis=CacheFullSeedRequired").ShouldBeTrue("full-context-refused");
        output.Contains("RESULT accepted=false diagnosis=CacheSeedMarkerInvalid").ShouldBeTrue("malformed-marker-refused");
        var invalidCount = Regex.Matches(output, "RESULT accepted=false diagnosis=CacheSeedMarkerInvalid").Count;
        invalidCount.ShouldBeGreaterThanOrEqualTo(3, "malformed-marker-refused mixed, duplicate, symlink");
        var repo = DelegateScriptRunner.RepoRoot;
        var front = Path.Combine(repo, "scripts", "verify-card0849-caches.ps1");
        var script = "$Front='" + front.Replace("'", "''") + "';" + """
            $sha = (& git -C (Split-Path -Parent (Split-Path -Parent $Front)) rev-parse HEAD).Trim()
            $global:seen = $false; $global:owned = ''
            function global:pwsh {
                param([switch]$NoProfile,[string]$File,[string]$Case,[string]$Manifest)
                $m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
                $global:owned = Split-Path -Parent $m.evidenceRoot
                if ($Case -eq 'runner-cache-seed' -and $m.coldSeed -is [bool] -and $m.coldSeed) {
                    $global:seen = $true
                    Write-Output 'cold-transport'
                }
                throw 'C912_STUB_STOP'
            }
            try {
                try { & $Front -Case Seed -Cold -Sha $sha } catch { if ($_.Exception.Message -ne 'C912_STUB_STOP') { throw } }
                if (-not $global:seen) { throw 'cold-transport missing' }
                try { & $Front -Case Reset -Cold -Sha $sha | Out-Null; throw 'bad-case-accepted' }
                catch { if ($_.Exception.Message -ne 'CacheColdModeInvalid') { throw } }
                try { & $Front -Case Seed -Cold -SavedDonor /tmp/donor -Sha $sha | Out-Null; throw 'conflict-accepted' }
                catch { if ($_.Exception.Message -ne 'CacheDonorSourceConflict') { throw } }
                Write-Output 'cold-conflict-refused'
            }
            finally {
                if ($global:owned -and (Split-Path -Leaf $global:owned) -match '^c849-c849[0-9a-f]{16}$') {
                    Remove-Item -LiteralPath $global:owned -Recurse -Force
                }
                Remove-Item Function:\pwsh -ErrorAction SilentlyContinue
            }
            """;
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var frontOutput = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, "cold-transport " + frontOutput);
        frontOutput.Contains("cold-transport").ShouldBeTrue("cold-transport");
        frontOutput.Contains("cold-conflict-refused").ShouldBeTrue("refusal-CacheDonorSourceConflict");
        var bridge = File.ReadAllText(Path.Combine(repo, "scripts", "c590-real.ps1"));
        bridge.Contains("$Manifest.coldSeed -isnot [bool]").ShouldBeTrue("cold-bridge-boolean-only");
        bridge.Contains("export C590_COLD_SEED=").ShouldBeTrue("cold-bridge-host-flag");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads()
    {
        var remote = Remote();
        foreach (var function in new[] { "case_verify_runner_caches", "case_verify_runner_caches_retired",
                     "case_deploy_parent", "case_deploy_temp_runner", "case_retire_temp_runner" })
        {
            var body = Block(remote, function);
            body.Contains("c849_require_ready allow-cold").ShouldBeTrue("cold-verify-empty-cache " + function);
        }
        Block(remote, "case_verify_runner_caches").Contains("if [ \"$C849_KIND\" = full ]; then c849_smoke").ShouldBeTrue("cold-no-smoke");
        Block(remote, "case_verify_runner_caches_retired").Contains("if [ \"$C849_KIND\" = full ]; then c849_smoke").ShouldBeTrue("full-smoke-retained");
        var front = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-card0849-caches.ps1"));
        front.Contains("C849 mixed marker kinds").ShouldBeTrue("mixed-kind-refused");
        var frontPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "verify-card0849-caches.ps1");
        var script = "$Front='" + frontPath.Replace("'", "''") + "';" + """
            $repo = Split-Path -Parent (Split-Path -Parent $Front)
            $sha = (& git -C $repo rev-parse HEAD).Trim()
            $global:owned = @(); $global:kind2 = 'cold'
            function global:pwsh {
                param([switch]$NoProfile,[string]$File,[string]$Case,[string]$Manifest)
                $m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
                $global:owned += Split-Path -Parent $m.evidenceRoot
                $dir = Join-Path $m.evidenceRoot $Case
                New-Item -ItemType Directory -Path $dir -Force | Out-Null
                [IO.File]::WriteAllText((Join-Path $dir 'c590-result.json'), '{"accepted":true,"exit":0}' + "`n")
                $status = @{ buildVersion=$sha; dispatchEligible=$true; acceptingNewWork=$true; draining=$false;
                    sessions=0; runnerSessions=0; queuedTasks=0; credential='TOKEN_SENTINEL_C912' } | ConvertTo-Json -Compress
                [IO.File]::WriteAllText((Join-Path $dir 'status.json'), $status + "`n")
                $private = if ($m.runnerId -eq 'server2-temp') { 'antiphon-runner-temp_runner-tmp' } else { 'antiphon-runner_runner-tmp' }
                $mounts = "antiphon-runner-cache-nuget-packages antiphon-runner-cache-nuget-scratch antiphon-runner-cache-npm-content`n$private /tmp true`n"
                [IO.File]::WriteAllText((Join-Path $dir 'runner-mounts.txt'), $mounts)
                $kind = if ($m.runnerId -eq 'server2-temp') { $global:kind2 } else { 'cold' }
                [IO.File]::WriteAllText((Join-Path $dir 'seed-kind.txt'), $kind + "`n")
                if ($Case -eq 'verify-runner-caches-retired') {
                    [IO.File]::WriteAllText((Join-Path $dir 'rollback.txt'), 'rollback-image=sha256:' + ('0' * 64) + "`n")
                }
            }
            try {
                $both = & $Front -Case Both -Sha $sha | Out-String
                if ($both -notmatch 'C849_BOTH kind=cold') { throw 'cold-verify-empty-cache' }
                Write-Output 'cold-verify-empty-cache'
                $retired = & $Front -Case Retired -Sha $sha | Out-String
                if ($retired -notmatch 'C849_RETIRED kind=cold') { throw 'cold-retired-without-smoke' }
                Write-Output 'cold-no-smoke'
                $global:kind2 = 'full'
                try { & $Front -Case Both -Sha $sha | Out-Null; throw 'mixed-kind-accepted' }
                catch { if ($_.Exception.Message -ne 'C849 mixed marker kinds') { throw } }
                Write-Output 'mixed-kind-refused'
            }
            finally {
                foreach ($path in ($global:owned | Select-Object -Unique)) {
                    if ((Split-Path -Leaf $path) -match '^c849-c849[0-9a-f]{16}$') {
                        Remove-Item -LiteralPath $path -Recurse -Force
                    }
                }
                Remove-Item Function:\pwsh -ErrorAction SilentlyContinue
            }
            """;
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, output);
        output.Contains("cold-verify-empty-cache").ShouldBeTrue("cold-verify-empty-cache");
        output.Contains("cold-no-smoke").ShouldBeTrue("cold-no-smoke");
        output.Contains("mixed-kind-refused").ShouldBeTrue("mixed-kind-refused");
        output.Contains("TOKEN_SENTINEL_C912").ShouldBeFalse("cold-toxic-sentinel-absent");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C912_Cold_exception_never_weakens_maintenance_or_donor_gates()
    {
        var previousJq = ForceLinuxJqAvailability.Value;
        try
        {
            ForceLinuxJqAvailability.Value = false;
            var jqCases = new Action[]
            {
                C912_Cold_volumes_seed_accepts_busy_main_without_packages,
                C912_Cold_volumes_seed_refuses_unknown_mounted_or_populated_targets,
                C912_Cold_volumes_seed_rechecks_initialization_and_probe_boundaries,
                C912_Cold_cache_probe_requires_uid_writes_cleanup_and_bounded_exit,
                C912_Cold_marker_has_distinct_validation_and_full_context_refusal
            };
            foreach (var run in jqCases)
            {
                SkipTestException? skip = null;
                try { run(); }
                catch (SkipTestException exception) { skip = exception; }
                skip.ShouldNotBeNull($"{run.Method.Name} must skip before running a jq-dependent Linux shell block");
                skip.Message.ShouldBe(NoLinuxJqReason);
            }
            ForceLinuxJqAvailability.Value = true;
            HasLinuxJq().ShouldBeTrue("CARD-0912 forced jq-present guard path");
        }
        finally
        {
            ForceLinuxJqAvailability.Value = previousJq;
        }
        var remote = Remote();
        var reset = Block(remote, "c849_reset");
        reset.Contains("c849_require_ready").ShouldBeTrue("full-gate-refused-reset");
        reset.Contains("c849_prune_idle").ShouldBeTrue("full-gate-refused-reset-idle");
        reset.Contains("CacheResetInUse").ShouldBeTrue("full-gate-refused-reset-attachments");
        var prune = Block(remote, "c849_prune");
        prune.Contains("c849_require_ready").ShouldBeTrue("full-gate-refused-prune");
        prune.Contains("c849_prune_idle").ShouldBeTrue("full-gate-refused-prune-idle");
        prune.Contains("c849_budget_gate").ShouldBeTrue("full-gate-refused-prune-budget");
        var seed = Block(remote, "c849_seed");
        seed.Contains("c849_prune_idle").ShouldBeTrue("full-gate-refused-saved");
        seed.Contains("c849_no_cache_attachments").ShouldBeTrue("full-gate-refused-saved-attachments");
        seed.Contains("CacheDonorNotIdleDrained").ShouldBeTrue("full-gate-refused-donor");
        Order(seed, "c849_status_zero server2-temp ||", "docker stop \"$donor\"").ShouldBeTrue("full-gate-refused-donor-before-stop");
        seed.Contains("CacheDonorConsumerBusy").ShouldBeTrue("full-gate-refused-donor-process");
        var rolling = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "deploy-server2.ps1"));
        rolling.Contains("OldRunnerStillBusy").ShouldBeTrue("redeploy-old still checks zero");
    }

    [Test]
    [Arguments("c849_seed")]
    [Arguments("case_deploy_parent")]
    [Arguments("case_deploy_temp_runner")]
    [Arguments("case_retire_temp_runner")]
    [Arguments("case_verify_runner_caches")]
    [Arguments("case_verify_runner_caches_retired")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C973_Cold_marker_readers_accept_pruned_seed_image_and_refuse_invalid_markers(string reader)
    {
        RequireLinuxJq();
        var output = LinuxShell(C973ReaderHarness() + $"\nreader='{reader}'\n" + """
            result="$(run_reader "$reader" 0 1 valid)"; code=$?
            printf '%s\n' "$result"
            [ "$code" = 0 ] && printf '%s' "$result" | grep -Fq '"accepted":true' && echo pruned-seed-image-accepted
            for variant in missing malformed duplicate foreign-marker malformed-image malformed-source symlink foreign-volume unwritable; do
                result="$(run_reader "$reader" 0 1 "$variant")"; code=$?
                [ "$code" = 2 ] && printf '%s' "$result" | grep -Fq '"accepted":false' && echo "invalid-marker-refused-$variant"
            done
            """);
        output.Contains("pruned-seed-image-accepted").ShouldBeTrue($"pruned-seed-image-accepted {reader}: {output}");
        foreach (var variant in new[] { "missing", "malformed", "duplicate", "foreign-marker", "malformed-image",
                     "malformed-source", "symlink", "foreign-volume", "unwritable" })
            output.Contains("invalid-marker-refused-" + variant).ShouldBeTrue($"{reader} refuses {variant}");
        if (reader == "case_verify_runner_caches_retired")
            output.Contains("rollback-image=sha256:" + new string('1', 64)).ShouldBeTrue("cold rollback receipt names an available helper, not pruned seed provenance");
    }

    [Test]
    [Arguments("runner-cache-seed")]
    [Arguments("runner-cache-inventory")]
    [Arguments("runner-cache-fixture")]
    [Arguments("runner-cache-prune-preview")]
    [Arguments("runner-cache-reset")]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C973_Cold_helper_readers_resolve_current_main_after_seed_image_prune(string context)
    {
        RequireLinuxJq();
        var output = LinuxShell(C973ReaderHarness() + $"\nreader='c849_image:{context}'\n" + """
            result="$(run_reader "$reader" 0 0 valid)"; code=$?
            printf '%s\n' "$result"
            [ "$code" = 0 ] && printf '%s' "$result" | grep -Fq 'HELPER=sha256:1111111111111111111111111111111111111111111111111111111111111111' && echo available-main-helper
            for variant in malformed-image foreign-main no-main no-image; do
                result="$(run_reader "$reader" 0 0 "$variant")"; code=$?
                [ "$code" = 2 ] && printf '%s' "$result" | grep -Fq 'DIAGNOSIS=CacheHelperImageMissing' && echo "helper-refused-$variant"
            done
            """);
        output.Contains("available-main-helper").ShouldBeTrue($"available-main-helper {context}: {output}");
        foreach (var variant in new[] { "malformed-image", "foreign-main", "no-main", "no-image" })
            output.Contains("helper-refused-" + variant).ShouldBeTrue($"{context} refuses {variant}");
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C973_Prune_and_saved_donor_still_require_full_after_seed_image_prune()
    {
        RequireLinuxJq();
        var output = LinuxShell(C973ReaderHarness() + """
            for reader in c849_seed full-required; do
                result="$(run_reader "$reader" 0 1 saved)"; code=$?
                [ "$code" = 2 ] && printf '%s' "$result" | grep -Fq 'DIAGNOSIS=CacheFullSeedRequired' && echo "full-context-refused-$reader"
            done
            result="$(run_reader full-required 0 1 full)"; code=$?
            [ "$code" = 0 ] && echo full-marker-unchanged
            """);
        output.ShouldContain("full-context-refused-c849_seed");
        output.ShouldContain("full-context-refused-full-required");
        output.ShouldContain("full-marker-unchanged");
    }

    private static string C973ReaderHarness()
    {
        var fixtures = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "fixtures");
        // Materialize source bytes under Linux too, so Windows/WSL path mapping cannot hide a guard.
        return """
            fixture_root="$(mktemp -d /tmp/c973-readers-XXXXXXXX)"
            printf 'C973_READER_ROOT=%s\n' "$fixture_root"
            trap '[[ "$fixture_root" == /tmp/c973-readers-???????? && -d "$fixture_root" ]] && rm -rf -- "$fixture_root"' EXIT
            """ + "\ncat > \"$fixture_root/remote.sh\" <<'C973_REMOTE_BYTES'\n" + Remote() + "\nC973_REMOTE_BYTES\n" +
            "cat > \"$fixture_root/reader.sh\" <<'C973_READER_BYTES'\n" + File.ReadAllText(Path.Combine(fixtures, "c973-marker-reader.sh")) + "\nC973_READER_BYTES\n" +
            "cat > \"$fixture_root/marker\" <<'C973_MARKER_BYTES'\n" + File.ReadAllText(Path.Combine(fixtures, "c973-cold-seed-accepted.txt")) + "C973_MARKER_BYTES\n" +
            "run_reader() { bash \"$fixture_root/reader.sh\" \"$fixture_root/remote.sh\" \"$1\" \"$fixture_root/marker\" \"$2\" \"$3\" \"$4\"; }\n";
    }

    private static string CacheSeedTreeHarness()
    {
        var text = Remote();
        return """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            tree="$root/tree"
            mkdir -p "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native" "$tree/packages/microsoft.netcore.app.ref/9.0.20" "$tree/npm" "$root/outside"
            printf 'host-payload\n' > "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
            chmod 0755 "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
            printf 'metadata\n' > "$tree/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata"
            printf 'metadata\n' > "$tree/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata"
            printf 'keep\n' > "$root/sibling"
            """ + "\n" + Block(text, "c849_validate_seed_relative") + "\n" + Block(text, "c849_validate_seed_tree") + "\n";
    }

    private static string CachePruneHarness()
    {
        var text = Remote();
        return """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            mkdir -p "$root/case" "$root/server2/cache/previews" "$root/recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native" "$root/recovery/packages/microsoft.netcore.app.ref/9.0.20"
            CASE_DIR="$root/case"
            SERVER2_ROOT="$root/server2"
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            C849_PACKAGES=antiphon-runner-cache-nuget-packages
            C849_SCRATCH=antiphon-runner-cache-nuget-scratch
            C849_NPM=antiphon-runner-cache-npm-content
            C590_PREVIEW_RUN=c84900000000000000000
            SHA=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
            LANE=host
            : > "$root/docker-trace"
            printf 'host\n' > "$root/recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost"
            printf 'metadata\n' > "$root/recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/.nupkg.metadata"
            printf 'metadata\n' > "$root/recovery/packages/microsoft.netcore.app.ref/9.0.20/.nupkg.metadata"
            hash="$(sha256sum "$root/recovery/packages/microsoft.netcore.app.host.linux-x64/9.0.20/runtimes/linux-x64/native/apphost" | cut -d' ' -f1)"
            printf 'recovery=%s\npayload-sha256=%s\nimage=sha256:%064d\n' "$root/recovery" "$hash" 0 > "$C849_READY"
            require_lane() { [ "$1" = "$LANE" ]; }
            c849_lock() { :; }
            c849_prune_idle() { :; }
            c849_require_ready() { :; }
            c849_image() { echo image; }
            sudo() { [ "$1" = -n ] && shift; "$@"; }
            docker() { printf '%s\n' "$*" >> "$root/docker-trace"; [ "$1" = image ] && return 0; return 2; }
            write_result() { printf 'DIAGNOSIS=%s\n' "$2"; exit "$3"; }
            c849_observe_volume() {
                local path="$root/volumes/packages"
                [ "$2" = nuget-scratch ] && path="$SCRATCH_PATH"
                [ "$2" = npm-content ] && path="$root/volumes/npm"
                printf '%s %s %s 1654:1654:700 100000 100000\n' "$1" "$2" "$path"
            }
            make_preview() {
                SCRATCH_PATH="$1"
                local receipt="$SERVER2_ROOT/cache/previews/$C590_PREVIEW_RUN"
                mkdir -p "$receipt"
                c849_observe_volume "$C849_PACKAGES" nuget-packages 100 > "$receipt/volumes.txt"
                c849_observe_volume "$C849_SCRATCH" nuget-scratch 100 >> "$receipt/volumes.txt"
                c849_observe_volume "$C849_NPM" npm-content 100 >> "$receipt/volumes.txt"
                local hash
                hash="$(sha256sum "$receipt/volumes.txt" | cut -d' ' -f1)"
                printf 'run=%s\nsource-sha=%s\nvolume-sha256=%s\ncreated-at=%s\n' "$C590_PREVIEW_RUN" "$SHA" "$hash" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$receipt/preview.txt"
            }
            """ + "\n" + Block(text, "c849_prune_validate_tree") + "\n" + Block(text, "c849_prune") + "\n";
    }

    // The real Codex-home variables and function over a throwaway server2 root. sudo is a plain
    // call and the owner is the current uid, so the harness needs no privilege.
    private static string CachePrepareHarness()
    {
        var text = Remote();
        return """
            root="$(mktemp -d)"
            trap 'rm -rf "$root"' EXIT
            mkdir -p "$root/volumes" "$root/case" "$root/outside"
            CASE_DIR="$root/case"
            SERVER2_ROOT="$root/server2"
            RUN=c849test
            LANE=host
            FAULT=''
            C849_PACKAGES=antiphon-runner-cache-nuget-packages
            C849_SCRATCH=antiphon-runner-cache-nuget-scratch
            C849_NPM=antiphon-runner-cache-npm-content
            C849_READY="$SERVER2_ROOT/cache/seed-accepted"
            write_result() { printf 'RESULT accepted=%s diagnosis=%s\n' "$1" "$2"; exit "$3"; }
            require_lane() { [ "$1" = "$LANE" ]; }
            c849_lock() { :; }
            docker() {
                local verb="$1" sub="${2:-}" name format code mount path arg
                shift
                case "$verb:$sub" in
                    image:inspect) return 0 ;;
                    volume:inspect)
                        shift
                        if [ "${1:-}" = -f ]; then format="$2"; name="$3"; else name="$1"; fi
                        [ -e "$root/volumes/$name" ] || [ -L "$root/volumes/$name" ] || return 1
                        case "$format" in
                            *'.Driver'*) [ "$FAULT" = driver ] && echo nfs || echo local ;;
                            *'.Options'*) [ "$FAULT" = options ] && echo '{"device":"foreign"}' || echo '{}' ;;
                            *'io.antiphon.owner'*) [ "$FAULT" = owner ] && echo foreign || echo server2-runner ;;
                            *'io.antiphon.cache-schema'*) [ "$FAULT" = schema ] && echo 2 || echo 1 ;;
                            *'io.antiphon.cache-role'*)
                                if [ "$FAULT" = role ]; then echo wrong
                                elif [ "$name" = "$C849_SCRATCH" ]; then echo nuget-scratch
                                elif [ "$name" = "$C849_NPM" ]; then echo npm-content
                                else echo nuget-packages; fi ;;
                        esac
                        return 0 ;;
                    volume:create)
                        name="${@: -1}"
                        if [ "$FAULT" = symlink ]; then ln -s "$root/outside" "$root/volumes/$name"
                        elif [ "$FAULT" = file ]; then printf 'file\n' > "$root/volumes/$name"
                        else mkdir -p "$root/volumes/$name"; fi
                        echo "$name"; return 0 ;;
                    run:*)
                        for ((i=1;i<=$#;i++)); do
                            arg="${!i}"
                            if [ "$arg" = --mount ]; then j=$((i+1)); mount="${!j}"; fi
                            if [ "$arg" = -c ]; then j=$((i+1)); code="${!j}"; fi
                        done
                        name="${mount#*source=}"; name="${name%%,*}"
                        path="$root/volumes/$name"
                        case "$code" in
                            *'stat -c'*)
                                [ "$FAULT" = symlink ] && return 1
                                [ "$FAULT" = file ] && return 1
                                [ "$FAULT" = mode ] && echo 1654:1654:755 || echo 1654:1654:700
                                return 0 ;;
                            *'.c849-probe-'*)
                                [ -d "$path" ] || return 1
                                printf 'probe\n' > "$path/.c849-probe-$RUN"
                                mv "$path/.c849-probe-$RUN" "$path/.c849-probe-$RUN.moved"
                                rm "$path/.c849-probe-$RUN.moved"; return 0 ;;
                            *'find /cache -mindepth'*)
                                find "$path" -mindepth 1 -print -quit; return 0 ;;
                            *'chown 1654'*)
                                [ -d "$path" ] || return 1
                                chmod 0700 "$path"; return 0 ;;
                        esac
                        return 2 ;;
                esac
                return 2
            }
            """ + "\n" + string.Join('\n', new[] { "c849_image", "c849_empty_volume", "c849_volume", "c849_prepare" }
                .Select(name => Block(text, name))) + "\n";
    }

    private static string CacheStatusHarness() => Block(Remote(), "c849_status_zero") + "\n" + """
        root="$(mktemp -d)"
        trap 'rm -rf "$root"' EXIT
        STATUS=zero
        c849_status_body() { printf '%s' "$STATUS"; }
        jq() {
            local opt="$1" expression="$2" input
            input="$(cat)"
            if [ "$opt" = -r ]; then echo 2026-09-30T00:00:00Z; return 0; fi
            case "$input" in
                zero) return 0 ;;
                sessions|runnerSessions|queuedTasks|null|garbage|unknown) return 1 ;;
            esac
            return 1
        }
        """;

    private static string CodexHomeHarness(string text) =>
        string.Join('\n',
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            "cd \"$root\"",
            "CASE_DIR=\"$root/case\"; mkdir -p \"$CASE_DIR\"",
            "SERVER2_ROOT=\"$root/server2\"; mkdir -p \"$SERVER2_ROOT/secrets\"",
            "write_result() { printf 'RESULT accepted=%s diagnosis=%s\\n' \"$1\" \"$2\"; exit \"$3\"; }",
            Block(text, "ensure_runner_codex_home"))
        + "\n" + CodexHomeLines(text);

    // The script's own CODEX_HOME_ variables (after SERVER2_ROOT), then the host lane, a plain
    // sudo and the current uid as owner.
    private static string CodexHomeLines(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("CODEX_HOME_", StringComparison.Ordinal))
            .Concat(new[]
            {
                "LANE=host",
                "sudo() { if [ \"$1\" = -n ]; then shift; fi; \"$@\"; }",
                "CODEX_HOME_OWNER=\"$(id -u):$(id -g)\"",
            }))
        + "\n";

    // The real identity variables and functions, over a throwaway server2 root, with write_result
    // reduced to a printed verdict. Extra functions are extracted from the script verbatim.
    private static string IdentityHarness(string text, params string[] functions)
    {
        var variables = text.Replace("\r\n", "\n").Split('\n')
            .Where(line => line.StartsWith("GIT_IDENTITY_", StringComparison.Ordinal));
        return string.Join('\n', new[]
        {
            "root=\"$(mktemp -d)\"",
            "trap 'rm -rf \"$root\"' EXIT",
            // Not the inherited cwd: under WSL that is this worktree, whose .git names a C:/ path
            // Linux git cannot resolve, so every git call there would fail for the wrong reason.
            "cd \"$root\"",
            "CASE_DIR=\"$root/case\"; mkdir -p \"$CASE_DIR\"",
            "SERVER2_ROOT=\"$root/server2\"; mkdir -p \"$SERVER2_ROOT/secrets\"",
            "SERVER2_ENV=\"$SERVER2_ROOT/stack.env\"",
            "write_result() { printf 'RESULT accepted=%s diagnosis=%s\\n' \"$1\" \"$2\"; exit \"$3\"; }",
        }
            .Concat(variables)
            .Concat(new[] { "stack_env_value", "ensure_runner_git_identity" }.Concat(functions).Select(f => Block(text, f))))
            + "\n";
    }

    // The remote script only ever runs under Linux bash, and these defects are behaviour (a
    // symlink's target mode, a restart's ordering), not text. On Windows the Linux shell is WSL:
    // Git Bash can neither create a symlink without privilege nor keep a 0600 mode.
    internal static string LinuxShell(string script)
    {
        if (ForceNoLinuxPwsh.Value)
            throw new InvalidOperationException("A Linux script block ran before the CARD-0905 pwsh skip.");
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
            if (!File.Exists(wsl))
                Skip.Test("No Linux shell: wsl.exe is not installed, and c590-remote.sh only runs under Linux bash.");
            start = new ProcessStartInfo(wsl) { ArgumentList = { "-e", "bash", "-s" } };
        }
        else
        {
            start = new ProcessStartInfo("bash") { ArgumentList = { "-s" } };
        }

        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(script.Replace("\r\n", "\n"));
        process.StandardInput.Close();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the Linux shell harness did not finish in 60s");
        }

        process.WaitForExit();
        var output = stdout.Result;
        Console.WriteLine(output);
        Console.WriteLine(stderr.Result);
        return output;
    }

    private static void Refuses(IReadOnlyList<string> commands, string probe, string diagnosis)
    {
        var line = commands.Single(command => command.Contains(probe, StringComparison.Ordinal));
        (line.Contains("write_result false " + diagnosis + " 2", StringComparison.Ordinal)
            || commands.SkipWhile(command => command != line).Skip(1).FirstOrDefault()
                == "write_result false " + diagnosis + " 2")
            .ShouldBeTrue(probe + " refuses with " + diagnosis);
    }

    // Executable lines with backslash continuations joined, comments dropped.
    private static List<string> Commands(string text)
    {
        var commands = new List<string>();
        var pending = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (pending.Length == 0 && (line.Length == 0 || line[0] == '#'))
                continue;
            if (line.EndsWith('\\'))
            {
                pending += line[..^1].TrimEnd() + " ";
                continue;
            }

            commands.Add(pending + line);
            pending = "";
        }

        return commands;
    }

    private static bool Order(string text, string first, string second)
    {
        var a = text.IndexOf(first, StringComparison.Ordinal);
        var b = text.IndexOf(second, StringComparison.Ordinal);
        return a >= 0 && b >= 0 && a < b;
    }

    private static IReadOnlyList<string> Executable(string text, string token) =>
        text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#')
            .Where(line => line.Contains(token, StringComparison.Ordinal))
            .ToList();

    private static string EnsureDirsBody(string text) => Block(text, "ensure_dirs");

    private static string Block(string text, string function)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.StartsWith(function + "() {", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0, "function " + function + " is missing");
        var end = Array.FindIndex(lines, start + 1, line => line == "}");
        end.ShouldBeGreaterThan(start, "function " + function + " never closes");
        return string.Join('\n', lines[start..(end + 1)]);
    }

    private static string Remote() =>
        File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "c590-remote.sh"));
}
