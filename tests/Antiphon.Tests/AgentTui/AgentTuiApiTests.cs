using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.AgentTui;

[NotInParallel("AgentTuiApi")]
[ClassDataSource<AgentTuiApiWebAppFactory>(Shared = SharedType.PerTestSession)]
public sealed class AgentTuiApiTests
{
    private readonly AgentTuiApiWebAppFactory _factory;

    public AgentTuiApiTests(AgentTuiApiWebAppFactory factory) => _factory = factory;

    [Before(Test)]
    public Task ResetAsync() => _factory.ResetAsync();

    [Test]
    public async Task Runner_catalogue_and_profile_crud_expose_the_public_contract()
    {
        using var client = _factory.CreateClient();
        var runnerTypes = await client.GetAsync("/api/agent-tui/runner-types");
        runnerTypes.StatusCode.ShouldBe(HttpStatusCode.OK);
        var runnerJson = await ReadJsonAsync(runnerTypes);
        runnerJson.EnumerateArray().Select(item => item.GetProperty("kind").GetString())
            .ShouldContain("OpenCode");

        var displayName = $"Task 5 CRUD {Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(displayName));
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadJsonAsync(create);
        var profileId = created.GetProperty("id").GetGuid();
        create.Headers.Location.ShouldBe(new Uri($"/api/agent-tui/profiles/{profileId}", UriKind.Relative));
        created.GetProperty("revision").GetInt32().ShouldBe(1);
        created.GetProperty("commandPreview").GetProperty("executable").GetString()
            .ShouldBe("task5-runner.exe");
        created.GetProperty("commandPreview").GetProperty("arguments").EnumerateArray()
            .Select(value => value.GetString()).ShouldBe(["--task5-argument"]);
        created.GetProperty("commandPreview").TryGetProperty("environment", out _).ShouldBeFalse();

        var list = await client.GetAsync("/api/agent-tui/profiles");
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).ShouldContain(profileId.ToString());

        var get = await client.GetAsync($"/api/agent-tui/profiles/{profileId}");
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fetched = await ReadJsonAsync(get);
        fetched.GetProperty("revisionId").GetGuid().ShouldBe(created.GetProperty("revisionId").GetGuid());

        var patch = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/agent-tui/profiles/{profileId}",
            ProfileRequest(displayName + " updated", expectedRevision: 1));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ReadJsonAsync(patch);
        updated.GetProperty("revision").GetInt32().ShouldBe(2);

        var duplicate = await client.PostAsJsonAsync(
            $"/api/agent-tui/profiles/{profileId}/duplicate",
            new { displayName = displayName + " copy" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Created);
        var duplicateJson = await ReadJsonAsync(duplicate);
        var duplicateId = duplicateJson.GetProperty("id").GetGuid();
        duplicateJson.GetProperty("isEnabled").GetBoolean().ShouldBeFalse();
        duplicateJson.GetProperty("secretEnvironment").GetArrayLength().ShouldBe(0);

        var deleteDuplicate = await client.DeleteAsync($"/api/agent-tui/profiles/{duplicateId}");
        deleteDuplicate.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var deleteOriginal = await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}");
        deleteOriginal.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Secret_routes_are_strict_write_only_and_keep_canary_out_of_reads_errors_logs_metrics_and_audit()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var canary = $"TASK5_SECRET_CANARY_{suffix}";
        var profileName = $"Task 5 confidential {suffix}";
        const string environmentName = "TASK5_API_TOKEN";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                profileName,
                authenticationMode: "ManagedEnvironment",
                secretEnvironmentNames: [environmentName]));
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadJsonAsync(create);
        var profileId = created.GetProperty("id").GetGuid();

        var strictBody = await client.PutAsJsonAsync(
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}",
            new { value = canary, expectedRevision = 1, correlationId = "public-correlation-is-forbidden" });
        strictBody.StatusCode.ShouldBe((HttpStatusCode)422);
        (await strictBody.Content.ReadAsStringAsync()).ShouldNotContain(canary);

        var querySecret = await client.PutAsJsonAsync(
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}?value={Uri.EscapeDataString(canary)}",
            new { expectedRevision = 1 });
        querySecret.IsSuccessStatusCode.ShouldBeFalse();
        (await querySecret.Content.ReadAsStringAsync()).ShouldNotContain(canary);

        var put = await client.PutAsJsonAsync(
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}",
            new { value = canary, expectedRevision = 1 });
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        var putBody = await put.Content.ReadAsStringAsync();
        putBody.ShouldNotContain(canary);
        putBody.ShouldContain("\"configured\":true");

        var validation = await client.PostAsync($"/api/agent-tui/profiles/{profileId}/validate", null);
        validation.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validationJson = await ReadJsonAsync(validation);
        var runId = validationJson.GetProperty("id").GetGuid();

        var readResponses = new[]
        {
            await client.GetAsync("/api/agent-tui/runner-types"),
            await client.GetAsync("/api/agent-tui/profiles"),
            await client.GetAsync($"/api/agent-tui/profiles/{profileId}"),
            await client.GetAsync($"/api/agent-tui/profiles/{profileId}/models"),
            await client.GetAsync($"/api/agent-tui/profiles/{profileId}/capabilities"),
            await client.GetAsync($"/api/agent-tui/validation-runs/{runId}"),
            await client.GetAsync("/metrics/agent-tui")
        };
        foreach (var response in readResponses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).ShouldNotContain(canary);
        }

        var stale = await client.PutAsJsonAsync(
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}",
            new { value = canary, expectedRevision = 99 });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ReadJsonAsync(stale);
        problem.GetProperty("code").GetString().ShouldBe("profile_revision_conflict");
        problem.ToString().ShouldNotContain(canary);

        string ciphertext;
        string auditContent;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ciphertext = await db.AgentTuiSecrets
                .Where(secret => secret.ProfileId == profileId && secret.Name == environmentName)
                .Select(secret => secret.Ciphertext)
                .SingleAsync();
            var audits = await db.AuditRecords
                .Where(record => record.Summary.Contains(profileId.ToString()))
                .Select(record => new { record.Summary, record.FullContent })
                .ToListAsync();
            audits.ShouldNotBeEmpty();
            auditContent = JsonSerializer.Serialize(audits);
        }
        ciphertext.ShouldNotContain(canary);
        auditContent.ShouldContain(environmentName);
        auditContent.ShouldContain("operation=set");
        auditContent.ShouldNotContain(canary);
        auditContent.ShouldNotContain(ciphertext);

        var metrics = await client.GetStringAsync("/metrics/agent-tui");
        metrics.ShouldContain(
            "antiphon_agent_tui_secret_operations_total{operation=\"write\",outcome=\"succeeded\"}");
        metrics.ShouldContain(
            "antiphon_agent_tui_secret_operations_total{operation=\"write\",outcome=\"conflict\"}");
        metrics.ShouldNotContain(canary);
        metrics.ShouldNotContain(ciphertext);
        var logs = _factory.ReadLogs();
        logs.ShouldNotContain(canary);
        logs.ShouldNotContain(ciphertext);

        var clear = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}",
            new { expectedRevision = 1 });
        clear.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await clear.Content.ReadAsStringAsync()).ShouldContain("\"configured\":false");
        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Stale_patch_returns_stable_conflict_code_and_records_revision_conflict_metric()
    {
        using var client = _factory.CreateClient();
        var name = $"Task 5 conflict {Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync("/api/agent-tui/profiles", ProfileRequest(name));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var stale = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/agent-tui/profiles/{profileId}",
            ProfileRequest(name + " stale", expectedRevision: 9));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await ReadJsonAsync(stale);
        problem.GetProperty("code").GetString().ShouldBe("profile_revision_conflict");

        var metrics = await client.GetStringAsync("/metrics/agent-tui");
        metrics.ShouldContain("antiphon_agent_tui_revision_conflicts_total{operation=\"profile_update\"}");
        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Concurrent_model_refresh_joins_one_bounded_run_and_validation_run_is_readable()
    {
        using var client = _factory.CreateClient();
        var name = $"Task 5 operations {Guid.NewGuid():N}";
        var model = $"provider/task5-{Guid.NewGuid():N}";
        _factory.Probe.DiscoveredModel = model;
        _factory.Probe.DelayDiscovery = true;
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(name, kind: "OpenCode", discoveryArguments: ["models"], versionArguments: ["--version"]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var firstTask = client.PostAsync($"/api/agent-tui/profiles/{profileId}/models/refresh", null);
        var secondTask = client.PostAsync($"/api/agent-tui/profiles/{profileId}/models/refresh", null);
        var refreshes = await Task.WhenAll(firstTask, secondTask);
        refreshes.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        var first = await ReadJsonAsync(refreshes[0]);
        var second = await ReadJsonAsync(refreshes[1]);
        first.GetProperty("run").GetProperty("id").GetGuid()
            .ShouldBe(second.GetProperty("run").GetProperty("id").GetGuid());
        first.GetProperty("models").EnumerateArray()
            .Select(item => item.GetProperty("identifier").GetString()).ShouldContain(model);
        _factory.Probe.DiscoveryCalls.ShouldBe(1);

        _factory.Probe.DelayDiscovery = false;
        var validate = await client.PostAsync($"/api/agent-tui/profiles/{profileId}/validate", null);
        validate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = await ReadJsonAsync(validate);
        var runId = validation.GetProperty("id").GetGuid();
        validation.GetProperty("operation").GetString().ShouldBe("validation");
        var getRun = await client.GetAsync($"/api/agent-tui/validation-runs/{runId}");
        getRun.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadJsonAsync(getRun)).GetProperty("id").GetGuid().ShouldBe(runId);

        var models = await client.GetAsync($"/api/agent-tui/profiles/{profileId}/models");
        models.StatusCode.ShouldBe(HttpStatusCode.OK);
        var capabilities = await client.GetAsync($"/api/agent-tui/profiles/{profileId}/capabilities");
        capabilities.StatusCode.ShouldBe(HttpStatusCode.OK);
        var metrics = await client.GetStringAsync("/metrics/agent-tui");
        metrics.ShouldContain("antiphon_agent_tui_discovery_runs_total{runner_type=\"open_code\",outcome=\"succeeded\",cache_result=\"refreshed\"}");
        metrics.ShouldContain("antiphon_agent_tui_discovery_duration_seconds{runner_type=\"open_code\",outcome=\"succeeded\"}");
        metrics.ShouldContain("antiphon_agent_tui_model_cache_age_seconds{runner_type=\"open_code\",cache_result=\"verified\"}");
        metrics.ShouldContain("antiphon_agent_tui_validation_stages_total{runner_type=\"open_code\",stage=\"executable\",outcome=\"succeeded\"}");
        metrics.ShouldContain("antiphon_agent_tui_validation_duration_seconds{runner_type=\"open_code\",outcome=\"partial\"}");
        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Metrics_render_all_families_with_prometheus_format_and_only_bounded_labels()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var profileName = $"Task5 metric profile {suffix}";
        var model = $"provider/private-model-{suffix}";
        var path = $@"C:\private\task5-{suffix}\runner.exe";
        var argument = $"--private-argument-{suffix}";
        var environmentName = $"TASK5_PRIVATE_{suffix.ToUpperInvariant()}";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                profileName,
                executable: path,
                arguments: [argument],
                nonSecretEnvironment: new Dictionary<string, string> { [environmentName] = "private-setting" },
                models: [new { identifier = model, displayName = "Private model" }]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var metrics = await client.GetStringAsync("/metrics/agent-tui");
        string[] families =
        [
            "antiphon_agent_tui_profiles",
            "antiphon_agent_tui_secret_protection_ready",
            "antiphon_agent_tui_secret_operations_total",
            "antiphon_agent_tui_discovery_runs_total",
            "antiphon_agent_tui_discovery_duration_seconds",
            "antiphon_agent_tui_model_cache_age_seconds",
            "antiphon_agent_tui_validation_stages_total",
            "antiphon_agent_tui_validation_duration_seconds",
            "antiphon_agent_tui_launches_total",
            "antiphon_agent_tui_launch_resolution_duration_seconds",
            "antiphon_agent_tui_imports_total",
            "antiphon_agent_tui_revision_conflicts_total"
        ];
        foreach (var family in families)
        {
            metrics.ShouldContain($"# HELP {family} ");
            metrics.ShouldContain($"# TYPE {family} ");
        }
        Regex.IsMatch(
            metrics,
            "(?m)^antiphon_agent_tui_profiles\\{runner_type=\\\"[a-z_]+\\\",enabled=\\\"(?:true|false)\\\",validation_state=\\\"[a-z_]+\\\",auth_mode=\\\"[a-z_]+\\\"\\} [0-9]+(?:\\.[0-9]+)?$")
            .ShouldBeTrue();
        metrics.ShouldContain("antiphon_agent_tui_secret_protection_ready{protector_type=\"data_protection\"}");
        metrics.ShouldContain("antiphon_agent_tui_launches_total");
        metrics.ShouldContain("antiphon_agent_tui_imports_total{outcome=\"succeeded\",change_kind=");

        foreach (var forbidden in new[]
                 {
                     profileName, profileId.ToString(), model, path, argument, environmentName,
                     "profile_id=", "model_id=", "environment_name=", "correlation_id=",
                     "executable=", "arguments=", "exception="
                 })
        {
            metrics.ShouldNotContain(forbidden);
        }

        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
    }

    private static object ProfileRequest(
        string displayName,
        int? expectedRevision = null,
        string kind = "Raw",
        string authenticationMode = "WrapperManaged",
        string executable = "task5-runner.exe",
        string[]? arguments = null,
        string[]? discoveryArguments = null,
        string[]? versionArguments = null,
        IReadOnlyDictionary<string, string>? nonSecretEnvironment = null,
        string[]? secretEnvironmentNames = null,
        object[]? models = null) => new
        {
            displayName,
            kind,
            isEnabled = true,
            isDefault = false,
            executable,
            arguments = arguments ?? ["--task5-argument"],
            discoveryArguments = discoveryArguments ?? Array.Empty<string>(),
            versionArguments = versionArguments ?? Array.Empty<string>(),
            workingDirectory = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            authenticationMode,
            nonSecretEnvironment = nonSecretEnvironment ?? new Dictionary<string, string>(),
            secretEnvironmentNames = secretEnvironmentNames ?? Array.Empty<string>(),
            modelArgumentName = "--model",
            guidance = "Task 5 API test guidance",
            models = models ?? Array.Empty<object>(),
            expectedRevision
        };

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string uri,
        object value)
    {
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = JsonContent.Create(value)
        };
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}

public sealed class AgentTuiApiWebAppFactory : AntiphonWebAppFactory
{
    private readonly string _logPath = Path.Combine(
        Path.GetTempPath(),
        "antiphon-agent-tui-api-logs",
        Guid.NewGuid().ToString("N"));

    public RecordingApiRunnerProcessProbe Probe { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Serilog:LogPath"] = _logPath,
                ["Serilog:ConsoleMinimumLevel"] = "Warning"
            }));
    }

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.RemoveAll<IRunnerProcessProbe>();
        services.AddSingleton<IRunnerProcessProbe>(Probe);
    }

    public override Task ResetAsync()
    {
        Probe.Reset();
        return base.ResetAsync();
    }

    public string ReadLogs()
    {
        if (!Directory.Exists(_logPath))
            return string.Empty;
        return string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(_logPath, "*.log")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(ReadSharedFile));
    }

    private static string ReadSharedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public sealed class RecordingApiRunnerProcessProbe : IRunnerProcessProbe
{
    private int _discoveryCalls;

    public bool DelayDiscovery { get; set; }
    public string DiscoveredModel { get; set; } = "provider/task5-default";
    public int DiscoveryCalls => Volatile.Read(ref _discoveryCalls);

    public void Reset()
    {
        DelayDiscovery = false;
        DiscoveredModel = "provider/task5-default";
        Volatile.Write(ref _discoveryCalls, 0);
    }

    public Task<RunnerPathCheck> CheckExecutableAsync(
        string executable,
        CancellationToken cancellationToken) =>
        Task.FromResult(new RunnerPathCheck(true, "Executable is available."));

    public Task<RunnerPathCheck> CheckFileAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(new RunnerPathCheck(true, "Wrapper is available."));

    public Task<RunnerPathCheck> CheckDirectoryAsync(string path, CancellationToken cancellationToken) =>
        Task.FromResult(new RunnerPathCheck(true, "Working directory is available."));

    public async Task<RunnerProcessResult> RunAsync(
        RunnerProcessRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Arguments.Contains("models", StringComparer.Ordinal))
        {
            Interlocked.Increment(ref _discoveryCalls);
            if (DelayDiscovery)
                await Task.Delay(150, cancellationToken);
            return Success(DiscoveredModel);
        }
        if (request.Arguments.Contains("--version", StringComparer.Ordinal))
            return Success("opencode 1.2.3");
        return Success(string.Empty);
    }

    private static RunnerProcessResult Success(string output) =>
        new(0, output, string.Empty, TimedOut: false, Started: true, CleanlyStopped: true);
}
