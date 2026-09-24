using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// Decides whether this process's selected tests reach the shared PostgreSQL store.
/// A db-free selection, a CARD-0476 probe that is not mode <c>mixed</c>, and worker
/// children stay cold. Everyone else awaits readiness once before tests run (CARD-0646).
/// </summary>
internal static class SharedStoreWarmup
{
    private const int MaxDepth = 3;
    private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodes();

    public static bool ProbeDefersWarmup(string? probePayload)
    {
        if (string.IsNullOrEmpty(probePayload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(probePayload);
            var mode = doc.RootElement.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null;
            return !string.Equals(mode, "mixed", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public static bool SelectionNeedsSharedStore(IEnumerable<TestContext> tests)
    {
        var pending = new Queue<(MethodBase Method, int Depth)>();
        var seen = new HashSet<MethodBase>();
        foreach (var test in tests)
        {
            var method = ResolveTestMethod(test);
            if (method is not null)
                Enqueue(pending, seen, method, 0);
            var classType = test.Metadata.TestDetails.ClassType;
            foreach (var hook in HookMethods(classType))
                Enqueue(pending, seen, hook, 0);
        }

        while (pending.Count > 0)
        {
            var (method, depth) = pending.Dequeue();
            if (BodyReachesDefaultStore(method, depth, pending, seen))
                return true;
        }

        return false;
    }

    public static bool ReachesDefaultStore(MethodInfo method)
    {
        var pending = new Queue<(MethodBase Method, int Depth)>();
        var seen = new HashSet<MethodBase>();
        Enqueue(pending, seen, method, 0);
        while (pending.Count > 0)
        {
            var (next, depth) = pending.Dequeue();
            if (BodyReachesDefaultStore(next, depth, pending, seen))
                return true;
        }

        return false;
    }

    private static bool BodyReachesDefaultStore(
        MethodBase method,
        int depth,
        Queue<(MethodBase Method, int Depth)> pending,
        HashSet<MethodBase> seen)
    {
        if (IsDefaultStoreMember(method))
            return true;

        foreach (var body in WithGenerated(method))
        {
            if (!ReferenceEquals(body, method))
                seen.Add(body);
            foreach (var called in Calls(body))
            {
                if (IsDefaultStoreMember(called))
                    return true;
                if (depth < MaxDepth && InTestAssembly(called))
                    Enqueue(pending, seen, called, depth + 1);
            }
        }

        return false;
    }

    private static void Enqueue(
        Queue<(MethodBase Method, int Depth)> pending,
        HashSet<MethodBase> seen,
        MethodBase method,
        int depth)
    {
        if (seen.Add(method))
            pending.Enqueue((method, depth));
    }

    private static bool InTestAssembly(MethodBase method) =>
        method.DeclaringType?.Assembly == typeof(SharedStoreWarmup).Assembly;

    private static bool IsDefaultStoreMember(MethodBase method)
    {
        var type = method.DeclaringType;
        if (type == typeof(TestDbFixture))
        {
            return method.Name is "get_ConnectionString"
                or "get_MaintenanceConnectionString"
                or "CreateDbContextOptions"
                or "CreateIsolatedSchemaAsync"
                or "CreateDbContext"
                or "DropClonedDatabaseAsync";
        }

        if (type == typeof(TestDbFixtureLifecycle))
        {
            return method.Name is "get_ConnectionString"
                or "get_MaintenanceConnectionString"
                or "EnsureReadyAsync"
                or "CreateDbContextOptions"
                or "CreateDbContext"
                or "CreateIsolatedSchemaAsync";
        }

        return false;
    }

    private static IEnumerable<MethodBase> WithGenerated(MethodBase method)
    {
        yield return method;
        var declaring = method.DeclaringType;
        if (declaring is null)
            yield break;

        var prefix = "<" + method.Name + ">";
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var sibling in declaring.GetMethods(flags))
        {
            if (sibling.Name.StartsWith(prefix, StringComparison.Ordinal))
                yield return sibling;
        }

        foreach (var nested in declaring.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!nested.Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            foreach (var nestedMethod in nested.GetMethods(flags))
                yield return nestedMethod;
        }
    }

    private static MethodInfo? ResolveTestMethod(TestContext test)
    {
        var details = test.Metadata.TestDetails;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = details.ClassType; type is not null && type != typeof(object); type = type.BaseType)
        {
            var matches = type.GetMethods(flags).Where(m => m.Name == details.MethodName).ToArray();
            if (matches.Length == 1)
                return matches[0];
            var count = details.MethodMetadata.Parameters.Length;
            var byCount = matches.Where(m => m.GetParameters().Length == count).ToArray();
            if (byCount.Length > 0)
                return byCount[0];
        }

        return null;
    }

    private static IEnumerable<MethodInfo> HookMethods(Type classType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = classType; type is not null && type != typeof(object); type = type.BaseType)
        {
            foreach (var method in type.GetMethods(flags))
            {
                var attributes = method.GetCustomAttributes(inherit: false);
                if (attributes.Any(attribute => attribute is BeforeAttribute or AfterAttribute))
                    yield return method;
            }
        }
    }

    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (il is null || il.Length == 0)
            yield break;

        var module = method.Module;
        var typeArgs = method.DeclaringType is { IsGenericType: true } declaring
            ? declaring.GetGenericArguments()
            : Type.EmptyTypes;
        var methodArgs = method is MethodInfo { IsGenericMethod: true } generic
            ? generic.GetGenericArguments()
            : Type.EmptyTypes;
        var index = 0;
        while (index < il.Length)
        {
            var raw = il[index++];
            short key = raw;
            if (raw == 0xFE)
            {
                if (index >= il.Length)
                    yield break;
                key = (short)((raw << 8) | il[index++]);
            }

            if (!OpCodesByValue.TryGetValue(key, out var opCode))
                yield break;

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                if (index + 4 > il.Length)
                    yield break;
                var token = BitConverter.ToInt32(il, index);
                index += 4;
                MethodBase? called = null;
                try
                {
                    called = module.ResolveMethod(token, typeArgs, methodArgs);
                }
                catch (ArgumentException)
                {
                }
                catch (BadImageFormatException)
                {
                }

                if (called is not null)
                    yield return called;
                continue;
            }

            if (!SkipOperand(il, ref index, opCode.OperandType))
                yield break;
        }
    }

    private static bool SkipOperand(byte[] il, ref int index, OperandType operandType)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return true;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                index += 1;
                break;
            case OperandType.InlineVar:
                index += 2;
                break;
            case OperandType.InlineBrTarget:
            case OperandType.InlineField:
            case OperandType.InlineI:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.InlineTok:
            case OperandType.InlineType:
            case OperandType.ShortInlineR:
                index += 4;
                break;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                index += 8;
                break;
            case OperandType.InlineSwitch:
                if (index + 4 > il.Length)
                    return false;
                var count = BitConverter.ToInt32(il, index);
                index += 4;
                if (count < 0)
                    return false;
                index += 4 * count;
                break;
            default:
                return false;
        }

        return index <= il.Length;
    }

    private static Dictionary<short, OpCode> BuildOpCodes()
    {
        var map = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode opCode)
                map.TryAdd(opCode.Value, opCode);
        }

        return map;
    }
}
