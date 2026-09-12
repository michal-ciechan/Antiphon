using TUnit.Core;
namespace C487.Probe;
public abstract class BaseCases { [Test] public void Inherited() {} }
[InheritsTests] public class Ordinary : BaseCases {
    [Test] public void Plain() {}
    [Test, Arguments(1), Arguments(2)] public void Rows(int n) { if (n < 1) throw new Exception(); }
    public static IEnumerable<int> Values() => [3, 4];
    [Test, MethodDataSource(nameof(Values))] public void Data(int n) { if (n < 3) throw new Exception(); }
}
[Category("Slow")] public partial class SlowCases { [Test] public void SlowOne() {} }
public partial class SlowCases { [Test] public void SlowTwo() {} }
[Category("OptIn"), Explicit] public class Manual { [Test] public void ManualOne() {} }
public class Hooks {
    [After(TestDiscovery)] public static void Export(TestDiscoveryContext context) {
        foreach (var p in context.GetType().GetProperties())
            Console.WriteLine($"PROP {p.Name}: {p.PropertyType}");
    }
}
