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
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
        var mutators = new List<MethodBase>();
        foreach (var type in typeof(ConPtyEnvironmentIsolationGuardTests).Assembly.GetTypes())
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

        // A census floor prevents a broken IL scanner from making the guard vacuously green.
        mutators.ShouldContain(method => method.DeclaringType == typeof(PtyBackendContractTests)
            && method.Name == nameof(PtyBackendContractTests.A_modern_request_falls_back_to_the_inbox_conhost_when_the_pair_is_incomplete),
            "conpty-env-mutator-census: fallback test");
        mutators.ShouldContain(method => method.DeclaringType == typeof(WindowsPtyArgvNativeTests)
            && method.Name == nameof(WindowsPtyArgvNativeTests.Windows_backends_keep_exact_native_argv),
            "conpty-env-mutator-census: native argv test");
        var violations = mutators.Where(method => !HasUnkeyedNotInParallel(method)
                && !HasUnkeyedNotInParallel(method.DeclaringType!))
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}").Order().ToArray();
        violations.ShouldBeEmpty("conpty-env-mutators-are-unkeyed-not-in-parallel: "
            + string.Join(", ", violations));
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
}
