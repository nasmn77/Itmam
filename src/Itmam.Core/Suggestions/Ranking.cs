namespace Itmam.Core.Suggestions;

/// <summary>
/// <c>score = log(baseFrequency + 1) + w_user · log(userCount + 1) · recencyDecay</c>, where
/// baseFrequency is per million words and recencyDecay halves every <c>halfLifeDays</c> since last use.
/// </summary>
public static class Ranking
{
    public static double BaseScore(double perMillion) => Math.Log(perMillion + 1);

    public static double RecencyDecay(DateTimeOffset lastUsed, DateTimeOffset now, double halfLifeDays)
    {
        var ageDays = Math.Max(0, (now - lastUsed).TotalDays);
        return Math.Pow(0.5, ageDays / halfLifeDays);
    }

    public static double UserScore(int userCount, DateTimeOffset lastUsed, DateTimeOffset now, SuggestionOptions options) =>
        userCount <= 0
            ? 0
            : options.UserWeight * Math.Log(userCount + 1) * RecencyDecay(lastUsed, now, options.RecencyHalfLifeDays);

    public static double Score(double baseScore, int userCount, DateTimeOffset lastUsed, DateTimeOffset now, SuggestionOptions options) =>
        baseScore + UserScore(userCount, lastUsed, now, options);
}
