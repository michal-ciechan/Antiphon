using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Api.Endpoints;
using Antiphon.Server.Api.Middleware;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class StandingSpecialistRoutingHttpTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Card0415_V09_changed_primary_does_not_reuse_its_process_as_an_alternate(bool disableFirst)
    {
        await using var h = await Harness.CreateAsync();
        var current = await h.PutAsync(null, h.Pairs);
        var oldPrimary = current.CandidateStates.Single(c => c.PhysicalAgentId == h.AgentId).Id;
        if (disableFirst) current = await h.PutAsync(current.ConcurrencyToken, h.Pairs, false);
        await using (var db = h.Context())
        {
            // Simulate a separately committed primary identity edit. Routing must reconcile its
            // own physical relation; it cannot carry a prior certificate into the replacement.
            var owner = await db.Agents.SingleAsync();
            owner.Kind = AgentKind.Codex;
            owner.ModelLevel = AgentModelLevel.Low;
            owner.ModelId = null;
            await db.SaveChangesAsync();
        }
        var after = await h.PutAsync(current.ConcurrencyToken, [h.Pairs[1], h.Pairs[0]]);
        var primary = after.CandidateStates.Single(c => c.PhysicalAgentId == h.AgentId);
        primary.AgentKind.ShouldBe(AgentKind.Codex);
        primary.Status.ShouldBe(StandingSpecialistCandidateStatus.PendingDependency);
        var alternate = after.CandidateStates.Single(c => c.Id == oldPrimary);
        alternate.PhysicalAgentId.ShouldBeNull();
        alternate.Status.ShouldBe(StandingSpecialistCandidateStatus.DeclaredButUnprovisioned);
        alternate.UnprovisionedAt.ShouldNotBeNull();
        await using var stored = h.Context();
        (await stored.Agents.CountAsync()).ShouldBe(1);
        (await stored.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Card0415_V09_null_configuration_preserves_exact_primary_and_declared_pairs_round_trip()
    {
        await using var h = await Harness.CreateAsync();
        var initial = await h.GetAsync();
        initial.ConcurrencyToken.ShouldBeNull();
        initial.PrimaryModelAlias.ShouldBe("sonnet");
        initial.Candidates.ShouldBeEmpty();
        var updated = await h.PutAsync(null, h.Pairs);
        updated.Candidates.ShouldBe(h.Pairs);
        updated.CandidateStates.Count.ShouldBe(2);
        updated.CandidateStates.ShouldAllBe(c => c.Status != StandingSpecialistCandidateStatus.Qualified);
        updated.CandidateStates.Single(c => c.AgentKind == AgentKind.Codex).Status
            .ShouldBe(StandingSpecialistCandidateStatus.PendingDependency);
        updated.CandidateStates.Single(c => c.AgentKind == AgentKind.ClaudeCode).ModelAlias.ShouldBe("sonnet");
        await using var db = h.Context();
        (await db.Agents.CountAsync()).ShouldBe(1, "saving configuration never creates a physical alternate");
        (await db.AgentTasks.CountAsync()).ShouldBe(0, "saving configuration never starts a qualification task");
        (await db.StandingSpecialistRoutings.SingleAsync()).CandidatesJson.ShouldContain("ClaudeCode");
    }

    [Test]
    [Arguments("empty")]
    [Arguments("too-many")]
    [Arguments("duplicate")]
    [Arguments("partial")]
    [Arguments("unsupported")]
    [Arguments("unknown-level")]
    [Arguments("wrong-head")]
    public async Task Card0415_V09_invalid_pairs_preserve_the_last_valid_revision(string defect)
    {
        await using var h = await Harness.CreateAsync();
        var current = await h.PutAsync(null, h.Pairs);
        RoutingCandidate[] bad = defect switch
        {
            "empty" => [],
            "too-many" => [.. h.Pairs, new(AgentKind.ClaudeCode, AgentModelLevel.Low), new(AgentKind.Codex, AgentModelLevel.High)],
            "duplicate" => [h.Pairs[0], h.Pairs[0]],
            "partial" => [h.Pairs[0], new(null, AgentModelLevel.Low)],
            "unsupported" => [h.Pairs[0], new(AgentKind.Grok, AgentModelLevel.Low)],
            "unknown-level" => [h.Pairs[0], new(AgentKind.Codex, (AgentModelLevel)999)],
            _ => [h.Pairs[1], h.Pairs[0]],
        };
        using var response = await h.Client.PutAsJsonAsync(h.Path,
            new PutStandingSpecialistRoutingRequest(current.ConcurrencyToken, true, bad), h.Json);
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var after = await h.GetAsync();
        after.ConcurrencyToken.ShouldBe(current.ConcurrencyToken);
        after.Candidates.ShouldBe(current.Candidates);
    }

    [Test]
    public async Task Card0415_V09_competing_fresh_writes_commit_one_revision()
    {
        await using var h = await Harness.CreateAsync();
        var current = await h.PutAsync(null, h.Pairs);
        var request = new PutStandingSpecialistRoutingRequest(current.ConcurrencyToken, true, h.Pairs);
        var responses = await Task.WhenAll(h.Client.PutAsJsonAsync(h.Path, request, h.Json), h.Client.PutAsJsonAsync(h.Path, request, h.Json));
        try
        {
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
            responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
            var refusal = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
            (await refusal.Content.ReadAsStringAsync()).ShouldContain("specialist_routing_stale");
            await using var db = h.Context();
            (await db.StandingSpecialistRoutings.CountAsync()).ShouldBe(1);
            (await db.StandingSpecialistCandidateStates.CountAsync()).ShouldBe(2);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Test]
    public async Task Card0415_V09_reordering_retains_identity_and_revalidate_does_not_clear_quarantine()
    {
        await using var h = await Harness.CreateAsync();
        RoutingCandidate[] pairs = [.. h.Pairs, new(AgentKind.ClaudeCode, AgentModelLevel.Low)];
        var current = await h.PutAsync(null, pairs);
        var candidateId = current.CandidateStates.Single(c => c.AgentKind == AgentKind.ClaudeCode && c.ModelLevel == AgentModelLevel.Low).Id;
        Guid authorization;
        await using (var seed = h.Context())
        {
            // Storage-preservation fixture only; this is not qualification or execution evidence.
            var candidate = await seed.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId);
            candidate.Status = StandingSpecialistCandidateStatus.Quarantined;
            candidate.TransientFailures = 3;
            candidate.Reason = "3 consecutive transient failures; explicit revalidate required";
            authorization = candidate.QualificationAuthorization;
            await seed.SaveChangesAsync();
        }
        var reordered = await h.PutAsync(current.ConcurrencyToken, [pairs[0], pairs[2], pairs[1]]);
        reordered.CandidateStates.Select(c => c.Id).Order().ShouldBe(current.CandidateStates.Select(c => c.Id).Order());
        using var response = await h.Client.PostAsJsonAsync(h.Path + "/revalidate",
            new RevalidateStandingSpecialistRequest(reordered.ConcurrencyToken!.Value), h.Json);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await using var db = h.Context();
        var stored = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidateId);
        stored.Status.ShouldBe(StandingSpecialistCandidateStatus.Quarantined);
        stored.TransientFailures.ShouldBe(3);
        stored.QualificationAuthorization.ShouldNotBe(authorization);
        stored.QualifiedAt.ShouldBeNull();
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Card0415_V09_disabled_revalidation_and_lookalike_owner_are_refused()
    {
        await using var h = await Harness.CreateAsync();
        var current = await h.PutAsync(null, h.Pairs, enabled: false);
        using var revalidate = await h.Client.PostAsJsonAsync(h.Path + "/revalidate",
            new RevalidateStandingSpecialistRequest(current.ConcurrencyToken!.Value), h.Json);
        revalidate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await using var db = h.Context();
        await db.Agents.Where(a => a.Id == h.AgentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Slug, "lookalike-" + h.AgentId));
        using var get = await h.Client.GetAsync(h.Path);
        get.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    internal sealed class Harness(IsolatedTestSchema database, WebApplication app, Guid agentId, bool network) : IAsyncDisposable
    {
        public Guid AgentId => agentId;
        public string Path => $"/api/agents/{agentId}/specialist-routing";
        public HttpClient Client { get; } = network
            ? new HttpClient { BaseAddress = new Uri(app.Urls.Single()) }
            : app.GetTestClient();
        public JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        public RoutingCandidate[] Pairs => [new(AgentKind.ClaudeCode, AgentModelLevel.High), new(AgentKind.Codex, AgentModelLevel.Low)];
        public AppDbContext Context() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.ConnectionString).Options);
        public async Task<StandingSpecialistRoutingDto> GetAsync() =>
            (await Client.GetFromJsonAsync<StandingSpecialistRoutingDto>(Path, Json))!;
        public async Task<StandingSpecialistRoutingDto> PutAsync(Guid? token, IReadOnlyList<RoutingCandidate> pairs, bool enabled = true)
        {
            using var response = await Client.PutAsJsonAsync(Path, new PutStandingSpecialistRoutingRequest(token, enabled, pairs), Json);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<StandingSpecialistRoutingDto>(Json))!;
        }
        public static async Task<Harness> CreateAsync(bool network = false)
        {
            var database = await TestDbFixture.CreateIsolatedSchemaAsync();
            var builder = WebApplication.CreateBuilder();
            if (network) builder.WebHost.UseUrls("http://127.0.0.1:0");
            else builder.WebHost.UseTestServer();
            builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(database.ConnectionString));
            builder.Services.AddSingleton<IOptions<DelegationSettings>>(Options.Create(new DelegationSettings()));
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IEventBus, MockEventBus>();
            builder.Services.AddScoped<StandingSpecialistRoutingService>();
            var app = builder.Build();
            app.UseMiddleware<ExceptionMiddleware>();
            app.MapStandingSpecialistRoutingEndpoints();
            await app.StartAsync();
            var id = Guid.NewGuid();
            var harness = new Harness(database, app, id, network);
            await using var db = harness.Context();
            db.Agents.Add(new Agent
            {
                Id = id, Name = "routing fixture", Slug = CheckInterpreterProvisioner.DefaultSlug,
                Kind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.High, ModelId = "sonnet",
                AlwaysOn = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return harness;
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
