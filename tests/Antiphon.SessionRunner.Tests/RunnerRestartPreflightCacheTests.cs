using System.Security.Cryptography;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
[NotInParallel]
public sealed class RunnerRestartPreflightCacheTests
{
    [Test]
    public async Task Identical_inputs_in_different_roots_share_one_validation()
    {
        using var world = new CacheWorld();
        var a = world.Inputs("a");
        var b = world.CopyTo("b");
        (await world.Cache.ApproveAsync(a)).ShouldBe(PreflightDisposition.Miss);
        (await world.Cache.ApproveAsync(b)).ShouldBe(PreflightDisposition.Hit);
        world.Validator.Invocations.ShouldBe(1);
    }

    [Test]
    [Arguments("entry")]
    [Arguments("helper")]
    [Arguments("platform")]
    [Arguments("wrapper")]
    [Arguments("validator")]
    [Arguments("shell-path")]
    [Arguments("shell-exe-sha")]
    [Arguments("shell-version")]
    [Arguments("shell-engine-sha")]
    [Arguments("entry-same-length-restored-timestamp")]
    [Arguments("entry-crlf-to-lf")]
    [Arguments("entry-bom")]
    [Arguments("root-only")]
    public async Task Each_key_dimension_invalidates_independently(string dimension)
    {
        using var world = new CacheWorld();
        var first = world.Inputs("a");
        if (dimension == "entry-crlf-to-lf")
            File.WriteAllBytes(first.EntryPath, "line1\r\nline2\r\n"u8.ToArray());
        if (dimension == "entry-bom")
            File.WriteAllBytes(first.EntryPath, "hello"u8.ToArray());
        await world.Cache.ApproveAsync(first);
        world.Validator.Invocations.ShouldBe(1);
        DateTime restored = default;
        switch (dimension)
        {
            case "entry":
                File.WriteAllText(first.EntryPath, File.ReadAllText(first.EntryPath) + "X");
                break;
            case "helper":
                File.WriteAllText(first.HelperPath, File.ReadAllText(first.HelperPath) + "X");
                break;
            case "platform":
                File.WriteAllText(first.PlatformPath, File.ReadAllText(first.PlatformPath) + "X");
                break;
            case "wrapper":
                File.WriteAllText(first.WrapperPath, File.ReadAllText(first.WrapperPath) + "X");
                break;
            case "validator":
                world.Cache.ValidatorIdentity = "mutated-validator";
                break;
            case "shell-path":
                world.Identity = world.Identity with { NormalizedPath = @"C:\other\pwsh.exe" };
                break;
            case "shell-exe-sha":
                world.Identity = world.Identity with { ExeSha256 = "BB" };
                break;
            case "shell-version":
                world.Identity = world.Identity with { Version = "0.0.0" };
                break;
            case "shell-engine-sha":
                world.Identity = world.Identity with { EngineSha256 = "FF" };
                break;
            case "entry-same-length-restored-timestamp":
            {
                var bytes = File.ReadAllBytes(first.EntryPath);
                restored = File.GetLastWriteTimeUtc(first.EntryPath);
                bytes[0] = bytes[0] == (byte)'A' ? (byte)'B' : (byte)'A';
                File.WriteAllBytes(first.EntryPath, bytes);
                File.SetLastWriteTimeUtc(first.EntryPath, restored);
                break;
            }
            case "entry-crlf-to-lf":
                File.WriteAllBytes(first.EntryPath, "line1\nline2\n"u8.ToArray());
                break;
            case "entry-bom":
                File.WriteAllBytes(first.EntryPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat("hello"u8.ToArray()).ToArray());
                break;
            case "root-only":
                var copy = world.CopyTo("other-root");
                (await world.Cache.ApproveAsync(copy)).ShouldBe(PreflightDisposition.Hit);
                world.Validator.Invocations.ShouldBe(1);
                return;
        }

        if (dimension == "entry-same-length-restored-timestamp")
            File.GetLastWriteTimeUtc(first.EntryPath).ShouldBe(restored);

        await world.Cache.ApproveAsync(first);
        world.Validator.Invocations.ShouldBe(2);
    }

    [Test]
    [Arguments("exit-1")]
    [Arguments("throw")]
    [Arguments("timeout")]
    [Arguments("canceled")]
    public async Task Failed_thrown_timed_out_and_canceled_validation_grant_no_approval(string shape)
    {
        using var world = new CacheWorld();
        var inputs = world.Inputs("a");
        switch (shape)
        {
            case "exit-1":
                world.Validator.ExitCode = 1;
                await Should.ThrowAsync<PreflightRefusalException>(() => world.Cache.ApproveAsync(inputs));
                break;
            case "throw":
                world.Validator.Throw = new InvalidOperationException("validator exploded");
                await Should.ThrowAsync<InvalidOperationException>(() => world.Cache.ApproveAsync(inputs));
                break;
            case "timeout":
                world.Validator.BlockForever = true;
                using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                    await Should.ThrowAsync<OperationCanceledException>(() => world.Cache.ApproveAsync(inputs, cancellationToken: cts.Token));
                break;
            default:
                world.Validator.BlockForever = true;
                using (var cts = new CancellationTokenSource())
                {
                    var pending = world.Cache.ApproveAsync(inputs, cancellationToken: cts.Token);
                    while (Volatile.Read(ref world.Validator.Invocations) == 0)
                        await Task.Delay(10);
                    cts.Cancel();
                    await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
                }
                break;
        }

        switch (shape)
        {
            case "exit-1":
                await Should.ThrowAsync<PreflightRefusalException>(() => world.Cache.ApproveAsync(inputs));
                break;
            case "throw":
                await Should.ThrowAsync<InvalidOperationException>(() => world.Cache.ApproveAsync(inputs));
                break;
            case "timeout":
                using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)))
                    await Should.ThrowAsync<OperationCanceledException>(() => world.Cache.ApproveAsync(inputs, cancellationToken: cts.Token));
                break;
            default:
                using (var cts = new CancellationTokenSource())
                {
                    var pending = world.Cache.ApproveAsync(inputs, cancellationToken: cts.Token);
                    while (Volatile.Read(ref world.Validator.Invocations) == 1)
                        await Task.Delay(10);
                    cts.Cancel();
                    await Should.ThrowAsync<OperationCanceledException>(async () => await pending);
                }
                break;
        }

        world.Validator.Invocations.ShouldBe(2);
        world.Validator.ExitCode = 0;
        world.Validator.Throw = null;
        world.Validator.BlockForever = false;
        await world.Cache.ApproveAsync(inputs);
        world.Validator.Invocations.ShouldBe(3);
        (await world.Cache.ApproveAsync(inputs)).ShouldBe(PreflightDisposition.Hit);
        world.Validator.Invocations.ShouldBe(3);
    }

    [Test]
    public async Task Concurrent_same_key_callers_share_one_validation_and_run_separately()
    {
        using var world = new CacheWorld();
        var inputs = world.Inputs("a");
        world.Validator.CountOnlyPath = inputs.EntryPath;
        var other = world.CopyTo("other");
        File.WriteAllText(other.WrapperPath, File.ReadAllText(other.WrapperPath) + "x");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Validator.Gate = gate.Task;
        var executions = 0;
        var nestedMs = 0L;
        var callers = Enumerable.Range(0, 4).Select(_ => world.Cache.ApproveAsync(
            inputs,
            execute: async () =>
            {
                Interlocked.Increment(ref executions);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await world.Cache.ApproveAsync(other).WaitAsync(TimeSpan.FromSeconds(5));
                Interlocked.Exchange(ref nestedMs, sw.ElapsedMilliseconds);
            })).ToArray();
        while (Volatile.Read(ref world.Validator.Invocations) == 0)
            await Task.Delay(10);
        gate.TrySetResult();
        await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(30));
        world.Validator.Invocations.ShouldBe(1);
        executions.ShouldBe(4);
        nestedMs.ShouldBeLessThan(5000);
    }

    [Test]
    public async Task Changed_input_between_validation_and_publication_is_refused()
    {
        using var world = new CacheWorld();
        var inputs = world.Inputs("a");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        world.Validator.Gate = gate.Task;
        var executions = 0;
        var pending = world.Cache.ApproveAsync(inputs, execute: () =>
        {
            Interlocked.Increment(ref executions);
            return Task.CompletedTask;
        });
        while (Volatile.Read(ref world.Validator.Invocations) == 0)
            await Task.Delay(10);
        File.WriteAllText(inputs.EntryPath, File.ReadAllText(inputs.EntryPath) + "changed");
        gate.TrySetResult();
        await Should.ThrowAsync<PreflightRefusalException>(async () => await pending);
        executions.ShouldBe(0);
        var original = world.CopyTo("restored");
        await world.Cache.ApproveAsync(new PreflightInputs(
            inputs.RequestedShell,
            original.EntryPath,
            original.HelperPath,
            original.PlatformPath,
            original.WrapperPath));
        world.Validator.Invocations.ShouldBe(2);
    }

    [Test]
    [Arguments("readable-first")]
    [Arguments("zero-byte-first")]
    [Arguments("reparse-first")]
    [Arguments("none-readable")]
    [Arguments("other-shell-only")]
    public void Resolver_picks_the_first_readable_regular_candidate_and_never_substitutes(string shape)
    {
        var root = Path.Combine(Path.GetTempPath(), "c476-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("PATH");
        try
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            var readable = Path.Combine(second, "pwsh.exe");
            if (shape is not ("none-readable" or "other-shell-only"))
            {
                WriteDummyExe(readable);
                File.WriteAllBytes(Path.Combine(second, "System.Management.Automation.dll"), "sma"u8.ToArray());
            }
            switch (shape)
            {
                case "readable-first":
                    WriteDummyExe(Path.Combine(first, "pwsh.exe"));
                    File.WriteAllBytes(Path.Combine(first, "System.Management.Automation.dll"), "sma-first"u8.ToArray());
                    break;
                case "zero-byte-first":
                    File.WriteAllBytes(Path.Combine(first, "pwsh.exe"), []);
                    break;
                case "reparse-first":
                    Directory.CreateDirectory(Path.Combine(first, "pwsh.exe"));
                    try
                    {
                        var link = Path.Combine(root, "link-pwsh.exe");
                        var target = Path.Combine(root, "target-pwsh.exe");
                        WriteDummyExe(target);
                        File.CreateSymbolicLink(link, target);
                    }
                    catch (Exception)
                    {
                        // Directory candidate above is enough to skip a non-regular first hit.
                    }
                    break;
                case "other-shell-only":
                    WriteDummyExe(Path.Combine(first, "powershell.exe"));
                    File.WriteAllBytes(Path.Combine(first, "System.Management.Automation.dll"), "sma"u8.ToArray());
                    break;
            }

            Environment.SetEnvironmentVariable("PATH", first + Path.PathSeparator + second);
            if (shape is "none-readable" or "other-shell-only")
            {
                var ex = Should.Throw<ShellResolutionException>(() => new ShellIdentityResolver().Resolve("pwsh.exe"));
                ex.RequestedShell.ShouldBe("pwsh.exe");
                ex.Message.ShouldContain("pwsh.exe");
                if (shape == "other-shell-only")
                    ex.Message.ShouldNotContain("powershell.exe", Case.Insensitive);
                return;
            }

            var identity = new ShellIdentityResolver().Resolve("pwsh.exe");
            var expected = shape == "readable-first" ? Path.GetFullPath(Path.Combine(first, "pwsh.exe")) : Path.GetFullPath(readable);
            identity.NormalizedPath.ShouldBe(expected);
            identity.ExeSha256.ShouldBe(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(expected))));
            identity.NormalizedPath.ShouldNotContain("powershell.exe");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", previous);
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Test]
    public async Task Shell_identity_drift_before_launch_refuses_execution()
    {
        using var world = new CacheWorld();
        var inputs = world.Inputs("a");
        var n = 0;
        world.ResolverHook = _ =>
        {
            n++;
            return n == 1
                ? world.Identity
                : world.Identity with { ExeSha256 = "DRIFT" };
        };
        var executions = 0;
        await Should.ThrowAsync<PreflightRefusalException>(() => world.Cache.ApproveAsync(
            inputs,
            execute: () =>
            {
                Interlocked.Increment(ref executions);
                return Task.CompletedTask;
            }));
        executions.ShouldBe(0);
        world.Cache.SuccessfulPreflights.ShouldBe(0);
    }

    private static void WriteDummyExe(string path) => File.WriteAllBytes(path, "dummy-exe"u8.ToArray());

    private sealed class FakeValidator
    {
        public int Invocations;
        public int ExitCode;
        public Exception? Throw;
        public Task? Gate;
        public bool BlockForever;
        public string? CountOnlyPath;

        public async Task<int> Validate(PreflightValidationCall call, CancellationToken ct)
        {
            if (CountOnlyPath is null || call.Inputs.EntryPath == CountOnlyPath)
                Interlocked.Increment(ref Invocations);
            if (Throw is not null)
                throw Throw;
            if (BlockForever)
                await Task.Delay(Timeout.Infinite, ct);
            if (Gate is not null)
                await Gate.WaitAsync(ct);
            return ExitCode;
        }
    }

    private sealed class IdentityBox
    {
        public ShellIdentity Value { get; set; } =
            new("pwsh.exe", @"C:\pwsh.exe", "AA", "7.6.6", "EE");
    }

    private sealed class CacheWorld : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "c476-cache-" + Guid.NewGuid().ToString("N"));
        public FakeValidator Validator { get; } = new();
        public IdentityBox Box { get; } = new();
        public Func<string, ShellIdentity>? ResolverHook;
        public RestartPreflightCache Cache { get; }

        public ShellIdentity Identity
        {
            get => Box.Value;
            set => Box.Value = value;
        }

        public CacheWorld()
        {
            Directory.CreateDirectory(Root);
            Cache = new RestartPreflightCache(
                validator: (call, ct) => Validator.Validate(call, ct),
                resolver: name => ResolverHook is not null ? ResolverHook(name) : Box.Value,
                validatorIdentity: "v1");
        }

        public PreflightInputs Inputs(string name)
        {
            var dir = Path.Combine(Root, name, "scripts");
            Directory.CreateDirectory(dir);
            var entry = Path.Combine(dir, "entry.ps1");
            var helper = Path.Combine(dir, "helper.ps1");
            var platform = Path.Combine(dir, "platform.ps1");
            var wrapper = Path.Combine(dir, "wrapper.ps1");
            File.WriteAllText(entry, "entry-body");
            File.WriteAllText(helper, "helper-body");
            File.WriteAllText(platform, "platform-body");
            File.WriteAllText(wrapper, "wrapper-body");
            return new PreflightInputs("pwsh.exe", entry, helper, platform, wrapper);
        }

        public PreflightInputs CopyTo(string name)
        {
            var source = Directory.GetDirectories(Root).Select(d => Path.Combine(d, "scripts")).First(Directory.Exists);
            var dir = Path.Combine(Root, name, "scripts");
            Directory.CreateDirectory(dir);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
            return new PreflightInputs(
                "pwsh.exe",
                Path.Combine(dir, "entry.ps1"),
                Path.Combine(dir, "helper.ps1"),
                Path.Combine(dir, "platform.ps1"),
                Path.Combine(dir, "wrapper.ps1"));
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, true); } catch { }
        }
    }
}
