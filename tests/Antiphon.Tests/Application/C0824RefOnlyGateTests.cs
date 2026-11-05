using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
[Timeout(15_000)] // CARD-0824 provisional JSON reload budget; pure gate bodies are bounded separately below.
public sealed class C0824RefOnlyGateTests
{
    [Test]
    [Timeout(2_000)]
    public void Default_off_and_empty_lists_admit_nothing()
    {
        var defaults = new RefOnlyDesktopSettings();
        defaults.Enabled.ShouldBeFalse("default-disabled");
        defaults.RunnerIds.ShouldBeEmpty();
        defaults.Roles.ShouldBe("none");
        var absent = new ConfigurationBuilder().Build();
        absent.GetSection(RefOnlyDesktopSettings.SectionName).Exists().ShouldBeFalse("optional-section-absent");
        var services = new ServiceCollection();
        services.AddRefOnlyDesktopSettings(absent);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptionsMonitor<RefOnlyDesktopSettings>>().CurrentValue.Enabled.ShouldBeFalse("absent-settings-disabled");
        foreach (var section in new[] { "{}", "{\"Enabled\":false,\"RunnerIds\":[\"fixture-runner\"],\"Roles\":\"Plan\"}",
                     "{\"Enabled\":true,\"RunnerIds\":[],\"Roles\":\"Plan\"}",
                     "{\"Enabled\":true,\"RunnerIds\":[\"fixture-runner\"],\"Roles\":\"none\"}" })
        {
            using var bound = new Bound(section);
            Should.NotThrow(() => _ = bound.Settings, "valid-empty-or-disabled-section-binds");
            bound.Gate.Admits(Eligible()).ShouldBeFalse(section == "{}" ? "default-disabled" :
                section.Contains("false") ? "explicit-disabled-refused" : section.Contains("[]") ? "empty-runners-refused" : "roles-none-refused");
        }
    }

    [Test]
    [Timeout(2_000)]
    public void Role_and_runner_both_must_match()
    {
        using var bound = new Bound(Enabled("Plan"));
        bound.Gate.Admits(Eligible()).ShouldBeTrue("both-match=true");
        var otherRunner = Eligible(); otherRunner.RunnerId = "fixture-runner-other";
        bound.Gate.Admits(otherRunner).ShouldBeFalse("runner-only-mismatch-refused");
        var otherRole = Eligible(); otherRole.Role = AgentTaskRole.Docs;
        bound.Gate.Admits(otherRole).ShouldBeFalse("role-only-mismatch-refused");
        otherRole.RunnerId = "fixture-runner-other";
        bound.Gate.Admits(otherRole).ShouldBeFalse("neither-refused");
        foreach (var runner in new[] { "fixture", "FIXTURE-RUNNER", "fixture-runner-extra", "unrecognized" })
        {
            var task = Eligible(); task.RunnerId = runner;
            bound.Gate.Admits(task).ShouldBeFalse("runner-exact:" + runner);
        }
        using var all = new Bound(Enabled("*"));
        foreach (var role in Enum.GetValues<AgentTaskRole>().Where(r => r != AgentTaskRole.Mutation))
        {
            var task = Eligible(); task.Role = role;
            all.Gate.Admits(task).ShouldBeTrue("explicit-wildcard:" + role);
        }
        var unknown = Eligible(); unknown.Role = (AgentTaskRole)int.MaxValue;
        all.Gate.Admits(unknown).ShouldBeFalse("unknown-role-refused");
        using var none = new Bound(Enabled("none"));
        none.Gate.Admits(Eligible()).ShouldBeFalse("roles-none-refused");
    }

    [Test]
    public void Malformed_reload_closes_only_new_admission()
    {
        var malformed = new (string Json, string Key)[]
        {
            ("{\"Enabled\":\"yes\"}", "Enabled"), ("{\"Enabled\":[]}", "Enabled"),
            ("{\"Enabled\":null}", "Enabled"), ("{\"Roles\":null}", "Roles"),
            ("{\"Roles\":{}}", "Roles"), ("{\"Roles\":[]}", "Roles"),
            ("{\"Roles\":[\"Plan\"]}", "Roles"), ("{\"RunnerIds\":\"fixture-runner\"}", "RunnerIds"),
            ("{\"RunnerIds\":{\"name\":\"fixture-runner\"}}", "RunnerIds"),
            ("{\"RunnerIds\":[null]}", "RunnerIds"), ("{\"Enabld\":true}", "Enabld"),
        };
        foreach (var (json, key) in malformed)
        {
            using var bad = new Bound(json);
            var ex = Should.Throw<OptionsValidationException>(() => _ = bad.Settings, "invalid-startup-key-refused:" + key);
            ex.Message.ShouldContain(RefOnlyDesktopSettings.SectionName + ":" + key);
        }
        foreach (var role in new[] { "", "plan", "1", "Plan,,Docs", "Plan, Docs", "Planner", "Mutation", "*,Plan", "None" })
        {
            using var bad = new Bound(Enabled(role));
            Should.Throw<OptionsValidationException>(() => _ = bad.Settings, "invalid-role-grammar-refused:" + role)
                .Message.ShouldContain(RefOnlyDesktopSettings.SectionName + ":Roles");
        }
        using var reload = new Bound(Enabled("Plan"));
        reload.Gate.Admits(Eligible()).ShouldBeTrue();
        reload.Reload(Enabled("plan"));
        for (var i = 0; i < 3; i++) reload.Gate.Admits(Eligible()).ShouldBeFalse("reload-admits=false");
        reload.Log.Warnings.ShouldBe(1, "diagnostic-once-per-invalid-spell");
        reload.Reload(Enabled("Plan"));
        reload.Gate.Admits(Eligible()).ShouldBeTrue("reload-recovers");
        reload.Reload(Enabled("plan"));
        reload.Gate.Admits(Eligible()).ShouldBeFalse();
        reload.Log.Warnings.ShouldBe(2);
        // Recorded-mode resolution under invalid/off configuration is exercised with persisted bindings in I5.
    }

    [Test]
    [Timeout(2_000)]
    public void Mutation_source_landing_and_desktop_are_excluded()
    {
        using var bound = new Bound(Enabled("*"));
        RemoteWorkspaceService.IsEligible(Eligible()).ShouldBeTrue("eligible-control=true");
        bound.Gate.Admits(Eligible()).ShouldBeTrue("eligible-control=true");
        var variants = new Action<AgentTask>[]
        {
            t => t.Workspace = WorkspaceMode.Shared, t => t.Workspace = WorkspaceMode.ReadOnly,
            t => t.RunnerId = null, t => t.RunnerId = "", t => t.RunnerId = " ",
            t => t.Role = AgentTaskRole.Mutation, t => t.SourceLandingOperationId = Guid.NewGuid(),
        };
        foreach (var change in variants)
        {
            var task = Eligible(); change(task);
            RemoteWorkspaceService.IsEligible(task).ShouldBeFalse("existing-eligibility-exclusion");
            bound.Gate.Admits(task).ShouldBeFalse("new-admission-exclusion");
        }
    }

    private static AgentTask Eligible() => new()
    {
        Id = Guid.NewGuid(), Workspace = WorkspaceMode.Worktree, RunnerId = "fixture-runner", Role = AgentTaskRole.Plan,
    };
    private static string Enabled(string roles) => "{\"Enabled\":true,\"RunnerIds\":[\"fixture-runner\"],\"Roles\":\"" + roles + "\"}";

    private sealed class Bound : IDisposable
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly IConfigurationRoot _configuration;
        private readonly ServiceProvider _services;
        public WarningLog Log { get; } = new();
        public RefOnlyDesktopGate Gate => _services.GetRequiredService<RefOnlyDesktopGate>();
        public RefOnlyDesktopSettings Settings => _services.GetRequiredService<IOptionsMonitor<RefOnlyDesktopSettings>>().CurrentValue;

        public Bound(string section)
        {
            _directory = Path.Combine(Path.GetTempPath(), "c0824-ref-only-json-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "settings.json");
            File.WriteAllText(_path, "{\"Delegation\":{\"RefOnlyDesktop\":" + section + "}}");
            _configuration = new ConfigurationBuilder().AddJsonFile(_path, optional: false, reloadOnChange: false).Build();
            var services = new ServiceCollection();
            services.AddRefOnlyDesktopSettings(_configuration);
            services.AddSingleton<ILogger<RefOnlyDesktopGate>>(Log);
            services.AddSingleton<RefOnlyDesktopGate>();
            _services = services.BuildServiceProvider();
        }
        public void Reload(string section)
        {
            File.WriteAllText(_path, "{\"Delegation\":{\"RefOnlyDesktop\":" + section + "}}");
            try { _configuration.Reload(); } catch (AggregateException ex) when (ex.Flatten().InnerExceptions.All(e => e is OptionsValidationException)) { }
        }
        public void Dispose()
        {
            _services.Dispose(); (_configuration as IDisposable)?.Dispose();
            File.Delete(_path); Directory.Delete(_directory);
        }
    }

    private sealed class WarningLog : ILogger<RefOnlyDesktopGate>
    {
        public int Warnings { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Warning) Warnings++; }
    }
}
