using System.Net;
using System.Net.Http.Json;
using Antiphon.Resilience;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.E2E.Fixtures;

// Scenario inputs, not a canned pipeline response: the real HTTP endpoint still projects
// database occupancy, policies, defaults and the supplied runner capability evidence.
internal static class PipelineContractScenario
{
    internal static void Configure(Dictionary<string, string?> settings)
    {
        settings["PhoneHomeRunner:Enabled"] = "false";
        settings["Delegation:DefaultRunnerId"] = "";
        settings["Delegation:MaxConcurrentTasks"] = "2";
        settings["Delegation:MaxOpenTasks"] = "6";
        foreach (var role in new[] { "Plan", "Code", "Review", "Debug", "Coverage", "Docs",
                     "Test", "Deploy", "Merge", "Investigate", "TestDesign", "Mutation", "Commit" })
            settings[$"Delegation:RolePolicy:{role}:RecommendedInFlight"] =
                role is "Code" or "Review" ? "2" : "1";
    }

    internal static void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient(ResilienceClientNames.RunnerRead)
            .AddHttpMessageHandler(() => new CapabilitiesHandler());

    private sealed class CapabilitiesHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/capabilities")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new RunnerCapabilitiesDto(
                        "modern", "modern", "contract scenario", false,
                        Features: ["required-platform-v1", "codex-cli-version-v1"],
                        Platform: "windows", CodexCliVersion: "1.2.3",
                        CodexCliVersionCheckedAtUtc: DateTimeOffset.UtcNow)),
                });
            return base.SendAsync(request, ct);
        }
    }
}
