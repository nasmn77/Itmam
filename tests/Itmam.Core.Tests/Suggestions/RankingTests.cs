using Itmam.Core.Suggestions;

namespace Itmam.Core.Tests.Suggestions;

public class RankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly SuggestionOptions Options = new() { UserWeight = 2, RecencyHalfLifeDays = 10 };

    [Fact]
    public void Base_score_is_log_of_frequency_plus_one() =>
        Ranking.BaseScore(Math.E - 1).ShouldBe(1, tolerance: 1e-12);

    [Fact]
    public void Score_without_user_use_is_base_score() =>
        Ranking.Score(3.5, userCount: 0, Now, Now, Options).ShouldBe(3.5);

    [Fact]
    public void Score_follows_the_formula()
    {
        var expected = 3.5 + 2 * Math.Log(5) * 0.5;
        Ranking.Score(3.5, userCount: 4, Now.AddDays(-10), Now, Options).ShouldBe(expected, tolerance: 1e-12);
    }

    [Fact]
    public void Recency_halves_every_half_life()
    {
        Ranking.RecencyDecay(Now, Now, 10).ShouldBe(1);
        Ranking.RecencyDecay(Now.AddDays(-10), Now, 10).ShouldBe(0.5, tolerance: 1e-12);
        Ranking.RecencyDecay(Now.AddDays(-20), Now, 10).ShouldBe(0.25, tolerance: 1e-12);
    }

    [Fact]
    public void Future_timestamps_do_not_increase_the_boost() =>
        Ranking.RecencyDecay(Now.AddDays(5), Now, 10).ShouldBe(1);

    [Fact]
    public void More_uses_rank_higher() =>
        Ranking.UserScore(10, Now, Now, Options).ShouldBeGreaterThan(Ranking.UserScore(2, Now, Now, Options));
}
