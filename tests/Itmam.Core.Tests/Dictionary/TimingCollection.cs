namespace Itmam.Core.Tests.Dictionary;

/// <summary>
/// The tests that measure load and lookup times: they run alone, after the parallel tests, so heavy tests
/// running at the same time (golden snapshots, full scans) do not slow down what they measure.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
