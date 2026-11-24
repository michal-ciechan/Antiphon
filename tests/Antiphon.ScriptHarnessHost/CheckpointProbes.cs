using System.Diagnostics;
using System.Reflection;
using System.Text;

// Unit 20 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the checkpoint tool's own readers, probed in a
// process of their own because each probe changes what is process-wide (PATH, the in-process git switch) or is supervised
// and killed from outside. They replaced the pwsh probes scripts/test-checkpoint-bounded-reader.ps1 and
// scripts/test-checkpoint-git-encoding.ps1, which loaded the same tool build and, beside it, run-checkpoint.ps1's own
// readers (deleted with it). The tool assembly is loaded from its file, its private readers by reflection.
internal static class CheckpointProbes
{
    private const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;

    public static bool Matches(string[] args) =>
        args.Length == 7 && args[0] == "checkpoint-bounded-reader"
        || args.Length == 4 && args[0] is "checkpoint-git-encoding" or "checkpoint-stamp";

    public static int Run(string[] args) => args[0] switch
    {
        "checkpoint-bounded-reader" => BoundedReader(Load(args[1]), args[2], args[3], args[4] == "held", int.Parse(args[5]), args[6]),
        "checkpoint-git-encoding" => GitEncoding(Load(args[1]), args[2], args[3]),
        _ => Stamp(Load(args[1]), args[2], args[3]),
    };

    private static Assembly Load(string toolDll)
    {
        Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(toolDll)!, "Antiphon.HostCleanup.dll"));
        return Assembly.LoadFrom(toolDll);
    }

    private static object? Field(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);

    // CARD-1199/CARD-1381 (CheckpointBoundedReaderTests; scripts/test-checkpoint-bounded-readers.mjs supervises, times and
    // rescues this process). project: the static project-reference read of ProjectWriteRoots, over a FIFO when held (refused
    // as not regular, never waited on); only the read is timed, a pre-read delay standing in for startup (C1381).
    // tool-git: BuildConfinement's bounded git child; tool-eval: its MSBuild evaluation. The fixture's bin directory goes
    // first on this process's PATH, so a stub git or dotnet whose descendant holds both pipes answers.
    private static int BoundedReader(Assembly tool, string mode, string fixture, bool held, int delayMs, string nonce)
    {
        Environment.SetEnvironmentVariable("PATH", Path.Combine(fixture, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        var confinement = tool.GetType("Antiphon.Checkpoints.BuildConfinement", throwOnError: true)!;
        switch (mode)
        {
            case "project":
            {
                if (nonce.Length != 32) throw new ArgumentException("project timing nonce required");
                var read = tool.GetType("Antiphon.Checkpoints.ProjectWriteRoots", throwOnError: true)!.GetMethod("TryReadReferences", Private)!;
                var delay = Stopwatch.StartNew();
                if (delayMs > 0) Thread.Sleep(delayMs);
                delay.Stop();
                object?[] call = [Path.Combine(fixture, "fixture.csproj"), null];
                var clock = Stopwatch.StartNew();
                Exception? refused = null;
                var admitted = false;
                try { admitted = (bool)read.Invoke(null, call)!; }
                catch (TargetInvocationException ex) { refused = ex.InnerException ?? ex; }
                finally
                {
                    clock.Stop();
                    Console.Error.WriteLine($"C1381_READ {nonce} project {(held ? "held" : "ordinary")} {clock.ElapsedMilliseconds} {delay.ElapsedMilliseconds}");
                }
                if (held)
                {
                    if (refused is null || !refused.Message.Contains("not regular", StringComparison.Ordinal))
                        throw new InvalidOperationException("held project admitted: " + (refused?.Message ?? "no refusal"));
                }
                else if (refused is not null || !admitted || call[1] is not List<string> { Count: 1 } references || references[0] != "child.csproj")
                    throw new InvalidOperationException("project value mismatch: " + refused?.Message);
                break;
            }
            case "tool-git":
            {
                var result = confinement.GetMethod("Git", Private)!.Invoke(null, [fixture, (IReadOnlyList<string>)["status"]])!;
                if (held ? (string?)Field(result, "Error") != "git-timeout"
                    : (int)Field(result, "Exit")! != 0 || Field(result, "Error") is not null || ((string)Field(result, "Stdout")!).Trim() != "ordinary-control")
                    throw new InvalidOperationException("git result mismatch: " + Field(result, "Error"));
                break;
            }
            case "tool-eval":
            {
                var result = confinement.GetMethod("Evaluate", Private)!.Invoke(null,
                    [Path.Combine(fixture, "fixture.csproj"), Path.Combine(fixture, "bin-out"), Path.Combine(fixture, "artifacts"),
                        Array.Empty<string>(), fixture, null, CancellationToken.None])!;
                var error = (string?)Field(result, "Error");
                if (held ? error != "msbuild evaluation timed out"
                    : error is not null || ((System.Collections.ICollection)Field(result, "References")!).Count != 0)
                    throw new InvalidOperationException("evaluation mismatch: " + error);
                break;
            }
            default:
                throw new ArgumentException("unknown mode " + mode);
        }
        Console.WriteLine($"PASS {mode} held={held}");
        return 0;
    }

    // CARD-1608 (CheckpointGitEncodingScriptTests): the confinement reads git's -z output as UTF-8 bytes whatever the
    // console code page, so a non-ASCII path change is named exactly (tamper: run with ANTIPHON_GIT_INPROCESS=off, so git
    // answers), and git output that is not UTF-8 is refused, never replaced (encoding: a stub git first on PATH).
    private static int GitEncoding(Assembly tool, string mode, string root)
    {
        var confinement = tool.GetType("Antiphon.Checkpoints.BuildConfinement", throwOnError: true)!;
        Directory.CreateDirectory(root);
        if (mode == "tamper")
        {
            Git(root, "init", "-q", "-b", "fixture");
            Git(root, "config", "core.quotePath", "false");
            Git(root, "config", "user.name", "Fixture");
            Git(root, "config", "user.email", "fixture@example.invalid");
            Git(root, "config", "commit.gpgsign", "false");
            File.WriteAllText(Path.Combine(root, "seed.txt"), "seed\n");
            Git(root, "add", "--", "seed.txt");
            Git(root, "commit", "-qm", "fixture");
            var capture = confinement.GetMethod("Capture", Private)!;
            var settle = confinement.GetMethod("Settle")!;
            object Baseline()
            {
                var captured = capture.Invoke(null, [root, null])!;
                return Field(captured, "Value") ?? throw new InvalidOperationException("baseline " + Field(captured, "Error"));
            }
            void Tampered(string relative, string label)
            {
                var baseline = Baseline();
                File.WriteAllText(Path.Combine(root, relative), "changed-content\n");
                var settled = settle.Invoke(null, [baseline, null])!;
                var detail = (string)Field(settled, "Detail")!;
                if ((string?)Field(settled, "Kind") != "tampered" || !detail.Contains("TAMPERED " + relative, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{label} missed kind={Field(settled, "Kind")} detailHex={Convert.ToHexString(Encoding.UTF8.GetBytes(detail))}");
            }
            Tampered("seed.txt", "ascii");
            File.WriteAllText(Path.Combine(root, "seed.txt"), "seed\n");
            if (Field(settle.Invoke(null, [Baseline(), null])!, "Kind") is { } kind) throw new InvalidOperationException("not clean after restore: " + kind);
            // cafe.txt with U+00E9, kept out of this ASCII source file.
            var relative = Encoding.UTF8.GetString([0x63, 0x61, 0x66, 0xC3, 0xA9, 0x2E, 0x74, 0x78, 0x74]);
            File.WriteAllText(Path.Combine(root, relative), "before\n");
            Tampered(relative, "untracked-non-ascii");
            File.WriteAllText(Path.Combine(root, relative), "before\n");
            Git(root, "add", "--", relative);
            Git(root, "commit", "-qm", "track non-ascii");
            Tampered(relative, "tracked-non-ascii");
            Console.WriteLine("C1608 tamper PASS");
            return 0;
        }
        if (mode != "encoding") throw new ArgumentException("unknown mode " + mode);
        var status = confinement.GetMethod("Git", Private)!.Invoke(null, [root, (IReadOnlyList<string>)["status"]])!;
        if ((string?)Field(status, "Error") != "git-encoding" || ((string)Field(status, "Stdout")!).Length != 0)
            throw new InvalidOperationException("invalid UTF-8 accepted: " + Field(status, "Error"));
        var listed = confinement.GetMethod("ListUntracked", Private)!.Invoke(null, [root])!;
        const string expected = "git-ls-files git output is not valid UTF-8 (a non-UTF-8 file name?)";
        if ((string?)Field(listed, "Error") != expected) throw new InvalidOperationException("encoding refusal text: " + Field(listed, "Error"));
        Console.WriteLine("C1608 encoding PASS");
        return 0;
    }

    // CARD-1199 (CheckpointStampSafetyTests, Linux; the test kills this process at four seconds): a tracked file replaced
    // by a FIFO, a symlink, a dangling symlink or a directory is settled as tampered and named, without blocking on the
    // FIFO and without changing the index, the link, its target or the directory; the unchanged regular file is clean.
    // The baseline is built by hand (index entry, content stamp), so only the settlement reads the shape.
    private static int Stamp(Assembly tool, string scratch, string shape)
    {
        var confinement = tool.GetType("Antiphon.Checkpoints.BuildConfinement", throwOnError: true)!;
        var settle = confinement.GetMethod("Settle")!;
        // The scoped test custody marker stays outside the scratch git worktree.
        var root = Path.Combine(scratch, "stamp-fixture");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "user.txt");
        File.WriteAllText(path, "original user bytes");
        Git(root, "init", "-q", "-b", "fixture");
        Git(root, "add", "user.txt");
        Git(root, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "fixture");
        var index = Git(root, "ls-files", "-s");
        var head = Git(root, "rev-parse", "HEAD").Trim();
        var entries = new Dictionary<string, string>(StringComparer.Ordinal) { ["user.txt"] = index.Split('\t')[0] };
        var contents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user.txt"] = "file|19|" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("original user bytes"))),
        };
        var baseline = Activator.CreateInstance(confinement.GetNestedType("Baseline")!,
            [head, root, entries, new Dictionary<string, string>(StringComparer.Ordinal), contents, null])!;
        var control = settle.Invoke(null, [baseline, null])!;
        if (Field(control, "Kind") is not null) throw new InvalidOperationException("healthy tool control: " + Field(control, "Detail"));
        var target = Path.Combine(root, "target");
        if (shape != "regular") File.Delete(path);
        switch (shape)
        {
            case "fifo": Run("mkfifo", root, path); break;
            case "symlink": File.WriteAllText(target, "target bytes"); File.CreateSymbolicLink(path, target); break;
            case "dangling": File.CreateSymbolicLink(path, Path.Combine(root, "missing")); break;
            case "directory": Directory.CreateDirectory(path); break;
            case "regular": break;
            default: throw new ArgumentException("unknown shape " + shape);
        }
        Console.WriteLine($"tool/{shape} STAMP starting");
        var result = settle.Invoke(null, [baseline, null])!;
        var kind = (string?)Field(result, "Kind");
        var detail = (string)Field(result, "Detail")!;
        if (shape == "regular" ? kind is not null : kind != "tampered" || !detail.Contains("TAMPERED user.txt", StringComparison.Ordinal))
            throw new InvalidOperationException("missing named/path refusal: " + detail);
        if (Git(root, "ls-files", "-s") != index) throw new InvalidOperationException("index changed");
        if (shape == "symlink" && File.ReadAllText(target) != "target bytes") throw new InvalidOperationException("link target changed");
        if (shape is "symlink" or "dangling" && new FileInfo(path).LinkTarget is null) throw new InvalidOperationException("link removed");
        if (shape == "directory" && !Directory.Exists(path)) throw new InvalidOperationException("directory removed");
        if (shape == "fifo") Run("test", root, "-p", path);
        // The survival and index checks above come before the fixture removes its own special files.
        if (shape is "fifo" or "symlink" or "dangling") File.Delete(path);
        Console.WriteLine($"tool/{shape} PASS");
        return 0;
    }

    private static string Git(string root, params string[] args) => Run("git", root, args);

    private static string Run(string fileName, string root, params string[] args)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000)) { process.Kill(true); throw new TimeoutException(fileName + " " + string.Join(' ', args)); }
        if (process.ExitCode != 0) throw new InvalidOperationException(fileName + " " + string.Join(' ', args) + ": " + stdout.Result + stderr.Result);
        return stdout.Result;
    }
}
