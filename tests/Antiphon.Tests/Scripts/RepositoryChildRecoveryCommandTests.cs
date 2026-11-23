using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Infrastructure;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0448, CARD-0661, CARD-1092. The recover-repository-children command (Antiphon.Nightly.RepositoryChildRecovery, which
/// replaced scripts/recover-repository-children.ps1 in unit 23 of the 2026-11-22 plan) on its seams: the record states read
/// from the server's record keys, the PID looked up only for a well-formed record of this repository with a start identity,
/// the tag, the command line, and the refusals. The identity is a fake here, so no process is looked up or touched;
/// RepositoryChildRecoveryTests runs the same command against the server's journal and real processes.
/// </summary>
[Category("Integration")]
public sealed class RepositoryChildRecoveryCommandTests
{
    private static readonly string Common = Path.Combine(Path.GetTempPath(), "c448-common", ".git");

    [Test]
    public void The_command_line_takes_the_repository_and_both_switches()
    {
        File.ReadAllBytes(Path.Combine(DockerStackDocuments.RepoRoot, "scripts", "lib", "Antiphon.Nightly", "RepositoryChildRecovery.cs")).ShouldAllBe(b => b < 128);
        var preview = RepositoryChildRecoveryOptions.Parse(["--repository", @"C:\checkout"]);
        (preview.Repository, preview.Execute, preview.ConfirmDescendantsExited).ShouldBe((@"C:\checkout", false, false));
        var confirmed = RepositoryChildRecoveryOptions.Parse(["--confirm-descendants-exited", "--repository", "/work/x", "--execute"]);
        (confirmed.Execute, confirmed.ConfirmDescendantsExited).ShouldBe((true, true));
        Should.Throw<NightlyUsageException>(() => RepositoryChildRecoveryOptions.Parse([])).Message.ShouldBe("missing --repository");
        Should.Throw<NightlyUsageException>(() => RepositoryChildRecoveryOptions.Parse(["--repository", "x", "-Execute"]));
    }

    [Test]
    [Arguments("""[]""", "unknown")]
    [Arguments("""{"SchemaVersion":2,"CommonDirectory":"@","ProcessId":7,"StartTicks":5}""", "unknown")]
    [Arguments("""{"CommonDirectory":"@","ProcessId":7,"StartTicks":5}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":null,"StartTicks":null,"Completed":true}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":0,"StartTicks":5}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":"7","StartTicks":5}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"/elsewhere/.git","ProcessId":7,"StartTicks":5}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":null,"ProcessId":7,"StartTicks":5}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":7}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":7,"StartTicks":null,"Completed":true}""", "completed")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":7,"StartTicks":null,"Completed":false}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":7,"StartTicks":null}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":7,"StartTicks":0}""", "unknown")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@","ProcessId":7,"StartTicks":5,"Completed":true}""", "identity")]
    [Arguments("""{"SchemaVersion":1,"CommonDirectory":"@UPPER/","ProcessId":7,"StartTicks":5}""", "identity")]
    [Arguments("""{"schemaVersion":1,"commonDirectory":"@","processId":7,"startTicks":5}""", "unknown")]
    public void A_record_is_looked_up_only_when_it_is_well_formed_and_names_this_repository(string json, string expected)
    {
        var lookups = new List<(int, long)>();
        string Identity(int pid, long ticks) { lookups.Add((pid, ticks)); return "reused"; }
        var common = Common.TrimEnd('/', '\\');
        var commonText = JsonSerializer.Serialize(common)[1..^1];
        var upper = JsonSerializer.Serialize(common.ToUpperInvariant() + Path.DirectorySeparatorChar)[1..^1];
        json = json.Replace("\"@UPPER/\"", "\"" + upper + "\"").Replace("\"@\"", "\"" + commonText + "\"");
        using var record = JsonDocument.Parse(json);
        var state = RepositoryChildRecovery.State(record.RootElement, common, new System.IO.Abstractions.FileSystem(), Identity);
        if (expected == "identity")
        {
            state.ShouldBe("reused");
            lookups.ShouldHaveSingleItem().ShouldBe((7, 5L));
        }
        else
        {
            state.ShouldBe(expected);
            lookups.ShouldBeEmpty("a completed or ambiguous record never looks its PID up (CARD-0661)");
        }
    }

    [Test]
    public void The_tag_names_the_purpose_and_the_task_in_the_fence_sentence_form()
    {
        Tag("""{"Purpose":"remote-prep-push","TaskId":"a1b2c3d4-e5f6-7890-abcd-ef1234567890"}""")
            .ShouldBe(" purpose=remote-prep-push task=a1b2c3d4e5f67890abcdef1234567890");
        Tag("""{"Purpose":null,"TaskId":"not-a-guid"}""").ShouldBe(" task=not-a-guid");
        Tag("""{"Purpose":"","TaskId":null}""").ShouldBe("");
        Tag("""{"SchemaVersion":1}""").ShouldBe("");
        Tag("""[1]""").ShouldBe("");

        static string Tag(string json)
        {
            using var record = JsonDocument.Parse(json);
            return RepositoryChildRecovery.Tag(record.RootElement);
        }
    }

    [Test]
    public async Task Only_a_confirmed_execute_removes_dead_reused_and_completed_records()
    {
        using var repo = new ScratchGitRepo("c448-command");
        var common = RepositoryChildRecovery.CanonicalDirectory(new System.IO.Abstractions.FileSystem(),
            (await repo.GitReadAsync("rev-parse", "--path-format=absolute", "--git-common-dir")).Trim());
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var answers = new Dictionary<int, string> { [11] = "alive", [12] = "dead", [13] = "reused", [14] = "unknown" };
        string Write(string name, int pid, long? ticks, bool completed = false)
        {
            var path = Path.Combine(children, name);
            File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["SchemaVersion"] = 1, ["CommonDirectory"] = common, ["ProcessId"] = pid, ["StartTicks"] = ticks, ["Completed"] = completed,
                ["Purpose"] = null, ["TaskId"] = null,
            }));
            return path;
        }
        var alive = Write("a-alive.json", 11, 100);
        var dead = Write("b-dead.json", 12, 100);
        var reused = Write("c-reused.json", 13, 100);
        var unreadable = Write("d-unknown.json", 14, 100);
        var completed = Write("e-completed.json", 15, null, completed: true);
        File.SetAttributes(dead, File.GetAttributes(dead) | FileAttributes.ReadOnly);

        foreach (var options in new[] { Array.Empty<string>(), ["--execute"], ["--confirm-descendants-exited"] })
        {
            var preview = Run(repo.Path, answers, options);
            preview.Exit.ShouldBe(3);
            preview.Output.ShouldNotContain("recovered (");
            preview.Output.ShouldContain("retained (dead): " + dead);
            preview.Output.ShouldContain("retained (completed): " + completed);
            preview.Output.TrimEnd().ShouldEndWith(RepositoryChildRecovery.Hint);
            Directory.EnumerateFiles(children).Count().ShouldBe(5);
        }

        var confirmed = Run(repo.Path, answers, "--execute", "--confirm-descendants-exited");
        confirmed.Exit.ShouldBe(3, confirmed.Error);
        confirmed.Output.Replace("\r\n", "\n").ShouldBe(
            "retained (alive): " + alive + "\n"
            + "recovered (dead): " + dead + "\n"
            + "recovered (reused): " + reused + "\n"
            + "retained (unknown): " + unreadable + "\n"
            + "recovered (completed): " + completed + "\n"
            + RepositoryChildRecovery.Hint + "\n");
        Directory.EnumerateFiles(children).Order(StringComparer.Ordinal).ShouldBe([alive, unreadable]);
        File.Exists(Path.Combine(common, "antiphon", "landing.lock")).ShouldBeTrue("the lock file stays; its handle was the exclusion");

        File.Delete(alive);
        File.Delete(unreadable);
        var clear = Run(repo.Path, answers, "--execute", "--confirm-descendants-exited");
        clear.Exit.ShouldBe(0, clear.Error);
        clear.Output.ShouldBeEmpty();
    }

    [Test]
    public async Task A_refusal_is_one_warning_and_exit_3()
    {
        using var repo = new ScratchGitRepo("c448-refusal");
        var answers = new Dictionary<int, string>();
        var common = RepositoryChildRecovery.CanonicalDirectory(new System.IO.Abstractions.FileSystem(),
            (await repo.GitReadAsync("rev-parse", "--path-format=absolute", "--git-common-dir")).Trim());

        // The children path is a file: not a plain directory.
        Directory.CreateDirectory(Path.Combine(common, "antiphon"));
        File.WriteAllText(Path.Combine(common, "antiphon", "children"), "not a directory");
        var file = Run(repo.Path, answers, "--execute", "--confirm-descendants-exited");
        (file.Exit, file.Output, file.Error.Trim()).ShouldBe((3, "", "WARNING: Repository recovery refused: Child journal directory is not a plain directory."));
        File.ReadAllText(Path.Combine(common, "antiphon", "children")).ShouldBe("not a directory");

        // git cannot answer: no common directory, nothing created.
        var outside = Directory.CreateTempSubdirectory("c448-not-a-repo-").FullName;
        try
        {
            var notRepo = RunWithGit(outside, answers, (_, _) => new NightlyGitResult(128, "fatal: not a git repository" + Environment.NewLine));
            (notRepo.Exit, notRepo.Output, notRepo.Error.Trim()).ShouldBe((3, "", "WARNING: Repository recovery refused: Cannot resolve Git common directory."));
            var twoLines = RunWithGit(outside, answers, (_, _) => new NightlyGitResult(0, "/a/.git" + Environment.NewLine + "/b/.git" + Environment.NewLine));
            twoLines.Error.Trim().ShouldBe("WARNING: Repository recovery refused: Cannot resolve Git common directory.");
            var missing = RunWithGit(outside, answers, (_, _) => new NightlyGitResult(0, Path.Combine(outside, "gone", ".git") + Environment.NewLine));
            missing.Error.Trim().ShouldBe("WARNING: Repository recovery refused: Git common directory is missing or inaccessible.");
            Directory.Exists(Path.Combine(outside, "gone")).ShouldBeFalse();
        }
        finally { ReadOnlyDirectory.Delete(outside); }
    }

    private static (int Exit, string Output, string Error) Run(string repository, IReadOnlyDictionary<int, string> answers, params string[] options) =>
        RunWithGit(repository, answers, NightlyNativeGit.Run, options);

    private static (int Exit, string Output, string Error) RunWithGit(string repository, IReadOnlyDictionary<int, string> answers, NightlyGit git,
        params string[] options)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new NightlyHost
        {
            Clock = TimeProvider.System,
            Git = git,
            Launcher = new RecordingLauncher(),
            Processes = new FakeProcessTable(),
            Probe = new FakeHostProbe(),
            Out = output,
            Error = error,
            Trace = TextWriter.Null,
        };
        var exit = RepositoryChildRecovery.Run(RepositoryChildRecoveryOptions.Parse(["--repository", repository, .. options]), host,
            new System.IO.Abstractions.FileSystem(), (pid, _) => answers.TryGetValue(pid, out var state) ? state : "unknown");
        return (exit, output.ToString(), error.ToString());
    }
}
