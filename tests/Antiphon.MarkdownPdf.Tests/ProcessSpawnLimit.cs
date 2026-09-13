using TUnit.Core.Interfaces;

namespace Antiphon.MarkdownPdf.Tests;

public sealed class ProcessSpawnLimit : IParallelLimit
{
    public int Limit => 1;
}
