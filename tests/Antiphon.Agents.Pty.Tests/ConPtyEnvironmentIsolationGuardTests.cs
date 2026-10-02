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
        });
        mutators.ShouldContain(method => method.Name == nameof(MutationFixtures.SafeHelper));
        mutators.ShouldContain(method => method.DeclaringType == typeof(SerializedFixture));
    }

    private static bool IsFixture(Type type) => type == typeof(MutationFixtures)
        || type == typeof(SerializedFixture)
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
            // Async state machines are inspected through their original method below, where
            // TUnit reads the scheduling attributes, rather than through generated MoveNext.
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) continue;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
                    ?? method.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
                var body = stateMachine?.GetMethod("MoveNext", flags) ?? method;
                if (MutatesConPtyDirectory(body, opCodes)) mutators.Add(method);
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
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) return false;
        var loadsDirectoryVariable = false;
        var callsEnvironmentSetter = false;
        for (var offset = 0; offset < il.Length;)
        {
            short value = il[offset++];
            if (value == 0xfe) value = (short)(0xfe00 | il[offset++]);
            var opCode = opCodes[value];
            if (opCode == OpCodes.Ldstr)
                loadsDirectoryVariable |= method.Module.ResolveString(BitConverter.ToInt32(il, offset))
                    == ConPtyRedistributable.DirectoryEnvVar;
            if (opCode == OpCodes.Call || opCode == OpCodes.Callvirt)
            {
                var called = method.Module.ResolveMethod(BitConverter.ToInt32(il, offset),
                    method.DeclaringType?.GetGenericArguments(),
                    method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null);
                callsEnvironmentSetter |= called?.DeclaringType == typeof(Environment)
                    && called.Name == nameof(Environment.SetEnvironmentVariable);
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
        return loadsDirectoryVariable && callsEnvironmentSetter;
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

        [NotInParallel("keyed-is-insufficient")]
        public static void BadKeyedHelper() => SetVariable(ConPtyRedistributable.DirectoryEnvVar, null);

        [NotInParallel]
        public static void SafeHelper() => SetVariable(ConPtyRedistributable.DirectoryEnvVar, null);

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
}
