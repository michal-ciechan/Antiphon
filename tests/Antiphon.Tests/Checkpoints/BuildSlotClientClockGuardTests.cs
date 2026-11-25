using Antiphon.Checkpoints;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

/// <summary>
/// CARD-2072. <see cref="BuildSlotClient"/> arms its admission deadline as
/// <c>new CancellationTokenSource(wait, timeProvider)</c>. A test that injects a fake
/// <c>clock</c> or <c>delay</c> and a finite <c>wait</c>, but no <c>timeProvider</c>, runs that
/// deadline on real time while its busy loop runs on fake time. A loaded host then fires the
/// deadline first. That shape failed C1616_admission_timeout_publishes_readable_nonexecuted_receipt
/// in Windows rc run b8cbfb24 group 112 (docs/investigations/2026-11-25-why-missed-group-112-admission-reason.md).
/// </summary>
[Category("Unit")]
public sealed class BuildSlotClientClockGuardTests
{
    [Test]
    public void no_test_arms_a_real_time_slot_deadline_under_a_fake_clock()
    {
        var root = RepoRoot();
        var constructions = new List<BuildSlotClockGuard.Construction>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (BuildSlotClockGuard.IsGenerated(relative))
                continue;
            var text = File.ReadAllText(path);
            if (text.Contains(nameof(BuildSlotClient), StringComparison.Ordinal))
                constructions.AddRange(BuildSlotClockGuard.Scan(text, relative));
        }

        // Vacuity: the scan must see the real constructions and map their injected time arguments.
        constructions.Count.ShouldBeGreaterThanOrEqualTo(10, "the guard found too few BuildSlotClient constructions");
        constructions.Count(item => item.Supplied.Contains("clock") || item.Supplied.Contains("delay"))
            .ShouldBeGreaterThanOrEqualTo(3, "the guard mapped too few injected clock/delay arguments");
        constructions.ShouldContain(item => item.Supplied.Contains("timeProvider"),
            "the guard mapped no timeProvider argument");

        var violations = constructions.Where(item => item.Violates).Select(item => item.Location).ToArray();
        violations.ShouldBeEmpty(
            "a BuildSlotClient with an injected clock/delay and a finite wait must also inject the timeProvider "
            + "that the fake clock advances; otherwise its admission deadline runs on real time (CARD-2072): "
            + string.Join(", ", violations));
    }

    [Test]
    public void guard_flags_the_pre_fix_c1616_construction()
    {
        // Verbatim from 23cdd65037ce43ac81aaf1cc0227b6e17087de2b (CheckpointAdmissionContractTests.cs:425).
        var found = BuildSlotClockGuard.Scan(Method("""
            var now = DateTimeOffset.UtcNow;
            var queued = new BuildSlotClient(new SlowFirstAcquire(broker.Recording(), TimeSpan.FromMilliseconds(1500)),
                endpoint, pid: 101, processStartUtc: start,
                wait: TimeSpan.FromSeconds(1), clock: () => now,
                delay: (_, _) => { now += TimeSpan.FromSeconds(1); return Task.CompletedTask; });
            """), "pre-fix.cs");
        found.Single().Violates.ShouldBeTrue("the C1616 shape arms a real-time deadline under a fake clock");
        found.Single().Location.ShouldBe("pre-fix.cs:6");
    }

    [Test]
    public void guard_accepts_the_fixed_c1616_construction()
    {
        // Verbatim from 3d3876410e256e59692733272da0f7dad18757c8: the deadline shares the controlled clock.
        var found = BuildSlotClockGuard.Scan(Method("""
            var clock = new ControlledTimeProvider(DateTimeOffset.UtcNow);
            var queued = new BuildSlotClient(new SlowFirstAcquire(broker.Recording(), TimeSpan.FromMilliseconds(1500)),
                endpoint, pid: 101, processStartUtc: start,
                wait: TimeSpan.FromSeconds(1), timeProvider: clock,
                delay: (_, _) => { clock.Advance(TimeSpan.FromSeconds(1)); return Task.CompletedTask; });
            """), "fixed.cs");
        found.Single().Violates.ShouldBeFalse();
    }

    [Test]
    [Arguments("positional", """var c = new BuildSlotClient(handler, "u", grace, wait, () => now, Delay, pid: 1);""", true)]
    [Arguments("positional-delay-only", """var c = new BuildSlotClient(handler, "u", TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(2), null, (_, _) => Task.CompletedTask);""", true)]
    [Arguments("qualified", """var c = new Antiphon.Checkpoints.BuildSlotClient(handler, "u", wait: w, delay: d);""", true)]
    [Arguments("default-wait", """var c = new BuildSlotClient(handler, "u", grace: g, clock: () => now, delay: d);""", false)]
    [Arguments("null-wait", """var c = new BuildSlotClient(handler, "u", wait: null, clock: () => now);""", false)]
    [Arguments("real-time", """var c = new BuildSlotClient(handler, "u", wait: TimeSpan.FromSeconds(1));""", false)]
    [Arguments("positional-provider", """var c = new BuildSlotClient(handler, "u", null, w, null, d, null, 1, "s", null, null, null, time);""", false)]
    [Arguments("other-type", """var c = new OtherClient(handler, "u", wait: w, clock: () => now);""", null)]
    public void guard_maps_construction_shapes(string name, string statement, bool? violates)
    {
        var found = BuildSlotClockGuard.Scan(Method(statement), name + ".cs");
        if (violates is null)
            found.ShouldBeEmpty(name);
        else
            found.Single().Violates.ShouldBe(violates.Value, name);
    }

    [Test]
    public void guard_resolves_target_typed_constructions()
    {
        var found = BuildSlotClockGuard.Scan("""
            class Fixture
            {
                private static BuildSlotClient Arrow(HttpMessageHandler h, TimeSpan w) =>
                    new(h, "u", wait: w, delay: (_, _) => Task.CompletedTask);
                private static BuildSlotClient Returned(HttpMessageHandler h, TimeSpan w)
                {
                    return new(h, "u", wait: w, clock: () => DateTimeOffset.UtcNow);
                }
                private static void Declared(HttpMessageHandler h, TimeSpan w, TimeProvider t)
                {
                    BuildSlotClient a = new(h, "u", wait: w, clock: () => DateTimeOffset.UtcNow, timeProvider: t);
                    Other b = new(h, "u", wait: w, clock: () => DateTimeOffset.UtcNow);
                }
            }
            """, "target.cs");
        found.Select(item => (item.Location, item.Violates)).ToArray()
            .ShouldBe(new[] { ("target.cs:4", true), ("target.cs:7", true), ("target.cs:11", false) });
    }

    private static string Method(string body) =>
        "class Fixture\n{\n    async Task Run()\n    {\n" + body + "\n    }\n}\n";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }
}

/// <summary>Syntactic scan for <see cref="BuildSlotClient"/> constructions (CARD-2072).</summary>
internal static class BuildSlotClockGuard
{
    internal sealed record Construction(string Location, IReadOnlySet<string> Supplied)
    {
        /// <summary>A finite wait with fake clock/delay must share the injected time provider.</summary>
        public bool Violates => Supplied.Contains("wait")
            && (Supplied.Contains("clock") || Supplied.Contains("delay"))
            && !Supplied.Contains("timeProvider");
    }

    private static readonly string[] Parameters = ConstructorParameters();

    public static bool IsGenerated(string relative) =>
        relative.Split('/').Any(part => part is "bin" or "obj" or "node_modules" or "TestOutput"
            || part.StartsWith("bin-", StringComparison.Ordinal));

    public static IReadOnlyList<Construction> Scan(string text, string path)
    {
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview), path);
        var found = new List<Construction>();
        foreach (var node in tree.GetRoot().DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var creates = node switch
            {
                ObjectCreationExpressionSyntax explicitNew => IsClient(explicitNew.Type),
                ImplicitObjectCreationExpressionSyntax implicitNew => IsClient(TargetType(implicitNew)),
                _ => false,
            };
            if (!creates)
                continue;
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            found.Add(new Construction($"{path}:{line}", Supplied(node.ArgumentList)));
        }
        return found;
    }

    private static HashSet<string> Supplied(ArgumentListSyntax? arguments)
    {
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        if (arguments is null)
            return supplied;
        for (var i = 0; i < arguments.Arguments.Count; i++)
        {
            var argument = arguments.Arguments[i];
            if (argument.Expression.IsKind(SyntaxKind.NullLiteralExpression)
                || argument.Expression.IsKind(SyntaxKind.DefaultLiteralExpression))
                continue;
            var name = argument.NameColon?.Name.Identifier.ValueText
                ?? (i < Parameters.Length ? Parameters[i] : throw new InvalidOperationException(
                    $"BuildSlotClient construction has more positional arguments than the constructor: {argument}"));
            supplied.Add(name);
        }
        return supplied;
    }

    private static TypeSyntax? TargetType(ImplicitObjectCreationExpressionSyntax node) => node.Parent switch
    {
        ArrowExpressionClauseSyntax arrow => arrow.Parent switch
        {
            MethodDeclarationSyntax method => method.ReturnType,
            LocalFunctionStatementSyntax local => local.ReturnType,
            PropertyDeclarationSyntax property => property.Type,
            _ => null,
        },
        ReturnStatementSyntax ret => ret.Ancestors().Select(ancestor => ancestor switch
        {
            MethodDeclarationSyntax method => method.ReturnType,
            LocalFunctionStatementSyntax local => local.ReturnType,
            _ => null,
        }).FirstOrDefault(type => type is not null),
        EqualsValueClauseSyntax equals => equals.Parent switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } => declaration.Type,
            PropertyDeclarationSyntax property => property.Type,
            ParameterSyntax parameter => parameter.Type,
            _ => null,
        },
        _ => null,
    };

    private static bool IsClient(TypeSyntax? type) => type switch
    {
        IdentifierNameSyntax name => name.Identifier.ValueText == nameof(BuildSlotClient),
        QualifiedNameSyntax qualified => IsClient(qualified.Right),
        AliasQualifiedNameSyntax alias => IsClient(alias.Name),
        NullableTypeSyntax nullable => IsClient(nullable.ElementType),
        _ => false,
    };

    private static string[] ConstructorParameters()
    {
        var names = typeof(BuildSlotClient).GetConstructors()
            .OrderByDescending(ctor => ctor.GetParameters().Length).First()
            .GetParameters().Select(parameter => parameter.Name!).ToArray();
        foreach (var required in new[] { "wait", "clock", "delay", "timeProvider" })
            if (!names.Contains(required, StringComparer.Ordinal))
                throw new InvalidOperationException($"BuildSlotClient no longer has a '{required}' parameter; update the CARD-2072 guard.");
        return names;
    }
}
