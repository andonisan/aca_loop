using Orchestrator.Portal.Presentation;
using Shouldly;

namespace Orchestrator.Portal.UnitTests;

/// <summary>
/// Formatting is where a display judgement can silently become a correctness problem, so the two that
/// matter operationally are pinned here: timestamps are unambiguous across timezones, and "no data" never
/// renders as a number.
/// </summary>
public class Format_Should_Constraint
{
    [Fact]
    public void Absolute_Should_RenderUtcWithAnExplicitSuffix()
    {
        var at = new DateTimeOffset(2026, 7, 25, 14, 30, 5, TimeSpan.FromHours(2));

        // Normalized to UTC and marked as such: operators in different timezones compare each other's
        // screenshots, and a locale-rendered timestamp is one two people can disagree about.
        Format.Absolute(at).ShouldBe("2026-07-25 12:30:05Z");
    }

    [Fact]
    public void Absolute_Should_RenderAnEmDashForNoValue()
    {
        Format.Absolute(null).ShouldBe("—");
    }

    [Theory]
    [InlineData(0.4, "400 ms")]
    [InlineData(2.5, "2.5 s")]
    [InlineData(90, "1m 30s")]
    [InlineData(7325, "2h 02m")]
    public void Duration_Should_StepTheUnitUpOnlyWhenTheSmallerOneStopsInforming(
        double seconds,
        string expected
    )
    {
        Format.Duration(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }

    [Fact]
    public void Duration_Should_NotRenderAMissingDurationAsZero()
    {
        // A run with no start time has no duration; "0 ms" would read as an instant success.
        Format.Duration(null).ShouldBe("—");
    }

    [Theory]
    [InlineData(2, "just now")]
    [InlineData(30, "30s ago")]
    [InlineData(600, "10m ago")]
    [InlineData(7200, "2h ago")]
    public void Age_Should_BeCoarseEnoughToScan(double seconds, string expected)
    {
        Format.Age(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }
}
