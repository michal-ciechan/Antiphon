using System.Collections.Specialized;
using System.Reflection;
using LinuxSharedCompilerPolicy = Antiphon.Tests.Infrastructure.LinuxPolicyCalls;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class LinuxSharedCompilerPolicyTests
{
    [Test]
    public void Only_compiling_commands_receive_toolset_properties()
    {
        foreach (var verb in new[] { "build", "publish", "pack", "msbuild", "test", "run" })
            LinuxSharedCompilerPolicy.Compiles("/usr/bin/dotnet", [verb, "a.csproj"]).ShouldBeTrue(verb);
        foreach (var args in new[] { new[] { "--info" }, new[] { "run", "--no-build", "--", "build" },
                     new[] { "msbuild", "-getItem:ProjectReference" }, new[] { "msbuild", "-getProperty:MSBuildSDKsPath" } })
            LinuxSharedCompilerPolicy.Compiles("dotnet", args).ShouldBeFalse();
        LinuxSharedCompilerPolicy.Compiles("agent", ["build"]).ShouldBeFalse();
        LinuxSharedCompilerPolicy.Arguments(["run", "--", "a b", "$(literal)"], "/cache/tools", "private")
            .ShouldBe(["run", "-p:RoslynTargetsPath=/cache/tools", "-p:RoslynTasksAssembly=/cache/tools/Microsoft.Build.Tasks.CodeAnalysis.dll",
                "-p:SharedCompilationId=private", "--", "a b", "$(literal)"]);
    }

    [Test]
    public void Windows_and_optout_preserve_existing_policy()
    {
        LinuxSharedCompilerPolicy.Enabled(true, "1").ShouldBeFalse();
        LinuxSharedCompilerPolicy.Enabled(false, "0").ShouldBeFalse();
        LinuxSharedCompilerPolicy.Enabled(false, "1").ShouldBeTrue();
        var env = new StringDictionary();
        LinuxPolicyCalls.ApplyWindowsPolicy(env, true);
        env["UseSharedCompilation"].ShouldBe("false");
        env["DOTNET_gcServer"].ShouldBe("0");
        env["ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT"] = "1";
        env["UseSharedCompilation"] = "caller";
        LinuxPolicyCalls.ApplyWindowsPolicy(env, true);
        env["UseSharedCompilation"].ShouldBe("caller");
    }

    [Test]
    public void Sparse_affinity_is_intersected_with_grant()
    {
        LinuxSharedCompilerPolicy.SelectCpus("2,4-5,9,12-13", 3).ShouldBe("2,4,5");
        LinuxSharedCompilerPolicy.SelectCpus("4,9", 6).ShouldBe("4,9");
        Should.Throw<ArgumentException>(() => LinuxSharedCompilerPolicy.SelectCpus("0-1000000", 6));
        Should.Throw<ArgumentException>(() => LinuxSharedCompilerPolicy.SelectCpus("0-3", 0));
    }

    [Test]
    public void Cache_and_pipe_identity_are_stable_across_worktrees()
    {
        var key = LinuxSharedCompilerPolicy.Identity("sdk-content", "uid-container", "2,4");
        key.Length.ShouldBe(64);
        key.ShouldNotBe(LinuxSharedCompilerPolicy.Identity("sdk-next", "uid-container", "2,4"));
        key.ShouldNotBe(LinuxSharedCompilerPolicy.Identity("sdk-content", "other-container", "2,4"));
        key.ShouldNotBe(LinuxSharedCompilerPolicy.Identity("sdk-content", "uid-container", "2"));
        LinuxSharedCompilerPolicy.Arguments(["build", "/work/a.csproj"], "/cache/" + key, key)[1]
            .ShouldBe("-p:RoslynTargetsPath=/cache/" + key);
        LinuxSharedCompilerPolicy.Arguments(["build", "/other/b.csproj"], "/cache/" + key, key)[1]
            .ShouldBe("-p:RoslynTargetsPath=/cache/" + key);
    }

    [Test]
    public void Conflicting_toolset_is_refused()
    {
        foreach (var property in new[] { "-p:RoslynTargetsPath=custom", "--property:RoslynTasksAssembly=custom", "/p:SharedCompilationId=custom",
                     "-p:Other=x;UseSharedCompilation=false", "-p:CscToolPath=custom" })
            Should.Throw<ArgumentException>(() => LinuxSharedCompilerPolicy.Arguments(["build", property], "/cache", "pipe"));
    }

    [Test]
    public void Untrusted_or_incomplete_cache_is_refused()
    {
        LinuxSharedCompilerPolicy.CacheMatches("expected", "modified", true).ShouldBeFalse();
        LinuxSharedCompilerPolicy.CacheMatches("expected", null!, true).ShouldBeFalse();
        LinuxSharedCompilerPolicy.CacheMatches("expected", "expected", false).ShouldBeFalse();
        LinuxSharedCompilerPolicy.CacheMatches("expected", "expected", true).ShouldBeTrue();
    }

    [Test]
    public void Compiler_limits_do_not_leak_to_driver_environment()
    {
        var env = new StringDictionary { ["unrelated"] = "retained" };
        LinuxPolicyCalls.ApplyWindowsPolicy(env, false);
        env.Count.ShouldBe(1);
        var child = LinuxSharedCompilerPolicy.CompilerEnvironment(3);
        child["DOTNET_PROCESSOR_COUNT"].ShouldBe("3");
        child["DOTNET_GCHeapHardLimit"].ShouldBe("100000000");
        child.Count.ShouldBe(2);
    }

    [Test]
    public void Default_enablement_requires_qualified_policy()
    {
        foreach (var setting in new[] { null, "", "true", "0", "01" })
            LinuxSharedCompilerPolicy.Enabled(false, setting!).ShouldBeFalse();
        LinuxSharedCompilerPolicy.Enabled(false, "1").ShouldBeTrue();
    }
}

// The shared source is linked into both consumers. Bind this pure roster to the
// checkpoint consumer; CP-5 exercises the PowerShell compilation of the same file.
internal static class LinuxPolicyCalls
{
    static Assembly Consumer => typeof(Antiphon.Checkpoints.ProcessDriver).Assembly;
    static T Call<T>(string method, params object?[] args)
    {
        try { return (T)Consumer.GetType("Antiphon.Build.LinuxSharedCompilerPolicy")!.GetMethod(method)!.Invoke(null, args)!; }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    public static bool Enabled(bool windows, string setting) => Call<bool>(nameof(Enabled), windows, setting);
    public static bool Compiles(string exe, string[] args) => Call<bool>(nameof(Compiles), exe, args);
    public static string SelectCpus(string allowed, int grant) => Call<string>(nameof(SelectCpus), allowed, grant);
    public static string Identity(string sdk, string scope, string cpus) => Call<string>(nameof(Identity), sdk, scope, cpus);
    public static string[] Arguments(string[] args, string tools, string pipe) => Call<string[]>(nameof(Arguments), args, tools, pipe);
    public static bool CacheMatches(string expected, string actual, bool trusted) => Call<bool>(nameof(CacheMatches), expected, actual, trusted);
    public static Dictionary<string, string> CompilerEnvironment(int count) => Call<Dictionary<string, string>>(nameof(CompilerEnvironment), count);
    public static void ApplyWindowsPolicy(StringDictionary environment, bool windows)
    {
        dynamic policy = Activator.CreateInstance(Consumer.GetType("Antiphon.Build.BuildDriverEnvironment")!)!;
        policy.Apply(environment, windows);
    }
}
