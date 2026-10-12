using System.CodeDom.Compiler;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public sealed class ConPtyEnvironmentIsolationGuardTests
{
    [Test]
    public void ConPty_environment_mutators_are_unkeyed_not_in_parallel()
    {
        var mutators = FindMutators(typeof(ConPtyEnvironmentIsolationGuardTests).Assembly.GetTypes()
            .Where(type => !IsFixture(type)));

        // A census floor prevents a broken IL scanner from making the guard vacuously green.
        mutators.ShouldContain(method => method.DeclaringType == typeof(PtyBackendContractTests)
            && method.Name == nameof(PtyBackendContractTests.A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete),
            "conpty-env-mutator-census: fallback test");
        mutators.ShouldContain(method => method.DeclaringType == typeof(WindowsPtyArgvNativeTests)
            && method.Name == nameof(WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv),
            "conpty-env-mutator-census: native argv test");
        Violations(mutators).ShouldBeEmpty("conpty-env-mutators-are-unkeyed-not-in-parallel");
    }

    [Test]
    public void Guard_detects_lambdas_closures_and_parameter_helpers_by_the_owning_method()
    {
        var mutators = FindMutators([typeof(MutationFixtures), typeof(SerializedFixture)]);
        Violations(mutators).ShouldBe(new[]
        {
            "MutationFixtures.BadClosure",
            "MutationFixtures.BadKeyedHelper",
            "MutationFixtures.BadLambda",
            "MutationFixtures.BadParameterHelper",
            "MutationFixtures.BadRuntimeParameter",
        });
        mutators.ShouldContain(method => method.Name == nameof(MutationFixtures.SafeHelper));
        mutators.ShouldContain(method => method.DeclaringType == typeof(SerializedFixture));
    }

    [Test]
    public void Guard_classifies_async_method_state_machines_by_the_owning_method()
    {
        var mutators = FindMutators([typeof(StateMachineFixtures.AsyncMethodFixture),
            typeof(StateMachineFixtures.SerializedAsyncMethodFixture)]);
        mutators.Select(method => $"{method.DeclaringType!.Name}.{method.Name}").Order().ToArray().ShouldBe(new[]
        {
            "AsyncMethodFixture.Bad",
            "AsyncMethodFixture.Keyed",
            "AsyncMethodFixture.SafeMethod",
            "SerializedAsyncMethodFixture.Safe",
        });
        Violations(mutators).ShouldBe(new[]
        {
            "AsyncMethodFixture.Bad",
            "AsyncMethodFixture.Keyed",
        });
    }

    [Test]
    public void Guard_classifies_async_iterator_state_machines_by_the_owning_method()
    {
        var mutators = FindMutators([typeof(StateMachineFixtures.AsyncIteratorFixture),
            typeof(StateMachineFixtures.SerializedAsyncIteratorFixture)]);
        mutators.Select(method => $"{method.DeclaringType!.Name}.{method.Name}").Order().ToArray().ShouldBe(new[]
        {
            "AsyncIteratorFixture.Bad",
            "AsyncIteratorFixture.Keyed",
            "AsyncIteratorFixture.SafeMethod",
            "SerializedAsyncIteratorFixture.Safe",
        });
        Violations(mutators).ShouldBe(new[]
        {
            "AsyncIteratorFixture.Bad",
            "AsyncIteratorFixture.Keyed",
        });
    }

    [Test]
    public void Guard_classifies_iterator_state_machines_by_the_owning_method()
    {
        var mutators = FindMutators([typeof(StateMachineFixtures.IteratorFixture),
            typeof(StateMachineFixtures.SerializedIteratorFixture)]);
        mutators.Select(method => $"{method.DeclaringType!.Name}.{method.Name}").Order().ToArray().ShouldBe(new[]
        {
            "IteratorFixture.Bad",
            "IteratorFixture.Keyed",
            "IteratorFixture.SafeMethod",
            "SerializedIteratorFixture.Safe",
        });
        Violations(mutators).ShouldBe(new[]
        {
            "IteratorFixture.Bad",
            "IteratorFixture.Keyed",
        });
    }

    [Test]
    public void Guard_refuses_state_machines_without_MoveNext()
    {
        var error = Should.Throw<InvalidOperationException>(() =>
            FindMutators([typeof(StateMachineFixtures.UnknownFixture)]));
        error.Message.ShouldBe("State machine for UnknownFixture.Unknown has no MoveNext body");
    }

    private static bool IsFixture(Type type) => type == typeof(MutationFixtures)
        || type == typeof(SerializedFixture)
        || type == typeof(StateMachineFixtures)
        || type.DeclaringType is { } parent && IsFixture(parent);

    private static string[] Violations(IEnumerable<MethodBase> mutators) => mutators
        .Where(method => !HasUnkeyedNotInParallel(method)
            && !HasUnkeyedNotInParallel(method.DeclaringType!))
        .Select(method => $"{method.DeclaringType!.Name}.{method.Name}").Order().ToArray();

    private static List<MethodBase> FindMutators(IEnumerable<Type> types)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
        var mutators = new List<MethodBase>();
        foreach (var type in types)
        {
            // Generated bodies are reached through call/delegate/state-machine edges below.
            // Scheduling always belongs to the outer user method, never a lambda or MoveNext.
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) continue;
            // TUnit's generated launchers invoke tests under the original test's metadata;
            // their __Invoke/.cctor methods are not separate scheduling owners.
            if (type.GetCustomAttribute<GeneratedCodeAttribute>()?.Tool == "TUnit") continue;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                if (MutatesConPtyDirectory(method, opCodes)) mutators.Add(method);
            }
        }

        return mutators;
    }

    private static bool HasUnkeyedNotInParallel(MemberInfo member) =>
        member.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(NotInParallelAttribute)
            && attribute.ConstructorArguments.All(argument =>
                argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> { Count: 0 }));

    private static bool MutatesConPtyDirectory(MethodBase method, IReadOnlyDictionary<short, OpCode> opCodes)
    {
        var loadsDirectoryVariable = false;
        var callsEnvironmentSetter = false;
        var parameterisedSetter = false;
        var pending = new Stack<MethodBase>();
        var visited = new HashSet<MethodBase>();
        pending.Push(method);
        while (pending.TryPop(out var body))
        {
            if (!visited.Add(body)) continue;
            // The common attribute also covers AsyncIteratorStateMachineAttribute.
            // A recognized but unreadable shape must never make the census green.
            var stateMachine = body.GetCustomAttribute<StateMachineAttribute>()?.StateMachineType;
            if (stateMachine is not null)
            {
                var moveNext = stateMachine.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance);
                if (moveNext?.GetMethodBody() is null)
                    throw new InvalidOperationException($"State machine for {body.DeclaringType!.Name}.{body.Name} has no MoveNext body");
                pending.Push(moveNext);
            }
            var il = body.GetMethodBody()?.GetILAsByteArray();
            if (il is null) continue;
            for (var offset = 0; offset < il.Length;)
            {
                short value = il[offset++];
                if (value == 0xfe) value = (short)(0xfe00 | il[offset++]);
                var opCode = opCodes[value];
                if (opCode == OpCodes.Ldstr)
                    loadsDirectoryVariable |= body.Module.ResolveString(BitConverter.ToInt32(il, offset))
                        == ConPtyRedistributable.DirectoryEnvVar;
                if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj
                    || opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn)
                {
                    var called = body.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                        body.DeclaringType?.GetGenericArguments(),
                        body is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null);
                    if (called?.DeclaringType == typeof(Environment)
                        && called.Name == nameof(Environment.SetEnvironmentVariable))
                    {
                        callsEnvironmentSetter = true;
                        // A parameterised setter can receive the directory name at runtime.
                        // Fail conservatively for EVERY caller, even without a literal in it;
                        // a scheduling attribute on the helper cannot serialize its callers.
                        parameterisedSetter |= body.GetParameters().Any(p => p.ParameterType == typeof(string));
                    }
                    if (called?.Module.Assembly == method.Module.Assembly) pending.Push(called);
                }
                offset += opCode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                    OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI
                        or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
                        or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                    _ => throw new InvalidOperationException($"Unsupported IL operand: {opCode.OperandType}")
                };
            }
        }
        return parameterisedSetter || loadsDirectoryVariable && callsEnvironmentSetter;
    }

    // Compiled IL fixtures, never executed and deliberately without [Test]. Only the
    // guard-the-guard scans these; production census excludes this exact fixture tree.
    private static class MutationFixtures
    {
        public static void BadLambda()
        {
            Action mutate = () => Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
            mutate();
        }

        public static void BadClosure(string? value)
        {
            Action mutate = () => Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, value);
            mutate();
        }

        public static void BadParameterHelper() => SetVariable(ConPtyRedistributable.DirectoryEnvVar, null);

        public static void BadRuntimeParameter(string name) => SetVariable(name, null);

        [NotInParallel("keyed-is-insufficient")]
        public static void BadKeyedHelper() => SetVariable(ConPtyRedistributable.DirectoryEnvVar, null);

        [NotInParallel]
        public static void SafeHelper() => SetVariable(ConPtyRedistributable.DirectoryEnvVar, null);

        [NotInParallel]
        internal static void SetVariable(string name, string? value) => Environment.SetEnvironmentVariable(name, value);
    }

    [NotInParallel]
    private static class SerializedFixture
    {
        public static void SafeLambda()
        {
            Action mutate = () => Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
            mutate();
        }
    }

    // Like the lambda/helper fixtures above, these are IL inputs only. The literal
    // and setter live after suspension in generated MoveNext bodies, not the roots.
    private static class StateMachineFixtures
    {
        internal static class AsyncMethodFixture
        {
            public static async Task Bad()
            {
                await Task.Yield();
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
            }

            [NotInParallel("keyed-is-insufficient")]
            public static async Task Keyed()
            {
                await Task.Yield();
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
            }

            [NotInParallel]
            public static async Task SafeMethod()
            {
                await Task.Yield();
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
            }
        }

        [NotInParallel]
        internal static class SerializedAsyncMethodFixture
        {
            public static async Task Safe()
            {
                await Task.Yield();
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
            }
        }

        internal static class AsyncIteratorFixture
        {
            public static async IAsyncEnumerable<int> Bad()
            {
                await Task.Yield();
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }

            [NotInParallel("keyed-is-insufficient")]
            public static async IAsyncEnumerable<int> Keyed()
            {
                await Task.Yield();
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }

            [NotInParallel]
            public static async IAsyncEnumerable<int> SafeMethod()
            {
                await Task.Yield();
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }
        }

        [NotInParallel]
        internal static class SerializedAsyncIteratorFixture
        {
            public static async IAsyncEnumerable<int> Safe()
            {
                await Task.Yield();
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }
        }

        internal static class IteratorFixture
        {
            public static IEnumerable<int> Bad()
            {
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }

            [NotInParallel("keyed-is-insufficient")]
            public static IEnumerable<int> Keyed()
            {
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }

            [NotInParallel]
            public static IEnumerable<int> SafeMethod()
            {
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }
        }

        [NotInParallel]
        internal static class SerializedIteratorFixture
        {
            public static IEnumerable<int> Safe()
            {
                yield return 0;
                Environment.SetEnvironmentVariable(ConPtyRedistributable.DirectoryEnvVar, null);
                yield return 1;
            }
        }

        internal static class UnknownFixture
        {
            [AsyncStateMachine(typeof(MissingMoveNext))]
            public static void Unknown() { }
        }

        internal sealed class MissingMoveNext { }
    }
}
