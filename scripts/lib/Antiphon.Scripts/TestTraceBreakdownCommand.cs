using System;
using Antiphon.Nightly;

namespace Antiphon.Scripts
{
    internal static class TestTraceBreakdownCommand
    {
        internal const string Name = "test-trace-summary";
        internal const string Usage = TestTraceBreakdown.Usage;

        internal static int Run(string[] args)
        {
            if (args.Length != 2)
                throw new NightlyUsageException(TestTraceBreakdown.Usage);
            return TestTraceBreakdown.Run(args[1], Console.Out, Console.Error);
        }
    }
}
