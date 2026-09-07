using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentReportStoreTests
{
    [Test]
    public async Task Exact_content_and_full_ids_own_distinct_files()
    {
        await using var w = new ReportWorkspace(); await w.InitializeAsync();
        var a = w.Task("é🙂\r\nreport  ", Guid.Parse("12345678-1111-1111-1111-111111111111"));
        var b = w.Task("é🙂\nreport", a.Id);
        var c = w.Task(a.Result!, Guid.Parse("12345678-2222-2222-2222-222222222222"));
        DelegationNoteDigest.Compute(a.Result!).ShouldBe(DelegationNoteDigest.Compute(b.Result!));
        var paths = new List<string>();
        foreach (var task in new[] { a, b, c })
        {
            var result = await w.Store().StoreAsync(task, CancellationToken.None);
            result.Path.ShouldBe(w.Expected(task), result.Reason);
            (await File.ReadAllBytesAsync(result.Path!)).ShouldBe(Encoding.UTF8.GetBytes(task.Result!));
            (await File.ReadAllTextAsync(result.Path!)).ShouldBe(task.Result);
            paths.Add(result.Path!);
        }
        paths.Distinct().Count().ShouldBe(3);
    }
    [Test]
    [Arguments("main")] [Arguments("nested-main")] [Arguments("worktree")]
    [Arguments("nested-worktree")] [Arguments("configured")] [Arguments("missing")]
    [Arguments("relative")] [Arguments("temp")] [Arguments("worktree-override")]
    [Arguments("unsupported")] [Arguments("unsupported-rescued")]
    public async Task Root_selection_requires_a_durable_owned_location(string scenario)
    {
        await using var w = new ReportWorkspace(); await w.InitializeAsync(true);
        var task = w.Task("report"); var settings = new DelegationSettings();
        task.RepoPath = null;
        if (scenario == "main") task.WorkingDirectory = w.Main;
        if (scenario.StartsWith("nested")) task.WorkingDirectory = Directory.CreateDirectory(Path.Combine(
            scenario == "nested-main" ? w.Main : w.Worktree, "nested")).FullName;
        using var nonGit = new ReportTempWorkspace();
        if (scenario is "configured" or "missing" or "relative" or "temp" or "worktree-override")
            task.WorkingDirectory = nonGit.Path;
        if (scenario.StartsWith("unsupported"))
        {
            var odd = Path.Combine(w.Root, "odd"); Directory.CreateDirectory(odd);
            await w.GitAsync(odd, "init", "--separate-git-dir=" + Path.Combine(w.Root, "odd-admin"));
            task.WorkingDirectory = odd;
        }
        if (scenario is "configured" or "unsupported-rescued") settings.ReportStorageRoot = Path.Combine(w.Main, ".antiphon", "configured");
        if (scenario == "relative") settings.ReportStorageRoot = "reports";
        if (scenario == "temp") settings.ReportStorageRoot = nonGit.Path;
        if (scenario == "worktree-override") settings.ReportStorageRoot = Path.Combine(w.Worktree, "reports");
        var result = await w.Store(settings).StoreAsync(task, CancellationToken.None);
        if (scenario is "missing" or "relative" or "temp" or "worktree-override" or "unsupported")
            result.Succeeded.ShouldBeFalse(scenario);
        else
        {
            result.Path.ShouldBe(scenario is "configured" or "unsupported-rescued"
                ? w.Expected(task).Replace(Path.Combine(".antiphon", "reports"), Path.Combine(".antiphon", "configured"))
                : w.Expected(task), result.Reason);
        }
    }
    [Test]
    [Arguments("existing-ignore")] [Arguments("local-exclude")] [Arguments("tracked")]
    [Arguments("negation")] [Arguments("exclude-refused")]
    public async Task Ignore_and_tracking_are_checked_before_writing(string scenario)
    {
        await using var w = new ReportWorkspace(); await w.InitializeAsync();
        var task = w.Task("private report bytes"); var path = w.Expected(task);
        var ignore = scenario == "existing-ignore" ? ".antiphon/\n"
            : scenario == "negation" ? "!.antiphon/reports/\n!.antiphon/reports/**\n" : "# unchanged\n";
        await File.WriteAllTextAsync(Path.Combine(w.Main, ".gitignore"), ignore);
        if (scenario == "tracked")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "tracked sentinel");
            await w.GitAsync(w.Main, "add", "-f", path);
        }
        if (scenario == "exclude-refused")
        {
            var exclude = Path.Combine(w.Main, ".git", "info", "exclude");
            File.Delete(exclude); Directory.CreateDirectory(exclude);
        }
        var store = w.Store();
        store.BeforeIoAsync = async (phase, ct) =>
        {
            if (phase != "write") return;
            (await w.GitAsync(w.Main, "check-ignore", "--no-index", path)).ShouldNotBeNullOrWhiteSpace();
            (await w.GitAsync(w.Main, "ls-files", "--", path)).ShouldBeEmpty();
        };
        var result = await store.StoreAsync(task, CancellationToken.None);
        if (scenario is "tracked" or "negation" or "exclude-refused")
        {
            result.Succeeded.ShouldBeFalse();
            if (scenario == "tracked") (await File.ReadAllTextAsync(path)).ShouldBe("tracked sentinel");
            else File.Exists(path).ShouldBeFalse();
        }
        else result.Path.ShouldBe(path, result.Reason);
        (await File.ReadAllTextAsync(Path.Combine(w.Main, ".gitignore"))).ShouldBe(ignore);
    }
    [Test]
    public async Task Publication_exposes_only_a_complete_verified_report()
    {
        await using var w = new ReportWorkspace(); await w.InitializeAsync();
        var task = w.Task("exact full report é🙂"); var path = w.Expected(task);
        var legacy = Path.Combine(w.Main, ".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!); await File.WriteAllTextAsync(legacy, "author detail");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = w.Store(); store.BeforePublishAsync = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); };
        var write = store.StoreAsync(task, CancellationToken.None);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); File.Exists(path).ShouldBeFalse(); }
        finally { release.TrySetResult(); }
        (await write).Path.ShouldBe(path);
        store.BeforePublishAsync = null;
        (await store.StoreAsync(task, CancellationToken.None)).Path.ShouldBe(path);
        await File.WriteAllTextAsync(path, "partial");
        (await store.StoreAsync(task, CancellationToken.None)).Path.ShouldBe(path);
        (await File.ReadAllTextAsync(path)).ShouldBe(task.Result);
        File.Delete(path);
        var simultaneous = await Task.WhenAll(store.StoreAsync(task, CancellationToken.None), store.StoreAsync(task, CancellationToken.None));
        simultaneous.All(r => r.Path == path).ShouldBeTrue();
        (await File.ReadAllTextAsync(legacy)).ShouldBe("author detail");
    }

    [Test]
    public async Task Paths_over_the_persisted_limit_are_unavailable()
    {
        await using var w = new ReportWorkspace(); await w.InitializeAsync();
        using var temp = new ReportTempWorkspace();
        var task = w.Task("report"); task.RepoPath = null; task.WorkingDirectory = temp.Path;
        var root = Path.Combine(w.Main, ".antiphon", string.Join(Path.DirectorySeparatorChar,
            Enumerable.Repeat(new string('x', 80), 12)));
        root.Length.ShouldBeGreaterThan(1000);
        var result = await w.Store(new() { ReportStorageRoot = root }).StoreAsync(task, CancellationToken.None);
        result.Path.ShouldBeNull(); result.Reason.ShouldBe("path-too-long"); Directory.Exists(root).ShouldBeFalse();
    }
    [Test]
    [Arguments("root")] [Arguments("write")] [Arguments("publish")] [Arguments("readback")]
    [Arguments("denied")] [Arguments("budget")] [Arguments("host-cancel")]
    public async Task Failures_and_cancellation_never_publish_a_bad_pointer(string scenario)
    {
        await using var w = new ReportWorkspace(); await w.InitializeAsync();
        var task = w.Task("report"); var store = w.Store();
        using var host = new CancellationTokenSource();
        store.BeforeIoAsync = async (phase, ct) =>
        {
            ct.CanBeCanceled.ShouldBeTrue("storage phases share the bounded cooperative token");
            if (scenario == phase) throw new IOException("injected refusal");
            if (phase != "publish") return;
            if (scenario == "denied") throw new UnauthorizedAccessException();
            if (scenario == "host-cancel") host.Cancel();
            if (scenario is "budget" or "host-cancel") await Task.Delay(Timeout.Infinite, ct);
        };
        if (scenario == "host-cancel")
            await Should.ThrowAsync<OperationCanceledException>(() => store.StoreAsync(task, host.Token));
        else (await store.StoreAsync(task, host.Token)).Succeeded.ShouldBeFalse();
        if (scenario != "readback") File.Exists(w.Expected(task)).ShouldBeFalse();
        Directory.EnumerateFiles(w.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }
}
