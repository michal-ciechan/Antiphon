using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.WorkspaceHooks;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
public class WorkspaceHookRunnerTests
{
    [Test]
    public async Task HookRunner_runs_script_in_workspace_cwd_and_exports_context()
    {
        using var workspace = TempWorkspace.Create();
        var script = await WriteScriptAsync(
            workspace.Path,
            "context-hook",
            """
            @(
                "cwd=$(Get-Location)"
                "hook=$env:ANTIPHON_HOOK_NAME"
                "card=$env:ANTIPHON_CARD_ID"
                "worktree=$env:ANTIPHON_WORKTREE_PATH"
                "custom=$env:CUSTOM_FLAG"
            ) | Set-Content -Path context.txt
            Write-Output "stdout-ok"
            [Console]::Error.WriteLine("stderr-ok")
            exit 0
            """,
            """
            {
              echo "cwd=$(pwd)"
              echo "hook=$ANTIPHON_HOOK_NAME"
              echo "card=$ANTIPHON_CARD_ID"
              echo "worktree=$ANTIPHON_WORKTREE_PATH"
              echo "custom=$CUSTOM_FLAG"
            } > context.txt
            echo stdout-ok
            echo stderr-ok >&2
            exit 0
            """);

        var runner = BuildRunner();
        var context = new WorkspaceHookContext(
            workspace.Path,
            CardId: "E06-123",
            WorktreePath: workspace.Path,
            Environment: new Dictionary<string, string> { ["CUSTOM_FLAG"] = "custom-value" });

        var result = await runner.RunAsync(
            context,
            "before_run",
            CommandFor(script),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.ExitCode.ShouldBe(0);
        result.TimedOut.ShouldBeFalse();
        result.Stdout.ShouldContain("stdout-ok");
        result.Stderr.ShouldContain("stderr-ok");

        var marker = ParseKeyValueFile(Path.Combine(workspace.Path, "context.txt"));
        PathsEqual(marker["cwd"], workspace.Path).ShouldBeTrue();
        marker["hook"].ShouldBe("before_run");
        marker["card"].ShouldBe("E06-123");
        PathsEqual(marker["worktree"], workspace.Path).ShouldBeTrue();
        marker["custom"].ShouldBe("custom-value");
    }

    [Test]
    public async Task HookRunner_nonzero_exit_returns_failure_result()
    {
        using var workspace = TempWorkspace.Create();
        var script = await WriteScriptAsync(
            workspace.Path,
            "failing-hook",
            """
            Write-Output "expected-output"
            [Console]::Error.WriteLine("expected-error")
            exit 42
            """,
            """
            echo expected-output
            echo expected-error >&2
            exit 42
            """);

        var result = await BuildRunner().RunAsync(
            new WorkspaceHookContext(workspace.Path, CardId: "E06-124", WorktreePath: workspace.Path),
            "after_create",
            CommandFor(script),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.ExitCode.ShouldBe(42);
        result.TimedOut.ShouldBeFalse();
        result.Stdout.ShouldContain("expected-output");
        result.Stderr.ShouldContain("expected-error");
    }

    [Test]
    public async Task HookRunner_timeout_kills_hung_hook()
    {
        using var workspace = TempWorkspace.Create();
        var script = await WriteScriptAsync(
            workspace.Path,
            "hung-hook",
            """
            "started" | Set-Content -Path timeout-started.txt
            Start-Sleep -Seconds 10
            exit 0
            """,
            """
            echo started > timeout-started.txt
            sleep 10
            exit 0
            """);

        var result = await BuildRunner().RunAsync(
            new WorkspaceHookContext(workspace.Path, CardId: "E06-125", WorktreePath: workspace.Path),
            "before_run",
            CommandFor(script),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.TimedOut.ShouldBeTrue();
        result.Duration.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        File.Exists(Path.Combine(workspace.Path, "timeout-started.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task HookRunner_runs_optional_workflow_hook_definition()
    {
        using var workspace = TempWorkspace.Create();
        var script = await WriteScriptAsync(
            workspace.Path,
            "definition-hook",
            """
            "definition-ran" | Set-Content -Path definition.txt
            exit 0
            """,
            """
            echo definition-ran > definition.txt
            exit 0
            """);

        var hook = new WorkspaceHookDefinition(CommandFor(script), TimeSpan.FromSeconds(5));

        var result = await BuildRunner().RunAsync(
            new WorkspaceHookContext(workspace.Path, CardId: "E06-126", WorktreePath: workspace.Path),
            "after_run",
            hook,
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Succeeded.ShouldBeTrue();
        File.ReadAllText(Path.Combine(workspace.Path, "definition.txt")).Trim().ShouldBe("definition-ran");
    }

    [Test]
    public async Task HookRunner_pre_hook_nonzero_aborts()
    {
        using var workspace = TempWorkspace.Create();
        var script = await WriteScriptAsync(
            workspace.Path,
            "abort-hook",
            """
            "abort-ran" | Set-Content -Path abort.txt
            exit 42
            """,
            """
            echo abort-ran > abort.txt
            exit 42
            """);

        var service = BuildService();
        var hooks = new WorkflowHooks(
            AfterCreate: null,
            BeforeRun: new WorkspaceHookDefinition(CommandFor(script), TimeSpan.FromSeconds(5)),
            AfterRun: null,
            BeforeRemove: null);

        await Should.ThrowAsync<ConflictException>(() =>
            service.RunBeforeRunAsync(
                new WorkspaceHookContext(workspace.Path, CardId: "E06-127", WorktreePath: workspace.Path),
                hooks,
                CancellationToken.None));

        File.Exists(Path.Combine(workspace.Path, "abort.txt")).ShouldBeTrue();
    }

    [Test]
    public async Task HookRunner_post_hook_nonzero_does_not_abort()
    {
        using var workspace = TempWorkspace.Create();
        var script = await WriteScriptAsync(
            workspace.Path,
            "post-hook",
            """
            "post-ran" | Set-Content -Path post.txt
            exit 42
            """,
            """
            echo post-ran > post.txt
            exit 42
            """);

        var service = BuildService();
        var hooks = new WorkflowHooks(
            AfterCreate: null,
            BeforeRun: null,
            AfterRun: new WorkspaceHookDefinition(CommandFor(script), TimeSpan.FromSeconds(5)),
            BeforeRemove: null);

        var result = await service.RunAfterRunAsync(
            new WorkspaceHookContext(workspace.Path, CardId: "E06-128", WorktreePath: workspace.Path),
            hooks,
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.Succeeded.ShouldBeFalse();
        result.ExitCode.ShouldBe(42);
        File.Exists(Path.Combine(workspace.Path, "post.txt")).ShouldBeTrue();
    }

    private static WorkspaceHookRunner BuildRunner() =>
        new(NullLogger<WorkspaceHookRunner>.Instance);

    private static WorkspaceHookService BuildService() =>
        new(BuildRunner(), NullLogger<WorkspaceHookService>.Instance);

    private static async Task<string> WriteScriptAsync(
        string workspacePath,
        string name,
        string windowsBody,
        string unixBody)
    {
        var extension = OperatingSystem.IsWindows() ? ".ps1" : ".sh";
        var scriptPath = Path.Combine(workspacePath, name + extension);
        var contents = OperatingSystem.IsWindows()
            ? windowsBody.Replace("\n", "\r\n")
            : $"#!/bin/sh\n{unixBody.Replace("\r\n", "\n")}";

        await File.WriteAllTextAsync(scriptPath, contents);
        return scriptPath;
    }

    private static string CommandFor(string scriptPath) =>
        OperatingSystem.IsWindows()
            ? $".\\{Path.GetFileName(scriptPath)}"
            : $"sh ./{Path.GetFileName(scriptPath)}";

    private static Dictionary<string, string> ParseKeyValueFile(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1]);

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                comparison);
    }

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempWorkspace Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"antiphon-hook-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for failed process tests.
            }
        }
    }
}
