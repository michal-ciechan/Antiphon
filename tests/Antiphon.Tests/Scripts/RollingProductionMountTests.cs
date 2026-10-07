using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class RollingProductionMountTests
{
    private static JsonObject Observation(C1008HostFixture f, string project) => new()
    {
        ["model"] = f.Docker["models"]![project]!.DeepClone(),
        ["owned"] = new JsonArray(f.Container('1', project, "session-runner", false), f.Container('2', project, "state-init", false)),
        ["volumes"] = f.Docker["volumes"]!.DeepClone(), ["project"] = project
    };

    // One Bash child per method; every case invokes the real production function.
    private static async Task Prove(string label, Action<C1008HostFixture, JsonObject, Action<string, bool, JsonObject>> cases)
    {
        using var f = new C1008HostFixture();
        var vectors = new JsonArray(); var expected = new Dictionary<string, bool>(StringComparer.Ordinal);
        var expectedDiagnosis = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, bool accepted, JsonObject input) {
            expected.Add(key, accepted);
            if (input["expectDiagnosis"] is JsonValue diagnosis)
                expectedDiagnosis.Add(key, diagnosis.GetValue<string>());
            var copy = input.DeepClone().AsObject();
            copy.Remove("expectDiagnosis");
            vectors.Add(new JsonObject { ["key"] = key, ["input"] = copy });
        }
        foreach (var project in new[] { "antiphon-runner", "antiphon-runner-temp" }) {
            var good = Observation(f, project); Add(project + ":valid", true, good);
            cases(f, good, (key, success, input) => Add(project + ":" + key, success, input));
        }
        File.WriteAllText(Path.Combine(f.Root, "mount-vectors.json"), vectors.ToJsonString());
        var command = """
            LANE=host
            while IFS= read -r vector; do
                key="$(printf '%s' "$vector" | jq -r .key)"
                (
                    input="$(printf '%s' "$vector" | jq -c .input)"
                    C1008_PROJECT="$(printf '%s' "$input" | jq -r .project)"
                    C994_FIXTURE_COMPOSE_JSON="$(printf '%s' "$input" | jq -c .model)"
                    owned="$(printf '%s' "$input" | jq -c .owned)"
                    volumes="$(printf '%s' "$input" | jq -c .volumes)"
                    override="$(printf '%s' "$input" | jq -r '.codexOverride // empty')"
                    [ -z "$override" ] || CODEX_HOME_PATH="$override"
                    override="$(printf '%s' "$input" | jq -r '.gitOverride // empty')"
                    [ -z "$override" ] || GIT_IDENTITY_PATH="$override"
                    metadataFault="$(printf '%s' "$input"|jq -r '.metadataFault // empty')"
                    if [ -n "$metadataFault" ];then sudo(){ [ "$1" = -n ] && shift;[ "$1" != "$metadataFault" ] || return 2;command "$@"; };fi
                    compose_host() { printf '%s' "$C994_FIXTURE_COMPOSE_JSON"; }
                    compose_temp() { printf '%s' "$C994_FIXTURE_COMPOSE_JSON"; }
                    c1008_compose_source() { :; }
                    C1008_PREVIOUS_SHA="$(printf '%s' "$input" | jq -r '.previousSha // "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"')"
                    c1008_previous_generation() {
                        C1008_PREVIOUS_SHA="$(printf '%s' "$input" | jq -r '.previousSha // "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"')"
                        printf '%s\n' '{"statusBuildVersion":null,"imageTag":null,"imageId":null}'
                    }
                    c1008_refuse() { printf 'DIAGNOSIS=%s\n' "$1"; exit 2; }
                    generation="$(printf '%s' "$input" | jq -r '.generation // "target"')"
                    model_code=0
                    if [ "$generation" = previous ]; then
                        model="$(c1008_previous_model "$C1008_PREVIOUS_SHA")" || model_code=$?
                    else
                        model="$(c1008_compose_model)" || model_code=$?
                    fi
                    if [ "$model_code" != 0 ]; then printf '%s\n' "$model"; exit "$model_code"; fi
                    for role in work runner-state runner-tmp dind-data; do
                        name="${C1008_PROJECT}_$role"
                        c1008_private_identity "$(printf '%s' "$volumes" | jq -c --arg n "$name" '.[$n]')" "$name" "$C1008_PROJECT" "$role" || exit 2
                    done
                    c1008_owned_mounts "$owned" "$model" "$volumes"
                    printf '%s' "$C1008_OWNED" | jq -e 'all(.[]; .Topology.version==1)' >/dev/null
                ) >"$C1008_FIXTURE_ROOT/mount-case.log" 2>&1 && code=0 || code=$?
                diagnosis="$(sed -n 's/^DIAGNOSIS=//p' "$C1008_FIXTURE_ROOT/mount-case.log" | tail -n 1)"
                printf 'MOUNT_CASE %s %s %s\n' "$key" "$code" "$diagnosis"
            done < <(jq -c '.[]' "$C1008_FIXTURE_ROOT/mount-vectors.json")
            write_result true '' 0
            """;
        var run = await f.Run(extra: command);
        run.Exit.ShouldBe(0, label + ": child; " + run.Output);
        var actual = run.Output.Split('\n').Where(x => x.StartsWith("MOUNT_CASE ", StringComparison.Ordinal))
            .Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(x => x[1], x => (Ok: x[2] == "0", Diagnosis: x.Length > 3 ? x[3] : ""), StringComparer.Ordinal);
        actual.Keys.Order().ShouldBe(expected.Keys.Order(), label + ": exact case roster");
        foreach (var (key, value) in expected) actual[key].Ok.ShouldBe(value, label + ": " + key + " diagnosis=" + actual[key].Diagnosis);
        foreach (var (key, diagnosis) in expectedDiagnosis)
            actual[key].Diagnosis.ShouldBe(diagnosis, label + ": " + key);
    }

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Compose_model_preserves_service_specific_mounts() => Prove("c994-model-targets", (_, good, add) => {
        foreach (var service in new[] { "state-init", "session-runner" }) {
            var bad = good.DeepClone().AsObject();
            var state = bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x => x!["source"]!.GetValue<string>() == "runner-state")!;
            state["target"] = service == "state-init" ? "/state" : "/runner-state"; add(service + ":swap", false, bad);
        }
        var variants = new[] { "external", "nocopy", "kind", "options", "missing", "extra", "duplicate", "read-only-type", "unused-volume", "unused-secret" };
        foreach (var variant in variants) {
            var bad = good.DeepClone().AsObject(); var mounts = bad["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray();
            switch (variant) {
                case "unused-volume": bad["model"]!["volumes"]!["foreign"] = new JsonObject{["name"]="foreign"}; break;
                case "unused-secret": bad["model"]!["secrets"]!["foreign"] = new JsonObject{["file"]="/foreign"}; break;
                case "external": bad["model"]!["volumes"]!["work"]!["external"] = true; break;
                case "nocopy": mounts.Single(x => x!["source"]!.GetValue<string>() == "runner-nuget-packages")!["volume"]!["nocopy"] = false; break;
                case "kind": mounts[0]!["type"] = "tmpfs"; break;
                case "options": mounts[0]!["volume"]!["subpath"] = "foreign"; break;
                case "missing": mounts.RemoveAt(0); break;
                case "extra": mounts.Add(new JsonObject { ["type"]="bind", ["source"]="/foreign", ["target"]="/foreign" }); break;
                case "duplicate": mounts[1]!["target"] = mounts[0]!["target"]!.DeepClone(); break;
                case "read-only-type": mounts[0]!["read_only"] = "false"; break;
            }
            add(variant, false, bad);
        }
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Named_volume_identity_and_topology_are_exact() => Prove("c994-volume-mount c994-volume-owner", (_, good, add) => {
        foreach (var field in new[] { "Name", "Source", "Destination", "Type", "RW", "missing-RW", "string-RW", "duplicate", "owner", "role", "generation", "anonymous", "mountpoint" }) {
            var bad = good.DeepClone().AsObject(); var mounts = bad["owned"]![0]!["Mounts"]!.AsArray(); var mount = mounts[0]!.AsObject();
            if (field == "missing-RW") mount.Remove("RW");
            else if (field == "string-RW") mount["RW"] = "true";
            else if(field=="generation")bad["volumes"]![mount["Name"]!.GetValue<string>()]!["CreatedAt"]=null;
            else if(field=="anonymous")mount["Name"]="";
            else if(field=="mountpoint")bad["volumes"]![mount["Name"]!.GetValue<string>()]!["Mountpoint"]="/foreign";
            else if (field == "RW") mount["RW"] = false;
            else if (field == "duplicate") mounts.Add(mount.DeepClone());
            else if (field is "owner" or "role") bad["volumes"]![mount["Name"]!.GetValue<string>()]!["Labels"]!["com.docker.compose." + (field == "owner" ? "project" : "volume")] = "foreign";
            else mount[field] = "foreign";
            add(field, false, bad);
        }
        var reordered = good.DeepClone().AsObject();
        foreach (var container in reordered["owned"]!.AsArray()) {
            var mounts = container!["Mounts"]!.AsArray(); var copy = mounts.Reverse().Select(x => x!.DeepClone()).ToArray();
            mounts.Clear(); foreach (var m in copy) mounts.Add(m);
        }
        add("unordered", true, reordered);
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Bind_sources_are_canonical_and_typed() => Prove("c994-bind-proof", (f, good, add) => {
        foreach (var variant in new[] { "source", "target", "rw", "missing", "extra", "directory-for-file", "symlink", "ancestor", "file-for-directory", "readlink", "stat", "provider-missing" }) {
            var bad = good.DeepClone().AsObject(); var mounts = bad["owned"]![0]!["Mounts"]!.AsArray();
            var bind = mounts.Single(x => x!["Destination"]!.GetValue<string>() == "/run/antiphon/gitconfig")!;
            switch (variant) {
                case "source": bind["Source"] = f.Root + "/missing"; break;
                case "target": bind["Destination"] = "/unexpected"; break;
                case "rw": bind["RW"] = true; break;
                case "missing": mounts.Remove(bind); break;
                case "extra": mounts.Add(new JsonObject { ["Type"]="bind", ["Source"]=f.Root, ["Destination"]="/foreign", ["RW"]=true }); break;
                case "directory-for-file": File.Delete(f.Root + "/gitconfig"); Directory.CreateDirectory(f.Root + "/gitconfig"); break;
                case "readlink": case "stat": bad["metadataFault"]=variant;break;
                case "provider-missing": mounts.Remove(mounts.Single(x=>x!["Destination"]!.GetValue<string>()=="/state/codex"));break;
                case "file-for-directory":
                    var file=f.Root+"/not-directory";File.WriteAllText(file,"inert");bad["codexOverride"]=file;
                    foreach(var service in new[]{"session-runner","state-init"}){
                        bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x=>x!["source"]!.GetValue<string>()==f.Root+"/codex")!["source"]=file;
                        bad["owned"]![service=="session-runner"?0:1]!["Mounts"]!.AsArray().Single(x=>x!["Source"]!.GetValue<string>()==f.Root+"/codex")!["Source"]=file;
                    }break;
                case "ancestor":
                    var parent=f.Root+"/parent-link";if(!Directory.Exists(parent))Directory.CreateSymbolicLink(parent,f.Root);
                    var child=parent+"/codex";bad["codexOverride"]=child;
                    foreach(var service in new[]{"session-runner","state-init"}){
                        bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x=>x!["source"]!.GetValue<string>()==f.Root+"/codex")!["source"]=child;
                        bad["owned"]![service=="session-runner"?0:1]!["Mounts"]!.AsArray().Single(x=>x!["Source"]!.GetValue<string>()==f.Root+"/codex")!["Source"]=child;
                    }break;
                case "symlink":
                    var link = f.Root + "/codex-link";
                    if (!Directory.Exists(link)) Directory.CreateSymbolicLink(link, f.Root + "/codex");
                    bad["codexOverride"] = link;
                    foreach (var service in new[] { "session-runner", "state-init" }) {
                        bad["model"]!["services"]![service]!["volumes"]!.AsArray().Single(x => x!["source"]!.GetValue<string>() == f.Root + "/codex")!["source"] = link;
                        bad["owned"]![service == "session-runner" ? 0 : 1]!["Mounts"]!.AsArray().Single(x => x!["Source"]!.GetValue<string>() == f.Root + "/codex")!["Source"] = link;
                    }
                    break;
            }
            // File-kind faults are represented by a separately owned source so
            // valid controls still see the original metadata at execution time.
            if (variant == "directory-for-file") {
                Directory.Delete(f.Root + "/gitconfig"); File.WriteAllText(f.Root + "/gitconfig", "inert");
                bad["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray().Single(x => x!["target"]!.GetValue<string>() == "/run/antiphon/gitconfig")!["source"] = f.Root;
                bad["gitOverride"] = f.Root; bind["Source"] = f.Root;
            }
            add(variant, false, bad);
        }
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Secrets_match_read_only_files() => Prove("c994-secret-proof", (_, good, add) => {
        foreach (var variant in new[] { "source", "target", "external", "rw", "missing-rw", "string-rw", "missing", "duplicate" }) {
            var bad = good.DeepClone().AsObject(); var model = bad["model"]!; var mounts = bad["owned"]![0]!["Mounts"]!.AsArray();
            var secret = mounts.Single(x => x!["Destination"]!.GetValue<string>() == "/run/secrets/phone-home")!.AsObject();
            switch (variant) {
                case "source": model["secrets"]!["phone-home"]!["file"] = "/foreign"; break;
                case "target": model["services"]!["session-runner"]!["secrets"]![0]!["target"] = "/foreign"; break;
                case "external": model["secrets"]!["phone-home"]!["external"] = true; break;
                case "rw": secret["RW"] = true; break;
                case "missing-rw": secret.Remove("RW"); break;
                case "string-rw": secret["RW"] = "false"; break;
                case "missing": mounts.Remove(secret); break;
                case "duplicate": mounts.Add(secret.DeepClone()); break;
            }
            add(variant, false, bad);
        }
        var shortTarget = good.DeepClone().AsObject();
        foreach (var secret in shortTarget["model"]!["services"]!["session-runner"]!["secrets"]!.AsArray()) secret!["target"] = secret["source"]!.DeepClone();
        add("short-target", true, shortTarget);
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public Task C994_Tmpfs_configuration_survives_exit() => Prove("c994-tmpfs-proof", (_, good, add) => {
        foreach (var variant in new[] { "running", "inspected", "mount-encoding", "missing", "contradiction", "extra", "readonly", "options", "impostor", "state-init-tmpfs", "duplicate-config", "malformed-options" }) {
            var bad = good.DeepClone().AsObject(); var container = bad["owned"]![0]!;
            switch (variant) {
                case "running": container["State"]!["Running"] = true; container["State"]!["Status"] = "running"; break;
                case "inspected": container["Mounts"]!.AsArray().Add(new JsonObject { ["Type"]="tmpfs", ["Destination"]="/run/antiphon", ["RW"]=true, ["Source"]="" }); break;
                case "mount-encoding": container["HostConfig"]!["Tmpfs"] = new JsonObject(); container["HostConfig"]!["Mounts"] = new JsonArray(new JsonObject { ["Type"]="tmpfs", ["Target"]="/run/antiphon", ["ReadOnly"]=false, ["TmpfsOptions"]=new JsonObject() }); break;
                case "missing": container["HostConfig"] = new JsonObject(); break;
                case "contradiction": container["HostConfig"]!["Mounts"] = new JsonArray(new JsonObject { ["Type"]="tmpfs", ["Target"]="/run/antiphon", ["ReadOnly"]=true }); break;
                case "extra": container["HostConfig"]!["Tmpfs"]!["/foreign"] = ""; break;
                case "readonly": container["HostConfig"]!["Tmpfs"]!["/run/antiphon"] = "ro"; break;
                case "options": container["HostConfig"]!["Tmpfs"]!["/run/antiphon"] = "size=123"; break;
                case "state-init-tmpfs":bad["owned"]![1]!["HostConfig"]!["Tmpfs"]!["/foreign"]="";break;
                case "duplicate-config":container["HostConfig"]!["Mounts"]=new JsonArray(new JsonObject{["Type"]="tmpfs",["Target"]="/run/antiphon"},new JsonObject{["Type"]="tmpfs",["Target"]="/run/antiphon"});break;
                case "malformed-options":container["HostConfig"]!["Tmpfs"]!["/run/antiphon"]=1;break;
                case "impostor": container["Mounts"]!.AsArray().Add(new JsonObject { ["Type"]="bind", ["Source"]="/foreign", ["Destination"]="/run/antiphon", ["RW"]=true }); break;
            }
            add(variant, variant is "running" or "inspected" or "mount-encoding", bad);
        }
    });

    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_Previous_generation_roster_is_derived_from_its_compose()
    {
        C1008HostFixture.RequireNativeLinux();
        await Prove("c1105-previous-roster", (f, good, add) => {
            var project = good["project"]!.GetValue<string>();
            var previousModel = MaterializePrevious(f.Root, project, project.EndsWith("-temp", StringComparison.Ordinal));
            var previousOwned = OwnedFrom(f, project, previousModel);
            var previous = good.DeepClone().AsObject();
            previous["model"] = previousModel.DeepClone();
            previous["owned"] = previousOwned;
            previous["generation"] = "previous";
            add("previous-accepts", true, previous);

            var targetRefuses = good.DeepClone().AsObject();
            targetRefuses["owned"] = previousOwned.DeepClone();
            targetRefuses["expectDiagnosis"] = "RecycleContainerStateUnknown";
            add("target-refuses-previous-container", false, targetRefuses);

            var eleventh = previous.DeepClone().AsObject();
            eleventh["owned"] = good["owned"]!.DeepClone();
            eleventh["expectDiagnosis"] = "RecycleContainerStateUnknown";
            add("eleventh-against-previous", false, eleventh);

            foreach (var generation in new[] { "previous", "target" }) {
                var foreign = (generation == "previous" ? previous : good).DeepClone().AsObject();
                if (generation == "previous") foreign["generation"] = "previous";
                foreign["owned"]![0]!["Mounts"]!.AsArray().Add(new JsonObject {
                    ["Type"] = "bind", ["Source"] = f.Root, ["Destination"] = "/foreign", ["RW"] = true });
                foreign["expectDiagnosis"] = "RecycleContainerStateUnknown";
                add("foreign-" + generation, false, foreign);
            }

            var unknown = previous.DeepClone().AsObject();
            unknown["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray().Add(new JsonObject {
                ["type"] = "bind", ["source"] = f.Root, ["target"] = "/foreign", ["read_only"] = true,
                ["bind"] = new JsonObject { ["create_host_path"] = true } });
            unknown["expectDiagnosis"] = "RecycleGenerationUnknown";
            add("unknown-bind", false, unknown);

            var duplicate = previous.DeepClone().AsObject();
            var mounts = duplicate["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray();
            mounts[1]!["target"] = mounts[0]!["target"]!.DeepClone();
            duplicate["expectDiagnosis"] = "RecycleGenerationUnknown";
            add("duplicate-target", false, duplicate);

            var relative = previous.DeepClone().AsObject();
            relative["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray()
                .First(x => x!["type"]!.GetValue<string>() == "bind")!["source"] = "relative/path";
            relative["expectDiagnosis"] = "RecycleGenerationUnknown";
            add("relative-bind", false, relative);

            var undeclared = previous.DeepClone().AsObject();
            undeclared["model"]!["services"]!["session-runner"]!["volumes"]!.AsArray()
                .First(x => x!["source"]!.GetValue<string>() == "work")!["source"] = "missing-role";
            undeclared["expectDiagnosis"] = "RecycleGenerationUnknown";
            add("undeclared-volume", false, undeclared);

            var missing = previous.DeepClone().AsObject();
            missing["model"]!["volumes"]!.AsObject().Remove("work");
            foreach (var service in new[] { "session-runner", "state-init" }) {
                var serviceMounts = missing["model"]!["services"]![service]!["volumes"]!.AsArray();
                for (var i = serviceMounts.Count - 1; i >= 0; i--)
                    if (serviceMounts[i]!["source"]!.GetValue<string>() == "work") serviceMounts.RemoveAt(i);
            }
            missing["expectDiagnosis"] = "RecycleGenerationUnknown";
            add("missing-recycle-volume", false, missing);
        });
        await ProveGenerationIdentity();
    }

    // Names the c1008_compose_model target guard. A missing service-secret target defaults to
    // the source name. Reverting that guard, or the declaration allowed list, refuses this fixture.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_V218_compose_model_accepts_the_host_shape()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture();
        var accept = C1008HostFixture.LoadComposeV218(f.Root);
        SessionVolumeBinds(accept).ShouldBe(11);
        var shortSet = accept.DeepClone().AsObject();
        var mounts = shortSet["services"]!["session-runner"]!["volumes"]!.AsArray();
        mounts.Remove(mounts.Single(x => x!["target"]!.GetValue<string>() == "/run/antiphon/github-token"));
        var external = accept.DeepClone().AsObject();
        external["secrets"]!["phone-home"]!["external"] = true;
        WriteV218(f, "v218-target.json", accept);
        WriteV218(f, "v218-short.json", shortSet);
        WriteV218(f, "v218-external.json", external);
        var run = await f.Run(extra: V218ModelProbe("c1008_compose_model"));
        run.Exit.ShouldBe(0, "v218-compose-model: " + run.Output);
        Case(run.Output, "target").ShouldBe(("accept", "11"));
        Case(run.Output, "short").ShouldBe(("refuse", "RecycleComposeMismatch"));
        Case(run.Output, "external").ShouldBe(("refuse", "RecycleComposeMismatch"));
    }

    // Names the c1008_previous_model declaration allowed list. external:false is the v2.18.1
    // declaration shape; external:true and an unknown bind stay RecycleGenerationUnknown.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_V218_previous_model_accepts_the_host_shape()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture();
        var accept = C1008HostFixture.LoadComposeV218(f.Root);
        var external = accept.DeepClone().AsObject();
        external["secrets"]!["antiphon-deploy-key"]!["external"] = true;
        var foreign = accept.DeepClone().AsObject();
        foreign["services"]!["session-runner"]!["volumes"]!.AsArray().Add(new JsonObject {
            ["type"] = "bind", ["source"] = f.Root + "/foreign", ["target"] = "/foreign", ["read_only"] = true,
            ["bind"] = new JsonObject { ["create_host_path"] = true } });
        WriteV218(f, "v218-target.json", accept);
        WriteV218(f, "v218-external.json", external);
        WriteV218(f, "v218-short.json", foreign);
        var run = await f.Run(extra: V218ModelProbe("c1008_previous_model \"$C1008_PREVIOUS_SHA\""));
        run.Exit.ShouldBe(0, "v218-previous-model: " + run.Output);
        Case(run.Output, "target").ShouldBe(("accept", "11"));
        Case(run.Output, "external").ShouldBe(("refuse", "RecycleGenerationUnknown"));
        Case(run.Output, "short").ShouldBe(("refuse", "RecycleGenerationUnknown"));
    }

    // An explicit relative target is mounted at /run/secrets/<target>. "elsewhere" is not the
    // roster destination, so both filters refuse before any caller can recycle.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_Relative_secret_target_is_refused()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture();
        WriteSecretCase(f, "elsewhere", _ => JsonValue.Create("elsewhere"));
        await AssertSecretCase(f, "elsewhere", expectAccept: false);
    }

    // A relative target equal to the secret's own name is the Compose default destination.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_Relative_secret_target_equal_to_its_name_is_accepted()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture();
        WriteSecretCase(f, "own-name", name => JsonValue.Create(name));
        await AssertSecretCase(f, "own-name", expectAccept: true);
    }

    // Absolute targets stay on the base-commit rule: the path must be /run/secrets/<source>.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_Absolute_secret_target_keeps_the_base_rule()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture();
        WriteSecretCase(f, "absolute-match", name => JsonValue.Create("/run/secrets/" + name));
        WriteSecretCase(f, "absolute-other", _ => JsonValue.Create("/run/secrets/elsewhere"));
        foreach (var (render, diagnosis) in SecretFilters)
        {
            var run = await f.Run(extra: V218ModelProbe(render, "absolute-match", "absolute-other"));
            run.Exit.ShouldBe(0, render + ": " + run.Output);
            run.Output.ShouldNotContain("startswith() requires string inputs");
            Case(run.Output, "absolute-match").ShouldBe(("accept", "11"), render);
            Case(run.Output, "absolute-other").ShouldBe(("refuse", diagnosis), render);
        }
    }

    // Explicit null, empty, and non-string targets are refusals. A missing key is not this case.
    [Test, ParallelLimiter<ProcessSpawnLimit>]
    public async Task C1105_Malformed_secret_target_is_refused()
    {
        C1008HostFixture.RequireNativeLinux();
        using var f = new C1008HostFixture();
        WriteExplicitNullTargets(f, "null-target");
        WriteSecretCase(f, "empty-target", _ => JsonValue.Create(""));
        WriteSecretCase(f, "number-target", _ => JsonValue.Create(1));
        WriteSecretCase(f, "false-target", _ => JsonValue.Create(false));
        WriteSecretCase(f, "object-target", _ => new JsonObject());
        var labels = new[] { "null-target", "empty-target", "number-target", "false-target", "object-target" };
        foreach (var (render, diagnosis) in SecretFilters)
        {
            var run = await f.Run(extra: V218ModelProbe(render, labels));
            run.Exit.ShouldBe(0, render + ": " + run.Output);
            run.Output.ShouldNotContain("startswith() requires string inputs");
            foreach (var label in labels)
                Case(run.Output, label).ShouldBe(("refuse", diagnosis), render + " " + label);
        }
    }

    private static readonly (string Render, string Diagnosis)[] SecretFilters =
    [
        ("c1008_compose_model", "RecycleComposeMismatch"),
        ("c1008_previous_model \"$C1008_PREVIOUS_SHA\"", "RecycleGenerationUnknown"),
    ];

    private static async Task AssertSecretCase(C1008HostFixture f, string label, bool expectAccept)
    {
        foreach (var (render, diagnosis) in SecretFilters)
        {
            var run = await f.Run(extra: V218ModelProbe(render, label));
            run.Exit.ShouldBe(0, label + " " + render + ": " + run.Output);
            run.Output.ShouldNotContain("startswith() requires string inputs");
            Case(run.Output, label).ShouldBe(expectAccept ? ("accept", "11") : ("refuse", diagnosis), render);
        }
    }

    // JsonNode's indexer returns C# null for a JSON null, and ToJsonString then omits the key.
    // The missing-key shape is the v2.18 acceptance case, so this writer keeps an explicit null.
    private static void WriteExplicitNullTargets(C1008HostFixture f, string label)
    {
        var text = C1008HostFixture.LoadComposeV218(f.Root).ToJsonString();
        var updated = text
            .Replace("\"source\":\"antiphon-deploy-key\"", "\"source\":\"antiphon-deploy-key\",\"target\":null", StringComparison.Ordinal)
            .Replace("\"source\":\"phone-home\"", "\"source\":\"phone-home\",\"target\":null", StringComparison.Ordinal);
        if (updated.Split("\"target\":null").Length != 3)
            throw new InvalidOperationException("explicit null targets were not written: " + text);
        File.WriteAllText(Path.Combine(f.Root, "v218-" + label + ".json"), updated);
    }

    private static void WriteSecretCase(C1008HostFixture f, string label, Func<string, JsonNode?> target)
    {
        var model = C1008HostFixture.LoadComposeV218(f.Root);
        foreach (var secret in model["services"]!["session-runner"]!["secrets"]!.AsArray())
        {
            var name = secret!["source"]!.GetValue<string>();
            var value = target(name);
            if (value is null) secret.AsObject().Remove("target");
            else secret["target"] = value;
        }
        WriteV218(f, "v218-" + label + ".json", model);
    }

    private static void WriteV218(C1008HostFixture f, string name, JsonNode model) =>
        File.WriteAllText(Path.Combine(f.Root, name), model.ToJsonString());

    private static int SessionVolumeBinds(JsonObject model) =>
        model["services"]!["session-runner"]!["volumes"]!.AsArray()
            .Count(x => x!["type"]!.GetValue<string>() is "volume" or "bind");

    private static (string Outcome, string Detail) Case(string output, string label)
    {
        var line = output.Split('\n').Single(x => x.StartsWith("V218_CASE " + label + " ", StringComparison.Ordinal));
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[2], parts[3]);
    }

    private static string V218ModelProbe(string render, params string[] labels)
    {
        if (labels.Length == 0) labels = ["target", "short", "external"];
        var calls = string.Join('\n', labels.Select(label =>
            $"            run_case {label} \"$C1008_FIXTURE_ROOT/v218-{label}.json\""));
        var sha = new string('b', 40);
        return $$"""
            LANE=host
            C1008_PROJECT="$HOST_PROJECT"
            C1008_PREVIOUS_SHA={{sha}}
            c1008_compose_source() { :; }
            c1008_refuse() { printf 'DIAGNOSIS=%s\n' "$1"; exit 2; }
            compose_host() { printf '%s' "$C1105_V218_MODEL"; }
            compose_temp() { printf '%s' "$C1105_V218_MODEL"; }
            run_case() {
                label="$1"
                C1105_V218_MODEL="$(cat "$2")"
                model_code=0
                model="$({{render}})" || model_code=$?
                if [ "$model_code" != 0 ]; then
                    diagnosis="$(printf '%s\n' "$model" | sed -n 's/^DIAGNOSIS=//p' | tail -n 1)"
                    printf 'V218_CASE %s refuse %s\n' "$label" "$diagnosis"
                    return 0
                fi
                count="$(c1008_session_mount_count "$model")" || { printf 'V218_CASE %s count-fail 0\n' "$label"; return 0; }
                printf 'V218_CASE %s accept %s\n' "$label" "$count"
            }
            {{calls}}
            write_result true '' 0
            """;
    }

    private static JsonArray OwnedFrom(C1008HostFixture f, string project, JsonObject model)
    {
        var saved = f.Docker["models"]![project]!.DeepClone();
        f.Docker["models"]![project] = model.DeepClone();
        var owned = new JsonArray(f.Container('1', project, "session-runner", false), f.Container('2', project, "state-init", false));
        f.Docker["models"]![project] = saved;
        return owned;
    }

    private static JsonObject MaterializePrevious(string root, string project, bool temp)
    {
        var psi = new ProcessStartInfo("node") { UseShellExecute = false, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = DelegateScriptRunner.RepoRoot };
        foreach (var arg in new[] { "scripts/fixtures/c994-production-compose-model.mjs", root, project,
                     temp ? "true" : "false", "previous" }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); process.WaitForExit(); throw new InvalidOperationException("Previous compose timeout"); }
        if (process.ExitCode != 0) throw new InvalidOperationException("Previous compose: " + stderr.GetAwaiter().GetResult());
        var model = JsonNode.Parse(stdout.GetAwaiter().GetResult())!["model"]!.AsObject();
        model["services"]!["session-runner"]!["volumes"]!.AsArray()
            .Any(x => x!["target"]!.GetValue<string>() == "/run/antiphon/github-token")
            .ShouldBeFalse("previous render still carries the token bind");
        return model;
    }

    // Real identity function, not the roster stub: each new refusal exits before any destructive docker call.
    private static async Task ProveGenerationIdentity()
    {
        using var f = new C1008HostFixture();
        var sha = new string('b', 40);
        var other = new string('c', 40);
        var command = """
            LANE=host
            C1008_PROJECT="$HOST_PROJECT"
            if [ -n "${C1008_COMPOSE_SOURCE_BODY:-}" ]; then eval "$C1008_COMPOSE_SOURCE_BODY"; fi
            owned="$(jq -c '[.containers[] | select(.Config.Labels["com.docker.compose.service"]=="session-runner" or .Config.Labels["com.docker.compose.service"]=="state-init")]' "$C1008_FIXTURE_ROOT/docker.json")"
            none="$(jq -c '[.containers[] | select(.Config.Labels["com.docker.compose.service"]=="build-slots")]' "$C1008_FIXTURE_ROOT/docker.json")"
            journal="$SERVER2_ROOT/recycle"
            sha=__SHA__
            other=__OTHER__
            show_generation() {
                c1008_previous_generation "$@" >"$C1008_FIXTURE_ROOT/generation.out"
                gen="$(cat "$C1008_FIXTURE_ROOT/generation.out")"
                if [ -z "${C1008_PREVIOUS_SHA-}" ]; then printf 'PREV_SHA=empty\n'
                else printf 'PREV_SHA=%s\n' "$C1008_PREVIOUS_SHA"; fi
                printf 'GENERATION=%s\n' "$gen"
            }
            source_missing() {
                CHECKOUT="$C1008_FIXTURE_ROOT/empty-git"
                c1008_previous_model "$sha"
            }
            check_bind_kinds() {
                [ "$(c1008_bind_kind /run/antiphon/claude-oauth-token)" = true ]
                [ "$(c1008_bind_kind /run/antiphon/gitconfig)" = true ]
                [ "$(c1008_bind_kind /run/antiphon/github-token)" = false ]
                [ "$(c1008_bind_kind /state/codex)" = false ]
                [ "$(c1008_bind_kind /codex-home)" = false ]
                [ "$(c1008_bind_kind /state/grok)" = false ]
                kind_code=0
                c1008_bind_kind /not-a-shipped-target || kind_code=$?
                [ "$kind_code" = 2 ]
            }
            check_keyed() {
                got="$(C1008_COMPOSE_DIR="$C1008_FIXTURE_ROOT/compose/bbbbbbbbbbbb" docker compose -p antiphon-runner config --format json)"
                printf '%s' "$got" | jq -e '.marker==true' >/dev/null
            }
            run_case() {
                key="$1"; shift
                (
                    "$@"
                ) >"$C1008_FIXTURE_ROOT/identity.log" 2>&1 && code=0 || code=$?
                diagnosis="$(sed -n 's/^DIAGNOSIS=//p' "$C1008_FIXTURE_ROOT/identity.log" | tail -n 1)"
                sha_out="$(sed -n 's/^PREV_SHA=//p' "$C1008_FIXTURE_ROOT/identity.log" | tail -n 1)"
                gen="$(sed -n 's/^GENERATION=//p' "$C1008_FIXTURE_ROOT/identity.log" | tail -n 1)"
                journal_present=0
                [ -e "$journal" ] && journal_present=1
                if [ -f "$C1008_FIXTURE_ROOT/docker-trace.jsonl" ]; then
                    stop="$(jq -s '[.[] | select(.[0]=="stop" or .[0]=="rm" or (.[0]=="volume" and .[1]=="rm"))] | length' "$C1008_FIXTURE_ROOT/docker-trace.jsonl")"
                else stop=0; fi
                printf 'IDENTITY %s %s %s %s %s %s\n' "$key" "$code" "${diagnosis:-none}" "$journal_present" "$stop" "${sha_out:-none}"
                printf 'IDENTITY_GEN %s %s\n' "$key" "${gen:-none}"
            }
            run_case none-owned show_generation "$HOST_PROJECT" "$none"
            rm -f -- "$SERVER2_ENV"
            run_case missing-stack c1008_previous_generation "$HOST_PROJECT" "$owned"
            printf 'SOURCE_REVISION=old\n' > "$SERVER2_ENV"
            run_case stack-not-hex c1008_previous_generation "$HOST_PROJECT" "$owned"
            printf 'SOURCE_REVISION=%s\n' "$sha" > "$SERVER2_ENV"
            C1008_STATUS="$(jq -cn --arg s "$other" '{buildVersion:$s}')"
            run_case status-mismatch c1008_previous_generation "$HOST_PROJECT" "$owned"
            printf 'SOURCE_REVISION=%s\n' "$sha" > "$SERVER2_ENV"
            printf 'SOURCE_REVISION=%s\n' "$other" > "$SERVER2_TEMP_ENV"
            C1008_STATUS="$(jq -cn --arg s "$sha" '{buildVersion:$s}')"
            run_case temp-env-mismatch c1008_previous_generation "$TEMP_PROJECT" "$owned"
            jq '.fault="image-inspect-wrong"' "$C1008_FIXTURE_ROOT/docker.json" > "$C1008_FIXTURE_ROOT/docker.json.new"
            mv -f -- "$C1008_FIXTURE_ROOT/docker.json.new" "$C1008_FIXTURE_ROOT/docker.json"
            printf 'SOURCE_REVISION=%s\n' "$sha" > "$SERVER2_ENV"
            C1008_STATUS="$(jq -cn --arg s "$sha" '{buildVersion:$s}')"
            run_case image-mismatch c1008_previous_generation "$HOST_PROJECT" "$owned"
            jq '.fault=""' "$C1008_FIXTURE_ROOT/docker.json" > "$C1008_FIXTURE_ROOT/docker.json.new"
            mv -f -- "$C1008_FIXTURE_ROOT/docker.json.new" "$C1008_FIXTURE_ROOT/docker.json"
            run_case matched show_generation "$HOST_PROJECT" "$owned"
            git init -q "$C1008_FIXTURE_ROOT/empty-git"
            run_case source-missing source_missing
            run_case bind-kinds check_bind_kinds
            jq '.models["antiphon-runner@bbbbbbbbbbbb"]={"marker":true}' "$C1008_FIXTURE_ROOT/docker.json" > "$C1008_FIXTURE_ROOT/docker.json.new"
            mv -f -- "$C1008_FIXTURE_ROOT/docker.json.new" "$C1008_FIXTURE_ROOT/docker.json"
            run_case keyed-model check_keyed
            write_result true '' 0
            """.Replace("__SHA__", sha, StringComparison.Ordinal).Replace("__OTHER__", other, StringComparison.Ordinal);
        var run = await f.Run(extra: command);
        run.Exit.ShouldBe(0, "c1105-generation-identity: " + run.Output);
        var rows = run.Output.Split('\n').Where(x => x.StartsWith("IDENTITY ", StringComparison.Ordinal))
            .Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(x => x[1], x => x, StringComparer.Ordinal);
        void Refused(string key, string diagnosis) {
            rows[key][2].ShouldBe("2", key);
            rows[key][3].ShouldBe(diagnosis, key);
            rows[key][4].ShouldBe("0", key + " journal");
            rows[key][5].ShouldBe("0", key + " destructive docker");
        }
        Refused("missing-stack", "RecycleGenerationUnknown");
        Refused("stack-not-hex", "RecycleGenerationUnknown");
        Refused("status-mismatch", "RecycleGenerationMismatch");
        Refused("temp-env-mismatch", "RecycleGenerationMismatch");
        Refused("image-mismatch", "RecycleGenerationMismatch");
        Refused("source-missing", "RecycleGenerationUnknown");
        rows["none-owned"][2].ShouldBe("0", "none-owned");
        rows["none-owned"][6].ShouldBe("empty", "none-owned previous sha");
        rows["matched"][2].ShouldBe("0", "matched");
        rows["matched"][6].ShouldBe(sha, "matched previous sha");
        rows["bind-kinds"][2].ShouldBe("0", "bind-kinds");
        rows["keyed-model"][2].ShouldBe("0", "keyed-model");
        var matched = run.Output.Split('\n').Single(x => x.StartsWith("IDENTITY_GEN matched ", StringComparison.Ordinal))["IDENTITY_GEN matched ".Length..];
        var generation = JsonNode.Parse(matched)!.AsObject();
        generation["statusBuildVersion"]!.GetValue<string>().ShouldBe(sha);
        generation["imageTag"]!.GetValue<string>().ShouldBe("antiphon-server2/session-testing:" + sha[..12]);
        generation["imageId"]!.GetValue<string>().ShouldBe("sha256:" + new string('a', 64));
        f.Trace.Any(a => a[0] is "stop" or "rm" || (a.Length > 1 && a[0] == "volume" && a[1] == "rm")).ShouldBeFalse("identity proof is read-only");
    }
}
