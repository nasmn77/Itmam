namespace Itmam.Core.Dictionary;

/// <summary>
/// Frequencies are stored as integers in "per billion" units (per-million × 1000) so rare words keep
/// three decimals of precision and still encode as small varints.
/// </summary>
public static class FrequencyScale
{
    private const double UnitsPerMillion = 1000.0;

    public static uint FromPerMillion(double perMillion) =>
        (uint)Math.Clamp(Math.Round(perMillion * UnitsPerMillion), 1, uint.MaxValue);

    public static double ToPerMillion(uint units) => units / UnitsPerMillion;
}
