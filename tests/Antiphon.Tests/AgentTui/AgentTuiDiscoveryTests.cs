using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.AgentTui;

[Category("Integration")]
[NotInParallel]
[ClassDataSource<TestDbFixture>(Shared = SharedType.PerTestSession)]
public sealed class AgentTuiDiscoveryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 13, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid TestActorId =
        new("a0000000-0000-0000-0000-000000000004");
    private const string WrapperPath = @"C:\synthetic\ocg.ps1";
    private readonly TestDbFixture _fixture;

    public AgentTuiDiscoveryTests(TestDbFixture fixture)
    {
        _fixture = fixture;
    }

    [After(Test)]
    public async Task CleanupProfilesAsync()
    {
        await using var db = _fixture.CreateDbContext();
        var profileIds = await db.AgentTuiProfiles
            .Where(profile => profile.DisplayName.StartsWith("Task 4 "))
            .Select(profile => profile.Id)
            .ToArrayAsync();
        if (profileIds.Length == 0)
            return;

        await db.AgentTuiValidationRuns
            .Where(run => profileIds.Contains(run.ProfileId))
            .ExecuteDeleteAsync();
        await db.AgentTuiSecrets
            .Where(secret => profileIds.Contains(secret.ProfileId))
            .ExecuteDeleteAsync();
        await db.AgentTuiModels
            .Where(model => profileIds.Contains(model.ProfileId))
            .ExecuteDeleteAsync();
        await db.AgentTuiProfiles
            .Where(profile => profileIds.Contains(profile.Id))
            .ExecuteUpdateAsync(update => update.SetProperty(profile => profile.ActiveRevisionId, (Guid?)null));
        await db.AgentTuiProfileRevisions
            .Where(revision => profileIds.Contains(revision.ProfileId))
            .ExecuteDeleteAsync();
        await db.AgentTuiProfiles
            .Where(profile => profileIds.Contains(profile.Id))
            .ExecuteDeleteAsync();
        await db.AuditRecords
            .Where(record => record.UserId == TestActorId)
            .ExecuteDeleteAsync();
        await db.Users
            .Where(user => user.Id == TestActorId)
            .ExecuteDeleteAsync();
    }

    [Test]
    public async Task OpenCode_discovery_uses_only_ordered_discovery_arguments_and_verifies_models()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(new RunnerProcessResult(
            0,
            "llmgateway/grok-4-5\nopenai/gpt-5.6-sol\nllmgateway/grok-4-5\n",
            string.Empty,
            TimedOut: false));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var models = await RefreshAsync(provider, profile.Id);

        probe.Requests.Count.ShouldBe(1);
        var request = probe.Requests.Single();
        request.Executable.ShouldBe("pwsh");
        request.Arguments.ShouldBe([
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WrapperPath, "models"
        ]);
        request.Arguments.ShouldNotContain("--auto");
        request.Arguments.ShouldNotContain("--mini");
        request.WorkingDirectory.ShouldBe(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        request.Environment.ShouldContainKey("SYNTHETIC_SETTING");
        models.Where(model => model.Source == AgentTuiModelSource.Discovered)
            .Select(model => model.Identifier)
            .ShouldBe(["openai/gpt-5.6-sol"]);
        models.Single(model => model.Identifier == "llmgateway/grok-4-5").Source
            .ShouldBe(AgentTuiModelSource.Curated);
        models.Single(model => model.Identifier == "llmgateway/grok-4-5").Availability
            .ShouldBe(AgentTuiModelAvailability.Verified);
        models.Single(model => model.Identifier == "openai/gpt-5.6-sol").Availability
            .ShouldBe(AgentTuiModelAvailability.Verified);
        models.Single(model => model.Identifier == "openai/gpt-5.6-sol").Family.ShouldBeNull();
        models.Single(model => model.Identifier == "openai/gpt-5.6-sol").DiscoveredAt
            .ShouldBe(FixedNow.UtcDateTime);
        models.ShouldContain(model =>
            model.Identifier == "operator/custom-model" && model.Source == AgentTuiModelSource.Operator);
    }

    [Test]
    public async Task Malformed_partial_discovery_keeps_previous_models_stale_and_discards_new_lines()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("provider/old-model\n"));
        probe.Enqueue(Success("provider/new-model\nnot a model; remove-me\n"));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await RefreshAsync(provider, profile.Id);

        var models = await RefreshAsync(provider, profile.Id);

        models.ShouldContain(model =>
            model.Identifier == "provider/old-model"
            && model.Source == AgentTuiModelSource.Discovered
            && model.Availability == AgentTuiModelAvailability.Stale);
        models.ShouldNotContain(model => model.Identifier == "provider/new-model");
        models.ShouldContain(model => model.Identifier == "llmgateway/grok-4-5");
        models.ShouldContain(model => model.Identifier == "operator/custom-model");
    }

    [Test]
    public async Task Discovery_rejects_non_opaque_lines_without_trimming_or_skipping_blanks()
    {
        var malformedResults = new[]
        {
            " provider/leading\n",
            "provider/trailing \n",
            "provider/internal whitespace\n",
            "provider/first\n\nprovider/second\n"
        };

        foreach (var malformed in malformedResults)
        {
            var probe = new RecordingRunnerProcessProbe();
            probe.Enqueue(Success("provider/stable\n"));
            probe.Enqueue(Success(malformed));
            await using var provider = BuildProvider(probe);
            var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
            await RefreshAsync(provider, profile.Id);

            var models = await RefreshAsync(provider, profile.Id);

            models.ShouldContain(model =>
                model.Identifier == "provider/stable"
                && model.Availability == AgentTuiModelAvailability.Stale);
            var identifiers = models.Select(model => model.Identifier).ToArray();
            foreach (var rejected in new[]
                     {
                         "provider/leading",
                         "provider/trailing",
                         "provider/internal whitespace",
                         "provider/first",
                         "provider/second"
                     })
            {
                identifiers.ShouldNotContain(rejected);
            }
        }
    }

    [Test]
    public async Task Oversized_timed_out_and_nonzero_discovery_each_preserve_the_stale_cache()
    {
        RunnerProcessResult[] failures =
        [
            new(0, "provider/partial\n", string.Empty, false, OutputTruncated: true),
            new(null, string.Empty, string.Empty, true),
            new(17, "provider/partial\n", "credential rejected", false)
        ];

        foreach (var failure in failures)
        {
            var probe = new RecordingRunnerProcessProbe();
            probe.Enqueue(Success("provider/stable-model\n"));
            probe.Enqueue(failure);
            await using var provider = BuildProvider(probe);
            var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
            await RefreshAsync(provider, profile.Id);

            var models = await RefreshAsync(provider, profile.Id);

            models.ShouldContain(model =>
                model.Identifier == "provider/stable-model"
                && model.Availability == AgentTuiModelAvailability.Stale);
            models.ShouldNotContain(model => model.Identifier == "provider/partial");
        }
    }

    [Test]
    public async Task Secret_shaped_discovery_diagnostics_never_reach_persisted_results()
    {
        const string canary = "synthetic-discovery-secret-canary";
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("provider/stable-model\n"));
        probe.Enqueue(new RunnerProcessResult(
            1,
            string.Empty,
            "API_TOKEN=[REDACTED]",
            TimedOut: false,
            SensitiveOutputDetected: true));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await RefreshAsync(provider, profile.Id);

        await RefreshAsync(provider, profile.Id);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.AgentTuiValidationRuns.AsNoTracking()
            .Where(run => run.ProfileId == profile.Id)
            .Select(run => new { run.ResultsJson, run.Summary, run.CapabilitiesJson })
            .ToListAsync();
        var retainedText = string.Join(
            "\n",
            persisted.Select(run =>
                $"{run.ResultsJson}\n{run.Summary}\n{run.CapabilitiesJson}"));
        retainedText.ShouldNotContain(canary);
        retainedText.ShouldNotContain("API_TOKEN", Case.Insensitive);
    }

    [Test]
    public async Task Non_OpenCode_refresh_returns_cached_catalogue_without_a_probe()
    {
        var probe = new RecordingRunnerProcessProbe();
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.ClaudeCode);

        var models = await RefreshAsync(provider, profile.Id);

        probe.Requests.ShouldBeEmpty();
        var identifiers = models.Select(model => model.Identifier).ToArray();
        identifiers.ShouldContain("fable");
        identifiers.ShouldContain("opus");
        identifiers.ShouldContain("sonnet");
        identifiers.ShouldContain("haiku");
        identifiers.ShouldContain("operator/custom-model");
    }

    [Test]
    public async Task Cached_list_get_models_and_capabilities_are_pure_reads()
    {
        var probe = new RecordingRunnerProcessProbe();
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>();
        await service.ListAsync(CancellationToken.None);
        await service.GetAsync(profile.Id, CancellationToken.None);
        await service.GetModelsAsync(profile.Id, CancellationToken.None);
        await service.GetCapabilitiesAsync(profile.Id, CancellationToken.None);

        probe.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task Missing_profile_preserves_the_original_not_found_failure()
    {
        await using var provider = BuildProvider(new RecordingRunnerProcessProbe());
        var missingProfileId = Guid.NewGuid();

        var exception = await Should.ThrowAsync<NotFoundException>(
            () => RefreshAsync(provider, missingProfileId));

        exception.Message.ShouldContain(missingProfileId.ToString());
    }

    [Test]
    public async Task Concurrent_same_profile_discovery_callers_join_one_shared_operation()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return Success("provider/joined-model\n");
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var first = RefreshAsync(provider, profile.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = RefreshAsync(provider, profile.Id);
        await Task.Delay(100);
        probe.Requests.Count.ShouldBe(1);
        release.TrySetResult();

        var results = await Task.WhenAll(first, second);
        results[0].Select(model => model.Identifier)
            .ShouldBe(results[1].Select(model => model.Identifier));
        probe.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Cancelling_one_joiner_does_not_cancel_or_dispose_the_shared_discovery()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return Success("provider/survives-cancel\n");
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        using var cancelledCaller = new CancellationTokenSource();

        var cancelled = RefreshAsync(provider, profile.Id, cancelledCaller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var survivor = RefreshAsync(provider, profile.Id);
        cancelledCaller.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        release.TrySetResult();

        var models = await survivor;
        models.ShouldContain(model => model.Identifier == "provider/survives-cancel");
        probe.Requests.Count.ShouldBe(1);
    }

    [Test]
    public async Task Validation_persists_sanitized_stages_capabilities_version_and_suitability()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("OpenCode 1.2.3\n"));
        probe.Enqueue(Success("llmgateway/grok-4-5\nopenai/gpt-5.6-sol\n"));
        probe.Enqueue(new RunnerProcessResult(
            null,
            string.Empty,
            string.Empty,
            TimedOut: false,
            Started: true,
            CleanlyStopped: true));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var run = await ValidateAsync(provider, profile.Id);

        run.ProfileRevisionId.ShouldBe(profile.RevisionId);
        run.RunnerVersion.ShouldBe("OpenCode 1.2.3");
        run.Status.ShouldBe(AgentTuiValidationStatus.Partial);
        run.Stages.Select(stage => stage.Name).ShouldBe([
            "executable", "arguments", "workingDirectory", "authentication",
            "versionCapabilities", "discovery", "startup", "cleanStop", "suitability"
        ]);
        foreach (var stage in run.Stages.Where(stage => stage.Name is not "suitability"))
        {
            (stage.Status is AgentTuiValidationStageStatus.Passed
                or AgentTuiValidationStageStatus.Degraded).ShouldBeTrue();
        }
        run.Capabilities.Single(capability => capability.Name == "structuredActivity").State
            .ShouldBe(AgentTuiCapabilityState.Degraded);
        run.Capabilities.Single(capability => capability.Name == "structuredActivity").Reason
            .ShouldBe("PTY quiet-time fallback; ACP/event integration not active");
        run.Suitability.Interactive.ShouldBeTrue();
        run.Suitability.Queued.ShouldBeFalse();
        run.Suitability.Delegated.ShouldBeFalse();
        run.Suitability.Resumable.ShouldBeFalse();
        probe.Requests[1].Arguments.ShouldBe([
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WrapperPath, "models"
        ]);
        probe.Requests[2].Arguments.ShouldContain("--auto");
        probe.Requests[2].Arguments.ShouldContain("--mini");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await db.AgentTuiValidationRuns.AsNoTracking().SingleAsync(candidate => candidate.Id == run.Id);
        entity.ProfileRevisionId.ShouldBe(profile.RevisionId);
        entity.ResultsJson.Length.ShouldBeLessThanOrEqualTo(16_000);
        entity.CapabilitiesJson.Length.ShouldBeLessThanOrEqualTo(16_000);
        entity.Summary!.Length.ShouldBeLessThanOrEqualTo(4_000);
    }

    [Test]
    public async Task Validation_uses_the_active_immutable_revision_captured_at_start()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref call) == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                    return Success("OpenCode 1.2.3\n");
                }

                return call == 2
                    ? Success("provider/captured-revision\n")
                    : new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true);
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var validation = ValidateAsync(provider, profile.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AgentTuiProfileDto updated;
        await using (var scope = provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>();
            updated = await service.UpdateAsync(
                profile.Id,
                NewRequest(profile.DisplayName, AgentKind.OpenCode) with
                {
                    ExpectedRevision = profile.Revision,
                    Arguments = ["--replacement-launch"]
                },
                CancellationToken.None);
        }
        release.TrySetResult();

        var run = await validation;
        updated.RevisionId.ShouldNotBe(profile.RevisionId);
        run.ProfileRevisionId.ShouldBe(profile.RevisionId);
        run.ProfileRevisionId.ShouldNotBe(updated.RevisionId);
    }

    [Test]
    public async Task In_flight_old_revision_discovery_cannot_verify_models_after_profile_update()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return Success("provider/old-revision-race\n");
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var refresh = RefreshAsync(provider, profile.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        AgentTuiProfileDto updated;
        await using (var scope = provider.CreateAsyncScope())
        {
            updated = await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>().UpdateAsync(
                profile.Id,
                NewRequest(profile.DisplayName, AgentKind.OpenCode) with
                {
                    ExpectedRevision = profile.Revision,
                    Arguments = ["--new-revision-launch"]
                },
                CancellationToken.None);
        }
        release.TrySetResult();

        var models = await refresh;
        updated.RevisionId.ShouldNotBe(profile.RevisionId);
        models.ShouldNotContain(model => model.Identifier == "provider/old-revision-race");
    }

    [Test]
    public async Task Profile_update_marks_previous_revision_discovery_stale()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("provider/revision-one\n"));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await RefreshAsync(provider, profile.Id);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>().UpdateAsync(
                profile.Id,
                NewRequest(profile.DisplayName, AgentKind.OpenCode) with
                {
                    ExpectedRevision = profile.Revision,
                    Arguments = ["--new-revision-launch"]
                },
                CancellationToken.None);
        }
        var models = await ReadModelsAsync(provider, profile.Id);

        models.ShouldContain(model =>
            model.Identifier == "provider/revision-one"
            && model.Availability == AgentTuiModelAvailability.Stale);
    }

    [Test]
    public async Task Profile_update_stales_only_curated_and_operator_models_with_discovery_evidence()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("llmgateway/grok-4-5\noperator/custom-model\n"));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await using (var db = _fixture.CreateDbContext())
        {
            db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Identifier = "operator/plain-model",
                DisplayName = "Plain operator model",
                Source = AgentTuiModelSource.Operator,
                Availability = AgentTuiModelAvailability.Unverified,
                CreatedAt = FixedNow.UtcDateTime,
                UpdatedAt = FixedNow.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        var verified = await RefreshAsync(provider, profile.Id);
        verified.Single(model => model.Identifier == "llmgateway/grok-4-5").Availability
            .ShouldBe(AgentTuiModelAvailability.Verified);
        verified.Single(model => model.Identifier == "operator/custom-model").Availability
            .ShouldBe(AgentTuiModelAvailability.Verified);
        await using (var db = _fixture.CreateDbContext())
        {
            db.AgentTuiModels.Add(new AgentTuiModel
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Identifier = "curated/plain-model",
                DisplayName = "Plain curated model",
                Source = AgentTuiModelSource.Curated,
                Availability = AgentTuiModelAvailability.Unverified,
                CreatedAt = FixedNow.UtcDateTime,
                UpdatedAt = FixedNow.UtcDateTime
            });
            await db.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>().UpdateAsync(
                profile.Id,
                NewRequest(profile.DisplayName, AgentKind.OpenCode) with
                {
                    ExpectedRevision = profile.Revision,
                    Arguments = ["--new-revision-launch"],
                    Models =
                    [
                        new AgentTuiModelWriteDto("operator/custom-model", "Operator label"),
                        new AgentTuiModelWriteDto("operator/plain-model", "Plain operator model")
                    ]
                },
                CancellationToken.None);
        }

        var models = await ReadModelsAsync(provider, profile.Id);
        var curatedEvidence = models.Single(model => model.Identifier == "llmgateway/grok-4-5");
        curatedEvidence.Source.ShouldBe(AgentTuiModelSource.Curated);
        curatedEvidence.Availability.ShouldBe(AgentTuiModelAvailability.Stale);
        curatedEvidence.DiscoveredAt.ShouldBe(FixedNow.UtcDateTime);

        var operatorEvidence = models.Single(model => model.Identifier == "operator/custom-model");
        operatorEvidence.Source.ShouldBe(AgentTuiModelSource.Operator);
        operatorEvidence.Availability.ShouldBe(AgentTuiModelAvailability.Stale);
        operatorEvidence.DiscoveredAt.ShouldBe(FixedNow.UtcDateTime);

        var plainCurated = models.Single(model => model.Identifier == "curated/plain-model");
        plainCurated.Source.ShouldBe(AgentTuiModelSource.Curated);
        plainCurated.Availability.ShouldBe(AgentTuiModelAvailability.Unverified);
        plainCurated.DiscoveredAt.ShouldBeNull();

        var plainOperator = models.Single(model => model.Identifier == "operator/plain-model");
        plainOperator.Source.ShouldBe(AgentTuiModelSource.Operator);
        plainOperator.Availability.ShouldBe(AgentTuiModelAvailability.Unverified);
        plainOperator.DiscoveredAt.ShouldBeNull();
    }

    [Test]
    public async Task Operator_label_and_source_survive_discovery_verification_omission_and_failure()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("operator/custom-model\n"));
        probe.Enqueue(Success("provider/replacement\n"));
        probe.Enqueue(Success("operator/custom-model\n"));
        probe.Enqueue(new RunnerProcessResult(1, string.Empty, string.Empty, false));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var verified = await RefreshAsync(provider, profile.Id);
        var verifiedOperator = verified.Single(model => model.Identifier == "operator/custom-model");
        verifiedOperator.Source.ShouldBe(AgentTuiModelSource.Operator);
        verifiedOperator.DisplayName.ShouldBe("Operator label");
        verifiedOperator.Availability.ShouldBe(AgentTuiModelAvailability.Verified);
        verifiedOperator.DiscoveredAt.ShouldBe(FixedNow.UtcDateTime);

        var omitted = await RefreshAsync(provider, profile.Id);
        omitted.Single(model => model.Identifier == "operator/custom-model").Availability
            .ShouldBe(AgentTuiModelAvailability.Unverified);

        await RefreshAsync(provider, profile.Id);
        var failedOperator = (await RefreshAsync(provider, profile.Id))
            .Single(model => model.Identifier == "operator/custom-model");
        failedOperator.Source.ShouldBe(AgentTuiModelSource.Operator);
        failedOperator.DisplayName.ShouldBe("Operator label");
        failedOperator.Availability.ShouldBe(AgentTuiModelAvailability.Stale);
    }

    [Test]
    public async Task Curated_source_survives_discovery_verification_failure_and_omission()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("llmgateway/grok-4-5\n"));
        probe.Enqueue(new RunnerProcessResult(1, string.Empty, string.Empty, false));
        probe.Enqueue(Success("provider/replacement\n"));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var overlapped = await RefreshAsync(provider, profile.Id);
        var failed = await RefreshAsync(provider, profile.Id);
        var omitted = await RefreshAsync(provider, profile.Id);

        var verifiedCurated = overlapped.Single(model => model.Identifier == "llmgateway/grok-4-5");
        verifiedCurated.Source.ShouldBe(AgentTuiModelSource.Curated);
        verifiedCurated.Availability.ShouldBe(AgentTuiModelAvailability.Verified);
        verifiedCurated.DiscoveredAt.ShouldBe(FixedNow.UtcDateTime);

        var staleCurated = failed.Single(model => model.Identifier == "llmgateway/grok-4-5");
        staleCurated.Source.ShouldBe(AgentTuiModelSource.Curated);
        staleCurated.Availability.ShouldBe(AgentTuiModelAvailability.Stale);

        var omittedCurated = omitted.Single(model => model.Identifier == "llmgateway/grok-4-5");
        omittedCurated.Source.ShouldBe(AgentTuiModelSource.Curated);
        omittedCurated.Availability.ShouldBe(AgentTuiModelAvailability.Unverified);
        omittedCurated.DiscoveredAt.ShouldBeNull();
    }

    [Test]
    public async Task Discovery_timeout_does_not_run_unbounded_stale_cache_work()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("provider/locked-stale\n"));
        await using var provider = BuildProvider(probe, timeoutSeconds: 1);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await RefreshAsync(provider, profile.Id);
        probe.Handler = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        };
        await using var lockingDb = _fixture.CreateDbContext();
        await using var transaction = await lockingDb.Database.BeginTransactionAsync();
        var locked = await lockingDb.AgentTuiModels
            .SingleAsync(model => model.ProfileId == profile.Id
                                  && model.Identifier == "provider/locked-stale");
        locked.DisplayName = "locked during timeout recovery";
        await lockingDb.SaveChangesAsync();

        var elapsed = Stopwatch.StartNew();
        await Should.ThrowAsync<OperationCanceledException>(() => RefreshAsync(provider, profile.Id));
        elapsed.Stop();

        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        await transaction.RollbackAsync();

        probe.Handler = null;
        probe.Enqueue(Success("provider/recovered\n"));
        await RefreshAsync(provider, profile.Id);
        await using var verifyDb = _fixture.CreateDbContext();
        (await verifyDb.AgentTuiValidationRuns.AsNoTracking()
            .CountAsync(run => run.ProfileId == profile.Id
                               && run.Operation == "discovery"
                               && run.Status == AgentTuiValidationStatus.Running))
            .ShouldBe(0);
    }

    [Test]
    public async Task Managed_auth_validation_fails_closed_when_declared_secrets_are_missing()
    {
        var protector = new RecordingSecretProtector();
        var probe = new RecordingRunnerProcessProbe();
        await using var provider = BuildProvider(probe, protector);
        var profile = await CreateProfileAsync(
            provider,
            AgentKind.OpenCode,
            AgentTuiAuthenticationMode.ManagedEnvironment,
            ["SERVICE_TOKEN"]);

        var run = await ValidateAsync(provider, profile.Id);

        run.Stages.Single(stage => stage.Name == "authentication").Status
            .ShouldBe(AgentTuiValidationStageStatus.Failed);
        run.Status.ShouldBe(AgentTuiValidationStatus.Failed);
        protector.UnprotectCalls.ShouldBe(0);
        probe.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task Wrapper_auth_validation_never_accesses_managed_keys()
    {
        var protector = new RecordingSecretProtector { ThrowOnUnprotect = true };
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("OpenCode 1.2.3\n"));
        probe.Enqueue(Success("provider/wrapper-model\n"));
        probe.Enqueue(new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true));
        await using var provider = BuildProvider(probe, protector);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var run = await ValidateAsync(provider, profile.Id);

        run.Stages.Single(stage => stage.Name == "authentication").Status
            .ShouldBe(AgentTuiValidationStageStatus.Passed);
        protector.UnprotectCalls.ShouldBe(0);
    }

    [Test]
    public async Task Configured_managed_auth_is_decrypted_only_into_the_probe_environment()
    {
        const string canary = "synthetic-managed-validation-canary";
        var protector = new RecordingSecretProtector();
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("OpenCode 1.2.3\n"));
        probe.Enqueue(Success("provider/managed-model\n"));
        probe.Enqueue(new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true));
        await using var provider = BuildProvider(probe, protector);
        var profile = await CreateProfileAsync(
            provider,
            AgentKind.OpenCode,
            AgentTuiAuthenticationMode.ManagedEnvironment,
            ["SERVICE_TOKEN"]);
        await using (var scope = provider.CreateAsyncScope())
        {
            var userDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userDb.Users.Add(new User
            {
                Id = TestActorId,
                UserName = $"task4-{Guid.NewGuid():N}",
                Email = $"task4-{Guid.NewGuid():N}@example.invalid",
                IsAdmin = true,
                CreatedAt = FixedNow.UtcDateTime
            });
            await userDb.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>().PutSecretAsync(
                profile.Id,
                "SERVICE_TOKEN",
                new AgentTuiSecretWriteRequest(canary, profile.Revision, "task4-managed-set"),
                CancellationToken.None);
        }

        var run = await ValidateAsync(provider, profile.Id);

        run.Stages.Single(stage => stage.Name == "authentication").Status
            .ShouldBe(AgentTuiValidationStageStatus.Passed);
        protector.UnprotectCalls.ShouldBe(1);
        probe.Requests.ShouldAllBe(request =>
            request.Environment["SERVICE_TOKEN"] == canary
            && request.SecretValues.Contains(canary));
        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.AgentTuiValidationRuns.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == run.Id);
        JsonSerializer.Serialize(persisted).ShouldNotContain(canary);
    }

    [Test]
    public async Task Concurrent_same_profile_validation_callers_join_one_shared_run()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref call);
                if (current == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                    return Success("OpenCode 1.2.3\n");
                }
                return current == 2
                    ? Success("provider/joined-validation\n")
                    : new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true);
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var first = ValidateAsync(provider, profile.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = ValidateAsync(provider, profile.Id);
        await Task.Delay(100);
        probe.Requests.Count.ShouldBe(1);
        release.TrySetResult();

        var results = await Task.WhenAll(first, second);
        results[0].Id.ShouldBe(results[1].Id);
        probe.Requests.Count.ShouldBe(3);
    }

    [Test]
    public async Task Overall_operation_timeout_returns_stale_cache_and_persists_a_terminal_run()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("provider/pre-timeout\n"));
        await using var provider = BuildProvider(probe, timeoutSeconds: 1);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await RefreshAsync(provider, profile.Id);
        probe.Handler = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        };

        var elapsed = Stopwatch.StartNew();
        var models = await RefreshAsync(provider, profile.Id);
        elapsed.Stop();

        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        models.ShouldContain(model =>
            model.Identifier == "provider/pre-timeout"
            && model.Availability == AgentTuiModelAvailability.Stale);
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var timedOutRun = await db.AgentTuiValidationRuns.AsNoTracking()
            .SingleAsync(run => run.ProfileId == profile.Id
                                && run.Operation == "discovery"
                                && run.Status == AgentTuiValidationStatus.TimedOut);
        timedOutRun.CompletedAt.ShouldNotBeNull();
    }

    [Test]
    public async Task Unexpected_probe_failure_is_sanitized_removed_from_join_map_and_retryable()
    {
        var call = 0;
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = (_, _) => Interlocked.Increment(ref call) == 1
                ? throw new InvalidOperationException("C:\\sensitive\\runner-path synthetic failure")
                : Task.FromResult(call == 2
                    ? Success("OpenCode 3.0\n")
                    : call == 3
                        ? Success("provider/after-failure\n")
                        : new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true))
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var first = await ValidateAsync(provider, profile.Id);
        var second = await ValidateAsync(provider, profile.Id);

        first.Status.ShouldBe(AgentTuiValidationStatus.Failed);
        first.Summary.ShouldNotContain("sensitive", Case.Insensitive);
        first.Summary.ShouldNotContain("runner-path", Case.Insensitive);
        second.Status.ShouldBe(AgentTuiValidationStatus.Partial);
        second.Id.ShouldNotBe(first.Id);
        probe.Requests.Count.ShouldBe(4);
    }

    [Test]
    public async Task Failed_validation_recovery_retains_static_capabilities()
    {
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = (_, _) => throw new InvalidOperationException("synthetic probe failure")
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var run = await ValidateAsync(provider, profile.Id);
        await using var scope = provider.CreateAsyncScope();
        var capabilities = await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>()
            .GetCapabilitiesAsync(profile.Id, CancellationToken.None);

        run.Status.ShouldBe(AgentTuiValidationStatus.Failed);
        capabilities.ShouldNotBeEmpty();
        capabilities.Single(capability => capability.Name == "structuredActivity").State
            .ShouldBe(AgentTuiCapabilityState.Degraded);
    }

    [Test]
    public async Task Validation_fails_clean_stop_and_suitability_when_cleanup_is_unconfirmed()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("runner 1.0\n"));
        probe.Enqueue(Success("provider/model\n"));
        probe.Enqueue(new RunnerProcessResult(
            null,
            string.Empty,
            string.Empty,
            TimedOut: false,
            Started: true,
            CleanlyStopped: true,
            CleanupConfirmed: false,
            Error: "Cleanup confirmation is pending."));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var run = await ValidateAsync(provider, profile.Id);

        run.Status.ShouldBe(AgentTuiValidationStatus.Failed);
        run.Stages.Single(stage => stage.Name == "cleanStop").Status
            .ShouldBe(AgentTuiValidationStageStatus.Failed);
        run.Suitability.Queued.ShouldBeFalse();
        run.Suitability.Delegated.ShouldBeFalse();
        run.Suitability.Resumable.ShouldBeFalse();
    }

    [Test]
    public async Task Failed_primary_recovery_is_terminalized_by_a_later_independent_read()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("synthetic recovery failure");
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var validation = ValidateAsync(provider, profile.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var lockingDb = _fixture.CreateDbContext();
        await using var transaction = await lockingDb.Database.BeginTransactionAsync();
        var running = await lockingDb.AgentTuiValidationRuns
            .SingleAsync(run => run.ProfileId == profile.Id
                                && run.Operation == "validation"
                                && run.Status == AgentTuiValidationStatus.Running);
        running.Summary = "locked during primary recovery";
        await lockingDb.SaveChangesAsync();
        release.TrySetResult();

        var elapsed = Stopwatch.StartNew();
        await Should.ThrowAsync<InvalidOperationException>(() => validation);
        elapsed.Stop();
        elapsed.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
        await transaction.RollbackAsync();

        await using var readScope = provider.CreateAsyncScope();
        var reconciled = await readScope.ServiceProvider.GetRequiredService<AgentTuiProfileService>()
            .GetValidationRunAsync(running.Id, CancellationToken.None);

        reconciled.Status.ShouldBe(AgentTuiValidationStatus.TimedOut);
        reconciled.CompletedAt.ShouldNotBeNull();
        await using var verifyDb = _fixture.CreateDbContext();
        (await verifyDb.AgentTuiValidationRuns.AsNoTracking()
            .SingleAsync(run => run.Id == running.Id)).Status
            .ShouldBe(AgentTuiValidationStatus.TimedOut);
    }

    [Test]
    public async Task Completed_validation_is_removed_from_the_join_map_so_retry_runs_again()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(new RunnerProcessResult(9, string.Empty, string.Empty, false));
        probe.Enqueue(Success("provider/failed-validation-model\n"));
        probe.Enqueue(new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true));
        probe.Enqueue(Success("OpenCode 2.0\n"));
        probe.Enqueue(Success("provider/retry-model\n"));
        probe.Enqueue(new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var first = await ValidateAsync(provider, profile.Id);
        var second = await ValidateAsync(provider, profile.Id);

        first.Id.ShouldNotBe(second.Id);
        first.Status.ShouldBe(AgentTuiValidationStatus.Failed);
        second.Status.ShouldBe(AgentTuiValidationStatus.Partial);
        probe.Requests.Count.ShouldBe(6);
    }

    [Test]
    public async Task Validation_stages_continue_after_version_failure_and_refresh_the_catalogue()
    {
        var probe = new RecordingRunnerProcessProbe();
        probe.Enqueue(Success("provider/before-validation\n"));
        probe.Enqueue(new RunnerProcessResult(7, string.Empty, string.Empty, false));
        probe.Enqueue(Success("provider/from-validation\n"));
        probe.Enqueue(new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true));
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);
        await RefreshAsync(provider, profile.Id);

        var run = await ValidateAsync(provider, profile.Id);
        var models = await ReadModelsAsync(provider, profile.Id);

        run.Status.ShouldBe(AgentTuiValidationStatus.Failed);
        run.Stages.Single(stage => stage.Name == "versionCapabilities").Status
            .ShouldBe(AgentTuiValidationStageStatus.Failed);
        run.Stages.Single(stage => stage.Name == "discovery").Status
            .ShouldBe(AgentTuiValidationStageStatus.Passed);
        run.Stages.Single(stage => stage.Name == "startup").Status
            .ShouldBe(AgentTuiValidationStageStatus.Passed);
        run.Stages.Single(stage => stage.Name == "cleanStop").Status
            .ShouldBe(AgentTuiValidationStageStatus.Passed);
        models.ShouldContain(model =>
            model.Identifier == "provider/from-validation"
            && model.Availability == AgentTuiModelAvailability.Verified);
        models.ShouldNotContain(model => model.Identifier == "provider/before-validation");
    }

    [Test]
    public async Task Cached_capabilities_ignore_an_active_running_validation_row()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;
        var probe = new RecordingRunnerProcessProbe
        {
            Handler = async (_, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref call);
                if (current == 1)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken);
                    return Success("OpenCode 4.0\n");
                }
                return current == 2
                    ? Success("provider/capability-read\n")
                    : new RunnerProcessResult(null, string.Empty, string.Empty, false, Started: true, CleanlyStopped: true);
            }
        };
        await using var provider = BuildProvider(probe);
        var profile = await CreateProfileAsync(provider, AgentKind.OpenCode);

        var validation = ValidateAsync(provider, profile.Id);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using (var scope = provider.CreateAsyncScope())
        {
            var capabilities = await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>()
                .GetCapabilitiesAsync(profile.Id, CancellationToken.None);
            capabilities.ShouldNotBeEmpty();
            capabilities.Single(capability => capability.Name == "structuredActivity").State
                .ShouldBe(AgentTuiCapabilityState.Degraded);
        }
        release.TrySetResult();
        await validation;
    }

    private ServiceProvider BuildProvider(
        RecordingRunnerProcessProbe probe,
        RecordingSecretProtector? protector = null,
        int timeoutSeconds = 5)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
            TestDbFixture.ConnectionString,
            npgsql =>
            {
                npgsql.MigrationsAssembly("Antiphon.Server");
                npgsql.SetPostgresVersion(16, 0);
            }));
        services.AddOptions<AgentTuiSettings>().Configure(settings =>
        {
            settings.ProbeTimeoutSeconds = timeoutSeconds;
            settings.MaxProbeOutputBytes = 4096;
        });
        services.AddSingleton<IValidateOptions<AgentTuiSettings>, AgentTuiSettingsValidator>();
        services.AddSingleton<IRunnerProcessProbe>(probe);
        services.AddSingleton<IAgentTuiSecretProtector>(protector ?? new RecordingSecretProtector());
        services.AddSingleton<AgentTuiRunnerCatalog>();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedNow));
        services.AddSingleton<AgentTuiOperationCoordinator>();
        services.AddScoped<AuditService>();
        services.AddScoped<AgentTuiProfileService>();
        services.AddScoped<ICurrentUser>(_ => new TestCurrentUser());
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<AgentTuiProfileDto> CreateProfileAsync(
        ServiceProvider provider,
        AgentKind kind,
        AgentTuiAuthenticationMode authenticationMode = AgentTuiAuthenticationMode.WrapperManaged,
        IReadOnlyList<string>? secretNames = null)
    {
        await using var scope = provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>();
        return await service.CreateAsync(
            NewRequest($"Task 4 {kind} {Guid.NewGuid():N}", kind) with
            {
                AuthenticationMode = authenticationMode,
                SecretEnvironmentNames = secretNames ?? []
            },
            CancellationToken.None);
    }

    private static AgentTuiProfileWriteRequest NewRequest(string displayName, AgentKind kind) => new(
        displayName,
        kind,
        IsEnabled: true,
        IsDefault: false,
        Executable: kind == AgentKind.OpenCode ? "pwsh" : "synthetic-runner",
        Arguments: kind == AgentKind.OpenCode
            ? ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WrapperPath, "--auto", "--mini"]
            : ["--launch"],
        DiscoveryArguments: kind == AgentKind.OpenCode
            ? ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WrapperPath, "models"]
            : [],
        VersionArguments: kind == AgentKind.OpenCode
            ? ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", WrapperPath, "--version"]
            : ["--version"],
        WorkingDirectory: Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
        AuthenticationMode: AgentTuiAuthenticationMode.WrapperManaged,
        NonSecretEnvironment: new Dictionary<string, string> { ["SYNTHETIC_SETTING"] = "ordinary" },
        SecretEnvironmentNames: [],
        ModelArgumentName: "--model",
        Guidance: "Synthetic Task 4 profile",
        Models: [new AgentTuiModelWriteDto("operator/custom-model", "Operator label")]);

    private static async Task<IReadOnlyList<AgentTuiModelDto>> RefreshAsync(
        ServiceProvider provider,
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>()
            .RefreshModelsAsync(profileId, cancellationToken);
    }

    private static async Task<AgentTuiValidationRunDto> ValidateAsync(
        ServiceProvider provider,
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>()
            .ValidateAsync(profileId, cancellationToken);
    }

    private static async Task<IReadOnlyList<AgentTuiModelDto>> ReadModelsAsync(
        ServiceProvider provider,
        Guid profileId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AgentTuiProfileService>()
            .GetModelsAsync(profileId, CancellationToken.None);
    }

    private static RunnerProcessResult Success(string stdout) =>
        new(0, stdout, string.Empty, TimedOut: false);

    private sealed class RecordingRunnerProcessProbe : IRunnerProcessProbe
    {
        private readonly ConcurrentQueue<RunnerProcessResult> _results = new();

        public List<RunnerProcessRequest> Requests { get; } = [];
        public Func<RunnerProcessRequest, CancellationToken, Task<RunnerProcessResult>>? Handler { get; set; }
        public RunnerPathCheck ExecutableCheck { get; set; } = new(true, "Executable is available.");
        public RunnerPathCheck FileCheck { get; set; } = new(true, "Wrapper is available.");
        public RunnerPathCheck DirectoryCheck { get; set; } = new(true, "Working directory is available.");

        public void Enqueue(RunnerProcessResult result) => _results.Enqueue(result);

        public Task<RunnerPathCheck> CheckExecutableAsync(
            string executable,
            CancellationToken cancellationToken) => Task.FromResult(ExecutableCheck);

        public Task<RunnerPathCheck> CheckFileAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(FileCheck);

        public Task<RunnerPathCheck> CheckDirectoryAsync(
            string path,
            CancellationToken cancellationToken) => Task.FromResult(DirectoryCheck);

        public Task<RunnerProcessResult> RunAsync(
            RunnerProcessRequest request,
            CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(request);
            if (Handler is not null)
                return Handler(request, cancellationToken);
            if (_results.TryDequeue(out var result))
                return Task.FromResult(result);
            throw new InvalidOperationException("The recording probe has no queued result.");
        }
    }

    private sealed class RecordingSecretProtector : IAgentTuiSecretProtector
    {
        public int UnprotectCalls { get; private set; }
        public bool ThrowOnUnprotect { get; init; }

        public string Protect(Guid profileId, string environmentName, string plaintext) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

        public string Unprotect(Guid profileId, string environmentName, string protectedValue)
        {
            UnprotectCalls++;
            if (ThrowOnUnprotect)
                throw new InvalidOperationException("Managed key access was not expected.");
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue));
        }
    }

    private sealed record TestCurrentUser : ICurrentUser
    {
        public Guid UserId { get; } = TestActorId;
        public string UserName { get; } = "task4-test";
        public string IpAddress { get; } = "203.0.113.44";
    }
}
