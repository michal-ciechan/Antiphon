using Antiphon.TestSupport;
using TUnit.Core;

namespace Antiphon.NativeLinuxPolicy.Probe;

[NativeLinuxOnly("direct class runs on Linux")]
public sealed class DirectClassProbe
{
    private readonly int _field = ProbeLog.Touch("direct-class.field");
    public DirectClassProbe() => ProbeLog.Touch("direct-class.ctor");

    [Before(Test)]
    public void Before() => ProbeLog.Touch("direct-class.before");

    [Test]
    public void Runs()
    {
        _ = _field;
        ProbeLog.Touch("direct-class.body");
    }
}

public sealed class DirectMethodProbe
{
    private readonly int _field = ProbeLog.Touch("direct-method.field");
    public DirectMethodProbe() => ProbeLog.Touch("direct-method.ctor");

    [Before(Test)]
    public void Before() => ProbeLog.Touch("direct-method.before");

    [Test]
    [NativeLinuxOnly("direct method runs on Linux")]
    public void Marked()
    {
        _ = _field;
        ProbeLog.Touch("direct-method.body");
    }
}

public abstract class InheritedBaseProbe
{
    [Test]
    [NativeLinuxOnly("inherited method runs on Linux")]
    public void Inherited() => ProbeLog.Touch("inherited.body");
}

[InheritsTests]
public sealed class InheritedDerivedProbe : InheritedBaseProbe
{
    private readonly int _field = ProbeLog.Touch("inherited.field");
    public InheritedDerivedProbe() => ProbeLog.Touch("inherited.ctor");

    [Before(Test)]
    public void Before()
    {
        _ = _field;
        ProbeLog.Touch("inherited.before");
    }
}

public abstract class UnmarkedBaseProbe
{
    [Test]
    public void FromBase() => ProbeLog.Touch("marked-derived.body");
}

[InheritsTests]
[NativeLinuxOnly("marked derived class runs on Linux")]
public sealed class MarkedDerivedProbe : UnmarkedBaseProbe
{
    private readonly int _field = ProbeLog.Touch("marked-derived.field");
    public MarkedDerivedProbe() => ProbeLog.Touch("marked-derived.ctor");

    [Before(Test)]
    public void Before()
    {
        _ = _field;
        ProbeLog.Touch("marked-derived.before");
    }
}

public sealed class OuterProbe
{
    [NativeLinuxOnly("nested class runs on Linux")]
    public sealed class InnerProbe
    {
        private readonly int _field = ProbeLog.Touch("nested.field");
        public InnerProbe() => ProbeLog.Touch("nested.ctor");

        [Before(Test)]
        public void Before() => ProbeLog.Touch("nested.before");

        [Test]
        public void Nested()
        {
            _ = _field;
            ProbeLog.Touch("nested.body");
        }
    }
}

[GenerateGenericTest(typeof(int))]
[NativeLinuxOnly("generic class runs on Linux")]
public sealed class GenericProbe<T>
{
    private readonly int _field = ProbeLog.Touch("generic.field");
    public GenericProbe() => ProbeLog.Touch("generic.ctor");

    [Before(Test)]
    public void Before() => ProbeLog.Touch("generic.before");

    [Test]
    public void GenericCase()
    {
        _ = _field;
        ProbeLog.Touch("generic.body");
    }
}

public sealed class OverloadProbe
{
    private readonly int _field = ProbeLog.Touch("overload.field");
    public OverloadProbe() => ProbeLog.Touch("overload.ctor");

    [Before(Test)]
    public void Before() => ProbeLog.Touch("overload.before");

    [Test]
    [Arguments(1)]
    [NativeLinuxOnly("overload int runs on Linux")]
    public void Case(int value)
    {
        _ = value;
        _ = _field;
        ProbeLog.Touch("overload-int.body");
    }

    [Test]
    [Arguments("a")]
    [NativeLinuxOnly("overload string runs on Linux")]
    public void Case(string value)
    {
        _ = value;
        _ = _field;
        ProbeLog.Touch("overload-string.body");
    }
}

public sealed class PortableSiblingProbe
{
    [Test]
    public void Portable() => ProbeLog.Portable("portable.body");
}
