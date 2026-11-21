using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S01: the nightly CLI command line (Antiphon.Nightly.NightlyArgs). "--kebab value" flags and bare
/// switches; comma lists; an unknown, repeated or valueless flag or a positional argument is a usage error.
/// </summary>
[Category("Unit")]
public sealed class NightlyArgsTests
{
    private static readonly string[] Valued = ["--log-root", "--suites", "--top", "--ref"];
    private static readonly string[] Switches = ["--what-if", "--no-report"];

    private static NightlyArgs Parse(params string[] arguments) => NightlyArgs.Parse(arguments, Valued, Switches);

    private static string Usage(params string[] arguments) => Should.Throw<NightlyUsageException>(() => Parse(arguments)).Message;

    [Test]
    public void Valued_flags_and_switches_are_read_in_any_order()
    {
        var args = Parse("--what-if", "--log-root", @"C:\logs\run 1", "--ref", "origin/master");

        args.Value("--log-root").ShouldBe(@"C:\logs\run 1");
        args.Value("--ref").ShouldBe("origin/master");
        args.Switch("--what-if").ShouldBeTrue();
        args.Switch("--no-report").ShouldBeFalse();
        args.Has("--log-root").ShouldBeTrue();
        args.Has("--what-if").ShouldBeTrue();
        args.Has("--suites").ShouldBeFalse();
    }

    [Test]
    public void Absent_flags_read_as_their_defaults()
    {
        var args = Parse();

        args.Value("--log-root").ShouldBeNull();
        args.Value("--log-root", "fallback").ShouldBe("fallback");
        args.List("--suites").ShouldBeEmpty();
        args.Int("--top", 50).ShouldBe(50);
        args.Switch("--what-if").ShouldBeFalse();
    }

    [Test]
    public void A_list_flag_splits_at_commas_trims_and_drops_empty_entries()
    {
        Parse("--suites", "antiphon, session-runner,,e2e ,").List("--suites").ShouldBe(new[] { "antiphon", "session-runner", "e2e" });
        Parse("--suites", "antiphon").List("--suites").ShouldBe(new[] { "antiphon" });
        Parse("--suites", "").List("--suites").ShouldBeEmpty();
    }

    [Test]
    public void An_empty_value_is_kept_as_given_and_is_not_a_required_value()
    {
        var args = Parse("--log-root", "");

        args.Has("--log-root").ShouldBeTrue();
        args.Value("--log-root", "fallback").ShouldBe("");
        Should.Throw<NightlyUsageException>(() => args.Required("--log-root")).Message.ShouldBe("missing --log-root");
        Should.Throw<NightlyUsageException>(() => Parse().Required("--ref")).Message.ShouldBe("missing --ref");
        Parse("--ref", "HEAD").Required("--ref").ShouldBe("HEAD");
    }

    [Test]
    public void An_integer_flag_parses_invariantly_and_refuses_text()
    {
        Parse("--top", "12").Int("--top", 50).ShouldBe(12);
        Parse("--top", "-3").Int("--top", 50).ShouldBe(-3);
        Should.Throw<NightlyUsageException>(() => Parse("--top", "1,000").Int("--top", 50)).Message.ShouldBe("--top expects an integer, not '1,000'");
        Should.Throw<NightlyUsageException>(() => Parse("--top", "ten").Int("--top", 50));
    }

    [Test]
    public void An_unknown_flag_is_a_usage_error()
    {
        Usage("--log-root", "x", "--suite", "a").ShouldBe("unknown flag --suite");
        Usage("-LogRoot", "x").ShouldBe("unexpected argument '-LogRoot'");
        Usage("--log-root=x").ShouldBe("write --log-root <value>, not --log-root=<value>");
        Usage("--bogus=1").ShouldBe("unknown flag --bogus");
        Usage("--WHAT-IF").ShouldBe("unknown flag --WHAT-IF");
    }

    [Test]
    public void A_repeated_flag_is_a_usage_error()
    {
        Usage("--suites", "a", "--suites", "b").ShouldBe("repeated flag --suites");
        Usage("--what-if", "--what-if").ShouldBe("repeated flag --what-if");
    }

    [Test]
    public void A_valued_flag_without_its_value_is_a_usage_error()
    {
        Usage("--log-root").ShouldBe("missing value for --log-root");
        Usage("--log-root", "--what-if").ShouldBe("missing value for --log-root");
        Usage("--ref", "--suites", "a").ShouldBe("missing value for --ref");
        // A value that only looks like a flag is a value.
        Parse("--ref", "--not-a-flag").Value("--ref").ShouldBe("--not-a-flag");
    }

    [Test]
    public void A_positional_argument_is_a_usage_error()
    {
        Usage("extra").ShouldBe("unexpected argument 'extra'");
        Usage("--what-if", "extra").ShouldBe("unexpected argument 'extra'");
    }

    [Test]
    public void Declarations_are_kebab_and_disjoint_and_only_declared_flags_are_read()
    {
        Should.Throw<ArgumentException>(() => NightlyArgs.Parse([], ["--LogRoot"], []));
        Should.Throw<ArgumentException>(() => NightlyArgs.Parse([], ["log-root"], []));
        Should.Throw<ArgumentException>(() => NightlyArgs.Parse([], ["--a"], ["--a"]));
        Should.Throw<ArgumentException>(() => NightlyArgs.Parse([], ["--a", "--a"], []));
        Should.Throw<ArgumentException>(() => Parse().Value("--other"));
        Should.Throw<ArgumentException>(() => Parse().Switch("--other"));
    }
}
