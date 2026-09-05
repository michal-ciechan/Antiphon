using System.Net;
using System.Net.Http.Json;
using System.Globalization;
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

// WebApplicationFactory<Program> hosts share process-wide ASP.NET Core startup state. Keep this
// shared HTTP contract fixture exclusive to prevent another integration test host from disposing
// or reconfiguring it before its first client is created.
[NotInParallel]
[ClassDataSource<AgentTuiApiWebAppFactory>(Shared = SharedType.PerTestSession)]
[Category("Integration")]
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
        runnerJson.EnumerateArray().Select(item => item.GetProperty("kind").GetString())
            .ShouldContain("Grok");

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
        var neverRun = created.GetProperty("validationSummary");
        neverRun.GetProperty("status").GetString().ShouldBe("NeverRun");
        neverRun.GetProperty("profileRevisionId").ValueKind.ShouldBe(JsonValueKind.Null);
        neverRun.GetProperty("isCurrentRevision").GetBoolean().ShouldBeFalse();
        neverRun.GetProperty("runnerVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        neverRun.GetProperty("probedAt").ValueKind.ShouldBe(JsonValueKind.Null);

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
    public async Task Profile_validation_summary_is_cached_and_marks_a_prior_revision_stale_without_probing()
    {
        using var client = _factory.CreateClient();
        var name = $"Task 5 validation summary {Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                name,
                kind: "OpenCode",
                discoveryArguments: ["models"],
                versionArguments: ["--version"]));
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadJsonAsync(create);
        var profileId = created.GetProperty("id").GetGuid();
        var originalRevisionId = created.GetProperty("revisionId").GetGuid();

        var callsBeforeFallbackReads = _factory.Probe.RunCalls;
        (await client.GetAsync($"/api/agent-tui/profiles/{profileId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/agent-tui/profiles"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Probe.RunCalls.ShouldBe(callsBeforeFallbackReads);

        var validate = await client.PostAsync($"/api/agent-tui/profiles/{profileId}/validate", null);
        validate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = await ReadJsonAsync(validate);
        var validationStatus = validation.GetProperty("status").GetString();
        var probedAt = validation.GetProperty("completedAt").GetDateTime();

        var current = await ReadJsonAsync(
            await client.GetAsync($"/api/agent-tui/profiles/{profileId}"));
        var currentSummary = current.GetProperty("validationSummary");
        currentSummary.GetProperty("status").GetString().ShouldBe(validationStatus);
        currentSummary.GetProperty("profileRevisionId").GetGuid().ShouldBe(originalRevisionId);
        currentSummary.GetProperty("isCurrentRevision").GetBoolean().ShouldBeTrue();
        currentSummary.GetProperty("runnerVersion").GetString().ShouldBe("OpenCode 1.2.3");
        currentSummary.GetProperty("probedAt").GetDateTime().ShouldBe(probedAt);

        var callsBeforePatch = _factory.Probe.RunCalls;
        var patch = await SendJsonAsync(
            client,
            HttpMethod.Patch,
            $"/api/agent-tui/profiles/{profileId}",
            ProfileRequest(
                name + " updated",
                expectedRevision: 1,
                kind: "OpenCode",
                discoveryArguments: ["models"],
                versionArguments: ["--version"]));
        patch.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await ReadJsonAsync(patch);
        var staleSummary = updated.GetProperty("validationSummary");
        staleSummary.GetProperty("status").GetString().ShouldBe(validationStatus);
        staleSummary.GetProperty("profileRevisionId").GetGuid().ShouldBe(originalRevisionId);
        staleSummary.GetProperty("isCurrentRevision").GetBoolean().ShouldBeFalse();
        staleSummary.GetProperty("runnerVersion").GetString().ShouldBe("OpenCode 1.2.3");
        staleSummary.GetProperty("probedAt").GetDateTime().ShouldBe(probedAt);
        _factory.Probe.RunCalls.ShouldBe(callsBeforePatch);

        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Capability_reads_return_typed_fallback_and_cached_snapshots_without_probing()
    {
        using var client = _factory.CreateClient();
        var name = $"Task 5 capability snapshot {Guid.NewGuid():N}";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                name,
                kind: "OpenCode",
                discoveryArguments: ["models"],
                versionArguments: ["--version"]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var fallback = await client.GetAsync($"/api/agent-tui/profiles/{profileId}/capabilities");
        fallback.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fallbackJson = await ReadJsonAsync(fallback);
        fallbackJson.GetProperty("capabilities").GetArrayLength().ShouldBeGreaterThan(0);
        fallbackJson.GetProperty("runnerVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        fallbackJson.GetProperty("probedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        _factory.Probe.RunCalls.ShouldBe(0);

        var validate = await client.PostAsync($"/api/agent-tui/profiles/{profileId}/validate", null);
        validate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var validation = await ReadJsonAsync(validate);
        var callsAfterValidation = _factory.Probe.RunCalls;

        var cached = await client.GetAsync($"/api/agent-tui/profiles/{profileId}/capabilities");
        cached.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cachedJson = await ReadJsonAsync(cached);
        cachedJson.GetProperty("capabilities").GetArrayLength().ShouldBeGreaterThan(0);
        cachedJson.GetProperty("runnerVersion").GetString().ShouldBe("OpenCode 1.2.3");
        cachedJson.GetProperty("probedAt").GetDateTime()
            .ShouldBe(validation.GetProperty("completedAt").GetDateTime());
        _factory.Probe.RunCalls.ShouldBe(callsAfterValidation);

        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Concurrent_idempotent_secret_put_protects_audits_and_counts_once_without_leaking_the_secret()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var canary = $"TASK5_IDEMPOTENCY_CANARY_{suffix}";
        var profileName = $"Task 5 idempotent secret {suffix}";
        const string environmentName = "TASK5_IDEMPOTENT_TOKEN";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                profileName,
                authenticationMode: "ManagedEnvironment",
                secretEnvironmentNames: [environmentName]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();
        var metricBefore = await ReadMetricAsync(
            client,
            "antiphon_agent_tui_secret_operations_total{operation=\"write\",outcome=\"succeeded\"}");

        _factory.SecretProtector.BlockProtection();
        var idempotencyKey = $"task5-{Guid.NewGuid():N}";
        var firstTask = PutSecretWithIdempotencyKeyAsync(
            client,
            profileId,
            environmentName,
            canary,
            idempotencyKey);
        await _factory.SecretProtector.WaitForProtectionAsync();
        var secondTask = PutSecretWithIdempotencyKeyAsync(
            client,
            profileId,
            environmentName,
            canary,
            idempotencyKey);
        _factory.SecretProtector.ReleaseProtection();
        var responses = await Task.WhenAll(firstTask, secondTask);

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.OK);
        var firstBody = await responses[0].Content.ReadAsStringAsync();
        var secondBody = await responses[1].Content.ReadAsStringAsync();
        firstBody.ShouldBe(secondBody);
        firstBody.ShouldNotContain(canary);
        _factory.SecretProtector.ProtectCalls.ShouldBe(1);

        string ciphertext;
        int auditCount;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ciphertext = await db.AgentTuiSecrets
                .Where(secret => secret.ProfileId == profileId && secret.Name == environmentName)
                .Select(secret => secret.Ciphertext)
                .SingleAsync();
            auditCount = await db.AuditRecords.CountAsync(record =>
                record.Summary.Contains(profileId.ToString())
                && record.Summary.Contains($"environmentName={environmentName}")
                && record.Summary.Contains("operation=set"));
        }
        auditCount.ShouldBe(1);
        ciphertext.ShouldNotContain(canary);

        var metrics = await client.GetStringAsync("/metrics/agent-tui");
        var metricAfter = ReadMetric(
            metrics,
            "antiphon_agent_tui_secret_operations_total{operation=\"write\",outcome=\"succeeded\"}");
        (metricAfter - metricBefore).ShouldBe(1);
        metrics.ShouldNotContain(canary);
        _factory.ReadLogs().ShouldNotContain(canary);

        var clear = await SendJsonAsync(
            client,
            HttpMethod.Delete,
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}",
            new { expectedRevision = 1 });
        clear.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Invalid_idempotency_keys_return_a_sanitized_stable_code_without_protecting_the_secret()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var canary = $"TASK5_INVALID_KEY_CANARY_{suffix}";
        const string environmentName = "TASK5_INVALID_KEY_TOKEN";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                $"Task 5 invalid key {suffix}",
                authenticationMode: "ManagedEnvironment",
                secretEnvironmentNames: [environmentName]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var invalid = await PutSecretWithIdempotencyKeyAsync(
            client,
            profileId,
            environmentName,
            canary,
            "contains space");
        var overlong = await PutSecretWithIdempotencyKeyAsync(
            client,
            profileId,
            environmentName,
            canary,
            new string('x', 201));

        foreach (var response in new[] { invalid, overlong })
        {
            response.StatusCode.ShouldBe((HttpStatusCode)422);
            var body = await ReadJsonAsync(response);
            body.GetProperty("code").GetString().ShouldBe("invalid_idempotency_key");
            body.ToString().ShouldNotContain(canary);
            body.ToString().ShouldNotContain("stackTrace");
        }
        _factory.SecretProtector.ProtectCalls.ShouldBe(0);
        _factory.ReadLogs().ShouldNotContain(canary);

        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Task_5_http_errors_always_include_stable_codes_without_stack_traces_or_secret_values()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var canary = $"TASK5_PROBLEM_CANARY_{suffix}";
        var profileName = $"Task 5 problem details {suffix}";
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(
                profileName,
                authenticationMode: "ManagedEnvironment",
                secretEnvironmentNames: ["TASK5_DECLARED_TOKEN"]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var notFound = await client.GetAsync($"/api/agent-tui/profiles/{Guid.NewGuid()}");
        var validation = await client.PutAsJsonAsync(
            $"/api/agent-tui/profiles/{profileId}/secrets/TASK5_UNDECLARED_TOKEN",
            new { value = canary, expectedRevision = 1 });
        var conflict = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(profileName));
        var profiles = await ReadJsonAsync(await client.GetAsync("/api/agent-tui/profiles"));
        var defaultProfileId = profiles.EnumerateArray()
            .Single(profile => profile.GetProperty("isDefault").GetBoolean())
            .GetProperty("id")
            .GetGuid();
        var inUse = await client.DeleteAsync($"/api/agent-tui/profiles/{defaultProfileId}");

        var codedResponses = new[]
        {
            (Response: notFound, Code: "not_found"),
            (Response: validation, Code: "validation_failed"),
            (Response: conflict, Code: "conflict"),
            (Response: inUse, Code: "profile_in_use")
        };
        foreach (var (response, code) in codedResponses)
        {
            var body = await ReadJsonAsync(response);
            body.GetProperty("code").GetString().ShouldBe(code);
            var text = body.ToString();
            text.ShouldNotContain("stackTrace");
            text.ShouldNotContain(canary);
        }

        (await client.DeleteAsync($"/api/agent-tui/profiles/{profileId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Model_refresh_exposes_its_outcome_and_validation_run_is_readable()
    {
        using var client = _factory.CreateClient();
        var name = $"Task 5 operations {Guid.NewGuid():N}";
        var model = $"provider/task5-{Guid.NewGuid():N}";
        _factory.Probe.DiscoveredModel = model;
        _factory.Probe.BlockDiscovery();
        var create = await client.PostAsJsonAsync(
            "/api/agent-tui/profiles",
            ProfileRequest(name, kind: "OpenCode", discoveryArguments: ["models"], versionArguments: ["--version"]));
        var profileId = (await ReadJsonAsync(create)).GetProperty("id").GetGuid();

        var refreshTask = client.PostAsync($"/api/agent-tui/profiles/{profileId}/models/refresh", null);
        await _factory.Probe.WaitForDiscoveryAsync();
        try
        {
            _factory.Probe.DiscoveryCalls.ShouldBe(1);
        }
        finally
        {
            _factory.Probe.ReleaseDiscovery();
        }
        var refresh = await refreshTask;
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var outcome = await ReadJsonAsync(refresh);
        outcome.TryGetProperty("run", out _).ShouldBeFalse();
        outcome.GetProperty("operation").GetString().ShouldBe("discovery");
        outcome.GetProperty("status").GetString().ShouldBe("Succeeded");
        outcome.GetProperty("cachedResultsRetained").GetBoolean().ShouldBeFalse();
        outcome.GetProperty("models").EnumerateArray()
            .Select(item => item.GetProperty("identifier").GetString()).ShouldContain(model);
        _factory.Probe.DiscoveryCalls.ShouldBe(1);

        _factory.Probe.FailDiscovery = true;
        _factory.Probe.DelayDiscovery = false;
        var failedRefresh = await client.PostAsync(
            $"/api/agent-tui/profiles/{profileId}/models/refresh",
            null);
        failedRefresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var failed = await ReadJsonAsync(failedRefresh);
        failed.GetProperty("operation").GetString().ShouldBe("discovery");
        failed.GetProperty("status").GetString().ShouldBe("Failed");
        failed.GetProperty("cachedResultsRetained").GetBoolean().ShouldBeTrue();
        failed.GetProperty("models").EnumerateArray()
            .ShouldContain(item => item.GetProperty("identifier").GetString() == model
                                   && item.GetProperty("availability").GetString() == "Stale");
        _factory.Probe.DiscoveryCalls.ShouldBe(2);

        _factory.Probe.FailDiscovery = false;
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
        var callsBeforeCachedReads = _factory.Probe.RunCalls;
        var capabilities = await client.GetAsync($"/api/agent-tui/profiles/{profileId}/capabilities");
        capabilities.StatusCode.ShouldBe(HttpStatusCode.OK);
        var capabilitySnapshot = await ReadJsonAsync(capabilities);
        capabilitySnapshot.GetProperty("capabilities").GetArrayLength().ShouldBeGreaterThan(0);
        capabilitySnapshot.GetProperty("runnerVersion").GetString().ShouldBe("OpenCode 1.2.3");
        capabilitySnapshot.GetProperty("probedAt").GetDateTime().ShouldBeGreaterThan(DateTime.MinValue);
        _factory.Probe.RunCalls.ShouldBe(callsBeforeCachedReads);
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

    private static async Task<HttpResponseMessage> PutSecretWithIdempotencyKeyAsync(
        HttpClient client,
        Guid profileId,
        string environmentName,
        string value,
        string idempotencyKey)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/agent-tui/profiles/{profileId}/secrets/{environmentName}")
        {
            Content = JsonContent.Create(new { value, expectedRevision = 1 })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey).ShouldBeTrue();
        return await client.SendAsync(request);
    }

    private static async Task<long> ReadMetricAsync(HttpClient client, string sampleName) =>
        ReadMetric(await client.GetStringAsync("/metrics/agent-tui"), sampleName);

    private static long ReadMetric(string metrics, string sampleName)
    {
        var sample = metrics.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SingleOrDefault(line => line.StartsWith(sampleName + " ", StringComparison.Ordinal));
        if (sample is null)
            return 0;
        return long.Parse(sample[(sample.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }
}

public sealed class AgentTuiApiWebAppFactory : AntiphonWebAppFactory
{
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private readonly string _logPath = Path.Combine(
        Path.GetTempPath(),
        "antiphon-agent-tui-api-logs",
        Guid.NewGuid().ToString("N"));
    private IsolatedTestSchema? _isolatedSchema;

    public RecordingApiRunnerProcessProbe Probe { get; } = new();
    public RecordingApiSecretProtector SecretProtector { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = IsolatedConnectionString,
                ["Serilog:LogPath"] = _logPath,
                ["Serilog:ConsoleMinimumLevel"] = "Warning",
                // Safe here, unlike on the shared schema the base factory runs against: this
                // host owns its own schema, so the import's agent backfill reaches nobody else.
                ["AgentTui:ImportProfilesOnStartup"] = "true"
            }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
                IsolatedConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsAssembly("Antiphon.Server");
                    npgsql.SetPostgresVersion(16, 0);
                }));
        });
    }

    protected override void ApplyTestOverrides(IServiceCollection services)
    {
        services.RemoveAll<IRunnerProcessProbe>();
        services.AddSingleton<IRunnerProcessProbe>(Probe);
        services.RemoveAll<IAgentTuiSecretProtector>();
        services.AddSingleton<IAgentTuiSecretProtector>(SecretProtector);
    }

    public override async Task ResetAsync()
    {
        await EnsureIsolatedSchemaAsync();
        Probe.Reset();
        SecretProtector.Reset();
        await base.ResetAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (_isolatedSchema is not null)
        {
            await _isolatedSchema.DisposeAsync();
            _isolatedSchema = null;
        }
        _schemaGate.Dispose();
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

    private string IsolatedConnectionString => _isolatedSchema?.ConnectionString
        ?? throw new InvalidOperationException("The API test schema must be created before the host starts.");

    /// <summary>This factory owns its schema already; do not let the base create a second one.</summary>
    protected override string ConnectionString => IsolatedConnectionString;

    private async Task EnsureIsolatedSchemaAsync()
    {
        if (_isolatedSchema is not null)
            return;

        await _schemaGate.WaitAsync();
        try
        {
            _isolatedSchema ??= await TestDbFixture.CreateIsolatedSchemaAsync();
        }
        finally
        {
            _schemaGate.Release();
        }
    }
}

public sealed class RecordingApiRunnerProcessProbe : IRunnerProcessProbe
{
    private int _discoveryCalls;
    private int _runCalls;
    private TaskCompletionSource? _discoveryEntered;
    private TaskCompletionSource? _releaseDiscovery;

    public bool DelayDiscovery { get; set; }
    public bool FailDiscovery { get; set; }
    public string DiscoveredModel { get; set; } = "provider/task5-default";
    public int DiscoveryCalls => Volatile.Read(ref _discoveryCalls);
    public int RunCalls => Volatile.Read(ref _runCalls);

    public void Reset()
    {
        ReleaseDiscovery();
        _discoveryEntered = null;
        _releaseDiscovery = null;
        DelayDiscovery = false;
        FailDiscovery = false;
        DiscoveredModel = "provider/task5-default";
        Volatile.Write(ref _discoveryCalls, 0);
        Volatile.Write(ref _runCalls, 0);
    }

    public void BlockDiscovery()
    {
        _discoveryEntered = NewSignal();
        _releaseDiscovery = NewSignal();
    }

    public async Task WaitForDiscoveryAsync() =>
        await (_discoveryEntered?.Task
            ?? throw new InvalidOperationException("Discovery was not configured to block."))
            .WaitAsync(TimeSpan.FromSeconds(5));

    public void ReleaseDiscovery() => _releaseDiscovery?.TrySetResult();

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
        Interlocked.Increment(ref _runCalls);
        if (request.Arguments.Contains("models", StringComparer.Ordinal))
        {
            Interlocked.Increment(ref _discoveryCalls);
            if (_releaseDiscovery is not null)
            {
                _discoveryEntered!.TrySetResult();
                await _releaseDiscovery.Task.WaitAsync(cancellationToken);
            }
            else if (DelayDiscovery)
                await Task.Delay(150, cancellationToken);
            if (FailDiscovery)
                return new RunnerProcessResult(1, string.Empty, "Discovery failed.", TimedOut: false);
            return Success(DiscoveredModel);
        }
        if (request.Arguments.Contains("--version", StringComparer.Ordinal))
            return Success("opencode 1.2.3");
        return Success(string.Empty);
    }

    private static RunnerProcessResult Success(string output) =>
        new(0, output, string.Empty, TimedOut: false, Started: true, CleanlyStopped: true);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class RecordingApiSecretProtector : IAgentTuiSecretProtector
{
    private int _protectCalls;
    private ManualResetEventSlim _releaseProtection = new(initialState: true);
    private TaskCompletionSource _protectionStarted = NewSignal();

    public int ProtectCalls => Volatile.Read(ref _protectCalls);

    public void Reset()
    {
        _releaseProtection.Set();
        _releaseProtection = new ManualResetEventSlim(initialState: true);
        _protectionStarted = NewSignal();
        Volatile.Write(ref _protectCalls, 0);
    }

    public void BlockProtection() => _releaseProtection.Reset();

    public async Task WaitForProtectionAsync() =>
        await _protectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

    public void ReleaseProtection() => _releaseProtection.Set();

    public string Protect(Guid profileId, string environmentName, string plaintext)
    {
        Interlocked.Increment(ref _protectCalls);
        _protectionStarted.TrySetResult();
        if (!_releaseProtection.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("The test secret protector was not released.");
        return $"test-protected-{profileId:N}-{environmentName}";
    }

    public string Unprotect(Guid profileId, string environmentName, string protectedValue) =>
        "test-managed-secret";

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
