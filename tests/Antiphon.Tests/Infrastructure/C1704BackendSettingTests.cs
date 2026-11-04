using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1704 S1 (BS-1..BS-7, T-D-7/T-D-8): <c>Landing:GitBackend</c> is a scalar in its own options type, refused
/// at startup in any other shape, resolved once per scope, and in S1 always resolves to the CLI.
/// </summary>
[Category("Unit")]
public sealed class C1704BackendSettingTests
{
    /// <summary>BS-1: the accepted values bind; absent is Cli.</summary>
    [Test]
    [Timeout(30_000)]
    [Arguments("", "Cli")]
    [Arguments("\"GitBackend\": \"Cli\"", "Cli")]
    [Arguments("\"GitBackend\": \"LibGit2\"", "LibGit2")]
    [Arguments("\"GitBackend\": \"Compare\"", "Compare")]
    public async Task Backend_values_bind(string json, string expected, CancellationToken ct)
    {
        using var settings = C1704Settings.Build(json);
        settings.Provider.GetRequiredService<IStartupValidator>().Validate();
        settings.Provider.GetRequiredService<IOptions<LandGitBackendSettings>>().Value.GitBackend.ShouldBe(expected);
        await Task.CompletedTask;
    }

    /// <summary>BS-2 (G-18, G-19, G-24): any other shape or spelling refuses startup, naming the key.</summary>
    [Test]
    [Timeout(30_000)]
    [Arguments("[]")]
    [Arguments("{}")]
    [Arguments("[\"Cli\"]")]
    [Arguments("null")]
    [Arguments("\"\"")]
    [Arguments("\"cli\"")]
    [Arguments("\" Cli\"")]
    [Arguments("\"Cli\\n\"")]
    [Arguments("\"Libgit2\"")]
    [Arguments("\"1\"")]
    public async Task Startup_refuses_bad_backend_values(string json, CancellationToken ct)
    {
        using var settings = C1704Settings.Build("\"GitBackend\": " + json);
        var refused = Should.Throw<OptionsValidationException>(() => settings.Provider.GetRequiredService<IStartupValidator>().Validate());
        refused.Message.ShouldContain("Landing:GitBackend");
        refused.Message.ShouldContain("git_backend_invalid");
        await Task.CompletedTask;
    }

    /// <summary>BS-3 (G-23): LibGit2 and Compare are accepted but not built in S1: Cli, one warning per value.</summary>
    [Test]
    [Timeout(30_000)]
    [Arguments("LibGit2")]
    [Arguments("Compare")]
    public async Task Unbuilt_backends_resolve_to_cli_and_log_once(string value, CancellationToken ct)
    {
        using var settings = C1704Settings.Build($"\"GitBackend\": \"{value}\"");
        settings.Backend.Resolve().ShouldBe(LandGitBackendNames.Cli);
        settings.Backend.Resolve().ShouldBe(LandGitBackendNames.Cli);
        var warnings = Warnings(settings);
        warnings.ShouldHaveSingleItem().ShouldContain("git_backend_not_built");
        warnings[0].ShouldContain(value);
        await Task.CompletedTask;
    }

    /// <summary>BS-4 (G-20): an invalid hot reload never throws from the resolver; it is Cli with one warning per bad value.</summary>
    [Test]
    [Timeout(30_000)]
    public async Task Invalid_hot_reload_resolves_to_cli(CancellationToken ct)
    {
        using var settings = C1704Settings.Build("\"GitBackend\": \"Cli\"");
        var backend = settings.Backend;
        backend.Resolve().ShouldBe(LandGitBackendNames.Cli);
        Warnings(settings).ShouldBeEmpty();

        settings.Rewrite("\"GitBackend\": []");
        await C1704Settings.UntilAsync(() => backend.Resolve() == LandGitBackendNames.Cli && Invalid(settings).Count == 1, "[]");
        settings.Rewrite("\"GitBackend\": \"Bogus\"");
        await C1704Settings.UntilAsync(() => backend.Resolve() == LandGitBackendNames.Cli && Invalid(settings).Count == 2, "Bogus");
        for (var i = 0; i < 5; i++) backend.Resolve().ShouldBe(LandGitBackendNames.Cli);
        Invalid(settings).Count.ShouldBe(2, "one warning per distinct bad value");
        Invalid(settings)[1].ShouldContain("Bogus");

        settings.Rewrite("\"GitBackend\": \"Compare\"");
        await C1704Settings.UntilAsync(() => backend.Resolve() == LandGitBackendNames.Cli
            && Warnings(settings).Any(w => w.Contains("git_backend_not_built")), "Compare");
        for (var i = 0; i < 5; i++) backend.Resolve().ShouldBe(LandGitBackendNames.Cli);
        Invalid(settings).Count.ShouldBe(2, "after the fix the invalid warning stops");
    }

    /// <summary>BS-5 (G-21): a bad backend reload never breaks reads of LandingSettings (the branch-deletion kill switch).</summary>
    [Test]
    [Timeout(30_000)]
    public async Task Invalid_backend_reload_leaves_landing_settings_readable(CancellationToken ct)
    {
        using var settings = C1704Settings.Build("\"DeleteSourceBranchAfterPublication\": true, \"GitBackend\": \"Cli\"");
        var landing = settings.Provider.GetRequiredService<IOptionsMonitor<LandingSettings>>();
        landing.CurrentValue.DeleteSourceBranchAfterPublication.ShouldBeTrue();
        settings.Rewrite("\"DeleteSourceBranchAfterPublication\": true, \"GitBackend\": []");
        await C1704Settings.UntilAsync(() => settings.Backend.Resolve() == LandGitBackendNames.Cli && Invalid(settings).Count == 1, "[]");
        landing.CurrentValue.DeleteSourceBranchAfterPublication.ShouldBeTrue();
    }

    /// <summary>BS-6 (G-22): the backend is read once per outer scope, never per read; a new scope sees a new value.</summary>
    [Test]
    [Timeout(30_000)]
    public async Task Backend_is_read_once_per_scope(CancellationToken ct)
    {
        var monitor = new CountingBackendMonitor(LandGitBackendNames.Cli);
        var log = new C1704Log();
        var git = new RecordingGit(new LandGitBackend(monitor, log.For<LandGitBackend>()));
        using (git.BeginOperationScope())
        {
            for (var i = 0; i < 20; i++) await C1704Reads.CallAsync(git, C1704Reads.Methods[i % C1704Reads.Methods.Length], ct);
            using (git.BeginOperationScope()) await git.ReadCommitAsync(C1704Reads.Repo, "HEAD", ct);
        }
        monitor.Reads.ShouldBe(1);
        monitor.Value = "Bogus";
        using (git.BeginOperationScope())
        {
            await git.ReadCommitAsync(C1704Reads.Repo, "HEAD", ct);
            git.ScopeBackend.ShouldBe(LandGitBackendNames.Cli);
        }
        monitor.Reads.ShouldBe(2);
        log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("Bogus")).ShouldBe(1, "the second scope saw the new value");
    }

    /// <summary>BS-7 (G-25): Program registers the backend through the extension (validators, ValidateOnStart) and the no-op observer.</summary>
    [Test]
    [Timeout(30_000)]
    public async Task Program_registers_the_backend_and_observer(CancellationToken ct)
    {
        var program = await File.ReadAllTextAsync(Path.Combine(Antiphon.Tests.Application.DelegateScriptRunner.RepoRoot, "server", "Program.cs"), ct);
        Count(program, "AddLandGitBackendSettings(").ShouldBe(1);
        program.ShouldNotContain("Configure<LandGitBackendSettings>");
        Count(program, "AddSingleton<IGitOperationObserver>(NoGitOperationObserver.Instance)").ShouldBe(1);
        Count(program, "IGitOperationObserver").ShouldBe(1);
        Count(program, "AddSingleton<LandGitBackend>()").ShouldBe(1);
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static List<string> Warnings(C1704Settings settings) =>
        settings.Log.Entries.Where(e => e.Level == LogLevel.Warning && e.Category == typeof(LandGitBackend).FullName)
            .Select(e => e.Message).ToList();

    private static List<string> Invalid(C1704Settings settings) =>
        Warnings(settings).Where(w => w.Contains("git_backend_invalid")).ToList();
}
