using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

// Locally inherited worker: exits from the assembly hook before executing test cases.
internal sealed class CompletedCardRemovalCrashWorker
{
    internal const string InputEnvironment = "ANTIPHON_C1032_REMOVAL_CHILD_INPUT";
    internal sealed record Input(string Root, string State, string StoreId, string Repository, string Origin,
        CompletedCardWorkspaceCommand Command, string Cut);

    internal static bool IsOwnedFixturePath(string root, string candidate, char directorySeparator) =>
        candidate.Replace(directorySeparator, '/').StartsWith(
            root.Replace(directorySeparator, '/') + '/', StringComparison.Ordinal);

    internal static string StartIdentity(Process process)
    {
        if (!OperatingSystem.IsLinux()) return process.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var stat = File.ReadAllText("/proc/" + process.Id + "/stat");
        return stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[19];
    }

    [Before(Assembly)]
    public static async Task RunOwnedWorkerAsync()
    {
        var inputPath = Environment.GetEnvironmentVariable(InputEnvironment);
        if (inputPath is null) return;
        try
        {
            var input = JsonSerializer.Deserialize<Input>(File.ReadAllText(inputPath))!;
            var root = Path.GetFullPath(input.Root);
            if (!Path.GetFileName(root).StartsWith("c1032-fence-", StringComparison.Ordinal)
                || !IsOwnedFixturePath(root, input.Command.Binding.WorktreePath, Path.DirectorySeparatorChar)
                || !IsOwnedFixturePath(root, Path.GetFullPath(inputPath), Path.DirectorySeparatorChar))
                throw new IOException("worker requires its parent-owned fixture root");
            var workspace = new RunnerWorkspaceService(input.Repository, Path.Combine(root, "work"), input.Origin,
                psi => Process.Start(psi), publishedBranch: "main");
            workspace.BindRecipient(input.Command.Binding.RunnerId, Guid.Parse(input.StoreId));
            var journal = new RunnerWorkspaceRemovalJournal(input.State, input.StoreId);
            var gate = new RunnerWorkspaceUseGate(journal); workspace.WorkspaceUseGate = gate;
            await using var runtime = new SessionRunnerRuntime(Microsoft.Extensions.Options.Options.Create(
                new SessionRunnerSettings { SessionLogPath = Path.Combine(root, "child-logs") }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SessionRunnerRuntime>.Instance);
            runtime.WorkspaceUseGate = gate;
            await runtime.AdoptOrphanedHostsAsync(new SystemProcessLivenessProbe(), CancellationToken.None);
            var remover = new RunnerCompletedCardWorkspaceRemoval(workspace);
            if (input.Cut == "fifo-refusal")
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                var result = await remover.RemoveAsync(input.Command, journal, gate, cancellation.Token);
                // An unrelated use proves the shared admission semaphore was released;
                // the refused workspace itself deliberately retains its durable fence.
                await using var lease = await gate.AcquireUseAsync(Path.Combine(root, "outside"), cancellation.Token);
                File.WriteAllText(inputPath + ".result", JsonSerializer.Serialize(new { result.Complete, result.Reason, admissionReleased = true }));
                Environment.Exit(0);
            }
            void Pause(string cut)
            {
                if (cut != input.Cut) return;
                var intent = new RunnerWorkspaceRemovalJournal(input.State, input.StoreId).Read()!.Single();
                using var process = Process.GetCurrentProcess();
                File.WriteAllText(inputPath + ".ready.tmp", JsonSerializer.Serialize(new
                {
                    pid = process.Id, startIdentity = StartIdentity(process), intent.Phase,
                    operationId = intent.Command.OperationId, intent.RemainingAllowance,
                    originalExists = Directory.Exists(input.Command.Binding.WorktreePath),
                    asideExists = intent.SetAsidePath is not null && Directory.Exists(intent.SetAsidePath),
                    registrationExists = Directory.Exists(input.Command.Binding.GitDirectory),
                    receiptExists = intent.Receipt is not null,
                }));
                File.Move(inputPath + ".ready.tmp", inputPath + ".ready");
                // Parent observes the marker, verifies PID/start ownership and kills/joins this
                // worker. No sleep chooses a race winner and no worker escapes its parent.
                new ManualResetEventSlim(false).Wait();
            }
            var type = remover.GetType();
            type.GetProperty("AfterJournalStateForTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(remover, (Action<string>)(phase => Pause("state:" + phase)));
            type.GetProperty("AfterMutationForTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(remover, (Action<string>)(effect => Pause("effect:" + effect)));
            type.GetProperty("BeforeDeleteEntryForTest", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(remover, (Action<string>)(path =>
                {
                    if (Path.GetFileName(path) == "cache-2") Pause("partial-delete");
                }));
            dynamic executor = remover;
            await executor.RemoveAsync(input.Command, journal, gate, CancellationToken.None);
            Environment.Exit(88); // completing without the required cut is a failed worker.
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("C1032 worker failed: " + ex.GetType().Name);
            Environment.Exit(89);
        }
    }
}
